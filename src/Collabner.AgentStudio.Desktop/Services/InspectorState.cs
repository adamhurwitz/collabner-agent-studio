// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Configuration;
using Collabner.McpInspector.Mcp;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// Holds the currently loaded <c>.mcp.json</c> configuration and the resulting live server
/// sessions for the desktop UI. Scoped per Blazor circuit; disposes the underlying MCP
/// connections when reloaded or when the circuit ends.
/// </summary>
public sealed class InspectorState : IAsyncDisposable
{
    private readonly List<ServerInspectionView> _servers = [];

    public string? ConfigPath { get; private set; }

    public IReadOnlyList<ServerInspectionView> Servers => _servers;

    public string? LoadError { get; private set; }

    public bool IsLoading { get; private set; }

    /// <summary>
    /// Raised whenever the loading state changes so the UI can re-render incrementally.
    /// </summary>
    public event Func<Task>? Changed;

    /// <summary>
    /// Loads the given <c>.mcp.json</c> file and connects to every configured MCP server,
    /// adding each server tab up front and inspecting them one at a time so the UI stays
    /// responsive while later servers are still connecting.
    /// </summary>
    public async Task LoadAsync(string path)
    {
        IsLoading = true;
        LoadError = null;
        await DisposeServersAsync();
        await NotifyAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                LoadError = "Enter a path to an .mcp.json file.";
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                LoadError = $"File not found: {fullPath}";
                return;
            }

            var config = await McpConfig.ReadFileAsync(fullPath);
            var servers = McpConfig.GetServers(config);
            if (servers.Count == 0)
            {
                LoadError = $"No MCP servers were found in {fullPath}.";
                return;
            }

            ConfigPath = fullPath;

            // Add every server tab immediately so the user can see and navigate them
            // before any inspection has finished.
            foreach (var entry in servers)
            {
                _servers.Add(new ServerInspectionView(entry.Key));
            }

            await NotifyAsync();

            // Inspect servers one at a time, surfacing each result as soon as it lands.
            var index = 0;
            foreach (var entry in servers)
            {
                var view = _servers[index++];

                view.Status = ServerLoadStatus.Loading;
                await NotifyAsync();

                var session = await McpServerSession.ConnectAsync(entry.Key, entry.Value);
                view.Complete(session);
                await NotifyAsync();
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
            await NotifyAsync();
        }
    }

    private Task NotifyAsync()
        => Changed?.Invoke() ?? Task.CompletedTask;

    private async Task DisposeServersAsync()
    {
        foreach (var server in _servers)
        {
            if (server.Connection is not null)
            {
                await server.Connection.DisposeAsync();
            }
        }

        _servers.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeServersAsync();
    }
}
