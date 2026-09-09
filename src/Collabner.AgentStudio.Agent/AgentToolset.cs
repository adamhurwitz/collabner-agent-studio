// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Collabner.AgentStudio.Agent;

/// <summary>
/// Builds the list of tools handed to the agent from live MCP sessions, wrapping each real tool
/// in an <see cref="InterceptingTool"/> and disambiguating names that collide across servers.
/// </summary>
public static class AgentToolset
{
    /// <summary>Stable identity for a tool, used to look up its <see cref="ToolOverride"/>.</summary>
    public static string ToolKey(string serverName, string toolName) => $"{serverName}\u001f{toolName}";

    public static List<AIFunction> Build(
        IEnumerable<(string ServerName, IReadOnlyList<McpClientTool> Tools)> servers,
        ToolCallGate gate,
        AgentSessionRecorder recorder,
        IReadOnlyDictionary<string, ToolOverride>? overrides = null)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AIFunction>();

        foreach (var (serverName, tools) in servers)
        {
            foreach (var tool in tools)
            {
                ToolOverride? over = null;
                overrides?.TryGetValue(ToolKey(serverName, tool.Name), out over);

                var display = !string.IsNullOrWhiteSpace(over?.Name) ? over!.Name! : tool.Name;
                if (!used.Add(display))
                {
                    // Names must be unique across the registered set; disambiguate on collision
                    // while preserving the original tool name for the real invocation.
                    var prefixed = $"{Sanitize(serverName)}__{display}";
                    display = prefixed;
                    var i = 2;
                    while (!used.Add(display))
                    {
                        display = $"{prefixed}_{i++}";
                    }
                }

                JsonElement? schema = null;
                if (!string.IsNullOrWhiteSpace(over?.ParametersJson))
                {
                    try
                    {
                        schema = JsonDocument.Parse(over!.ParametersJson!).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        // Ignore invalid schema overrides; the original schema is kept.
                    }
                }

                result.Add(new InterceptingTool(tool, serverName, display, gate, recorder, over?.Description, schema));
            }
        }

        return result;
    }

    private static string Sanitize(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            buffer[i] = char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_';
        }

        return new string(buffer);
    }
}
