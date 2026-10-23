"""
Write the bridge's built-in tool catalog from this server's tools.

The Python server is where the built-in tools are described (their docstrings
and signatures are what MCP clients see). This script exports those
descriptions into unity-package/Editor/Catalog/builtin-tools.json, so the
in-editor agent and the bridge's /tools/catalog and /tools/describe routes use
exactly the same text:

  name          the tool name
  summary       the first paragraph of the docstring (what the agent sends by default)
  description   the whole docstring (returned by describe_tool)
  input_schema  the JSON schema of the arguments, without "title" entries
  method, path  the bridge route the tool calls

Usage (from server/):
    uv run python scripts/export_tool_catalog.py           # write the file
    uv run python scripts/export_tool_catalog.py --check   # exit 1 if the file is out of date

check_contract.py runs the --check comparison too.
"""

from __future__ import annotations

import argparse
import asyncio
import inspect
import json
import re
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
SERVER_ROOT = SCRIPTS.parent
sys.path.insert(0, str(SERVER_ROOT / "src"))
sys.path.insert(0, str(SCRIPTS))

from check_contract import parse_python_calls  # noqa: E402

CATALOG_PATH = SERVER_ROOT.parent / "unity-package" / "Editor" / "Catalog" / "builtin-tools.json"

# Python-only tools: they reach extension tools, they are not bridge tools themselves.
PYTHON_ONLY = {"list_unity_tools", "call_unity_tool"}


def summary_of(description: str) -> str:
    return re.split(r"\n\s*\n", description.strip(), maxsplit=1)[0].strip()


def without_titles(value):
    if isinstance(value, dict):
        return {k: without_titles(v) for k, v in value.items() if k != "title"}
    if isinstance(value, list):
        return [without_titles(v) for v in value]
    return value


def build_catalog() -> str:
    from unity_mcp.server import mcp

    routes: dict[str, tuple[str, str]] = {}
    for call in parse_python_calls():
        routes.setdefault(call["tool"], (call["method"], call["path"]))

    tools = []
    missing = []
    for tool in asyncio.run(mcp.list_tools()):
        if tool.name in PYTHON_ONLY:
            continue
        if tool.name not in routes:
            missing.append(tool.name)
            continue
        description = inspect.cleandoc(tool.description or "")
        method, path = routes[tool.name]
        tools.append({
            "name": tool.name,
            "summary": summary_of(description),
            "description": description,
            "input_schema": without_titles(tool.inputSchema),
            "method": method,
            "path": path,
        })
    if missing:
        sys.exit(f"No bridge route found for: {', '.join(missing)}")

    catalog = {
        "generated_by": "server/scripts/export_tool_catalog.py — do not edit by hand",
        "tools": tools,
    }
    return json.dumps(catalog, indent=2, ensure_ascii=False) + "\n"


def is_up_to_date() -> bool:
    return CATALOG_PATH.exists() and CATALOG_PATH.read_text(encoding="utf-8") == build_catalog()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="exit 1 if the catalog file is out of date")
    args = parser.parse_args()

    if args.check:
        if is_up_to_date():
            print(f"{CATALOG_PATH.name} is up to date.")
            return 0
        print(f"{CATALOG_PATH} is out of date: run scripts/export_tool_catalog.py")
        return 1

    text = build_catalog()
    CATALOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    CATALOG_PATH.write_text(text, encoding="utf-8")
    print(f"Wrote {len(json.loads(text)['tools'])} tools to {CATALOG_PATH}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
