using GroundKit.Core.Contracts;
using YamlDotNet.RepresentationModel;

namespace GroundKit.Registry;

public sealed record RegistryDefinition(
    string Name,
    string Registry,
    string Description,
    string SourceType,
    string Source,
    string? DocsPath,
    string? Ref,
    string TagPattern,
    IReadOnlyList<RegistryVersion> Versions
)
{
    public string PackageId => Name;

    public SourceKind Kind =>
        SourceType.ToLowerInvariant() switch
        {
            "git" => SourceKind.GitRepository,
            "local" or "directory" => SourceKind.LocalDirectory,
            "llms.txt" or "llms" => SourceKind.LlmsText,
            "raw" or "page" => SourceKind.RawPage,
            _ => SourceKind.Unknown,
        };

    public bool IsVersioned => Versions.Count > 0;

    public IReadOnlyList<(string Version, string? Tag)> ResolveVersions()
    {
        if (!IsVersioned)
        {
            string? tag = Ref;
            return new List<(string Version, string? Tag)> { ("latest", tag) };
        }

        return Versions
            .Select<RegistryVersion, (string Version, string? Tag)>(version =>
            {
                string? tag = version.Tag ?? TagPattern.Replace("{version}", version.Version);
                return (Version: version.Version, Tag: tag);
            })
            .ToArray();
    }
}

public sealed record RegistryVersion(string Version, string? Tag);

public static class RegistryDefinitionLoader
{
    public static IReadOnlyList<RegistryDefinition> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Registry directory was not found: {directory}");
        }

        return Directory
            .EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadFile)
            .ToArray();
    }

    public static RegistryDefinition LoadFile(string path)
    {
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root =
            yaml.Documents.SingleOrDefault()?.RootNode as YamlMappingNode
            ?? throw Invalid(path, "root must be a mapping");

        var name = Required(root, "name", path);
        var registry =
            Path.GetFileName(
                Path.GetDirectoryName(path)
                    ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            ) ?? throw Invalid(path, "registry directory is missing");
        var description = Optional(root, "description") ?? string.Empty;
        var sourceNode = root.Children.TryGetValue(new YamlScalarNode("source"), out var node)
            ? node as YamlMappingNode
            : null;
        if (
            sourceNode is null
            && root.Children.TryGetValue(new YamlScalarNode("versions"), out var versionsNode)
            && versionsNode is YamlSequenceNode versionSequence
            && versionSequence.Children.FirstOrDefault() is YamlMappingNode firstVersion
            && firstVersion.Children.TryGetValue(new YamlScalarNode("source"), out var nestedSource)
        )
        {
            sourceNode = nestedSource as YamlMappingNode;
        }
        if (sourceNode is null)
        {
            throw Invalid(path, "source mapping or version source mapping is required");
        }

        var sourceType = Required(sourceNode, "type", path, "source");
        var source =
            Optional(sourceNode, "url")
            ?? Optional(sourceNode, "path")
            ?? throw Invalid(path, "source.url or source.path is required");
        var docsPath = Optional(sourceNode, "docs_path");
        var sourceRef = Optional(sourceNode, "ref");
        var tagPattern = Optional(root, "tag_pattern") ?? "v{version}";
        var versions = ReadVersions(root, path);
        var expectedName = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(path, $"name '{name}' does not match filename '{expectedName}'");
        }

        var definition = new RegistryDefinition(
            name,
            registry,
            description,
            sourceType,
            source,
            docsPath,
            sourceRef,
            tagPattern,
            versions
        );
        Validate(definition, path);
        return definition;
    }

    private static IReadOnlyList<RegistryVersion> ReadVersions(YamlMappingNode root, string path)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("versions"), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence || sequence.Children.Count == 0)
        {
            throw Invalid(path, "versions must be a non-empty sequence");
        }

        return sequence
            .Children.Select(item =>
            {
                if (item is not YamlMappingNode mapping)
                {
                    throw Invalid(path, "each version must be a mapping");
                }

                var version = Required(mapping, "version", path, "versions");
                return new RegistryVersion(version, Optional(mapping, "tag"));
            })
            .ToArray();
    }

    public static void Validate(IReadOnlyList<RegistryDefinition> definitions)
    {
        var duplicates = definitions
            .GroupBy(definition => definition.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate registry definitions: {string.Join(", ", duplicates)}"
            );
        }
    }

    private static void Validate(RegistryDefinition definition, string path)
    {
        if (definition.Kind == SourceKind.Unknown)
        {
            throw Invalid(path, $"unsupported source type '{definition.SourceType}'");
        }

        if (definition.Registry.StartsWith('@') && definition.Registry.Length == 1)
        {
            throw Invalid(path, "registry directory is invalid");
        }
    }

    private static string Required(
        YamlMappingNode mapping,
        string key,
        string path,
        string? prefix = null
    ) =>
        Optional(mapping, key)
        ?? throw Invalid(
            path,
            $"{(prefix is null ? string.Empty : prefix + ".")}{key} is required"
        );

    private static string? Optional(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            ? (node as YamlScalarNode)?.Value?.Trim()
            : null;
    }

    private static InvalidDataException Invalid(string path, string message) =>
        new($"Invalid registry definition '{path}': {message}.");
}
