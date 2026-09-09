// Copyright © 2026 Collabner. All rights reserved.

using System.Reflection;
using GitHub.Copilot;

namespace Collabner.AgentStudio.Agent;

/// <summary>A model exposed by the Copilot runtime.</summary>
public sealed record CopilotModelInfo(string Id, string? Name);

/// <summary>Verifies that the Copilot runtime can be reached and authenticated.</summary>
public static class CopilotConnection
{
    /// <summary>The version of the referenced GitHub Copilot SDK, read from the loaded assembly.</summary>
    public static string SdkVersion { get; } = ResolveSdkVersion();

    private static string ResolveSdkVersion()
    {
        var assembly = typeof(CopilotClient).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip source-control metadata (e.g. "1.0.11+abc1234") for a clean display value.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>Lists the models available to the signed-in Copilot account.</summary>
    public static async Task<IReadOnlyList<CopilotModelInfo>> ListModelsAsync(
        string? gitHubToken,
        string? baseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var options = new CopilotClientOptions();
        if (!string.IsNullOrWhiteSpace(gitHubToken))
        {
            options.GitHubToken = gitHubToken;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            options.BaseDirectory = baseDirectory;
        }

        await using var client = new CopilotClient(options);
        var models = await client.ListModelsAsync();
        return models?
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new CopilotModelInfo(m.Id, m.Name))
            .ToArray() ?? [];
    }

    /// <summary>
    /// Summarizes an agent system prompt using a throwaway Copilot session that has no MCP tools
    /// and no skills registered, so the model only reasons about the prompt text itself.
    /// </summary>
    public static async Task<string> SummarizeSystemPromptAsync(
        string systemPrompt,
        string? gitHubToken,
        string? baseDirectory = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return string.Empty;
        }

        var options = new CopilotClientOptions();
        if (!string.IsNullOrWhiteSpace(gitHubToken))
        {
            options.GitHubToken = gitHubToken;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            options.BaseDirectory = baseDirectory;
        }

        await using var client = new CopilotClient(options);

        var sessionConfig = new SessionConfig
        {
            Tools = [],
            OnPermissionRequest = PermissionHandler.ApproveAll,
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            sessionConfig.Model = model;
        }

        await using var session = await client.CreateSessionAsync(sessionConfig);

        var prompt =
            "Summarize the following AI agent system prompt. In a few concise sentences, describe "
            + "the agent's role, its main capabilities, and any key rules or constraints. "
            + "Respond with the summary text only.\n\n---\n" + systemPrompt + "\n---";

        var reply = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = prompt },
            TimeSpan.FromMinutes(2),
            cancellationToken);

        return reply?.Data.Content?.Trim() ?? string.Empty;
    }

    public static async Task<(bool Ok, string Message)> TestAsync(
        string? gitHubToken,
        string? baseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new CopilotClientOptions();
            if (!string.IsNullOrWhiteSpace(gitHubToken))
            {
                options.GitHubToken = gitHubToken;
            }

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                options.BaseDirectory = baseDirectory;
            }

            await using var client = new CopilotClient(options);
            var models = await client.ListModelsAsync();
            var count = models?.Count ?? 0;
            return (true, count > 0 ? $"Connected — {count} models available." : "Connected.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
