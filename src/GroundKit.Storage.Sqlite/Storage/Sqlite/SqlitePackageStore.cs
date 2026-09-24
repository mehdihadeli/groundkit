using System.Data;
using GroundKit.Core.Abstractions;
using GroundKit.Core.Contracts;
using GroundKit.Observability.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GroundKit.Storage.Sqlite;

public sealed class SqlitePackageStore(
    PackageStoreOptions options,
    ILogger<SqlitePackageStore> logger
) : IPackageStore
{
    public async Task<string> SaveAsync(
        BuildResult buildResult,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(options.RootPath);

        var versionLabel = string.IsNullOrWhiteSpace(buildResult.Manifest.Version)
            ? "dev"
            : buildResult.Manifest.Version;
        var packagePath = Path.Combine(
            options.RootPath,
            $"{buildResult.Manifest.PackageId}@{versionLabel}.db"
        );

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        using var activity = GroundKitTelemetry.ActivitySource.StartActivity(
            GroundKitTelemetry.Activities.PackageBuild
        );
        var startedAt = DateTimeOffset.UtcNow;

        await using var context = CreateDbContext(packagePath);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureSearchSchemaAsync(context, cancellationToken);

        context.Manifests.Add(MapManifest(buildResult.Manifest));
        context.Sources.Add(MapSource(buildResult.Source, buildResult.Manifest.PackageId));
        context.Documents.AddRange(buildResult.Documents.Select(MapDocument));
        context.Chunks.AddRange(buildResult.Chunks.Select(MapChunk));
        context.Warnings.AddRange(buildResult.Warnings.Select(MapWarning));

        await context.SaveChangesAsync(cancellationToken);
        await RebuildSearchIndexAsync(context, cancellationToken);

        GroundKitTelemetry.Metrics.PackagesInstalled.Add(1);
        GroundKitTelemetry.Metrics.BuildDurationMs.Record(
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
        );
        logger.LogInformation(
            "Saved package {PackageId} to {PackagePath}.",
            buildResult.Manifest.PackageId,
            packagePath
        );

        return packagePath;
    }

    public async Task<string> ImportAsync(
        string packageFilePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFilePath);

        if (!File.Exists(packageFilePath))
        {
            throw new FileNotFoundException("Package file was not found.", packageFilePath);
        }

        Directory.CreateDirectory(options.RootPath);

        await using var sourceContext = CreateDbContext(packageFilePath);
        var manifest = await sourceContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (manifest is null)
        {
            throw new InvalidOperationException(
                "Package file does not contain GroundKit manifest metadata."
            );
        }

        var destinationPath = Path.Combine(options.RootPath, Path.GetFileName(packageFilePath));

        if (
            !Path.GetFullPath(packageFilePath)
                .Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)
        )
        {
            File.Copy(packageFilePath, destinationPath, overwrite: true);
        }

        logger.LogInformation(
            "Imported package {PackageId} from {SourcePath} to {DestinationPath}.",
            manifest.PackageId,
            packageFilePath,
            destinationPath
        );

        return destinationPath;
    }

    public async Task<string> ExportAsync(
        string packageId,
        string destinationPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var package = await FindPackageAsync(packageId, cancellationToken);
        if (package is null)
        {
            throw new InvalidOperationException($"Package '{packageId}' was not found.");
        }

        var exportPath = ResolveExportPath(destinationPath, package);
        var exportDirectory = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrWhiteSpace(exportDirectory))
        {
            Directory.CreateDirectory(exportDirectory);
        }

        File.Copy(package.PackagePath, exportPath, overwrite: true);
        logger.LogInformation(
            "Exported package {PackageId} from {PackagePath} to {ExportPath}.",
            package.PackageId,
            package.PackagePath,
            exportPath
        );

        return exportPath;
    }

    public async Task<IReadOnlyList<PackageSummary>> ListAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(options.RootPath))
        {
            return [];
        }

        var summaries = new List<PackageSummary>();
        foreach (
            var packagePath in Directory.EnumerateFiles(
                options.RootPath,
                "*.db",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            await using var context = CreateDbContext(packagePath);
            var manifest = await context
                .Manifests.AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
            if (manifest is not null)
            {
                summaries.Add(MapSummary(manifest, packagePath));
            }
        }

        return summaries
            .OrderBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(summary => summary.BuiltAt)
            .ToList();
    }

    public Task<PackageSummary?> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default
    ) => FindPackageAsync(packageId, cancellationToken);

    public async Task<int> RemoveAsync(
        string packageId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(options.RootPath))
        {
            return 0;
        }

        var removedCount = 0;
        foreach (
            var packagePath in Directory
                .EnumerateFiles(options.RootPath, "*.db", SearchOption.TopDirectoryOnly)
                .ToList()
        )
        {
            await using var context = CreateDbContext(packagePath);
            var manifest = await context
                .Manifests.AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (
                manifest is null
                || !manifest.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            await context.DisposeAsync();
            File.Delete(packagePath);
            removedCount++;
        }

        if (removedCount > 0)
        {
            logger.LogInformation(
                "Removed {RemovedCount} package file(s) for {PackageId}.",
                removedCount,
                packageId
            );
        }

        return removedCount;
    }

    public async Task<int> RemoveAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!Directory.Exists(options.RootPath))
        {
            return 0;
        }

        foreach (
            var packagePath in Directory
                .EnumerateFiles(options.RootPath, "*.db", SearchOption.TopDirectoryOnly)
                .ToList()
        )
        {
            await using var context = CreateDbContext(packagePath);
            var manifest = await context
                .Manifests.AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (
                manifest is null
                || !manifest.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            await context.DisposeAsync();
            File.Delete(packagePath);
            logger.LogInformation(
                "Removed package {PackageId}@{Version} from {PackagePath}.",
                packageId,
                version,
                packagePath
            );
            return 1;
        }

        return 0;
    }

    public async Task<DocumentationSource?> GetSourceAsync(
        string packageId,
        CancellationToken cancellationToken = default
    )
    {
        var package = await FindPackageAsync(packageId, cancellationToken);
        if (package is null)
        {
            return null;
        }

        await using var context = CreateDbContext(package.PackagePath);
        var source = await context.Sources.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return source is null ? null : MapSource(source);
    }

    public async Task<DocsQueryResponse> QueryAsync(
        DocsQueryRequest request,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = GroundKitTelemetry.ActivitySource.StartActivity(
            GroundKitTelemetry.Activities.Search
        );
        activity?.SetTag("groundkit.package_id", request.PackageId);

        var package = await FindPackageAsync(request.PackageId, cancellationToken);

        if (package is null)
        {
            throw new InvalidOperationException($"Package '{request.PackageId}' was not found.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        await using var context = CreateDbContext(package.PackagePath);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var ftsQuery = BuildFtsQuery(request.Topic);
        var optionsValue = request.Options ?? new RetrievalOptions();

        const string sql =
            @"
SELECT chunks.document_title,
       chunks.section_title,
       chunks.content,
       chunks.token_estimate,
       chunks.has_code,
       bm25(chunk_search, 5.0, 10.0, 1.0) AS score
FROM chunk_search
INNER JOIN chunks ON chunks.chunk_id = chunk_search.chunk_id
WHERE chunk_search MATCH $query
ORDER BY score
LIMIT $limit;";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$query", ftsQuery);
        command.Parameters.AddWithValue("$limit", optionsValue.MaxHits);

        var rawHits = new List<DocsQueryHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rawHits.Add(
                new DocsQueryHit(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetBoolean(4),
                    -reader.GetDouble(5)
                )
            );
        }

        var filteredHits = ApplyRelativeCutoff(rawHits, optionsValue.RelativeScoreCutoff);
        var selectedHits = TrimToTokenBudget(filteredHits, optionsValue.MaxTokens);
        var totalTokens = selectedHits.Sum(hit => hit.TokenEstimate);

        GroundKitTelemetry.Metrics.ResultsReturned.Add(selectedHits.Count);
        GroundKitTelemetry.Metrics.SearchDurationMs.Record(
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
        );

        return new DocsQueryResponse(package.PackageId, package.Version, selectedHits, totalTokens);
    }

    private async Task<PackageSummary?> FindPackageAsync(
        string packageSelector,
        CancellationToken cancellationToken
    )
    {
        var separator = packageSelector.LastIndexOf('@');
        var packageId = separator > 0 ? packageSelector[..separator] : packageSelector;
        var version = separator > 0 ? packageSelector[(separator + 1)..] : null;

        return (await ListAsync(cancellationToken))
            .Where(summary =>
                summary.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                && (
                    version is null
                    || string.Equals(summary.Version, version, StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderByDescending(summary => summary.BuiltAt)
            .FirstOrDefault();
    }

    private static async Task EnsureSearchSchemaAsync(
        PackageDbContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            @"
CREATE VIRTUAL TABLE IF NOT EXISTS chunk_search
USING fts5(chunk_id UNINDEXED, document_title, section_title, content);",
            cancellationToken
        );
    }

    private static async Task RebuildSearchIndexAsync(
        PackageDbContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Database.ExecuteSqlRawAsync("DELETE FROM chunk_search;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            @"
INSERT INTO chunk_search (chunk_id, document_title, section_title, content)
SELECT chunk_id, document_title, section_title, content
FROM chunks;",
            cancellationToken
        );
    }

    private static PackageDbContext CreateDbContext(string packagePath)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PackageDbContext>();
        optionsBuilder.UseSqlite(
            new SqliteConnectionStringBuilder
            {
                DataSource = packagePath,
                Pooling = false,
            }.ToString()
        );
        return new PackageDbContext(optionsBuilder.Options);
    }

    private static PackageSummary MapSummary(ManifestEntity manifest, string packagePath)
    {
        return new PackageSummary(
            manifest.PackageId,
            manifest.DisplayName,
            manifest.Version,
            manifest.DocumentCount,
            manifest.ChunkCount,
            manifest.BuiltAt,
            packagePath
        );
    }

    private static ManifestEntity MapManifest(PackageManifest manifest)
    {
        return new ManifestEntity
        {
            PackageId = manifest.PackageId,
            DisplayName = manifest.DisplayName,
            Version = manifest.Version,
            SourceKind = manifest.SourceKind,
            SourceLocation = manifest.SourceLocation,
            SourceCanonicalId = manifest.SourceCanonicalId,
            SourceFingerprint = manifest.SourceFingerprint,
            BuiltAt = manifest.BuiltAt,
            BuilderVersion = manifest.BuilderVersion,
            DocumentCount = manifest.DocumentCount,
            ChunkCount = manifest.ChunkCount,
            WarningCount = manifest.WarningCount,
        };
    }

    private static SourceMetadataEntity MapSource(DocumentationSource source, string packageId)
    {
        return new SourceMetadataEntity
        {
            PackageId = packageId,
            SourceKind = source.Kind,
            CanonicalId = source.CanonicalId,
            DisplayName = source.DisplayName,
            Location = source.Location,
            DocsPath = source.DocsPath,
            Version = source.Version,
            Tag = source.Tag,
            Branch = source.Branch,
            Fingerprint = source.Fingerprint,
            LastBuiltAt = source.LastBuiltAt,
            LastCheckedAt = source.LastCheckedAt,
        };
    }

    private static DocumentationSource MapSource(SourceMetadataEntity source)
    {
        return new DocumentationSource(
            source.SourceKind,
            source.CanonicalId,
            source.DisplayName,
            source.Location,
            source.DocsPath,
            source.Version,
            source.Tag,
            source.Branch,
            source.Fingerprint,
            source.LastBuiltAt,
            source.LastCheckedAt
        );
    }

    private static DocumentEntity MapDocument(DocumentRecord document)
    {
        return new DocumentEntity
        {
            DocumentId = document.DocumentId,
            Title = document.Title,
            Path = document.Path,
            Content = document.Content,
            Description = document.Description,
            Language = document.Language,
        };
    }

    private static ChunkEntity MapChunk(ChunkRecord chunk)
    {
        return new ChunkEntity
        {
            ChunkId = chunk.ChunkId,
            DocumentId = chunk.DocumentId,
            DocumentTitle = chunk.DocumentTitle,
            SectionTitle = chunk.SectionTitle,
            Path = chunk.Path,
            Content = chunk.Content,
            TokenEstimate = chunk.TokenEstimate,
            HasCode = chunk.HasCode,
            ContentHash = chunk.ContentHash,
            Sequence = chunk.Sequence,
            PreviousChunkId = chunk.PreviousChunkId,
            NextChunkId = chunk.NextChunkId,
        };
    }

    private static WarningEntity MapWarning(BuildWarning warning)
    {
        return new WarningEntity
        {
            Code = warning.Code,
            Message = warning.Message,
            SourcePath = warning.SourcePath,
        };
    }

    private static IReadOnlyList<DocsQueryHit> ApplyRelativeCutoff(
        IReadOnlyList<DocsQueryHit> hits,
        double relativeScoreCutoff
    )
    {
        if (hits.Count == 0)
        {
            return hits;
        }

        var topScore = hits[0].Score;
        if (topScore <= 0)
        {
            return hits;
        }

        var cutoff = topScore * Math.Max(relativeScoreCutoff, 0.1d);
        return hits.Where(hit => hit.Score >= cutoff).ToList();
    }

    private static string ResolveExportPath(string destinationPath, PackageSummary package)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);

        if (Directory.Exists(fullDestinationPath) || !Path.HasExtension(fullDestinationPath))
        {
            return Path.Combine(fullDestinationPath, Path.GetFileName(package.PackagePath));
        }

        return fullDestinationPath;
    }

    private static IReadOnlyList<DocsQueryHit> TrimToTokenBudget(
        IReadOnlyList<DocsQueryHit> hits,
        int maxTokens
    )
    {
        var total = 0;
        var selected = new List<DocsQueryHit>();

        foreach (var hit in hits)
        {
            if (total + hit.TokenEstimate > maxTokens)
            {
                GroundKitTelemetry.Metrics.TokenBudgetTrims.Add(1);
                break;
            }

            selected.Add(hit);
            total += hit.TokenEstimate;
        }

        return selected;
    }

    private static string BuildFtsQuery(string topic)
    {
        var terms = topic
            .Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Select(term => term.Replace("\"", string.Empty))
            .Where(term => term.Length > 1)
            .Select(term => $"\"{term}*\"")
            .ToList();

        if (terms.Count == 0)
        {
            throw new InvalidOperationException(
                "Query topic did not contain any searchable terms."
            );
        }

        return string.Join(" OR ", terms);
    }
}
