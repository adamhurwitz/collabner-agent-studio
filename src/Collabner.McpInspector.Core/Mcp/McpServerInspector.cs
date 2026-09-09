// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Configuration;
using Collabner.McpInspector.Wire;
using ModelContextProtocol.Protocol;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// Connects to MCP servers and captures their wire session together with the tool list.
/// Used by the desktop application to populate the Session and Objects views.
/// </summary>
public static class McpServerInspector
{
    /// <summary>
    /// Connects to a single server, lists its tools, and captures the JSON-RPC session.
    /// </summary>
    public static async Task<McpServerInspection> InspectAsync(string serverName, McpServerConfig config)
    {
        var sink = new CollectingWireSink();
        using var loggerFactory = WireLogging.CreateWireLoggerFactory(sink);

        try
        {
            await using var client = await McpClientFactory.CreateClientAsync(serverName, config, loggerFactory);
            ListToolsResult tools = await client.ListToolsAsync(new ListToolsRequestParams());
            return new McpServerInspection(serverName, sink.Snapshot(), tools, null);
        }
        catch (Exception ex)
        {
            return new McpServerInspection(serverName, sink.Snapshot(), null, ex.Message);
        }
    }

    /// <summary>
    /// Inspects every server in the supplied configuration set.
    /// </summary>
    public static async Task<IReadOnlyList<McpServerInspection>> InspectAllAsync(
        IReadOnlyDictionary<string, McpServerConfig> servers)
    {
        var results = new List<McpServerInspection>(servers.Count);
        foreach (var entry in servers)
        {
            results.Add(await InspectAsync(entry.Key, entry.Value));
        }

        return results;
    }
}
