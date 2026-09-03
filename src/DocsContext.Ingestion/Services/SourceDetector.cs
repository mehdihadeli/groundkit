using DocsContext.Core.Contracts;

namespace DocsContext.Ingestion.Services;

public interface ISourceDetector
{
    DocumentationSource Detect(string input);
}

public sealed class SourceDetector : ISourceDetector
{
    private static readonly string[] GitHosts =
    [
        "github.com",
        "gitlab.com",
        "bitbucket.org",
        "codeberg.org",
    ];
    private static readonly string[] RawDocumentExtensions =
    [
        ".md",
        ".mdx",
        ".markdown",
        ".txt",
        ".html",
        ".htm",
    ];

    public DocumentationSource Detect(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (Directory.Exists(input))
        {
            var fullPath = Path.GetFullPath(input);
            var displayName = Path.GetFileName(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            );
            return new DocumentationSource(
                SourceKind.LocalDirectory,
                Canonicalize(displayName),
                displayName,
                fullPath
            );
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Unsupported source input: '{input}'.");
        }

        var display = GetDisplayName(uri);
        var canonicalId = Canonicalize(display);

        if (IsGitRepository(uri, input))
        {
            return new DocumentationSource(SourceKind.GitRepository, canonicalId, display, input);
        }

        if (IsLlmsText(uri))
        {
            return new DocumentationSource(SourceKind.LlmsText, canonicalId, display, input);
        }

        return new DocumentationSource(SourceKind.RawPage, canonicalId, display, input);
    }

    private static bool IsGitRepository(Uri uri, string input)
    {
        if (input.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!GitHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (RawDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsLlmsText(uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2;
    }

    private static bool IsLlmsText(Uri uri)
    {
        return uri.AbsolutePath.EndsWith("/llms.txt", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith("/llms-full.txt", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath == "/";
    }

    private static string GetDisplayName(Uri uri)
    {
        var segment = uri
            .AbsolutePath.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return uri.Host;
        }

        var extension = Path.GetExtension(segment);
        return string.IsNullOrWhiteSpace(extension)
            ? segment
            : Path.GetFileNameWithoutExtension(segment);
    }

    private static string Canonicalize(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join(
            '-',
            new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
