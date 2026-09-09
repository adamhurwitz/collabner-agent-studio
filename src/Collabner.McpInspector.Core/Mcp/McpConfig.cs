// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using Collabner.McpInspector.Configuration;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// Reads and normalizes <c>.mcp.json</c> configuration files.
/// </summary>
public static class McpConfig
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static McpConfigFile? Parse(string json)
        => JsonSerializer.Deserialize<McpConfigFile>(json, ReadOptions);

    public static async Task<McpConfigFile?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(json);
    }

    /// <summary>
    /// Returns the merged set of server entries, preferring <c>servers</c> then <c>mcpServers</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, McpServerConfig> GetServers(McpConfigFile? config)
        => config?.Servers ?? config?.McpServers ?? new Dictionary<string, McpServerConfig>();
}
