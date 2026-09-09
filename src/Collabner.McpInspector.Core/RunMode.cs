// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.McpInspector;

/// <summary>
/// Controls how tool schemas are retrieved from an MCP server.
/// </summary>
public enum RunMode
{
    /// <summary>Return the raw protocol <c>ListToolsResult</c>.</summary>
    Protocol,

    /// <summary>Return the SDK client tool projection.</summary>
    Sdk,
}
