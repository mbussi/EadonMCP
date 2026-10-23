"""
Dressing Phase 2 — precision placement tools.

These tools handle the mid-scale work between large procedural generation
and individual object placement: snapping loose props to surfaces, distributing
identical objects evenly, adding natural variation to uniform placements,
and organising the result into a tidy hierarchy container.

snap_to_surface        — raycast-snap objects to the ground or any surface
align_and_distribute   — evenly space, stack, align, or circle-arrange objects
randomize_transforms   — apply bounded random position/rotation/scale variation
group_objects          — create a named container and reparent objects into it
"""

import json

from unity_mcp.tools.components import normalise_property_values
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def snap_to_surface(
    object_paths: list[str],
    cast_direction: list[float] | None = None,
    max_distance: float = 100.0,
    surface_offset: float = 0.0,
    align_to_normal: bool = False,
    layer_mask: int | None = None,
) -> str:
    """
    Raycasts from each object's position and moves it to the hit surface.

    This fixes the most common dressing error: objects floating above the
    ground or sinking into a slope after placement.

    object_paths:    objects to snap (accepts one or many)

    cast_direction:  [x, y, z] direction vector for the raycast.
                     Default [0, -1, 0] (straight down onto the terrain/floor).
                     Use [0, 1, 0] to snap to a ceiling, or a custom direction
                     for wall snapping.

    max_distance:    how far to cast in metres (default 100)

    surface_offset:  gap to leave above the hit surface in metres (default 0).
                     Use a small positive value (e.g. 0.01) if objects are
                     sinking into the surface due to pivot placement.

    align_to_normal: rotate the object so its up-axis matches the surface
                     normal (default false). Useful for rocks and organic props
                     on slopes. Keep false for furniture and buildings.

    layer_mask:      Unity layer bitmask to restrict which surfaces are hit.
                     Null = all layers (default). Use e.g. 1 << 8 to hit only
                     the Terrain layer.

    EXAMPLES:
      # Snap a row of barrels to the ground after placement
      snap_to_surface(["/Village/Barrel_01", "/Village/Barrel_02"])

      # Snap rocks to a slope and tilt them to match the normal
      snap_to_surface(rock_paths, align_to_normal=True)

      # Snap props with a 2cm clearance (pivot is at centre, not base)
      snap_to_surface(paths, surface_offset=0.02)

    Returns: { snapped_count, skipped: [{ path, reason }] }
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/snap_to_surface", {
            "object_paths":   object_paths,
            "cast_direction": cast_direction or [0.0, -1.0, 0.0],
            "max_distance":   max_distance,
            "surface_offset": surface_offset,
            "align_to_normal": align_to_normal,
            "layer_mask":     layer_mask,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def align_and_distribute(
    object_paths: list[str],
    mode: str,
    # distribute_between
    start_point: list[float] | None = None,
    end_point: list[float] | None = None,
    face_direction: bool = True,
    # align_to_axis
    axis: str | None = None,
    value: float | None = None,
    use_pivot: str = "center",
    # stack
    stack_axis: str = "y",
    base_position: list[float] | None = None,
    # circle
    center: list[float] | None = None,
    radius: float = 0.0,
    facing_outward: bool = True,
    start_angle_degrees: float = 0.0,
) -> str:
    """
    Repositions a set of objects relative to each other using one of four modes.

    ── MODES ────────────────────────────────────────────────────────────────

    distribute_between
      Evenly spaces N objects in a straight line between two world-space
      points. If face_direction=True (default), each object is rotated to
      face along the line.

      Required: start_point=[x,y,z], end_point=[x,y,z]
      Optional: face_direction (default true)

      EXAMPLE — 6 fence posts between two corners:
        align_and_distribute(
            fence_post_paths, "distribute_between",
            start_point=[10,0,0], end_point=[10,0,20],
            face_direction=True
        )

    align_to_axis
      Shifts all objects so a specified edge of their bounding box aligns
      to a given axis value. Use this to level a row of crates to the
      same Y height, or flush a line of objects against a wall.

      Required: axis ("x"|"y"|"z"), value (float)
      Optional: use_pivot ("min"|"center"|"max", default "center")

      EXAMPLE — level all object bottoms to Y=0:
        align_and_distribute(
            crate_paths, "align_to_axis",
            axis="y", value=0.0, use_pivot="min"
        )

    stack
      Stacks objects on top of each other along the stack_axis, using each
      object's bounds to determine height so the stack doesn't float or clip.

      Optional: stack_axis ("x"|"y"|"z", default "y"),
                base_position [x,y,z] (default: first object's current position)

      EXAMPLE — stack three crates:
        align_and_distribute(
            [crate_bottom, crate_mid, crate_top], "stack",
            stack_axis="y", base_position=[5, 0, 5]
        )

    circle
      Evenly distributes objects around a horizontal circle. If
      facing_outward=True (default), each object faces away from the centre.

      Required: center=[x,y,z], radius (float)
      Optional: facing_outward (default true), start_angle_degrees (default 0)

      EXAMPLE — 8 torches around a ritual circle:
        align_and_distribute(
            torch_paths, "circle",
            center=[0, 0, 0], radius=5.0,
            facing_outward=False
        )

    ── RETURNS ──────────────────────────────────────────────────────────────
    { repositioned_count, warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/align_and_distribute", {
            "object_paths":        object_paths,
            "mode":                mode,
            "start_point":         start_point,
            "end_point":           end_point,
            "face_direction":      face_direction,
            "axis":                axis,
            "value":               value,
            "use_pivot":           use_pivot,
            "stack_axis":          stack_axis,
            "base_position":       base_position,
            "center":              center,
            "radius":              radius,
            "facing_outward":      facing_outward,
            "start_angle_degrees": start_angle_degrees,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def randomize_transforms(
    object_paths: list[str],
    seed: int | None = None,
    position_range: list[float] | None = None,
    rotation_range: list[float] | None = None,
    scale_range: list[float] | None = None,
    snap_after: bool = False,
) -> str:
    """
    Applies bounded random offsets to position, rotation, and/or scale on a
    set of objects.

    This is the difference between dressing that looks placed by a computer
    and dressing that looks like it was actually used. A row of identical barrels
    with ±15° Y rotation and ±10% scale variation reads as lived-in; without it,
    it reads as a prefab row.

    object_paths:   objects to vary (at least one of the range params required)

    seed:           integer seed for the random generator. If omitted, a random
                    seed is chosen and returned in the response so you can
                    reproduce or describe the result later.

    position_range: [x, y, z] max offset per axis (applied symmetrically ±).
                    e.g. [0.3, 0, 0.3] shuffles XZ by up to ±30cm, no Y change.

    rotation_range: [x, y, z] max rotation offset in degrees per axis.
                    e.g. [0, 180, 0]  — full random Y (good for crates, rocks)
                    e.g. [5, 180, 5]  — slight XZ tilt plus full Y (good for rocks)
                    e.g. [0, 15, 0]   — subtle ±15° Y (good for furniture)

    scale_range:    [min, max] uniform scale multiplier applied to each object
                    independently. e.g. [0.85, 1.15] gives ±15% size variation.
                    Applied as a multiplier on top of the current local scale.

    snap_after:     if True, each object is raycasted downward and snapped to
                    the surface below after randomization. Useful after adding
                    position_range that shifts objects off the ground.

    EXAMPLES:
      # Natural variation for a row of barrels
      randomize_transforms(
          barrel_paths,
          rotation_range=[0, 180, 0],
          scale_range=[0.9, 1.1]
      )

      # Rock scatter with tilt, full rotation, and size variation
      randomize_transforms(
          rock_paths,
          rotation_range=[15, 180, 15],
          scale_range=[0.6, 1.4],
          snap_after=True
      )

      # Subtle shuffle for market stall props (preserves ground contact)
      randomize_transforms(
          stall_paths,
          position_range=[0.2, 0, 0.2],
          rotation_range=[0, 20, 0],
          snap_after=True
      )

    Returns: { randomized_count, seed_used, warnings[] }
    seed_used is the seed that was actually applied — record it if you want
    to reproduce this exact variation later.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/randomize_transforms", {
            "object_paths":   object_paths,
            "seed":           seed,
            "position_range": position_range,
            "rotation_range": rotation_range,
            "scale_range":    scale_range,
            "snap_after":     snap_after,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def group_objects(
    object_paths: list[str],
    group_name: str,
    parent_path: str | None = None,
    pivot: str = "center",
    tag: str | None = None,
    layer: str | None = None,
) -> str:
    """
    Creates a new empty GameObject, positions it at the group pivot, and
    reparents all specified objects under it. The programmatic equivalent of
    selecting a set of objects and pressing Ctrl+G in the hierarchy.

    After a dress_area pass or a scatter run, calling group_objects keeps the
    hierarchy navigable and makes the entire dressed location easy to select,
    move, or disable as a unit.

    object_paths: objects to group

    group_name:   name for the new container, e.g. "MarketStall_NE_Clutter",
                  "RitualCircle_Torches", "BanditCamp_Tent_A"

    parent_path:  where to create the container in the hierarchy.
                  If omitted, uses the common ancestor of the grouped objects
                  (or the scene root if they have none).

    pivot:        where to place the container's origin:
                  "center"       — centroid of all grouped objects (default)
                  "first"        — position of the first object in the list
                  "world_origin" — (0, 0, 0)

    tag:          optional tag for the container object

    layer:        optional layer for the container object

    EXAMPLES:
      # Organise the output of a dress_area call
      group_objects(
          object_paths=placed_paths,
          group_name="MarketStall_NE",
          parent_path="/Village/Dressing"
      )

      # Group ritual torches with a shared tag
      group_objects(
          torch_paths,
          group_name="RitualCircle",
          pivot="center",
          tag="RitualProp"
      )

    Returns: { container_path, grouped_count, warnings[] }
    container_path is the hierarchy path of the newly created container — use
    it in subsequent reparent_objects or set_object_metadata calls.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/group_objects", {
            "object_paths": object_paths,
            "group_name":   group_name,
            "parent_path":  parent_path,
            "pivot":        pivot,
            "tag":          tag,
            "layer":        layer,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"




async def create_trigger_volume(
    name: str,
    position: list[float],
    shape: str = "box",
    rotation: list[float] | None = None,
    size: list[float] | None = None,
    radius: float | None = None,
    height: float | None = None,
    tag: str | None = None,
    layer: str | None = None,
    script_component: str | None = None,
    script_properties: list[dict] | None = None,
    parent_path: str | None = None,
) -> str:
    """
    Creates a complete trigger GameObject in one call: a named empty object
    with an isTrigger collider, optional tag, optional layer, and an optional
    MonoBehaviour script with pre-configured properties.

    Open-world games use hundreds of trigger volumes — dialogue triggers, zone
    transitions, combat arenas, loot proximity detectors, ambient sound regions.
    Currently setting one up requires ~5 separate tool calls. This does it in one.

    name:     GameObject name, e.g. "DialogueTrigger_Elder", "SpawnZone_BanditCamp"

    position: [x, y, z] world-space centre of the trigger volume

    shape:    "box"     — BoxCollider (default)
              "sphere"  — SphereCollider
              "capsule" — CapsuleCollider

    ── SIZE PARAMETERS (pick the right ones for your shape) ─────────────────

    For box:
      size=[width, height, depth]   e.g. size=[3.0, 2.5, 3.0]

    For sphere:
      radius=1.5                    OR  size=[1.5]

    For capsule:
      radius=0.5, height=2.0       OR  size=[0.5, 2.0]

    If size/radius/height are all omitted, sensible defaults are used
    (1×1×1 box, 0.5m sphere radius, 0.5r×2h capsule).

    ── IDENTITY ─────────────────────────────────────────────────────────────

    tag:    Unity tag name. Must already exist in Project Settings → Tags.
            e.g. "DialogueTrigger", "CombatZone", "QuestTrigger"

    layer:  Unity layer name, e.g. "Triggers", "Default"

    ── SCRIPT COMPONENT ─────────────────────────────────────────────────────

    script_component:   type_name of a MonoBehaviour to add, from
                        get_component_catalog. e.g. "DialogueTrigger",
                        "QuestTrigger", "SpawnZone"

    script_properties:  list of { "property_path": "...", "value": ... }
                        to set on the added script. Same format as
                        add_component_to_object.
                        Supported types: float, int, bool, string, enum.
                        For Vector3/Color/ObjectReference use set_object_properties
                        on the returned object_path afterward.

    EXAMPLES:
      # Simple dialogue trigger box
      create_trigger_volume(
          name="DialogueTrigger_Elder",
          shape="box",
          position=[45.0, 1.0, 22.0],
          size=[3.0, 2.5, 3.0],
          tag="DialogueTrigger",
          layer="Triggers",
          script_component="DialogueTrigger",
          script_properties=[
              {"property_path": "dialogueId",   "value": "elder_intro"},
              {"property_path": "triggerOnce",  "value": True}
          ],
          parent_path="/World/Triggers"
      )

      # Sphere-shaped spawn zone, no script
      create_trigger_volume(
          name="SpawnZone_Bandits",
          shape="sphere",
          position=[100.0, 0.0, 50.0],
          radius=8.0,
          tag="SpawnZone",
          layer="Triggers"
      )

    Returns: {
      object_path,      ← use this in set_object_properties for complex script fields
      shape,
      effective_size,   ← [w,h,d] for box, [r] for sphere, [r,h] for capsule
      tag,
      script_added,     ← null if no script, component name if added
      warnings[]
    }
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/create_trigger_volume", {
            "name":              name,
            "shape":             shape,
            "position":          position,
            "rotation":          rotation,
            "size":              size,
            "radius":            radius,
            "height":            height,
            "tag":               tag,
            "layer":             layer,
            "script_component":  script_component,
            "script_properties": normalise_property_values(script_properties) if script_properties else script_properties,
            "parent_path":       parent_path,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def apply_material(
    object_paths: list[str],
    material_name: str | None = None,
    material_path: str | None = None,
    material_tag: str | None = None,
    random_from_tag: bool = False,
    material_slot: int = 0,
) -> str:
    """
    Assigns a material to the MeshRenderer of one or more scene objects.

    Exactly one of material_name, material_path, or material_tag must be
    provided. For visual variety across a set of identical props, use
    material_tag + random_from_tag=True.

    object_paths:   objects to modify (must have MeshRenderer components)

    ── MATERIAL LOOKUP (use exactly one) ────────────────────────────────────

    material_name:  exact asset name in the project, e.g. "Wood_Dark_Worn".
                    Matches by name == comparison (case-sensitive).

    material_path:  full asset database path,
                    e.g. "Assets/Materials/Props/Wood_Dark_Worn.mat"
                    Use this when multiple materials share the same name.

    material_tag:   case-insensitive name-contains search across all project
                    materials. e.g. "wood" finds Wood_Dark, Wood_Pale_Old,
                    BrokenWood_Planks, etc.
                    Combined with random_from_tag=True this assigns a different
                    material from the matching pool to each object.

    ── OPTIONS ───────────────────────────────────────────────────────────────

    random_from_tag:  when True and multiple materials match material_tag,
                      each object gets a independently-chosen material from
                      the pool rather than all getting the same one.
                      Ignored when using material_name or material_path
                      (single candidate by definition). Default false.

    material_slot:    which MeshRenderer material index to replace.
                      Default 0 (the primary material). Use 1, 2, etc. for
                      multi-material meshes (e.g. a building with separate
                      wall and roof materials).

    EXAMPLES:
      # Same material on all crates
      apply_material(
          object_paths=crate_paths,
          material_name="Wood_Dark_Worn"
      )

      # Different wood variant on each barrel (visual variety)
      apply_material(
          object_paths=barrel_paths,
          material_tag="wood",
          random_from_tag=True
      )

      # Replace only the roof material (slot 1) on a building
      apply_material(
          object_paths=["/Village/Barn"],
          material_name="Thatch_Old",
          material_slot=1
      )

    Returns: {
      applied_count,
      assignments: [{ path, material }],   ← which material went on which object
      warnings[]
    }
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/apply_material", {
            "object_paths":   object_paths,
            "material_name":  material_name,
            "material_path":  material_path,
            "material_tag":   material_tag,
            "random_from_tag": random_from_tag,
            "material_slot":  material_slot,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"




async def dress_area(
    center: list[float],
    radius: float,
    intent: str,
    density: str = "medium",
    exclude_tags: list[str] | None = None,
    include_tags: list[str] | None = None,
    snap_to_surfaces: bool = True,
    avoid_existing: bool = True,
    avoidance_radius: float = 0.5,
    parent_path: str | None = None,
    group_name: str | None = None,
) -> str:
    """
    Intent-driven area dressing — the flagship tool of this group.

    Given a world-space centre, a radius, and a natural-language description
    of what you want in the area, dress_area:
      1. Scores every prefab in the catalog against your intent
      2. Generates well-distributed candidate positions (Poisson disk)
      3. Discards positions that overlap existing scene objects
      4. Raycasts each surviving position to the nearest surface below
      5. Assigns a prefab to each position (weighted by relevance)
      6. Submits everything as a proposal to the Operation Queue for review

    Nothing is placed until you call execute_proposal with the returned
    proposal_id. The Operation Queue window shows every proposed item with
    its position and prefab name; you can approve, reject, or edit individual
    items before executing.

    ── PARAMETERS ───────────────────────────────────────────────────────────

    center:   [x, y, z] world-space centre of the area to fill

    radius:   radius of the circular area in metres

    intent:   natural-language description of what you want.
              Words are matched against PrefabMetadata tags, categories,
              and display names — the more specific the better.

              Good intents:
                "neglected medieval market stall with crates and barrels"
                "dungeon corridor with wall sconces and debris on the floor"
                "forest clearing with fallen logs, mushrooms, and rocks"
                "bandit camp cooking area with firepit props and sacks"

              The scoring is tag-overlap based. If a prefab has tags
              ["crate", "wooden", "medieval"] and your intent contains
              "medieval crates", that prefab scores 2 and is much more
              likely to be selected than one with no matching tags.

    density:  "sparse"  — ~5-15 objects, 2m min spacing
              "medium"  — ~10-25 objects, 1.2m min spacing (default)
              "dense"   — ~20-40 objects, 0.7m min spacing

    exclude_tags:  prefab tags to never use, e.g. ["futuristic", "sci-fi"]

    include_tags:  if provided, only prefabs matching at least one of these
                   tags are considered. Useful for narrowing to a specific
                   asset set without relying on intent scoring.

    snap_to_surfaces:  raycast each candidate position downward and land it
                       on the terrain/floor (default True).

    avoid_existing:    skip positions that overlap existing scene colliders
                       within avoidance_radius (default True).

    avoidance_radius:  minimum clearance from existing objects in metres
                       (default 0.5m). Reduce to 0.1 for tight spaces.

    parent_path:  if provided, each placed prefab gets this parent. Leave
                  null and use group_objects afterward to organise the result.

    group_name:   name used in the proposal summary, e.g. "MarketStall_NE".
                  Defaults to a position-derived name.

    ── WORKFLOW ─────────────────────────────────────────────────────────────

    1. dress_area(...)             → returns proposal_id + summary
    2. [Review Operation Queue]    → approve/reject individual items
    3. execute_proposal(proposal_id)
    4. snap_to_surface(placed_paths)    ← optional cleanup pass
    5. randomize_transforms(placed_paths, rotation_range=[0,180,0])
    6. set_object_metadata(placed_paths, tag="Prop", static_flags=["LightmapStatic"])
    7. group_objects(placed_paths, group_name="MarketStall_NE_Clutter")

    ── EXAMPLES ─────────────────────────────────────────────────────────────

    # Market stall clutter, 8m radius
    dress_area(
        center=[45.0, 0.0, 22.0],
        radius=8.0,
        intent="busy medieval market stall crates barrels sacks hanging lantern",
        density="medium",
        include_tags=["medieval", "prop"],
        group_name="MarketStall_NE"
    )

    # Sparse dungeon entrance dressing
    dress_area(
        center=[0.0, 0.0, 0.0],
        radius=12.0,
        intent="dungeon entrance rocks rubble bones skulls torches",
        density="sparse",
        exclude_tags=["village", "urban"]
    )

    # Dense forest floor coverage
    dress_area(
        center=[100.0, 0.0, 50.0],
        radius=15.0,
        intent="forest floor ferns mushrooms fallen logs rocks",
        density="dense",
        snap_to_surfaces=True
    )

    ── RETURNS ──────────────────────────────────────────────────────────────

    {
      "proposal_id":     "prop_a3f2b1c4",  ← pass to execute_proposal
      "summary":         "14 props proposed for ...",
      "proposed_count":  14,
      "discarded_count": 3,  ← positions too close to existing objects
      "group_name":      "MarketStall_NE",
      "warnings":        [...]
    }

    The summary breaks down the proposal by prefab type so you can see
    at a glance what was selected before opening the Operation Queue.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/dress_area", {
            "center":           center,
            "radius":           radius,
            "intent":           intent,
            "density":          density,
            "exclude_tags":     exclude_tags,
            "include_tags":     include_tags,
            "snap_to_surfaces": snap_to_surfaces,
            "avoid_existing":   avoid_existing,
            "avoidance_radius": avoidance_radius,
            "parent_path":      parent_path,
            "group_name":       group_name,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
