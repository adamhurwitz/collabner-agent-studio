// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Mcp;
using Collabner.McpInspector.Wire;
using ModelContextProtocol.Protocol;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// The loading lifecycle of a single server tab in the desktop UI.
/// </summary>
public enum ServerLoadStatus
{
    /// <summary>Queued but not yet being inspected.</summary>
    Pending,

    /// <summary>Currently connecting and listing tools.</summary>
    Loading,

    /// <summary>Inspection finished (successfully or with an error).</summary>
    Loaded,
}

/// <summary>
/// A UI-facing wrapper around a single server that tracks its loading state so tabs can be
/// shown and progressed incrementally while other servers are still connecting.
/// </summary>
public sealed class ServerInspectionView
{
    public ServerInspectionView(string serverName) => ServerName = serverName;

    public string ServerName { get; }

    public ServerLoadStatus Status { get; set; } = ServerLoadStatus.Pending;

    public McpServerSession? Connection { get; private set; }

    public bool IsLoaded => Status == ServerLoadStatus.Loaded;

    public bool IsConnected => Connection?.IsConnected ?? false;

    public string? Error => Connection?.Error;

    public IReadOnlyList<WireMessage> Session => Connection?.Session ?? [];

    public IReadOnlyList<Tool> Tools => Connection?.Tools ?? [];

    public IReadOnlyList<Resource> Resources => Connection?.Resources ?? [];

    public IReadOnlyList<Prompt> Prompts => Connection?.Prompts ?? [];

    /// <summary>Records the live connection and marks the server as loaded.</summary>
    public void Complete(McpServerSession connection)
    {
        Connection = connection;
        Status = ServerLoadStatus.Loaded;
    }
}
