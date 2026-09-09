// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json.Serialization;

namespace Collabner.McpInspector.Configuration;

/// <summary>
/// Represents the contents of an <c>.mcp.json</c> configuration file. Both the
/// <c>servers</c> and <c>mcpServers</c> keys are supported.
/// </summary>
public sealed class McpConfigFile
{
    [JsonPropertyName("servers")]
    public Dictionary<string, McpServerConfig>? Servers { get; set; }

    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig>? McpServers { get; set; }
}
