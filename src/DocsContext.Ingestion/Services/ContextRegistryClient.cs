using System.Net.Http.Json;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;

namespace DocsContext.Ingestion.Services;

public sealed class ContextRegistryClient(HttpClient httpClient) : IContextRegistryClient
{
    private const string DefaultRegistryUrl = "http://localhost:8080";

    public async Task<IReadOnlyList<RegistryPackage>> SearchAsync(
        string registry,
        string name,
        string? version = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var query =
            $"search?registry={Uri.EscapeDataString(registry)}&name={Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            query += $"&version={Uri.EscapeDataString(version)}";
        }

        using var response = await httpClient.GetAsync(query, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RegistryPackage[]>(cancellationToken) ?? [];
    }

    public async Task<RegistryPackageMetadata> GetMetadataAsync(
        string registry,
        string name,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        using var response = await httpClient.GetAsync(
            $"packages/{Uri.EscapeDataString(registry)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}",
            cancellationToken
        );
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RegistryPackageMetadata>(cancellationToken)
            ?? throw new InvalidOperationException("Registry returned empty package metadata.");
    }

    public async Task DownloadAsync(
        string registry,
        string name,
        string version,
        string destinationPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        using var response = await httpClient.GetAsync(
            $"packages/{Uri.EscapeDataString(registry)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/download",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        await EnsureSuccessAsync(response, cancellationToken);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Registry request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {detail}"
        );
    }

    internal static string ResolveBaseUrl() =>
        Environment.GetEnvironmentVariable("DOCSCONTEXT_REGISTRY_URL")?.TrimEnd('/')
        ?? DefaultRegistryUrl;
}
