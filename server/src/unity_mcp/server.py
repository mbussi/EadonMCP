"""Unity MCP Bridge — server entry point."""

import argparse
import hmac
import json
import os
import sys

from mcp.server.fastmcp import FastMCP
from mcp.server.transport_security import TransportSecuritySettings

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError
from unity_mcp.tools.scene import get_scene_hierarchy, get_scene_context, get_prefab_catalog, get_zone_context
from unity_mcp.tools.placement import place_prefab, set_transform, delete_object, create_empty, replace_prefab
from unity_mcp.tools.lighting import create_light, set_ambient_light, set_skybox, set_fog, create_reflection_probe
from unity_mcp.tools.proposal import propose_changes, execute_proposal, discard_proposal, get_pending_proposals
from unity_mcp.tools.procedural import place_prefab_batch, scatter_prefabs, place_along_path, fill_grid
from unity_mcp.tools.components import get_component_catalog, add_component_to_object
from unity_mcp.tools.catalog import list_unity_tools, call_unity_tool
from unity_mcp.tools.scaffold import get_scaffold_zones, get_scaffold_anchors, get_scaffold_markers, create_zone_scaffold, link_zones, create_reference_point, create_gameplay_marker, update_gameplay_marker, hide_scaffold, show_scaffold, resolve_scaffold_markers, execute_bake, create_patrol_route, get_patrol_routes
from unity_mcp.tools.cutscene import get_cutscene_templates, create_cutscene_sequence, preview_cutscene_sequence, update_cutscene_sequence
from unity_mcp.tools.objects import find_objects, get_object_properties, set_object_properties, set_object_metadata, reparent_objects, duplicate_objects
from unity_mcp.tools.dressing import snap_to_surface, align_and_distribute, randomize_transforms, group_objects, create_trigger_volume, apply_material, dress_area
from unity_mcp.tools.open_world import query_terrain, bake_navmesh, set_navmesh_area, get_loaded_scenes, manage_scene, paint_terrain_layer, bake_lighting
from unity_mcp.tools.production_quality import validate_scene, create_audio_zone, create_postprocessing_volume
from unity_mcp.tools.metadata import create_prefab_metadata, update_prefab_metadata, update_scene_context
from unity_mcp.tools.debug import capture_scene_view
from unity_mcp.tools.project_files import write_project_file, read_project_file, list_project_files, refresh_assets

mcp = FastMCP("unity-mcp")

@mcp.tool()
async def ping_unity() -> str:
    """Verify the connection to the Unity Editor and return basic scene information."""
    try:
        data    = await UnityClient.instance().get("/status")
        scene   = data.get("scene_name") or "untitled"
        path    = data.get("scene_path") or "unsaved"
        version = data.get("unity_version", "unknown")
        playing = data.get("is_playing", False)
        return (
            f"Unity {version} is running. "
            f"Active scene: '{scene}' ({path}). "
            f"Play mode: {'active' if playing else 'inactive'}."
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"

mcp.tool()(get_scene_hierarchy)
mcp.tool()(get_scene_context)
mcp.tool()(get_prefab_catalog)

mcp.tool()(place_prefab)
mcp.tool()(set_transform)
mcp.tool()(delete_object)
mcp.tool()(create_empty)

mcp.tool()(create_light)
mcp.tool()(set_ambient_light)
mcp.tool()(set_skybox)
mcp.tool()(set_fog)

mcp.tool()(propose_changes)
mcp.tool()(execute_proposal)
mcp.tool()(discard_proposal)
mcp.tool()(get_pending_proposals)

mcp.tool()(place_prefab_batch)
mcp.tool()(scatter_prefabs)
mcp.tool()(place_along_path)
mcp.tool()(fill_grid)

mcp.tool()(get_component_catalog)
mcp.tool()(add_component_to_object)

# Tools registered by Unity extensions (e.g. the PCG tools) are reached through these two.
mcp.tool()(list_unity_tools)
mcp.tool()(call_unity_tool)

mcp.tool()(get_scaffold_zones)
mcp.tool()(get_scaffold_anchors)
mcp.tool()(get_scaffold_markers)
mcp.tool()(create_zone_scaffold)
mcp.tool()(link_zones)
mcp.tool()(create_reference_point)
mcp.tool()(create_gameplay_marker)
mcp.tool()(update_gameplay_marker)
mcp.tool()(hide_scaffold)
mcp.tool()(show_scaffold)
mcp.tool()(resolve_scaffold_markers)
mcp.tool()(execute_bake)

mcp.tool()(get_cutscene_templates)
mcp.tool()(create_cutscene_sequence)
mcp.tool()(preview_cutscene_sequence)
mcp.tool()(update_cutscene_sequence)

mcp.tool()(find_objects)
mcp.tool()(get_object_properties)
mcp.tool()(set_object_properties)
mcp.tool()(set_object_metadata)
mcp.tool()(reparent_objects)
mcp.tool()(duplicate_objects)

mcp.tool()(snap_to_surface)
mcp.tool()(align_and_distribute)
mcp.tool()(randomize_transforms)
mcp.tool()(group_objects)
mcp.tool()(create_trigger_volume)
mcp.tool()(apply_material)
mcp.tool()(dress_area)

mcp.tool()(query_terrain)
mcp.tool()(bake_navmesh)
mcp.tool()(set_navmesh_area)
mcp.tool()(get_loaded_scenes)
mcp.tool()(manage_scene)

mcp.tool()(validate_scene)
mcp.tool()(create_audio_zone)
mcp.tool()(create_postprocessing_volume)

mcp.tool()(create_prefab_metadata)
mcp.tool()(update_prefab_metadata)
mcp.tool()(update_scene_context)
mcp.tool()(get_zone_context)

mcp.tool()(paint_terrain_layer)
mcp.tool()(bake_lighting)
mcp.tool()(replace_prefab)

mcp.tool()(create_reflection_probe)
mcp.tool()(create_patrol_route)
mcp.tool()(get_patrol_routes)

mcp.tool()(capture_scene_view)

mcp.tool()(write_project_file)
mcp.tool()(read_project_file)
mcp.tool()(list_project_files)
mcp.tool()(refresh_assets)

# ---------------------------------------------------------------------------
# Network transport security
# ---------------------------------------------------------------------------

SERVER_TOKEN_ENV_VAR = "UNITY_MCP_SERVER_TOKEN"
LOOPBACK_HOSTS = ("127.0.0.1", "localhost", "::1")


class BearerTokenMiddleware:
    """
    ASGI middleware for the sse / streamable-http transports: every HTTP request
    must carry "Authorization: Bearer <UNITY_MCP_SERVER_TOKEN>". Lifespan and
    other non-HTTP events pass straight through.
    """

    def __init__(self, app, token: str) -> None:
        self.app = app
        self._token = token.encode("utf-8")

    async def __call__(self, scope, receive, send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        auth = b""
        for name, value in scope.get("headers") or []:
            if name.lower() == b"authorization":
                auth = value
                break

        supplied = auth[7:].strip() if auth[:7].lower() == b"bearer " else b""
        if supplied and hmac.compare_digest(supplied, self._token):
            await self.app(scope, receive, send)
            return

        body = json.dumps({
            "error": "unauthorized",
            "message": f"Send 'Authorization: Bearer <token>' with the value of {SERVER_TOKEN_ENV_VAR} "
                       "set on the machine running this MCP server.",
        }).encode("utf-8")
        await send({
            "type": "http.response.start",
            "status": 401,
            "headers": [(b"content-type", b"application/json"), (b"content-length", str(len(body)).encode())],
        })
        await send({"type": "http.response.body", "body": body})


def run_network(transport: str, host: str, port: int, token: str) -> None:
    """Run the sse / streamable-http transport, requiring the server token when one is set."""
    import uvicorn

    mcp.settings.host = host
    mcp.settings.port = port
    if host not in LOOPBACK_HOSTS:
        # FastMCP's DNS-rebinding protection only allows localhost Host headers,
        # which would refuse every LAN client. The bearer token protects this mode.
        mcp.settings.transport_security = TransportSecuritySettings(enable_dns_rebinding_protection=False)

    app = mcp.sse_app() if transport == "sse" else mcp.streamable_http_app()
    if token:
        app = BearerTokenMiddleware(app, token)

    uvicorn.Server(uvicorn.Config(app, host=host, port=port, log_level=mcp.settings.log_level.lower())).run()


def main() -> None:
    parser = argparse.ArgumentParser(
        prog="unity-mcp",
        description=(
            "Unity MCP Bridge server.\n\n"
            "Run without arguments for local use with Claude Desktop (stdio).\n"
            "Use --transport sse to expose the server over the network so a remote\n"
            "Claude Desktop can connect via URL."
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--transport",
        choices=["stdio", "sse", "streamable-http"],
        default=os.environ.get("UNITY_MCP_TRANSPORT", "stdio"),
        help=(
            "MCP transport. "
            "'stdio' (default) is launched as a subprocess by Claude Desktop on the same machine. "
            "'sse' starts an HTTP server that a remote Claude Desktop can connect to via URL. "
            "'streamable-http' is an alternative HTTP transport supported by newer MCP clients. "
            "Env var: UNITY_MCP_TRANSPORT."
        ),
    )
    parser.add_argument(
        "--host",
        default=os.environ.get("UNITY_MCP_SERVER_HOST", "127.0.0.1"),
        help=(
            "Interface to bind to for sse/streamable-http transport. "
            "Default '127.0.0.1' accepts connections from this machine only. "
            "Use '0.0.0.0' to accept connections from other machines on the network; "
            f"that requires the {SERVER_TOKEN_ENV_VAR} environment variable. "
            "Env var: UNITY_MCP_SERVER_HOST."
        ),
    )
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.environ.get("UNITY_MCP_SERVER_PORT", "8000")),
        help=(
            "Port to listen on for sse/streamable-http transport (default: 8000). "
            "Must be open in any firewall on this machine. "
            "Env var: UNITY_MCP_SERVER_PORT."
        ),
    )
    args = parser.parse_args()

    if args.transport == "stdio":
        mcp.run(transport="stdio")
        return

    token = os.environ.get(SERVER_TOKEN_ENV_VAR, "").strip()
    if args.host not in LOOPBACK_HOSTS and not token:
        print(
            f"[unity-mcp] Refusing to listen on {args.host}: other machines could call every tool "
            f"without a password. Set {SERVER_TOKEN_ENV_VAR} to a long random value and give the "
            "same value to each client as 'Authorization: Bearer <token>'. "
            "Or bind to 127.0.0.1 for this machine only.",
            file=sys.stderr,
            flush=True,
        )
        sys.exit(2)

    endpoint = "/sse" if args.transport == "sse" else mcp.settings.streamable_http_path
    shown_host = "<this-machine-ip>" if args.host not in LOOPBACK_HOSTS else args.host
    print(
        f"[unity-mcp] Starting {args.transport.upper()} server on {args.host}:{args.port}"
        + (f" (clients must send the {SERVER_TOKEN_ENV_VAR} token)" if token else ""),
        flush=True,
    )
    print(f"[unity-mcp] Client URL: http://{shown_host}:{args.port}{endpoint}", flush=True)
    run_network(args.transport, args.host, args.port, token)


if __name__ == "__main__":
    main()
