// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.AgentStudio.Agent;

/// <summary>The kind of event captured during an agent run.</summary>
public enum AgentEventKind
{
    PromptSubmitted,
    SessionInfo,
    SystemPrompt,
    RequestToModel,
    ModelResponse,
    ToolCallRequested,
    ToolCallCompleted,
    Error,
    RunCompleted,
}

/// <summary>A single timestamped entry in an agent run's event timeline.</summary>
public sealed record AgentEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    AgentEventKind Kind,
    string Summary,
    string? Detail);

/// <summary>
/// Append-only capture of everything that happens during an agent run: the outgoing model
/// requests, model responses, and tool-call activity. Host-agnostic so the same recorder is
/// used by the desktop UI and a CLI.
/// </summary>
public sealed class AgentSessionRecorder
{
    private readonly List<AgentEvent> _events = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<AgentEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    /// <summary>Raised whenever a new event is recorded.</summary>
    public event Action? Changed;

    public Guid Add(AgentEventKind kind, string summary, string? detail = null)
    {
        var evt = new AgentEvent(Guid.NewGuid(), DateTimeOffset.Now, kind, summary, detail);
        lock (_sync)
        {
            _events.Add(evt);
        }

        Changed?.Invoke();
        return evt.Id;
    }

    /// <summary>Replaces the summary/detail of an existing event, e.g. to grow a streaming entry in place.</summary>
    public void Update(Guid id, string summary, string? detail = null)
    {
        var changed = false;
        lock (_sync)
        {
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].Id == id)
                {
                    _events[i] = _events[i] with { Summary = summary, Detail = detail };
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
        }

        Changed?.Invoke();
    }
}
