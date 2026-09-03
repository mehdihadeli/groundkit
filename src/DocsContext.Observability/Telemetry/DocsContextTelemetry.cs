using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocsContext.Observability.Telemetry;

public static class DocsContextTelemetry
{
    public const string ActivitySourceName = "DocsContext";
    public const string MeterName = "DocsContext";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static class Activities
    {
        public const string SourceDetect = "source-detect";
        public const string GitFetch = "git-fetch";
        public const string LlmsFetch = "llms-fetch";
        public const string PageFetch = "page-fetch";
        public const string Normalize = "normalize";
        public const string Chunk = "chunk";
        public const string PackageBuild = "package-build";
        public const string Search = "search";
        public const string MctTool = "mcp-tool";
    }

    public static class Metrics
    {
        public static readonly Histogram<double> BuildDurationMs = Meter.CreateHistogram<double>(
            "docscontext.build.duration",
            unit: "ms"
        );

        public static readonly Histogram<double> SearchDurationMs = Meter.CreateHistogram<double>(
            "docscontext.search.duration",
            unit: "ms"
        );

        public static readonly Counter<long> PackagesInstalled = Meter.CreateCounter<long>(
            "docscontext.packages.installed",
            unit: "packages"
        );

        public static readonly Counter<long> ChunksCreated = Meter.CreateCounter<long>(
            "docscontext.chunks.created",
            unit: "chunks"
        );

        public static readonly Counter<long> ResultsReturned = Meter.CreateCounter<long>(
            "docscontext.results.returned",
            unit: "results"
        );

        public static readonly Counter<long> TokenBudgetTrims = Meter.CreateCounter<long>(
            "docscontext.token_budget.trims",
            unit: "trims"
        );

        public static readonly Counter<long> IngestionFailures = Meter.CreateCounter<long>(
            "docscontext.ingestion.failures",
            unit: "failures"
        );

        public static readonly Counter<long> RefreshFailures = Meter.CreateCounter<long>(
            "docscontext.refresh.failures",
            unit: "failures"
        );
    }
}
