namespace DocsContext.Core.Contracts;

public sealed record PackageResolutionRequest(
    string Query,
    string? VersionHint = null,
    int MaxResults = 5
);

public sealed record PackageResolutionResult(
    string PackageId,
    string DisplayName,
    string? Version,
    string Summary,
    double Score
);

public sealed record DocsQueryRequest(
    string PackageId,
    string Topic,
    RetrievalOptions? Options = null
);

public sealed record DocsQueryResponse(
    string PackageId,
    string? Version,
    IReadOnlyList<DocsQueryHit> Hits,
    int TotalTokens
);

public sealed record DocsQueryHit(
    string DocumentTitle,
    string SectionTitle,
    string Content,
    int TokenEstimate,
    bool HasCode,
    double Score
);

public sealed record RetrievalOptions(
    int MaxTokens = 2_000,
    int MaxHits = 8,
    double RelativeScoreCutoff = 0.5
);
