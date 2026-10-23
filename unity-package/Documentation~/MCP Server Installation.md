# MCP Server Installation

Setup steps
1. Install uv
macOS: curl -LsSf https://astral.sh/uv/install.sh | sh
Windows (PowerShell): powershell -c "irm https://astral.sh/uv/install.ps1 | iex"
2. Setup base project structure
```text
mcp-server/
├── pyproject.toml
└── src/
    └── unity_mcp/
        └── __init__.py      ← the empty one
```
3. Install Python dependencies
```bash
cd mcp-server
uv sync --group dev
```
4. Add all other .py files
5. Run tests
```bash
cd mcp-server
uv run pytest
```
