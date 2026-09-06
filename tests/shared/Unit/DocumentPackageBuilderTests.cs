using System.Net;
using System.Net.Http.Headers;
using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundKitShared.Tests.Unit;

public sealed class DocumentPackageBuilderTests : IDisposable
{
    private readonly string _tempRoot;

    public DocumentPackageBuilderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
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
    public async Task Should_Build_Manifest_And_Chunks_For_Local_Directory()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "intro.md"),
            "# Introduction\n\nThis is the intro.\n\n## Details\n\nMore details here."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        result.Source.Kind.ShouldBe(SourceKind.LocalDirectory);
        result.Manifest.DisplayName.ShouldBe("docs");
        result.Manifest.DocumentCount.ShouldBeGreaterThanOrEqualTo(1);
        result.Manifest.ChunkCount.ShouldBeGreaterThanOrEqualTo(2);
        result.Chunks.ShouldAllBe(chunk => !string.IsNullOrWhiteSpace(chunk.ChunkId));
    }

    [Fact]
    public async Task Should_Use_Local_Directory_Root_When_Docs_Folder_Is_Missing()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "readme.md"), "# Readme\n\nContent.");

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(_tempRoot);

        result.Manifest.DocumentCount.ShouldBeGreaterThanOrEqualTo(1);
        result.Manifest.ChunkCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Should_Reject_Empty_Local_Directory()
    {
        var emptyPath = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(emptyPath);

        var builder = CreateBuilder();

        await Should.ThrowAsync<InvalidOperationException>(() => builder.BuildAsync(emptyPath));
    }

    [Fact]
    public async Task Should_Skip_Excluded_Paths_In_Local_Directory()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(Path.Combine(docsPath, "node_modules"));
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "node_modules", "hidden.md"),
            "# Hidden\n\nShould be skipped."
        );
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "visible.md"),
            "# Visible\n\nShould be included."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        result.Documents.ShouldNotContain(document => document.Path.Contains("node_modules"));
        result.Documents.ShouldContain(document => document.Path.Contains("visible.md"));
    }

    [Fact]
    public async Task Should_Deduplicate_Identical_Sections()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "dup.md"),
            "# Title\n\nSame paragraph.\n\n# Title\n\nSame paragraph."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        result.Warnings.ShouldContain(warning =>
            warning.Code == BuildWarningCode.DuplicateChunkSkipped
        );
    }

    [Fact]
    public async Task Should_Fetch_Linked_Documents_From_Llms_Index()
    {
        var factory = new TestHttpClientFactory(
            new Dictionary<string, HttpResponseMessage>
            {
                ["https://docs.example.com/llms-full.txt"] = NotFound(),
                ["https://docs.example.com/llms.txt"] = TextResponse(
                    "# Docs\n\n[Guide](/guide.md)"
                ),
                ["https://docs.example.com/guide.md"] = TextResponse(
                    "# Guide\n\nInstall and configure."
                ),
            }
        );

        var result = await CreateBuilder(factory).BuildAsync("https://docs.example.com");

        result.Source.Kind.ShouldBe(SourceKind.LlmsText);
        result.Manifest.DocumentCount.ShouldBe(2);
        result.Documents.ShouldContain(document => document.Path == "guide.md");
        result.Documents.ShouldContain(document => document.Title == "Guide");
    }

    [Fact]
    public async Task Should_Extract_Title_Headings_And_Code_From_Raw_Html_Page()
    {
        var factory = new TestHttpClientFactory(
            new Dictionary<string, HttpResponseMessage>
            {
                ["https://example.com/article"] = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<html><head><title>Article</title></head><body><h1>Intro</h1><p>Hello</p><pre><code>dotnet run</code></pre></body></html>",
                        MediaTypeHeaderValue.Parse("text/html")
                    ),
                },
            }
        );

        var result = await CreateBuilder(factory).BuildAsync("https://example.com/article");

        result.Source.Kind.ShouldBe(SourceKind.RawPage);
        result.Documents.Single().Title.ShouldBe("Article");
        result.Documents.Single().Content.ShouldContain("# Intro");
        result.Documents.Single().Content.ShouldContain("```text");
        result.Documents.Single().Content.ShouldContain("dotnet run");
    }

    private DocumentPackageBuilder CreateBuilder(IHttpClientFactory? httpClientFactory = null)
    {
        if (httpClientFactory is null)
        {
            httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        }

        return new DocumentPackageBuilder(
            new SourceDetector(),
            httpClientFactory,
            NullLogger<DocumentPackageBuilder>.Instance
        );
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly IReadOnlyDictionary<string, HttpResponseMessage> _responses;

        public TestHttpClientFactory(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        {
            _responses = responses;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new TestMessageHandler(_responses));
        }
    }

    private sealed class TestMessageHandler(
        IReadOnlyDictionary<string, HttpResponseMessage> responses
    ) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (responses.TryGetValue(request.RequestUri!.ToString(), out var response))
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var clone = new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(content),
                };
                if (response.Content.Headers.ContentType is not null)
                {
                    clone.Content.Headers.ContentType = response.Content.Headers.ContentType;
                }

                return clone;
            }

            return NotFound();
        }
    }

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    private static HttpResponseMessage TextResponse(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };
}
