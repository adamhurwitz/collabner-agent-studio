// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Collabner.McpInspector.Wire;

/// <summary>
/// Parses MCP transport log messages and forwards the embedded JSON-RPC payloads to a sink.
/// </summary>
internal sealed class WirePayloadLogger(string categoryName, IWirePayloadSink sink) : ILogger
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
    };

    private string? _lastLabel;
    private string? _lastPayload;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (!categoryName.StartsWith("ModelContextProtocol", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!IsWirePayloadMessage(message) || !TryGetDirection(message, out var label))
        {
            return;
        }

        if (!TryExtractJsonPayload(message, out var payload))
        {
            return;
        }

        if (string.Equals(_lastLabel, label, StringComparison.Ordinal) &&
            string.Equals(_lastPayload, payload, StringComparison.Ordinal))
        {
            return;
        }

        _lastLabel = label;
        _lastPayload = payload;

        sink.Record(label, payload);
    }

    private static bool IsWirePayloadMessage(string message)
    {
        return message.Contains("transport sending message", StringComparison.OrdinalIgnoreCase)
            || message.Contains("transport received message", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDirection(string message, out string label)
    {
        label = string.Empty;

        if (message.Contains("response", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("received", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("receive", StringComparison.OrdinalIgnoreCase))
        {
            label = "RESPONSE";
            return true;
        }

        if (message.Contains("request", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("sending", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("send", StringComparison.OrdinalIgnoreCase))
        {
            label = "REQUEST";
            return true;
        }

        return false;
    }

    private static bool TryExtractJsonPayload(string message, out string payload)
    {
        payload = string.Empty;

        if (!TrySliceFirstJson(message, out var candidate))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(candidate);
            payload = JsonSerializer.Serialize(document, PrettyJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TrySliceFirstJson(string message, out string json)
    {
        json = string.Empty;

        var objectStart = message.IndexOf('{');
        var arrayStart = message.IndexOf('[');

        int start;
        if (objectStart < 0 && arrayStart < 0)
        {
            return false;
        }

        if (objectStart < 0)
        {
            start = arrayStart;
        }
        else if (arrayStart < 0)
        {
            start = objectStart;
        }
        else
        {
            start = Math.Min(objectStart, arrayStart);
        }

        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var i = start; i < message.Length; i++)
        {
            var ch = message[i];

            if (inString)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{' || ch == '[')
            {
                depth++;
                continue;
            }

            if (ch == '}' || ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    json = message.Substring(start, i - start + 1);
                    return true;
                }
            }
        }

        return false;
    }
}
