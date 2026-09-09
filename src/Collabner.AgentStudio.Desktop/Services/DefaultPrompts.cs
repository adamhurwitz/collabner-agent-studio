// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// Built-in default text for the Tool Reasoning box, used until the user customises it.
/// </summary>
public static class DefaultPrompts
{
    /// <summary>Default reasoning instruction seeded into the Agent tab's Tool Reasoning box.</summary>
    public const string ToolReasoning =
        "Before every tool call, state in one sentence why you are calling this tool and what you "
        + "expect it to return.";
}
