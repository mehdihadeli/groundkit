using DocsContext.Core.Contracts;

namespace DocsContext.Core.Abstractions;

public interface IDocumentPackageBuilder
{
    Task<BuildResult> BuildAsync(string input, string? docsPath = null, CancellationToken cancellationToken = default);
}