using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Observability.Telemetry;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DocsContext.Mcp;

[McpServerToolType]
public static class DocsContextMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(
        Name = "resolve-source",
        Title = "Resolve Installed Documentation Sources",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ResolveSourcePayload)
    )]
    [Description("Resolve installed documentation packages by package id or display name.")]
    public static async Task<CallToolResult> ResolveSourceAsync(
        [Description("Package id or display name to match.")] string query,
        IPackageStore packageStore,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken,
        [Description("Maximum number of matches to return.")] int maxResults = 5
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.MctTool
        );
        activity?.SetTag("docscontext.mcp.method", "resolve-source");

        if (string.IsNullOrWhiteSpace(query))
        {
            return CreateErrorResult("Argument 'query' is required.");
        }

        if (maxResults <= 0)
        {
            return CreateErrorResult("Argument 'maxResults' must be greater than zero.");
        }

        var results = (await packageStore.ListAsync(cancellationToken))
            .Select(package => new
            {
                package.PackageId,
                package.DisplayName,
                package.Version,
                package.DocumentCount,
                package.ChunkCount,
                Score = ComputeMatchScore(query, package),
            })
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(result => new ResolveSourcePackage(
                result.PackageId,
                result.DisplayName,
                result.Version,
                result.DocumentCount,
                result.ChunkCount,
                result.Score
            ))
            .ToArray();

        var payload = new ResolveSourcePayload(results);
        return await CreateSuccessResultAsync(
            toolResponseAgent,
            "resolve-source",
            payload,
            cancellationToken
        );
    }

    [McpServerTool(
        Name = "query-docs",
        Title = "Query Installed Documentation",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryDocsPayload)
    )]
    [Description("Query an installed documentation package for focused sections.")]
    public static async Task<CallToolResult> QueryDocsAsync(
        [Description("Installed package id.")] string packageId,
        [Description("Query text to search inside the package.")] string topic,
        IPackageStore packageStore,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken,
        [Description("Maximum token budget for returned hits.")] int maxTokens = 2_000,
        [Description("Maximum number of hits to return.")] int maxHits = 8,
        [Description("Minimum relative score cutoff for returned hits.")]
            double relativeScoreCutoff = 0.5
    )
    {
        using var activity = DocsContextTelemetry.ActivitySource.StartActivity(
            DocsContextTelemetry.Activities.MctTool
        );
        activity?.SetTag("docscontext.mcp.method", "query-docs");

        if (string.IsNullOrWhiteSpace(packageId))
        {
            return CreateErrorResult("Argument 'packageId' is required.");
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            return CreateErrorResult("Argument 'topic' is required.");
        }

        if (maxTokens <= 0)
        {
            return CreateErrorResult("Argument 'maxTokens' must be greater than zero.");
        }

        if (maxHits <= 0)
        {
            return CreateErrorResult("Argument 'maxHits' must be greater than zero.");
        }

        if (relativeScoreCutoff is < 0 or > 1)
        {
            return CreateErrorResult("Argument 'relativeScoreCutoff' must be between 0 and 1.");
        }

        var response = await packageStore.QueryAsync(
            new DocsQueryRequest(
                packageId,
                topic,
                new RetrievalOptions(maxTokens, maxHits, relativeScoreCutoff)
            ),
            cancellationToken
        );

        var payload = new QueryDocsPayload(
            response.PackageId,
            response.Version,
            response.TotalTokens,
            response
                .Hits.Select(hit => new QueryDocsHit(
                    hit.DocumentTitle,
                    hit.SectionTitle,
                    hit.Content,
                    hit.TokenEstimate,
                    hit.HasCode,
                    hit.Score
                ))
                .ToArray()
        );

        return await CreateSuccessResultAsync(
            toolResponseAgent,
            "query-docs",
            payload,
            cancellationToken
        );
    }

    [McpServerTool(
        Name = "get_docs",
        Title = "Get Library Documentation",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryDocsPayload)
    )]
    [Description(
        "Primary documentation lookup. Resolve a package first, then search it with a short API name or topic."
    )]
    public static Task<CallToolResult> GetDocsAsync(
        [Description("Installed package id returned by resolve-source.")] string library,
        [Description("Short API name, keyword, or phrase. Extra words narrow results.")]
            string topic,
        IPackageStore packageStore,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken,
        [Description("Maximum token budget for returned hits.")] int maxTokens = 2_000,
        [Description("Maximum number of hits to return.")] int maxHits = 8,
        [Description("Minimum relative score cutoff from 0 to 1.")] double relativeScoreCutoff = 0.5
    ) =>
        QueryDocsAsync(
            library,
            topic,
            packageStore,
            toolResponseAgent,
            cancellationToken,
            maxTokens,
            maxHits,
            relativeScoreCutoff
        );

    [McpServerTool(
        Name = "library_catalog",
        Title = "Browse Starter Library Catalog",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LibraryCatalogPayload)
    )]
    [Description(
        "Browse 20 curated library documentation sources available for local package building."
    )]
    public static async Task<CallToolResult> LibraryCatalogAsync(
        [Description("Optional library name or keyword filter.")] string? query,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken
    )
    {
        var entries = LibraryCatalog
            .StarterLibraries.Where(entry =>
                string.IsNullOrWhiteSpace(query)
                || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        return await CreateSuccessResultAsync(
            toolResponseAgent,
            "library_catalog",
            new LibraryCatalogPayload(entries),
            cancellationToken
        );
    }

    [McpServerTool(
        Name = "search_packages",
        Title = "Search Documentation Packages",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchPackagesPayload)
    )]
    [Description(
        "Search the hosted documentation registry. Use short package names, then download a matching version."
    )]
    public static async Task<CallToolResult> SearchPackagesAsync(
        [Description("Package registry, such as npm, pip, cargo, or maven.")] string registry,
        [Description("Short package name, such as react, next, or fastapi.")] string name,
        IContextRegistryClient registryClient,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken,
        [Description("Optional exact package version.")] string? version = null
    )
    {
        if (string.IsNullOrWhiteSpace(registry) || string.IsNullOrWhiteSpace(name))
        {
            return CreateErrorResult("Arguments 'registry' and 'name' are required.");
        }

        var results = await registryClient.SearchAsync(registry, name, version, cancellationToken);
        return await CreateSuccessResultAsync(
            toolResponseAgent,
            "search_packages",
            new SearchPackagesPayload(results),
            cancellationToken
        );
    }

    [McpServerTool(
        Name = "download_package",
        Title = "Download Documentation Package",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DownloadPackagePayload)
    )]
    [Description(
        "Download a hosted documentation package and install it locally for get_docs queries."
    )]
    public static async Task<CallToolResult> DownloadPackageAsync(
        [Description("Package registry, such as npm or pip.")] string registry,
        [Description("Package name.")] string name,
        [Description("Exact package version.")] string version,
        IContextRegistryClient registryClient,
        IPackageStore packageStore,
        DocsContextToolResponseAgent toolResponseAgent,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(registry)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(version)
        )
        {
            return CreateErrorResult("Arguments 'registry', 'name', and 'version' are required.");
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"docs-context-{Guid.NewGuid():N}.db");
        try
        {
            await registryClient.DownloadAsync(
                registry,
                name,
                version,
                temporaryPath,
                cancellationToken
            );
            var installedPath = await packageStore.ImportAsync(temporaryPath, cancellationToken);
            var payload = new DownloadPackagePayload(name, version, installedPath);
            return await CreateSuccessResultAsync(
                toolResponseAgent,
                "download_package",
                payload,
                cancellationToken
            );
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<CallToolResult> CreateSuccessResultAsync<TPayload>(
        DocsContextToolResponseAgent toolResponseAgent,
        string toolName,
        TPayload payload,
        CancellationToken cancellationToken
    )
        where TPayload : notnull
    {
        var text = await toolResponseAgent.ComposeAsync(toolName, payload, cancellationToken);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(payload, JsonOptions),
        };
    }

    private static CallToolResult CreateErrorResult(string message)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }

    private static double ComputeMatchScore(string query, PackageSummary package)
    {
        var normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return 0;
        }

        if (package.PackageId.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        if (package.DisplayName.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0.95;
        }

        if (package.PackageId.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0.85;
        }

        if (package.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0.75;
        }

        var queryTerms = normalizedQuery.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (queryTerms.Length == 0)
        {
            return 0;
        }

        var matchedTerms = queryTerms.Count(term =>
            package.PackageId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || package.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
        );

        return matchedTerms == 0 ? 0 : matchedTerms / (double)queryTerms.Length * 0.6;
    }

    public sealed record ResolveSourcePayload(IReadOnlyList<ResolveSourcePackage> Packages);

    public sealed record ResolveSourcePackage(
        string PackageId,
        string DisplayName,
        string? Version,
        int DocumentCount,
        int ChunkCount,
        double Score
    );

    public sealed record QueryDocsPayload(
        string PackageId,
        string? Version,
        int TotalTokens,
        IReadOnlyList<QueryDocsHit> Hits
    );

    public sealed record QueryDocsHit(
        string DocumentTitle,
        string SectionTitle,
        string Content,
        int TokenEstimate,
        bool HasCode,
        double Score
    );

    public sealed record LibraryCatalogPayload(IReadOnlyList<LibraryCatalogEntry> Libraries);

    public sealed record SearchPackagesPayload(IReadOnlyList<RegistryPackage> Results);

    public sealed record DownloadPackagePayload(string Name, string Version, string InstalledPath);
}
