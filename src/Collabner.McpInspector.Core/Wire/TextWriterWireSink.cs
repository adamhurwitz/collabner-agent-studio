// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.McpInspector.Wire;

/// <summary>
/// Writes wire payloads to a <see cref="TextWriter"/> (used by the console app's
/// <c>--trace-wire</c> mode).
/// </summary>
public sealed class TextWriterWireSink(TextWriter writer) : IWirePayloadSink
{
    public void Record(string direction, string payload)
    {
        writer.WriteLine($"WIRE {direction}");
        writer.WriteLine(payload);
        writer.WriteLine();
    }
}
