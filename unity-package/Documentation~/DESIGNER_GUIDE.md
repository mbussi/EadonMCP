# Unity MCP Bridge — Designer Guide

This guide explains how to use the Unity MCP Bridge to work with Claude as an AI assistant in your level design workflow. No programming knowledge is required.

---

## How it works

You talk to Claude in plain English in Claude Desktop. Claude can read your scene, understand your project's design intent, and suggest or make changes — but nothing happens to your scene unless you approve it. Every change Claude makes is registered with Unity's Undo system, so **Ctrl+Z always works**.

**The golden rule:** Claude can see your scene, but it only acts when you ask it to. You remain in control at every step.

---

## Starting a session

Always start a **new conversation** in Claude Desktop when you begin a work session. Tools are loaded at the start of a conversation — they are not available mid-conversation if the session was started before the MCP server was configured.

> **Check the connection first**
>
> Before anything else, ask Claude to verify it can reach Unity:
>
> *"Check if Unity is connected."*
>
> You should get back your Unity version and the name of the open scene. If not, check that Unity is open and the `[MCP Bridge] Listening on port 11000` message is visible in the Console.

---

## Setting up your scene for better AI decisions

The quality of Claude's suggestions improves significantly when you author metadata for your project. This is optional but highly recommended.

### SceneContext — describe your level

Create one via **Assets > Create > Eadon > MCP Bridge > Scene Context**, then assign it in **Project Settings > Eadon > MCP Bridge**.

| Field | What to write |
|---|---|
| **Level Name** | "Old Town District", "Underground Lab" |
| **Setting** | "city", "dungeon", "forest", "spaceship" |
| **Architectural Style** | "19th century European", "brutalist", "gothic" |
| **Era** | "Victorian", "contemporary", "far future" |
| **Active Tags** | Tags Claude should prefer: `urban`, `stone`, `weathered` |
| **Excluded Tags** | Tags Claude must avoid: `modern`, `plastic`, `rural` |
| **Notes** | Write this as a brief design note: *"A run-down industrial district. Buildings should look asymmetric and weathered. Street clutter is encouraged. Avoid anything pristine."* |

### PrefabMetadata — describe your prefabs

Create one via **Assets > Create > Eadon > MCP Bridge > Prefab Metadata**, and keep it next to its prefab in the project folder.

The most important field is **Description** — write it as you would for a colleague who has never seen the asset:

> *"A narrow 3-storey brick townhouse. Suits densely packed 19th-century European city streets. Pairs well with cobblestone roads and wrought-iron lamp posts. Avoid placing next to modern or rural assets."*

---

## Reading your scene

Before placing anything, ask Claude to read the scene. It uses this information to make contextually appropriate suggestions.

### What's in my scene?

```
What objects are currently in my scene?
```
```
Give me an overview of the scene hierarchy, just the top two levels.
```
```
What's in the Environment/Buildings folder in my scene?
```

### What prefabs do I have?

```
What prefabs do I have available in Assets/Prefabs/City?
```
```
Show me all the prefabs tagged as "urban".
```
```
What vegetation prefabs are available?
```

### What are the design constraints?

```
What does the scene context say about this level?
```
```
Read the design brief for this scene before we start working.
```

> **Tip:** Start every session by asking Claude to read the scene context and available prefabs before giving it any instructions. This one habit makes a significant difference in suggestion quality.

---

## Placing objects

### Place a prefab

```
Place Townhouse_A at position (10, 0, 5), rotated 90 degrees to face east.
```
```
Put a street lamp at (8, 0, 5). Parent it under the Lighting folder.
```
```
Place a market stall at the corner of the square at (0, 0, 0).
```

You can be as loose or as precise as you like. Claude will use the prefab catalog to choose the most appropriate asset if you describe what you want rather than naming a specific prefab:

```
Place something that looks like a run-down Victorian building at (15, 0, 0).
```

### Move or resize an object

```
Move the fountain to (0, 0, 0).
```
```
Rotate House_01 to face south.
```
```
Scale the archway at Environment/Archway to 1.5x its current size.
```
```
Move the barrel group 3 metres to the left.
```

You need to use the object's **scene hierarchy path** when referring to a specific object. If you are unsure of the path, ask Claude to read the hierarchy first.

### Delete an object

```
Delete the placeholder cube from the scene.
```
```
Remove the object at Environment/OldPlaceholder.
```

### Create an empty container

Good scene organisation helps Claude navigate your hierarchy and parent objects correctly.

```
Create an empty GameObject called "Buildings" to organise the scene.
```
```
Create a container called "StreetFurniture" inside the Environment group.
```

---

## Lighting

### Add a light

```
Add a warm point light above the doorway at (10, 3, 5). Intensity 1.5, range 8 metres.
```
```
Create a directional light for the sun, angled from the south-west. Warm afternoon colour.
```
```
Add a spot light inside the tunnel entrance at (0, 2, -5), pointing down the tunnel.
```
```
Set up a soft street lamp at (12, 0, 3). Warm orange glow, soft shadows.
```

### Ambient and skybox

```
Set the ambient light to a cool overcast blue. Trilight mode.
```
```
Switch the ambient light to Skybox mode.
```
```
Assign the NightSky skybox material from Assets/Materials/Skyboxes.
```
```
Clear the skybox — I want a plain colour background.
```

### Fog

```
Enable fog with a slight grey-blue tint. Exponential mode, light density.
```
```
Set linear fog starting at 30 metres and fully opaque at 150 metres.
```
```
Turn off fog.
```

> **Note on URP:** `set_fog` controls the legacy fog pass. For full volumetric fog in URP, use a Volume with a Fog override in the scene directly. All other lighting tools work normally in URP.

---

## Procedural generation

These tools place multiple objects in a single operation. The entire batch is grouped under one Undo entry.

### Scatter within an area

```
Scatter 20 oak trees randomly around (50, 0, 50) within a 25 metre radius. Keep at least 3 metres between each tree.
```
```
Scatter 12 barrels near the warehouse entrance at (0, 0, 10), 8 metre radius, aligned to the ground surface.
```
```
Randomly distribute 30 grass tufts across the clearing. Centre (20, 0, 20), radius 15m. Use seed 42 so the placement is reproducible.
```

`align_to_surface` snaps objects to terrain or mesh colliders below them — useful on uneven ground, requires colliders to be present.

### Place along a path

```
Line up fence posts along the path (0,0,0) → (20,0,0) → (20,0,15), spaced 2 metres apart. Align them to face along the path.
```
```
Place street lamps every 8 metres along the main road from (0,0,0) to (80,0,0).
```
```
Run a stone wall from (5,0,5) to (5,0,25) to (20,0,25), posts every 1.5 metres.
```

### Fill a grid

```
Fill a 6×4 grid of cobblestone floor tiles starting at (0,0,0), each tile 1 metre wide and deep.
```
```
Create a 3×3 grid of market stalls centered around (10,0,10), 4 metre spacing, offset alternate rows.
```
```
Tile the floor of the courtyard: 8 columns, 6 rows, 2×2 metre flagstones, starting at (-8, 0, -6).
```

### Batch placement (mixed prefabs)

When you need to place several different objects in one go:

```
Place these in one batch: Townhouse_A at (0,0,0), Townhouse_B at (12,0,0), and a market stall at (6,0,8).
```

---

## The proposal workflow

For any significant change — anything involving more than two or three objects — use the proposal workflow. Claude submits a plan for your review before touching the scene.

### Step 1 — Ask for a proposal

Tell Claude what you want and explicitly ask it not to make changes yet:

```
Look at my scene context and available prefabs, then propose a street corner setup with two buildings, a lamp post, and appropriate ambient lighting. Don't make any changes yet — submit a proposal for me to review.
```
```
I want to set up the market square. Propose a layout with 4 stalls, a central fountain, and some crates for atmosphere. Use the proposal system.
```
```
Propose a lighting setup appropriate for a late-afternoon Victorian city scene. Include a directional sun light, point lights on the existing lamp posts, and suitable ambient settings.
```

### Step 2 — Review in the Operation Queue window

Open **Eadon > MCP Bridge > Operation Queue** in Unity. You will see the proposal with each operation listed and a plain-English description of what it will do. Click an operation to see the exact values it will use (positions, prefabs, colours).

- **Execute** — applies all operations as a single undoable action. If any operation fails, everything is undone and the scene is left as it was.
- **Approve** — lets Claude execute it. Use this when you want Claude to run it and report back. Claude cannot execute a proposal you have not approved.
- **Discard** — removes the proposal without making any changes

### Step 3 — Execute or discard

After clicking **Execute** in the window, tell Claude the result (Claude can also check it for itself):

```
I approved the proposal. What did it place?
```

Or if you discarded it:

```
I discarded that. Let's try a different approach — fewer buildings, more open space.
```

You can also ask Claude to execute (after you click **Approve**) or discard directly:

```
Execute proposal abc12345.
```
```
Discard that proposal.
```
```
What proposals are currently pending?
```

> **Tip:** Use proposals for anything that would be annoying to undo manually. Scattering 30 trees, setting up a full lighting rig, or laying out a street — these are all good candidates for the proposal workflow.

---

## Practical workflow examples

### Setting up a new scene

```
1. Read my scene context and the prefabs available in Assets/Prefabs/City.
2. Based on the design brief, propose a starting layout for the main street —
   a few buildings on each side, some lamp posts, and basic ambient lighting.
   Use the proposal system so I can review before anything is placed.
```

### Dressing a specific area

```
I've placed the major buildings. Now I want to add street-level detail around
the market square at (0, 0, 0). Scatter some barrels, crates, and small props
within a 12 metre radius. Keep at least 1.5 metres between each piece.
Use a proposal so I can see what you're planning.
```

### Iterating on a lighting setup

```
The scene feels too bright and clean. Darken the ambient light, add some warm
fog, and adjust the directional light to a lower angle as if it's late afternoon.
Propose the changes first.
```

### Reorganising the hierarchy

```
Create container objects to organise my scene: Buildings, Lighting,
Props, and Terrain. Don't move anything yet, just create the containers.
```

---

## Writing effective prompts

**Be specific about position and orientation when it matters:**

| Less effective | More effective |
|---|---|
| Put a tree somewhere | Scatter 5 pine trees around (30, 0, 20), 10m radius |
| Add some lights | Add a warm point light at each corner of the square, 3m high, intensity 1.2 |
| Make it look more lived-in | Scatter 8 small props (crates, barrels) near the warehouse entrance at (0,0,5), 6m radius |

**Reference the scene hierarchy for existing objects:**

Before asking Claude to move or delete something, ask it to read the hierarchy so it knows the correct path:

```
Read my scene hierarchy, then move the temporary marker object to (0, 0, 0).
```

**Let Claude choose when you don't have a preference:**

```
The scene context describes a Victorian city. Choose appropriate prefabs
from my catalog and propose a street dressing for the alley behind the
main square. I trust your judgment on which assets to use.
```

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Claude says it can't connect to Unity | Make sure Unity is open. Check the Editor Console for `[MCP Bridge] Listening on port 11000`. If absent, check that **Start On Load** is ticked in **Project Settings > Eadon > MCP Bridge**. |
| Tools are not available | Start a new conversation in Claude Desktop. Tools are only loaded at conversation start. |
| Claude placed an object in the wrong place | Use Ctrl+Z in Unity to undo. Give Claude the corrected position and ask it to try again. |
| The Operation Queue window is empty | Proposals survive a script recompile but not closing Unity. Ask Claude to resubmit. |
| Claude says a proposal is not approved | Click **Approve** (or **Execute**) on it in Eadon > MCP Bridge > Operation Queue. |
| A proposal failed | Nothing changed: the whole proposal was undone. The window's Recent section and Claude both show which operation failed and why. |
| Scatter isn't snapping to terrain | `align_to_surface` requires colliders on your terrain. Check that your terrain has a Terrain Collider component. |
| Claude chose the wrong prefab | Improve the `Description` field in that prefab's `PrefabMetadata` asset, and add relevant tags. |

---

## Quick reference

| What you want to do | Ask Claude |
|---|---|
| See the scene | *"What's in my scene?"* |
| See available prefabs | *"What prefabs do I have in [folder]?"* |
| Place a prefab | *"Place [prefab name] at [position]."* |
| Move an object | *"Move [path] to [position]."* |
| Delete an object | *"Delete [path]."* |
| Add a light | *"Add a [type] light at [position]."* |
| Set ambient/fog/skybox | *"Set the ambient light to [description]."* |
| Scatter objects | *"Scatter [count] [prefab] around [position], radius [r]m."* |
| Line objects up | *"Place [prefab] every [n]m along the path [waypoints]."* |
| Tile a floor | *"Fill a [C]×[R] grid of [prefab] at [origin], cell size [w]×[d]m."* |
| Get a plan first | *"Propose [description of what you want]. Don't make changes yet."* |
| Approve a proposal | Click **Execute** (or **Approve**, to let Claude run it) in Eadon > MCP Bridge > Operation Queue |
| Undo everything | **Ctrl+Z** in Unity |
