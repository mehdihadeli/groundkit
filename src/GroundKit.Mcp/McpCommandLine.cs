using GroundKit.Hosting;
using GroundKit.Mcp.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Cli;

namespace GroundKit.Mcp;

internal static class McpCommandLine
{
    public static string[] NormalizeArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return args;
        }

        var normalized = args.ToArray();
        normalized[0] = NormalizeCommand(normalized[0]);

        for (var index = 1; index < normalized.Length; index++)
        {
            normalized[index] = NormalizeOption(normalized[index]);
        }

        return normalized;
    }

    public static CommandApp CreateCommandApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("groundkit-mcp");
            config.AddExample("-l", "react,vite");
            config.AddExample("h", "-u", "http://localhost:3001", "-l", "react,vite");
            config
                .AddCommand<HttpMcpCommand>("http")
                .WithAlias("h")
                .WithDescription("Start the HTTP MCP host and expose `/mcp` (alias: h).")
                .WithExample("h", "-u", "http://localhost:3001", "-l", "react,vite");
        });
        app.SetDefaultCommand<StdioMcpCommand>()
            .WithDescription(
                "Start the MCP server over stdio. Use -l or --libs to restrict packages."
            );
        return app;
    }

    public static string NormalizeCommand(string command) =>
        command.ToLowerInvariant() switch
        {
            "h" => "http",
            _ => command,
        };

    public static string NormalizeOption(string option) =>
        option.ToLowerInvariant() switch
        {
            "-l" => "--libs",
            "-u" => "--urls",
            _ => option,
        };

    internal static async Task<int> RunStdioAsync(string? libraries)
    {
        if (!string.IsNullOrWhiteSpace(libraries))
        {
            Environment.SetEnvironmentVariable("GROUNDKIT_ALLOWED_LIBRARIES", libraries);
        }

        var builder = Host.CreateApplicationBuilder([]);
        builder.AddServiceDefaults();
        builder.Services.AddGroundKitServices();
        builder.Services.AddGroundKitMcp();

        using var host = builder.Build();
        await host.Services.GetRequiredService<GroundKitMcpServer>().RunAsync();
        return 0;
    }

    internal static async Task<int> RunHttpAsync(string? libraries, string? urls)
    {
        if (!string.IsNullOrWhiteSpace(libraries))
        {
            Environment.SetEnvironmentVariable("GROUNDKIT_ALLOWED_LIBRARIES", libraries);
        }

        var builder = WebApplication.CreateBuilder([]);
        builder.AddServiceDefaults();
        builder.Services.AddGroundKitServices();
        builder.Services.AddGroundKitMcp();
        if (!string.IsNullOrWhiteSpace(urls))
        {
            builder.WebHost.UseUrls(urls);
        }

        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapMcp("/mcp");
        await app.RunAsync();
        return 0;
    }
}

public sealed class StdioMcpCommand : AsyncCommand<StdioMcpCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-l|--libs")]
        public string? Libraries { get; init; }
    }

    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    ) => McpCommandLine.RunStdioAsync(settings.Libraries);
}

public sealed class HttpMcpCommand : AsyncCommand<HttpMcpCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-l|--libs")]
        public string? Libraries { get; init; }

        [CommandOption("-u|--urls")]
        public string? Urls { get; init; }
    }

    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    ) => McpCommandLine.RunHttpAsync(settings.Libraries, settings.Urls);
}
