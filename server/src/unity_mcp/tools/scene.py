"""
Scene inspection tools — Phase 2.

Read-only tools that give the AI the context it needs before acting.
These should typically be called in this order at the start of a session:
  1. get_scene_context   — understand the level's design intent
  2. get_prefab_catalog  — discover what assets are available
  3. get_scene_hierarchy — understand what already exists in the scene
"""

import json
import urllib.parse

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def get_scene_hierarchy(max_depth: int = -1) -> str:
    """
    Return the full object tree of the active scene.

    Each node includes the object's name, full hierarchy path, world-space
    position/rotation and local scale, active state, and component type names.
    Children are nested under their parents.

    Object paths (every tool's object_path / parent_path): names joined by '/',
    e.g. "Village/Props/Barrel". Siblings that share a name get a 1-based index,
    "Village/Props/Barrel[2]", in the paths tools return and accept; a path
    without an index that matches several objects fails with AMBIGUOUS_PATH and
    lists the exact paths. The active scene is searched first, then the other
    loaded scenes; "Dungeon:Props/Torch" names a scene explicitly. Paths of
    objects outside the active scene are returned with that prefix.

    Call this before any placement operation to understand what already exists
    in the scene and to obtain valid parent_path values for new objects.

    max_depth: How many levels of children to include. -1 means no limit.
               Use 1 or 2 for a quick structural overview of large scenes.
    """
    try:
        suffix = f"?max_depth={max_depth}" if max_depth >= 0 else ""
        data = await UnityClient.instance().get(f"/scene/hierarchy{suffix}")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_scene_context() -> str:
    """
    Return the active SceneContext metadata for the current level.

    SceneContext is a ScriptableObject authored by the designer that describes
    the level's setting (city, dungeon, forest...), architectural style, era,
    preferred asset tags, and tags to avoid. It also carries freeform design
    notes intended to guide AI decisions.

    Read this before selecting prefabs so that all choices are consistent with
    the level's design intent. If no SceneContext has been configured, the
    response explains how to create one.
    """
    try:
        data = await UnityClient.instance().get("/scene/context")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_prefab_catalog(search_path: str = "Assets") -> str:
    """
    Return all prefabs found under search_path, enriched with PrefabMetadata
    where available.

    Each entry with metadata includes: asset path, display name, category,
    tags, plain-English description, compatible and incompatible tags, and
    placement rules (spacing, surface alignment, indoor/outdoor constraints).
    Prefabs without metadata are also listed with their path and name only.

    Use this alongside get_scene_context to identify the most appropriate
    prefabs before calling any placement tool.

    search_path: AssetDatabase folder to search. Defaults to all of Assets.
                 Narrow it to speed up large projects, e.g. "Assets/Prefabs/City".
    """
    try:
        encoded = urllib.parse.quote(search_path)
        data = await UnityClient.instance().get(f"/assets/prefabs?search_path={encoded}")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"




async def get_zone_context(zone_name: str) -> str:
    """
    Return the SceneContext for a specific scaffold zone.

    Looks up the zone name in McpBridgeSettings.zoneContexts. If a matching
    entry exists, returns that zone's dedicated context. If not, falls back
    to the global SceneContext (same as get_scene_context).

    Use this when working inside a named scaffold zone that has its own
    design identity distinct from the rest of the scene — for example, a
    'Forest_Ruins' zone in a tile that also contains a 'Village_Center' zone.

    Parameters
    ----------
    zone_name : name of the scaffold zone (must match a ScaffoldZone name and
                a ZoneContextEntry.zoneName in McpBridgeSettings exactly)

    Returns
    -------
    Same shape as get_scene_context — level_name, setting, architectural_style,
    era, active_tags, excluded_tags, notes — plus zone_name in the response
    when a zone-specific context was found.

    Workflow
    --------
    # Before dressing a zone with its own design identity:
    get_zone_context("Zone3_Ruins")
    # → returns ruined-specific active_tags even if the global context
    #   is set to a broader 'medieval village' setting
    """
    client = UnityClient.instance()
    try:
        result = await client.get(f"/scene/context?zone_name={urllib.parse.quote(zone_name)}")
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
