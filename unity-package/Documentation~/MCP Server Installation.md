# MCP Server Installation

The repository (EadonMCP) holds both halves:
- `unity-package/` — the Unity package `com.eadon.mcp-bridge` (the bridge, the in-editor agent, Level Scaffolding)
- `server/` — the Python MCP server
- `extensions/pcg/` — the PCG tools, for projects that have PCGFramework

Below, `<EadonMCP>` is the folder you cloned the repository into.

## Unity package

1. Add the package to the project's `Packages/manifest.json`:
```json
"com.eadon.mcp-bridge": "file:<EadonMCP>/unity-package"
```
The path can be absolute or relative to the `Packages` folder. Unity adds Newtonsoft JSON, Timeline and Splines if the project doesn't have them.

2. Optional — to run the bridge's tests in the Test Runner, list the package as testable in the same manifest:
```json
"testables": [ "com.eadon.mcp-bridge" ]
```

3. Optional — for the PCG tools, copy `<EadonMCP>/extensions/pcg/Editor` into the project's PCGFramework, e.g. `Assets/PCGFramework/Editor/Mcp`. It only compiles when the package is installed.

4. Settings are in **Project Settings > Eadon > MCP Bridge**. The menus are under **Eadon > MCP Bridge**.

## Python server

1. Install uv
macOS: curl -LsSf https://astral.sh/uv/install.sh | sh
Windows (PowerShell): powershell -c "irm https://astral.sh/uv/install.ps1 | iex"

2. Install Python dependencies
```bash
cd <EadonMCP>/server
uv sync --group dev
```

3. Run tests
```bash
cd <EadonMCP>/server
uv run pytest
```

4. Point the MCP client at it, e.g. in Claude Desktop's `claude_desktop_config.json`:
```json
"unity-mcp": {
  "command": "uv",
  "args": ["--directory", "<EadonMCP>/server", "run", "unity-mcp"]
}
```

5. Check that the two halves agree (routes and request keys):
```bash
cd <EadonMCP>/server
uv run python scripts/check_contract.py
```
