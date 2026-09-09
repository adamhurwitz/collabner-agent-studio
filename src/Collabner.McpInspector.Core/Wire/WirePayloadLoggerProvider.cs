// Copyright © 2026 Collabner. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Collabner.McpInspector.Wire;

/// <summary>
/// An <see cref="ILoggerProvider"/> that forwards MCP transport wire payloads to an
/// <see cref="IWirePayloadSink"/>.
/// </summary>
public sealed class WirePayloadLoggerProvider(IWirePayloadSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new WirePayloadLogger(categoryName, sink);

    public void Dispose()
    {
    }
}
