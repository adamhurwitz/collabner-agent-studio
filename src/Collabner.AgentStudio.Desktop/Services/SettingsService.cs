// Copyright © 2026 Collabner. All rights reserved.

using System.Text.Json;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// Loads and persists <see cref="StudioSettings"/> under the user's application data folder.
/// Registered as a singleton so every tab reads the same shared defaults.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CollabnerAgentStudio");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    /// <summary>The current settings snapshot.</summary>
    public StudioSettings Current { get; private set; }

    /// <summary>Raised after settings are saved so open tabs can refresh.</summary>
    public event Action? Changed;

    private StudioSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(_path)) ?? new StudioSettings();
            }
        }
        catch
        {
            // A corrupt or unreadable settings file falls back to defaults rather than failing startup.
        }

        return new StudioSettings();
    }

    public async Task SaveAsync(StudioSettings settings)
    {
        Current = settings;
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, JsonOptions));
        Changed?.Invoke();
    }
}
