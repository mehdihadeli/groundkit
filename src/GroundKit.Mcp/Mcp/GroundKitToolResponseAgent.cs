using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace GroundKit.Mcp;

public sealed class GroundKitToolResponseAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ChatClientAgent agent;

    public GroundKitToolResponseAgent(ILoggerFactory loggerFactory, IServiceProvider services)
    {
        agent = new ChatClientAgent(
            new GroundKitFormattingChatClient(),
            instructions: "Format GroundKit MCP tool results into concise plain text. Use only supplied data and never invent details.",
            name: "groundkit-tool-agent",
            description: "Converts structured GroundKit MCP payloads into concise human-readable text.",
            loggerFactory: loggerFactory,
            services: services
        );
    }

    public async Task<string> ComposeAsync(
        string toolName,
        object payload,
        CancellationToken cancellationToken = default
    )
    {
        var prompt = JsonSerializer.Serialize(
            new ToolPromptEnvelope(toolName, payload),
            JsonOptions
        );
        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);

        return string.IsNullOrWhiteSpace(response.Text)
            ? JsonSerializer.Serialize(payload, JsonOptions)
            : response.Text;
    }

    private sealed record ToolPromptEnvelope(string ToolName, object Payload);
}

internal sealed class GroundKitFormattingChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var text = ComposeText(messages);
        var response = new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new TextContent(text)])
            {
                AuthorName = "groundkit-tool-agent",
            }
        );

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, ComposeText(messages))
        {
            AuthorName = "groundkit-tool-agent",
        };
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose() { }

    private static string ComposeText(IEnumerable<ChatMessage> messages)
    {
        var prompt = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = messages.LastOrDefault()?.Text;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "No tool output available.";
        }

        var envelope = JsonSerializer.Deserialize<ToolPromptEnvelope>(prompt, JsonOptions);
        if (envelope is null)
        {
            return prompt;
        }

        return envelope.ToolName switch
        {
            "resolve-source" => FormatResolveSource(envelope.Payload),
            "query-docs" => FormatQueryDocs(envelope.Payload),
            "library_catalog" => FormatLibraryCatalog(envelope.Payload),
            "search_packages" => FormatSearchPackages(envelope.Payload),
            "download_package" => FormatDownloadPackage(envelope.Payload),
            _ => envelope.Payload.GetRawText(),
        };
    }

    private static string FormatLibraryCatalog(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("libraries", out var libraries)
            || libraries.GetArrayLength() == 0
        )
        {
            return "No catalog libraries matched.";
        }

        var builder = new StringBuilder("Starter documentation libraries:\n");
        foreach (var library in libraries.EnumerateArray())
        {
            builder
                .Append("- ")
                .Append(library.GetProperty("name").GetString())
                .Append(": ")
                .Append(library.GetProperty("description").GetString())
                .Append(". Build from ")
                .Append(library.GetProperty("repository").GetString())
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatSearchPackages(JsonElement payload)
    {
        if (!payload.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
        {
            return "No registry packages matched.";
        }

        var builder = new StringBuilder("Registry packages:\n");
        foreach (var result in results.EnumerateArray())
        {
            builder
                .Append("- ")
                .Append(result.GetProperty("name").GetString())
                .Append('@')
                .Append(result.GetProperty("version").GetString());
            if (
                result.TryGetProperty("description", out var description)
                && description.ValueKind != JsonValueKind.Null
            )
            {
                builder.Append(": ").Append(description.GetString());
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatDownloadPackage(JsonElement payload)
    {
        return $"Installed {payload.GetProperty("name").GetString()}@{payload.GetProperty("version").GetString()} at {payload.GetProperty("installedPath").GetString()}.";
    }

    private static string FormatResolveSource(JsonElement payload)
    {
        if (!payload.TryGetProperty("packages", out var packages) || packages.GetArrayLength() == 0)
        {
            return "No installed package matched the query.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Resolved {packages.GetArrayLength()} package match(es):");

        var rank = 1;
        foreach (var package in packages.EnumerateArray())
        {
            var packageId = package.GetProperty("packageId").GetString() ?? "unknown";
            var displayName = package.GetProperty("displayName").GetString() ?? packageId;
            var version = package.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString()
                : null;
            var documentCount = package.GetProperty("documentCount").GetInt32();
            var chunkCount = package.GetProperty("chunkCount").GetInt32();
            var score = package.GetProperty("score").GetDouble();

            builder.Append(rank++).Append(". ").Append(packageId).Append(" - ").Append(displayName);

            if (!string.IsNullOrWhiteSpace(version))
            {
                builder.Append(" (v").Append(version).Append(')');
            }

            builder
                .Append(". docs=")
                .Append(documentCount)
                .Append(", chunks=")
                .Append(chunkCount)
                .Append(", score=")
                .Append(score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatQueryDocs(JsonElement payload)
    {
        var packageId = payload.GetProperty("packageId").GetString() ?? "unknown";
        var version = payload.TryGetProperty("version", out var versionElement)
            ? versionElement.GetString()
            : null;
        var totalTokens = payload.TryGetProperty("totalTokens", out var tokensElement)
            ? tokensElement.GetInt32()
            : 0;

        if (!payload.TryGetProperty("hits", out var hits) || hits.GetArrayLength() == 0)
        {
            return $"No matching documentation found in {packageId}.";
        }

        var builder = new StringBuilder();
        builder.Append("Package ").Append(packageId);

        if (!string.IsNullOrWhiteSpace(version))
        {
            builder.Append(" (v").Append(version).Append(')');
        }

        builder
            .Append(" returned ")
            .Append(hits.GetArrayLength())
            .Append(" hit(s) within ")
            .Append(totalTokens)
            .AppendLine(" tokens:");

        foreach (var hit in hits.EnumerateArray())
        {
            var title = hit.GetProperty("documentTitle").GetString() ?? "Untitled";
            var section = hit.GetProperty("sectionTitle").GetString() ?? "Root";
            var content = hit.GetProperty("content").GetString() ?? string.Empty;
            var score = hit.GetProperty("score").GetDouble();
            var tokenEstimate = hit.GetProperty("tokenEstimate").GetInt32();
            var hasCode = hit.GetProperty("hasCode").GetBoolean();

            builder
                .Append("- ")
                .Append(title)
                .Append(" / ")
                .Append(section)
                .Append(". score=")
                .Append(score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                .Append(", tokens=")
                .Append(tokenEstimate)
                .Append(", code=")
                .Append(hasCode ? "yes" : "no")
                .AppendLine();
            builder.Append("  ").AppendLine(Truncate(CompactWhitespace(content), 220));
        }

        return builder.ToString().TrimEnd();
    }

    private static string CompactWhitespace(string text)
    {
        return string.Join(
            ' ',
            text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }

    private sealed record ToolPromptEnvelope(string ToolName, JsonElement Payload);
}
