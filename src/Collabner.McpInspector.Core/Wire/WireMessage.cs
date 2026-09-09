// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.McpInspector.Wire;

/// <summary>
/// A single JSON-RPC wire payload captured while communicating with an MCP server.
/// </summary>
/// <param name="Direction">Either <c>REQUEST</c> or <c>RESPONSE</c>.</param>
/// <param name="Payload">The pretty-printed JSON payload.</param>
/// <param name="Timestamp">When the payload was observed.</param>
public sealed record WireMessage(string Direction, string Payload, DateTimeOffset Timestamp);
