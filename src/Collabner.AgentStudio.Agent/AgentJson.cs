// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Collabner.AgentStudio.Agent;

/// <summary>Shared JSON options for serializing agent events and tool payloads.</summary>
internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
