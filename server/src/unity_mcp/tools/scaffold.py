"""
Scaffold tools — translate level design documents into Unity scene skeletons.

get_scaffold_zones       — list all ScaffoldZone instances in the scene
get_scaffold_anchors     — list all ScaffoldAnchor instances
get_scaffold_markers     — list all GameplayMarker instances
create_zone_scaffold     — create a zone with auto-generated anchor points
link_zones               — connect two zones with a transition connector
create_reference_point   — add a named anchor manually to a zone
create_gameplay_marker   — place a functional intent marker at an anchor
update_gameplay_marker   — update tags or config on an existing marker
hide_scaffold            — hide all scaffold objects (non-destructive)
show_scaffold            — restore scaffold visibility

Typical workflow for a dungeon zone from a design document:

  1. create_zone_scaffold(...)       — zone volume + auto-anchors appear in scene
  2. get_scaffold_anchors()          — confirm anchor paths (e.g. step_05)
  3. create_gameplay_marker(...)     — add enemy/trap/loot at anchor positions
  4. link_zones(...)                 — connect to adjacent zones
  5. get_scaffold_markers()          — review all markers before baking (Phase 3)
"""

import json

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def get_scaffold_zones() -> str:
    """
    Returns all ScaffoldZone instances in the active scene.

    For each zone: zone_name, zone_path, zone_type, design_note, dressing_tags,
    anchor_count, marker_count, position, rotation.

    Call this after create_zone_scaffold to confirm creation and to get the
    zone_path needed for subsequent calls.
    """
    try:
        data = await UnityClient.instance().get("/assets/scaffold/zones")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_scaffold_anchors() -> str:
    """
    Returns all ScaffoldAnchor instances in the active scene.

    For each anchor: label, anchor_path, anchor_type, position, zone_path.

    anchor_path is what you pass to create_gameplay_marker's anchor_path parameter:
      "/ForgottenTunnels/Zone1_Descent/step_05"
      "/ForgottenTunnels/Zone1_Descent/_mid"

    anchor_type values: Entry, Exit, Named, Step, WallRef, Connection
    """
    try:
        data = await UnityClient.instance().get("/assets/scaffold/anchors")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_scaffold_markers() -> str:
    """
    Returns all GameplayMarker instances in the active scene.

    For each marker: label, marker_path, marker_type, semantic_tags,
    detection_radius, bake_state (Pending/Resolved/Baked), position, zone_path.

    Use this to review all markers before starting the bake workflow, or to
    find marker_path values for update_gameplay_marker calls.
    """
    try:
        data = await UnityClient.instance().get("/assets/scaffold/markers")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_zone_scaffold(
    zone_name: str,
    zone_type: str,
    dimensions: dict,
    entry_position: list[float] | None = None,
    entry_rotation: list[float] | None = None,
    parent_path: str | None = None,
    design_note: str = "",
    dressing_tags: list[str] | None = None,
) -> str:
    """
    Creates a ScaffoldZone GameObject with auto-generated anchor points.

    ── ZONE TYPES ────────────────────────────────────────────────────────────

    SpiralStaircase  Descends on local -Y. Anchors: _entry (top), step_01…step_NN,
                     _mid, _exit (bottom). Required dims: width, step_count,
                     riser_height, turns.

    StraightCorridor Extends on local +Z. Anchors: _entry, _exit, _mid, _quarter,
                     _three_quarter, _left_wall_mid, _right_wall_mid.
                     Required dims: width, depth (length), height.

    CircularChamber  Centred on origin. Anchors: _entry (south), _exit (north),
                     _center, _far_end, _north/south/east/west, _perimeter_00…07.
                     Required dims: diameter, height.

    RectangularRoom  Origin at south face. Anchors: _entry, _exit, _center,
                     _corner_ne/nw/se/sw, _wall_*_mid ×4.
                     Required dims: width, depth, height.

    CrawlTunnel      Anchors: _entry, _mid, _exit.
                     Required dims: width, depth, height.

    OpenArea         Anchors: _center, _edge_north/south/east/west, _edge_ne/nw/se/sw.
                     Required dims: radius (circular) OR width + depth (box).

    Custom           No auto-generated anchors. Add with create_reference_point.

    ── DIMENSIONS ────────────────────────────────────────────────────────────

    Pass only the fields relevant to the chosen zone_type. Omitted fields use
    sensible defaults for that type.

    {
      "width":        3.0,   "depth":       30.0,   "height":      3.0,
      "diameter":    20.0,   "step_count":  25,     "riser_height": 0.2,
      "turns":        2.5,   "radius":      10.0
    }

    ── PARAMETERS ────────────────────────────────────────────────────────────

    zone_name:      GameObject name, e.g. "Zone1_Descent"
    zone_type:      See zone type list above
    dimensions:     Dict — only include fields relevant to this zone type
    entry_position: World-space [x, y, z] of the zone entry. Default [0, 0, 0]
    entry_rotation: Euler angles [x, y, z]. Default [0, 0, 0]
    parent_path:    Scene hierarchy path of a container, e.g. "/ForgottenTunnels"
    design_note:    Verbatim text from the design document for this zone
    dressing_tags:  Ambient fill tags for bake step, e.g. ["dungeon", "damp"]

    Returns: { zone_path, zone_type, anchors_created, anchor_paths[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/create_zone", {
            "zone_name":      zone_name,
            "zone_type":      zone_type,
            "dimensions":     dimensions,
            "entry_position": entry_position or [0.0, 0.0, 0.0],
            "entry_rotation": entry_rotation or [0.0, 0.0, 0.0],
            "parent_path":    parent_path,
            "design_note":    design_note,
            "dressing_tags":  dressing_tags or [],
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def link_zones(
    from_zone_path: str,
    to_zone_path: str,
    transition_type: str,
    dimensions: dict,
    connection_label: str,
) -> str:
    """
    Connects two ScaffoldZones with a transition connector zone.

    Creates a new ScaffoldZone of transition_type at the from_zone's _exit
    anchor, oriented toward the to_zone's _entry anchor. Updates ConnectedZones
    on all three zones.

    transition_type: any ScaffoldZoneType (usually StraightCorridor or CrawlTunnel)
    dimensions:      size of the transition zone (same dict format as create_zone_scaffold)
    connection_label: name for the connector, e.g. "Conn_Z1_Z2"

    Returns: { connection_path, from_anchor, to_anchor }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/link_zones", {
            "from_zone_path":   from_zone_path,
            "to_zone_path":     to_zone_path,
            "transition_type":  transition_type,
            "dimensions":       dimensions,
            "connection_label": connection_label,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_reference_point(
    label: str,
    anchor_type: str = "Named",
    zone_path: str | None = None,
    anchor_path: str | None = None,
    position_offset: list[float] | None = None,
    world_position: list[float] | None = None,
) -> str:
    """
    Adds a named ScaffoldAnchor to an existing zone manually.

    Use this for positions mentioned in the design document that weren't
    auto-generated, e.g. a specific alcove or off-path interaction point.

    Position resolved in priority order:
      1. anchor_path + position_offset  (relative to an existing anchor)
      2. world_position + position_offset (absolute world position)

    anchor_type values: Named, Entry, Exit, WallRef, Connection, Step

    The anchor is parented to the zone containing anchor_path, or to zone_path.

    Returns: { anchor_path, anchor_type, position }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/create_reference_point", {
            "zone_path":       zone_path,
            "label":           label,
            "anchor_type":     anchor_type,
            "anchor_path":     anchor_path,
            "position_offset": position_offset or [0.0, 0.0, 0.0],
            "world_position":  world_position,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_gameplay_marker(
    marker_label: str,
    marker_type: str,
    semantic_tags: list[str],
    anchor_path: str | None = None,
    world_position: list[float] | None = None,
    position_offset: list[float] | None = None,
    detection_radius: float = 0.0,
    config: dict | None = None,
) -> str:
    """
    Places a GameplayMarker at a position in a ScaffoldZone.

    A GameplayMarker records functional intent WITHOUT referencing any specific
    prefab. The prefab connection is made during the bake step via semantic_tags
    matched against PrefabMetadata.tags[].

    ── MARKER TYPES ──────────────────────────────────────────────────────────

    EnemySpawn     Enemy that spawns at this position
    Trap           Trap or hazard
    LootContainer  Container with items
    Interaction    Interactable object (lever, valve, door, dialogue trigger)
    AmbientDressing Environmental dressing hint for zone-level bake fill
    Custom         Any other functional intent

    ── PLACEMENT ─────────────────────────────────────────────────────────────

    Provide anchor_path OR world_position. position_offset is added to either.
    The marker is placed as a child of the zone containing anchor_path.
    Use get_scaffold_anchors to list valid anchor paths.

    ── SEMANTIC TAGS ─────────────────────────────────────────────────────────

    Matched against PrefabMetadata.tags[] during the bake resolve step.
    Be specific — more overlap = better prefab match score:
      Bad:  ["enemy"]
      Good: ["enemy", "drowner", "undead", "sound_sensitive", "blind"]

    ── CONFIG ────────────────────────────────────────────────────────────────

    Optional dict with marker-type-specific data. Stored as ConfigJson.

    EnemySpawn:
      { "enemy_type": "AncientDrowner", "health": 80, "damage": 15,
        "detection_type": "hearing", "behavior": "stationary_head_tracking" }

    Trap:
      { "trap_type": "squeaky_step", "alert_radius": 20.0,
        "trigger_condition": "walk", "crouch_reduction": 0.8,
        "fixable": true, "fix_item": "Oil",
        "fix_state_key": "squeaky_step_05_oiled" }

    LootContainer:
      { "container_type": "skeleton",
        "loot_items": ["DryTeeth_x15", "OldKey", "DrownnerEssence"] }

    ── PARAMETERS ────────────────────────────────────────────────────────────

    detection_radius: World-space radius of the trigger/detection sphere.
                      Drawn as a translucent sphere in the scene view. 0 = none.

    Returns: { marker_path, marker_type, position }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/create_marker", {
            "marker_label":     marker_label,
            "marker_type":      marker_type,
            "semantic_tags":    semantic_tags,
            "anchor_path":      anchor_path,
            "world_position":   world_position,
            "position_offset":  position_offset or [0.0, 0.0, 0.0],
            "detection_radius": detection_radius,
            "config":           config,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def update_gameplay_marker(
    marker_path: str,
    semantic_tags: list[str] | None = None,
    config: dict | None = None,
    detection_radius: float | None = None,
) -> str:
    """
    Updates an existing GameplayMarker's semantic_tags, config, or detection radius.

    Only provided fields are updated — omitted fields are unchanged.

    marker_path: exact path from create_gameplay_marker or get_scaffold_markers.

    Returns: { marker_path, updated_fields[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/update_marker", {
            "marker_path":      marker_path,
            "semantic_tags":    semantic_tags,
            "config":           config,
            "detection_radius": detection_radius,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def hide_scaffold() -> str:
    """
    Sets all root-level ScaffoldZone GameObjects inactive.

    Non-destructive — all scaffold data is preserved. Call show_scaffold to restore.

    Returns: { active: false, zones_affected, message }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/hide_scaffold", {})
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def show_scaffold() -> str:
    """
    Re-activates all root-level ScaffoldZone GameObjects. Reverses hide_scaffold.

    Returns: { active: true, zones_affected, message }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/show_scaffold", {})
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"



async def resolve_scaffold_markers(
    zone_path: str | None = None,
    marker_types: list[str] | None = None,
    max_candidates_per_marker: int = 3,
) -> str:
    """
    Matches each Pending GameplayMarker's semantic_tags against every
    PrefabMetadata asset in the project and returns ranked prefab candidates.

    Call this after the scaffold is complete and before execute_bake.
    Present the proposals to the user and wait for confirmation before baking.

    ── SCORING ──────────────────────────────────────────────────────────────

    Each prefab is scored per marker:
      tag_overlap_count   — how many of the marker's semantic_tags appear
                            in the prefab's PrefabMetadata.tags[]
      scene_context_bonus — +1 per tag that also appears in the active
                            SceneContext.activeTags (set in McpBridgeSettings)
      total_score         — overlap + bonus, used for ranking

    Candidates are returned sorted descending by total_score.
    Ties are broken alphabetically by asset_path for stable ordering.

    ── RESPONSE ─────────────────────────────────────────────────────────────

    {
      "proposals": [
        {
          "marker_path":   "/ForgottenTunnels/Zone1_Descent/AncientDrowner_Mid",
          "marker_label":  "AncientDrowner_Mid",
          "marker_type":   "EnemySpawn",
          "semantic_tags": ["enemy", "drowner", "undead"],
          "candidates": [
            {
              "asset_guid":          "abc123...",
              "asset_path":          "Assets/Prefabs/Enemies/AncientDrowner.prefab",
              "display_name":        "Ancient Drowner",
              "tag_overlap_count":   3,
              "scene_context_bonus": 1,
              "total_score":         4
            }
          ]
        }
      ],
      "unresolvable": [
        {
          "marker_path":   "...",
          "marker_label":  "SqueakyStep_05",
          "semantic_tags": ["trap", "squeaky_step"]
        }
      ]
    }

    Markers in unresolvable had zero tag matches. Either add PrefabMetadata
    to a suitable prefab, adjust the marker's semantic_tags, or skip those
    markers and handle them manually.

    ── PARAMETERS ───────────────────────────────────────────────────────────

    zone_path:                 Optional — restrict to one zone's markers.
                               Full hierarchy path, e.g. "/ForgottenTunnels/Zone1_Descent".
                               If omitted, all Pending markers in the scene are resolved.
    marker_types:              Optional — filter by type, e.g. ["EnemySpawn", "LootContainer"].
                               If omitted, all types are included.
    max_candidates_per_marker: How many ranked candidates to return per marker. Default 3.
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/resolve_markers", {
            "zone_path":                 zone_path,
            "marker_types":              marker_types,
            "max_candidates_per_marker": max_candidates_per_marker,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def execute_bake(
    confirmations: list[dict],
    hide_scaffold_after: bool = False,
) -> str:
    """
    Instantiates prefabs at confirmed GameplayMarker positions and marks each
    marker as Baked. All operations are registered with Unity's Undo system.

    Call this AFTER resolve_scaffold_markers and after the user has confirmed
    (or you have selected) which prefab candidate to use for each marker.

    ── CONFIRMATIONS FORMAT ─────────────────────────────────────────────────

    confirmations = [
      {
        "marker_path": "/ForgottenTunnels/Zone1_Descent/AncientDrowner_Mid",
        "asset_guid":  "abc123..."
      },
      {
        "marker_path": "/ForgottenTunnels/Zone1_Descent/Loot_Skeleton_Bottom",
        "asset_guid":  "def456..."
      }
    ]

    marker_path  — exact path from get_scaffold_markers or resolve_scaffold_markers
    asset_guid   — the chosen candidate's asset_guid from resolve_scaffold_markers

    ── WHAT HAPPENS ─────────────────────────────────────────────────────────

    For each confirmation:
      1. The prefab is instantiated at the marker's world position and rotation
      2. The instance is named {marker_label}_Instance
      3. The instance is parented to the same zone container as the marker
      4. The marker's bake_state is set to Baked
      5. The marker's resolved_prefab_guid is set

    Markers are NOT deleted — they stay in the scene as Baked (teal gizmo)
    to record the design intent. Use hide_scaffold() to suppress them visually.

    ── PARAMETERS ───────────────────────────────────────────────────────────

    confirmations:       List of { marker_path, asset_guid } pairs
    hide_scaffold_after: If true, set the affected zone(s) inactive after baking.
                         Reversible via show_scaffold(). Default false.

    Returns: { baked_count, instances[{ marker_label, instance_path }], warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/execute_bake", {
            "confirmations":       confirmations,
            "hide_scaffold_after": hide_scaffold_after,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_patrol_route(
    name:              str,
    waypoints:         list[dict],
    loop:              bool  | None = None,
    default_idle_time: float        = 1.0,
    zone_path:         str   | None = None,
    parent_path:       str   | None = None,
) -> str:
    """
    Create a named patrol route as a scene hierarchy structure.

    The route is created as:
        PatrolRoute_{name}/              ← container, PatrolRouteData component
            WP_00                        ← waypoint, PatrolWaypoint component
            WP_01_{label}                ← waypoint with label in its name
            ...

    This structure is framework-agnostic. Your game's AI can discover routes at
    runtime by finding GameObjects named "PatrolRoute_*" and reading child world
    positions — no dependency on the bridge's editor-only components required.

    The PatrolRouteData and PatrolWaypoint components carry design intent for the
    editor session and are stripped from player builds. See PatrolRouteComponents.cs
    for runtime integration options.

    Parameters
    ----------
    name              : route name, used as suffix on the container: "PatrolRoute_{name}"
    waypoints         : ordered list of waypoint definitions. Each is a dict with:
                          position       [x, y, z] — required
                          idle_time      float      — seconds to idle here (-1 = use default)
                          look_direction [x, y, z]  — Euler angles to face while idling
                          label          str        — optional name suffix e.g. "east_gate"
    loop              : True = last waypoint connects back to first (default True)
    default_idle_time : seconds to idle at waypoints that don't specify their own (default 1.0)
    zone_path         : hierarchy path of the ScaffoldZone to parent this route under.
                        Takes priority over parent_path. The zone must already exist.
    parent_path       : fallback parent path when zone_path is not provided

    Returns
    -------
    { route_path, waypoint_count, loop, zone_path?, waypoints: [{path, label?}] }

    Example — guard patrol in a dungeon antechamber
    ------------------------------------------------
    create_patrol_route(
        name="GuardSweep_East",
        zone_path="/Dungeon/Zone2_Antechamber",
        loop=True,
        default_idle_time=1.5,
        waypoints=[
            {"position": [4.0, 0.0, 2.0],  "label": "entry_post",  "idle_time": 3.0},
            {"position": [12.0, 0.0, 2.0]},
            {"position": [12.0, 0.0, 14.0], "label": "far_corner",
             "idle_time": 2.0, "look_direction": [0, 90, 0]},
            {"position": [4.0, 0.0, 14.0]},
        ]
    )
    """
    try:
        data = await UnityClient.instance().post("/tools/scaffold/create_patrol_route", {
            "name":               name,
            "waypoints":          waypoints,
            "loop":               loop,
            "default_idle_time":  default_idle_time,
            "zone_path":          zone_path,
            "parent_path":        parent_path,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_patrol_routes() -> str:
    """
    List all patrol routes currently in the active scene.

    Returns each route with its waypoints, positions, idle times, labels, and
    effective idle times (per-waypoint or resolved from the route default).

    Use this before create_patrol_route to check what routes already exist in
    a zone, or to review the current patrol layout before making changes.

    Returns
    -------
    { routes: [{ route_path, name, zone_path?, loop, waypoint_count,
                 waypoints: [{ path, index, position, idle_time, label?, look_direction? }] }] }
    """
    try:
        data = await UnityClient.instance().get("/assets/scaffold/patrol_routes")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
