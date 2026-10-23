# Level Design Scaffold System — Architecture Document

## Table of Contents

1. [Purpose](#1-purpose)
2. [Design Principles](#2-design-principles)
3. [Assembly Structure](#3-assembly-structure)
4. [Core Components](#4-core-components)
   - 4.1 [ScaffoldZone](#41-scaffoldzone)
   - 4.2 [ScaffoldAnchor](#42-scaffoldanchor)
   - 4.3 [GameplayMarker](#43-gameplaymarker)
5. [Zone Type System](#5-zone-type-system)
6. [Anchor Generation Rules](#6-anchor-generation-rules)
7. [Editor Overlay](#7-editor-overlay)
8. [MCP Integration](#8-mcp-integration)
   - 8.1 [Route Map](#81-route-map)
   - 8.2 [Request and Response Shapes](#82-request-and-response-shapes)
   - 8.3 [Python Tool Catalogue](#83-python-tool-catalogue)
9. [Bake Workflow](#9-bake-workflow)
10. [File Structure](#10-file-structure)
11. [Development Roadmap](#11-development-roadmap)

---

## 1. Purpose

The scaffold system bridges the gap between a level design document and a playable Unity scene. It provides a structured, editor-only layer of named spatial reference points, zone volumes, and functional intent markers that an AI agent can create directly from a design document and that a designer can iterate on visually before any real assets are placed.

The system targets the workflow where a level designer has a written description of a location — including zone dimensions, enemy positions, trap locations, and loot placements expressed in narrative or relative terms — and needs to translate that into a working scene layout without placing final assets prematurely.

The scaffold is not a finished scene. It is working documentation that lives inside the editor, drives the placement of final prefabs via the bake step, and is hidden (not deleted) when the area is complete so the design intent remains recoverable.

---

## 2. Design Principles

**Standalone.** The scaffold assembly has no dependencies beyond `UnityEngine`. It does not reference the MCP Bridge, the PCG Framework, or the Prefab Metadata system. Any project can install it without installing anything else.

**Editor-only runtime footprint.** Scaffold components are added to scene GameObjects, so they must live in a runtime-accessible assembly. However, all visual and editor-specific code is guarded with `#if UNITY_EDITOR`. `HideFlags.DontSaveInBuild` is set on every scaffold object at creation time. No scaffold data ships in a build.

**Spatial skeleton first.** The agent creates zones, connections, and anchors before placing any asset. The skeleton can be walked through in the editor and verified against the design document before anything is committed.

**Markers carry intent, not implementation.** A `GameplayMarker` records what should be at a location (an enemy with these stats, a trap with this radius, a loot container with these items) using semantic tags and a config blob. It does not reference any specific prefab. The connection to actual assets is made exclusively during the bake step.

**Bake is always proposal-first.** The agent never instantiates prefabs silently. The resolve step returns ranked candidates per marker; the designer or agent reviews and confirms before execute runs.

**MCP Bridge is the only integration point.** The bridge is the only assembly that references both the scaffold system and the Prefab Metadata system. Neither system knows about the other. The bridge orchestrates the bake step.

---

## 3. Assembly Structure

```
Assets/
└── LevelScaffold/
    ├── LevelScaffold.asmdef          # Runtime assembly, no external references
    ├── Runtime/
    │   ├── ScaffoldZone.cs
    │   ├── ScaffoldAnchor.cs
    │   ├── GameplayMarker.cs
    │   └── ZoneTypeDefinitions.cs
    └── Editor/
        ├── LevelScaffold.Editor.asmdef   # Editor assembly, references LevelScaffold
        ├── ScaffoldOverlay.cs
        ├── ScaffoldZoneEditor.cs
        └── GameplayMarkerEditor.cs
```

**`LevelScaffold.asmdef`**

```json
{
  "name": "LevelScaffold",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "autoReferenced": false
}
```

No external references. `autoReferenced: false` prevents Unity from silently pulling it into all assemblies.

**`LevelScaffold.Editor.asmdef`**

```json
{
  "name": "LevelScaffold.Editor",
  "references": ["LevelScaffold"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "autoReferenced": false
}
```

**MCP Bridge `.asmdef`** must add `"LevelScaffold"` and `"LevelScaffold.Editor"` to its references to gain access to the scaffold types from the handler code.

---

## 4. Core Components

### 4.1 ScaffoldZone

Attached to the root GameObject of each zone. Stores the zone definition and draws the volume gizmo.

**Serialised fields**

| Field | Type | Description |
|---|---|---|
| `ZoneType` | `ScaffoldZoneType` (enum) | Determines anchor generation and gizmo shape |
| `Dimensions` | `ZoneDimensions` | Type-specific dimension struct (see §5) |
| `DesignNote` | `string` | Copied verbatim from the design document. Displayed in the inspector and in the scene gizmo label. Preserved permanently as the authoritative record of design intent. |
| `DressingTags` | `string[]` | Semantic tags describing ambient dressing for this zone (e.g. `["dungeon", "damp", "medieval"]`). Used by the bake step to query PrefabMetadata for environmental fill. |
| `ConnectedZones` | `ScaffoldZone[]` | References to adjacent zones, populated by `link_zones`. |

**Gizmo behaviour**

- `OnDrawGizmos`: draws the zone volume outline (wireframe) using the zone-type colour from the palette, plus a small label with the zone name at the top-left of the volume. Always visible, regardless of selection.
- `OnDrawGizmosSelected`: additionally draws filled semi-transparent volume and dimension text.
- Gizmo colour is determined by zone type:

| Zone type | Colour |
|---|---|
| Staircase / vertical | Blue |
| Corridor / tunnel | Amber |
| Chamber / room (circular) | Purple |
| Chamber / room (rectangular) | Purple |
| Open area | Green |
| Connection (transition) | Grey dashed |

**Lifecycle**

- Created by the MCP handler via `create_zone_scaffold`.
- `hideFlags = HideFlags.DontSaveInBuild` set on `Awake`.
- Child anchors are created at the same time by the handler; `ScaffoldZone` does not generate them at runtime.

---

### 4.2 ScaffoldAnchor

Attached to every named position child of a `ScaffoldZone`. Provides a spatial reference point the agent can use in subsequent placement calls.

**Serialised fields**

| Field | Type | Description |
|---|---|---|
| `AnchorType` | `ScaffoldAnchorType` (enum) | Controls gizmo colour and shape |
| `AnchorLabel` | `string` | Unique within the zone; forms the addressable name (e.g. `step_05`, `_mid`, `_far_end`) |

**Anchor type enum**

```
Entry       – zone entry point (green arrow)
Exit        – zone exit point (green arrow, opposite direction)
Named       – explicitly named position from design doc (white sphere)
Step        – indexed step on a staircase (grey dot)
WallRef     – wall surface reference used for relative placement (blue tick)
Connection  – connection point to an adjacent zone (cyan diamond)
```

**Gizmo behaviour**

- `OnDrawGizmos`: type-appropriate shape at the anchor's world position, always visible.
- `OnDrawGizmosSelected`: label drawn in scene view space via `Handles.Label`.
- Step anchors use a smaller gizmo (3px) to avoid cluttering a staircase with 25 dots.
- Entry/Exit anchors draw a directional arrow indicating facing direction.

**Addressing**

An anchor is addressed in MCP tool calls as a hierarchy path:

```
/ForgottenTunnels/Zone1_Descent/step_05
/ForgottenTunnels/Zone1_Descent/_mid
/ForgottenTunnels/Zone3_FloodedHall/_center
```

---

### 4.3 GameplayMarker

Represents a single piece of functional intent at a known position. Does not reference any prefab — the semantic tags are the only connection to the asset system.

**Serialised fields**

| Field | Type | Description |
|---|---|---|
| `MarkerType` | `GameplayMarkerType` (enum) | Controls gizmo, drives bake behaviour |
| `MarkerLabel` | `string` | Unique label from the design doc (e.g. `AncientDrowner_Mid`, `SqueakyStep_05`) |
| `SemanticTags` | `string[]` | Tags describing what asset should fulfil this marker. Matched against `PrefabMetadata.tags[]` during the bake resolve step. |
| `DetectionRadius` | `float` | World-space radius drawn as a translucent sphere (for enemies, traps, audio zones). Zero = no radius drawn. |
| `ConfigJson` | `string` | JSON object containing marker-type-specific configuration. See config schema below. |
| `BakeState` | `BakeState` (enum) | `Pending`, `Resolved`, `Baked`. Updated by the bake workflow. |
| `ResolvedPrefabGuid` | `string` | Populated by the resolve step with the chosen prefab GUID. Empty until resolved. |

**Marker type enum and default semantic tag patterns**

| Type | Default tag pattern | Config keys |
|---|---|---|
| `EnemySpawn` | `["enemy", "{type}", ...]` | `enemy_type`, `health`, `damage`, `detection_type`, `detection_radius`, `behavior` |
| `Trap` | `["trap", "{trap_type}", ...]` | `trap_type`, `alert_radius`, `trigger_condition`, `fixable`, `fix_item`, `fix_state_key` |
| `LootContainer` | `["loot", "{container_type}", ...]` | `container_type`, `loot_items[]` |
| `Interaction` | `["interaction", "{interaction_type}", ...]` | `interaction_type`, `requirement`, `reward`, `state_key` |
| `AmbientDressing` | author-defined | `density_hint`, `placement_style` |
| `Custom` | author-defined | freeform |

**ConfigJson example — EnemySpawn**

```json
{
  "enemy_type": "AncientDrowner",
  "health": 80,
  "damage": 15,
  "detection_type": "hearing",
  "detection_radius": 10.0,
  "behavior": "stationary_head_tracking",
  "alert_behavior": "shamble_toward_source"
}
```

**ConfigJson example — Trap**

```json
{
  "trap_type": "squeaky_step",
  "alert_radius": 20.0,
  "trigger_condition": "walk",
  "crouch_reduction": 0.8,
  "fixable": true,
  "fix_item": "Oil",
  "fix_state_key": "squeaky_step_05_oiled"
}
```

**Gizmo behaviour**

- `OnDrawGizmos`: type-specific icon glyph + `DetectionRadius` drawn as a translucent sphere. Colour indicates `BakeState` (orange = Pending, yellow = Resolved, teal = Baked).
- Always visible regardless of selection.
- `BakeState = Baked` dims the gizmo to indicate it is no longer actionable.

---

## 5. Zone Type System

Zone types are defined as a C# enum. Each type determines the `ZoneDimensions` fields that are meaningful and the anchors that are auto-generated at creation time.

```csharp
public enum ScaffoldZoneType
{
    SpiralStaircase,
    StraightCorridor,
    CircularChamber,
    RectangularRoom,
    CrawlTunnel,
    OpenArea,
    Custom
}
```

### ZoneDimensions

`ZoneDimensions` is a single serialised struct. Fields are interpreted differently per zone type. Unused fields are ignored.

| Field | Type | Used by |
|---|---|---|
| `Width` | `float` | All types |
| `Depth` | `float` | StraightCorridor, RectangularRoom, OpenArea |
| `Height` | `float` | All types |
| `Diameter` | `float` | CircularChamber |
| `StepCount` | `int` | SpiralStaircase |
| `RiserHeight` | `float` | SpiralStaircase |
| `Turns` | `float` | SpiralStaircase (e.g. 2.5 = two and a half rotations) |
| `Radius` | `float` | OpenArea (circular), CircularChamber (alias for Diameter/2) |

---

## 6. Anchor Generation Rules

When the MCP handler calls `create_zone_scaffold`, it creates the zone container and then calls the zone type's anchor generator to produce the child `ScaffoldAnchor` GameObjects. The generator for each type is deterministic — the same dimensions always produce the same named anchors at the same positions.

### SpiralStaircase

Required dimensions: `Width`, `StepCount`, `RiserHeight`, `Turns`

Generated anchors:

| Label | Type | Position |
|---|---|---|
| `_entry` | Entry | Top of staircase |
| `_exit` | Exit | Bottom of staircase |
| `_mid` | Named | Step at `StepCount / 2`, rounded down |
| `step_NN` (all steps) | Step | Per-step position along the spiral, zero-padded two digits |

Step world positions are calculated from the staircase origin using `RiserHeight`, `Width / 2` as the spiral radius, and `Turns * 2π / StepCount` as the angular increment per step.

### StraightCorridor

Required dimensions: `Width`, `Depth` (length), `Height`

Generated anchors:

| Label | Type | Position |
|---|---|---|
| `_entry` | Entry | Centre of entry face |
| `_exit` | Exit | Centre of exit face |
| `_mid` | Named | Centre of the corridor |
| `_quarter` | Named | 25% along the length |
| `_three_quarter` | Named | 75% along the length |
| `_left_wall_mid` | WallRef | Centre of left wall |
| `_right_wall_mid` | WallRef | Centre of right wall |

### CircularChamber

Required dimensions: `Diameter`, `Height`

Generated anchors:

| Label | Type | Position |
|---|---|---|
| `_entry` | Entry | North edge (default; rotated by facing at creation) |
| `_exit` | Exit | South edge |
| `_center` | Named | Geometric centre |
| `_far_end` | Named | Diametrically opposite `_entry` |
| `_north`, `_south`, `_east`, `_west` | Named | Cardinal compass positions on perimeter |
| `_perimeter_00` … `_perimeter_07` | Named | 8 evenly-spaced perimeter points (0° = north, clockwise) |

### RectangularRoom

Required dimensions: `Width`, `Depth`, `Height`

Generated anchors: `_entry` (default south face), `_exit` (north face), `_center`, `_corner_ne`, `_corner_nw`, `_corner_se`, `_corner_sw`, `_wall_north_mid`, `_wall_south_mid`, `_wall_east_mid`, `_wall_west_mid`

### CrawlTunnel

Required dimensions: `Width`, `Depth` (length), `Height`

Generated anchors: `_entry`, `_exit`, `_mid`

### OpenArea

Required dimensions: `Width`, `Depth` (or `Radius` for circular), `Height`

Generated anchors: `_center`, `_edge_north`, `_edge_south`, `_edge_east`, `_edge_west`, `_edge_ne`, `_edge_nw`, `_edge_se`, `_edge_sw`

### Custom

No anchors generated automatically. All anchors are created manually via `create_reference_point`.

---

## 7. Editor Overlay

The scaffold overlay is a standalone scene view toolbar built using Unity's `Overlay` API (Unity 2021.2+). It is part of the `LevelScaffold.Editor` assembly and has no dependency on any other editor assembly.

**Toolbar controls**

| Control | Type | Effect |
|---|---|---|
| Show zones | Toggle | Show/hide zone volume gizmos |
| Show anchors | Toggle | Show/hide anchor gizmos |
| Show markers | Toggle | Show/hide gameplay marker gizmos |
| Show radii | Toggle | Show/hide detection/trigger radius spheres |
| Show labels | Toggle | Show/hide text labels on anchors and markers |
| Bake status | Read-only badge | `N pending / M resolved / K baked` across the scene |

Toggle states are persisted in `SessionState` (survive editor restart, cleared on Unity close).

**Custom inspector contributions**

- `ScaffoldZoneEditor`: displays `DesignNote` in a large read-only text box at the top of the inspector. Adds a "Refresh Anchors" button that regenerates anchors from current dimensions (with a confirmation dialog — this destroys and recreates all child anchors).
- `GameplayMarkerEditor`: displays `ConfigJson` in a pretty-printed, read-only JSON panel. Shows `SemanticTags` as a tag-chip list. Shows `BakeState` with colour-coded badge.

---

## 8. MCP Integration

The MCP Bridge handles all scaffold operations. The `LevelScaffold` assembly is referenced by the Bridge's editor `.asmdef` alongside `PrefabMetadata`. The scaffold system itself has no knowledge of the bridge.

### 8.1 Route Map

All scaffold routes follow the existing bridge conventions: `GET` for read-only queries, `POST` for operations that mutate the scene.

| Route | Handler | Description |
|---|---|---|
| `GET /assets/scaffold/zones` | `ScaffoldQueryHandler` | List all ScaffoldZone instances in the active scene |
| `GET /assets/scaffold/markers` | `ScaffoldQueryHandler` | List all GameplayMarker instances |
| `GET /assets/scaffold/anchors` | `ScaffoldQueryHandler` | List all ScaffoldAnchor instances |
| `POST /tools/scaffold/create_zone` | `ScaffoldHandler` | Create a zone scaffold with auto-generated anchors |
| `POST /tools/scaffold/link_zones` | `ScaffoldHandler` | Connect two zones with a transition description |
| `POST /tools/scaffold/create_reference_point` | `ScaffoldHandler` | Add a named anchor to an existing zone |
| `POST /tools/scaffold/create_marker` | `ScaffoldHandler` | Place a GameplayMarker at an anchor or world position |
| `POST /tools/scaffold/update_marker` | `ScaffoldHandler` | Update a marker's semanticTags or configJson |
| `POST /tools/scaffold/resolve_markers` | `BakeHandler` | Match semanticTags → PrefabMetadata; return ranked candidates |
| `POST /tools/scaffold/execute_bake` | `BakeHandler` | Instantiate approved prefabs; update BakeState |
| `POST /tools/scaffold/hide_scaffold` | `ScaffoldHandler` | Set all scaffold objects inactive (reversible) |
| `POST /tools/scaffold/show_scaffold` | `ScaffoldHandler` | Re-activate all scaffold objects |

### 8.2 Request and Response Shapes

All requests follow the bridge envelope convention: `POST` bodies are flat JSON objects parsed directly into C# request DTOs. All responses use `{ "success": true, "data": {} }` or `{ "success": false, "error": { "code": "", "message": "" } }`.

**`create_zone`**

```json
{
  "zone_name": "Zone1_Descent",
  "zone_type": "SpiralStaircase",
  "dimensions": {
    "width": 2.0,
    "step_count": 25,
    "riser_height": 0.2,
    "turns": 2.5
  },
  "entry_position": [0.0, 20.0, 0.0],
  "entry_rotation": [0.0, 0.0, 0.0],
  "parent_path": "/ForgottenTunnels",
  "design_note": "Tight spiral staircase (2m wide) — claustrophobic. Walls damp with condensation."
}
```

Response `data`:

```json
{
  "zone_path": "/ForgottenTunnels/Zone1_Descent",
  "anchors_created": 27,
  "anchor_paths": [
    "/ForgottenTunnels/Zone1_Descent/_entry",
    "/ForgottenTunnels/Zone1_Descent/step_01",
    "..."
  ]
}
```

---

**`link_zones`**

```json
{
  "from_zone_path": "/ForgottenTunnels/Zone1_Descent",
  "to_zone_path": "/ForgottenTunnels/Zone2_PrisonersWalk",
  "transition_type": "StraightCorridor",
  "dimensions": { "width": 3.0, "depth": 30.0, "height": 3.0 },
  "connection_label": "Conn_Z1_Z2"
}
```

Response `data`:

```json
{
  "connection_path": "/ForgottenTunnels/Conn_Z1_Z2",
  "from_anchor": "/ForgottenTunnels/Zone1_Descent/_exit",
  "to_anchor": "/ForgottenTunnels/Zone2_PrisonersWalk/_entry"
}
```

---

**`create_marker`**

```json
{
  "marker_label": "AncientDrowner_Mid",
  "marker_type": "EnemySpawn",
  "anchor_path": "/ForgottenTunnels/Zone1_Descent/_mid",
  "position_offset": [0.5, 0.0, 0.0],
  "semantic_tags": ["enemy", "drowner", "undead", "blind", "sound_sensitive"],
  "detection_radius": 10.0,
  "config": {
    "enemy_type": "AncientDrowner",
    "health": 80,
    "damage": 15,
    "detection_type": "hearing",
    "behavior": "stationary_head_tracking",
    "alert_behavior": "shamble_toward_source"
  }
}
```

The `anchor_path` field is optional. If omitted, `world_position: [x, y, z]` must be provided instead.

---

**`resolve_markers`**

```json
{
  "zone_path": "/ForgottenTunnels/Zone1_Descent",
  "marker_types": ["EnemySpawn", "LootContainer"],
  "max_candidates_per_marker": 3
}
```

If `zone_path` is omitted, resolves all pending markers in the scene. `marker_types` acts as a filter.

Response `data`:

```json
{
  "proposals": [
    {
      "marker_path": "/ForgottenTunnels/Zone1_Descent/AncientDrowner_Mid",
      "marker_label": "AncientDrowner_Mid",
      "marker_type": "EnemySpawn",
      "candidates": [
        {
          "asset_guid": "abc123",
          "asset_path": "Assets/Prefabs/Enemies/AncientDrowner.prefab",
          "display_name": "Ancient Drowner",
          "tag_overlap_count": 4,
          "scene_context_bonus": 1,
          "total_score": 5
        }
      ]
    }
  ],
  "unresolvable": []
}
```

`unresolvable` lists markers for which no PrefabMetadata asset matched any semantic tag.

---

**`execute_bake`**

```json
{
  "confirmations": [
    {
      "marker_path": "/ForgottenTunnels/Zone1_Descent/AncientDrowner_Mid",
      "asset_guid": "abc123"
    },
    {
      "marker_path": "/ForgottenTunnels/Zone1_Descent/Loot_Skeleton",
      "asset_guid": "def456"
    }
  ],
  "hide_scaffold_after": false
}
```

Response `data`:

```json
{
  "baked_count": 2,
  "instances": [
    {
      "marker_label": "AncientDrowner_Mid",
      "instance_path": "/ForgottenTunnels/Zone1_Descent/AncientDrowner_Mid_Instance"
    }
  ],
  "warnings": []
}
```

### 8.3 Python Tool Catalogue

Tools live in `mcp-server/src/unity_mcp/tools/scaffold.py`. Each tool follows the existing bridge pattern: individual parameters, a Pydantic request model from `models.py`, and a `client.post()` or `client.get()` call.

| Python tool | HTTP call | Summary |
|---|---|---|
| `get_scaffold_zones()` | `GET /assets/scaffold/zones` | List all zones in the scene |
| `get_scaffold_markers()` | `GET /assets/scaffold/markers` | List all markers, optionally filtered by zone or bake state |
| `create_zone_scaffold(...)` | `POST /tools/scaffold/create_zone` | Create a zone with auto-generated anchors |
| `link_zones(...)` | `POST /tools/scaffold/link_zones` | Connect two zones |
| `create_reference_point(...)` | `POST /tools/scaffold/create_reference_point` | Add a named anchor manually |
| `create_gameplay_marker(...)` | `POST /tools/scaffold/create_marker` | Place a functional intent marker |
| `update_gameplay_marker(...)` | `POST /tools/scaffold/update_marker` | Update tags or config on an existing marker |
| `resolve_scaffold_markers(...)` | `POST /tools/scaffold/resolve_markers` | Match semantic tags → prefab candidates |
| `execute_bake(...)` | `POST /tools/scaffold/execute_bake` | Instantiate confirmed prefabs |
| `hide_scaffold()` | `POST /tools/scaffold/hide_scaffold` | Hide all scaffold objects |
| `show_scaffold()` | `POST /tools/scaffold/show_scaffold` | Show all scaffold objects |

---

## 9. Bake Workflow

The bake workflow translates the scaffold's semantic intent into actual prefab instances. It runs in two explicit steps to preserve the proposal-first principle.

### Step 1 — Resolve

The agent calls `resolve_scaffold_markers`. The `BakeHandler` in the bridge:

1. Finds all `GameplayMarker` instances with `BakeState == Pending` in the target scope.
2. For each marker, loads all `PrefabMetadata` ScriptableObjects via `AssetDatabase.FindAssets("t:PrefabMetadata")`.
3. Scores each prefab against the marker's `SemanticTags`:
   - Base score: count of tags in `PrefabMetadata.tags[]` that appear in `marker.SemanticTags`.
   - Context bonus: +1 for each tag that appears in both `PrefabMetadata.tags[]` and the active `SceneContext.activeTags`.
4. Returns the top `max_candidates_per_marker` results per marker, sorted descending by score.
5. Markers with zero matches are returned in the `unresolvable` list with their semantic tags, so the agent can report them and prompt the designer to either add metadata to a prefab or adjust the tags.
6. Markers are **not** updated — `BakeState` stays `Pending` until `execute_bake` runs.

The agent presents the proposals in the conversation and waits for confirmation. The designer may accept all, reject individual markers, or substitute a different candidate.

### Step 2 — Execute

The agent calls `execute_bake` with the confirmed marker→GUID mapping. The `BakeHandler`:

1. For each confirmation:
   a. Resolves `asset_guid` to an asset path via `AssetDatabase.GUIDToAssetPath`.
   b. Loads the prefab via `AssetDatabase.LoadAssetAtPath<GameObject>`.
   c. Instantiates it at the marker's world position and rotation.
   d. Parents it to the marker's parent zone container.
   e. Registers the instantiation with `Undo`.
   f. Sets `marker.BakeState = Baked` and `marker.ResolvedPrefabGuid = asset_guid`.
2. If `hide_scaffold_after` is true, sets all scaffold objects in the affected zones inactive.
3. Marks the scene dirty.

### Zone dressing (separate concern)

Zone-level ambient fill (torches along a corridor, debris on a floor) is handled separately from functional markers. The workflow depends on what is available:

- **PCG Framework present**: the agent generates and runs a PCG graph scoped to the zone's bounds, using `DressingTags` to query PrefabMetadata for appropriate assets. The graph is attached to the zone container.
- **PCG Framework absent**: the agent uses the existing `scatter_prefabs` or `place_along_path` MCP tools with prefabs found via the metadata system.

The dressing step is triggered independently of the marker bake. Neither blocks the other.

---

## 10. File Structure

```
Assets/
└── LevelScaffold/
    ├── LevelScaffold.asmdef
    ├── Runtime/
    │   ├── ScaffoldZone.cs               Zone component: fields, lifecycle, HideFlags
    │   ├── ScaffoldAnchor.cs             Anchor component: fields, lifecycle
    │   ├── GameplayMarker.cs             Marker component: fields, BakeState, lifecycle
    │   ├── ZoneTypeDefinitions.cs        ScaffoldZoneType enum, ZoneDimensions struct
    │   └── AnchorTypeDefinitions.cs      ScaffoldAnchorType enum, GameplayMarkerType enum
    └── Editor/
        ├── LevelScaffold.Editor.asmdef
        ├── ScaffoldOverlay.cs            Scene view overlay: toggles, bake status badge
        ├── ScaffoldAnchorGenerator.cs    Per-zone-type anchor generation logic
        ├── ScaffoldZoneEditor.cs         Custom inspector for ScaffoldZone
        └── GameplayMarkerEditor.cs       Custom inspector for GameplayMarker

Assets/
└── UnityMcpBridge/
    └── Editor/
        ├── [existing core, tools, metadata, UI]
        └── Scaffold/
            ├── ScaffoldQueryHandler.cs   GET routes: zones, markers, anchors
            ├── ScaffoldHandler.cs        POST routes: create, link, reference point, marker, hide/show
            └── BakeHandler.cs            POST routes: resolve_markers, execute_bake

mcp-server/
└── src/
    └── unity_mcp/
        ├── models.py                     Add: scaffold request/response Pydantic models
        └── tools/
            └── scaffold.py               register_scaffold_tools(server)
```

---

## 11. Development Roadmap

Work is divided into three phases. Each phase is independently releasable and adds usable capability. Phase 1 delivers the visual scaffolding layer. Phase 2 makes it agent-accessible. Phase 3 connects it to the asset system.

### Phase 1 — Standalone scaffold

Goal: a designer can create scaffold objects manually in the editor and see the visualisation.

- [ ] `ZoneTypeDefinitions.cs` — enum and `ZoneDimensions` struct
- [ ] `AnchorTypeDefinitions.cs` — enums
- [ ] `ScaffoldAnchor.cs` — component with Gizmo drawing (`#if UNITY_EDITOR`)
- [ ] `GameplayMarker.cs` — component with Gizmo drawing, radius sphere, bake state colour
- [ ] `ScaffoldZone.cs` — component with type-appropriate volume Gizmo
- [ ] `ScaffoldAnchorGenerator.cs` — per-type anchor generation; callable from editor and MCP handler
- [ ] `ScaffoldZoneEditor.cs` — custom inspector with DesignNote display and Refresh Anchors button
- [ ] `GameplayMarkerEditor.cs` — custom inspector with tag chips and ConfigJson panel
- [ ] `ScaffoldOverlay.cs` — scene view toolbar with toggles and bake status
- [ ] `LevelScaffold.asmdef` and `LevelScaffold.Editor.asmdef`

Acceptance criterion: a developer can manually create a `ScaffoldZone` GameObject, configure it as a spiral staircase, run Refresh Anchors, and see all 25+ anchor points rendered in the scene view.

### Phase 2 — MCP integration

Goal: an agent can read a design document, call scaffold tools, and produce a complete zone layout in Unity from a single conversation.

- [ ] Pydantic models in `models.py` for all scaffold request/response shapes
- [ ] `ScaffoldQueryHandler.cs` — GET routes
- [ ] `ScaffoldHandler.cs` — POST routes (create zone, link, reference point, marker, hide/show)
- [ ] `scaffold.py` — Python tool registrations
- [ ] Register all routes in `McpRouter.cs`
- [ ] Add `LevelScaffold` and `LevelScaffold.Editor` references to bridge `.asmdef`
- [ ] Tests: zone creation with anchor generation, zone linking, marker placement

Acceptance criterion: agent reads Zone 1 of a design document and produces a correctly anchored spiral staircase scaffold with two enemy markers, three trap markers, and one loot marker, all positioned relative to the named step anchors.

### Phase 3 — Bake workflow

Goal: the agent can translate a completed scaffold into actual prefab instances using the PrefabMetadata system.

- [ ] `BakeHandler.cs` — `resolve_markers` and `execute_bake` routes
- [ ] Scoring algorithm: tag overlap + scene context bonus
- [ ] Handling of unresolvable markers (returned in response, not silently dropped)
- [ ] `BakeState` update on markers after execute
- [ ] Undo registration for all instantiated prefabs
- [ ] `hide_scaffold` / `show_scaffold` operations
- [ ] Python tools: `resolve_scaffold_markers`, `execute_bake`
- [ ] Tests: resolve scoring with known PrefabMetadata, execute with GUID confirmation, unresolvable marker handling

Acceptance criterion: agent resolves all markers in a completed Zone 1 scaffold, presents candidates, and on confirmation instantiates the correct enemy prefabs and loot container at the correct positions with the scene registering the change for Undo.

---

*Document version 1.0 — scope: scaffold system only. MCP bridge compatibility, PCG integration, and component tooling are covered in separate documents.*
