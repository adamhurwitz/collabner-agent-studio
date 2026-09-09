// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json.Serialization;

namespace Collabner.McpInspector.Configuration;

/// <summary>
/// A single MCP server entry from an <c>.mcp.json</c> configuration file.
/// </summary>
public sealed class McpServerConfig
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("executable")]
    public string? Executable { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("cwd")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string?>? EnvironmentVariables { get; set; }

    /// <summary>The endpoint of an HTTP (Streamable HTTP or SSE) MCP server.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Additional HTTP headers sent with every request to an HTTP MCP server.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
