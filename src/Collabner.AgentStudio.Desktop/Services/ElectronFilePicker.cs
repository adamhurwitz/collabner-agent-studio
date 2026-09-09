// Copyright © 2026 Collabner. All rights reserved.

using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Collabner.AgentStudio.Desktop.Services;

/// <summary>
/// Uses the native Electron open dialog to pick an <c>.mcp.json</c> file.
/// Returns <c>null</c> when Electron is not the active host.
/// </summary>
public static class ElectronFilePicker
{
    public static async Task<string?> PickMcpJsonAsync()
    {
        if (!HybridSupport.IsElectronActive)
        {
            return null;
        }

        var window = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (window is null)
        {
            return null;
        }

        var options = new OpenDialogOptions
        {
            Title = "Select an .mcp.json file",
            Filters =
            [
                new FileFilter { Name = "MCP config", Extensions = ["json"] },
                new FileFilter { Name = "All files", Extensions = ["*"] },
            ],
            Properties = [OpenDialogProperty.openFile],
        };

        var files = await Electron.Dialog.ShowOpenDialogAsync(window, options);
        return files?.FirstOrDefault();
    }

    public static async Task<string?> PickDirectoryAsync(string title)
    {
        if (!HybridSupport.IsElectronActive)
        {
            return null;
        }

        var window = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (window is null)
        {
            return null;
        }

        var options = new OpenDialogOptions
        {
            Title = title,
            Properties = [OpenDialogProperty.openDirectory],
        };

        var dirs = await Electron.Dialog.ShowOpenDialogAsync(window, options);
        return dirs?.FirstOrDefault();
    }
}
