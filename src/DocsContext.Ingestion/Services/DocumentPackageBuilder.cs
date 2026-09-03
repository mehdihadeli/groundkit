using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Observability.Telemetry;
using Microsoft.Extensions.Logging;

namespace DocsContext.Ingestion.Services;

public sealed class DocumentPackageBuilder(
    ISourceDetector sourceDetector,
    IHttpClientFactory httpClientFactory,
    ILogger<DocumentPackageBuilder> logger
) : IDocumentPackageBuilder
{
    private static readonly string[] DefaultDocsFolders = ["docs", "documentation", "doc"];
    private static readonly string[] SupportedExtensions = [".md", ".mdx", ".markdown", ".txt"];
    private static readonly Regex HeadingPattern = new("^(#{1,6})\\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex H1Pattern =
        new("^#\\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkPattern =
        new("\\[[^\\]]+\\]\\(([^)]+)\\)", RegexOptions.Compiled);
    private static readonly Regex BareUrlPattern =
        new("https?://\\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlTitlePattern =
        new(
            "<title[^>]*>(.*?)</title>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
    private static readonly Regex HtmlHeadingPattern =
        new(
            "<h([1-6])[^>]*>(.*?)</h\\1>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
    private static readonly Regex HtmlCodeBlockPattern =
        new(
            "<pre[^>]*><code[^>]*>(.*?)</code></pre>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
    private static readonly Regex HtmlParagraphPattern =
        new(
            "<(p|div|section|article|main|blockquote)[^>]*>(.*?)</\\1>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
    private static readonly Regex HtmlListItemPattern =
        new(
            "<li[^>]*>(.*?)</li>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
    private static readonly Regex HtmlBreakPattern =
        new("<br\\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlTagPattern =
        new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CollapseNewlinesPattern = new("\\n{3,}", RegexOptions.Compiled);

    public async Task<BuildResult> BuildAsync(
        string input,
        string? docsPath = null,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.PackageBuild
        );
        activity?.SetTag("docscontext.source.input", input);

        var warnings = new List<BuildWarning>();
        var source = sourceDetector.Detect(input) with { DocsPath = docsPath };
        activity?.SetTag("docscontext.source.kind", source.Kind.ToString());

        string? cleanupPath = null;

        try
        {
            var payload = source.Kind switch
            {
                SourceKind.LocalDirectory => await LoadFromDirectoryAsync(
                    source.Location,
                    docsPath,
                    warnings,
                    cancellationToken
                ),
                SourceKind.GitRepository => await LoadFromGitRepositoryAsync(
                    source,
                    docsPath,
                    warnings,
                    cancellationToken
                ),
                SourceKind.LlmsText => await LoadFromLlmsAsync(source, warnings, cancellationToken),
                SourceKind.RawPage => await LoadFromRawPageAsync(source, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Source kind '{source.Kind}' is not implemented yet."
                ),
            };

            cleanupPath = payload.CleanupPath;

            var documents = new List<DocumentRecord>();
            var chunks = new List<ChunkRecord>();

            foreach (var sourceDocument in payload.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var content = sourceDocument.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    warnings.Add(
                        new BuildWarning(
                            BuildWarningCode.EmptyDocumentSkipped,
                            "Skipped empty document.",
                            sourceDocument.Path
                        )
                    );
                    continue;
                }

                var documentId = ComputeHash(sourceDocument.Path);
                documents.Add(
                    new DocumentRecord(
                        documentId,
                        sourceDocument.Title,
                        sourceDocument.Path,
                        content
                    )
                );

                chunks.AddRange(
                    CreateChunks(
                        documentId,
                        sourceDocument.Title,
                        sourceDocument.Path,
                        content,
                        warnings
                    )
                );
            }

            if (chunks.Count < 3)
            {
                warnings.Add(
                    new BuildWarning(
                        BuildWarningCode.LowSectionCount,
                        "Very few sections were indexed. The docs may live in a different repository or subfolder.",
                        source.Location
                    )
                );
            }

            DocsContextTelemetry.Metrics.ChunksCreated.Add(chunks.Count);

            var builtAt = DateTimeOffset.UtcNow;
            var manifest = new PackageManifest(
                source.CanonicalId,
                source.DisplayName,
                source.Version,
                source.Kind,
                source.Location,
                source.CanonicalId,
                payload.Fingerprint,
                builtAt,
                "0.1.0-dev",
                documents.Count,
                chunks.Count,
                warnings.Count
            );
            var persistedSource = source with
            {
                Fingerprint = payload.Fingerprint,
                LastBuiltAt = builtAt,
                LastCheckedAt = builtAt,
            };

            logger.LogInformation(
                "Built package {PackageId} from {SourceKind} with {DocumentCount} documents and {ChunkCount} chunks.",
                manifest.PackageId,
                manifest.SourceKind,
                manifest.DocumentCount,
                manifest.ChunkCount
            );

            return new BuildResult(persistedSource, manifest, warnings, documents, chunks);
        }
        catch
        {
            DocsContextTelemetry.Metrics.IngestionFailures.Add(1);
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(cleanupPath) && Directory.Exists(cleanupPath))
            {
                Directory.Delete(cleanupPath, recursive: true);
            }
        }
    }

    private async Task<SourcePayload> LoadFromGitRepositoryAsync(
        DocumentationSource source,
        string? docsPath,
        List<BuildWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        var clonePath = await CloneRepositoryAsync(source, cancellationToken);
        var payload = await LoadFromDirectoryAsync(
            clonePath,
            docsPath,
            warnings,
            cancellationToken
        );
        return payload with { CleanupPath = clonePath };
    }

    private static async Task<SourcePayload> LoadFromDirectoryAsync(
        string sourceRoot,
        string? docsPath,
        List<BuildWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        var docsRoot = ResolveDocsRoot(sourceRoot, docsPath, warnings);
        var files = EnumerateDocumentationFiles(docsRoot).ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"No supported documentation files were found under '{docsRoot}'."
            );
        }

        var documents = new List<SourceDocumentInput>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var relativePath = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
            var title = ExtractTitle(content, Path.GetFileNameWithoutExtension(file));
            documents.Add(new SourceDocumentInput(relativePath, title, content));
        }

        return new SourcePayload(documents, ComputeFingerprint(files), CleanupPath: null);
    }

    private async Task<SourcePayload> LoadFromLlmsAsync(
        DocumentationSource source,
        List<BuildWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        var llmsUri = await ResolveLlmsUriAsync(source.Location, cancellationToken);
        var llmsContent = await DownloadStringAsync(
            llmsUri,
            DocsContextTelemetry.Activities.LlmsFetch,
            cancellationToken
        );

        if (llmsUri.AbsolutePath.EndsWith("/llms-full.txt", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = NormalizeTextContent(llmsContent);
            var title = ExtractTitle(normalized, source.DisplayName);
            var document = new SourceDocumentInput("llms-full.txt", title, normalized);
            return new SourcePayload(
                [document],
                ComputeContentFingerprint([document]),
                CleanupPath: null
            );
        }

        var documents = new List<SourceDocumentInput>
        {
            new("llms.txt", $"{source.DisplayName} llms", NormalizeTextContent(llmsContent)),
        };

        foreach (var linkedUri in ParseLlmsLinks(llmsContent, llmsUri).Take(12))
        {
            try
            {
                documents.Add(
                    await LoadRemoteDocumentAsync(linkedUri, source.DisplayName, cancellationToken)
                );
            }
            catch (Exception exception)
            {
                warnings.Add(
                    new BuildWarning(
                        BuildWarningCode.SourceFetchFallback,
                        $"Failed to fetch linked llms document '{linkedUri}'.",
                        linkedUri.ToString()
                    )
                );
                logger.LogWarning(
                    exception,
                    "Failed to fetch linked llms document {LinkedUri}.",
                    linkedUri
                );
            }
        }

        return new SourcePayload(
            documents,
            ComputeContentFingerprint(documents),
            CleanupPath: null
        );
    }

    private async Task<SourcePayload> LoadFromRawPageAsync(
        DocumentationSource source,
        CancellationToken cancellationToken
    )
    {
        var document = await LoadRemoteDocumentAsync(
            new Uri(source.Location),
            source.DisplayName,
            cancellationToken
        );

        return new SourcePayload(
            [document],
            ComputeContentFingerprint([document]),
            CleanupPath: null
        );
    }

    private static string ResolveDocsRoot(
        string sourceRoot,
        string? docsPath,
        List<BuildWarning> warnings
    )
    {
        if (!string.IsNullOrWhiteSpace(docsPath))
        {
            var explicitPath = Path.IsPathRooted(docsPath)
                ? docsPath
                : Path.Combine(sourceRoot, docsPath);

            if (!Directory.Exists(explicitPath))
            {
                throw new DirectoryNotFoundException($"Docs path '{explicitPath}' does not exist.");
            }

            return explicitPath;
        }

        foreach (
            var candidate in DefaultDocsFolders.Select(folder => Path.Combine(sourceRoot, folder))
        )
        {
            if (Directory.Exists(candidate))
            {
                warnings.Add(
                    new BuildWarning(
                        BuildWarningCode.DocsPathNotExplicit,
                        $"Auto-detected docs path '{candidate}'.",
                        candidate
                    )
                );
                return candidate;
            }
        }

        return sourceRoot;
    }

    private static IEnumerable<string> EnumerateDocumentationFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path =>
                SupportedExtensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            .Where(path => !IsExcludedPath(path));
    }

    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/.git/")
            || normalized.Contains("/node_modules/")
            || normalized.Contains("/bin/")
            || normalized.Contains("/obj/");
    }

    private static string ExtractTitle(string content, string fallback)
    {
        var match = H1Pattern.Match(content);
        return match.Success ? match.Groups[1].Value.Trim() : fallback;
    }

    private static IReadOnlyList<ChunkRecord> CreateChunks(
        string documentId,
        string documentTitle,
        string relativePath,
        string content,
        List<BuildWarning> warnings
    )
    {
        var sectionBuffers = new List<(string Title, string Content)>();
        var currentTitle = documentTitle;
        var current = new StringBuilder();

        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var headingMatch = HeadingPattern.Match(line);
            if (headingMatch.Success && current.Length > 0)
            {
                sectionBuffers.Add((currentTitle, current.ToString().Trim()));
                current.Clear();
                currentTitle = headingMatch.Groups[2].Value.Trim();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            sectionBuffers.Add((currentTitle, current.ToString().Trim()));
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var stagedChunks =
            new List<(
                string Title,
                string Content,
                string Hash,
                bool HasCode,
                int TokenEstimate
            )>();

        foreach (
            var (title, sectionContent) in sectionBuffers.Where(section =>
                !string.IsNullOrWhiteSpace(section.Content)
            )
        )
        {
            var sectionHash = ComputeHash(sectionContent);
            if (!unique.Add(sectionHash))
            {
                warnings.Add(
                    new BuildWarning(
                        BuildWarningCode.DuplicateChunkSkipped,
                        $"Skipped duplicate section '{title}'.",
                        relativePath
                    )
                );
                continue;
            }

            stagedChunks.Add(
                (
                    title,
                    sectionContent,
                    sectionHash,
                    sectionContent.Contains("```", StringComparison.Ordinal),
                    EstimateTokens(sectionContent)
                )
            );
        }

        var chunks = new List<ChunkRecord>(stagedChunks.Count);
        for (var index = 0; index < stagedChunks.Count; index++)
        {
            var staged = stagedChunks[index];
            var chunkId = ComputeHash($"{documentId}:{index}:{staged.Title}:{staged.Hash}");
            chunks.Add(
                new ChunkRecord(
                    chunkId,
                    documentId,
                    documentTitle,
                    staged.Title,
                    relativePath,
                    staged.Content,
                    staged.TokenEstimate,
                    staged.HasCode,
                    staged.Hash,
                    index
                )
            );
        }

        for (var index = 0; index < chunks.Count; index++)
        {
            var previousChunkId = index > 0 ? chunks[index - 1].ChunkId : null;
            var nextChunkId = index < chunks.Count - 1 ? chunks[index + 1].ChunkId : null;
            chunks[index] = chunks[index] with
            {
                PreviousChunkId = previousChunkId,
                NextChunkId = nextChunkId,
            };
        }

        return chunks;
    }

    private static int EstimateTokens(string content)
    {
        return Math.Max(1, (int)Math.Ceiling(content.Length / 4d));
    }

    private static string ComputeFingerprint(IEnumerable<string> files)
    {
        var payload = string.Join(
            "\n",
            files
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                })
        );

        return ComputeHash(payload);
    }

    private static string ComputeContentFingerprint(IEnumerable<SourceDocumentInput> documents)
    {
        var payload = string.Join(
            "\n",
            documents
                .OrderBy(document => document.Path, StringComparer.OrdinalIgnoreCase)
                .Select(document => $"{document.Path}:{ComputeHash(document.Content)}")
        );

        return ComputeHash(payload);
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<string> CloneRepositoryAsync(
        DocumentationSource source,
        CancellationToken cancellationToken
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.GitFetch
        );
        activity?.SetTag("docscontext.source.location", source.Location);

        var tempRoot = Path.Combine(
            Environment.CurrentDirectory,
            ".tmp",
            "docs-context",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(Path.GetDirectoryName(tempRoot)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone --depth 1 \"{source.Location}\" \"{tempRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git clone process.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Git clone failed: {error}");
        }

        return tempRoot;
    }

    private async Task<Uri> ResolveLlmsUriAsync(string input, CancellationToken cancellationToken)
    {
        var inputUri = new Uri(input, UriKind.Absolute);
        if (
            inputUri.AbsolutePath.EndsWith("/llms.txt", StringComparison.OrdinalIgnoreCase)
            || inputUri.AbsolutePath.EndsWith("/llms-full.txt", StringComparison.OrdinalIgnoreCase)
        )
        {
            return inputUri;
        }

        var rootUri = new Uri($"{inputUri.Scheme}://{inputUri.Authority}/", UriKind.Absolute);
        foreach (
            var candidate in new[]
            {
                new Uri(rootUri, "llms-full.txt"),
                new Uri(rootUri, "llms.txt"),
            }
        )
        {
            if (await CanFetchAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"No llms.txt or llms-full.txt endpoint was found for '{inputUri}'."
        );
    }

    private async Task<bool> CanFetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.LlmsFetch
        );
        activity?.SetTag("docscontext.source.location", uri.ToString());

        using var response = await httpClientFactory
            .CreateClient()
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<SourceDocumentInput> LoadRemoteDocumentAsync(
        Uri uri,
        string fallbackTitle,
        CancellationToken cancellationToken
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.PageFetch
        );
        activity?.SetTag("docscontext.source.location", uri.ToString());

        using var response = await httpClientFactory
            .CreateClient()
            .GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);

        string content;
        string title;

        if (IsHtmlDocument(uri, mediaType, rawContent))
        {
            content = NormalizeHtmlContent(rawContent, fallbackTitle, out title);
        }
        else
        {
            content = NormalizeTextContent(rawContent);
            title = ExtractTitle(content, fallbackTitle);
        }

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = uri.Host;
        }

        return new SourceDocumentInput(path, title, content);
    }

    private async Task<string> DownloadStringAsync(
        Uri uri,
        string activityName,
        CancellationToken cancellationToken
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(activityName);
        activity?.SetTag("docscontext.source.location", uri.ToString());

        using var response = await httpClientFactory
            .CreateClient()
            .GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyList<Uri> ParseLlmsLinks(string content, Uri baseUri)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkPattern.Matches(content))
        {
            TryAddUri(match.Groups[1].Value);
        }

        foreach (Match match in BareUrlPattern.Matches(content))
        {
            TryAddUri(match.Value.TrimEnd('.', ',', ';', ')'));
        }

        return results.Select(uri => new Uri(uri, UriKind.Absolute)).ToList();

        void TryAddUri(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith('#'))
            {
                return;
            }

            if (!Uri.TryCreate(baseUri, candidate, out var resolvedUri))
            {
                return;
            }

            if (!resolvedUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsLlmsTextUri(resolvedUri))
            {
                return;
            }

            results.Add(resolvedUri.ToString());
        }
    }

    private static bool IsLlmsTextUri(Uri uri)
    {
        return uri.AbsolutePath.EndsWith("/llms.txt", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith("/llms-full.txt", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath == "/";
    }

    private static bool IsHtmlDocument(Uri uri, string? mediaType, string rawContent)
    {
        if (
            !string.IsNullOrWhiteSpace(mediaType)
            && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return rawContent.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || rawContent.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTextContent(string content)
    {
        return content.Replace("\r\n", "\n").Trim();
    }

    private static string NormalizeHtmlContent(string html, string fallbackTitle, out string title)
    {
        var working = html.Replace("\r\n", "\n");
        working = Regex.Replace(
            working,
            "<(script|style|noscript)[^>]*>.*?</\\1>",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        title = HtmlTitlePattern.Match(working) is { Success: true } titleMatch
            ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
            : fallbackTitle;

        working = HtmlCodeBlockPattern.Replace(
            working,
            match =>
                $"\n```text\n{WebUtility.HtmlDecode(StripTags(match.Groups[1].Value)).Trim()}\n```\n"
        );
        working = HtmlHeadingPattern.Replace(
            working,
            match =>
                $"\n{new string('#', int.Parse(match.Groups[1].Value))} {WebUtility.HtmlDecode(StripTags(match.Groups[2].Value)).Trim()}\n\n"
        );
        working = HtmlParagraphPattern.Replace(
            working,
            match => $"\n{WebUtility.HtmlDecode(StripTags(match.Groups[2].Value)).Trim()}\n\n"
        );
        working = HtmlListItemPattern.Replace(
            working,
            match => $"\n- {WebUtility.HtmlDecode(StripTags(match.Groups[1].Value)).Trim()}"
        );
        working = HtmlBreakPattern.Replace(working, "\n");
        working = WebUtility.HtmlDecode(StripTags(working));
        working = CollapseNewlinesPattern.Replace(working, "\n\n").Trim();

        if (string.IsNullOrWhiteSpace(working))
        {
            return $"# {fallbackTitle}\n";
        }

        if (!H1Pattern.IsMatch(working))
        {
            working = $"# {title}\n\n{working}";
        }

        return working;
    }

    private static string StripTags(string html)
    {
        return HtmlTagPattern.Replace(html, string.Empty);
    }

    private sealed record SourceDocumentInput(string Path, string Title, string Content);

    private sealed record SourcePayload(
        IReadOnlyList<SourceDocumentInput> Documents,
        string Fingerprint,
        string? CleanupPath
    );
}
