# Cutscene Sequence System — Architecture Document

## Table of Contents

1. [Purpose](#1-purpose)
2. [Design Principles](#2-design-principles)
3. [Dependencies](#3-dependencies)
4. [Assembly Structure](#4-assembly-structure)
5. [Template Catalogue](#5-template-catalogue)
   - 5.1 [DialogueCamera](#51-dialoguecamera)
   - 5.2 [AreaReveal](#52-areareveal)
   - 5.3 [FocusCut](#53-focuscut)
   - 5.4 [ArrivalFlythrough](#54-arrivalflythrough)
6. [Scene and Asset Structure](#6-scene-and-asset-structure)
7. [Cinemachine Version Handling](#7-cinemachine-version-handling)
8. [MCP Integration](#8-mcp-integration)
   - 8.1 [Route Map](#81-route-map)
   - 8.2 [Request and Response Shapes](#82-request-and-response-shapes)
   - 8.3 [Python Tool Catalogue](#83-python-tool-catalogue)
9. [Designer Handoff](#9-designer-handoff)
10. [File Structure](#10-file-structure)
11. [Development Roadmap](#11-development-roadmap)

---

## 1. Purpose

The cutscene system automates the creation of short, scripted camera sequences
for common in-game moments. It targets the workflow where a developer needs a
camera path when entering dialogue, revealing a new area, focusing on a discovered
object, or introducing a location — sequences that are mechanically identical
each time but tedious to set up by hand.

The system is deliberately **not a general Timeline editor**. It covers the
~90% of "small" cutscenes that follow a small number of well-defined patterns,
and does so in a single agent conversation turn. Bespoke cinematic sequences
remain a manual authoring task.

---

## 2. Design Principles

**Template-first.** Four named templates cover the common cases in open-world
games. The agent instantiates and parametrises a template; it does not
construct raw Timeline clips or Cinemachine graphs from scratch. This keeps
the tool surface small and the output predictable.

**Creates assets, not gameplay logic.** The handler creates the Unity assets
(Timeline `.playable` file, virtual camera GameObjects, dolly track) and
organises them in the scene hierarchy. How those assets are triggered at
runtime — `UnityEvent`, `AnimationEvent`, proximity trigger, dialogue system
callback — is the developer's concern and is not touched by these tools.

**One sequence, one root.** All scene objects belonging to a sequence live
under a single container GameObject. The Timeline `.playable` asset sits in
the project at a developer-specified path. Moving or deleting the container
removes the sequence cleanly.

**Non-destructive CinemachineBrain.** Every Cinemachine setup requires a
`CinemachineBrain` on the main camera. The handler detects whether one
already exists and warns if not; it never adds one silently, as that would
alter the developer's existing camera configuration.

**Optional dependency.** If the Cinemachine or Timeline packages are not
installed, `get_cutscene_templates` returns an empty list with an explanatory
message and `create_cutscene_sequence` fails with a clear error. The rest of
the MCP bridge continues to function normally.

---

## 3. Dependencies

| Package | Minimum version | Role |
|---|---|---|
| `com.unity.cinemachine` | 2.9 | Virtual cameras, dolly tracks, smooth paths |
| `com.unity.timeline` | 1.7 | Playable Director, Cinemachine track, Signal track |

Both are bundled with Unity 2021.3 LTS and later. They are listed as
optional — the bridge `.asmdef` references them via `versionDefines` so that
the Cutscene folder compiles with `#if CINEMACHINE` guards when the packages
are absent.

### Cinemachine 2.x vs 3.x

Cinemachine 3.x (shipped with Unity 6) replaced the full public API.
Both versions are supported via conditional compilation:

```json
"versionDefines": [
  {
    "name":       "com.unity.cinemachine",
    "expression": "[2.0,3.0)",
    "define":     "CM2"
  },
  {
    "name":       "com.unity.cinemachine",
    "expression": "[3.0,)",
    "define":     "CM3"
  }
]
```

Inside handler code:
```csharp
#if CM2
    // CinemachineVirtualCamera, CinemachineSmoothPath, CinemachineDollyCart
#elif CM3
    // CmCamera, SplineContainer, CinemachineSplineCart
#else
    // return McpResponse.Fail("CINEMACHINE_NOT_FOUND", ...)
#endif
```

See §7 for the full API mapping table.

---

## 4. Assembly Structure

The cutscene system has no runtime MonoBehaviours — Cinemachine and Timeline
supply all the components that live on scene objects. The implementation
therefore lives entirely inside `UnityMcpBridge.Editor`, with two additions to
its `.asmdef`:

```json
{
  "name": "UnityMcpBridge.Editor",
  "references": [
    "Newtonsoft.Json",
    "PCGFramework.Runtime",
    "PCGFramework.Editor",
    "LevelScaffold",
    "LevelScaffold.Editor",
    "Unity.Timeline",
    "Cinemachine"
  ],
  "versionDefines": [
    { "name": "com.unity.cinemachine", "expression": "[2.0,3.0)", "define": "CM2" },
    { "name": "com.unity.cinemachine", "expression": "[3.0,)",    "define": "CM3" }
  ]
}
```

`Unity.Timeline` is the assembly name for both `com.unity.timeline` versions.
`Cinemachine` is the assembly name for both Cinemachine 2.x and 3.x.

---

## 5. Template Catalogue

### 5.1 DialogueCamera

**Use case.** Player speaks to an NPC, merchant, or quest-giver. The camera
blends from the follow camera to a two-shot or over-shoulder angle, holds for
the duration of dialogue, then blends back.

**What the agent must provide**

| Parameter | Type | Description |
|---|---|---|
| `player_path` | string | Hierarchy path of the player root, e.g. `/Player` |
| `npc_path` | string | Hierarchy path of the NPC root, e.g. `/Village/Merchant_A` |
| `camera_side` | string | `"left"` \| `"right"` \| `"front"` \| `"auto"` |
| `duration` | float | Total sequence length in seconds |
| `blend_in` | float | Blend from follow cam in seconds (default 0.5) |
| `blend_out` | float | Blend back to follow cam in seconds (default 0.5) |
| `look_offset` | float[3] | Local offset from the NPC's root to look at, e.g. `[0, 1.6, 0]` for head height |
| `sequence_name` | string | Name used for the container and exported asset |
| `timeline_path` | string | Asset path for the `.playable` file |

**What gets created**

```
/Cutscenes/
  {sequence_name}/                    [container, PlayableDirector]
    VCam_Dialogue_{sequence_name}     [CinemachineVirtualCamera]
      → Body: OrbitalTransposer, offset computed from camera_side
      → Aim:  HardLookAt → NPC look_offset point
```

**Timeline tracks**

| Track | Binding | Duration |
|---|---|---|
| `CinemachineTrack` | VCam_Dialogue | blend_in → (duration − blend_out) |
| `ActivationTrack` | VCam_Dialogue | same |
| `SignalTrack` | — | Signal at t=blend_in: `"dialogue_start"` |
| `SignalTrack` | — | Signal at t=(duration − blend_out): `"dialogue_end"` |

**Camera position calculation**

`camera_side = "auto"` picks the side that faces the camera's current forward
at creation time. The handler reads the Scene view camera transform and picks
whichever side (`left` or `right`) places the NPC more squarely in frame.

For `"front"`, the camera is placed between player and NPC looking toward the
NPC (classic interview shot).

Offset magnitude is computed from the `approximateSize` of the NPC prefab's
`PrefabMetadata`, if one exists. If no metadata is found, defaults to 2.5m
horizontal and 1.6m height.

---

### 5.2 AreaReveal

**Use case.** Player enters a new area and the camera sweeps along a
designer-specified path to show the location before returning. Typical for
dungeon entrances, settlement arrivals, boss arenas.

**What the agent must provide**

| Parameter | Type | Description |
|---|---|---|
| `waypoints` | float[3][] | World-space positions for the camera path (minimum 2) |
| `look_at_path` | string? | Optional hierarchy path of what the camera looks toward throughout. If null, camera faces along the path tangent |
| `duration` | float | Total length in seconds |
| `ease_in` | float | Fraction of duration for ease-in (default 0.2) |
| `ease_out` | float | Fraction of duration for ease-out (default 0.2) |
| `sequence_name` | string | |
| `timeline_path` | string | |

**What gets created**

```
/Cutscenes/
  {sequence_name}/                      [container, PlayableDirector]
    Path_{sequence_name}                [CinemachineSmoothPath / SplineContainer]
    Cart_{sequence_name}                [DollyCart / SplineCart]
    VCam_Reveal_{sequence_name}         [CinemachineVirtualCamera]
      → Body: TrackedDolly → Cart
      → Aim:  LookAt → look_at_path (or path tangent if null)
```

**Timeline tracks**

| Track | Binding | Duration |
|---|---|---|
| `CinemachineTrack` | VCam_Reveal | full duration |
| `ActivationTrack` | VCam_Reveal | full duration |
| `SignalTrack` | — | Signal at t=0: `"reveal_start"` |
| `SignalTrack` | — | Signal at end: `"reveal_complete"` |

The dolly cart position is animated via a custom `ControlTrack` clip that
drives the cart's `m_Position` from 0 → 1 with the specified ease curve.

---

### 5.3 FocusCut

**Use case.** A quick dramatic cut to something important — a discovered
item, a pressure plate activating, a boss stirring. The camera moves to a
close angle on the target, holds briefly, then blends back.

**What the agent must provide**

| Parameter | Type | Description |
|---|---|---|
| `target_path` | string | Hierarchy path of the thing to focus on |
| `camera_offset` | float[3] | World-space offset from the target (default `[0, 1.2, -3.0]`) |
| `duration` | float | Hold time at the focus angle in seconds |
| `blend_in` | float | Blend from active camera in seconds (default 0.3) |
| `blend_out` | float | Blend back in seconds (default 0.5) |
| `sequence_name` | string | |
| `timeline_path` | string | |

**What gets created**

```
/Cutscenes/
  {sequence_name}/                      [container, PlayableDirector]
    VCam_Focus_{sequence_name}          [CinemachineVirtualCamera]
      → Body: HardLockToTarget + offset
      → Aim:  HardLookAt → target
```

**Timeline tracks**

| Track | Binding | Duration |
|---|---|---|
| `CinemachineTrack` | VCam_Focus | blend_in → (blend_in + duration) |
| `ActivationTrack` | VCam_Focus | same |
| `SignalTrack` | — | Signal at t=blend_in: `"focus_held"` |

---

### 5.4 ArrivalFlythrough

**Use case.** A sweeping exterior or overhead shot introducing a major
location — a fortress, a city district, a labyrinth. More cinematic than
AreaReveal; appropriate for first-time arrivals at story-significant places.

**What the agent must provide**

| Parameter | Type | Description |
|---|---|---|
| `waypoints` | float[3][] | Camera path waypoints (minimum 3 for a compelling arc) |
| `look_at_position` | float[3] | Fixed world-space point the camera faces throughout (the area's visual centre of mass) |
| `duration` | float | Total length in seconds |
| `ease_in` | float | Fraction for ease-in (default 0.15) |
| `ease_out` | float | Fraction for ease-out (default 0.25) |
| `sequence_name` | string | |
| `timeline_path` | string | |

**What gets created**

```
/Cutscenes/
  {sequence_name}/                          [container, PlayableDirector]
    Path_{sequence_name}                    [CinemachineSmoothPath / SplineContainer]
    Cart_{sequence_name}                    [DollyCart / SplineCart]
    LookTarget_{sequence_name}              [empty GameObject at look_at_position]
    VCam_Flythrough_{sequence_name}         [CinemachineVirtualCamera]
      → Body: TrackedDolly → Cart
      → Aim:  HardLookAt → LookTarget
```

**Timeline tracks**

Same structure as AreaReveal with different signal names:
`"arrival_start"`, `"arrival_complete"`.

---

## 6. Scene and Asset Structure

### Scene hierarchy convention

All sequences live under a single `/Cutscenes` container at scene root. This
keeps the hierarchy clean and makes sequences easy to find, disable, or
migrate between scenes.

```
/Cutscenes
  /DialogueCamera_MerchantA          ← one container per sequence
    VCam_Dialogue_MerchantA
    PlayableDirector                 ← bound to Timeline_DialogueCamera_MerchantA.playable
  /AreaReveal_DungeonEntrance
    Path_DungeonEntrance
    Cart_DungeonEntrance
    VCam_Reveal_DungeonEntrance
    PlayableDirector
```

If `/Cutscenes` does not exist when `create_cutscene_sequence` is called, it is
created automatically.

### Timeline asset convention

```
Assets/
  Cutscenes/
    DialogueCamera_MerchantA.playable
    AreaReveal_DungeonEntrance.playable
```

The `timeline_path` parameter specifies the full asset path. If omitted, the
handler defaults to `Assets/Cutscenes/{sequence_name}.playable` and creates
the `Assets/Cutscenes/` folder if it does not exist.

### Signal assets

Unity's Signal track requires `SignalAsset` ScriptableObjects on disk. The
handler creates one `SignalAsset` per named signal in
`Assets/Cutscenes/Signals/`:

```
Assets/Cutscenes/Signals/
  dialogue_start.signal
  dialogue_end.signal
  reveal_start.signal
  reveal_complete.signal
  focus_held.signal
  arrival_start.arrival
  arrival_complete.signal
```

Signal assets are reused across sequences — if `dialogue_start.signal` already
exists it is not recreated. The developer adds `SignalReceiver` components to
relevant objects and wires the signals to their game systems.

---

## 7. Cinemachine Version Handling

The handler files use conditional compilation for the two major Cinemachine APIs.

| Concept | CM 2.x | CM 3.x |
|---|---|---|
| Virtual camera component | `CinemachineVirtualCamera` | `CmCamera` |
| Smooth path | `CinemachineSmoothPath` | `SplineContainer` (via `UnityEngine.Splines`) |
| Dolly cart | `CinemachineDollyCart` | `CinemachineSplineCart` |
| Brain | `CinemachineBrain` | `CinemachineBrain` (unchanged) |
| Look-at target | `.m_LookAt` property | `.Target.TrackingTarget` |
| Follow target | `.m_Follow` property | `.Target.CustomLookAtTarget` |

Each template file has two implementation blocks:

```csharp
#if CM2
private static void BuildDialogueCameraVCam_CM2(...)
{
    var vcam = go.AddComponent<CinemachineVirtualCamera>();
    vcam.m_LookAt = lookTarget;
    // ...
}
#elif CM3
private static void BuildDialogueCameraVCam_CM3(...)
{
    var vcam = go.AddComponent<CmCamera>();
    vcam.Target.TrackingTarget = lookTarget;
    // ...
}
#endif
```

The public template methods call the appropriate private implementation.

---

## 8. MCP Integration

### 8.1 Route Map

| Route | Handler | Description |
|---|---|---|
| `GET /assets/cutscene/templates` | `CutsceneQueryHandler` | List all templates with their parameter schemas |
| `POST /tools/cutscene/create_sequence` | `CutsceneHandler` | Instantiate a template and create all assets |

### 8.2 Request and Response Shapes

**`GET /assets/cutscene/templates` — response**

```json
{
  "cinemachine_version": "2.9.7",
  "timeline_version": "1.7.6",
  "templates": [
    {
      "name": "DialogueCamera",
      "description": "Two-shot / over-shoulder camera for dialogue sequences.",
      "parameters": [
        { "name": "player_path",    "type": "string",   "required": true,  "description": "Hierarchy path of the player root" },
        { "name": "npc_path",       "type": "string",   "required": true,  "description": "Hierarchy path of the NPC root" },
        { "name": "camera_side",    "type": "string",   "required": false, "default": "auto", "options": ["left","right","front","auto"] },
        { "name": "duration",       "type": "float",    "required": true,  "description": "Total sequence length in seconds" },
        { "name": "blend_in",       "type": "float",    "required": false, "default": 0.5 },
        { "name": "blend_out",      "type": "float",    "required": false, "default": 0.5 },
        { "name": "look_offset",    "type": "float[3]", "required": false, "default": [0,1.6,0] }
      ]
    },
    {
      "name": "AreaReveal",
      "description": "Dolly-along-path reveal of a location.",
      "parameters": [
        { "name": "waypoints",      "type": "float[3][]", "required": true, "min_count": 2 },
        { "name": "look_at_path",   "type": "string",     "required": false },
        { "name": "duration",       "type": "float",      "required": true },
        { "name": "ease_in",        "type": "float",      "required": false, "default": 0.2 },
        { "name": "ease_out",       "type": "float",      "required": false, "default": 0.2 }
      ]
    },
    {
      "name": "FocusCut",
      "description": "Quick dramatic cut to a target object.",
      "parameters": [
        { "name": "target_path",    "type": "string",   "required": true },
        { "name": "camera_offset",  "type": "float[3]", "required": false, "default": [0,1.2,-3.0] },
        { "name": "duration",       "type": "float",    "required": true },
        { "name": "blend_in",       "type": "float",    "required": false, "default": 0.3 },
        { "name": "blend_out",      "type": "float",    "required": false, "default": 0.5 }
      ]
    },
    {
      "name": "ArrivalFlythrough",
      "description": "Sweeping flythrough path with a fixed look-at point.",
      "parameters": [
        { "name": "waypoints",         "type": "float[3][]", "required": true, "min_count": 3 },
        { "name": "look_at_position",  "type": "float[3]",   "required": true },
        { "name": "duration",          "type": "float",       "required": true },
        { "name": "ease_in",           "type": "float",       "required": false, "default": 0.15 },
        { "name": "ease_out",          "type": "float",       "required": false, "default": 0.25 }
      ]
    }
  ]
}
```

If Cinemachine is not installed, the response is:
```json
{
  "cinemachine_version": null,
  "timeline_version": null,
  "templates": [],
  "message": "Cinemachine package not found. Install com.unity.cinemachine via the Package Manager to use cutscene tools."
}
```

---

**`POST /tools/cutscene/create_sequence` — request**

```json
{
  "template":       "DialogueCamera",
  "sequence_name":  "DialogueCamera_MerchantA",
  "timeline_path":  "Assets/Cutscenes/DialogueCamera_MerchantA.playable",
  "params": {
    "player_path":  "/Player",
    "npc_path":     "/Village/Merchant_A",
    "camera_side":  "right",
    "duration":     8.0,
    "blend_in":     0.5,
    "blend_out":    0.5,
    "look_offset":  [0, 1.7, 0]
  }
}
```

**`POST /tools/cutscene/create_sequence` — response**

```json
{
  "sequence_name":     "DialogueCamera_MerchantA",
  "container_path":    "/Cutscenes/DialogueCamera_MerchantA",
  "timeline_path":     "Assets/Cutscenes/DialogueCamera_MerchantA.playable",
  "vcam_paths":        ["/Cutscenes/DialogueCamera_MerchantA/VCam_Dialogue_MerchantA"],
  "signals_created":   ["dialogue_start", "dialogue_end"],
  "cinemachine_brain_found": true,
  "warnings":          []
}
```

The `cinemachine_brain_found` flag tells the agent whether to warn the
developer that a CinemachineBrain needs to be on the main camera.

### 8.3 Python Tool Catalogue

Both tools live in `mcp-server/src/unity_mcp/tools/cutscene.py` as
module-level functions, registered in `server.py` with `mcp.tool()`.

| Python function | HTTP call | Summary |
|---|---|---|
| `get_cutscene_templates()` | `GET /assets/cutscene/templates` | List available templates with parameter schemas |
| `create_cutscene_sequence(...)` | `POST /tools/cutscene/create_sequence` | Instantiate a template, create Timeline asset and scene objects |

---

## 9. Designer Handoff

After `create_cutscene_sequence` returns, these manual steps remain:

**Always required:**

1. **Connect to a trigger.** The `PlayableDirector` component on the sequence
   container does not play itself. Wire it to a gameplay event:
   ```csharp
   // In a proximity trigger, dialogue system, animation event, etc.
   GetComponent<PlayableDirector>().Play();
   ```

2. **CinemachineBrain check.** If `cinemachine_brain_found` was `false` in
   the response, add a `CinemachineBrain` component to the Main Camera.

**When using Signal tracks:**

3. **Add SignalReceiver.** Add a `SignalReceiver` component to the relevant
   GameObject (usually the player or a dialogue manager). Wire each signal to
   the appropriate `UnityEvent`:
   - `dialogue_start` → open dialogue UI
   - `dialogue_end` → close dialogue UI, return control

**For path-based templates (AreaReveal, ArrivalFlythrough):**

4. **Review waypoints.** Check the generated path in the scene view. Select
   individual path nodes and adjust positions for the desired framing.

5. **Adjust dolly timing.** The cart moves at a constant rate by default.
   Open the Timeline asset and reshape the animation curve on the cart's
   position track for better easing.

---

## 10. File Structure

```
Assets/
└── UnityMcpBridge/
    └── Editor/
        └── Cutscene/
            ├── CutsceneQueryHandler.cs    GET /assets/cutscene/templates
            ├── CutsceneHandler.cs         POST /tools/cutscene/create_sequence
            └── Templates/
                ├── CutsceneTemplateBase.cs  Abstract base: shared helpers (folder setup,
                │                             signal asset creation, brain detection, undo group)
                ├── DialogueCameraTemplate.cs
                ├── AreaRevealTemplate.cs
                ├── FocusCutTemplate.cs
                └── ArrivalFlythroughTemplate.cs

mcp-server/
└── src/
    └── unity_mcp/
        └── tools/
            └── cutscene.py               get_cutscene_templates, create_cutscene_sequence
```

### Models

Cutscene request/response types are added to `Models.cs` alongside all other
bridge models:

```csharp
public class CreateSequenceRequest
{
    [JsonProperty("template")]      public string              Template     { get; set; }
    [JsonProperty("sequence_name")] public string              SequenceName { get; set; }
    [JsonProperty("timeline_path")] public string              TimelinePath { get; set; }
    [JsonProperty("params")]        public JObject             Params       { get; set; }
}

public class CreateSequenceResultData
{
    [JsonProperty("sequence_name")]           public string   SequenceName          { get; set; }
    [JsonProperty("container_path")]          public string   ContainerPath         { get; set; }
    [JsonProperty("timeline_path")]           public string   TimelinePath          { get; set; }
    [JsonProperty("vcam_paths")]              public string[] VcamPaths             { get; set; }
    [JsonProperty("signals_created")]         public string[] SignalsCreated        { get; set; }
    [JsonProperty("cinemachine_brain_found")] public bool     CinemachineBrainFound { get; set; }
    [JsonProperty("warnings")]                public string[] Warnings              { get; set; }
}

public class CutsceneTemplateListData
{
    [JsonProperty("cinemachine_version")] public string                   CinemachineVersion { get; set; }
    [JsonProperty("timeline_version")]    public string                   TimelineVersion    { get; set; }
    [JsonProperty("templates")]           public CutsceneTemplateInfo[]   Templates          { get; set; }
    [JsonProperty("message")]             public string                   Message            { get; set; }
}

public class CutsceneTemplateInfo
{
    [JsonProperty("name")]        public string                    Name        { get; set; }
    [JsonProperty("description")] public string                    Description { get; set; }
    [JsonProperty("parameters")]  public CutsceneParameterInfo[]   Parameters  { get; set; }
}

public class CutsceneParameterInfo
{
    [JsonProperty("name")]        public string   Name        { get; set; }
    [JsonProperty("type")]        public string   Type        { get; set; }
    [JsonProperty("required")]    public bool     Required    { get; set; }
    [JsonProperty("default")]     public string   Default     { get; set; }
    [JsonProperty("description")] public string   Description { get; set; }
    [JsonProperty("options")]     public string[] Options     { get; set; }
    [JsonProperty("min_count")]   public int?     MinCount    { get; set; }
}
```

The template `params` field uses `JObject` (Newtonsoft) because each template
has a different parameter shape. The handler pattern-matches on `template` and
deserialises `params` into the appropriate strongly-typed DTO internally.

---

## 11. Development Roadmap

### Phase 1 — Infrastructure + DialogueCamera

Goal: the agent can create a working dialogue camera sequence from a single call.

- [ ] `UnityMcpBridge.Editor.asmdef` — add `Unity.Timeline` and `Cinemachine` references + version defines
- [ ] `CutsceneTemplateBase.cs` — shared helpers: folder/asset creation, signal asset cache, brain detection, undo group
- [ ] `CutsceneQueryHandler.cs` — `GetTemplatesAsync()`, version detection, Cinemachine-absent graceful response
- [ ] `CutsceneHandler.cs` — `CreateSequenceAsync()`, template dispatch, param validation
- [ ] `DialogueCameraTemplate.cs` — full CM2 + CM3 implementations
- [ ] `Models.cs` — add cutscene types
- [ ] `McpRouter.cs` — add two routes
- [ ] `cutscene.py` — `get_cutscene_templates`, `create_cutscene_sequence`
- [ ] `server.py` — register tools

Acceptance criterion: agent calls `get_cutscene_templates`, selects
DialogueCamera, calls `create_cutscene_sequence` with player and NPC paths,
and the Unity scene contains a correctly structured container with a virtual
camera looking at the NPC and a `.playable` asset with Cinemachine and Signal
tracks. Playing the Timeline in editor cuts to the dialogue angle and back.

### Phase 2 — Remaining templates

Goal: all four templates work end-to-end.

- [ ] `AreaRevealTemplate.cs` — path generation from waypoints, dolly cart, look-at
- [ ] `FocusCutTemplate.cs` — offset camera on target, quick blend timing
- [ ] `ArrivalFlythroughTemplate.cs` — multi-waypoint path, fixed look target

Acceptance criterion: agent creates an AreaReveal sequence for a dungeon
entrance with three waypoints and the camera smoothly traverses the path while
looking at the entrance.

### Phase 3 — Preview and iteration

Goal: the agent can preview a sequence in the editor and adjust it.

- [ ] `POST /tools/cutscene/preview_sequence` — calls `PlayableDirector.Play()` on the
      sequence container in the Editor
- [ ] `POST /tools/cutscene/update_sequence` — modify waypoints, duration, or blend
      values on an existing sequence without recreating it from scratch

---

*Document version 1.0 — scope: cutscene system only. Scaffold and MCP bridge
integration are covered in separate documents.*
