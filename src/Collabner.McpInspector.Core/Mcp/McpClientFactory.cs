// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// Creates configured <see cref="McpClient"/> instances from <see cref="McpServerConfig"/> entries.
/// </summary>
public static class McpClientFactory
{
    public static async Task<McpClient> CreateClientAsync(
        string serverName,
        McpServerConfig config,
        ILoggerFactory loggerFactory)
    {
        IClientTransport transport = IsHttp(config)
            ? CreateHttpTransport(serverName, config, loggerFactory)
            : CreateStdioTransport(serverName, config, loggerFactory);

        return await McpClient.CreateAsync(transport, new McpClientOptions(), loggerFactory);
    }

    private static bool IsHttp(McpServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Url))
        {
            return true;
        }

        return config.Type is not null
            && (config.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
                || config.Type.Equals("streamable-http", StringComparison.OrdinalIgnoreCase)
                || config.Type.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase)
                || config.Type.Equals("sse", StringComparison.OrdinalIgnoreCase));
    }

    private static StdioClientTransport CreateStdioTransport(
        string serverName,
        McpServerConfig config,
        ILoggerFactory loggerFactory)
    {
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = serverName,
            Command = ResolveCommand(config),
            Arguments = ResolveArguments(config),
            WorkingDirectory = ResolveWorkingDirectory(config),
            EnvironmentVariables = ResolveEnvironmentVariables(config),
        }, loggerFactory);
    }

    private static HttpClientTransport CreateHttpTransport(
        string serverName,
        McpServerConfig config,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
        {
            throw new InvalidOperationException("An HTTP MCP server entry must define a url.");
        }

        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"The url '{config.Url}' is not a valid absolute URI.");
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = serverName,
            Endpoint = endpoint,
            TransportMode = ResolveTransportMode(config),
            AdditionalHeaders = config.Headers is { Count: > 0 } ? config.Headers : null,
        }, loggerFactory);
    }

    private static HttpTransportMode ResolveTransportMode(McpServerConfig config)
    {
        return config.Type switch
        {
            not null when config.Type.Equals("sse", StringComparison.OrdinalIgnoreCase)
                => HttpTransportMode.Sse,
            not null when config.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
                || config.Type.Equals("streamable-http", StringComparison.OrdinalIgnoreCase)
                || config.Type.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase)
                => HttpTransportMode.StreamableHttp,
            _ => HttpTransportMode.AutoDetect,
        };
    }

    private static IDictionary<string, string?>? ResolveEnvironmentVariables(McpServerConfig config)
    {
        if (config.EnvironmentVariables is null || config.EnvironmentVariables.Count == 0)
        {
            return null;
        }

        return config.EnvironmentVariables;
    }

    private static string ResolveCommand(McpServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Command))
        {
            return config.Command;
        }

        if (!string.IsNullOrWhiteSpace(config.Executable))
        {
            return config.Executable;
        }

        throw new InvalidOperationException("Each MCP server entry must define a command or executable.");
    }

    private static string[] ResolveArguments(McpServerConfig config)
    {
        if (config.Args is { Count: > 0 })
        {
            return config.Args.ToArray();
        }

        return Array.Empty<string>();
    }

    private static string ResolveWorkingDirectory(McpServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory))
        {
            return Path.GetFullPath(config.WorkingDirectory);
        }

        return Directory.GetCurrentDirectory();
    }
}
