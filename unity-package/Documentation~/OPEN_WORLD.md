# Open World Tools — Architecture Document

## Table of Contents

1. [Context](#1-context)
2. [Group 3 — Open World](#2-group-3--open-world)
   - 2.1 [query_terrain](#21-query_terrain)
   - 2.2 [bake_navmesh](#22-bake_navmesh)
   - 2.3 [set_navmesh_area](#23-set_navmesh_area)
   - 2.4 [manage_scene](#24-manage_scene)
3. [MCP Integration](#3-mcp-integration)
   - 3.1 [Route Map](#31-route-map)
   - 3.2 [Request and Response Shapes](#32-request-and-response-shapes)
   - 3.3 [Python Tool Catalogue](#33-python-tool-catalogue)
4. [Compound Workflow Patterns](#4-compound-workflow-patterns)
5. [Implementation Notes](#5-implementation-notes)
6. [Development Roadmap](#6-development-roadmap)

---

## 1. Context

This document specifies the third group of tools identified as high-leverage additions to the MCP Bridge for a solo developer working on open-world 3D games.

**Group 1 — Foundation** provided the query and mutation primitives needed to inspect and modify existing scene content. **Group 2 — Dressing Workflow** addressed medium-scale prop placement and area dressing. **Group 3 — Open World** covers the three remaining pain points that are specific to the streaming multi-scene open-world architecture:

- The agent is blind to the terrain it's placing objects onto. It cannot ask whether a position is on a slope, a path, or shallow water, which limits the quality of every placement decision it makes.
- NavMesh must be rebaked after every layout change. As a solo developer doing all geometry work, this is a constant source of friction — the agent should be able to trigger it at the end of any operation that modifies walkable surfaces.
- Editing an open world built from multiple streaming scenes requires constantly loading, unloading, and reorganising additive scenes in the editor. None of that is currently accessible through the bridge.

These tools are not individually dramatic. Their value comes from composability: once the agent can read terrain, bake navmesh, and manage which scenes are loaded, it can complete a full area-dressing workflow without the developer ever leaving the conversation.

---

## 2. Group 3 — Open World

### 2.1 query_terrain

**What it does.** Given one or more world-space positions, samples the active terrain tile covering each position and returns the surface height, interpolated normal vector, slope angle, and the weight of each terrain layer at that point. Supports bulk queries to avoid per-placement round-trips.

**Why it matters.** Without terrain data, the agent places objects at a fixed Y coordinate and hopes for the best. With it, the agent can answer: "Is this slope too steep for a building?", "What texture is painted here — is this a dirt road or a meadow?", "Should I align this rock to the terrain normal?" The prefab metadata `placement.requiresFlatGround` rule becomes enforceable instead of decorative.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `positions` | float[3][] | One or more world-space query points. Each is `[x, y, z]` — the Y component is ignored for sampling but returned in the response for convenience |
| `include_layer_weights` | bool | Whether to return per-layer alpha weights (default true). Set false when only height/normal is needed to avoid the alphamap cost on high-resolution terrain |

**Response per position:**

```json
{
  "position":       [45.0, 0.0, 22.0],
  "surface_height": 3.42,
  "normal":         [0.05, 0.98, -0.12],
  "slope_degrees":  8.1,
  "terrain_name":   "Terrain_Tile_03",
  "layer_weights": [
    { "layer_name": "Grass",      "weight": 0.72 },
    { "layer_name": "DirtPath",   "weight": 0.21 },
    { "layer_name": "RockyGround","weight": 0.07 }
  ],
  "error": null
}
```

Positions that fall outside all active terrain tiles are returned with `"error": "NO_TERRAIN"` and null values for all sampled fields, rather than failing the entire request. This allows bulk queries over partially-terraformed areas to degrade gracefully.

**Design note.** `query_terrain` is a read-only GET route. Bulk queries (all positions in a single call) are essential — a scatter proposal for 30 props that checks terrain validity one-by-one would be unacceptably slow. The handler performs all sampling on the main thread in a single frame, so even large batches are fast.

---

### 2.2 bake_navmesh

**What it does.** Triggers a NavMesh bake for one or more `NavMeshSurface` components in the active scene. Supports baking all surfaces, baking surfaces on specific objects, or baking by surface agent type name.

**Package assumption.** This tool targets the **Unity AI Navigation package** (`com.unity.ai.navigation`, available in Unity 2022.2+ and bundled with Unity 6). The core type is `NavMeshSurface` from the `Unity.AI.Navigation` assembly. The legacy `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()` API is not used.

**Why it matters.** Every layout change that modifies walkable geometry invalidates the NavMesh. As a solo developer handling all of geometry, lighting, dressing, and gameplay, rebaking manually after each agent-driven change breaks the flow. `bake_navmesh` at the end of a placement or dressing operation keeps the NavMesh current without interrupting the conversation.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `surface_paths` | string[]? | Hierarchy paths of GameObjects whose `NavMeshSurface` components to bake. If omitted and `all` is false, bakes only the first surface found |
| `all` | bool | If true, find and bake every `NavMeshSurface` in the active scene (default false) |
| `agent_type_name` | string? | If provided, only bake surfaces whose AgentType name matches (e.g. `"Humanoid"`, `"Spider"`). Ignored if `surface_paths` is set |

**Response:**

```json
{
  "baked_count":    2,
  "surfaces": [
    {
      "object_path":   "/World/NavMesh/MainSurface",
      "agent_type":    "Humanoid",
      "elapsed_ms":    312
    },
    {
      "object_path":   "/World/NavMesh/FlyingSurface",
      "agent_type":    "Flyer",
      "elapsed_ms":    88
    }
  ],
  "warnings": []
}
```

**Design note.** `NavMeshSurface.BuildNavMesh()` is synchronous and runs on the main thread. For large terrains this can take several seconds — the handler blocks until all requested surfaces are complete and returns elapsed times so the agent can report meaningful progress. The response surfaces are listed in completion order.

---

### 2.3 set_navmesh_area

**What it does.** Assigns a NavMesh area type to one or more scene objects by adding or configuring a `NavMeshModifier` component. Also supports placing a `NavMeshModifierVolume` at a world position for volumetric area overrides that don't need to be attached to geometry.

**Why it matters.** NavMesh area types drive AI cost calculations, water avoidance, road preference, and no-go zones. In an open world, you place hundreds of trigger-like volumes for things like "water area" (high cost), "road" (reduced cost), "combat arena" (locked off until cleared), and "restricted zone" (impassable). Currently configuring these requires adding components and setting serialised properties by hand. With this tool the agent can tag areas during the same dressing pass that places the props.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `object_paths` | string[]? | Objects to add/configure a `NavMeshModifier` on. At least one of `object_paths` or `volume` must be provided |
| `area_name` | string | NavMesh area name from the project's area type list (e.g. `"Walkable"`, `"Not Walkable"`, `"Jump"`, or any custom area). Resolved to index via `GameObjectUtility.GetNavMeshAreaFromName` |
| `override_area` | bool | Whether to override the child hierarchy's area as well. Mirrors `NavMeshModifier.overrideArea` (default true) |
| `volume` | object? | If provided, creates a `NavMeshModifierVolume` instead of attaching a component to an existing object. Contains `position`, `size`, `rotation`, and optionally `parent_path` |

**`volume` sub-object:**

| Field | Type | Description |
|---|---|---|
| `position` | float[3] | World-space centre |
| `size` | float[3] | Volume extents `[width, height, depth]` |
| `rotation` | float[3]? | Euler angles (default `[0,0,0]`) |
| `name` | string? | Name for the created GameObject (default `"NavMeshVolume_{area_name}"`) |
| `parent_path` | string? | Hierarchy parent for the new object |

**Response:**

```json
{
  "updated_count": 3,
  "applied": [
    { "path": "/Village/Props/WaterPlane", "mode": "modifier", "area": "Water", "area_index": 3 },
    { "path": "/Village/Props/WaterEdge",  "mode": "modifier", "area": "Water", "area_index": 3 }
  ],
  "volumes_created": [
    { "path": "/World/NavVolumes/NavMeshVolume_Water", "area": "Water", "area_index": 3 }
  ],
  "warnings": ["Area 'Quicksand' not found in project settings. Available areas: Walkable, Not Walkable, Jump, Water, Road"]
}
```

An unknown `area_name` returns a warning listing all available area names rather than failing, so the agent can correct the call without a full round-trip inspection.

---

### 2.4 manage_scene

**What it does.** Provides editor-level control over Unity's multi-scene setup: listing loaded scenes, loading and unloading additive scenes, moving objects between scenes, and creating new scene files. Covers the scene management operations that a streaming open-world developer performs constantly while editing.

**Why it matters.** Open-world games built on terrain tiles or zone streaming load multiple scenes additively at runtime. In the editor, you typically keep one or two tiles loaded while working on a specific area and swap them as you move around the world. The agent currently cannot see which scenes are loaded, cannot bring in the adjacent tile to check a boundary, and cannot move a misplaced object into the correct scene. Every one of these is a task that interrupts the design conversation to become a mouse operation in the Unity editor.

`manage_scene` consolidates five related operations into a single tool family:

**Operations:**

| `operation` | Description |
|---|---|
| `list` | Return all currently loaded scenes with their paths, active status, and dirty flag |
| `load` | Load a scene additively by asset path |
| `unload` | Unload an additively-loaded scene by name or path. Refuses to unload the active scene if it is the only loaded scene |
| `move_objects` | Move one or more scene objects from their current scene into a target scene. Objects at non-root hierarchy positions are detached from their parent first (with a warning) unless the parent is also in the move list |
| `create` | Create a new empty scene asset at a specified path and optionally load it additively |

**Parameters (all operations share `operation`):**

| Parameter | Type | Used by | Description |
|---|---|---|---|
| `operation` | string | All | One of `list`, `load`, `unload`, `move_objects`, `create` |
| `scene_path` | string? | `load`, `unload`, `create` | Asset database path, e.g. `"Assets/Scenes/Village_NE.unity"`. `unload` also accepts a scene name |
| `set_active` | bool? | `load`, `create` | Make the loaded/created scene the active scene (default false) |
| `object_paths` | string[]? | `move_objects` | Objects to migrate |
| `target_scene_name` | string? | `move_objects` | Name of the destination scene (must already be loaded) |
| `save_after_create` | bool? | `create` | Write the scene file immediately after creation (default true) |

**Response examples:**

`list`:
```json
{
  "scenes": [
    { "name": "SampleScene",    "path": "Assets/Scenes/SampleScene.unity",    "is_active": true,  "is_dirty": false },
    { "name": "Village_NE",     "path": "Assets/Scenes/Village_NE.unity",     "is_active": false, "is_dirty": true  },
    { "name": "Village_NW",     "path": "Assets/Scenes/Village_NW.unity",     "is_active": false, "is_dirty": false }
  ]
}
```

`load`:
```json
{
  "scene_name": "Dungeon_B2",
  "scene_path": "Assets/Scenes/Dungeon_B2.unity",
  "loaded":     true,
  "is_active":  false
}
```

`move_objects`:
```json
{
  "moved_count": 4,
  "moved": [
    { "path": "/Village_NE/Prop_Barrel_07", "from_scene": "Village_NW", "to_scene": "Village_NE" }
  ],
  "warnings": ["Object '/Village_NW/Crate_Stack/Crate_A' was detached from parent 'Crate_Stack' before moving — parent was not included in the move list"]
}
```

---

## 3. MCP Integration

### 3.1 Route Map

All routes follow existing bridge conventions: `GET` for read-only queries, `POST` for scene-mutating operations.

**Terrain**

| Route | Handler | Description |
|---|---|---|
| `POST /tools/world/query_terrain` | `TerrainQueryHandler` | Sample height, normal, slope, and layer weights for one or more positions |

Note: this is `POST` rather than `GET` because the request body is an array of positions. A `GET` with query-string-encoded position arrays would be unwieldy.

**NavMesh**

| Route | Handler | Description |
|---|---|---|
| `POST /tools/world/bake_navmesh` | `NavMeshHandler` | Bake one, some, or all NavMeshSurface components in the active scene |
| `POST /tools/world/set_navmesh_area` | `NavMeshHandler` | Assign a NavMesh area type to objects or create a NavMeshModifierVolume |

**Scene Management**

| Route | Handler | Description |
|---|---|---|
| `GET /scene/scenes` | `SceneManagementHandler` | List all currently loaded scenes |
| `POST /tools/scene/manage` | `SceneManagementHandler` | Load, unload, move objects, or create scenes |

---

### 3.2 Request and Response Shapes

**`query_terrain` — request**

```json
{
  "positions": [
    [45.0, 0.0, 22.0],
    [60.0, 0.0, 30.0],
    [12.5, 0.0, 8.0]
  ],
  "include_layer_weights": true
}
```

**`query_terrain` — response**

```json
{
  "results": [
    {
      "position":       [45.0, 0.0, 22.0],
      "surface_height": 3.42,
      "normal":         [0.05, 0.98, -0.12],
      "slope_degrees":  8.1,
      "terrain_name":   "Terrain_Tile_03",
      "layer_weights": [
        { "layer_name": "Grass",       "weight": 0.72 },
        { "layer_name": "DirtPath",    "weight": 0.21 },
        { "layer_name": "RockyGround", "weight": 0.07 }
      ],
      "error": null
    },
    {
      "position":       [60.0, 0.0, 30.0],
      "surface_height": null,
      "normal":         null,
      "slope_degrees":  null,
      "terrain_name":   null,
      "layer_weights":  null,
      "error":          "NO_TERRAIN"
    }
  ]
}
```

---

**`bake_navmesh` — request: bake all surfaces**

```json
{
  "all": true
}
```

**`bake_navmesh` — request: bake specific surface**

```json
{
  "surface_paths": ["/World/NavMesh/MainSurface"],
  "all": false
}
```

**`bake_navmesh` — request: bake by agent type**

```json
{
  "agent_type_name": "Humanoid",
  "all": false
}
```

**`bake_navmesh` — response**

```json
{
  "baked_count": 1,
  "surfaces": [
    {
      "object_path": "/World/NavMesh/MainSurface",
      "agent_type":  "Humanoid",
      "elapsed_ms":  512
    }
  ],
  "warnings": []
}
```

---

**`set_navmesh_area` — request: modifier on objects**

```json
{
  "object_paths":  ["/Village/Props/WaterPlane", "/Village/Props/WaterShore"],
  "area_name":     "Water",
  "override_area": true
}
```

**`set_navmesh_area` — request: volumetric override**

```json
{
  "area_name": "Not Walkable",
  "volume": {
    "position":    [45.0, 1.0, 22.0],
    "size":        [10.0, 4.0, 10.0],
    "name":        "Restricted_CombatArena_A",
    "parent_path": "/World/NavVolumes"
  }
}
```

**`set_navmesh_area` — response**

```json
{
  "updated_count":    2,
  "applied": [
    { "path": "/Village/Props/WaterPlane", "mode": "modifier", "area": "Water", "area_index": 3 },
    { "path": "/Village/Props/WaterShore", "mode": "modifier", "area": "Water", "area_index": 3 }
  ],
  "volumes_created": [],
  "warnings":        []
}
```

---

**`GET /scene/scenes` — response**

```json
{
  "scenes": [
    { "name": "SampleScene", "path": "Assets/Scenes/SampleScene.unity", "is_active": true,  "is_dirty": false },
    { "name": "Village_NE",  "path": "Assets/Scenes/Village_NE.unity",  "is_active": false, "is_dirty": true  }
  ]
}
```

---

**`manage_scene` — request: load**

```json
{
  "operation":  "load",
  "scene_path": "Assets/Scenes/Village_NW.unity",
  "set_active": false
}
```

**`manage_scene` — request: unload**

```json
{
  "operation":  "unload",
  "scene_path": "Assets/Scenes/Village_NW.unity"
}
```

**`manage_scene` — request: move objects**

```json
{
  "operation":         "move_objects",
  "object_paths":      ["/Village_NW/Prop_Barrel_07", "/Village_NW/Lamp_Post_03"],
  "target_scene_name": "Village_NE"
}
```

**`manage_scene` — request: create**

```json
{
  "operation":         "create",
  "scene_path":        "Assets/Scenes/Village_SE.unity",
  "set_active":        false,
  "save_after_create": true
}
```

---

### 3.3 Python Tool Catalogue

All tools live in `mcp-server/src/unity_mcp/tools/open_world.py` as module-level async functions registered in `server.py` with `mcp.tool()`.

| Python function | Route | Summary |
|---|---|---|
| `query_terrain(positions, include_layer_weights)` | `POST /tools/world/query_terrain` | Sample height, normal, slope, layer weights for N positions |
| `bake_navmesh(surface_paths, all, agent_type_name)` | `POST /tools/world/bake_navmesh` | Bake NavMeshSurface components |
| `set_navmesh_area(object_paths, area_name, override_area, volume)` | `POST /tools/world/set_navmesh_area` | Assign NavMesh area type or create modifier volume |
| `get_loaded_scenes()` | `GET /scene/scenes` | List all currently loaded scenes |
| `manage_scene(operation, scene_path, set_active, object_paths, target_scene_name, save_after_create)` | `POST /tools/scene/manage` | Load, unload, move objects, or create scenes |

---

## 4. Compound Workflow Patterns

These patterns show how Group 3 tools combine with existing tools in real open-world workflows.

### Pattern A: Terrain-aware prop placement

After `dress_area` proposes a set of placements, validate and refine them using terrain data before executing:

```
1. dress_area(center=[100,0,50], radius=15,
              intent="rocky hillside with boulders and scraggly pines")
   → proposal "prop_dress_b7c1" with 18 proposed placements

2. query_terrain(positions=[...all 18 proposed positions...])
   → surface heights, normals, layer weights for each

3. [Agent reviews: positions with slope_degrees > 35 or layer "Water" > 0.3 → reject]
   → 3 positions rejected, 15 approved

4. execute_proposal("prop_dress_b7c1", confirmed_only=[...15 paths...])
   → prefabs placed at validated positions

5. snap_to_surface(object_paths=[...15 placed objects...], align_to_normal=true)
   → rocks and trees sit flush on the actual terrain slope

6. bake_navmesh(all=true)
   → NavMesh updated to account for the new obstacle geometry
```

### Pattern B: Marking a water area after placing it

```
1. place_prefab("Assets/Prefabs/World/WaterPlane_Large.prefab",
                position=[80, 0.2, 60], scale=[3,1,3])
   → "/World/WaterPlane_01" placed

2. set_navmesh_area(object_paths=["/World/WaterPlane_01"],
                    area_name="Water")
   → NavMeshModifier added with "Water" area type

3. set_object_metadata(object_paths=["/World/WaterPlane_01"],
                       tag="Water", layer="Water",
                       static_flags=["LightmapStatic"])

4. bake_navmesh(all=true)
   → NavMesh now routes around or penalises the water tile
```

### Pattern C: Switching terrain tiles while editing

You are dressing an area that straddles two terrain tiles, `Village_NE` and `Village_NW`. Only `Village_NE` is currently loaded.

```
1. get_loaded_scenes()
   → ["SampleScene", "Village_NE"]

2. manage_scene(operation="load", scene_path="Assets/Scenes/Village_NW.unity")
   → Village_NW loaded additively

3. find_objects(near_position=[0,0,0], near_radius=30, name_pattern="Barrel*")
   → finds 3 barrels that were accidentally placed in Village_NW
     but should be in Village_NE

4. manage_scene(operation="move_objects",
                object_paths=[...3 barrel paths...],
                target_scene_name="Village_NE")
   → barrels moved to correct scene

5. manage_scene(operation="unload",
                scene_path="Assets/Scenes/Village_NW.unity")
   → Village_NW unloaded, Village_NE saved dirty
```

### Pattern D: Setting up a no-go zone for a quest area

A design document says: *"The tribunal courtyard is sealed off until the player completes the Inquisitor quest. No enemies or NPCs should enter or leave during the sealed phase."*

```
1. find_objects(name_pattern="TribunalCourtyard*", near_position=[0,0,0], near_radius=200)
   → get the courtyard bounds from its existing trigger or collider

2. set_navmesh_area(
     area_name="Not Walkable",
     volume={
       position: [courtyard_center],
       size:     [courtyard_width, 4.0, courtyard_depth],
       name:     "NavBlock_TribunalCourtyard_Sealed",
       parent_path: "/World/NavVolumes"
     }
   )
   → NavMeshModifierVolume created at courtyard bounds

3. bake_navmesh(all=true)
   → NavMesh now has a hole over the courtyard

4. create_trigger_volume(
     name="QuestTrigger_TribunalUnlock",
     shape="box", position=[courtyard_entrance],
     size=[4, 3, 2],
     tag="QuestTrigger",
     script_component="QuestTrigger",
     script_properties=[
       {propertyPath:"questId",     value:"inquisitor_complete"},
       {propertyPath:"triggerOnce", value:true}
     ]
   )
   → trigger that will unseal the area at runtime
     (runtime logic to activate/deactivate the NavMeshModifierVolume
      and trigger a rebake is the developer's responsibility)
```

---

## 5. Implementation Notes

### C# handler structure

Three new handler classes live in `UnityMcpBridge.Editor.Tools`:

- `TerrainQueryHandler.cs` — handles `POST /tools/world/query_terrain`
- `NavMeshHandler.cs` — handles `POST /tools/world/bake_navmesh` and `POST /tools/world/set_navmesh_area`
- `SceneManagementHandler.cs` — handles `GET /scene/scenes` and `POST /tools/scene/manage`

All three follow the existing pattern: `public static class`, methods called via `MainThreadDispatcher.EnqueueAsync`.

### Assembly references

The bridge `.asmdef` requires one new reference:

```json
"references": [
  "...",
  "Unity.AI.Navigation"
]
```

`Unity.AI.Navigation` is the assembly name for the `com.unity.ai.navigation` package. If the package is not installed, the handler should return `McpResponse.Fail("PACKAGE_NOT_INSTALLED", "com.unity.ai.navigation is required...")` rather than failing at compile time. Use `versionDefines` in the `.asmdef` to conditionally compile:

```json
"versionDefines": [
  {
    "name":       "com.unity.ai.navigation",
    "expression": "[1.0,)",
    "define":     "AI_NAVIGATION"
  }
]
```

Inside handler code:

```csharp
#if AI_NAVIGATION
    // NavMeshSurface, NavMeshModifier, NavMeshModifierVolume calls
#else
    return McpResponse.Fail("PACKAGE_NOT_INSTALLED",
        "com.unity.ai.navigation is required for NavMesh tools.");
#endif
```

### `query_terrain` — finding the right tile

Open-world terrain is often divided into tiles. The handler must find which `Terrain` object covers a given world position:

```csharp
private static Terrain FindTerrainAt(Vector3 worldPos)
{
    foreach (var terrain in Terrain.activeTerrains)
    {
        var origin = terrain.transform.position;
        var size   = terrain.terrainData.size;
        if (worldPos.x >= origin.x && worldPos.x <= origin.x + size.x &&
            worldPos.z >= origin.z && worldPos.z <= origin.z + size.z)
            return terrain;
    }
    return null;
}
```

Converting world position to terrain-normalised UV (required by alphamap and normal sampling):

```csharp
var u = (worldPos.x - terrain.transform.position.x) / terrain.terrainData.size.x;
var v = (worldPos.z - terrain.transform.position.z) / terrain.terrainData.size.z;
```

Alphamap sampling uses pixel coordinates rather than normalised UV:

```csharp
var alphaX = Mathf.Clamp((int)(u * data.alphamapWidth),  0, data.alphamapWidth  - 1);
var alphaZ = Mathf.Clamp((int)(v * data.alphamapHeight), 0, data.alphamapHeight - 1);
var weights = data.GetAlphamaps(alphaX, alphaZ, 1, 1);
// weights[z, x, layerIndex] — note z-first indexing
```

### `bake_navmesh` — finding surfaces

```csharp
// All surfaces in scene
var allSurfaces = Object.FindObjectsOfType<NavMeshSurface>();

// Surfaces matching agent type
var named = allSurfaces.Where(s =>
    NavMesh.GetSettingsNameFromID(s.agentTypeID) == agentTypeName);

// Surface on a specific object
var go = GameObject.Find(hierarchyPath);
var surface = go?.GetComponent<NavMeshSurface>();
```

`NavMeshSurface.BuildNavMesh()` is synchronous. For very large terrains it may take several seconds — this is expected and the handler should not time out. The elapsed time returned in the response allows the Python layer to log meaningful progress messages.

Mark the scene dirty after baking:

```csharp
EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
```

### `set_navmesh_area` — area name resolution

```csharp
var areaIndex = GameObjectUtility.GetNavMeshAreaFromName(areaName);
if (areaIndex == -1)
{
    // Area not found — return available names in error
    var available = string.Join(", ", GameObjectUtility.GetNavMeshAreaNames());
    return McpResponse.Fail("AREA_NOT_FOUND",
        $"Area '{areaName}' not found. Available: {available}");
}
```

Applying a `NavMeshModifier`:

```csharp
Undo.RecordObject(go, "Set NavMesh Area");
var modifier = Undo.AddComponent<NavMeshModifier>(go);
modifier.overrideArea = overrideArea;
modifier.area = areaIndex;
EditorUtility.SetDirty(go);
```

If a `NavMeshModifier` already exists on the object, update it rather than adding a duplicate.

### `manage_scene` — cross-scene object moves

Moving an object that has a parent in a different scene will throw unless the parent relationship is severed first. The handler checks each object's parent:

```csharp
// Before moving, detach from parents that aren't in the move set
foreach (var path in objectPaths)
{
    var go = FindObject(path);
    if (go.transform.parent != null)
    {
        var parentInMoveSet = objectPaths.Contains(GetHierarchyPath(go.transform.parent.gameObject));
        if (!parentInMoveSet)
        {
            Undo.SetTransformParent(go.transform, null, "Detach for scene move");
            warnings.Add($"'{path}' was detached from parent '{GetHierarchyPath(go.transform.parent.gameObject)}'");
        }
    }
}
```

Then move:

```csharp
SceneManager.MoveGameObjectToScene(go, targetScene);
```

`SceneManager.MoveGameObjectToScene` is available in editor context and is Undo-friendly when preceded by `Undo.RecordObject`.

Refusing to unload the last scene:

```csharp
if (EditorSceneManager.sceneCount == 1)
    return McpResponse.Fail("CANNOT_UNLOAD_LAST",
        "Cannot unload the only loaded scene.");
```

---

## 6. Development Roadmap

A single phase is appropriate here — the three tool families are independent of each other and can ship together.

### Phase 1 — All Group 3 tools

**Goal:** the agent can read terrain data to validate placements, trigger NavMesh rebakes, configure area types, and manage the multi-scene editor setup — all within a single conversation turn.

**C#:**
- [ ] `TerrainQueryHandler.cs` — `POST /tools/world/query_terrain`
  - Find terrain tile covering each position
  - Sample height, interpolated normal, slope, alpha weights
  - Graceful `NO_TERRAIN` per-position rather than whole-request failure
  - Bulk-optimised: all positions sampled in one main-thread dispatch
- [ ] `NavMeshHandler.cs` — `POST /tools/world/bake_navmesh`
  - `AI_NAVIGATION` conditional compilation guard
  - Find surfaces by all / object path / agent type name
  - Return elapsed time per surface
  - `MarkSceneDirty` after each bake
- [ ] `NavMeshHandler.cs` — `POST /tools/world/set_navmesh_area`
  - Area name → index via `GetNavMeshAreaFromName`; return available names on failure
  - Add or update `NavMeshModifier` on existing objects (no duplicates)
  - Create `NavMeshModifierVolume` for the `volume` sub-object path
  - Undo registration for all changes
- [ ] `SceneManagementHandler.cs` — `GET /scene/scenes`
  - List all loaded scenes with name, path, active flag, dirty flag
- [ ] `SceneManagementHandler.cs` — `POST /tools/scene/manage`
  - `load`: `EditorSceneManager.OpenScene` with `OpenSceneMode.Additive`
  - `unload`: guard against removing last scene; `EditorSceneManager.CloseScene`
  - `move_objects`: parent-detach safety check; `SceneManager.MoveGameObjectToScene`; Undo
  - `create`: `EditorSceneManager.NewScene` + optional `SaveScene`
- [ ] `McpRouter.cs` — register all five new routes
- [ ] `UnityMcpBridge.Editor.asmdef` — add `Unity.AI.Navigation` reference and `versionDefines`
- [ ] `Models.cs` — add request/response DTOs for all three handlers

**Python:**
- [ ] `open_world.py` — five module-level async functions
- [ ] `server.py` — register all five tools with `mcp.tool()`

**Acceptance criteria:**
- Agent queries terrain at 10 positions across two terrain tiles, correctly identifies which positions have slope > 30°, and rejects those from a placement proposal
- Agent bakes all NavMesh surfaces in a scene with three `NavMeshSurface` components and reports per-surface elapsed times
- Agent assigns "Water" area type to a water plane and creates a "Not Walkable" modifier volume over a blocked courtyard, then rebakes
- Agent lists loaded scenes, loads an adjacent tile additively, moves two misplaced objects into the correct scene, then unloads the tile — all from a single conversation turn

---

*Document version 1.0 — scope: Open World tool group only (Group 3). Foundation tools (Group 1), Dressing Workflow (Group 2), Cutscene System, Scaffold System, and PCG integration are covered in separate documents.*
