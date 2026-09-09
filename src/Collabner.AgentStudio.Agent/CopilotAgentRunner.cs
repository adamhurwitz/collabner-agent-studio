// Copyright © 2026 Collabner. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Collabner.AgentStudio.Agent;

/// <summary>Inputs for a single Copilot agent run.</summary>
public sealed class CopilotRunOptions
{
    public required string Prompt { get; init; }

    public string? SystemPrompt { get; init; }

    /// <summary>When true the system prompt replaces Copilot's built-in prompt; otherwise it is appended.</summary>
    public bool ReplaceSystemPrompt { get; init; }

    public string? Model { get; init; }

    /// <summary>When set, requests readable extended-thinking output (reasoning summary + streaming deltas).</summary>
    public bool ShowReasoning { get; init; }

    /// <summary>Optional reasoning effort ("low", "medium", "high", "xhigh"); blank uses the model default.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>Optional GitHub token; when omitted the signed-in Copilot CLI user is used.</summary>
    public string? GitHubToken { get; init; }

    /// <summary>Optional base directory for Copilot runtime data (sets COPILOT_HOME); blank uses ~/.copilot.</summary>
    public string? BaseDirectory { get; init; }

    /// <summary>Directories scanned for skills (each contains subfolders with a SKILL.md); registered with the session.</summary>
    public IReadOnlyList<string>? SkillDirectories { get; init; }

    /// <summary>Skill names (from SKILL.md frontmatter or folder name) to disable for the session.</summary>
    public IReadOnlyList<string>? DisabledSkills { get; init; }

    /// <summary>When set, pauses each skill invocation (via the pre-tool-use hook) for human review.</summary>
    public SkillCallGate? SkillGate { get; init; }

    /// <summary>How long to wait for the turn to reach idle before the client-side wait times out (does not abort the runtime).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Intercepting tool wrappers exposed to the Copilot runtime.</summary>
    public required IReadOnlyList<AIFunction> Tools { get; init; }
}

/// <summary>The result of a single run, describing how its Copilot session was fulfilled.</summary>
/// <param name="SessionReused">True when the existing session was continued; false when a new one was created.</param>
/// <param name="SessionTurns">How many prompts have been sent on the current session, including this one.</param>
/// <param name="SessionId">The Copilot runtime's id for the session, or null if none is active.</param>
public sealed record RunOutcome(bool SessionReused, int SessionTurns, string? SessionId);

/// <summary>
/// The session-defining inputs. Two runs with an equal signature can share one Copilot session
/// (preserving conversation context and prompt caching); any difference forces a new session
/// because tools, model, system prompt, and reasoning are fixed when the session is created.
/// </summary>
internal sealed record CopilotSessionSignature(
    string? Model,
    string? SystemPrompt,
    bool ReplaceSystemPrompt,
    bool ShowReasoning,
    string? ReasoningEffort,
    string ToolsHash,
    string SkillsKey)
{
    public static CopilotSessionSignature From(CopilotRunOptions options) => new(
        string.IsNullOrWhiteSpace(options.Model) ? null : options.Model,
        string.IsNullOrWhiteSpace(options.SystemPrompt) ? null : options.SystemPrompt,
        options.ReplaceSystemPrompt,
        options.ShowReasoning,
        string.IsNullOrWhiteSpace(options.ReasoningEffort) ? null : options.ReasoningEffort,
        HashTools(options.Tools),
        SkillsKeyOf(options));

    private static string SkillsKeyOf(CopilotRunOptions options)
    {
        var dirs = options.SkillDirectories is { Count: > 0 } d ? string.Join('\u001f', d) : string.Empty;
        var disabled = options.DisabledSkills is { Count: > 0 } x ? string.Join('\u001f', x) : string.Empty;
        return $"{dirs}\u001e{disabled}";
    }

    private static string HashTools(IReadOnlyList<AIFunction> tools)
    {
        var sb = new StringBuilder();
        foreach (var tool in tools)
        {
            sb.Append(tool.Name).Append('\u001f')
              .Append(tool.Description).Append('\u001f')
              .Append(tool.JsonSchema.GetRawText()).Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>
/// Runs a prompt against the GitHub Copilot runtime via <see cref="CopilotClient"/>. The MCP
/// tools are passed as invocable <see cref="AIFunction"/> wrappers, so the runtime calls back
/// into this process (through the gate) rather than owning the MCP connections itself. Session
/// events are streamed into the recorder for the timeline.
/// <para>
/// The client and session are kept alive between runs so consecutive prompts continue the same
/// conversation. A new session is created only when the session-defining inputs change
/// (see <see cref="CopilotSessionSignature"/>) or after a cancelled/failed turn.
/// </para>
/// </summary>
public sealed class CopilotAgentRunner : IAsyncDisposable
{
    private CopilotClient? _client;
    private string? _clientKey;
    private CopilotSession? _session;
    private CopilotSessionSignature? _signature;
    private int _turns;

    // The skill pre-tool-use hook is registered once per session but must act on the current turn's
    // recorder and cancellation token, so they are refreshed at the start of every run.
    private AgentSessionRecorder? _turnRecorder;
    private CancellationToken _turnCancellation;

    /// <summary>Whether a live session is currently held (i.e. the next run may reuse it).</summary>
    public bool HasSession => _session is not null;

    /// <summary>
    /// Predicts whether running <paramref name="options"/> now would start a fresh session
    /// rather than reuse the current one (because no session is held or its inputs changed).
    /// </summary>
    public bool WouldStartNewSession(CopilotRunOptions options)
        => _session is null || _signature != CopilotSessionSignature.From(options);

    /// <summary>The Copilot runtime's id for the current session, or null when none is active.</summary>
    public string? SessionId => _session?.SessionId;

    public async Task<RunOutcome> RunAsync(
        CopilotRunOptions options,
        AgentSessionRecorder recorder,
        CancellationToken cancellationToken)
    {
        recorder.Add(AgentEventKind.PromptSubmitted, options.Prompt);

        _turnRecorder = recorder;
        _turnCancellation = cancellationToken;

        var clientKey = $"{options.GitHubToken}\u001f{options.BaseDirectory}";
        var signature = CopilotSessionSignature.From(options);

        try
        {
            if (_client is null || _clientKey != clientKey)
            {
                await DisposeSessionAsync();
                await DisposeClientAsync();
                _client = new CopilotClient(BuildClientOptions(options));
                _clientKey = clientKey;
            }

            bool reused;
            if (_session is not null && _signature == signature)
            {
                reused = true;
                _turns++;
            }
            else
            {
                await DisposeSessionAsync();
                _session = await _client.CreateSessionAsync(BuildSessionConfig(options));
                _signature = signature;
                _turns = 1;
                reused = false;
            }

            recorder.Add(
                AgentEventKind.SessionInfo,
                reused ? $"Reusing session {_session.SessionId} — turn {_turns}" : $"New session created — {_session.SessionId}",
                reused ? null : "Session inputs changed (model, system prompt, tools, or reasoning), so a fresh session was started.");

            var reasoning = new ReasoningStream(recorder);
            using var subscription = _session.On<SessionEvent>(evt => Record(recorder, evt, reasoning));
            using var cancelReg = cancellationToken.Register(() => _ = _session.AbortAsync());

            var reply = await _session.SendAndWaitAsync(new MessageOptions { Prompt = options.Prompt }, options.Timeout, cancellationToken);
            reasoning.FlushPending();
            recorder.Add(AgentEventKind.RunCompleted, reply?.Data.Content ?? "(done)");
            return new RunOutcome(reused, _turns, _session.SessionId);
        }
        catch (OperationCanceledException)
        {
            recorder.Add(AgentEventKind.Error, "Run cancelled.");
            // A cancelled turn can leave the session mid-generation; drop it so the next run starts clean.
            await DisposeSessionAsync();
            return new RunOutcome(false, 0, null);
        }
        catch (Exception ex)
        {
            recorder.Add(AgentEventKind.Error, ex.Message, ex.ToString());
            await DisposeSessionAsync();
            return new RunOutcome(false, 0, null);
        }
    }

    /// <summary>Discards the current session so the next run starts a brand-new conversation.</summary>
    public async Task ResetSessionAsync() => await DisposeSessionAsync();

    /// <summary>
    /// Pre-tool-use hook: skill invocations arrive as the built-in <c>skill</c> tool. When the gate
    /// is armed this blocks until the user allows or blocks the skill; other tools pass straight
    /// through (they are gated separately by their <see cref="InterceptingTool"/> wrappers).
    /// </summary>
    private async Task<PreToolUseHookOutput?> HandleSkillPreToolUseAsync(PreToolUseHookInput input, SkillCallGate gate)
    {
        if (!string.Equals(input.ToolName, "skill", StringComparison.OrdinalIgnoreCase))
        {
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }

        var skillName = TryGetSkillName(input.ToolArgs);
        var argsJson = input.ToolArgs?.GetRawText() ?? "{}";
        _turnRecorder?.Add(AgentEventKind.ToolCallRequested, $"skill: {skillName}", argsJson);

        var allow = await gate.WaitAsync(
            new PendingSkillCall { SkillName = skillName, ArgumentsJson = argsJson },
            _turnCancellation);

        if (allow)
        {
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }

        _turnRecorder?.Add(AgentEventKind.Error, $"skill '{skillName}' blocked before execution", null);
        return new PreToolUseHookOutput
        {
            PermissionDecision = "deny",
            PermissionDecisionReason = $"Skill '{skillName}' was blocked by the user before it ran.",
        };
    }

    private static string TryGetSkillName(JsonElement? args)
    {
        if (args is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty("skill", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? "(unknown)";
        }

        return "(unknown)";
    }

    private static CopilotClientOptions BuildClientOptions(CopilotRunOptions options)
    {
        var clientOptions = new CopilotClientOptions();
        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
        {
            clientOptions.GitHubToken = options.GitHubToken;
        }

        if (!string.IsNullOrWhiteSpace(options.BaseDirectory))
        {
            clientOptions.BaseDirectory = options.BaseDirectory;
        }

        return clientOptions;
    }

    private SessionConfig BuildSessionConfig(CopilotRunOptions options)
    {
        var sessionConfig = new SessionConfig
        {
            Tools = [.. options.Tools],
            // The runtime's own permission prompt is auto-approved; our gate provides the pause.
            OnPermissionRequest = PermissionHandler.ApproveAll,
        };

        if (options.SkillGate is { } skillGate)
        {
            // Skills run inside the runtime, so the only awaitable pause point is the pre-tool-use hook.
            sessionConfig.Hooks = new SessionHooks
            {
                OnPreToolUse = (input, _) => HandleSkillPreToolUseAsync(input, skillGate),
            };
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            sessionConfig.Model = options.Model;
        }

        if (options.ShowReasoning)
        {
            // Readable thinking text (for OpenAI models) plus incremental reasoning deltas.
            sessionConfig.ReasoningSummary = ReasoningSummary.Detailed;
            sessionConfig.Streaming = true;
        }

        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort))
        {
            sessionConfig.ReasoningEffort = options.ReasoningEffort;
        }

        if (options.SkillDirectories is { Count: > 0 } skillDirectories)
        {
            sessionConfig.SkillDirectories = [.. skillDirectories];
        }

        if (options.DisabledSkills is { Count: > 0 } disabledSkills)
        {
            sessionConfig.DisabledSkills = [.. disabledSkills];
        }

        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            sessionConfig.SystemMessage = new SystemMessageConfig
            {
                Mode = options.ReplaceSystemPrompt ? SystemMessageMode.Replace : SystemMessageMode.Append,
                Content = options.SystemPrompt,
            };
        }

        return sessionConfig;
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        _signature = null;
        _turns = 0;
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        _clientKey = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSessionAsync();
        await DisposeClientAsync();
    }

    private static void Record(AgentSessionRecorder recorder, SessionEvent evt, ReasoningStream reasoning)
    {
        switch (evt)
        {
            case SystemMessageEvent system:
                recorder.Add(AgentEventKind.SystemPrompt, $"[{system.Data.Role}] {Trunc(system.Data.Content)}", system.Data.Content);
                break;
            case UserMessageEvent user:
                var sent = string.IsNullOrEmpty(user.Data.TransformedContent) ? user.Data.Content : user.Data.TransformedContent;
                recorder.Add(AgentEventKind.RequestToModel, Trunc(sent), sent);
                break;
            case AssistantReasoningDeltaEvent delta:
                reasoning.AppendDelta(delta.Data.ReasoningId, delta.Data.DeltaContent);
                break;
            case AssistantReasoningEvent reasoningEvt:
                reasoning.Complete(reasoningEvt.Data.ReasoningId, reasoningEvt.Data.Content);
                break;
            case AssistantMessageEvent message:
                reasoning.FlushPending();
                recorder.Add(AgentEventKind.ModelResponse, Trunc(message.Data.Content), message.Data.Content);
                break;
            case SessionSkillsLoadedEvent skills:
                var names = string.Join(", ", skills.Data.Skills.Select(s => s.Name));
                recorder.Add(AgentEventKind.SessionInfo, $"Skills loaded ({skills.Data.Skills.Length})", names);
                break;
            case SkillInvokedEvent skill:
                reasoning.FlushPending();
                recorder.Add(
                    AgentEventKind.ToolCallCompleted,
                    $"skill invoked: {skill.Data.Name}",
                    $"trigger: {skill.Data.Trigger?.Value}\nsource: {skill.Data.Source}");
                break;
            case ToolExecutionStartEvent tool:
                reasoning.FlushPending();
                // Skill calls are already surfaced by the pre-tool-use hook and SkillInvokedEvent.
                if (!string.Equals(tool.Data.ToolName, "skill", StringComparison.OrdinalIgnoreCase))
                {
                    recorder.Add(AgentEventKind.ToolCallRequested, $"runtime: {tool.Data.ToolName}");
                }

                break;
            case SessionErrorEvent error:
                recorder.Add(AgentEventKind.Error, error.Data.Message);
                break;
        }
    }

    private static string Trunc(string? text)
        => string.IsNullOrEmpty(text) ? "(empty)" : text.Length <= 140 ? text : text[..140] + "…";

    /// <summary>
    /// Streams <c>assistant.reasoning_delta</c> chunks into a single, in-place-updated timeline entry
    /// per reasoning block so extended thinking shows as it arrives (throttled to limit re-renders),
    /// and reconciles with the consolidated <c>assistant.reasoning</c> event when it lands.
    /// </summary>
    private sealed class ReasoningStream(AgentSessionRecorder recorder)
    {
        private static readonly long UiIntervalTicks = TimeSpan.FromMilliseconds(100).Ticks;
        private readonly Dictionary<string, Block> _blocks = [];
        private readonly Lock _sync = new();

        private sealed class Block
        {
            public Guid EventId;
            public readonly StringBuilder Text = new();
            public long LastPushTicks;
            public int PushedLength;
        }

        public void AppendDelta(string reasoningId, string? delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            Guid id;
            string text;
            lock (_sync)
            {
                if (!_blocks.TryGetValue(reasoningId, out var block))
                {
                    block = new Block { EventId = recorder.Add(AgentEventKind.ModelResponse, "[reasoning] …") };
                    _blocks[reasoningId] = block;
                }

                block.Text.Append(delta);

                var now = DateTimeOffset.UtcNow.Ticks;
                if (block.PushedLength > 0 && now - block.LastPushTicks < UiIntervalTicks)
                {
                    return;
                }

                block.LastPushTicks = now;
                block.PushedLength = block.Text.Length;
                id = block.EventId;
                text = block.Text.ToString();
            }

            recorder.Update(id, $"[reasoning] {Trunc(text)}", text);
        }

        public void Complete(string reasoningId, string? content)
        {
            Guid id;
            string text;
            var isNew = false;
            lock (_sync)
            {
                if (_blocks.Remove(reasoningId, out var block))
                {
                    if (!string.IsNullOrEmpty(content))
                    {
                        block.Text.Clear();
                        block.Text.Append(content);
                    }

                    id = block.EventId;
                    text = block.Text.ToString();
                }
                else
                {
                    id = Guid.Empty;
                    text = content ?? string.Empty;
                    isNew = true;
                }
            }

            if (text.Length == 0)
            {
                return;
            }

            if (isNew)
            {
                recorder.Add(AgentEventKind.ModelResponse, $"[reasoning] {Trunc(text)}", text);
            }
            else
            {
                recorder.Update(id, $"[reasoning] {Trunc(text)}", text);
            }
        }

        /// <summary>Forces any throttled reasoning text to the UI before a message/tool row or at run end.</summary>
        public void FlushPending()
        {
            List<(Guid Id, string Text)> updates = [];
            lock (_sync)
            {
                foreach (var block in _blocks.Values)
                {
                    if (block.Text.Length > block.PushedLength)
                    {
                        block.PushedLength = block.Text.Length;
                        updates.Add((block.EventId, block.Text.ToString()));
                    }
                }
            }

            foreach (var (id, text) in updates)
            {
                recorder.Update(id, $"[reasoning] {Trunc(text)}", text);
            }
        }
    }
}
