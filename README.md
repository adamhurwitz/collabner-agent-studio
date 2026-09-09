# Collabner Agent Studio

Collabner Agent Studio is a cross-platform desktop app for building Agents using the GitHub Copilot SDK for a harness. It has tabs for Project, MCP, SKILLs, and Agent with Evals and Prod coming soon. You begin by pointing the Project to an Agent Directory and loading its files or creating them. The files define the agent: SystemPrompt.md, a skills directory, and a .mcp.json file. The Agent Studio lets you test and edit your files to get the behavior you want for your agent. 


### Features

**MCP Inspector**

- View the full set of tool names and descriptions exposed by each connected server.
- Invoke MCP functions one at a time, with a form for arguments and a view of the response.

**Agent**

- Run and adjust the system prompt directly in the UI and re-run prompts to compare behavior.
- Uses the GitHub Copilot SDK as the agent harness for seamless promotion to production
- Select the model used for a run.
- Set a pause / breakpoint at a tool call before it executes.
- Intercept a tool call and manually enter the response instead of calling the server.
- View the session events for each call, including the model's reasoning.
- Add tool reasoning to the system prompt to guide tool selection.

**Skill & tool selection tester**

- Load your skills and MCP servers and review their titles and descriptions in one place.
- Enter example prompts and see which tools and skills get selected.
- Run multi-step prompts and watch the agent select the appropriate chain of tools and skills.

**Sessions & auth**

- Session storage is kept separate from the CLI so desktop and command-line runs don't collide.
- Authenticate using a GitHub token and the logged-in user for Copilot access.


The toolkit ships as a **Desktop** app – a Blazor + [Electron.NET](https://github.com/ElectronNET/Electron.NET) application (Collabner Agent Studio) with Project, MCP, SKILLs, Agent, and Settings tabs.

## Projects

| Project | Description |
| --- | --- |
| `Collabner.McpInspector.Core` | Shared library. Reads `.mcp.json`, creates MCP clients over stdio or HTTP, loads tool schemas, and captures the JSON-RPC wire session. |
| `Collabner.AgentStudio.Agent` | Agent engine: session event recorder, tool-call gate, MCP tool interception, and the function-invocation run loop. |
| `Collabner.AgentStudio.Desktop` | Blazor Server UI hosted in Electron (Collabner Agent Studio) with the Inspector, Agent, and Settings tabs. |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) and the Electron.NET CLI (`dotnet tool install ElectronNET.CLI -g`) — only needed to run or package the desktop app under Electron.

## Configuration

The desktop app reads a standard MCP configuration file. Server entries can live under either a `servers` or `mcpServers` key.

```json
{
  "servers": {
    "cloudx": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@example/mcp-server"],
      "cwd": "./",
      "env": {
        "API_KEY": "your-token"
      }
    },
    "remote": {
      "type": "http",
      "url": "https://example.com/mcp",
      "headers": {
        "Authorization": "Bearer your-token"
      }
    }
  }
}
```

Supported per-server fields:

| Field | Description |
| --- | --- |
| `type` | Transport type: `stdio` (default), `http`/`streamable-http`, or `sse`. |
| `command` / `executable` | The executable to launch (stdio only; at least one is required). |
| `args` | Arguments passed to the command (stdio only). |
| `cwd` | Working directory for the server process (stdio only). |
| `env` | Environment variables for the server process (stdio only). |
| `url` | Endpoint of an HTTP MCP server (required for `http`/`sse`). |
| `headers` | Additional HTTP headers sent with every request (HTTP only). |

Both stdio and HTTP (Streamable HTTP and SSE) transports are supported. When a `url` is present the server is treated as HTTP; with `type` omitted the HTTP transport mode is auto-detected.

## Desktop usage

The desktop app can run either inside Electron or as a plain Blazor web app.

### Run in the browser (development)

```powershell
dotnet run --project src/Collabner.AgentStudio.Desktop
```

Then open the URL printed in the console.

### Run in Electron

```powershell
dotnet tool install ElectronNET.CLI -g   # first time only
cd src/Collabner.AgentStudio.Desktop
electronize start
```



## Building

```powershell
# Build the whole solution
dotnet build Collabner.AgentStudio.slnx

# Package the desktop app with Electron
cd src/Collabner.AgentStudio.Desktop
electronize build /target win
```


## License

Copyright © 2026 Collabner. Licensed under the [MIT License](LICENSE).

