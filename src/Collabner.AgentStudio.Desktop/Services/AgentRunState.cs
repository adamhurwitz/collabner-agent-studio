// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;
using System.Text.RegularExpressions;
using Collabner.AgentStudio.Agent;
using Collabner.McpInspector.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>A loaded MCP server available to the agent.</summary>
public sealed class AgentServerView
{
    public required string Name { get; init; }

    public McpServerSession? Session { get; set; }

    public bool IsConnected => Session?.IsConnected ?? false;

    public string? Error => Session?.Error;

    public int ToolCount => Session?.AITools.Count ?? 0;
}

/// <summary>Who authored a chat message in the agent conversation.</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>A skill discovered under the configured skills directory.</summary>
public sealed class SkillView
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Path { get; init; }

    public required string Content { get; set; }
}

/// <summary>A single bubble in the agent chat history.</summary>
public sealed class ChatMessage
{
    public required ChatRole Role { get; init; }

    public required string Text { get; set; }

    public bool IsError { get; init; }

    /// <summary>When true, this entry is a horizontal separator marking the start of a new session.</summary>
    public bool IsSessionDivider { get; init; }
}

/// <summary>
/// An editable copy of one loaded tool's advertised definition. The user tweaks the name,
/// description, or parameter schema; the originals are retained so edits can be detected and reset.
/// </summary>
public sealed class ToolEditModel
{
    public required string ServerName { get; init; }

    public required string OriginalName { get; init; }

    public required string OriginalDescription { get; init; }

    public required string OriginalParametersJson { get; init; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ParametersJson { get; set; } = string.Empty;

    /// <summary>Validation message for the current edits, or null when valid.</summary>
    public string? Error { get; set; }

    public bool IsModified =>
        Name != OriginalName ||
        Description != OriginalDescription ||
        ParametersJson.Trim() != OriginalParametersJson.Trim();

    public void ResetToOriginal()
    {
        Name = OriginalName;
        Description = OriginalDescription;
        ParametersJson = OriginalParametersJson;
        Error = null;
    }
}

/// <summary>
/// Per-circuit state for the Agent tab: holds the configuration, the loaded MCP sessions, the
/// event recorder, and the tool-call gate, and drives a run in the background so the UI can
/// keep rendering (and resolve gated tool calls) while the agent loop is suspended.
/// </summary>
public sealed class AgentRunState : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly CopilotAgentRunner _runner = new();
    private readonly List<AgentServerView> _servers = [];
    private readonly List<ChatMessage> _chat = [];
    private readonly List<CopilotModelInfo> _models = [];
    private readonly List<ToolEditModel> _toolEdits = [];
    private readonly List<SkillView> _skills = [];
    private string _systemPrompt;
    private string? _toolSkillEdit;
    private string _toolReasoning;
    private bool _appendToolReasoning;
    private string? _selectedModelId;
    private string? _reasoningEffort;
    private string? _lastPrompt;
    private bool _modelsLoaded;
    private int _processedEvents;
    private string? _systemPromptSummary;
    private CancellationTokenSource? _cts;

    /// <summary>File in the agent directory that holds the editable system prompt.</summary>
    private const string SystemPromptFileName = "systemprompt.md";

    public AgentRunState(SettingsService settings)
    {
        _settings = settings;

        // System prompt and skills are not read here: they stay empty until the user
        // clicks Load on the Project page, which triggers the directory scan explicitly.
        _systemPrompt = string.Empty;
        _toolReasoning = settings.Current.ToolReasoning;
        _appendToolReasoning = settings.Current.AppendToolReasoning;
        _selectedModelId = settings.Current.CopilotModel;
        _reasoningEffort = settings.Current.ReasoningEffort;
        SystemPromptOverride = false;
        McpConfigPath = settings.Current.DefaultMcpConfigPath ?? ".mcp.json";
        Gate.BreakOnToolCall = settings.Current.BreakOnToolCall;
        SkillGate.BreakOnSkill = settings.Current.BreakOnSkill;
        Recorder.Changed += OnRecorderChanged;
        Gate.Changed += OnChildChanged;
        SkillGate.Changed += OnChildChanged;
    }

    public AgentSessionRecorder Recorder { get; } = new();

    public ToolCallGate Gate { get; } = new();

    public SkillCallGate SkillGate { get; } = new();

    /// <summary>The accumulated user/assistant conversation shown in the chat pane.</summary>
    public IReadOnlyList<ChatMessage> Chat => _chat;

    /// <summary>Models available to the signed-in Copilot account, populated by <see cref="LoadModelsAsync"/>.</summary>
    public IReadOnlyList<CopilotModelInfo> Models => _models;

    public bool IsLoadingModels { get; private set; }

    /// <summary>The runtime's default model (the "Auto" entry the SDK returns first), or null before models load.</summary>
    public string? DefaultModelId => _models.Count > 0 ? _models[0].Id : null;

    /// <summary>The model id chosen in the Agent tab dropdown.</summary>
    public string? SelectedModelId
    {
        get => _selectedModelId;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? null : value;
            if (_selectedModelId == normalized)
            {
                return;
            }

            _selectedModelId = normalized;
            ModelDirty = ComputeModelDirty();
        }
    }

    /// <summary>Reasoning effort applied to the model ("low", "medium", "high", "xhigh"); empty uses the model default.</summary>
    public string? ReasoningEffort
    {
        get => _reasoningEffort;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? null : value;
            if (_reasoningEffort == normalized)
            {
                return;
            }

            _reasoningEffort = normalized;
            ModelDirty = ComputeModelDirty();
        }
    }

    /// <summary>True when the selected model or reasoning effort differs from the saved default.</summary>
    public bool ModelDirty { get; private set; }

    private bool ComputeModelDirty() =>
        _selectedModelId != _settings.Current.CopilotModel ||
        _reasoningEffort != _settings.Current.ReasoningEffort;

    /// <summary>Loads the available models once per circuit; safe to call repeatedly.</summary>
    public async Task LoadModelsAsync()
    {
        if (_modelsLoaded || IsLoadingModels)
        {
            return;
        }

        IsLoadingModels = true;
        await NotifyAsync();
        try
        {
            var token = _settings.Current.ResolveGitHubToken();
            var baseDir = string.IsNullOrWhiteSpace(_settings.Current.CopilotBaseDirectory) ? null : _settings.Current.CopilotBaseDirectory;
            var models = await CopilotConnection.ListModelsAsync(token, baseDir);
            _models.Clear();
            _models.AddRange(models);
            _modelsLoaded = true;

            // With no saved override, preselect the runtime default without marking the row dirty.
            _selectedModelId ??= DefaultModelId;
        }
        catch
        {
            // Leave the list empty; the dropdown shows no models.
        }
        finally
        {
            IsLoadingModels = false;
            await NotifyAsync();
        }
    }

    /// <summary>Persists the selected model as the default used for future runs.</summary>
    public async Task SaveModelAsync()
    {
        var updated = _settings.Current.Clone();
        updated.CopilotModel = _selectedModelId;
        updated.ReasoningEffort = _reasoningEffort;
        await _settings.SaveAsync(updated);
        ModelDirty = false;
        await NotifyAsync();
    }


    public string SystemPrompt
    {
        get => _systemPrompt;
        set
        {
            if (_systemPrompt == value)
            {
                return;
            }

            _systemPrompt = value;
            _toolSkillEdit = null;
            SystemPromptDirty = true;
            _ = NotifyAsync();
        }
    }

    /// <summary>
    /// The tool- and skill-related markdown sections of <see cref="SystemPrompt"/>, edited in
    /// isolation on the Agent tab. Edits are merged back into the full prompt by
    /// <see cref="SaveSystemPromptAsync"/>.
    /// </summary>
    public string ToolSkillPrompt
    {
        get => _toolSkillEdit ?? SystemPromptSections.ExtractToolSkill(_systemPrompt);
        set
        {
            if (ToolSkillPrompt == value)
            {
                return;
            }

            _toolSkillEdit = value;
            SystemPromptDirty = true;
            _ = NotifyAsync();
        }
    }

    /// <summary>True once the user has saved the prompt as an explicit override (sent with Replace mode).</summary>
    public bool SystemPromptOverride { get; private set; }

    /// <summary>True when the prompt has unsaved edits that will not take effect until <see cref="SaveSystemPromptAsync"/>.</summary>
    public bool SystemPromptDirty { get; private set; }

    /// <summary>Path to <c>systemprompt.md</c> in the current agent directory, or null when no directory is set.</summary>
    private string? SystemPromptFilePath
    {
        get
        {
            var dir = _settings.Current.AgentDirectory;
            return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, SystemPromptFileName);
        }
    }

    /// <summary>
    /// Loads the system prompt from <c>systemprompt.md</c> in the current agent directory, if present,
    /// so the Agent tab reflects the prompt saved alongside the agent being built.
    /// </summary>
    public async Task LoadSystemPromptFromDirectoryAsync()
    {
        var promptPath = SystemPromptFilePath;
        if (promptPath is null || !File.Exists(promptPath))
        {
            return;
        }

        _systemPrompt = await File.ReadAllTextAsync(promptPath);
        _toolSkillEdit = null;
        SystemPromptOverride = !string.IsNullOrWhiteSpace(_systemPrompt);
        SystemPromptDirty = false;
        await NotifyAsync();
    }

    /// <summary>The latest system prompt summary produced by <see cref="RefreshSystemPromptSummaryAsync"/>, or null.</summary>
    public string? SystemPromptSummary => _systemPromptSummary;

    /// <summary>True while a system prompt summary is being generated.</summary>
    public bool IsSummarizingPrompt { get; private set; }

    /// <summary>The error from the last summary attempt, or null when it succeeded.</summary>
    public string? SummaryError { get; private set; }

    /// <summary>
    /// Summarizes the current system prompt via a fresh, tool-free and skill-free Copilot session.
    /// Called after Load and Save; clears the summary when the prompt is empty.
    /// </summary>
    public async Task RefreshSystemPromptSummaryAsync()
    {
        if (string.IsNullOrWhiteSpace(_systemPrompt))
        {
            _systemPromptSummary = null;
            SummaryError = null;
            IsSummarizingPrompt = false;
            await NotifyAsync();
            return;
        }

        IsSummarizingPrompt = true;
        SummaryError = null;
        await NotifyAsync();

        try
        {
            _systemPromptSummary = await CopilotConnection.SummarizeSystemPromptAsync(
                _systemPrompt,
                _settings.Current.ResolveGitHubToken(),
                string.IsNullOrWhiteSpace(_settings.Current.CopilotBaseDirectory) ? null : _settings.Current.CopilotBaseDirectory,
                string.IsNullOrWhiteSpace(_settings.Current.CopilotModel) ? null : _settings.Current.CopilotModel);
        }
        catch (Exception ex)
        {
            _systemPromptSummary = null;
            SummaryError = ex.Message;
        }
        finally
        {
            IsSummarizingPrompt = false;
            await NotifyAsync();
        }
    }

    /// <summary>
    /// Commits the current textbox text so the next run replaces Copilot's built-in prompt with it
    /// (or reverts to Copilot's default when blank). Persists it as the default for future launches,
    /// and writes it to <c>systemprompt.md</c> in the agent directory when one is set.
    /// </summary>
    public async Task SaveSystemPromptAsync()
    {
        // Fold any Agent-tab edits to the tool/skill sections back into the full prompt first.
        if (_toolSkillEdit is not null)
        {
            _systemPrompt = SystemPromptSections.MergeToolSkill(_systemPrompt, _toolSkillEdit);
            _toolSkillEdit = null;
        }

        SystemPromptOverride = !string.IsNullOrWhiteSpace(_systemPrompt);
        SystemPromptDirty = false;

        var updated = _settings.Current.Clone();
        updated.ToolReasoning = _toolReasoning ?? string.Empty;
        updated.AppendToolReasoning = _appendToolReasoning;
        await _settings.SaveAsync(updated);

        var promptPath = SystemPromptFilePath;
        if (promptPath is not null)
        {
            await File.WriteAllTextAsync(promptPath, _systemPrompt ?? string.Empty);
        }

        await NotifyAsync();
        await RefreshSystemPromptSummaryAsync();
    }

    /// <summary>The Tool Reasoning text, optionally appended to the system prompt on each run.</summary>
    public string ToolReasoning
    {
        get => _toolReasoning;
        set
        {
            if (_toolReasoning == value)
            {
                return;
            }

            _toolReasoning = value;
            SystemPromptDirty = true;
            _ = NotifyAsync();
        }
    }

    /// <summary>When true, <see cref="ToolReasoning"/> is appended to the system prompt sent to the model.</summary>
    public bool AppendToolReasoning
    {
        get => _appendToolReasoning;
        set
        {
            if (_appendToolReasoning == value)
            {
                return;
            }

            _appendToolReasoning = value;
            SystemPromptDirty = true;
            _ = NotifyAsync();
        }
    }

    /// <summary>Combines the system prompt with the Tool Reasoning text when appending is enabled.</summary>
    private static string CombinePrompt(string basePrompt, string appendPart)
    {
        if (string.IsNullOrWhiteSpace(appendPart))
        {
            return basePrompt;
        }

        return string.IsNullOrWhiteSpace(basePrompt)
            ? appendPart
            : $"{basePrompt.TrimEnd()}\n\n{appendPart}";
    }

    /// <summary>
    /// The full prompt as actually sent to the model for the current run: the resolved system
    /// prompt, the loaded tool definitions, and the transformed user message. Assembled from
    /// intercepted session events and the connected MCP sessions; null until any are available.
    /// </summary>
    public string? FullPrompt
    {
        get
        {
            var events = Recorder.Events;
            var system = events.LastOrDefault(e => e.Kind == AgentEventKind.SystemPrompt)?.Detail;
            var user = events.LastOrDefault(e => e.Kind == AgentEventKind.RequestToModel)?.Detail;
            var tools = DescribeLoadedTools();
            if (system is null && user is null && tools is null)
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            if (system is not null)
            {
                sb.AppendLine("===== SYSTEM =====").AppendLine(system).AppendLine();
            }

            if (tools is not null)
            {
                sb.AppendLine("===== TOOLS =====").AppendLine(tools).AppendLine();
            }

            if (user is not null)
            {
                sb.AppendLine("===== USER =====").AppendLine(user);
            }

            return sb.ToString().TrimEnd();
        }
    }

    private string? DescribeLoadedTools()
    {
        var connected = _servers.Where(s => s is { IsConnected: true, Session: not null }).ToList();
        if (connected.Count == 0)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var server in connected)
        {
            foreach (var tool in server.Session!.AITools)
            {
                sb.Append("- ").Append(tool.Name).Append(" (").Append(server.Name).Append(')');
                if (!string.IsNullOrWhiteSpace(tool.Description))
                {
                    sb.Append(": ").Append(tool.Description);
                }

                sb.AppendLine();
                sb.Append("  parameters: ").AppendLine(tool.JsonSchema.GetRawText());
            }
        }

        return sb.Length == 0 ? null : sb.ToString().TrimEnd();
    }

    public string McpConfigPath { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public bool IsLoadingServers { get; private set; }

    public bool IsRunning { get; private set; }

    public string? LoadError { get; private set; }

    public IReadOnlyList<AgentServerView> Servers => _servers;

    public bool BreakOnToolCall
    {
        get => Gate.BreakOnToolCall;
        set
        {
            if (Gate.BreakOnToolCall == value)
            {
                return;
            }

            Gate.BreakOnToolCall = value;
            _ = PersistBreakSettingsAsync();
        }
    }

    public bool BreakOnSkill
    {
        get => SkillGate.BreakOnSkill;
        set
        {
            if (SkillGate.BreakOnSkill == value)
            {
                return;
            }

            SkillGate.BreakOnSkill = value;
            _ = PersistBreakSettingsAsync();
        }
    }

    private async Task PersistBreakSettingsAsync()
    {
        var updated = _settings.Current.Clone();
        updated.BreakOnToolCall = Gate.BreakOnToolCall;
        updated.BreakOnSkill = SkillGate.BreakOnSkill;
        await _settings.SaveAsync(updated);
    }

    /// <summary>True while a Copilot session is alive and the next prompt will continue it.</summary>
    public bool HasSession => _runner.HasSession;

    /// <summary>The Copilot runtime's id for the current session, or null when none is active.</summary>
    public string? SessionId { get; private set; }

    /// <summary>True when the most recent run continued the existing session rather than starting a new one.</summary>
    public bool LastRunReusedSession { get; private set; }

    /// <summary>Prompts sent on the current session, including the latest; 0 when no session is active.</summary>
    public int SessionTurns { get; private set; }

    /// <summary>Editable definitions for every loaded tool; rebuilt whenever servers are (re)loaded.</summary>
    public IReadOnlyList<ToolEditModel> ToolEdits => _toolEdits;

    /// <summary>Skills discovered under the configured skills directory.</summary>
    public IReadOnlyList<SkillView> Skills => _skills;

    /// <summary>True while the skills directory is being scanned.</summary>
    public bool IsLoadingSkills { get; private set; }

    /// <summary>Error surfaced while loading skills, or null when the last scan succeeded.</summary>
    public string? SkillsError { get; private set; }

    /// <summary>The configured skills directory, or null when none is set.</summary>
    public string? SkillsDirectory => _settings.Current.SkillsDirectory;

    /// <summary>True when the named skill is enabled (i.e. not toggled off by the user).</summary>
    public bool IsSkillEnabled(string name) => !_settings.Current.DisabledSkills.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Toggles a skill on or off and persists the choice; takes effect on the next run.</summary>
    public async Task SetSkillEnabledAsync(string name, bool enabled)
    {
        var disabled = _settings.Current.DisabledSkills;
        if (enabled)
        {
            disabled.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        }
        else if (!disabled.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            disabled.Add(name);
        }

        await _settings.SaveAsync(_settings.Current);
        await NotifyAsync();
    }

    /// <summary>True when any loaded tool's advertised definition has been edited.</summary>
    public bool AnyToolEdited => _toolEdits.Any(t => t.IsModified);

    /// <summary>True when a previous prompt can be re-sent (e.g. after editing tool definitions).</summary>
    public bool CanRerun => !IsRunning && !string.IsNullOrWhiteSpace(_lastPrompt);

    /// <summary>Raised whenever any observable state changes (config, servers, events, gate).</summary>
    public event Func<Task>? Changed;

    public async Task LoadServersAsync()
    {
        IsLoadingServers = true;
        LoadError = null;
        await DisposeServersAsync();
        await NotifyAsync();

        try
        {
            var full = Path.GetFullPath(McpConfigPath);
            if (!File.Exists(full))
            {
                LoadError = $"File not found: {full}";
                return;
            }

            var config = await McpConfig.ReadFileAsync(full);
            var servers = McpConfig.GetServers(config);
            if (servers.Count == 0)
            {
                LoadError = $"No MCP servers were found in {full}.";
                return;
            }

            foreach (var entry in servers)
            {
                var view = new AgentServerView { Name = entry.Key };
                _servers.Add(view);
                await NotifyAsync();

                view.Session = await McpServerSession.ConnectAsync(entry.Key, entry.Value);
                await NotifyAsync();
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            RebuildToolEditors();
            IsLoadingServers = false;
            await NotifyAsync();
        }
    }

    /// <summary>Rebuilds the editable tool list from the connected servers, discarding prior edits.</summary>
    private void RebuildToolEditors()
    {
        _toolEdits.Clear();
        foreach (var server in _servers.Where(s => s is { IsConnected: true, Session: not null }))
        {
            foreach (var tool in server.Session!.AITools)
            {
                var schema = PrettyJson(tool.JsonSchema.GetRawText());
                var model = new ToolEditModel
                {
                    ServerName = server.Name,
                    OriginalName = tool.Name,
                    OriginalDescription = tool.Description ?? string.Empty,
                    OriginalParametersJson = schema,
                };
                model.ResetToOriginal();
                _toolEdits.Add(model);
            }
        }
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    /// <summary>
    /// Scans the configured skills directory for <c>SKILL.md</c> files and loads each one's
    /// name/description (from YAML frontmatter) plus its full text for display on the Agent tab.
    /// </summary>
    public async Task LoadSkillsAsync()
    {
        IsLoadingSkills = true;
        SkillsError = null;
        _skills.Clear();
        await NotifyAsync();

        try
        {
            var dir = _settings.Current.SkillsDirectory;
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            if (!Directory.Exists(dir))
            {
                SkillsError = $"Directory not found: {dir}";
                return;
            }

            var files = Directory
                .EnumerateFiles(dir, "SKILL.md", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                var (name, description) = ParseSkillFrontmatter(content);
                _skills.Add(new SkillView
                {
                    Name = string.IsNullOrWhiteSpace(name)
                        ? Path.GetFileName(Path.GetDirectoryName(file)!) ?? "skill"
                        : name,
                    Description = description ?? string.Empty,
                    Path = file,
                    Content = content,
                });
            }

            if (_skills.Count == 0)
            {
                SkillsError = $"No SKILL.md files found under {dir}.";
            }
        }
        catch (Exception ex)
        {
            SkillsError = ex.Message;
        }
        finally
        {
            IsLoadingSkills = false;
            await NotifyAsync();
        }
    }

    /// <summary>Writes edited text back to a skill's <c>SKILL.md</c> file and refreshes it in memory.</summary>
    public async Task SaveSkillAsync(SkillView skill, string content)
    {
        await File.WriteAllTextAsync(skill.Path, content);
        skill.Content = content;
        await NotifyAsync();
    }

    /// <summary>
    /// Resolves the skill parent directories to register with the Copilot session. The SDK
    /// discovers <c>SKILL.md</c> files in the immediate subdirectories of each entry.
    /// </summary>
    private IReadOnlyList<string>? ResolveSkillDirectories()
    {
        var dir = _settings.Current.SkillsDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }

        return [dir];
    }

    /// <summary>The skill names the user has toggled off, or null when none are disabled.</summary>
    private IReadOnlyList<string>? ResolveDisabledSkills()
    {
        var disabled = _settings.Current.DisabledSkills;
        return disabled.Count > 0 ? [.. disabled] : null;
    }

    /// <summary>Extracts the <c>name</c> and <c>description</c> from a SKILL.md YAML frontmatter block.</summary>
    private static (string? Name, string? Description) ParseSkillFrontmatter(string content)
    {
        if (!content.TrimStart().StartsWith("---", StringComparison.Ordinal))
        {
            return (null, null);
        }

        string? name = null;
        string? description = null;
        var started = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim() == "---")
            {
                if (!started)
                {
                    started = true;
                    continue;
                }

                break;
            }

            if (!started)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim().Trim('"', '\'');
            if (name is null && key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (description is null && key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value;
            }
        }

        return (name, description);
    }

    public async Task StartRunAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        var prompt = Prompt.Trim();
        Prompt = string.Empty;
        await LaunchAsync(prompt);
    }

    /// <summary>Re-sends the previous prompt, applying any current tool-definition edits.</summary>
    public async Task RerunLastAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(_lastPrompt))
        {
            return;
        }

        await LaunchAsync(_lastPrompt);
    }

    /// <summary>Launches a chat run that asks the agent to use the given skill.</summary>
    public async Task RunSkillAsync(SkillView skill)
    {
        if (IsRunning)
        {
            return;
        }

        await LaunchAsync($"Use the \"{skill.Name}\" skill.");
    }

    /// <summary>Restores a single tool's advertised definition to the server's original.</summary>
    public void ResetToolEdit(ToolEditModel model)
    {
        model.ResetToOriginal();
        _ = NotifyAsync();
    }

    /// <summary>Restores every edited tool definition to the server's original.</summary>
    public void ResetAllToolEdits()
    {
        foreach (var model in _toolEdits)
        {
            model.ResetToOriginal();
        }

        _ = NotifyAsync();
    }

    private async Task LaunchAsync(string prompt)
    {
        if (!ValidateToolEdits())
        {
            await NotifyAsync();
            return;
        }

        _lastPrompt = prompt;

        var tools = BuildTools();

        // When the box is left blank (no override) Copilot uses its own prompt; capture it afterwards
        // so the user can see and edit it.
        var basePrompt = SystemPromptOverride && !string.IsNullOrWhiteSpace(_systemPrompt) ? _systemPrompt : string.Empty;
        var appendPart = _appendToolReasoning && !string.IsNullOrWhiteSpace(_toolReasoning) ? _toolReasoning.Trim() : string.Empty;
        var effectiveSystemPrompt = CombinePrompt(basePrompt, appendPart);
        var sendOverride = !string.IsNullOrWhiteSpace(effectiveSystemPrompt);
        var captureSystemPrompt = !sendOverride && string.IsNullOrWhiteSpace(_systemPrompt);
        var options = new CopilotRunOptions
        {
            Prompt = prompt,
            SystemPrompt = sendOverride ? effectiveSystemPrompt : null,
            ReplaceSystemPrompt = sendOverride,
            Model = string.IsNullOrWhiteSpace(_settings.Current.CopilotModel) ? null : _settings.Current.CopilotModel,
            ShowReasoning = true,
            ReasoningEffort = string.IsNullOrWhiteSpace(_reasoningEffort) ? null : _reasoningEffort,
            GitHubToken = _settings.Current.ResolveGitHubToken(),
            BaseDirectory = string.IsNullOrWhiteSpace(_settings.Current.CopilotBaseDirectory) ? null : _settings.Current.CopilotBaseDirectory,
            SkillDirectories = ResolveSkillDirectories(),
            DisabledSkills = ResolveDisabledSkills(),
            SkillGate = SkillGate,
            Timeout = TimeSpan.FromMinutes(Math.Max(1, _settings.Current.RunTimeoutMinutes)),
            Tools = tools,
        };

        // Mark the break in the chat when this prompt will start a fresh session (e.g. after a
        // model/skill/tool change), matching the "New session" button.
        var sessionDivider = _runner.WouldStartNewSession(options) ? AddSessionDivider() : null;

        _chat.Add(new ChatMessage { Role = ChatRole.User, Text = prompt });
        Recorder.Clear();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        await NotifyAsync();

        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await _runner.RunAsync(options, Recorder, token);
                LastRunReusedSession = outcome.SessionReused;
                SessionTurns = outcome.SessionTurns;
                SessionId = outcome.SessionId;
                if (sessionDivider is not null && !outcome.SessionReused && !string.IsNullOrEmpty(outcome.SessionId))
                {
                    sessionDivider.Text = $"New session · {outcome.SessionId}";
                }
            }
            finally
            {
                if (captureSystemPrompt)
                {
                    var sdkPrompt = Recorder.Events
                        .LastOrDefault(e => e.Kind == AgentEventKind.SystemPrompt)?.Detail;
                    if (!string.IsNullOrWhiteSpace(sdkPrompt))
                    {
                        // Assign the backing field directly: this is a preview of Copilot's default,
                        // not a user override, so it must not be marked dirty.
                        _systemPrompt = sdkPrompt;
                    }
                }

                IsRunning = false;
                await NotifyAsync();
            }
        });
    }

    /// <summary>Builds the tool set for a run, applying the user's edited definitions as overrides.</summary>
    private List<AIFunction> BuildTools()
    {
        var overrides = new Dictionary<string, ToolOverride>();
        foreach (var edit in _toolEdits.Where(e => e.IsModified))
        {
            overrides[AgentToolset.ToolKey(edit.ServerName, edit.OriginalName)] = new ToolOverride
            {
                Name = edit.Name != edit.OriginalName ? edit.Name.Trim() : null,
                Description = edit.Description != edit.OriginalDescription ? edit.Description : null,
                ParametersJson = edit.ParametersJson.Trim() != edit.OriginalParametersJson.Trim() ? edit.ParametersJson : null,
            };
        }

        var toolProviders = _servers
            .Where(s => s is { IsConnected: true, Session: not null })
            .Select(s => (s.Name, (IReadOnlyList<McpClientTool>)s.Session!.AITools));
        return AgentToolset.Build(toolProviders, Gate, Recorder, overrides);
    }

    /// <summary>Validates edited names and schemas, recording a per-tool <see cref="ToolEditModel.Error"/>.</summary>
    private bool ValidateToolEdits()
    {
        var ok = true;
        foreach (var edit in _toolEdits)
        {
            edit.Error = null;
            if (!edit.IsModified)
            {
                continue;
            }

            if (!Regex.IsMatch(edit.Name, "^[a-zA-Z0-9_-]{1,64}$"))
            {
                edit.Error = "Name must be 1–64 characters: letters, digits, underscore, or hyphen.";
                ok = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(edit.ParametersJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(edit.ParametersJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        edit.Error = "Parameters must be a JSON object.";
                        ok = false;
                    }
                }
                catch (JsonException ex)
                {
                    edit.Error = $"Invalid JSON: {ex.Message}";
                    ok = false;
                }
            }
        }

        return ok;
    }

    public void CancelRun()
    {
        _cts?.Cancel();
        Gate.AbortAll();
        SkillGate.AbortAll();
    }

    public void Continue(Guid id) => Gate.Resolve(id, ToolDecision.Passthrough());

    public void SubmitManual(Guid id, string result) => Gate.Resolve(id, ToolDecision.Manual(result));

    public void Abort(Guid id) => Gate.Resolve(id, ToolDecision.Abort());

    /// <summary>Allows a paused skill invocation to run.</summary>
    public void AllowSkill(Guid id) => SkillGate.Resolve(id, true);

    /// <summary>Blocks a paused skill invocation so it never runs.</summary>
    public void BlockSkill(Guid id) => SkillGate.Resolve(id, false);

    /// <summary>Clears the visible conversation and ends the current session so the next prompt starts fresh.</summary>
    public async Task ClearChatAsync()
    {
        _chat.Clear();
        Recorder.Clear();
        await _runner.ResetSessionAsync();
        SessionTurns = 0;
        LastRunReusedSession = false;
        SessionId = null;
        await NotifyAsync();
    }

    /// <summary>
    /// Ends the current session and clears session events, but keeps the chat history and marks the
    /// break with a divider so the next prompt begins a fresh session.
    /// </summary>
    public async Task StartNewSessionAsync()
    {
        AddSessionDivider();

        Recorder.Clear();
        await _runner.ResetSessionAsync();
        SessionTurns = 0;
        LastRunReusedSession = false;
        SessionId = null;
        await NotifyAsync();
    }

    /// <summary>
    /// Ensures a "New session" divider marks the end of the chat and returns it, or null when the
    /// chat is empty (the first session needs no divider). Reuses a trailing divider if present.
    /// </summary>
    private ChatMessage? AddSessionDivider()
    {
        if (_chat.Count == 0)
        {
            return null;
        }

        if (_chat[^1].IsSessionDivider)
        {
            return _chat[^1];
        }

        var divider = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = "New session",
            IsSessionDivider = true,
        };
        _chat.Add(divider);
        return divider;
    }

    private void OnChildChanged() => _ = NotifyAsync();

    private void OnRecorderChanged()
    {
        AppendAssistantMessages();
        _ = NotifyAsync();
    }

    private void AppendAssistantMessages()
    {
        var events = Recorder.Events;
        if (events.Count < _processedEvents)
        {
            _processedEvents = 0;
        }

        for (var i = _processedEvents; i < events.Count; i++)
        {
            var evt = events[i];
            switch (evt.Kind)
            {
                case AgentEventKind.RunCompleted:
                    _chat.Add(new ChatMessage { Role = ChatRole.Assistant, Text = evt.Summary });
                    break;
                case AgentEventKind.Error:
                    _chat.Add(new ChatMessage { Role = ChatRole.Assistant, Text = evt.Summary, IsError = true });
                    break;
            }
        }

        _processedEvents = events.Count;
    }

    private Task NotifyAsync() => Changed?.Invoke() ?? Task.CompletedTask;

    private async Task DisposeServersAsync()
    {
        foreach (var server in _servers)
        {
            if (server.Session is not null)
            {
                await server.Session.DisposeAsync();
            }
        }

        _servers.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        CancelRun();
        await _runner.DisposeAsync();
        await DisposeServersAsync();
    }
}
