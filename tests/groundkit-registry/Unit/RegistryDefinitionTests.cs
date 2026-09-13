using GroundKit.Registry;

namespace GroundKit.Registry.Tests.Unit;

public sealed class RegistryDefinitionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "groundkit-registry-tests",
        Guid.NewGuid().ToString("n")
    );

    public RegistryDefinitionTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Should_Load_Definitions_From_Package_Root()
    {
        var path = WriteDefinition(
            "react.yaml",
            """
            name: react
            description: UI library
            source:
              type: git
              url: https://github.com/facebook/react.git
              docs_path: docs
            """
        );

        var definition = RegistryDefinitionLoader.LoadFile(path);

        definition.Name.ShouldBe("react");
        definition.Registry.ShouldBe(RegistryDefinitionLoader.DefaultRegistry);
        definition.DocsPath.ShouldBe("docs");
        definition.ResolveVersions().ShouldBe([("latest", (string?)null)]);
    }

    [Fact]
    public void Should_Resolve_Explicit_Version_Tags()
    {
        var path = WriteDefinition(
            "react.yaml",
            """
            name: react
            source:
              type: git
              url: https://github.com/facebook/react.git
            versions:
              - version: 19.0.0
                tag: v19.0.0
              - version: 19.1.0
            """
        );

        var definition = RegistryDefinitionLoader.LoadFile(path);
        var versions = definition.ResolveVersions();

        versions.ShouldBe([("19.0.0", (string?)"v19.0.0"), ("19.1.0", (string?)"v19.1.0")]);
    }

    [Fact]
    public void Should_Reject_Name_That_Does_Not_Match_Filename()
    {
        var path = WriteDefinition(
            "react.yaml",
            """
            name: vue
            source:
              type: git
              url: https://github.com/vuejs/core.git
            """
        );

        Should
            .Throw<InvalidDataException>(() => RegistryDefinitionLoader.LoadFile(path))
            .Message.ShouldContain("does not match filename");
    }

    private string WriteDefinition(string fileName, string contents)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
