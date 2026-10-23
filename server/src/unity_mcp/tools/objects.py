"""
Foundation tools — scene query and object mutation primitives.

These tools are the connective tissue that makes all other agent workflows
composable. Without find_objects the agent is blind to what already exists.
Without get/set_object_properties it cannot inspect or reconfigure existing
scene content.

find_objects            — query scene objects by tag, component, name, layer, position
get_object_properties   — read serialised component property values
set_object_properties   — write serialised component property values
set_object_metadata     — set tag, layer, name, static flags in one call (bulk)
reparent_objects        — move objects in the hierarchy
duplicate_objects       — copy objects with optional positional offset
"""

import json

from unity_mcp.tools.components import normalise_property_values
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def find_objects(
    tag: str | None = None,
    component_type: str | None = None,
    name_pattern: str | None = None,
    layer: str | None = None,
    near_position: list[float] | None = None,
    near_radius: float = 20.0,
    include_inactive: bool = False,
    max_results: int = 100,
) -> str:
    """
    Searches the active scene for GameObjects matching one or more filters.

    At least one filter must be provided. Filters are AND-combined.
    Call get_scene_hierarchy instead if you need the full object tree.

    ── FILTERS ──────────────────────────────────────────────────────────────

    tag             Exact Unity tag, e.g. "Interactable", "Enemy", "Prop"

    component_type  Component type name from get_component_catalog,
                    e.g. "Light", "Rigidbody", "NavMeshAgent", "AudioSource",
                    or your own script names like "DialogueTrigger"

    name_pattern    Glob pattern for the object name.
                    * = any characters, ? = one character
                    e.g. "Barrel*", "*Torch*", "Fence_??"
                    Case-insensitive.

    layer           Unity layer name, e.g. "Environment", "Pickable", "Default"

    near_position   [x, y, z] world-space centre of a sphere query.
                    Returns objects whose transform position is within
                    near_radius metres (default 20m) of this point.
                    Also catches collider-bearing objects via Physics.OverlapSphere.

    ── RESPONSE ─────────────────────────────────────────────────────────────

    Each object in the results array contains:
      name, path, position, rotation, scale, tag, layer, is_active, components[]

    path is what other tools expect as object_path, e.g.:
      "/Village/Props/Barrel_04"
      "/World/Lighting/Lamp_03"

    components[] lists the C# type names of every component on the object.

    ── EXAMPLES ─────────────────────────────────────────────────────────────

    # All lights within 30m of a point
    find_objects(component_type="Light", near_position=[45, 0, 22], near_radius=30)

    # All objects tagged "Prop" near a market stall
    find_objects(tag="Prop", near_position=[45, 0, 22], near_radius=10)

    # All objects whose name starts with "Barrel"
    find_objects(name_pattern="Barrel*")

    # All NavMesh obstacles in the scene
    find_objects(component_type="NavMeshObstacle")

    # All objects on the Environment layer near a camp
    find_objects(layer="Environment", near_position=[100, 0, 50], near_radius=25)

    include_inactive: include inactive GameObjects (default false)
    max_results:      cap on returned objects (default 100)
    """
    try:
        data = await UnityClient.instance().post("/tools/scene/find_objects", {
            "tag":              tag,
            "component_type":   component_type,
            "name_pattern":     name_pattern,
            "layer":            layer,
            "near_position":    near_position,
            "near_radius":      near_radius,
            "include_inactive": include_inactive,
            "max_results":      max_results,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_object_properties(
    object_path: str,
    component_type: str,
    property_paths: list[str] | None = None,
) -> str:
    """
    Reads the serialised property values of a component on a scene object.

    WORKFLOW: call get_component_catalog to find property_path strings,
    then call this to read current values, then set_object_properties to change them.

    object_path:    hierarchy path from get_scene_hierarchy or find_objects,
                    e.g. "/Village/Lamp_03"

    component_type: type_name from get_component_catalog, e.g. "Light", "Rigidbody"

    property_paths: list of specific property paths to read, e.g.
                    ["m_Intensity", "m_Color", "m_Range"]
                    If omitted, returns all top-level serialised properties.
                    Use get_component_catalog to find valid property paths.

    RESPONSE per property:
      property_path  — exact path to use in set_object_properties
      value          — current value (float, int, bool, string, [r,g,b,a], etc.)
      type           — Float | Integer | Boolean | String | Color | Vector3 |
                       Enum:LightType[Spot|Directional|Point|Area] | ObjectReference | ...

    EXAMPLE — read a light's current settings:
      get_object_properties("/Village/Lamp_03", "Light",
                            ["m_Intensity", "m_Color", "m_Range", "m_Type"])
    """
    try:
        data = await UnityClient.instance().post("/tools/objects/get_properties", {
            "object_path":    object_path,
            "component_type": component_type,
            "property_paths": property_paths,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_object_properties(
    object_path: str,
    component_type: str,
    properties: list[dict],
    create_if_missing: bool = False,
) -> str:
    """
    Modifies serialised property values on a component that already exists
    on a scene object. All changes are registered with Unity's Undo system.

    object_path:    hierarchy path of the target object
    component_type: type_name from get_component_catalog
    create_if_missing: if true, adds the component if it isn't present
                       (same as add_component_to_object). Default false.

    PROPERTIES FORMAT:
      List of { "property_path": "...", "value": ... } — identical format to
      add_component_to_object. Use property_path values from get_component_catalog
      or from get_object_properties.

      float/int   → number:           1.5
      bool        → boolean:          true
      string      → string:           "name"
      enum        → string:           "Point"  (use enum_names from catalog)
      Vector3     → array:            [0, -9.81, 0]
      Color       → array [r,g,b,a]:  [1.0, 0.85, 0.6, 1.0]
      ObjectRef   → object:           { "asset_guid": "abc123..." }

    EXAMPLE — normalise a light that's too bright:
      set_object_properties(
          "/Village/Lamp_03",
          "Light",
          [
            { "property_path": "m_Intensity", "value": 1.0 },
            { "property_path": "m_Color",     "value": [1.0, 0.85, 0.6, 1.0] }
          ]
      )

    Returns: { object_path, component_type, applied_properties[], failed_properties[], warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/objects/set_properties", {
            "object_path":      object_path,
            "component_type":   component_type,
            "properties":       normalise_property_values(properties),
            "create_if_missing": create_if_missing,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_object_metadata(
    object_paths: list[str],
    name: str | None = None,
    tag: str | None = None,
    layer: str | None = None,
    static_flags: list[str] | None = None,
    is_active: bool | None = None,
) -> str:
    """
    Sets identity properties on one or more GameObjects in a single Undo-able call.
    Accepts a list of paths so an entire dressing pass can be tagged/layered/flagged
    at once without iterating each object individually.

    Only the fields you provide are updated. Omitted fields are unchanged.

    object_paths:  one or more hierarchy paths (bulk)

    name:          rename the object(s). For a single object: exact name.
                   For multiple objects: "Barrel" → Barrel_01, Barrel_02, ...

    tag:           Unity tag name. Must already exist in Project Settings → Tags.
                   e.g. "Prop", "Interactable", "Enemy", "Environment"

    layer:         Unity layer name, e.g. "Default", "Environment", "Pickable"

    static_flags:  list of one or more:
                     "LightmapStatic"        (also accepts "ContributeGI" for Unity 2022+)
                     "OccluderStatic"
                     "BatchingStatic"
                     "NavigationStatic"
                     "OccludeeStatic"
                     "ReflectionProbeStatic"
                     "OffMeshLinkGeneration"
                   Pass ["All"] to set every flag. Pass ["None"] to clear all.

    is_active:     set the GameObject active or inactive

    EXAMPLE — mark 30 placed props for lightmap baking:
      set_object_metadata(
          object_paths=[...30 paths from dress_area...],
          tag="Prop",
          layer="Environment",
          static_flags=["LightmapStatic", "OccluderStatic"]
      )

    Returns: { updated_count, applied[], warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/objects/set_metadata", {
            "object_paths": object_paths,
            "name":         name,
            "tag":          tag,
            "layer":        layer,
            "static_flags": static_flags,
            "is_active":    is_active,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def reparent_objects(
    object_paths: list[str],
    new_parent_path: str | None = None,
    keep_world_position: bool = True,
) -> str:
    """
    Moves one or more objects to a new parent in the hierarchy.
    Registered with Unity's Undo system.

    object_paths:        objects to move
    new_parent_path:     hierarchy path of the new parent.
                         Pass null or omit to move objects to the scene root.
    keep_world_position: if true, the object stays at its current world-space
                         position after reparenting (default true).
                         If false, the local transform is preserved instead.

    EXAMPLE — organise placed objects into a container:
      reparent_objects(
          object_paths=["/Barrel_01", "/Barrel_02", "/Crate_03"],
          new_parent_path="/Village/MarketStall_NE"
      )

    Returns: { reparented_count, warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/objects/reparent", {
            "object_paths":        object_paths,
            "new_parent_path":     new_parent_path,
            "keep_world_position": keep_world_position,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def duplicate_objects(
    object_paths: list[str],
    count: int = 1,
    position_offset: list[float] | None = None,
    rotation_offset: list[float] | None = None,
    parent_path: str | None = None,
    name_suffix: str | None = None,
) -> str:
    """
    Creates one or more copies of existing scene objects, preserving all
    components and property values. All copies are registered with Undo.

    object_paths:    objects to duplicate (each gets `count` copies)

    count:           number of copies per source object (default 1)

    position_offset: world-space offset applied CUMULATIVELY per copy.
                     e.g. [4, 0, 0] with count=5 places copies at +4m, +8m,
                     +12m, +16m, +20m from the source. Default [0, 0, 0].

    rotation_offset: Euler angle offset applied cumulatively per copy.
                     e.g. [0, 45, 0] with count=4 gives 45°, 90°, 135°, 180°
                     Y-rotation offset. Default [0, 0, 0].

    parent_path:     parent for all duplicates. Uses each source's own parent
                     if omitted.

    name_suffix:     string appended to the source name for each copy, e.g.
                     "_Mirror" → SourceName_Mirror.
                     If omitted: _Copy, _Copy_2, _Copy_3, ...

    EXAMPLES:
      # Line up 5 lamp posts at 4m intervals along Z
      duplicate_objects(
          object_paths=["/Village/LampPost_A"],
          count=5,
          position_offset=[0, 0, 4]
      )

      # Make 3 copies of a stall in a square
      duplicate_objects(
          object_paths=["/Village/MarketStall_Template"],
          count=3,
          position_offset=[6, 0, 0],
          parent_path="/Village/Stalls"
      )

    Returns: { created_count, paths[], warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/objects/duplicate", {
            "object_paths":    object_paths,
            "count":           count,
            "position_offset": position_offset or [0.0, 0.0, 0.0],
            "rotation_offset": rotation_offset or [0.0, 0.0, 0.0],
            "parent_path":     parent_path,
            "name_suffix":     name_suffix,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
