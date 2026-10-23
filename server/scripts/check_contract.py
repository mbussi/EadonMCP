"""
Check that the Python tools and the in-editor agent agree with the Unity bridge.

Reads the C# of the Unity bridge (routes in McpRouter.cs, request types and
their [JsonProperty] names, the agent's ToolRegistry.cs) and the Python tools
in this repo, then reports:

  - Python calls to a route the bridge does not have
  - keys a Python tool sends that the request type does not read
    (Unity ignores them silently)
  - Python tool functions that are not registered in server.py
  - bridge routes no Python tool calls
  - agent tools whose route does not exist, that have no schema, or whose
    schema keys the request type does not read
  - Python tools the agent does not offer

Usage:
    uv run python scripts/check_contract.py "/path/to/Unity MCP Claude"

The path can be the Unity project, its Assets folder, or Assets/UnityMCP.
Exits with 1 when it finds a mismatch, 0 otherwise. Coverage gaps (routes
without a Python tool, Python tools the agent lacks) are listed but do not
fail the check.

This is a text-level check, not a compiler: it reads the code with regular
expressions and the Python ast module, so unusual formatting can hide a call.
"""

from __future__ import annotations

import argparse
import ast
import re
import sys
from pathlib import Path

PY_ROOT = Path(__file__).resolve().parent.parent / "src" / "unity_mcp"


# ---------------------------------------------------------------------------
# C#
# ---------------------------------------------------------------------------

def find_editor_folder(path: Path) -> Path:
    for candidate in (path, path / "UnityMCP", path / "Assets" / "UnityMCP"):
        editor = candidate / "Source" / "Editor"
        if (editor / "Core" / "McpRouter.cs").exists():
            return editor
    sys.exit(f"Could not find Source/Editor/Core/McpRouter.cs under {path}")


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def matching_brace(text: str, start: int, open_ch: str = "{", close_ch: str = "}") -> int:
    depth, i = 1, start
    while depth and i < len(text):
        if text[i] == open_ch:
            depth += 1
        elif text[i] == close_ch:
            depth -= 1
        i += 1
    return i


def parse_classes(editor: Path) -> dict[str, dict]:
    """class name -> {"base": name or None, "fields": [json names]}"""
    classes: dict[str, dict] = {}
    for file in editor.rglob("*.cs"):
        text = strip_comments(file.read_text(encoding="utf-8", errors="replace"))
        for m in re.finditer(r"\bclass\s+(\w+)\s*(?::\s*([\w.]+))?[^{;]*\{", text):
            body_end = matching_brace(text, m.end())
            body = text[m.end():body_end - 1]
            fields = []
            for fm in re.finditer(
                r'(\[JsonProperty\(\s*"([^"]+)"[^\]]*\)\]\s*)?public\s+(?!static|const|override|class|enum|void|async)'
                r'[\w<>\[\],.?\s]+?\s+(\w+)\s*(\{\s*get|=|;)', body):
                fields.append(fm.group(2) or fm.group(3))
            entry = classes.setdefault(m.group(1), {"base": m.group(2), "fields": []})
            if len(fields) > len(entry["fields"]):
                entry["fields"] = fields
                entry["base"] = m.group(2)
    return classes


def all_fields(classes: dict[str, dict], name: str) -> list[str]:
    fields: list[str] = []
    seen = set()
    while name and name in classes and name not in seen:
        seen.add(name)
        fields += classes[name]["fields"]
        name = (classes[name]["base"] or "").split(".")[-1]
    return fields


def parse_routes(editor: Path) -> dict[tuple[str, str], dict]:
    text = strip_comments((editor / "Core" / "McpRouter.cs").read_text(encoding="utf-8"))
    routes = {}
    pattern = r'method\s*==\s*"(\w+)"\s*&&\s*path\s*==\s*"([^"]+)"\s*\)\s*return\s+await\s+([\w.]+)\(([^;]*);'
    for m in re.finditer(pattern, text, flags=re.S):
        body = m.group(4)
        req = re.search(r"ParseBodyAsync<(\w+)>", body)
        routes[(m.group(1), m.group(2))] = {
            "handler": m.group(3),
            "request": req.group(1) if req else None,
            "query": re.findall(r'QueryString\["(\w+)"\]', body),
        }
    return routes


def parse_agent(editor: Path) -> tuple[dict[str, tuple[str, str]], dict[str, list[str]]]:
    text = strip_comments((editor / "Agent" / "ToolRegistry.cs").read_text(encoding="utf-8"))
    route_map = {m.group(1): (m.group(2), m.group(3))
                 for m in re.finditer(r'\{\s*"(\w+)",\s*\("(\w+)",\s*"([^"]+)"\)\s*\}', text)}
    schemas = {}
    for m in re.finditer(r'\bT\(\s*"(\w+)"', text):
        end = matching_brace(text, m.end(), "(", ")")
        schemas[m.group(1)] = re.findall(r'\bP\(\s*"(\w+)"', text[m.end():end])
    return route_map, schemas


# ---------------------------------------------------------------------------
# Python
# ---------------------------------------------------------------------------

def parse_python_calls() -> list[dict]:
    calls = []
    for file in sorted((PY_ROOT / "tools").glob("*.py")) + [PY_ROOT / "server.py"]:
        tree = ast.parse(file.read_text(encoding="utf-8"))
        for fn in ast.walk(tree):
            if not isinstance(fn, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            for call in ast.walk(fn):
                if not (isinstance(call, ast.Call) and isinstance(call.func, ast.Attribute)
                        and call.func.attr in ("get", "post") and call.args):
                    continue
                first = call.args[0]
                if isinstance(first, ast.Constant) and isinstance(first.value, str):
                    path = first.value
                elif isinstance(first, ast.JoinedStr):
                    path = "".join(v.value if isinstance(v, ast.Constant) else "" for v in first.values)
                else:
                    continue
                if not path.startswith("/"):
                    continue
                keys = None
                if len(call.args) > 1 and isinstance(call.args[1], ast.Dict):
                    keys = [k.value for k in call.args[1].keys if isinstance(k, ast.Constant)]
                calls.append({"file": file.name, "tool": fn.name, "method": call.func.attr.upper(),
                              "path": path.split("?")[0], "keys": keys})
    return calls


def registered_tools() -> set[str]:
    text = (PY_ROOT / "server.py").read_text(encoding="utf-8")
    return (set(re.findall(r"mcp\.tool\(\)\((\w+)\)", text))
            | set(re.findall(r"@mcp\.tool\(\)\s*\n\s*(?:async\s+)?def\s+(\w+)", text)))


# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------

def reads(fields: list[str], key: str) -> bool:
    # Newtonsoft matches exact names first, then case-insensitively.
    return key in fields or key.lower() in (f.lower() for f in fields)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("unity_path", type=Path, help="Unity project, its Assets folder, or Assets/UnityMCP")
    editor = find_editor_folder(parser.parse_args().unity_path)

    classes = parse_classes(editor)
    routes = parse_routes(editor)
    route_map, schemas = parse_agent(editor)
    calls = parse_python_calls()
    registered = registered_tools()

    problems: list[str] = []
    notes: list[str] = []

    called = set()
    for c in calls:
        key = (c["method"], c["path"])
        called.add(key)
        route = routes.get(key)
        if route is None:
            problems.append(f"{c['tool']}: no route for {c['method']} {c['path']}")
            continue
        if c["keys"] is None or route["request"] is None:
            continue
        fields = all_fields(classes, route["request"])
        if not fields:
            notes.append(f"{c['tool']}: could not read the fields of {route['request']}")
            continue
        ignored = [k for k in c["keys"] if not reads(fields, k)]
        if ignored:
            problems.append(f"{c['tool']}: Unity ignores {ignored} ({route['request']} reads {fields})")

    tool_functions = {c["tool"] for c in calls}
    for name in sorted(tool_functions - registered):
        problems.append(f"{name}: not registered in server.py")

    for key, route in routes.items():
        if key not in called:
            notes.append(f"route with no Python tool: {key[0]} {key[1]} ({route['handler']})")

    for tool, (method, path) in route_map.items():
        route = routes.get((method, path))
        if route is None:
            problems.append(f"agent {tool}: no route for {method} {path}")
            continue
        if tool not in schemas:
            notes.append(f"agent {tool}: in the route map but has no schema, so the model cannot call it")
            continue
        if route["request"]:
            fields = all_fields(classes, route["request"])
            ignored = [k for k in schemas[tool] if fields and not reads(fields, k)]
        else:
            ignored = [k for k in schemas[tool] if method == "GET" and k not in route["query"]]
        if ignored:
            problems.append(f"agent {tool}: Unity ignores {ignored}")
    for tool in schemas:
        if tool not in route_map:
            problems.append(f"agent {tool}: has a schema but no route")
    for name in sorted(tool_functions - set(route_map) - {"ping_unity"}):
        notes.append(f"agent does not offer {name}")

    print(f"Checked {len(calls)} Python calls, {len(routes)} routes, {len(route_map)} agent tools.\n")
    print(f"Mismatches ({len(problems)}):")
    for p in problems:
        print(f"  {p}")
    print(f"\nCoverage notes ({len(notes)}):")
    for n in notes:
        print(f"  {n}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
