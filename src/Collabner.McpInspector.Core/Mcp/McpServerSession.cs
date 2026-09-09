// Copyright © 2026 Collabner. All rights reserved.

using Collabner.McpInspector.Configuration;
using Collabner.McpInspector.Wire;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Collabner.McpInspector.Mcp;

/// <summary>
/// A live connection to a single MCP server. Unlike a one-shot inspection, the underlying
/// client is kept alive so the UI can discover the server's tools, resources, and prompts
/// and then send new invocations — every JSON-RPC payload is captured in the shared wire
/// session so the Session view updates as requests are sent.
/// </summary>
public sealed class McpServerSession : IAsyncDisposable
{
    private readonly McpClient? _client;
    private readonly CollectingWireSink _sink;
    private readonly ILoggerFactory _loggerFactory;

    private McpServerSession(
        string serverName,
        McpClient? client,
        CollectingWireSink sink,
        ILoggerFactory loggerFactory,
        IReadOnlyList<Tool> tools,
        IReadOnlyList<McpClientTool> aiTools,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<Prompt> prompts,
        string? error)
    {
        ServerName = serverName;
        _client = client;
        _sink = sink;
        _loggerFactory = loggerFactory;
        Tools = tools;
        AITools = aiTools;
        Resources = resources;
        Prompts = prompts;
        Error = error;
    }

    /// <summary>The configured name of the server.</summary>
    public string ServerName { get; }

    /// <summary>The tools advertised by the server.</summary>
    public IReadOnlyList<Tool> Tools { get; }

    /// <summary>The same tools projected as invokable <see cref="AIFunction"/> instances.</summary>
    public IReadOnlyList<McpClientTool> AITools { get; }

    /// <summary>The resources advertised by the server.</summary>
    public IReadOnlyList<Resource> Resources { get; }

    /// <summary>The prompts advertised by the server.</summary>
    public IReadOnlyList<Prompt> Prompts { get; }

    /// <summary>An error message if the connection failed; otherwise <c>null</c>.</summary>
    public string? Error { get; }

    /// <summary>Whether the session holds a usable, connected client.</summary>
    public bool IsConnected => _client is not null && Error is null;

    /// <summary>An immutable snapshot of every wire payload captured so far.</summary>
    public IReadOnlyList<WireMessage> Session => _sink.Snapshot();

    /// <summary>
    /// Connects to the server and lists its tools, resources, and prompts. Discovery calls
    /// for capabilities a server does not implement are tolerated and simply yield empty lists.
    /// </summary>
    public static async Task<McpServerSession> ConnectAsync(string serverName, McpServerConfig config)
    {
        var sink = new CollectingWireSink();
        var loggerFactory = WireLogging.CreateWireLoggerFactory(sink);

        try
        {
            var client = await McpClientFactory.CreateClientAsync(serverName, config, loggerFactory);

            var tools = await ListToolsAsync(client);
            var aiTools = await ListAIToolsAsync(client);
            var resources = await ListResourcesAsync(client);
            var prompts = await ListPromptsAsync(client);

            return new McpServerSession(serverName, client, sink, loggerFactory, tools, aiTools, resources, prompts, null);
        }
        catch (Exception ex)
        {
            loggerFactory.Dispose();
            return new McpServerSession(serverName, null, sink, loggerFactory, [], [], [], [], ex.Message);
        }
    }

    /// <summary>Invokes a tool with the supplied arguments.</summary>
    public ValueTask<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return _client!.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
    }

    /// <summary>Reads a resource by its URI.</summary>
    public ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return _client!.ReadResourceAsync(uri, cancellationToken: cancellationToken);
    }

    /// <summary>Gets a prompt with the supplied arguments.</summary>
    public ValueTask<GetPromptResult> GetPromptAsync(
        string promptName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return _client!.GetPromptAsync(promptName, arguments, cancellationToken: cancellationToken);
    }

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException($"MCP server '{ServerName}' is not connected.");
        }
    }

    private static async Task<IReadOnlyList<Tool>> ListToolsAsync(McpClient client)
    {
        try
        {
            return [.. (await client.ListToolsAsync(new ListToolsRequestParams())).Tools];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<McpClientTool>> ListAIToolsAsync(McpClient client)
    {
        try
        {
            return [.. await client.ListToolsAsync()];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<Resource>> ListResourcesAsync(McpClient client)
    {
        try
        {
            return [.. (await client.ListResourcesAsync(new ListResourcesRequestParams())).Resources];
        }
        catch
        {
            // A server that does not advertise this capability answers with an error; the wire
            // log still records the exchange, so surface an empty list to the UI here.
            return [];
        }
    }

    private static async Task<IReadOnlyList<Prompt>> ListPromptsAsync(McpClient client)
    {
        try
        {
            return [.. (await client.ListPromptsAsync(new ListPromptsRequestParams())).Prompts];
        }
        catch
        {
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _loggerFactory.Dispose();
    }
}
