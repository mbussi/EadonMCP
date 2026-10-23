"""
Generic tools for the tools that Unity extensions register with the bridge.

Extensions (for example the PCG tools, or a game's own tools) register their
tools inside Unity, so this server has no function for each of them. These
two tools reach all of them:

  list_unity_tools  — list the extension tools, or describe one in full
  call_unity_tool   — run an extension tool by name
"""

from __future__ import annotations

import json
import urllib.parse
from typing import Any

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def list_unity_tools(name: str | None = None) -> str:
    """
    List the extra tools that Unity extensions provide, or describe one of them in full.

    Without a name: returns every extension tool installed in this Unity project,
    each with name, summary, input_schema and read_only.
    With a name: returns that tool's full description (usage guide and examples)
    and its input_schema. Read it before the first call to a tool you don't know.

    Run the tools with call_unity_tool. The bridge's own tools (placement,
    lighting, scaffolding, …) are not listed here: they are regular tools.

    name: an extension tool's name, e.g. "create_pcg_graph" (optional)
    """
    try:
        if name:
            data = await UnityClient.instance().get(
                f"/tools/describe?name={urllib.parse.quote(name)}")
        else:
            data = await UnityClient.instance().get("/tools/catalog?source=extensions")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def call_unity_tool(name: str, arguments: dict[str, Any] | None = None) -> str:
    """
    Run a tool that a Unity extension provides, by name.

    Find the tools and their arguments with list_unity_tools. Pass the arguments
    as an object matching the tool's input_schema.

    name:      the extension tool's name, e.g. "validate_pcg_graph"
    arguments: the tool's arguments, e.g. {"pcgg_json": "..."}

    Returns the tool's result as JSON.
    """
    try:
        data = await UnityClient.instance().post("/tools/call", {
            "name":      name,
            "arguments": arguments or {},
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
