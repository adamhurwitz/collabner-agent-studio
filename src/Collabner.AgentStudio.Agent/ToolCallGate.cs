// Copyright © 2026 Collabner. All rights reserved.

using System.Collections.Concurrent;

namespace Collabner.AgentStudio.Agent;

/// <summary>How a paused tool call should be resolved.</summary>
public enum ToolDecisionKind
{
    /// <summary>Forward the call to the real MCP server.</summary>
    Passthrough,

    /// <summary>Return a user-supplied result without calling the server.</summary>
    Manual,

    /// <summary>Abort the tool call (and the turn).</summary>
    Abort,
}

/// <summary>The outcome the user (or policy) chose for a pending tool call.</summary>
public sealed record ToolDecision(ToolDecisionKind Kind, object? ManualResult = null)
{
    public static ToolDecision Passthrough() => new(ToolDecisionKind.Passthrough);

    public static ToolDecision Manual(object? result) => new(ToolDecisionKind.Manual, result);

    public static ToolDecision Abort() => new(ToolDecisionKind.Abort);
}

/// <summary>A tool call awaiting a decision at the gate.</summary>
public sealed class PendingToolCall
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string ServerName { get; init; }

    public required string ToolName { get; init; }

    public required string DisplayName { get; init; }

    public required string ArgumentsJson { get; init; }
}

/// <summary>
/// The human-in-the-loop gate. The agent loop awaits <see cref="WaitAsync"/> for each tool call;
/// the UI resolves it with a <see cref="ToolDecision"/>. When <see cref="BreakOnToolCall"/> is
/// false the gate auto-resolves to passthrough, making the gate a transparent decorator.
/// </summary>
public sealed class ToolCallGate
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ToolDecision>> _pending = new();
    private readonly List<PendingToolCall> _queue = [];
    private readonly Lock _sync = new();

    /// <summary>Whether the gate pauses before each tool call. Defaults to on.</summary>
    public bool BreakOnToolCall { get; set; } = true;

    /// <summary>Tool calls currently waiting for a decision.</summary>
    public IReadOnlyList<PendingToolCall> Queue
    {
        get
        {
            lock (_sync)
            {
                return _queue.ToArray();
            }
        }
    }

    /// <summary>Raised when the pending queue changes.</summary>
    public event Action? Changed;

    public async Task<ToolDecision> WaitAsync(PendingToolCall call, CancellationToken cancellationToken)
    {
        if (!BreakOnToolCall)
        {
            return ToolDecision.Passthrough();
        }

        var tcs = new TaskCompletionSource<ToolDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[call.Id] = tcs;
        lock (_sync)
        {
            _queue.Add(call);
        }

        Changed?.Invoke();

        await using var registration = cancellationToken.Register(() => tcs.TrySetResult(ToolDecision.Abort()));
        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(call.Id, out _);
            lock (_sync)
            {
                _queue.RemoveAll(c => c.Id == call.Id);
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Resolves a waiting tool call with the given decision.</summary>
    public void Resolve(Guid id, ToolDecision decision)
    {
        if (_pending.TryGetValue(id, out var tcs))
        {
            tcs.TrySetResult(decision);
        }
    }

    /// <summary>Aborts every pending tool call, e.g. when a run is cancelled.</summary>
    public void AbortAll()
    {
        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetResult(ToolDecision.Abort());
        }
    }
}
