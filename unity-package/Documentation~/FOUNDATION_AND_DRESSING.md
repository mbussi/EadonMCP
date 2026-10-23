# Foundation and Dressing Workflow — Architecture Document

## Table of Contents

1. [Context](#1-context)
2. [Group 1 — Foundation](#2-group-1--foundation)
   - 2.1 [find_objects](#21-find_objects)
   - 2.2 [get_object_properties](#22-get_object_properties)
   - 2.3 [set_object_properties](#23-set_object_properties)
   - 2.4 [set_object_metadata](#24-set_object_metadata)
   - 2.5 [reparent_objects](#25-reparent_objects)
   - 2.6 [duplicate_objects](#26-duplicate_objects)
3. [Group 2 — The Dressing Workflow](#3-group-2--the-dressing-workflow)
   - 3.1 [snap_to_surface](#31-snap_to_surface)
   - 3.2 [align_and_distribute](#32-align_and_distribute)
   - 3.3 [randomize_transforms](#33-randomize_transforms)
   - 3.4 [group_objects](#34-group_objects)
   - 3.5 [create_trigger_volume](#35-create_trigger_volume)
   - 3.6 [apply_material](#36-apply_material)
   - 3.7 [dress_area](#37-dress_area)
4. [MCP Integration](#4-mcp-integration)
   - 4.1 [Route Map](#41-route-map)
   - 4.2 [Request and Response Shapes](#42-request-and-response-shapes)
   - 4.3 [Python Tool Catalogue](#43-python-tool-catalogue)
5. [Compound Workflow Patterns](#5-compound-workflow-patterns)
6. [Implementation Notes](#6-implementation-notes)
7. [Development Roadmap](#7-development-roadmap)

---

## 1. Context

This document specifies two groups of tools that were identified as the highest-leverage
additions to the MCP Bridge for a solo developer working on open-world 3D games.

**Group 1 — Foundation** provides the query and mutation primitives that almost every
other agent workflow depends on. Without `find_objects`, the agent is blind to anything
it didn't place itself. Without `get_object_properties` and `set_object_properties`,
it cannot inspect or reconfigure existing scene content. These tools are not dramatic
individually; they are the connective tissue that makes everything else composable.

**Group 2 — The dressing workflow** directly addresses the #1 stated pain point:
filling a specific location (a village square, a dungeon entrance, a camp) with props,
clutter, lights, and functional volumes takes an "insane amount of time." The tools in
this group do not try to be general placement engines — `scatter_prefabs` and
`place_along_path` already exist for that. Instead they cover the mid-scale,
context-aware work between large procedural generation and individual object placement:
snapping loose objects to surfaces, distributing identical objects evenly, applying
material variants for visual variety, creating trigger volumes in one call, and the
flagship `dress_area` tool that turns a radius and a natural-language intent into a
reviewable placement proposal.

---

## 2. Group 1 — Foundation

### 2.1 find_objects

**What it does.** Queries the active scene for GameObjects matching one or more filters.
Returns hierarchy paths, transforms, tags, layers, and component lists for every match.
Every other tool that reads or modifies existing content depends on this.

**Why it matters.** The agent currently cannot answer "what walls are near this position?"
or "which objects in this courtyard have a Light component?" Without spatial and
component queries it must already know the hierarchy path of anything it wants to touch.

**Filters (all optional, AND-combined when multiple are supplied):**

| Parameter | Type | Description |
|---|---|---|
| `tag` | string | Exact tag match, e.g. `"Interactable"`, `"Enemy"` |
| `component_type` | string | Component type name, e.g. `"Light"`, `"NavMeshObstacle"`, `"AudioSource"`, or user script names |
| `name_pattern` | string | Glob pattern, e.g. `"Barrel*"`, `"*Torch*"`, `"Fence_??"` |
| `layer` | string | Layer name, e.g. `"Environment"`, `"Pickable"` |
| `near_position` | float[3] | World-space centre of a sphere query |
| `near_radius` | float | Radius in metres for the sphere query (default 20.0) |
| `include_inactive` | bool | Include inactive GameObjects (default false) |
| `max_results` | int | Cap on returned objects (default 100) |

**Response per object:**

```json
{
  "name":       "Barrel_04",
  "path":       "/Village/Props/Barrel_04",
  "position":   [12.5, 0.0, 8.3],
  "rotation":   [0.0, 45.0, 0.0],
  "scale":      [1.0, 1.0, 1.0],
  "tag":        "Prop",
  "layer":      "Environment",
  "is_active":  true,
  "components": ["MeshFilter", "MeshRenderer", "BoxCollider"]
}
```

**Design note.** `find_objects` does not replace `get_scene_hierarchy`. The hierarchy
tool gives structural context (parent/child relationships, full tree). `find_objects`
gives filtered search results for acting on specific objects.

---

### 2.2 get_object_properties

**What it does.** Reads the serialised property values of a component that already
exists on a scene object. Companion to `add_component_to_object`, which sets properties
at creation time, and `set_object_properties`, which changes them afterward.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_path` | string | Hierarchy path, e.g. `"/Village/Lamp_03"` |
| `component_type` | string | typeName from `get_component_catalog`, e.g. `"Light"` |
| `property_paths` | string[]? | Specific properties to read. If omitted, returns all serialised properties |

**Response:**

```json
{
  "object_path":       "/Village/Lamp_03",
  "component_type":    "Light",
  "properties": [
    { "property_path": "m_Type",      "value": "Point",             "type": "Enum" },
    { "property_path": "m_Intensity", "value": 1.2,                 "type": "Float" },
    { "property_path": "m_Color",     "value": [1.0, 0.85, 0.6, 1], "type": "Color" },
    { "property_path": "m_Range",     "value": 8.0,                 "type": "Float" }
  ]
}
```

**Use case.** Read all lights in an area, identify which ones are too bright, pass those
paths to `set_object_properties` to normalise their intensity. Neither operation would
be possible without the other.

---

### 2.3 set_object_properties

**What it does.** Modifies serialised property values on a component that already exists
on a scene object. Registered with Unity's Undo system. Property format is identical to
`add_component_to_object`; the only difference is the component must already be present.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_path` | string | Hierarchy path of the target object |
| `component_type` | string | typeName from `get_component_catalog` |
| `properties` | list[{propertyPath, value}] | Same format as `add_component_to_object` |
| `create_if_missing` | bool | If true, add the component if it isn't present (default false) |

**Response:** same shape as `add_component_to_object` — lists `applied_properties`,
`failed_properties`, and `warnings`.

**Combined read-then-write pattern:**

```
get_object_properties → inspect current values
    ↓
decide what to change
    ↓
set_object_properties → apply changes
```

This pattern enables corrections: "find all lights with intensity > 2.0 in the courtyard
and set them to 1.0" — something that was completely impossible before this pair of tools.

---

### 2.4 set_object_metadata

**What it does.** Sets identity properties that live on the `GameObject` itself rather
than on any component: tag, layer, name, static flags, and active state. These are
the most frequently set properties on placed objects and the ones currently requiring
the most manual work.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | One or more hierarchy paths (bulk operation) |
| `name` | string? | Rename the object. Uses the last path segment if bulk. For bulk, appends `_01`, `_02`, etc. |
| `tag` | string? | Unity tag name, e.g. `"Interactable"`, `"Environment"` |
| `layer` | string? | Layer name, e.g. `"Default"`, `"Environment"`, `"Pickable"` |
| `static_flags` | string[]? | One or more: `"LightmapStatic"`, `"NavigationStatic"`, `"OccluderStatic"`, `"BatchingStatic"`, `"ReflectionProbeStatic"`, `"OccludeeStatic"`, `"OffMeshLinkGeneration"`. Pass `["All"]` to set everything. Pass `["None"]` to clear all. |
| `is_active` | bool? | Set active/inactive |

**Response:**

```json
{
  "updated_count": 8,
  "applied": ["tag", "layer", "static_flags"],
  "warnings": []
}
```

**Why bulk matters.** A typical dressing pass places 20–30 props. Each one needs
`tag: "Prop"`, `layer: "Environment"`, `static: LightmapStatic + OccluderStatic`.
Doing this one object at a time is where a large fraction of the manual time goes.
Bulk `set_object_metadata` collapses that to a single call after the dressing is done.

---

### 2.5 reparent_objects

**What it does.** Moves one or more scene objects to a new parent in the hierarchy,
with an option to preserve or ignore world position. Registered with Undo.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to move |
| `new_parent_path` | string? | New parent hierarchy path. Pass `null` to move to scene root |
| `keep_world_position` | bool | If true, transform stays at same world position (default true) |

**Use case.** After `dress_area` places objects, reparent them all into an organisational
container. After creating a settlement, move all buildings into `/Settlement_Riverford/`
so the hierarchy stays navigable.

---

### 2.6 duplicate_objects

**What it does.** Creates one or more copies of existing scene objects, optionally
applying positional or rotational offsets. Registered with Undo.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to duplicate |
| `count` | int | Number of copies per source object (default 1) |
| `position_offset` | float[3]? | World-space offset applied to each successive copy |
| `rotation_offset` | float[3]? | Euler angle offset applied to each successive copy |
| `parent_path` | string? | Parent for duplicates; uses source object's parent if omitted |
| `name_suffix` | string? | Appended to the source name. If omitted, uses `_Copy`, `_Copy_2`, etc. |

**Use case.** Duplicate a lamp post 5 times with a 4m Z offset to line a road.
Duplicate a market stall 3 times to populate a square without re-describing each one.

---

## 3. Group 2 — The Dressing Workflow

### 3.1 snap_to_surface

**What it does.** For each specified object, performs a Physics raycast from the
object's position along a direction, then moves the object to the hit surface.
Optionally rotates the object to match the surface normal. Handles the single most
common dressing error: objects floating above a surface or sinking into a slope.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to snap (bulk) |
| `cast_direction` | float[3] | Raycast direction (default `[0,-1,0]` — downward) |
| `max_distance` | float | Max raycast distance in metres (default 100.0) |
| `surface_offset` | float | Gap to leave above the hit surface in metres (default 0.0) |
| `align_to_normal` | bool | Rotate object's up-axis to match surface normal (default false) |
| `layer_mask` | int? | Bitmask of layers to hit. If null, hits all layers |

**Response:**

```json
{
  "snapped_count": 12,
  "skipped": [
    { "path": "/Props/Barrel_07", "reason": "No surface found within 100m" }
  ]
}
```

**Design note.** `align_to_normal` is most useful for rocks, debris, and organic props.
For buildings and furniture, keep it false — tilted crates look wrong even on slopes.
The `surface_offset` is useful for objects with pivot points at their base vs. centre.

---

### 3.2 align_and_distribute

**What it does.** Repositions a list of objects relative to each other or to a
specified path. Covers the "fence posts evenly between A and B" use case and the
"stack these crates" use case.

**Modes:**

| Mode | Description |
|---|---|
| `distribute_between` | Space N objects evenly between two world-space points. Objects face along the line by default. |
| `align_to_axis` | Align all objects to the same X, Y, or Z value (snap them to a plane). Useful for aligning tops of crates at the same height. |
| `stack` | Stack objects on top of each other along a specified axis. Uses each object's bounds to compute the stacking offset. |
| `circle` | Distribute objects evenly around a circle. Optional `facing_outward` flag points each object away from the centre. |

**Parameters (all modes):**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to reposition |
| `mode` | string | See table above |

**Parameters (distribute_between):**

| Parameter | Type | Description |
|---|---|---|
| `start_point` | float[3] | World-space start of the distribution line |
| `end_point` | float[3] | World-space end of the distribution line |
| `face_direction` | bool | Rotate objects to face along the line (default true) |

**Parameters (align_to_axis):**

| Parameter | Type | Description |
|---|---|---|
| `axis` | string | `"x"` \| `"y"` \| `"z"` |
| `value` | float | The axis value to align to (e.g. `y: 0.0` to level all objects to the ground plane) |
| `use_pivot` | string | `"min"` \| `"center"` \| `"max"` of each object's bounds |

**Parameters (stack):**

| Parameter | Type | Description |
|---|---|---|
| `stack_axis` | string | `"x"` \| `"y"` \| `"z"` (default `"y"`) |
| `base_position` | float[3]? | Bottom of the stack. Uses first object's position if omitted |

**Parameters (circle):**

| Parameter | Type | Description |
|---|---|---|
| `center` | float[3] | World-space centre |
| `radius` | float | Radius in metres |
| `facing_outward` | bool | Face away from centre (default true) |
| `start_angle_degrees` | float | Rotation offset around the circle (default 0) |

---

### 3.3 randomize_transforms

**What it does.** Applies bounded random offsets to position, rotation, and scale
of a set of objects to break up the uniformity of programmatically-placed props.
Every dressing pass benefits from this: a row of identical barrels looks placed;
the same row with ±15° rotation variation and ±0.1 scale variation looks lived-in.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to vary |
| `seed` | int? | Random seed for reproducibility. If omitted, uses a random seed and returns it in the response for reproduction |
| `position_range` | float[3]? | Maximum position offset in each axis `[x, y, z]` (applied symmetrically, e.g. `[0.3, 0, 0.3]` shuffles XZ by up to 30cm) |
| `rotation_range` | float[3]? | Maximum rotation offset in each axis in degrees. For props, `[0, 180, 0]` (full random Y) is common |
| `scale_range` | float[2]? | `[min, max]` uniform scale multiplier, e.g. `[0.85, 1.15]` for ±15% size variation |
| `snap_after` | bool | Run `snap_to_surface` after randomizing (default false) |

**Response includes the `seed` used** so the same variation can be reproduced or
described to the user.

---

### 3.4 group_objects

**What it does.** Creates a new empty GameObject, optionally at a computed centre
position, and reparents a list of objects under it. The organisational equivalent
of "select all these crates and press Ctrl+G." Keeps the hierarchy navigable after
a `dress_area` call places 30 objects.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to group |
| `group_name` | string | Name for the new container, e.g. `"MarketStall_A"` |
| `parent_path` | string? | Where to create the container. Uses common ancestor if omitted |
| `pivot` | string | `"center"` (centroid of objects) \| `"first"` (first object's position) \| `"world_origin"` |
| `tag` | string? | Tag for the container object |
| `layer` | string? | Layer for the container object |

---

### 3.5 create_trigger_volume

**What it does.** Creates a complete trigger GameObject in one call: a named empty
object with an `isTrigger` collider, a tag, an optional layer, and an optional script
component. Open-world games use hundreds of these — dialogue triggers, zone transitions,
spawn volumes, combat arena boundaries, ambient sound zones, loot proximity detectors.
Currently this requires five separate tool calls; this reduces it to one.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `name` | string | GameObject name, e.g. `"DialogueTrigger_Elder"` |
| `shape` | string | `"box"` \| `"sphere"` \| `"capsule"` |
| `position` | float[3] | World-space centre position |
| `rotation` | float[3]? | Euler angles (default `[0,0,0]`) |
| `size` | float[3]? | Box: `[width, height, depth]`. Capsule: `[radius, height, radius]` |
| `radius` | float? | Sphere/capsule radius (alternative to `size`) |
| `height` | float? | Capsule height |
| `tag` | string? | Tag for the trigger object (e.g. `"DialogueTrigger"`, `"CombatZone"`) |
| `layer` | string? | Layer name |
| `script_component` | string? | typeName of a MonoBehaviour to add, e.g. `"DialogueTrigger"` |
| `script_properties` | list[{propertyPath, value}]? | Properties to set on the added script |
| `parent_path` | string? | Parent in the hierarchy |

**Response:**

```json
{
  "object_path":  "/Village/Triggers/DialogueTrigger_Elder",
  "shape":        "box",
  "size":         [3.0, 2.5, 3.0],
  "tag":          "DialogueTrigger",
  "script_added": "DialogueTrigger",
  "warnings":     []
}
```

---

### 3.6 apply_material

**What it does.** Assigns materials to `MeshRenderer` components on one or more
objects. Supports exact-name lookup, tag-based search, and — the key feature —
random-from-tag assignment so that a set of identical objects each gets a different
material variant without individually specifying each one.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[] | Objects to modify |
| `material_name` | string? | Exact AssetDatabase name, e.g. `"Wood_Dark_Worn"` |
| `material_path` | string? | Full asset path, e.g. `"Assets/Materials/Props/Wood_Dark_Worn.mat"` |
| `material_tag` | string? | PrefabMetadata-style tag to find candidates, e.g. `"wood"` \| `"stone"` |
| `random_from_tag` | bool | Apply a different random material (from the tag results) to each object (default false). When false, all objects get the same material |
| `material_slot` | int | MeshRenderer material index to replace (default 0) |

**Response:**

```json
{
  "applied_count":   8,
  "assignments": [
    { "path": "/Props/Barrel_01", "material": "Wood_Dark_Worn.mat" },
    { "path": "/Props/Barrel_02", "material": "Wood_Pale_Old.mat" },
    { "path": "/Props/Barrel_03", "material": "Wood_Dark_Worn.mat" }
  ],
  "warnings": []
}
```

**Implementation note.** `material_tag` is matched against `PrefabMetadata` assets
whose category is `"material"` or whose tags include the specified value. This requires
`PrefabMetadata` to be authored for materials, not just prefabs — the metadata system
already supports this, it simply hasn't been used for materials before.

---

### 3.7 dress_area

**What it does.** Given a world-space centre, a radius, and a natural-language intent
description, queries the prefab catalog for contextually appropriate props, performs
spatial checks to avoid placing on top of existing objects, snaps proposed placements
to surfaces, and submits the entire result as a proposal for human review before
anything is instantiated.

This is the high-level tool. Every other Group 2 tool is usable standalone; `dress_area`
combines them into a single agent-driven workflow. "Fill this village square with market
stall clutter" becomes a one-sentence prompt.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `center` | float[3] | World-space centre of the area to dress |
| `radius` | float | Radius in metres |
| `intent` | string | Natural-language description of desired dressing, e.g. `"neglected market stall with crates, barrels, and a broken lantern"` |
| `density` | string | `"sparse"` \| `"medium"` \| `"dense"` (default `"medium"`) |
| `exclude_tags` | string[]? | Prefab tags to exclude from consideration |
| `include_tags` | string[]? | If provided, only use prefabs matching at least one of these tags |
| `snap_to_surfaces` | bool | Run snap_to_surface on each placement (default true) |
| `avoid_existing` | bool | Skip positions that overlap existing scene objects (default true) |
| `avoidance_radius` | float | Minimum clearance from existing objects in metres (default 0.5) |
| `parent_path` | string? | If specified, placed objects are grouped under this container; if omitted, `group_objects` is called automatically to create a container |
| `group_name` | string? | Name for the auto-created container. Defaults to a name derived from the intent and position |

**Execution model.** `dress_area` never instantiates anything silently. Its output is
a proposal submitted to the existing `ProposalStore` (same system used by
`propose_changes`). The Unity Operation Queue Window shows each proposed placement with
its human-readable description. The agent reports the proposal ID and summary in the
conversation. The user approves, rejects individual items, or modifies counts before
`execute_proposal` runs.

**Internal process:**

1. Parse `intent` to extract object categories and style cues (e.g. "broken", "stacked", "lit")
2. Query `MetadataRegistry` by extracted tags + active `SceneContext.activeTags` to find candidate prefabs
3. Generate candidate positions within the radius using a Poisson disk distribution to maintain minimum spacing
4. For each candidate position, check against existing scene objects within `avoidance_radius`
5. For surviving positions, run a downward raycast to find the surface height
6. Assign a prefab to each position (weighted by relevance score from step 2)
7. Submit as a single proposal with one operation per placement
8. Return the proposal ID and a summary count

**Interaction with PCG framework.** If the PCG framework is installed and the area
overlaps a zone with `DressingTags`, `dress_area` can optionally generate a PCG graph
for the placement rather than using the Poisson disk approach — this gives more
structured, pattern-aware results for architectural zones. The choice is automatic based
on what's available; `dress_area` degrades gracefully without the PCG framework.

**Example response:**

```json
{
  "proposal_id":  "prop_dress_a3f2",
  "summary":      "14 props proposed for market stall area at (45, 0, 22): 6 crates, 4 barrels, 2 sacks, 1 broken lantern, 1 market awning post",
  "group_path":   "/Village/Dressing/MarketStall_NE",
  "warnings":     ["3 positions discarded — too close to existing objects"]
}
```

---

## 4. MCP Integration

### 4.1 Route Map

All routes follow bridge conventions. Group 1 tools use a `/tools/objects/` prefix;
Group 2 dressing tools extend `/tools/placement/`.

**Group 1 — Foundation**

| Route | Handler | Description |
|---|---|---|
| `POST /tools/scene/find_objects` | `SceneQueryHandler` | Query objects by tag, component, name, layer, or spatial bounds |
| `POST /tools/objects/get_properties` | `ObjectHandler` | Read component property values |
| `POST /tools/objects/set_properties` | `ObjectHandler` | Write component property values |
| `POST /tools/objects/set_metadata` | `ObjectHandler` | Set tag, layer, name, static flags |
| `POST /tools/objects/reparent` | `ObjectHandler` | Move objects in the hierarchy |
| `POST /tools/objects/duplicate` | `ObjectHandler` | Copy objects with optional offset |

**Group 2 — Dressing**

| Route | Handler | Description |
|---|---|---|
| `POST /tools/placement/snap_to_surface` | `DressingHandler` | Raycast-snap objects to surfaces |
| `POST /tools/placement/align_and_distribute` | `DressingHandler` | Align, stack, or distribute objects |
| `POST /tools/placement/randomize_transforms` | `DressingHandler` | Apply bounded random variations |
| `POST /tools/placement/group_objects` | `DressingHandler` | Create container and reparent |
| `POST /tools/placement/create_trigger_volume` | `DressingHandler` | Create trigger collider in one call |
| `POST /tools/placement/apply_material` | `DressingHandler` | Assign materials with tag search |
| `POST /tools/placement/dress_area` | `DressingHandler` | Intent-driven area dressing proposal |

### 4.2 Request and Response Shapes

**`find_objects` — request**

```json
{
  "tag":            "Prop",
  "component_type": "Light",
  "name_pattern":   "Barrel*",
  "layer":          "Environment",
  "near_position":  [45.0, 0.0, 22.0],
  "near_radius":    15.0,
  "include_inactive": false,
  "max_results":    50
}
```

At least one filter must be provided. Filters are AND-combined. The `near_position`
filter uses `Physics.OverlapSphere` to find objects within the sphere, then applies
remaining filters to those candidates.

---

**`set_object_metadata` — request**

```json
{
  "object_paths":  ["/Village/Props/Barrel_01", "/Village/Props/Barrel_02"],
  "tag":           "Prop",
  "layer":         "Environment",
  "static_flags":  ["LightmapStatic", "OccluderStatic"]
}
```

---

**`snap_to_surface` — request**

```json
{
  "object_paths":  ["/Village/Props/Barrel_01", "/Village/Props/Barrel_02"],
  "cast_direction": [0, -1, 0],
  "max_distance":   50.0,
  "surface_offset": 0.0,
  "align_to_normal": false
}
```

---

**`create_trigger_volume` — request**

```json
{
  "name":     "DialogueTrigger_Elder",
  "shape":    "box",
  "position": [45.0, 1.0, 22.0],
  "size":     [3.0, 2.5, 3.0],
  "tag":      "DialogueTrigger",
  "layer":    "Triggers",
  "script_component": "DialogueTrigger",
  "script_properties": [
    { "propertyPath": "dialogueId", "value": "elder_intro" },
    { "propertyPath": "triggerOnce", "value": true }
  ],
  "parent_path": "/Village/Triggers"
}
```

---

**`dress_area` — request**

```json
{
  "center":          [45.0, 0.0, 22.0],
  "radius":          8.0,
  "intent":          "busy medieval market stall with crates, sacks, and a hanging lantern",
  "density":         "medium",
  "include_tags":    ["medieval", "prop"],
  "snap_to_surfaces": true,
  "avoid_existing":   true,
  "avoidance_radius": 0.6,
  "group_name":      "MarketStall_NE"
}
```

### 4.3 Python Tool Catalogue

**Group 1 — Foundation**

| Python function | Route | Summary |
|---|---|---|
| `find_objects(tag, component_type, name_pattern, layer, near_position, near_radius, include_inactive, max_results)` | `POST /tools/scene/find_objects` | Query scene objects by filter |
| `get_object_properties(object_path, component_type, property_paths)` | `POST /tools/objects/get_properties` | Read existing component values |
| `set_object_properties(object_path, component_type, properties, create_if_missing)` | `POST /tools/objects/set_properties` | Write existing component values |
| `set_object_metadata(object_paths, name, tag, layer, static_flags, is_active)` | `POST /tools/objects/set_metadata` | Set tag, layer, static flags, name |
| `reparent_objects(object_paths, new_parent_path, keep_world_position)` | `POST /tools/objects/reparent` | Move objects in hierarchy |
| `duplicate_objects(object_paths, count, position_offset, rotation_offset, parent_path, name_suffix)` | `POST /tools/objects/duplicate` | Copy objects with offsets |

**Group 2 — Dressing**

| Python function | Route | Summary |
|---|---|---|
| `snap_to_surface(object_paths, cast_direction, max_distance, surface_offset, align_to_normal, layer_mask)` | `POST /tools/placement/snap_to_surface` | Snap to ground/surface |
| `align_and_distribute(object_paths, mode, ...)` | `POST /tools/placement/align_and_distribute` | Align, stack, line, or circle |
| `randomize_transforms(object_paths, seed, position_range, rotation_range, scale_range, snap_after)` | `POST /tools/placement/randomize_transforms` | Add natural variation |
| `group_objects(object_paths, group_name, parent_path, pivot, tag, layer)` | `POST /tools/placement/group_objects` | Create container and reparent |
| `create_trigger_volume(name, shape, position, rotation, size, radius, height, tag, layer, script_component, script_properties, parent_path)` | `POST /tools/placement/create_trigger_volume` | Full trigger in one call |
| `apply_material(object_paths, material_name, material_path, material_tag, random_from_tag, material_slot)` | `POST /tools/placement/apply_material` | Assign material variants |
| `dress_area(center, radius, intent, density, exclude_tags, include_tags, snap_to_surfaces, avoid_existing, avoidance_radius, parent_path, group_name)` | `POST /tools/placement/dress_area` | Intent-driven dressing proposal |

---

## 5. Compound Workflow Patterns

These patterns show how the tools combine in real workflows. They are the design tests
for the tool set — every tool was shaped by whether it participated usefully here.

### Pattern A: Dressing a market stall from a design document

A design document says: _"Market stall on the east side of the square. Wooden crates
stacked 2-3 high, barrels of varying sizes, a sack or two, one hanging lantern. Worn
and slightly chaotic look."_

```
1. find_objects(near_position=[45,0,22], near_radius=12, component_type="MeshRenderer")
   → understand what's already there; avoid placing on top of it

2. dress_area(center=[45,0,22], radius=8,
              intent="worn wooden market stall crates barrels sacks hanging lantern chaotic",
              include_tags=["medieval", "prop", "wood"])
   → proposal submitted to Operation Queue with 14 placements

3. [User reviews in Operation Queue, approves 12, rejects 2 that clip through the stall post]
   → execute_proposal("prop_dress_a3f2")

4. snap_to_surface(object_paths=[...all 12 placed objects...])
   → fix any floating

5. randomize_transforms(object_paths=[...crates and barrels...],
                        rotation_range=[0,180,0], scale_range=[0.9,1.1])
   → break uniformity

6. set_object_metadata(object_paths=[...all 12...], tag="Prop",
                       layer="Environment", static_flags=["LightmapStatic"])
   → mark for baking

7. group_objects(object_paths=[...all 12...], group_name="MarketStall_NE_Clutter")
   → organise hierarchy
```

### Pattern B: Fixing all overly-bright lights in an area

```
1. find_objects(component_type="Light", near_position=[0,0,0], near_radius=30)
   → returns 8 light paths

2. [For each returned light path:]
   get_object_properties(object_path, component_type="Light", property_paths=["m_Intensity"])
   → read current intensity

3. [Filter to those where intensity > 1.5]

4. set_object_properties(object_path, component_type="Light",
                          properties=[{propertyPath:"m_Intensity", value:1.0}])
   → normalise each
```

### Pattern C: Setting up a quest trigger zone

A design document says: _"When the player first enters the old mill area, Elder Brennan
starts his warning dialogue. Trigger is roughly in the doorway."_

```
1. find_objects(name_pattern="OldMill*")
   → get the mill's position from its object

2. create_trigger_volume(
       name="QuestTrigger_ElderWarning",
       shape="box",
       position=[mill.position + doorway_offset],
       size=[2.5, 2.5, 1.5],
       tag="QuestTrigger",
       layer="Triggers",
       script_component="QuestTrigger",
       script_properties=[
         {propertyPath:"questId",    value:"elder_warning"},
         {propertyPath:"triggerOnce", value:true}
       ],
       parent_path="/World/Triggers"
   )
   → trigger is live in one call
```

### Pattern D: Standardising props after a scatter run

After `scatter_prefabs` places 40 rock props:

```
1. find_objects(name_pattern="Rock_*", near_position=scatter_center, near_radius=25)
   → all 40 paths

2. snap_to_surface(object_paths=[...40 paths...], align_to_normal=true)
   → embed rocks naturally in terrain

3. randomize_transforms(object_paths=[...40 paths...],
                         rotation_range=[15,180,15], scale_range=[0.7,1.3])
   → varied sizes and angles

4. set_object_metadata(object_paths=[...40 paths...], tag="Environment",
                        layer="Environment", static_flags=["All"])
   → ready for baking

5. group_objects(object_paths=[...40 paths...], group_name="RockScatter_Hillside_A")
```

---

## 6. Implementation Notes

### C# handler structure

Group 1 tools live in `ObjectHandler.cs`; Group 2 tools live in `DressingHandler.cs`.
Both are static classes in `UnityMcpBridge.Editor.Tools`, following the existing
handler pattern.

**`find_objects` implementation.** The spatial query uses
`Physics.OverlapSphere` for the `near_position` filter. All other filters (tag, layer,
component_type, name_pattern) are applied in a second pass on the returned Colliders'
GameObjects. Name pattern matching uses `fnmatch`-style glob via a simple `*` wildcard
expander (no external dependency needed).

**`snap_to_surface` implementation.** Uses `Physics.Raycast` from each object's pivot
position in the cast direction. Each raycast runs on the main thread. For bulk
operations (>10 objects), consider batching into 10-object Undo groups rather than one
per object.

**`dress_area` proposal integration.** `DressingHandler.DressAreaAsync` builds a
`ProposalOperation[]` array using the existing `ProposalOperation` model and calls the
same `ProposalStore.Submit()` path that `ProposalHandler.ProposeChangesAsync` uses.
The proposal is surfaced in `McpOperationQueueWindow` identically to any other proposal.
No changes to the proposal/execution pipeline are required.

**`apply_material` — material search.** Material lookup by tag queries
`AssetDatabase.FindAssets("t:Material")` and loads each, checking the material's name
against the tag. A more accurate approach would be to extend `PrefabMetadata` with an
opt-in `MaterialMetadata` ScriptableObject, but name-based tag matching is sufficient
for the common case and requires no schema changes.

**`create_trigger_volume` — collider sizing.** The `size` parameter is interpreted
differently per shape:
- Box: `[width, height, depth]` maps directly to `BoxCollider.size`
- Sphere: uses `size[0]` or `radius` as `SphereCollider.radius`
- Capsule: `radius` + `height` map to `CapsuleCollider.radius` + `CapsuleCollider.height`

---

## 7. Development Roadmap

### Phase 1 — Group 1: Foundation queries and mutation

Goal: the agent can find anything in the scene and read/write its properties.

**C# (ObjectHandler.cs):**
- [ ] `POST /tools/scene/find_objects` → tag, component, name, layer, spatial filters
- [ ] `POST /tools/objects/get_properties` → read serialised component values
- [ ] `POST /tools/objects/set_properties` → write serialised component values
- [ ] `POST /tools/objects/set_metadata` → tag, layer, name, static flags (bulk)
- [ ] `POST /tools/objects/reparent`
- [ ] `POST /tools/objects/duplicate`

**Python:**
- [ ] `find_objects`, `get_object_properties`, `set_object_properties`
- [ ] `set_object_metadata`, `reparent_objects`, `duplicate_objects`

Acceptance criterion: agent reads all lights in a 20m radius around a point, identifies
those with intensity > 1.5, and normalises them to 1.0 in a single conversation turn.

### Phase 2 — Group 2: Precision placement tools

Goal: snapping, distribution, variation, and organisation.

**C# (DressingHandler.cs — first batch):**
- [ ] `POST /tools/placement/snap_to_surface`
- [ ] `POST /tools/placement/align_and_distribute` (all four modes)
- [ ] `POST /tools/placement/randomize_transforms`
- [ ] `POST /tools/placement/group_objects`

**Python:**
- [ ] `snap_to_surface`, `align_and_distribute`, `randomize_transforms`, `group_objects`

Acceptance criterion: agent places 6 fence posts evenly between two specified points,
snaps them to the terrain, adds ±5° Y rotation variation, marks them as
NavigationStatic, and groups them into a container — all from a single description.

### Phase 3 — Group 2: Functional volumes and materials

**C# (DressingHandler.cs — second batch):**
- [ ] `POST /tools/placement/create_trigger_volume` (box, sphere, capsule with optional script)
- [ ] `POST /tools/placement/apply_material` (exact, tag-based, random-from-tag)

**Python:**
- [ ] `create_trigger_volume`, `apply_material`

Acceptance criterion: agent creates a dialogue trigger in a mill doorway with the
correct script component and property values configured in one call.

### Phase 4 — Group 2: dress_area

Goal: intent-driven area dressing via the existing proposal system.

**C# (DressingHandler.cs — dress_area):**
- [ ] Poisson disk candidate generation within radius
- [ ] Spatial avoidance check against existing objects
- [ ] Surface raycast for each candidate
- [ ] Prefab selection by tag overlap + SceneContext bonus (reuse BakeHandler scoring)
- [ ] Submit as ProposalStore entry
- [ ] PCG framework integration (optional path if PCGFramework present)

**Python:**
- [ ] `dress_area`

Acceptance criterion: agent calls `dress_area` with a market stall intent, proposal
appears in Unity's Operation Queue window with per-placement descriptions, user approves
12 of 14, and prefabs are instantiated at the correct positions after execute.

---

*Document version 1.0 — scope: Foundation and Dressing Workflow tool groups only.
Scaffold, Cutscene, PCG, and lighting tools are covered in separate documents.*
