# Eadon MCP

An MCP server that lets AI agents work in the Unity Editor.

- `unity-package/` — the Unity package `com.eadon.mcp-bridge` (Eadon MCP Bridge): the bridge (an HTTP API inside the Editor), the in-editor agent, Level Scaffolding, and semantic metadata for prefabs and scenes. Requires Unity 6000.3.
- `server/` — the Python MCP server that MCP clients talk to; it calls the bridge.
- `extensions/pcg/` — the PCG tools. Copied next to PCGFramework in projects that have it; they register their tools with the bridge (MCP clients reach them through `list_unity_tools` and `call_unity_tool`) and only compile when the package is installed.
- `server/scripts/check_contract.py` — checks that the Python tools, the tool catalog and the bridge agree on routes and argument names.
- `server/scripts/export_tool_catalog.py` — writes the built-in tool catalog (`unity-package/Editor/Catalog/builtin-tools.json`) from the Python docstrings. Run it after changing a tool.

Installation: `unity-package/Documentation~/MCP Server Installation.md`. The other guides are in the same folder.
