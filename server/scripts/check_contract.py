"""
Check that the Python tools, the tool catalog and the Unity bridge agree.

Reads the C# of the Unity bridge (routes in McpRouter.cs, request types and
their [JsonProperty] names, the tools extensions register with
McpToolRegistry), the built-in tool catalog (Editor/Catalog/builtin-tools.json)
and the Python tools in this repo, then reports:

  - Python calls to a route the bridge does not have
  - keys a Python tool sends that the request type does not read
    (Unity ignores them silently)
  - Python tool functions that are not registered in server.py
  - a built-in tool catalog that is out of date with the Python docstrings
    (run scripts/export_tool_catalog.py)
  - catalog tools whose route does not exist
  - catalog tool arguments the route does not read: the in-editor agent and
    call_unity_tool send the arguments exactly as the schema names them
  - bridge routes no tool reaches

Usage (from server/):
    uv run python scripts/check_contract.py            # the repo's unity-package and extensions
    uv run python scripts/check_contract.py <path>     # another copy of the package

The path can be the package folder (the one with package.json and Editor/), the
repository root, or an older project layout (Assets/UnityMCP with Source/Editor).
Routes and tools that extensions register (McpRouteRegistry, McpToolRegistry)
are read from the repository's extensions/ folder.
Exits with 1 when it finds a mismatch, 0 otherwise. Coverage gaps (routes no
tool reaches) are listed but do not fail the check.

This is a text-level check, not a compiler: it reads the code with regular
expressions and the Python ast module, so unusual formatting can hide a call.
"""

from __future__ import annotations

import argparse
import ast
import json
import re
import sys
from pathlib import Path

SERVER_ROOT = Path(__file__).resolve().parent.parent
PY_ROOT = SERVER_ROOT / "src" / "unity_mcp"
REPO_ROOT = SERVER_ROOT.parent
DEFAULT_PACKAGE = REPO_ROOT / "unity-package"
EXTENSIONS_ROOT = REPO_ROOT / "extensions"


# ---------------------------------------------------------------------------
# C#
# ---------------------------------------------------------------------------

def find_editor_folder(path: Path) -> Path:
    candidates = [path / "Editor", path / "unity-package" / "Editor"]
    for old in (path, path / "UnityMCP", path / "Assets" / "UnityMCP"):
        candidates.append(old / "Source" / "Editor")
    for editor in candidates:
        if (editor / "Core" / "McpRouter.cs").exists():
            return editor
    sys.exit(f"Could not find Editor/Core/McpRouter.cs under {path}")


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


def parse_classes(folders: list[Path]) -> dict[str, dict]:
    """class name -> {"base": name or None, "fields": [json names]}"""
    classes: dict[str, dict] = {}
    files = [f for folder in folders if folder.exists() for f in folder.rglob("*.cs")]
    for file in files:
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


def parse_routes(editor: Path, extensions: Path) -> dict[tuple[str, str], dict]:
    text = strip_comments((editor / "Core" / "McpRouter.cs").read_text(encoding="utf-8"))
    routes = {}
    for m in re.finditer(r'method\s*==\s*"(\w+)"\s*&&\s*path\s*==\s*"([^"]+)"\s*\)([^;]*;)', text):
        routes[(m.group(1), m.group(2))] = {"handler": "McpRouter", "request": None,
                                            "query": re.findall(r'QueryString\["(\w+)"\]', m.group(3))}
    pattern = r'method\s*==\s*"(\w+)"\s*&&\s*path\s*==\s*"([^"]+)"\s*\)\s*return\s+await\s+([\w.]+)\(([^;]*);'
    for m in re.finditer(pattern, text, flags=re.S):
        body = m.group(4)
        req = re.search(r"ParseBodyAsync<(\w+)>", body)
        routes[(m.group(1), m.group(2))] = {
            "handler": m.group(3),
            "request": req.group(1) if req else None,
            "query": re.findall(r'QueryString\["(\w+)"\]', body),
        }
    # Routes registered by extensions: McpRouteRegistry.Register("METHOD", "/path", handler, owner);
    if extensions.exists():
        for file in extensions.rglob("*.cs"):
            ext = strip_comments(file.read_text(encoding="utf-8", errors="replace"))
            for m in re.finditer(r'McpRouteRegistry\.Register\(\s*"(\w+)"\s*,\s*"([^"]+)"\s*,(.*?)\);', ext, flags=re.S):
                body = m.group(3)
                req = re.search(r"ParseBodyAsync<(\w+)>", body)
                routes[(m.group(1).upper(), m.group(2))] = {
                    "handler": f"{file.parent.parent.name} extension",
                    "request": req.group(1) if req else None,
                    "query": re.findall(r'QueryString\["(\w+)"\]', body),
                }
    # Extension tools register their own route.
    for tool in parse_extension_tools(extensions):
        routes[(tool["method"], tool["path"])] = {
            "handler": f"{tool['name']} ({tool['owner']})",
            "request": tool["request"],
            "query": list(tool["schema_keys"]),
        }
    return routes


def _string_value(text: str, field: str) -> str | None:
    m = re.search(rf'\b{field}\s*=\s*(@?)"((?:[^"]|"")*)"', text, flags=re.S)
    if not m:
        return None
    return m.group(2).replace('""', '"') if m.group(1) else m.group(2)


def parse_extension_tools(extensions: Path) -> list[dict]:
    """Tools registered with McpToolRegistry.Register(new McpToolDefinition { ... })."""
    tools = []
    if not extensions.exists():
        return tools
    for file in extensions.rglob("*.cs"):
        text = file.read_text(encoding="utf-8", errors="replace")
        for m in re.finditer(r"McpToolRegistry\.Register\(\s*new\s+McpToolDefinition\s*\{", text):
            body = text[m.end():matching_brace(text, m.end()) - 1]
            name = _string_value(body, "Name")
            schema_text = re.search(r'InputSchema\s*=\s*JObject\.Parse\(\s*@"((?:[^"]|"")*)"', body, flags=re.S)
            schema = json.loads(schema_text.group(1).replace('""', '"')) if schema_text else {}
            request = re.search(r"ToObject<(\w+)>", body)
            owner = re.search(r'Owner\s*=\s*(\w+|"[^"]*")', body)
            owner_name = owner.group(1).strip('"') if owner else "extension"
            if owner_name == "Owner":
                const = re.search(r'const\s+string\s+Owner\s*=\s*"([^"]+)"', text)
                owner_name = const.group(1) if const else "extension"
            tools.append({
                "name": name,
                "method": (_string_value(body, "Method") or "POST").upper(),
                "path": _string_value(body, "Path") or f"/tools/ext/{name}",
                "request": request.group(1) if request else None,
                "schema_keys": list((schema.get("properties") or {}).keys()),
                "owner": owner_name,
            })
    return tools


def load_catalog(editor: Path) -> list[dict]:
    """The built-in tools from Editor/Catalog/builtin-tools.json, plus describe_tool."""
    file = editor / "Catalog" / "builtin-tools.json"
    if not file.exists():
        return []
    tools = [{"name": t["name"], "method": t["method"], "path": t["path"],
              "schema_keys": list((t.get("input_schema") or {}).get("properties", {}).keys())}
             for t in json.loads(file.read_text(encoding="utf-8"))["tools"]]
    tools.append({"name": "describe_tool", "method": "GET", "path": "/tools/describe", "schema_keys": ["name"]})
    return tools


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
    parser.add_argument("unity_path", type=Path, nargs="?", default=DEFAULT_PACKAGE,
                        help="the package folder (default: the repository's unity-package)")
    editor = find_editor_folder(parser.parse_args().unity_path)

    classes = parse_classes([editor, EXTENSIONS_ROOT])
    routes = parse_routes(editor, EXTENSIONS_ROOT)
    catalog = load_catalog(editor) + parse_extension_tools(EXTENSIONS_ROOT)
    calls = parse_python_calls()
    registered = registered_tools()

    problems: list[str] = []
    notes: list[str] = []

    # The catalog must match the Python docstrings it is generated from (only for
    # this repository's own package: another copy may be an older version).
    if editor.resolve() == (DEFAULT_PACKAGE / "Editor").resolve():
        sys.path.insert(0, str(SERVER_ROOT / "src"))
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from export_tool_catalog import is_up_to_date
        if not is_up_to_date():
            problems.append("builtin-tools.json is out of date: run scripts/export_tool_catalog.py")
    if not catalog:
        problems.append(f"no tool catalog found under {editor / 'Catalog'}")

    reached = set()
    for c in calls:
        key = (c["method"], c["path"])
        reached.add(key)
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

    # Catalog tools: the agent and call_unity_tool send the schema's argument names as they are.
    for tool in catalog:
        key = (tool["method"], tool["path"])
        reached.add(key)
        route = routes.get(key)
        if route is None:
            problems.append(f"tool {tool['name']}: no route for {key[0]} {key[1]}")
            continue
        if route["request"]:
            fields = all_fields(classes, route["request"])
            ignored = [k for k in tool["schema_keys"] if fields and not reads(fields, k)]
        elif key[0] == "GET":
            ignored = [k for k in tool["schema_keys"] if k not in route["query"]]
        else:
            ignored = []
        if ignored:
            problems.append(f"tool {tool['name']}: Unity ignores {ignored}")

    # Routes the bridge serves for itself, not as a tool.
    reached |= {("GET", "/tools/catalog"), ("POST", "/tools/call")}
    for key, route in routes.items():
        if key not in reached:
            notes.append(f"route no tool reaches: {key[0]} {key[1]} ({route['handler']})")

    print(f"Checked {len(calls)} Python calls, {len(routes)} routes, {len(catalog)} catalog tools.\n")
    print(f"Mismatches ({len(problems)}):")
    for p in problems:
        print(f"  {p}")
    print(f"\nCoverage notes ({len(notes)}):")
    for n in notes:
        print(f"  {n}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
