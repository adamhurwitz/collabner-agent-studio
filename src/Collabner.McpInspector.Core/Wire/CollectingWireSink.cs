// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.McpInspector.Wire;

/// <summary>
/// Collects wire payloads in memory (used by the desktop app's Session view).
/// </summary>
public sealed class CollectingWireSink : IWirePayloadSink
{
    private readonly object _gate = new();
    private readonly List<WireMessage> _messages = [];

    public void Record(string direction, string payload)
    {
        lock (_gate)
        {
            _messages.Add(new WireMessage(direction, payload, DateTimeOffset.Now));
        }
    }

    /// <summary>Returns an immutable snapshot of the captured messages.</summary>
    public IReadOnlyList<WireMessage> Snapshot()
    {
        lock (_gate)
        {
            return _messages.ToArray();
        }
    }
}
