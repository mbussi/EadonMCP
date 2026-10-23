# Production Quality Tools — Architecture Document

## Table of Contents

1. [Context](#1-context)
2. [Group 5 — Production Quality](#2-group-5--production-quality)
   - 2.1 [validate_scene](#21-validate_scene)
   - 2.2 [create_audio_zone](#22-create_audio_zone)
   - 2.3 [create_postprocessing_volume](#23-create_postprocessing_volume)
3. [MCP Integration](#3-mcp-integration)
   - 3.1 [Route Map](#31-route-map)
   - 3.2 [Request and Response Shapes](#32-request-and-response-shapes)
   - 3.3 [Python Tool Catalogue](#33-python-tool-catalogue)
4. [Compound Workflow Patterns](#4-compound-workflow-patterns)
5. [Implementation Notes](#5-implementation-notes)
6. [Development Roadmap](#6-development-roadmap)

---

## 1. Context

This document specifies the fifth group of tools identified as high-leverage additions to the MCP Bridge for a solo developer working on open-world 3D games.

The preceding groups addressed what the agent can *create and modify*. Group 5 addresses two things a solo developer consistently neglects, not from lack of care but lack of time: **validation** and **environmental polish**.

**Validation** is the work that teams have QA for and solo developers do sporadically. Missing script references, untagged triggers, materials that went null after a rename, objects floating a centimetre above the ground — these accumulate invisibly during a development session and cost disproportionate time to track down later. `validate_scene` makes a full diagnostic pass free: it costs one tool call and returns a list of paths the agent can act on immediately.

**Environmental polish** covers the category of Unity setup tasks that are always the same structural pattern but tedious enough to defer. Entering a dungeon needs a reverb zone and a darker post-processing volume. Walking into a forest needs a different reverb and a warmer grade. Every area transition in an open world needs both. `create_audio_zone` and `create_postprocessing_volume` collapse what is currently a five-to-ten-step manual setup into a single descriptive call.

Neither group requires optional packages for the core functionality — `validate_scene` and `create_audio_zone` use only built-in Unity APIs. `create_postprocessing_volume` requires URP (which is already the project's render pipeline) and compiles to a graceful error if the package is absent.

---

## 2. Group 5 — Production Quality

### 2.1 validate_scene

**What it does.** Runs a configurable set of diagnostic checks across the active scene (or a scoped hierarchy subtree) and returns a structured report of issues. Each issue includes the hierarchy path of the offending object and a detail string explaining what was found. The agent presents the report and can propose or execute fixes using other MCP tools.

**Why it matters.** Solo developers skip validation passes because running them manually takes time. The most common issues — missing script references from refactors, trigger volumes left with tag "Untagged", materials that went null when a package was removed — are invisible until they cause a runtime failure or a confusing editor warning. Making validation a one-call background operation means it happens at the end of every working session rather than never.

**Checks:**

| Check ID | Severity | What it finds |
|---|---|---|
| `missing_scripts` | `error` | GameObjects with one or more MonoBehaviour components whose script asset is missing (null reference after domain reload) |
| `missing_materials` | `error` | MeshRenderer components with one or more null or unassigned material slots |
| `untagged_triggers` | `warning` | Colliders with `isTrigger = true` and tag `"Untagged"` — almost always a forgotten configuration step |
| `duplicate_names` | `warning` | GameObjects that share a name with a sibling under the same parent — breaks `FindByPath` and confuses human navigation |
| `meshrenderer_no_collider` | `info` | GameObjects with a MeshRenderer but no Collider on the same object. Useful for prop audits; generates false positives for visual-only dressing, so keep this opt-in |
| `large_light_range` | `info` | Point or Spot lights whose `range` exceeds `light_range_threshold` (default 100m). Catches accidentally-large values that blow out an area without the developer noticing |
| `floating_objects` | `info` | Renderable GameObjects with no surface detected within `floating_ray_distance` directly below them. Catches props left floating after terrain was sculpted beneath them |

Running all checks on a large scene is fast — the expensive ones (`floating_objects`) use `Physics.Raycast` which is cheap per-object, and the total cost is bounded by `max_results_per_check`.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `checks` | string[]? | Check IDs to run. If null, runs all checks. Pass a subset to focus the report (e.g. `["missing_scripts", "missing_materials"]`) |
| `scope_path` | string? | Hierarchy path of the root object to restrict the scan to. If null, scans the entire active scene |
| `max_results_per_check` | int | Cap on issues returned per check, to avoid flooding the response (default 50) |
| `light_range_threshold` | float | Maximum acceptable light range in metres for the `large_light_range` check (default 100.0) |
| `floating_ray_distance` | float | Downward raycast distance for the `floating_objects` check in metres. Objects with no surface hit within this distance are flagged (default 0.5) |

**Response:**

```json
{
  "total_issues": 7,
  "checks_run":   ["missing_scripts", "missing_materials", "untagged_triggers"],
  "results": [
    {
      "check_id":    "missing_scripts",
      "severity":    "error",
      "description": "Objects with missing MonoBehaviour script references",
      "count":       2,
      "issues": [
        { "path": "/World/Props/Barrel_07",      "detail": "1 missing script component" },
        { "path": "/Triggers/DialogueZone_Elder", "detail": "2 missing script components" }
      ]
    },
    {
      "check_id":    "untagged_triggers",
      "severity":    "warning",
      "description": "Trigger colliders with tag 'Untagged'",
      "count":       5,
      "issues": [
        { "path": "/Triggers/Volume_CombatArea_A", "detail": "BoxCollider isTrigger=true, tag=Untagged" },
        { "path": "/Triggers/Volume_CombatArea_B", "detail": "BoxCollider isTrigger=true, tag=Untagged" }
      ]
    }
  ]
}
```

If no issues are found for a check, that check's entry has `count: 0` and `issues: []`.

---

### 2.2 create_audio_zone

**What it does.** Creates either an `AudioReverbZone` (for ambient acoustic environment shaping) or a spatialized `AudioSource` (for looping ambient sound playback) at a world-space position with configurable radii. Eight named presets cover the most common open-world acoustic environments, removing the need to look up and configure AudioReverbPreset values by hand.

**Why it matters.** Every biome transition, interior space, and environmental zone in an open world needs audio treatment. The AudioReverbZone and spatialised AudioSource setup is always the same five-step process — create empty, add component, set min/max distance, assign preset or clip, tag the collider. Doing this for every cave entrance, every village tavern, and every outdoor-to-indoor transition is the kind of task that gets deferred until the end of a project.

**Zone types:**

| `zone_type` | Components created | What it does |
|---|---|---|
| `reverb_zone` | `AudioReverbZone` | Shapes the acoustic environment — adds echo, dampening, and spatial character to all audio sources within range. No AudioClip required. Use for caves, interiors, underwater, large outdoor spaces. |
| `ambient_source` | `AudioSource` (3D spatialised) + `SphereCollider` (trigger, for visualisation) | Plays a looping ambient sound at a world position with distance-based falloff. Use for streams, campfires, wind through trees, distant machinery. The SphereCollider is trigger-only and sized to `max_distance` for scene-view visibility. |

**Reverb presets:**

| Preset | Unity `AudioReverbPreset` mapping | Typical use |
|---|---|---|
| `cave` | `Cave` | Enclosed rocky spaces, mines |
| `forest` | `Forest` | Dense tree cover, muffled outdoors |
| `dungeon` | `StoneCorridor` | Stone corridors, underground passages |
| `interior` | `Room` | Wooden/plaster interiors, taverns, houses |
| `exterior` | `Plain` | Open outdoor areas, fields, clifftops |
| `underwater` | `Underwater` | Submerged areas |
| `cathedral` | `ConcertHall` | Large stone interiors, temples, arenas |
| `alley` | `Alley` | Narrow urban spaces, cramped corridors |

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `zone_type` | string | `"reverb_zone"` or `"ambient_source"` |
| `preset` | string? | One of the preset names above. Required for `reverb_zone`. Optional for `ambient_source` (ignored if provided) |
| `position` | float[3] | World-space centre of the zone |
| `min_distance` | float | Inner radius in metres — full effect / direct sound at this range |
| `max_distance` | float | Outer radius in metres — effect fades to zero at this range |
| `audio_clip_path` | string? | AssetDatabase path to an AudioClip. Required for `ambient_source`. Ignored for `reverb_zone` |
| `volume` | float? | 0.0–1.0 playback volume for `ambient_source` (default 1.0) |
| `loop` | bool? | Loop playback for `ambient_source` (default true) |
| `play_on_awake` | bool? | Auto-play on scene start for `ambient_source` (default true) |
| `parent_path` | string? | Hierarchy path of the parent object |
| `name` | string? | Name for the created GameObject. Defaults to `"AudioReverbZone_{preset}"` or `"AmbientSource"` |

**Response:**

```json
{
  "object_path": "/World/Audio/AudioReverbZone_cave",
  "zone_type":   "reverb_zone",
  "preset":      "cave",
  "min_distance": 5.0,
  "max_distance": 20.0,
  "warnings":    []
}
```

---

### 2.3 create_postprocessing_volume

**What it does.** Creates a URP `Volume` GameObject with a new (or reused) `VolumeProfile` asset containing specified post-processing overrides. Supports both global volumes (affecting the entire scene) and local box volumes with a blend distance. Each named override is applied via its effect name and a flat properties dict, removing the need to navigate the Volume inspector manually.

**Why it matters.** Zone-based visual identity is one of the most effective tools in open-world environmental design. A dungeon should look different from a sunlit meadow, and the transition between them should be gradual. In URP, achieving this requires creating a VolumeProfile ScriptableObject, adding the Volume component, setting isGlobal, configuring blendDistance, and then navigating a nested override system for each effect. That is the same sequence every time. `create_postprocessing_volume` collapses it to a named list of effects with values.

**Package requirement.** Requires URP (`com.unity.render-pipelines.universal`). The handler compiles cleanly without it and returns `PACKAGE_NOT_INSTALLED` at runtime. The `URP` scripting define is added via `versionDefines` in the bridge `.asmdef`.

**Supported effect names (URP):**

`Bloom`, `ChromaticAberration`, `ColorAdjustments`, `ColorCurves`, `ColorLookup`, `DepthOfField`, `FilmGrain`, `LensDistortion`, `LiftGammaGain`, `MotionBlur`, `PaniniProjection`, `ShadowsMidtonesHighlights`, `SplitToning`, `Tonemapping`, `Vignette`, `WhiteBalance`

Effect names are matched case-insensitively. An unrecognised effect name is added to `overrides_failed` with an explanation rather than failing the whole request.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `position` | float[3]? | World-space centre of the volume. Required for non-global volumes. Ignored if `is_global` is true |
| `size` | float[3]? | Box extents `[width, height, depth]` in world units. If null, a point volume is created (blend distance still applies) |
| `blend_distance` | float | Distance over which the volume fades in, in world units (default 10.0) |
| `is_global` | bool | If true, the volume affects the entire scene regardless of camera position (default false) |
| `priority` | float | Volume override priority — higher values win when volumes overlap (default 0) |
| `profile_path` | string? | AssetDatabase path to an existing `VolumeProfile` to reuse. If null, a new profile is created |
| `profile_name` | string? | Name for the new profile asset, saved under `Assets/Volumes/`. Defaults to a name derived from the overrides provided |
| `overrides` | list[{effect, properties}] | Effects to add and configure on the profile (see format below) |
| `parent_path` | string? | Hierarchy path of the parent object |
| `name` | string? | Name for the created GameObject. Defaults to `"PostProcessVolume_{profile_name}"` |

**Override format:**

```json
{
  "effect": "Bloom",
  "properties": {
    "intensity":  0.5,
    "threshold":  0.9,
    "scatter":    0.7
  }
}
```

Property names are the C# field names on the `VolumeComponent` subclass, case-insensitive. Values must be JSON-compatible types: numbers for float/int parameters, booleans for bool parameters, `[r, g, b, a]` arrays for colour parameters. Only specified properties are overridden — unspecified ones keep their profile defaults and are not enabled.

**Response:**

```json
{
  "object_path":       "/World/Volumes/PostProcessVolume_DungeonEntry",
  "profile_path":      "Assets/Volumes/DungeonEntry_Profile.asset",
  "is_global":         false,
  "blend_distance":    8.0,
  "overrides_applied": ["Bloom", "ColorAdjustments"],
  "overrides_failed":  [],
  "warnings":          []
}
```

---

## 3. MCP Integration

### 3.1 Route Map

All routes follow existing bridge conventions.

| Route | Handler | Description |
|---|---|---|
| `POST /tools/scene/validate` | `SceneValidatorHandler` | Run diagnostic checks, return structured issue report |
| `POST /tools/world/create_audio_zone` | `AudioZoneHandler` | Create AudioReverbZone or spatialised AudioSource |
| `POST /tools/world/create_postprocessing_volume` | `PostProcessingHandler` | Create URP Volume with profile and overrides |

---

### 3.2 Request and Response Shapes

**`validate_scene` — request**

```json
{
  "checks":               ["missing_scripts", "missing_materials", "untagged_triggers"],
  "scope_path":           "/World/Village_NE",
  "max_results_per_check": 25,
  "light_range_threshold":  50.0,
  "floating_ray_distance":   0.3
}
```

Omit `checks` (or set to null) to run all checks. Omit `scope_path` to scan the whole scene.

**`validate_scene` — response**

```json
{
  "total_issues": 4,
  "checks_run":   ["missing_scripts", "missing_materials", "untagged_triggers"],
  "results": [
    {
      "check_id":    "missing_scripts",
      "severity":    "error",
      "description": "Objects with missing MonoBehaviour script references",
      "count":       1,
      "issues": [
        { "path": "/World/Village_NE/NPCs/Merchant_A", "detail": "1 missing script component" }
      ]
    },
    {
      "check_id":    "missing_materials",
      "severity":    "error",
      "description": "MeshRenderers with null or unassigned material slots",
      "count":       0,
      "issues":      []
    },
    {
      "check_id":    "untagged_triggers",
      "severity":    "warning",
      "description": "Trigger colliders with tag 'Untagged'",
      "count":       3,
      "issues": [
        { "path": "/World/Village_NE/Triggers/MarketZone",      "detail": "BoxCollider isTrigger=true, tag=Untagged" },
        { "path": "/World/Village_NE/Triggers/ShopEntrance_A",  "detail": "BoxCollider isTrigger=true, tag=Untagged" },
        { "path": "/World/Village_NE/Triggers/ShopEntrance_B",  "detail": "BoxCollider isTrigger=true, tag=Untagged" }
      ]
    }
  ]
}
```

---

**`create_audio_zone` — request: reverb zone**

```json
{
  "zone_type":    "reverb_zone",
  "preset":       "cave",
  "position":     [45.0, 2.0, 22.0],
  "min_distance": 4.0,
  "max_distance": 18.0,
  "name":         "Reverb_CaveEntrance",
  "parent_path":  "/World/Audio"
}
```

**`create_audio_zone` — request: ambient source**

```json
{
  "zone_type":       "ambient_source",
  "position":        [80.0, 0.5, 60.0],
  "min_distance":    3.0,
  "max_distance":    15.0,
  "audio_clip_path": "Assets/Audio/Ambient/Stream_Loop.wav",
  "volume":          0.7,
  "loop":            true,
  "play_on_awake":   true,
  "name":            "Ambient_Stream",
  "parent_path":     "/World/Audio"
}
```

**`create_audio_zone` — response**

```json
{
  "object_path":  "/World/Audio/Reverb_CaveEntrance",
  "zone_type":    "reverb_zone",
  "preset":       "cave",
  "min_distance": 4.0,
  "max_distance": 18.0,
  "warnings":     []
}
```

---

**`create_postprocessing_volume` — request: local volume**

```json
{
  "position":       [45.0, 5.0, 22.0],
  "size":           [30.0, 12.0, 30.0],
  "blend_distance": 8.0,
  "is_global":      false,
  "priority":       1,
  "profile_name":   "DungeonEntry",
  "overrides": [
    {
      "effect": "Bloom",
      "properties": { "intensity": 0.15, "threshold": 1.1 }
    },
    {
      "effect": "ColorAdjustments",
      "properties": { "saturation": -25.0, "postExposure": -0.3 }
    },
    {
      "effect": "Vignette",
      "properties": { "intensity": 0.35, "smoothness": 0.5 }
    }
  ],
  "parent_path": "/World/Volumes",
  "name":        "PostProcess_DungeonEntry"
}
```

**`create_postprocessing_volume` — request: global scene volume**

```json
{
  "is_global":    true,
  "priority":     0,
  "profile_name": "SceneBase",
  "overrides": [
    {
      "effect": "Tonemapping",
      "properties": { "mode": "ACES" }
    },
    {
      "effect": "ColorAdjustments",
      "properties": { "contrast": 8.0, "saturation": 5.0 }
    }
  ]
}
```

**`create_postprocessing_volume` — response**

```json
{
  "object_path":       "/World/Volumes/PostProcess_DungeonEntry",
  "profile_path":      "Assets/Volumes/DungeonEntry_Profile.asset",
  "is_global":         false,
  "blend_distance":    8.0,
  "overrides_applied": ["Bloom", "ColorAdjustments", "Vignette"],
  "overrides_failed":  [],
  "warnings":          []
}
```

---

### 3.3 Python Tool Catalogue

All tools live in `mcp-server/src/unity_mcp/tools/production_quality.py` as module-level async functions registered in `server.py` with `mcp.tool()`.

| Python function | Route | Summary |
|---|---|---|
| `validate_scene(checks, scope_path, max_results_per_check, light_range_threshold, floating_ray_distance)` | `POST /tools/scene/validate` | Run diagnostic checks, return structured issue report |
| `create_audio_zone(zone_type, preset, position, min_distance, max_distance, audio_clip_path, volume, loop, play_on_awake, parent_path, name)` | `POST /tools/world/create_audio_zone` | Create AudioReverbZone or spatialised AudioSource |
| `create_postprocessing_volume(position, size, blend_distance, is_global, priority, profile_path, profile_name, overrides, parent_path, name)` | `POST /tools/world/create_postprocessing_volume` | Create URP Volume with profile and overrides |

---

## 4. Compound Workflow Patterns

### Pattern A: End-of-session quality pass

After a dressing session, run a full validation scan before saving, so issues are caught while the context is still fresh:

```
1. validate_scene(checks=null, scope_path="/World/Village_NE")
   → 7 issues: 1 missing script, 4 untagged triggers, 2 floating props

2. [Agent presents report]
   "Found 7 issues in Village_NE:
    - 1 error: Merchant_A has a missing script component
    - 4 warnings: trigger volumes missing tags (MarketZone, ShopEntrance x2, StableGate)
    - 2 info: Barrel_07 and Crate_Stack_03 appear to be floating"

3. set_object_metadata(
     object_paths=["/World/Village_NE/Triggers/MarketZone",
                   "/World/Village_NE/Triggers/ShopEntrance_A",
                   "/World/Village_NE/Triggers/ShopEntrance_B",
                   "/World/Village_NE/Triggers/StableGate"],
     tag="ZoneTrigger"
   )
   → 4 triggers tagged in one call

4. snap_to_surface(object_paths=["/World/Village_NE/Props/Barrel_07",
                                   "/World/Village_NE/Props/Crate_Stack_03"])
   → floating props snapped to ground

5. [Missing script on Merchant_A requires manual fix — agent flags for developer]
```

### Pattern B: Area transition atmosphere

Setting up the transition from a sunlit village square into a dungeon entrance. One call per zone type, all three layers composed:

```
1. create_audio_zone(
     zone_type="reverb_zone",
     preset="dungeon",
     position=[dungeon_entrance],
     min_distance=3.0,
     max_distance=15.0,
     parent_path="/World/Audio"
   )
   → stone corridor acoustics blend in as player approaches

2. create_audio_zone(
     zone_type="ambient_source",
     position=[dungeon_entrance],
     min_distance=2.0,
     max_distance=12.0,
     audio_clip_path="Assets/Audio/Ambient/DungeonDrips_Loop.wav",
     volume=0.6,
     parent_path="/World/Audio"
   )
   → dripping water fades in

3. create_postprocessing_volume(
     position=[dungeon_entrance],
     size=[20.0, 8.0, 20.0],
     blend_distance=6.0,
     priority=1,
     profile_name="DungeonTransition",
     overrides=[
       {"effect": "ColorAdjustments", "properties": {"saturation": -30.0, "postExposure": -0.5}},
       {"effect": "Bloom",            "properties": {"intensity": 0.1, "threshold": 1.2}},
       {"effect": "Vignette",         "properties": {"intensity": 0.4}}
     ],
     parent_path="/World/Volumes"
   )
   → visual desaturation and vignette as player enters
```

### Pattern C: Biome audit before terrain bake

Before a lighting bake, validate the entire world to find issues that would survive into the baked result:

```
1. validate_scene(
     checks=["missing_materials", "missing_scripts", "floating_objects"],
     floating_ray_distance=0.3
   )
   → 12 floating objects detected across 3 terrain tiles

2. [Agent groups by scene tile and presents summary]

3. snap_to_surface(object_paths=[...all 12 paths...], align_to_normal=false)
   → all floating objects snapped

4. validate_scene(checks=["floating_objects"])
   → 0 issues — clean to bake
```

### Pattern D: Global scene setup

Setting baseline post-processing and ambient audio for a new scene before any detail work:

```
1. create_postprocessing_volume(
     is_global=true,
     priority=0,
     profile_name="Scene_DungeonBase",
     overrides=[
       {"effect": "Tonemapping",      "properties": {"mode": "ACES"}},
       {"effect": "ColorAdjustments", "properties": {"saturation": -15.0, "contrast": 8.0}},
       {"effect": "FilmGrain",        "properties": {"intensity": 0.12, "type": "Thin1"}}
     ]
   )
   → baseline tone and grain for the whole scene

2. create_audio_zone(
     zone_type="reverb_zone",
     preset="dungeon",
     position=[0, 0, 0],
     min_distance=999.0,
     max_distance=1000.0,
     name="Reverb_GlobalDungeon"
   )
   → effectively global reverb (large enough to cover the whole scene)
```

---

## 5. Implementation Notes

### C# handler structure

Three new handler classes live in `UnityMcpBridge.Editor.Tools`:

- `SceneValidatorHandler.cs` — `POST /tools/scene/validate`
- `AudioZoneHandler.cs` — `POST /tools/world/create_audio_zone`
- `PostProcessingHandler.cs` — `POST /tools/world/create_postprocessing_volume`

All three follow the existing pattern: `public static class`, methods called via `MainThreadDispatcher.EnqueueAsync`.

### `validate_scene` — check implementations

**`missing_scripts`**: Unity exposes a direct utility method:

```csharp
int count = GameObjectUtility.GetMonoBehavioursWithMissingScripts(go);
if (count > 0) /* flag it */
```

**`missing_materials`**: Iterate all `MeshRenderer` components found via `Resources.FindObjectsOfTypeAll<MeshRenderer>()` (or scoped via `GetComponentsInChildren`). Check `renderer.sharedMaterials` for null entries:

```csharp
var mats = renderer.sharedMaterials;
int nullCount = mats.Count(m => m == null);
```

**`untagged_triggers`**: Find all `Collider` components, filter by `isTrigger == true && go.CompareTag("Untagged")`.

**`duplicate_names`**: For each GameObject, compare its name against its siblings (children of the same parent). Build a `Dictionary<string, int>` of name counts per parent, flag any with count > 1.

**`floating_objects`**: For each `Renderer`-bearing GameObject, cast `Physics.Raycast` downward from `(go.transform.position + Vector3.up * 0.05f)`:

```csharp
bool hit = Physics.Raycast(
    go.transform.position + Vector3.up * 0.05f,
    Vector3.down,
    out _,
    req.FloatingRayDistance
);
if (!hit) /* flag as potentially floating */
```

Exclude objects that are themselves triggers or have `Rigidbody.isKinematic == false` (physics-driven objects legitimately float).

**`large_light_range`**: Find all `Light` components where `light.type == LightType.Point || light.type == LightType.Spot`, flag any where `light.range > req.LightRangeThreshold`.

**`meshrenderer_no_collider`**: Find all GameObjects with `MeshRenderer` that have no `Collider` on the same object. Exclude child-of-particle-system and objects on the "Ignore Raycast" layer.

**`duplicate_names`**: Build name counts per parent in a single pass:

```csharp
var children = parent == null
    ? scene.GetRootGameObjects()
    : parent.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray();

var seen = new Dictionary<string, List<string>>();
foreach (var go in children)
{
    var name = go.name;
    if (!seen.ContainsKey(name)) seen[name] = new List<string>();
    seen[name].Add(GetPath(go));
}
// Flag any entry with > 1 path
```

### `create_audio_zone` — reverb preset mapping

```csharp
private static readonly Dictionary<string, AudioReverbPreset> PresetMap =
    new Dictionary<string, AudioReverbPreset>(StringComparer.OrdinalIgnoreCase)
    {
        { "cave",       AudioReverbPreset.Cave },
        { "forest",     AudioReverbPreset.Forest },
        { "dungeon",    AudioReverbPreset.StoneCorridor },
        { "interior",   AudioReverbPreset.Room },
        { "exterior",   AudioReverbPreset.Plain },
        { "underwater", AudioReverbPreset.Underwater },
        { "cathedral",  AudioReverbPreset.ConcertHall },
        { "alley",      AudioReverbPreset.Alley },
    };
```

For `ambient_source`, the SphereCollider is added purely for scene-view visualisation — it is set to `isTrigger = true` so it has no physics effect. Its radius is set to `max_distance`. The AudioSource is configured with `spatialBlend = 1.0f` (fully 3D), `rolloffMode = AudioRolloffMode.Linear`, and the `minDistance` / `maxDistance` values from the request.

### `create_postprocessing_volume` — assembly guard and profile creation

Add `URP` define to the bridge `.asmdef`:

```json
"versionDefines": [
  { "name": "com.unity.render-pipelines.universal",
    "expression": "[14.0,)",
    "define":     "URP" }
]
```

And add `Unity.RenderPipelines.Universal.Runtime` to references.

Guard in handler code:

```csharp
#if !URP
    return McpResponse.Fail("PACKAGE_NOT_INSTALLED",
        "com.unity.render-pipelines.universal is required for post-processing volume tools.");
#else
    // ... Volume creation code here ...
#endif
```

**Profile creation:**

```csharp
var profile = ScriptableObject.CreateInstance<VolumeProfile>();
AssetDatabase.CreateAsset(profile, $"Assets/Volumes/{req.ProfileName}_Profile.asset");
```

**Adding overrides using reflection:**

Effect type names live in the `UnityEngine.Rendering.Universal` namespace. Load by name:

```csharp
var typeName = $"UnityEngine.Rendering.Universal.{effectName}";
var type     = Type.GetType($"{typeName}, Unity.RenderPipelines.Universal.Runtime");
if (type == null) { /* add to overrides_failed */ continue; }

var component = (VolumeComponent)profile.Add(type);
```

**Setting property values via SerializedObject:**

`VolumeParameter<T>` has two serialised fields: `m_Value` (the typed value) and `overrideState` (bool). Use `SerializedObject` to set both, which avoids needing to know the concrete `VolumeParameter<T>` subtype:

```csharp
var so = new SerializedObject(component);
foreach (var (propertyName, value) in properties)
{
    var overrideProp = so.FindProperty($"{propertyName}.overrideState");
    var valueProp    = so.FindProperty($"{propertyName}.m_Value");
    if (overrideProp == null || valueProp == null) continue;

    overrideProp.boolValue = true;
    SetSerializedPropertyValue(valueProp, value);  // handles float, int, bool, Color
}
so.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(component);
```

`SetSerializedPropertyValue` is a private helper that dispatches on `SerializedPropertyType`, mirroring the existing `SetProperty` approach in `ObjectHandler`. The enum case needs special handling: when the property type is an enum field (e.g. `Tonemapping.mode`), cast the JSON string value to the enum index via `Enum.Parse(field.FieldType, value.ToString())`.

**Volume GameObject creation:**

```csharp
var go      = new GameObject(req.Name ?? $"PostProcessVolume_{req.ProfileName}");
var volume  = Undo.AddComponent<Volume>(go);
volume.isGlobal      = req.IsGlobal;
volume.blendDistance = req.BlendDistance;
volume.priority      = req.Priority;
volume.profile       = profile;

if (!req.IsGlobal && req.Size != null)
{
    var box         = Undo.AddComponent<BoxCollider>(go);
    box.isTrigger   = true;
    box.size        = new Vector3(req.Size[0], req.Size[1], req.Size[2]);
}
```

`BoxCollider` on a Volume GameObject is not required by Unity but provides useful scene-view visualisation of the volume bounds. Set `isTrigger = true` so it has no physics effect.

---

## 6. Development Roadmap

Two phases reflect a natural dependency break: the validator and audio zone use only built-in Unity APIs and can ship immediately, while post-processing requires the URP version define and slightly more complex implementation.

### Phase 1 — Validator and audio zone

**Goal:** the agent can audit a scene for common issues and report actionable paths, and can create audio reverb zones and ambient sources in a single call.

**C#:**
- [ ] `SceneValidatorHandler.cs` — `POST /tools/scene/validate`
  - All seven checks implemented
  - `scope_path` restricts scan to a subtree
  - `max_results_per_check` cap applied per check
  - Severity correctly assigned per check ID
- [ ] `AudioZoneHandler.cs` — `POST /tools/world/create_audio_zone`
  - `reverb_zone`: GameObject + AudioReverbZone + preset mapping + min/max distance
  - `ambient_source`: GameObject + AudioSource (3D, spatialised) + SphereCollider trigger
  - Undo registration for both created objects and added components
- [ ] `Models.cs` — add request/response DTOs for both handlers
- [ ] `McpRouter.cs` — add two routes

**Python:**
- [ ] `production_quality.py` — `validate_scene`, `create_audio_zone`
- [ ] `server.py` — register both tools

**Acceptance criteria:**
- `validate_scene(checks=null)` on a scene with known missing scripts, untagged triggers, and floating objects returns the correct paths for all three
- `create_audio_zone(zone_type="reverb_zone", preset="cave", ...)` creates a correctly configured AudioReverbZone with the Cave preset and the specified min/max distances
- `create_audio_zone(zone_type="ambient_source", audio_clip_path="...", ...)` creates a spatialised AudioSource with a SphereCollider trigger at the max_distance radius

### Phase 2 — Post-processing volume

**Goal:** the agent can create URP Volume GameObjects with named overrides in a single call, supporting both global scene volumes and local blended volumes.

**C#:**
- [ ] `PostProcessingHandler.cs` — `POST /tools/world/create_postprocessing_volume`
  - `URP` versionDefine guard; graceful PACKAGE_NOT_INSTALLED otherwise
  - VolumeProfile asset creation in `Assets/Volumes/`
  - Effect type resolution by name via reflection
  - Property setting via `SerializedObject` / `m_Value` + `overrideState` approach
  - Global volume (`isGlobal=true`) and local box volume paths
  - `profile_path` reuse path (load existing profile, add/update overrides)
  - Undo registration for all created assets and objects
- [ ] `Models.cs` — add request/response DTOs for PostProcessingHandler
- [ ] `McpRouter.cs` — add route
- [ ] `UnityMcpBridge.Editor.asmdef` — add `Unity.RenderPipelines.Universal.Runtime` reference and `URP` versionDefine

**Python:**
- [ ] `production_quality.py` — add `create_postprocessing_volume`
- [ ] `server.py` — register tool

**Acceptance criteria:**
- `create_postprocessing_volume(is_global=true, profile_name="SceneBase", overrides=[{effect:"Tonemapping", properties:{mode:"ACES"}}])` creates a VolumeProfile asset with a Tonemapping override set to ACES, and a global Volume GameObject in the scene
- `create_postprocessing_volume(position=[...], size=[...], blend_distance=8.0, overrides=[{effect:"Bloom",...},{effect:"ColorAdjustments",...}])` creates a correctly sized local Volume with a BoxCollider and both overrides enabled and populated in the profile
- An unknown effect name (e.g. `"FakeEffect"`) appears in `overrides_failed` rather than failing the entire request

---

*Document version 1.0 — scope: Production Quality tool group only (Group 5). Foundation tools (Group 1), Dressing Workflow (Group 2), Open World tools (Group 3), Cutscene System, Scaffold System, and PCG integration are covered in separate documents.*
