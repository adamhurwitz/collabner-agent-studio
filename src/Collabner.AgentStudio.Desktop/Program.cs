// Copyright © 2026 Collabner. All rights reserved.

using Collabner.AgentStudio.Desktop.Components;
using Collabner.AgentStudio.Desktop.Services;
using ElectronNET.API;
using ElectronNET.API.Entities;

var builder = WebApplication.CreateBuilder(args);

// Hook the app into the Electron host process when launched via `electronize`.
builder.WebHost.UseElectron(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddScoped<InspectorState>();
builder.Services.AddScoped<AgentRunState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<Collabner.AgentStudio.Desktop.Components.App>()
    .AddInteractiveServerRenderMode();

if (HybridSupport.IsElectronActive)
{
    await app.StartAsync();

    var window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
    {
        Width = 1280,
        Height = 860,
        Title = "Collabner Agent Studio",
        Show = true,
    });

    // Remove the default File/Edit/View/Window/Help menu bar.
    Electron.Menu.SetApplicationMenu(Array.Empty<MenuItem>());
    window.RemoveMenu();

    window.OnClosed += () => Electron.App.Quit();

    await app.WaitForShutdownAsync();
}
else
{
    // Falls back to running as a plain web app (useful for development in a browser).
    app.Run();
}
