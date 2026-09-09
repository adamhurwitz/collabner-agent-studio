// Copyright © 2026 Collabner. All rights reserved.

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// User-configurable defaults for Collabner Agent Studio, persisted between runs.
/// </summary>
public sealed class StudioSettings
{
    /// <summary>Default <c>.mcp.json</c> path pre-filled on the Inspector and Agent tabs.</summary>
    public string? DefaultMcpConfigPath { get; set; }

    /// <summary>Base directory for the agent being built in the studio.</summary>
    public string? AgentDirectory { get; set; }

    /// <summary>Tool reasoning instruction shown in the Agent tab's Tool Reasoning box.</summary>
    public string ToolReasoning { get; set; } = DefaultPrompts.ToolReasoning;

    /// <summary>Whether the Tool Reasoning text is appended to the system prompt on each run.</summary>
    public bool AppendToolReasoning { get; set; }

    /// <summary>Directory scanned for skills (used by the Agent tab later).</summary>
    public string? SkillsDirectory { get; set; }

    /// <summary>Skill names the user has toggled off; excluded from the Copilot session.</summary>
    public List<string> DisabledSkills { get; set; } = [];

    /// <summary>Identifier of the model the agent should use.</summary>
    public string? CopilotModel { get; set; }

    /// <summary>Whether the agent surfaces the model's extended-thinking (reasoning) in the timeline.</summary>
    public bool ShowReasoning { get; set; } = true;

    /// <summary>Reasoning effort ("low", "medium", "high", "xhigh"); empty uses the model default.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>Optional GitHub token for Copilot auth. When empty, the signed-in Copilot CLI user is used.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>
    /// Base directory for Copilot runtime data (session state, config). Sets COPILOT_HOME on the
    /// spawned runtime. When empty, the runtime defaults to ~/.copilot.
    /// </summary>
    public string? CopilotBaseDirectory { get; set; }

    /// <summary>Whether the agent pauses before every tool call by default.</summary>
    public bool BreakOnToolCall { get; set; } = true;

    /// <summary>Whether the agent pauses before every skill invocation so its choice can be reviewed.</summary>
    public bool BreakOnSkill { get; set; }

    /// <summary>Maximum automatic turns before the agent loop is forced to stop.</summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>How long the client waits for a single agent turn (including paused tool calls) before it stops waiting, in minutes.</summary>
    public int RunTimeoutMinutes { get; set; } = 10;

    /// <summary>Environment variable consulted for a Copilot token when none is saved in settings.</summary>
    public const string GitHubTokenEnvVar = "COPILOT_GITHUB_TOKEN";

    /// <summary>
    /// Resolves the Copilot token, preferring the value saved in settings and falling back to the
    /// <see cref="GitHubTokenEnvVar"/> environment variable. Returns null when neither is set.
    /// </summary>
    public string? ResolveGitHubToken()
    {
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            return GitHubToken;
        }

        var fromEnv = Environment.GetEnvironmentVariable(GitHubTokenEnvVar);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    public StudioSettings Clone() => (StudioSettings)MemberwiseClone();
}
