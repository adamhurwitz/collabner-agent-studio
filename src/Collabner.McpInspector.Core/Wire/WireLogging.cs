// Copyright © 2026 Collabner. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Collabner.McpInspector.Wire;

/// <summary>
/// Helpers for building an <see cref="ILoggerFactory"/> that captures MCP wire traffic.
/// </summary>
public static class WireLogging
{
    /// <summary>
    /// Creates a logger factory that traces MCP transport payloads into the supplied sink.
    /// </summary>
    public static ILoggerFactory CreateWireLoggerFactory(IWirePayloadSink sink)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new WirePayloadLoggerProvider(sink));
        });
    }
}
