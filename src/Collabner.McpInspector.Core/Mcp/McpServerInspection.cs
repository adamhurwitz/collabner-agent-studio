// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Wire;
using ModelContextProtocol.Protocol;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// The result of inspecting a single MCP server: the captured wire session and the tool list.
/// </summary>
/// <param name="ServerName">The configured name of the server.</param>
/// <param name="Session">The JSON-RPC request/response payloads exchanged during inspection.</param>
/// <param name="Tools">The tool list returned by the server, or <c>null</c> if the call failed.</param>
/// <param name="Error">An error message if the connection or tool listing failed.</param>
public sealed record McpServerInspection(
    string ServerName,
    IReadOnlyList<WireMessage> Session,
    ListToolsResult? Tools,
    string? Error)
{
    public bool IsConnected => Error is null;
}
