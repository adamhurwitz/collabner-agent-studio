// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// Loads tool schemas from an MCP server using either the protocol or SDK projection.
/// </summary>
public static class SchemaLoader
{
    public static async Task<object?> LoadAsync(
        string serverName,
        McpServerConfig config,
        RunMode mode,
        ILoggerFactory loggerFactory)
    {
        return mode == RunMode.Protocol
            ? await LoadProtocolSchemaAsync(serverName, config, loggerFactory)
            : await LoadSdkSchemaAsync(serverName, config, loggerFactory);
    }

    public static async Task<object?> LoadProtocolSchemaAsync(
        string serverName,
        McpServerConfig config,
        ILoggerFactory loggerFactory)
    {
        try
        {
            await using var client = await McpClientFactory.CreateClientAsync(serverName, config, loggerFactory);
            ListToolsResult result = await client.ListToolsAsync(new ListToolsRequestParams());
            return result;
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    public static async Task<object?> LoadSdkSchemaAsync(
        string serverName,
        McpServerConfig config,
        ILoggerFactory loggerFactory)
    {
        try
        {
            await using var client = await McpClientFactory.CreateClientAsync(serverName, config, loggerFactory);
            var tools = await client.ListToolsAsync();
            return tools;
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
