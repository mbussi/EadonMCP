"""
Open world tools for the Unity MCP Bridge.

Covers the three pain points specific to streaming open-world development:

  query_terrain      — read terrain height, normal, slope, and layer weights at
                       world positions before placing objects
  bake_navmesh       — trigger NavMeshSurface builds after layout changes
  set_navmesh_area   — assign NavMesh area types to objects or create modifier volumes
  get_loaded_scenes  — list all additive scenes currently open in the editor
  manage_scene       — load, unload, move objects between, or create new scenes

Typical end-of-dressing workflow:
  1. query_terrain  → validate positions before proposing placements
  2. dress_area / place_prefab / scatter_prefabs
  3. set_navmesh_area → tag water, roads, restricted zones
  4. bake_navmesh(all=True) → keep navmesh current
"""

from __future__ import annotations

import json
from typing import Optional

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def query_terrain(
    positions: list[list[float]],
    include_layer_weights: bool = True,
) -> str:
    """
    Sample terrain height, surface normal, slope, and layer weights at one or more
    world-space positions.

    CALL THIS BEFORE PLACING OBJECTS to validate that positions are suitable:
      - slope_degrees > 30–35 typically means too steep for buildings and fences
      - layer_weights shows what texture is painted there (grass, road, water, etc.)
      - surface_height gives the actual Y coordinate to snap objects to terrain level

    Positions outside all active terrain tiles return error="NO_TERRAIN" with null
    fields. The rest of the batch succeeds normally — partial terrain coverage is fine.

    Parameters
    ----------
    positions : list of [x, y, z]
        World-space positions to sample. The Y component is ignored for sampling
        but echoed back in the response. Pass all candidate positions in one call.
    include_layer_weights : bool
        Whether to return per-layer alpha weights (default True). Set False when
        you only need height/normal to reduce overhead on high-resolution terrain.

    Returns per-position
    --------------------
    surface_height : float  — Y in world space at that point
    normal         : [x,y,z] — interpolated surface normal (unit vector)
    slope_degrees  : float  — steepness in degrees (0 = flat, 90 = cliff)
    terrain_name   : str    — name of the Terrain tile covering this position
    layer_weights  : [{layer_name, weight}] — terrain layer alpha values (sum ≈ 1.0)
    error          : str | null — "NO_TERRAIN" if no tile covers the position
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/query_terrain", {
            "positions":             positions,
            "include_layer_weights": include_layer_weights,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def bake_navmesh(
    surface_paths:   Optional[list[str]] = None,
    all:             bool                = False,
    agent_type_name: Optional[str]       = None,
) -> str:
    """
    Build one or more NavMeshSurface components in the active scene.

    Requires the Unity AI Navigation package (com.unity.ai.navigation).
    Returns PACKAGE_NOT_INSTALLED if the package is missing.

    Call this at the end of any operation that adds, moves, or removes objects
    with geometry that affects walkable surfaces — including dress_area,
    scatter_prefabs, place_prefab, and set_navmesh_area.

    Selection priority (first matching rule wins):
      1. surface_paths — bake the specific surfaces on those hierarchy paths
      2. agent_type_name — bake all surfaces for that agent type (e.g. "Humanoid")
      3. all=True — bake every NavMeshSurface in the active scene
      4. default — bake only the first surface found

    For most open-world dressing workflows, all=True is the correct choice.
    Use surface_paths or agent_type_name only when you know which tile(s) changed.

    Parameters
    ----------
    surface_paths   : list of hierarchy paths to NavMeshSurface GameObjects
    all             : bake every surface in the scene (default False)
    agent_type_name : only bake surfaces matching this agent type name
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/bake_navmesh", {
            "surface_paths":   surface_paths,
            "all":             all,
            "agent_type_name": agent_type_name,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def set_navmesh_area(
    area_name:     str,
    object_paths:  Optional[list[str]] = None,
    override_area: bool                = True,
    volume:        Optional[dict]      = None,
) -> str:
    """
    Assign a NavMesh area type to objects (via NavMeshModifier component) or create
    a floating volumetric override (via NavMeshModifierVolume).

    Requires the Unity AI Navigation package (com.unity.ai.navigation).

    area_name must match a name in Project Settings > Navigation (or Navigation > Areas
    in the AI Navigation window). Common built-in names: "Walkable", "Not Walkable",
    "Jump". Custom names (e.g. "Water", "Road") are defined per-project.

    On unknown area_name, the response includes a 'warnings' list of all available
    names — check that to correct the call.

    Always call bake_navmesh after to apply the changes to the NavMesh data.

    Parameters
    ----------
    area_name     : NavMesh area name (case-sensitive, must match project settings)
    object_paths  : hierarchy paths of existing objects to add NavMeshModifier to
    override_area : whether the modifier overrides child hierarchy areas (default True)
    volume        : dict to create a NavMeshModifierVolume instead of a component.
                    Shape: {
                      "position": [x, y, z],   # world-space centre
                      "size":     [w, h, d],    # extents in world units
                      "rotation": [x, y, z],   # optional Euler angles
                      "name":     "...",        # optional GameObject name
                      "parent_path": "..."      # optional hierarchy parent
                    }

    Example — mark a water plane and block a courtyard:
        set_navmesh_area("Water",        object_paths=["/World/WaterPlane"])
        set_navmesh_area("Not Walkable", volume={"position":[0,1,0], "size":[20,4,20]})
        bake_navmesh(all=True)
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/set_navmesh_area", {
            "area_name":     area_name,
            "object_paths":  object_paths,
            "override_area": override_area,
            "volume":        volume,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def get_loaded_scenes() -> str:
    """
    List all scenes currently loaded in the Unity Editor.

    Returns name, asset path, active status, and dirty flag for each.
    Use this before manage_scene to understand the current state:
      - is_active: True for the scene that receives new objects by default
      - is_dirty:  True if the scene has unsaved changes

    In a streaming open-world setup, you typically have one or two terrain tile
    scenes loaded additively alongside the base scene. This call shows you exactly
    what is open before you load, unload, or move objects between scenes.
    """
    client = UnityClient.instance()
    try:
        result = await client.get("/scene/scenes")
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def manage_scene(
    operation:         str,
    scene_path:        Optional[str]       = None,
    set_active:        Optional[bool]      = None,
    object_paths:      Optional[list[str]] = None,
    target_scene_name: Optional[str]       = None,
    save_after_create: Optional[bool]      = None,
) -> str:
    """
    Multi-scene operations in the editor: list, load (additively), unload or
    create scenes, and move objects from one loaded scene to another.

    Operations
    ----------
    "list"
        Same as get_loaded_scenes — returns all loaded scenes.
        No additional parameters required.

    "load"
        Load a scene additively alongside the current ones.
        scene_path  : required, e.g. "Assets/Scenes/Village_NW.unity"
        set_active  : optional, make it the active scene (default False)

    "unload"
        Remove an additively-loaded scene from the editor.
        scene_path  : required — asset path or scene name
        Fails if this is the only loaded scene.

    "move_objects"
        Move one or more scene objects from their current scene into another.
        object_paths      : required — list of hierarchy paths to move
        target_scene_name : required — name of the destination scene (must be loaded)
        Objects with parents NOT in the move list are detached first (a warning is
        returned for each detachment).

    "create"
        Create a new empty scene asset and load it additively.
        scene_path        : required, e.g. "Assets/Scenes/Village_SE.unity"
        set_active        : optional, make it the active scene (default False)
        save_after_create : optional, write the .unity file immediately (default True)

    Common workflow — fix misplaced objects across a tile boundary:
        get_loaded_scenes()
        manage_scene("load", scene_path="Assets/Scenes/Village_NW.unity")
        find_objects(name_pattern="Barrel*", near_position=[...])
        manage_scene("move_objects",
                     object_paths=[...],
                     target_scene_name="Village_NE")
        manage_scene("unload", scene_path="Assets/Scenes/Village_NW.unity")
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/scene/manage", {
            "operation":         operation,
            "scene_path":        scene_path,
            "set_active":        set_active,
            "object_paths":      object_paths,
            "target_scene_name": target_scene_name,
            "save_after_create": save_after_create,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})



async def paint_terrain_layer(
    position:    list[float],
    radius:      float,
    layer_name:  str,
    opacity:     float           = 0.5,
    falloff:     str             = "smooth",
    blend_mode:  str             = "add",
    terrain_name:Optional[str]  = None,
) -> str:
    """
    Paint a terrain texture layer at a world position using a circular brush.

    All layer weights are renormalised after painting so the alphamap sums to 1.0
    at every affected pixel — you cannot end up with an invalid terrain.

    Best used immediately after executing a dress_area proposal, to paint the
    terrain layer that matches the placed props:
      - Place rocks and gravel → paint "RockyGround" under them
      - Place trees and undergrowth → paint "ForestFloor"
      - Dress a dirt path with weeds → paint "DirtPath" along the centre

    Parameters
    ----------
    position     : [x, y, z] world-space brush centre. Y is ignored for painting.
    radius       : brush radius in world metres.
    layer_name   : terrain layer to paint. Must match a layer name on the terrain
                   tile (case-insensitive). If the name is wrong, the error response
                   lists all available layer names.
    opacity      : brush strength 0.0–1.0 (default 0.5). How strongly to paint
                   at the brush centre.
    falloff      : brush edge falloff.
                   "smooth"   — S-curve ease-in/ease-out (default, most natural)
                   "linear"   — even fade from centre to edge
                   "constant" — full opacity everywhere inside the radius (hard edge)
    blend_mode   : how opacity interacts with existing layer value.
                   "add" — increase the layer toward opacity (default, safe for
                           layering multiple passes)
                   "set" — force the layer to exactly opacity at the brush centre,
                           scaling all others down proportionally
    terrain_name : name of the terrain tile to paint on. Omit to use the tile that
                   covers position (correct for single-terrain scenes and most
                   tiled setups).

    Returns
    -------
    { terrain_name, layer_painted, layer_index, pixels_affected, warnings }

    Example — paint after dressing a forest clearing
    -------------------------------------------------
    # After executing a dress_area proposal for scattered trees and undergrowth:
    paint_terrain_layer([60, 0, 40], radius=18,
                        layer_name="ForestFloor", opacity=0.7, blend_mode="set")
    paint_terrain_layer([60, 0, 40], radius=22,
                        layer_name="Grass",       opacity=0.4, blend_mode="add")
    # Centre is pure ForestFloor; outer ring blends back to Grass
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/paint_terrain_layer", {
            "position":     position,
            "radius":       radius,
            "layer_name":   layer_name,
            "opacity":      opacity,
            "falloff":      falloff,
            "blend_mode":   blend_mode,
            "terrain_name": terrain_name,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def bake_lighting(
    mode:                   str  = "async",
    bake_reflection_probes: bool = False,
) -> str:
    """
    Trigger Unity's progressive lightmapper or clear baked lighting data.

    The lighting equivalent of bake_navmesh. Call at the end of any session
    that makes significant geometry or lighting changes — new buildings, moved
    lights, added prop clusters, or changed terrain.

    Modes
    -----
    "async" (default)
        Triggers a bake and returns immediately with started=true. Monitor
        progress in Window > Rendering > Lighting. This is the correct mode
        for most sessions — bakes on large scenes take minutes and should not
        block the conversation.

    "sync"
        Blocks until the bake is complete and returns elapsed_ms. Use only for
        small scenes or targeted re-bakes where you need to confirm completion
        before continuing. Will appear to hang Claude for the bake duration.

    "clear"
        Deletes all baked lighting data from the scene without re-baking.
        Useful before a full rework of the lighting setup.

    Parameters
    ----------
    mode                   : "async" | "sync" | "clear" (default "async")
    bake_reflection_probes : True = also capture reflection probe snapshots
                             (sync mode: calls BakeAllReflectionProbesSnapshots
                              after the main bake; async mode: probes configured
                              for baked mode are captured automatically)

    Returns
    -------
    { mode, started, completed?, elapsed_ms?, message }

    Example — end-of-session bake
    ------------------------------
    # After a full dressing session:
    bake_navmesh(all=True)    # NavMesh first — fast
    bake_lighting(mode="async", bake_reflection_probes=True)
    # → both bakes triggered, both monitored in Unity background
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/bake_lighting", {
            "mode":                   mode,
            "bake_reflection_probes": bake_reflection_probes,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
