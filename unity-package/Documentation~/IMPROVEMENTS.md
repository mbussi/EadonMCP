# System Improvements — Architecture Document

## Table of Contents

1. [Context](#1-context)
2. [Group 6 — Metadata Authoring](#2-group-6--metadata-authoring)
   - 2.1 [create_prefab_metadata](#21-create_prefab_metadata)
   - 2.2 [update_prefab_metadata](#22-update_prefab_metadata)
   - 2.3 [update_scene_context](#23-update_scene_context)
   - 2.4 [Zone Contexts](#24-zone-contexts)
3. [Group 7 — Write Operations](#3-group-7--write-operations)
   - 3.1 [paint_terrain_layer](#31-paint_terrain_layer)
   - 3.2 [bake_lighting](#32-bake_lighting)
4. [Group 8 — Level Design Operations](#4-group-8--level-design-operations)
   - 4.1 [replace_prefab](#41-replace_prefab)
   - 4.2 [create_reflection_probe](#42-create_reflection_probe)
5. [Group 9 — Patrol Routes](#5-group-9--patrol-routes)
   - 5.1 [create_patrol_route](#51-create_patrol_route)
   - 5.2 [get_patrol_routes](#52-get_patrol_routes)
6. [Group 10 — Visual Feedback](#6-group-10--visual-feedback)
   - 6.1 [capture_scene_view](#61-capture_scene_view)
7. [Additive: Validate Scene Extensions](#7-additive-validate-scene-extensions)
8. [MCP Integration](#8-mcp-integration)
   - 8.1 [Route Map](#81-route-map)
   - 8.2 [Request and Response Shapes](#82-request-and-response-shapes)
   - 8.3 [Python Tool Catalogue](#83-python-tool-catalogue)
9. [Compound Workflow Patterns](#9-compound-workflow-patterns)
10. [Implementation Notes](#10-implementation-notes)
11. [Development Roadmap](#11-development-roadmap)

---

## 1. Context

The existing bridge covers placement, lighting, proposals, procedural generation, scaffold, cutscenes, dressing, open-world tools, and production quality. Taken together it gives the agent substantial ability to act on the scene. What it cannot do is close certain loops that are structurally necessary for those actions to produce good results consistently.

**The metadata loop is open.** `dress_area`, `resolve_scaffold_markers`, and `get_prefab_catalog` all produce better results when `PrefabMetadata` and `SceneContext` assets are richly authored. But those assets can only be created and edited in the Unity Inspector. Every prefab added to the project without metadata is a prefab the agent cannot reason about effectively.

**Several read tools have no write counterpart.** `query_terrain` reads surface data but nothing paints terrain layers. `bake_navmesh` has a lighting equivalent missing from the toolset.

**Common level design operations have no coverage.** Replacing all instances of one prefab with another is a standard production task that currently requires a manual find-delete-replace sequence. Reflection probes — placed by the dozen in any interior-heavy level — have no dedicated tool.

**Movement patterns are invisible to the scaffold system.** The scaffold system handles spawn points, loot, and generic markers well, but patrol routes — a first-class concept in any open-world level design document — have no representation.

**The agent cannot see the scene.** All feedback is text-based. The designer must switch to the Editor viewport to verify every spatial decision Claude makes.

These additions are designed to close those gaps specifically. They build on existing systems rather than introducing new architectural patterns.

---

## 2. Group 6 — Metadata Authoring

The metadata authoring tools give Claude write access to the assets that the entire system's quality depends on. They should be thought of less as individual tools and more as a single loop-closer: Claude can now author the inputs that feed back into its own placement decisions.

### 2.1 `create_prefab_metadata`

**What it does.** Creates a `PrefabMetadata` ScriptableObject asset alongside a prefab, following the convention `Prefab_Name.metadata.asset` in the same directory. Immediately registers the new asset in `MetadataRegistry` so all other tools see it without requiring an Editor restart.

**Why it matters.** Without this, every new prefab requires manual Inspector work before the agent can reason about it. With it, the end of a prefab import session can include "author metadata for these new assets" as a natural part of the conversation.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `prefab_path` | string | AssetDatabase path of the prefab to describe |
| `display_name` | string? | Human-readable name. Defaults to the prefab filename |
| `category` | string? | Broad category: `building`, `prop`, `vegetation`, `light-fixture`, `terrain`, etc. |
| `tags` | string[]? | Searchable tags: `urban`, `medieval`, `ruined`, `indoor`, `stone`, etc. |
| `description` | string? | Plain-English description for the AI — appearance, scale, ideal usage |
| `compatible_with` | string[]? | Tags of assets this pairs well with |
| `incompatible_with` | string[]? | Tags this asset clashes with |
| `requires_flat_ground` | bool? | Placement rule (default true) |
| `can_stack` | bool? | Multiple instances can be stacked (default false) |
| `indoor_only` | bool? | Only valid in enclosed spaces (default false) |
| `outdoor_only` | bool? | Only valid outdoors (default false) |
| `minimum_spacing` | float? | Minimum distance in metres between instances |
| `approximate_size` | float[3]? | Bounding box `[w, h, d]` in world units |
| `overwrite` | bool | Replace existing metadata if present (default false) |

**Response:**

```json
{
  "prefab_path":    "Assets/Prefabs/City/Townhouse_A.prefab",
  "metadata_path":  "Assets/Prefabs/City/Townhouse_A.metadata.asset",
  "created":        true,
  "overwritten":    false
}
```

If metadata already exists and `overwrite` is false, the response fails with `METADATA_EXISTS`, listing the path of the existing asset.

---

### 2.2 `update_prefab_metadata`

**What it does.** Updates fields on an existing `PrefabMetadata` asset. Only fields explicitly provided are changed — omitted fields are left unchanged. This is the correct tool when you want to add tags to an existing asset or correct a description without recreating the asset from scratch.

**Parameters:**

Same set as `create_prefab_metadata` except `prefab_path` is used to locate the existing asset, `overwrite` is not applicable, and every other field is nullable (null = leave unchanged).

**Response:**

```json
{
  "prefab_path":   "Assets/Prefabs/City/Townhouse_A.prefab",
  "metadata_path": "Assets/Prefabs/City/Townhouse_A.metadata.asset",
  "updated_fields": ["tags", "description", "compatible_with"]
}
```

---

### 2.3 `update_scene_context`

**What it does.** Writes to the active `SceneContext` asset — the one assigned in `McpBridgeSettings`, or the auto-discovered one if no explicit assignment exists. Only provided fields are updated. Useful for expanding the design notes during a session, adjusting active tags after a biome change, or correcting the setting description.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `level_name` | string? | Name of the level |
| `setting` | string? | Broad environment type: `city`, `dungeon`, `forest`, etc. |
| `architectural_style` | string? | Visual style: `gothic`, `brutalist`, `cyberpunk`, etc. |
| `era` | string? | Historical or fictional era: `medieval`, `victorian`, etc. |
| `active_tags` | string[]? | Replace the active tags list entirely |
| `add_active_tags` | string[]? | Append tags to the existing active tags list |
| `remove_active_tags` | string[]? | Remove specific tags from the active tags list |
| `excluded_tags` | string[]? | Replace the excluded tags list entirely |
| `notes` | string? | Replace the design notes entirely |
| `append_notes` | string? | Append text to the existing design notes (adds a newline separator) |

**Design note.** `add_active_tags` / `remove_active_tags` are more useful during iterative sessions than replacing the whole list, since you typically want to refine rather than restart the tag set. Similarly, `append_notes` lets Claude add session notes to a running design document without erasing earlier entries.

**Response:**

```json
{
  "context_path":   "Assets/SceneContexts/Village_NE.asset",
  "updated_fields": ["active_tags", "append_notes"]
}
```

---

### 2.4 Zone Contexts

**What it is.** A `McpBridgeSettings` enhancement and a new route that extends the existing context resolution to support per-zone overrides.

**The problem.** A single `SceneContext` per scene works well when all areas share a consistent identity. But in a streaming open-world tile that contains multiple biomes — a forest edge alongside a ruined village, for example — the agent currently gets one set of preferred tags regardless of which zone it's dressing. This means either the tags are too broad (reducing the signal quality for both areas) or the designer must manually switch the active context by editing the settings asset.

**The change.** `McpBridgeSettings` gains a `List<ZoneContextEntry>` field where each entry maps a zone name to a dedicated `SceneContext`:

```csharp
[Tooltip("Per-zone SceneContext overrides. Key must match a ScaffoldZone name.")]
public List<ZoneContextEntry> zoneContexts = new List<ZoneContextEntry>();
```

`get_zone_context(zone_name)` looks up the zone entry first. If found, it returns that context. If not, it falls back to the global `sceneContext`. `get_scene_context()` is unchanged.

The existing `GET /scene/context` route gains an optional `?zone_name=` query parameter that triggers the zone-aware resolution path.

**No new Python tool is required.** `get_zone_context` is exposed as a thin wrapper:

```python
async def get_zone_context(zone_name: str) -> str:
    data = await client.get(f"/scene/context?zone_name={zone_name}")
    return json.dumps(data)
```

**Note.** Creating and assigning the zone context entries themselves still requires the Unity Inspector — this change only enables the resolution logic. Creating `SceneContext` assets for zones can be done via `update_scene_context` once they exist, or via a follow-on `create_scene_context` tool if this proves to be a friction point.

---

## 3. Group 7 — Write Operations

Both tools in this group are the natural write-side counterparts to existing read-only tools. `query_terrain` can read terrain data; `paint_terrain_layer` writes it. `bake_navmesh` triggers a navmesh bake; `bake_lighting` triggers a lighting bake. Neither requires a new architectural pattern.

### 3.1 `paint_terrain_layer`

**What it does.** Paints one or more terrain texture layers at a world position with a circular brush. Supports additive and set blend modes and three falloff curves. Correctly normalises all layer weights after painting so the alphamap sums to 1.0 at every pixel.

**Why it matters.** Terrain painting is one of the most time-consuming parts of open-world dressing and one of the most pattern-driven. After `dress_area` places a dirt path with gravel and weeds, painting the `DirtPath` layer under the path and blending it into the `Grass` layer at the edges is predictable, manual work that should be delegatable.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `position` | float[3] | World-space centre of the paint brush |
| `radius` | float | Brush radius in world metres |
| `layer_name` | string | Terrain layer name to paint (must match a layer on the active terrain tile) |
| `opacity` | float | Brush strength 0.0–1.0 |
| `falloff` | string | `"linear"` (even fade), `"smooth"` (S-curve), `"constant"` (hard edge). Default `"smooth"` |
| `blend_mode` | string | `"add"` (increase layer toward opacity), `"set"` (force layer to opacity). Default `"add"` |
| `terrain_name` | string? | If provided, restricts painting to the named terrain tile. Otherwise uses the tile covering `position` |

**Response:**

```json
{
  "terrain_name":   "Terrain_Tile_03",
  "layer_painted":  "DirtPath",
  "layer_index":    2,
  "pixels_affected": 312,
  "warnings":       []
}
```

`layer_name` is matched case-insensitively. An unknown layer name returns a warning listing all available layer names rather than failing, so the agent can correct the call.

**Design note.** A common compound call is `paint_terrain_layer` immediately after `dress_area` is executed — the dress_area positions are known from the proposal, so the agent can compute a brush position and radius from the same intent. This makes the terrain visually consistent with the props placed on it in a single pass.

---

### 3.2 `bake_lighting`

**What it does.** Triggers the progressive lightmapper or clears baked lighting data. Because a full bake on a large scene can take minutes, the tool uses `Lightmapping.BakeAsync()` and returns immediately with a "bake started" status. The designer monitors progress in Unity's Lighting window. A synchronous mode is available for small or partial bakes where waiting is acceptable.

**Why it matters.** `bake_navmesh` already exists for the same reason — layout changes invalidate baked data and the agent should be able to trigger a rebake at the end of a session. The lighting equivalent has been missing.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `mode` | string | `"async"` (trigger and return immediately), `"sync"` (block until complete — use only for small scenes), `"clear"` (delete all baked data). Default `"async"` |
| `selected_only` | bool | If true, bakes only the currently selected objects. Requires objects to be selected in the Editor before calling. Default false |
| `bake_reflection_probes` | bool | Also trigger reflection probe captures after baking. Default false |

**Response (async):**

```json
{
  "mode":            "async",
  "bake_started":    true,
  "message":         "Lighting bake started. Monitor progress in Window > Rendering > Lighting."
}
```

**Response (sync):**

```json
{
  "mode":        "sync",
  "completed":   true,
  "elapsed_ms":  4210
}
```

**Response (clear):**

```json
{
  "mode":    "clear",
  "cleared": true
}
```

**Failure case.** If a bake is already in progress, the tool returns `BAKE_IN_PROGRESS` rather than queueing a second one. The agent should check and wait (or ask the designer to wait) before retrying.

---

## 4. Group 8 — Level Design Operations

### 4.1 `replace_prefab`

**What it does.** Finds every instance of one prefab in the scene (or a scoped subtree) and replaces each with an instance of a different prefab, preserving the original's position, rotation, scale, parent, and name. All replacements are grouped under a single named Undo entry.

**Why it matters.** Placeholder-to-final asset swaps happen constantly in production. Currently they require `find_objects` + manual batch delete + batch re-place, which loses hierarchy relationships and doesn't scale. This is a first-class operation that should be one call.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `old_prefab_path` | string | AssetDatabase path of the prefab being replaced |
| `new_prefab_path` | string | AssetDatabase path of the replacement prefab |
| `scope_path` | string? | Hierarchy path to restrict the search. Null = whole active scene |
| `preserve_transform` | bool | Keep original position, rotation, and scale (default true) |
| `preserve_name` | bool | Keep the original instance's GameObject name (default true) |
| `preserve_parent` | bool | Keep the original instance's parent (default true) |
| `dry_run` | bool | Return what would be replaced without modifying the scene (default false) |

**Response:**

```json
{
  "replaced_count": 7,
  "dry_run":        false,
  "replaced": [
    {
      "old_path": "/World/Village/Buildings/Townhouse_Placeholder_01",
      "new_path": "/World/Village/Buildings/Townhouse_Placeholder_01"
    }
  ],
  "warnings": []
}
```

The `new_path` typically matches `old_path` because names are preserved by default — the prefab asset changes but the scene hierarchy path stays the same.

**Implementation note.** Instance detection uses `PrefabUtility.GetCorrespondingObjectFromSource(go)` to find which prefab asset an instance was created from, then compares with the loaded `old_prefab_path` asset. Only direct instances are matched — if `old_prefab_path` is a variant and the scene contains instances of the base prefab, those are not replaced.

---

### 4.2 `create_reflection_probe`

**What it does.** Creates a `ReflectionProbe` component at a world position with a configurable influence volume. Supports baked, realtime, and custom modes. In open-world levels, reflection probes are placed by the dozen — one per interior room, one per covered archway, one per large puddle or metallic surface — and the setup is identical every time.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `position` | float[3] | World-space centre of the probe and its influence volume |
| `size` | float[3]? | Influence box extents `[w, h, d]`. Defaults to `[10, 5, 10]` |
| `mode` | string | `"baked"`, `"realtime"`, or `"custom"`. Default `"baked"` |
| `importance` | int | Higher values override lower when volumes overlap. Default 1 |
| `intensity` | float | Reflection intensity multiplier 0–1. Default 1.0 |
| `hdr` | bool | Use HDR capture. Default true |
| `resolution` | int | Cubemap capture resolution: 16, 32, 64, 128, 256, 512, 1024. Default 128 |
| `shadow_distance` | float? | Override shadow distance for this probe's captures. Null = use scene default |
| `parent_path` | string? | Hierarchy parent path |
| `name` | string? | GameObject name. Defaults to `"ReflectionProbe"` |

**Response:**

```json
{
  "object_path": "/World/Village/Tavern/ReflectionProbe",
  "mode":        "baked",
  "importance":  1,
  "size":        [6.0, 3.5, 8.0],
  "warnings":    []
}
```

**Design note.** After placing a set of reflection probes in an area, call `bake_lighting(bake_reflection_probes=true)` to capture them in one pass.

---

## 5. Group 9 — Patrol Routes

### 5.1 `create_patrol_route`

**What it does.** Creates a named patrol route as a scene hierarchy structure: a container GameObject with child waypoint GameObjects at the specified positions. Each waypoint carries optional per-stop metadata (idle time, look direction) via a lightweight `PatrolWaypoint` component defined in the bridge's editor assembly. The route is associated with a scaffold zone when one is specified, making it part of the zone's design data and visible alongside spawn markers.

**Why it matters.** A patrol route is not a prop and not a trigger — it is navigation intent, and it belongs in the same level design pass as spawn markers and loot positions. The scaffold system handles those well but has no representation for movement patterns. A guard's patrol path, a merchant's daily route, an enemy formation's sweep path — these are all first-class level design decisions that should be expressible in the same conversation as the scaffolding that surrounds them.

**Architecture.** The tool creates scene objects only. It does not generate nav paths, does not validate that waypoints are on the NavMesh, and does not attach any AI agent components. Those connections are the game developer's responsibility. The bridge creates a clean, named route structure that the AI system can discover at runtime using a consistent naming convention.

The created structure:

```
PatrolRoute_{name}                  ← container, PatrolRouteData component
  ├── WP_00                         ← waypoint, PatrolWaypoint component
  ├── WP_01
  ├── ...
  └── WP_NN
```

`PatrolRouteData` (defined in `UnityMcpBridge.Editor`, stripped at build time via `[ExecuteAlways]`/`[HideInInspector]` convention — the game code should define its own equivalent):

```csharp
public class PatrolRouteData : MonoBehaviour
{
    public bool  loop          = true;
    public float defaultIdleTime = 1f;
    public string zonePath;       // back-reference to the owning ScaffoldZone
}

public class PatrolWaypoint : MonoBehaviour
{
    public int   index;
    public float idleTime    = -1f;   // -1 = use route default
    public bool  hasLookDirection;
    public Vector3 lookDirection;
}
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `name` | string | Route name, used as the container's GameObject name suffix |
| `waypoints` | list[WaypointDef] | Ordered list of waypoint definitions |
| `loop` | bool | Whether the route wraps from last waypoint back to first. Default true |
| `default_idle_time` | float | Seconds to idle at each waypoint that doesn't specify its own. Default 1.0 |
| `zone_path` | string? | ScaffoldZone hierarchy path. If provided, the route is parented under the zone |
| `parent_path` | string? | Explicit parent path (used when `zone_path` is not provided) |

**`WaypointDef` sub-object:**

| Field | Type | Description |
|---|---|---|
| `position` | float[3] | World-space position |
| `idle_time` | float? | Override idle time at this stop. Null = use route default |
| `look_direction` | float[3]? | Euler angles for the character to face while idling at this waypoint. Null = face path direction |
| `label` | string? | Optional human-readable label, e.g. `"east_gate_post"` — becomes the waypoint's display name |

**Response:**

```json
{
  "route_path":     "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop",
  "waypoint_count": 4,
  "loop":           true,
  "zone_path":      "/Dungeon/Zone1_Descent",
  "waypoints": [
    { "path": "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop/WP_00", "label": "entry_post" },
    { "path": "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop/WP_01", "label": null },
    { "path": "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop/WP_02", "label": "far_post" },
    { "path": "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop/WP_03", "label": null }
  ]
}
```

---

### 5.2 `get_patrol_routes`

**What it does.** Returns all patrol routes currently in the active scene, with their waypoint positions and associated zone paths. The write-side complement for reviewing what routes exist before creating new ones.

**Response:**

```json
{
  "routes": [
    {
      "route_path":     "/Dungeon/Zone1_Descent/PatrolRoute_GuardLoop",
      "name":           "GuardLoop",
      "zone_path":      "/Dungeon/Zone1_Descent",
      "loop":           true,
      "waypoint_count": 4,
      "waypoints": [
        { "path": "...", "position": [4.0, 0.0, 12.0], "idle_time": 2.0, "label": "entry_post" }
      ]
    }
  ]
}
```

---

## 6. Group 10 — Visual Feedback

### 6.1 `capture_scene_view`

**What it does.** Renders the current Unity Editor Scene View camera to a JPEG image and returns it as base64-encoded data. Claude receives the image in its context, can describe what it sees, identify problems, and suggest specific adjustments — closing the feedback loop that currently requires the designer to switch to the Editor viewport for every spatial decision.

**Why it matters.** Every other tool in the bridge asks the agent to reason about spatial relationships from text alone: paths, positions, and property values. This is a fundamental constraint that affects the quality of dressing decisions, lighting placement, and zone layout reviews. Once the agent can see the result, the workflow shifts from "apply, check manually, correct" to "apply, capture, review together, correct" — with Claude participating in the review step.

**This tool is architecturally different from all others.** The Python tool does not return a string — it returns image data that the MCP protocol delivers to Claude as image content. This is the one case where the Python implementation diverges from the standard `json.dumps(result)` pattern.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `width` | int? | Capture width in pixels. Default 1280 |
| `height` | int? | Capture height in pixels. Default 720 |
| `quality` | int? | JPEG quality 1–100. Default 85 |
| `focus_path` | string? | If provided, moves the Scene View camera to frame this object before capture |
| `focus_distance` | float? | Distance from object when using `focus_path`. Default: auto-sized from object bounds |

**C# response:**

```json
{
  "image_base64": "...",
  "format":       "jpeg",
  "width":        1280,
  "height":       720,
  "focused_on":   "/World/Village/Market"
}
```

**Python tool return type.** Unlike all other tools, `capture_scene_view` returns an `Image` object from the MCP types library, not a JSON string. This is how FastMCP delivers visual content to Claude:

```python
from mcp.types import Image

async def capture_scene_view(...) -> Image:
    data = await client.get("/tools/debug/capture_scene_view?...")
    return Image(data=data["image_base64"], mimeType="image/jpeg")
```

**C# implementation.** The capture uses the `SceneView` camera and reads pixels into a `Texture2D`:

```csharp
var sceneView = SceneView.lastActiveSceneView;
if (sceneView == null)
    return McpResponse.Fail("NO_SCENE_VIEW", "No active Scene View window found.");

if (focusPath != null)
    FrameObject(sceneView, focusPath);

var rt      = new RenderTexture(width, height, 24);
var prevRT  = sceneView.camera.targetTexture;

sceneView.camera.targetTexture = rt;
sceneView.camera.Render();

RenderTexture.active = rt;
var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
tex.Apply();

var bytes  = tex.EncodeToJPG(quality);
var base64 = Convert.ToBase64String(bytes);

// Restore state
sceneView.camera.targetTexture = prevRT;
RenderTexture.active = null;
Object.DestroyImmediate(rt);
Object.DestroyImmediate(tex);
```

**Security note.** The capture renders whatever is currently visible in the Scene View. It does not capture passwords, private files, or anything outside the Unity Editor. It is appropriate to flag this as a "debug" tool in the route path (`/tools/debug/capture_scene_view`) to make its special nature visible in logs.

---

## 7. Additive: Validate Scene Extensions

These are two new check IDs added to the existing `validate_scene` tool. No new route, no new tool — just new entries in `SceneValidatorHandler`'s check registry.

### `missing_lod_groups`

**Severity:** `warning`

**What it finds.** `MeshRenderer` components on objects whose mesh has a vertex count above a configurable threshold but no `LODGroup` anywhere in the parent chain. High-polygon meshes without LOD are a common performance problem in open-world scenes and one that accumulates invisibly over a long build.

**New parameter:** `lod_vertex_threshold` (int, default 2000). Objects with fewer vertices than this are not flagged.

**Implementation:**

```csharp
var mf = go.GetComponent<MeshFilter>();
if (mf?.sharedMesh == null) continue;
if (mf.sharedMesh.vertexCount < req.LodVertexThreshold) continue;
if (go.GetComponentInParent<LODGroup>() != null) continue;

issues.Add(new ValidationIssueData {
    Path   = GetPath(go),
    Detail = $"MeshRenderer with {mf.sharedMesh.vertexCount} verts, no LODGroup in parent chain"
});
```

---

### `overlapping_statics`

**Severity:** `info`

**What it finds.** Static GameObjects whose colliders physically intersect other static GameObjects. Overlapping static geometry causes lightmap UV seaming artifacts, z-fighting, and AO bleeding — problems that are invisible in a busy scene and expensive to track down later.

**Implementation.** For each static object with a non-trigger `Collider`, `Physics.OverlapBox` is called with the collider's bounds slightly reduced (×0.95) to avoid false positives from objects that share an exact edge. Parent/child pairs are excluded. The search is restricted to other static objects.

**Performance note.** This check is expensive on large scenes — `Physics.OverlapBox` is O(n) per static object. The `max_results_per_check` cap applies. This check should be run scoped (`scope_path`) rather than scene-wide.

---

## 8. MCP Integration

### 8.1 Route Map

**New handlers and routes:**

| Route | Handler | Description |
|---|---|---|
| `POST /tools/metadata/create_prefab_metadata` | `MetadataAuthorHandler` | Create new PrefabMetadata asset |
| `POST /tools/metadata/update_prefab_metadata` | `MetadataAuthorHandler` | Update existing PrefabMetadata asset |
| `POST /tools/metadata/update_scene_context` | `MetadataAuthorHandler` | Write to the active SceneContext asset |
| `GET /scene/context?zone_name={name}` | `SceneQueryHandler` (extended) | Zone-aware context resolution |
| `POST /tools/world/paint_terrain_layer` | `TerrainPaintHandler` | Paint a terrain texture layer |
| `POST /tools/world/bake_lighting` | `LightBakeHandler` | Trigger or clear a lighting bake |
| `POST /tools/placement/replace_prefab` | `PlacementHandler` (extended) | Replace all instances of one prefab with another |
| `POST /tools/world/create_reflection_probe` | `ReflectionProbeHandler` | Create a ReflectionProbe component |
| `POST /tools/scaffold/create_patrol_route` | `PatrolRouteHandler` | Create a named patrol route with waypoints |
| `GET /assets/scaffold/patrol_routes` | `PatrolRouteHandler` | List all patrol routes in the scene |
| `GET /tools/debug/capture_scene_view` | `SceneViewCaptureHandler` | Render and return Scene View as JPEG |

**Extended (no route change):**

| Route | Change |
|---|---|
| `POST /tools/scene/validate` | Add `missing_lod_groups`, `overlapping_statics` check IDs; add `lod_vertex_threshold` request parameter |
| `GET /scene/context` | Add optional `zone_name` query parameter |

**New file in `McpBridgeSettings.cs`:**

```csharp
[Serializable]
public class ZoneContextEntry
{
    public string       zoneName;
    public SceneContext context;
}
```

**New `.asmdef` references:** None required. All new handlers use only built-in Unity APIs.

---

### 8.2 Request and Response Shapes

**`create_prefab_metadata` — request**

```json
{
  "prefab_path":      "Assets/Prefabs/City/Townhouse_A.prefab",
  "display_name":     "Narrow Townhouse A",
  "category":         "building",
  "tags":             ["urban", "medieval", "residential", "stone"],
  "description":      "A narrow 3-storey stone townhouse. Suits densely packed city streets. Pairs well with cobblestone roads and lamp posts.",
  "compatible_with":  ["cobblestone", "lamp-post", "urban-foliage"],
  "incompatible_with":["forest", "natural", "wilderness"],
  "requires_flat_ground": true,
  "minimum_spacing":  0.3,
  "approximate_size": [4.0, 9.0, 6.0]
}
```

---

**`update_scene_context` — request**

```json
{
  "add_active_tags":  ["ruined", "overgrown"],
  "remove_active_tags": ["pristine"],
  "append_notes":     "2024-06-08: Added the ruined east wing. Avoid placing intact furniture in Zone3 eastward."
}
```

---

**`paint_terrain_layer` — request**

```json
{
  "position":    [45.0, 0.0, 22.0],
  "radius":      6.0,
  "layer_name":  "DirtPath",
  "opacity":     0.85,
  "falloff":     "smooth",
  "blend_mode":  "add"
}
```

---

**`bake_lighting` — request**

```json
{
  "mode":                    "async",
  "bake_reflection_probes":  true
}
```

---

**`replace_prefab` — request**

```json
{
  "old_prefab_path": "Assets/Prefabs/Placeholders/House_Block.prefab",
  "new_prefab_path": "Assets/Prefabs/Buildings/Townhouse_A.prefab",
  "scope_path":      "/World/Village",
  "dry_run":         false
}
```

---

**`create_reflection_probe` — request**

```json
{
  "position":    [8.0, 1.5, 14.0],
  "size":        [6.0, 3.5, 8.0],
  "mode":        "baked",
  "importance":  2,
  "resolution":  256,
  "parent_path": "/World/Village/Tavern/Lighting",
  "name":        "ReflectionProbe_TavernMain"
}
```

---

**`create_patrol_route` — request**

```json
{
  "name":              "GuardSweep_East",
  "loop":              true,
  "default_idle_time": 1.5,
  "zone_path":         "/Dungeon/Zone2_Antechamber",
  "waypoints": [
    { "position": [4.0, 0.0, 2.0],  "label": "entry_post",   "idle_time": 3.0 },
    { "position": [12.0, 0.0, 2.0], "label": null,            "idle_time": null },
    { "position": [12.0, 0.0, 14.0],"label": "far_corner",   "idle_time": 2.0, "look_direction": [0, 90, 0] },
    { "position": [4.0, 0.0, 14.0], "label": null,            "idle_time": null }
  ]
}
```

---

**`capture_scene_view` — GET parameters**

```
/tools/debug/capture_scene_view?width=1280&height=720&quality=85&focus_path=/World/Village/Market
```

---

### 8.3 Python Tool Catalogue

New tools live in new modules:

| Python function | Module | Route |
|---|---|---|
| `create_prefab_metadata(...)` | `metadata.py` | `POST /tools/metadata/create_prefab_metadata` |
| `update_prefab_metadata(...)` | `metadata.py` | `POST /tools/metadata/update_prefab_metadata` |
| `update_scene_context(...)` | `metadata.py` | `POST /tools/metadata/update_scene_context` |
| `get_zone_context(zone_name)` | `scene.py` (append) | `GET /scene/context?zone_name=` |
| `paint_terrain_layer(...)` | `open_world.py` (append) | `POST /tools/world/paint_terrain_layer` |
| `bake_lighting(...)` | `open_world.py` (append) | `POST /tools/world/bake_lighting` |
| `replace_prefab(...)` | `placement.py` (append) | `POST /tools/placement/replace_prefab` |
| `create_reflection_probe(...)` | `lighting.py` (append) | `POST /tools/world/create_reflection_probe` |
| `create_patrol_route(...)` | `scaffold.py` (append) | `POST /tools/scaffold/create_patrol_route` |
| `get_patrol_routes()` | `scaffold.py` (append) | `GET /assets/scaffold/patrol_routes` |
| `capture_scene_view(...)` | `debug.py` | `GET /tools/debug/capture_scene_view` |

`capture_scene_view` is the only tool that does not return `json.dumps(result)` — it returns `mcp.types.Image`. This requires its own module (`debug.py`) to keep the import and return type visible as a deliberate exception.

---

## 9. Compound Workflow Patterns

### Pattern A: Onboarding a new prefab batch

A new art drop arrives with 12 new building prefabs. Currently this means 12 manual Inspector sessions. With the new tools:

```
1. get_prefab_catalog("Assets/Prefabs/Buildings/NewDrop")
   → lists the 12 prefabs with no metadata

2. [For each prefab in turn]:
   create_prefab_metadata(
     prefab_path="Assets/Prefabs/Buildings/BlockHouse_A.prefab",
     display_name="Block House A",
     category="building",
     tags=["urban", "industrial", "stone", "3-storey"],
     description="...",
     requires_flat_ground=True,
     minimum_spacing=0.5,
     approximate_size=[6.0, 9.0, 5.0]
   )

3. get_prefab_catalog("Assets/Prefabs/Buildings/NewDrop")
   → confirms all 12 now have metadata

4. dress_area(center=[...], radius=30, intent="industrial district with block housing")
   → dress_area now scores the new prefabs correctly
```

---

### Pattern B: Terrain dressing after placement

After executing a `dress_area` proposal for a forest clearing:

```
1. execute_proposal("prop_dress_a3f2")
   → 15 trees and ground cover placed

2. paint_terrain_layer(position=[60,0,40], radius=18,
                       layer_name="Grass",    opacity=0.9, blend_mode="set")
   paint_terrain_layer(position=[60,0,40], radius=18,
                       layer_name="ForestFloor", opacity=0.6, blend_mode="add")
   → terrain texture matches the placed props

3. capture_scene_view(focus_path="/World/Forest/Clearing", width=1920, height=1080)
   → [Claude reviews the image]
   "The north edge looks a bit bare — the grass is too uniform there.
    Should I scatter some additional ground cover around [65,0,32]?"
```

---

### Pattern C: Scaffold zone with patrol route

Building out a guard room from a design document:

```
1. create_zone_scaffold("Zone2_GuardRoom", "RectangularRoom",
                        {width:10, depth:8, height:3.5})

2. get_scaffold_anchors()
   → confirms anchors: _entry, _exit, _center, _corner_ne, etc.

3. create_gameplay_marker(anchor_path="/Zone2_GuardRoom/_center",
                          marker_type="EnemySpawn",
                          tags=["guard", "heavy", "stationary"])

4. create_patrol_route(
     name="GuardCircuit",
     zone_path="/Zone2_GuardRoom",
     loop=True,
     waypoints=[
       {position:[...corner_nw...], idle_time:2.0, label:"nw_post"},
       {position:[...corner_ne...], idle_time:1.0},
       {position:[...corner_se...], idle_time:2.0, label:"se_post"},
       {position:[...corner_sw...], idle_time:1.0},
     ]
   )
   → route created as a child of Zone2_GuardRoom

5. resolve_scaffold_markers(zone_name="Zone2_GuardRoom")
   → returns heavy guard prefab candidates

6. execute_bake(zone_name="Zone2_GuardRoom")
   → guard spawner placed at center

7. capture_scene_view(focus_path="/Zone2_GuardRoom")
   → review result
```

---

### Pattern D: End-of-session quality and lighting pass

After a full dressing session:

```
1. validate_scene(checks=["missing_scripts", "missing_materials",
                           "missing_lod_groups", "overlapping_statics"],
                  lod_vertex_threshold=3000,
                  scope_path="/World/Village_NE")
   → 2 missing LOD groups, 0 overlaps, 1 missing material

2. replace_prefab(
     old_prefab_path="Assets/Prefabs/Placeholder_Crate.prefab",
     new_prefab_path="Assets/Prefabs/Props/Crate_Wooden_A.prefab",
     scope_path="/World/Village_NE"
   )
   → 8 placeholder crates swapped

3. [Place reflection probes in each interior]
   create_reflection_probe(position=[8,1.5,14], size=[6,3.5,8],
                           parent_path="/World/Village_NE/Tavern")
   ...

4. bake_lighting(mode="async", bake_reflection_probes=True)
   → bake started

5. update_scene_context(
     append_notes="2024-06-08: Village_NE layout complete. LOD groups needed on Townhouse_C and WarehouseA."
   )
   → session notes persisted to the SceneContext asset
```

---

## 10. Implementation Notes

### Metadata authoring — atomicity

`create_prefab_metadata` and `update_prefab_metadata` must call `MetadataRegistry.Reload()` after saving the asset, so the new metadata is immediately visible to `get_prefab_catalog` and `dress_area` in the same conversation. Without this, the new asset would require an Editor restart to appear in the registry cache.

```csharp
AssetDatabase.CreateAsset(metadata, metadataPath);
AssetDatabase.SaveAssets();
MetadataRegistry.Reload();  // ← required immediately after save
```

For `update_scene_context`, `EditorUtility.SetDirty(context)` and `AssetDatabase.SaveAssets()` are sufficient — `SceneQueryHandler.ResolveSceneContext()` re-reads the asset each call, so there is no cache to invalidate.

---

### Terrain painting — alphamap normalisation

Unity requires that the sum of all layer weights at every alphamap pixel equals 1.0. After modifying a layer, the remaining layers must be renormalised. The strategy depends on blend mode:

- **`set` mode**: set the target layer to `opacity`, then scale all other layers proportionally so the total remains 1.0.
- **`add` mode**: increase the target layer by the brush strength, then reduce all other layers proportionally. If the target layer is already at 1.0, further additions are no-ops.

```csharp
private static void NormalizeAlphamaps(float[,,] maps, int z, int x, int targetLayer, string blendMode)
{
    float targetVal = maps[z, x, targetLayer];
    float otherSum  = 0f;
    for (int i = 0; i < maps.GetLength(2); i++)
        if (i != targetLayer) otherSum += maps[z, x, i];

    float remainder = Mathf.Clamp01(1f - targetVal);
    if (otherSum < 1e-5f) return; // avoid divide by zero

    float scale = remainder / otherSum;
    for (int i = 0; i < maps.GetLength(2); i++)
        if (i != targetLayer)
            maps[z, x, i] *= scale;
}
```

---

### Lighting bake — async tracking

`Lightmapping.BakeAsync()` returns `true` if the bake was successfully enqueued and `false` if the scene isn't set up for baking (no mixed or baked lights, lightmapping disabled in the scene). The tool should surface this distinction clearly:

```csharp
bool started = Lightmapping.BakeAsync();
if (!started)
    return McpResponse.Fail("BAKE_NOT_STARTED",
        "Lightmapping.BakeAsync() returned false. Check that: " +
        "(1) at least one light is set to Mixed or Baked mode, " +
        "(2) Lighting > Realtime Global Illumination is not the only active mode.");
```

---

### Prefab replace — source matching

`PrefabUtility.GetCorrespondingObjectFromSource(go)` climbs the prefab variant chain until it reaches the outermost base. To match at the correct level of the variant hierarchy, use:

```csharp
// Matches only the direct prefab (not its base)
var source = PrefabUtility.GetNearestPrefabInstanceRoot(go);
var asset  = PrefabUtility.GetCorrespondingObjectFromSource(source);
```

Compare `asset` with the loaded `oldPrefab` using reference equality (`asset == oldPrefab`), not path comparison.

---

### Capture — Scene View state restoration

`SceneView.camera.targetTexture` must be restored after capture, and the `RenderTexture` and `Texture2D` must be explicitly destroyed (not garbage collected) to avoid memory leaks in the Editor. The `try/finally` pattern is mandatory:

```csharp
var rt = new RenderTexture(width, height, 24);
try
{
    sceneView.camera.targetTexture = rt;
    sceneView.camera.Render();
    // ... ReadPixels, Encode ...
}
finally
{
    sceneView.camera.targetTexture = null;
    RenderTexture.active           = null;
    Object.DestroyImmediate(rt);
}
```

---

### Patrol routes — runtime compatibility

The `PatrolRouteData` and `PatrolWaypoint` components are defined in `UnityMcpBridge.Editor`. They are editor-only and are stripped from runtime builds. This means they cannot be referenced by the game's AI scripts at runtime.

The recommended pattern is for the game code to define its own parallel `PatrolRoute` and `PatrolWaypoint` MonoBehaviours, and for the bridge to author the scene structure with a consistent naming convention that the game-side scripts can discover. Alternatively, the game developer can copy the component definitions from `UnityMcpBridge.Editor` into a runtime assembly, matching the field names so that the bridge-authored data is directly usable.

This is an intentional design decision: the bridge defines structure, not runtime behaviour. The game code owns the AI.

---

## 11. Development Roadmap

Four phases ordered by value-to-effort ratio. Group 6 (metadata authoring) is first because it improves every other tool in the system. Group 10 (capture) is last because its return type is architecturally distinct from all other tools and requires careful testing across MCP clients.

### Phase 1 — Metadata Authoring + Validate Extensions

**Goal:** Close the metadata loop. Every prefab and scene context can be authored and updated through the bridge. Two new validate_scene checks catch open-world-specific production issues.

- [ ] `MetadataAuthorHandler.cs` — create and update PrefabMetadata, update SceneContext
- [ ] `McpBridgeSettings.cs` — add `zoneContexts: List<ZoneContextEntry>`
- [ ] `SceneQueryHandler.cs` — extend `GET /scene/context` with `zone_name` param
- [ ] `ValidateSceneHandler.cs` — add `missing_lod_groups` and `overlapping_statics` checks; add `lod_vertex_threshold` request param
- [ ] `Models.cs` — add DTOs for all new requests/responses
- [ ] `McpRouter.cs` — register new metadata routes
- [ ] `metadata.py` — `create_prefab_metadata`, `update_prefab_metadata`, `update_scene_context`
- [ ] `scene.py` — append `get_zone_context`
- [ ] `server.py` — register all new tools

### Phase 2 — Write Operations + Prefab Replace

**Goal:** `query_terrain` gains a write counterpart; `bake_navmesh` gains its lighting equivalent; prefab replacement becomes a single call.

- [ ] `TerrainPaintHandler.cs` — alphamap paint with normalisation, multi-tile support
- [ ] `LightBakeHandler.cs` — async bake trigger, sync mode, clear mode
- [ ] `PlacementHandler.cs` — add `ReplacePrefabAsync` method and route
- [ ] `Models.cs` — add DTOs
- [ ] `McpRouter.cs` — register routes
- [ ] `open_world.py` — append `paint_terrain_layer`, `bake_lighting`
- [ ] `placement.py` — append `replace_prefab`
- [ ] `server.py` — register new tools

### Phase 3 — Reflection Probes + Patrol Routes

**Goal:** Reflection probes become a one-call operation. The scaffold system can represent movement-pattern intent alongside spawn and loot markers.

- [ ] `ReflectionProbeHandler.cs` — create probe with configurable mode/bounds/resolution
- [ ] `PatrolRouteHandler.cs` — create route with waypoint GameObjects and components; query all routes
- [ ] `PatrolRouteData.cs` / `PatrolWaypoint.cs` — lightweight editor-only MonoBehaviour definitions
- [ ] `Models.cs` — add DTOs
- [ ] `McpRouter.cs` — register routes
- [ ] `lighting.py` — append `create_reflection_probe`
- [ ] `scaffold.py` — append `create_patrol_route`, `get_patrol_routes`
- [ ] `server.py` — register new tools

### Phase 4 — Visual Feedback

**Goal:** Claude can see the scene. The designer can ask for a screenshot, review the result conversationally, and direct spatial corrections without leaving the conversation.

- [ ] `SceneViewCaptureHandler.cs` — render to RenderTexture, encode JPEG, return base64
- [ ] `Models.cs` — add response DTO
- [ ] `McpRouter.cs` — register GET route
- [ ] `debug.py` — `capture_scene_view` with Image return type (not json.dumps)
- [ ] `server.py` — register tool
- [ ] Verify image delivery across Claude Desktop, claude.ai, and any other target MCP clients

**Phase 4 acceptance criteria:**
- `capture_scene_view()` returns an image Claude can describe
- `capture_scene_view(focus_path="/some/object")` frames the object before capture
- Memory is correctly freed after capture (no RenderTexture/Texture2D leaks in the Editor process after 20 consecutive calls)
- The tool gracefully returns `NO_SCENE_VIEW` rather than crashing when no Scene View window is open

---

*Document version 1.0 — covers Groups 6–10, validate_scene extensions, and zone context system changes. All preceding groups (1–5) are documented separately.*
