// Copyright © 2026 Collabner. All rights reserved.

using System.Collections.Concurrent;

namespace Collabner.AgentStudio.Agent;

/// <summary>A skill invocation awaiting a decision at the gate.</summary>
public sealed class PendingSkillCall
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string SkillName { get; init; }

    public required string ArgumentsJson { get; init; }
}

/// <summary>
/// The human-in-the-loop gate for skill invocations. Skills run inside the Copilot runtime, so the
/// pause is applied from the <c>OnPreToolUse</c> hook: the hook awaits <see cref="WaitAsync"/>,
/// which blocks the skill until the UI allows or blocks it. When <see cref="BreakOnSkill"/> is
/// false the gate auto-allows, making it transparent.
/// </summary>
public sealed class SkillCallGate
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _pending = new();
    private readonly List<PendingSkillCall> _queue = [];
    private readonly Lock _sync = new();

    /// <summary>Whether the gate pauses before each skill invocation. Defaults to off.</summary>
    public bool BreakOnSkill { get; set; }

    /// <summary>Skill invocations currently waiting for a decision.</summary>
    public IReadOnlyList<PendingSkillCall> Queue
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

    /// <summary>Blocks until the UI decides; returns true to allow the skill, false to block it.</summary>
    public async Task<bool> WaitAsync(PendingSkillCall call, CancellationToken cancellationToken)
    {
        if (!BreakOnSkill)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[call.Id] = tcs;
        lock (_sync)
        {
            _queue.Add(call);
        }

        Changed?.Invoke();

        await using var registration = cancellationToken.Register(() => tcs.TrySetResult(false));
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

    /// <summary>Resolves a waiting skill invocation: allow it through, or block it.</summary>
    public void Resolve(Guid id, bool allow)
    {
        if (_pending.TryGetValue(id, out var tcs))
        {
            tcs.TrySetResult(allow);
        }
    }

    /// <summary>Blocks every pending skill invocation, e.g. when a run is cancelled.</summary>
    public void AbortAll()
    {
        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetResult(false);
        }
    }
}
