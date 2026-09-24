using System.Net;

namespace GroundKit.Registry;

public sealed class RegistryPublisher(HttpClient client)
{
    public async Task<bool> ExistsAsync(
        string registry,
        string name,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        using var response = await client.GetAsync(
            Path(registry, name, version),
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task PublishAsync(
        string registry,
        string name,
        string version,
        string packagePath,
        CancellationToken cancellationToken = default
    )
    {
        var publishKey = Environment.GetEnvironmentVariable("REGISTRY_PUBLISH_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(publishKey))
        {
            throw new InvalidOperationException(
                "REGISTRY_PUBLISH_KEY is required for publishing packages."
            );
        }

        await using var stream = File.OpenRead(packagePath);
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(registry, name, version))
        {
            Content = new StreamContent(stream),
        };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Headers.Authorization = new("Bearer", publishKey);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private string Path(string registry, string name, string version) =>
        $"packages/{Uri.EscapeDataString(registry)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}";

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
}
