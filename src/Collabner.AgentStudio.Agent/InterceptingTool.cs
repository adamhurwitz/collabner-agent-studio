// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Collabner.AgentStudio.Agent;

/// <summary>
/// Wraps a real MCP tool (an <see cref="AIFunction"/>) so every invocation passes through the
/// <see cref="ToolCallGate"/>. The advertised name, description, and schema can be overridden to
/// experiment with the model's tool choice; execution is always routed to the real server (or
/// replaced by a user value at the gate), regardless of the advertised definition.
/// </summary>
public sealed class InterceptingTool : DelegatingAIFunction
{
    private readonly string _serverName;
    private readonly string _displayName;
    private readonly string? _descriptionOverride;
    private readonly JsonElement? _schemaOverride;
    private readonly ToolCallGate _gate;
    private readonly AgentSessionRecorder _recorder;

    public InterceptingTool(
        AIFunction inner,
        string serverName,
        string displayName,
        ToolCallGate gate,
        AgentSessionRecorder recorder,
        string? descriptionOverride = null,
        JsonElement? schemaOverride = null)
        : base(inner)
    {
        _serverName = serverName;
        _displayName = displayName;
        _descriptionOverride = descriptionOverride;
        _schemaOverride = schemaOverride;
        _gate = gate;
        _recorder = recorder;
    }

    public override string Name => _displayName;

    public override string Description => _descriptionOverride ?? base.Description;

    public override JsonElement JsonSchema => _schemaOverride ?? base.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var argsJson = SerializeArguments(arguments);
        _recorder.Add(AgentEventKind.ToolCallRequested, _displayName, argsJson);

        var pending = new PendingToolCall
        {
            ServerName = _serverName,
            ToolName = InnerFunction.Name,
            DisplayName = _displayName,
            ArgumentsJson = argsJson,
        };

        var decision = await _gate.WaitAsync(pending, cancellationToken);

        switch (decision.Kind)
        {
            case ToolDecisionKind.Manual:
                _recorder.Add(AgentEventKind.ToolCallCompleted, $"{_displayName} (manual)", Describe(decision.ManualResult));
                return decision.ManualResult;

            case ToolDecisionKind.Abort:
                _recorder.Add(AgentEventKind.Error, $"{_displayName} aborted", null);
                throw new OperationCanceledException($"Tool call '{_displayName}' was aborted.");

            default:
                var result = await base.InvokeCoreAsync(arguments, cancellationToken);
                _recorder.Add(AgentEventKind.ToolCallCompleted, $"{_displayName} (real)", Describe(result));
                return result;
        }
    }

    private static string SerializeArguments(AIFunctionArguments arguments)
    {
        var map = new Dictionary<string, object?>(arguments);
        return JsonSerializer.Serialize(map, AgentJson.Options);
    }

    private static string? Describe(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, AgentJson.Options);
}
