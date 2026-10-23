# Eadon MCP

An MCP server that lets AI agents work in the Unity Editor.

- `unity-package/` — the Unity package `com.eadon.mcp-bridge` (Eadon MCP Bridge): the bridge (an HTTP API inside the Editor), the in-editor agent, Level Scaffolding, and semantic metadata for prefabs and scenes. Requires Unity 6000.3.
- `server/` — the Python MCP server that MCP clients talk to; it calls the bridge.
- `extensions/pcg/` — the PCG tools. Copied next to PCGFramework in projects that have it; they register their routes with the bridge and only compile when the package is installed.
- `server/scripts/check_contract.py` — checks that the Python tools and the bridge agree on routes and request keys.

Installation: `unity-package/Documentation~/MCP Server Installation.md`. The other guides are in the same folder.
