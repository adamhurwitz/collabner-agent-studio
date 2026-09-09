// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.McpInspector.Wire;

/// <summary>
/// Receives wire payloads extracted from the MCP transport logs.
/// </summary>
public interface IWirePayloadSink
{
    void Record(string direction, string payload);
}
