namespace DocsContext.Core.Contracts;

public sealed record RegistryPackage(
    string Name,
    string Registry,
    string Version,
    string? Description,
    long? Size
);

public sealed record RegistryPackageMetadata(
    string Registry,
    string Name,
    string Version,
    string? SourceCommit
);
