// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.AgentStudio.Agent;

/// <summary>
/// A user-supplied replacement for a tool's advertised definition, used to experiment with how
/// the name, description, or parameter schema affect the model's tool choice. A null field keeps
/// the tool's original value; execution is always routed to the real underlying tool.
/// </summary>
public sealed class ToolOverride
{
    /// <summary>Replacement advertised name, or null to keep the original.</summary>
    public string? Name { get; init; }

    /// <summary>Replacement description, or null to keep the original.</summary>
    public string? Description { get; init; }

    /// <summary>Replacement JSON-schema object for the parameters, or null to keep the original.</summary>
    public string? ParametersJson { get; init; }
}
