using DocsContext.Core.Contracts;
using DocsContext.Ingestion.Services;

namespace DocsContext.Tests.Unit;

public sealed class SourceDetectorTests
{
    private readonly SourceDetector _detector = new();

    [Fact]
    public void Detect_LocalDirectory_ReturnsLocalDirectoryKind()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);

        try
        {
            var source = _detector.Detect(path);

            Assert.Equal(SourceKind.LocalDirectory, source.Kind);
            Assert.Equal(Path.GetFileName(path), source.DisplayName);
            Assert.Equal(Path.GetFullPath(path), source.Location);
            Assert.False(string.IsNullOrWhiteSpace(source.CanonicalId));
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo", "repo")]
    [InlineData("https://gitlab.com/team/project.git", "project")]
    [InlineData("https://bitbucket.org/user/repo", "repo")]
    public void Detect_GitRepositoryUrl_ReturnsGitRepositoryKind(string url, string expectedName)
    {
        var source = _detector.Detect(url);

        Assert.Equal(SourceKind.GitRepository, source.Kind);
        Assert.Equal(expectedName, source.DisplayName);
        Assert.Equal(url, source.Location);
    }

    [Theory]
    [InlineData("https://example.com/llms.txt")]
    [InlineData("https://example.com/llms-full.txt")]
    [InlineData("https://example.com/")]
    public void Detect_LlmsTextUrl_ReturnsLlmsTextKind(string url)
    {
        var source = _detector.Detect(url);

        Assert.Equal(SourceKind.LlmsText, source.Kind);
    }

    [Theory]
    [InlineData("https://example.com/page.md")]
    [InlineData("https://example.com/page.html")]
    [InlineData("https://example.com/raw.txt")]
    public void Detect_RawDocumentUrl_ReturnsRawPageKind(string url)
    {
        var source = _detector.Detect(url);

        Assert.Equal(SourceKind.RawPage, source.Kind);
    }

    [Fact]
    public void Detect_EmptyInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _detector.Detect("  "));
    }

    [Fact]
    public void Detect_UnsupportedInput_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _detector.Detect("not-a-path-or-url"));
    }
}
