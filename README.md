# Eadon MCP

An MCP server that lets AI agents work in the Unity Editor.

- `unity-package/` — the Unity side: the bridge (an HTTP API inside the Editor), the in-editor agent, and LevelScaffold.
- `server/` — the Python MCP server that MCP clients talk to; it calls the bridge.
- `server/scripts/check_contract.py` — checks that the Python tools and the bridge agree on routes and keys.

Documentation is in `unity-package/Documentation~/`.
