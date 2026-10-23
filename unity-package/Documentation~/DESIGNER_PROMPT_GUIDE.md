# Unity MCP Bridge — Designer Prompt Guide

This guide covers every tool in the bridge. For each tool it shows the kinds of prompt that cause problems and the kinds that work well. The underlying principle is almost always the same: Claude can only act on what you give it. Vague intent produces guesswork; specific context produces reliable results.

---

## How to start every session

Before asking Claude to do anything, orient it with the three discovery calls. Claude cannot see your project without them.

```
1. ping_unity          — confirm Unity is connected
2. get_scene_context   — understand the level's design intent and style rules
3. get_prefab_catalog  — discover what assets are available
4. get_scene_hierarchy — understand what already exists in the scene
```

You don't need to say "call these tools" — just start with something like *"Read the scene context and prefab catalog, then let's work on the market area."* Claude will know to run the discovery pass first.

---

## 1. Connection and Scene State

### `ping_unity`
Verifies that Unity is running and the bridge is active. Returns the active scene name and Unity version. Run this first if anything seems wrong.

| | |
|:---:|:---|
| ❌ | "Are you connected?" |
| ✓ | "Ping Unity and tell me what scene is open." |

*The bad prompt asks Claude a question it answers from memory. The good prompt makes Claude actually verify the live connection.*

---

### `get_scene_context`
Reads the SceneContext ScriptableObject for the current level — the design document that tells Claude what setting, style, preferred tags, and excluded tags apply. **This is the most important call to make before any asset selection.**

| | |
|:---:|:---|
| ❌ | "Add some props to the dungeon." |
| ✓ | "Read the scene context first, then suggest appropriate props for the dungeon based on the style and preferred tags." |

| | |
|:---:|:---|
| ❌ | "What kind of level is this?" |
| ✓ | "Read the scene context and summarise the level's setting, era, and any design notes." |

*Without reading the scene context, Claude has no basis for making style-appropriate choices. It will either ask you a lot of questions or make generic guesses.*

---

### `get_prefab_catalog`
Lists all prefabs in the project, with rich metadata (tags, category, placement rules, compatible/incompatible pairs) where PrefabMetadata has been authored. Call this before any placement work.

| | |
|:---:|:---|
| ❌ | "Do we have a barrel prefab?" |
| ✓ | "Search the prefab catalog under Assets/Prefabs/Props for anything tagged 'storage' or 'container'." |

| | |
|:---:|:---|
| ❌ | "Place some crates and barrels near the dock." |
| ✓ | "Check the prefab catalog for crates and barrels, then place a small cluster near the dock at [45, 0, 12]." |

*The first bad prompt makes Claude guess at prefab names and paths. The second skips discovery entirely, which means Claude will probably invent a path and fail.*

---

### `get_scene_hierarchy`
Returns the full object tree with positions, rotations, and component lists. Use it to get exact hierarchy paths before referencing any existing object.

| | |
|:---:|:---|
| ❌ | "Move the market stall to the left a bit." |
| ✓ | "Read the scene hierarchy, find the market stall objects, then tell me their paths so we can reposition them." |

| | |
|:---:|:---|
| ❌ | "What's in the scene?" |
| ✓ | "Show me the top two levels of the hierarchy (max_depth=2) to see the main structural containers." |

*Passing max_depth=2 on a large scene avoids a huge dump of data you don't need yet. On smaller scenes, omit it.*

---

## 2. Object Placement

### `place_prefab`
Places a single prefab instance at a given world position. Use `place_prefab_batch` instead if you're placing more than two or three objects.

| | |
|:---:|:---|
| ❌ | "Place a house near the town square." |
| ✓ | "Place Assets/Prefabs/Buildings/Townhouse_B.prefab at position [12, 0, 8], rotated 90° on Y, parented to /World/Village/Buildings." |

| | |
|:---:|:---|
| ❌ | "Add a torch to the dungeon wall." |
| ✓ | "Use dry_run first to confirm placement, then place the wall torch prefab at [4.5, 1.8, 22], rotated [0, 180, 0] to face inward, parented to /Dungeon/Zone1/Lighting." |

*Positions, rotations, and parent paths must be explicit. 'Near the town square' means nothing to the tool — it has no concept of where the town square is unless you give coordinates.*

---

### `set_transform`
Moves, rotates, or rescales an existing scene object. Omit any axis you don't want to change.

| | |
|:---:|:---|
| ❌ | "Move the barrel a bit to the right." |
| ✓ | "Move /World/Village/Market/Barrel_03 to position [14.5, 0, 9.2]. Leave rotation and scale unchanged." |

| | |
|:---:|:---|
| ❌ | "Scale up the signpost so it's more visible." |
| ✓ | "Set the scale of /World/Village/Signpost_01 to [1.4, 1.4, 1.4]." |

---

### `delete_object`
Deletes an object and all its children. This is undoable in the Editor but there's no undo from Claude's side once confirmed.

| | |
|:---:|:---|
| ❌ | "Delete all the placeholder objects." |
| ✓ | "Use dry_run on /World/Placeholders to confirm what's inside it, then delete it if it only contains temp markers." |

| | |
|:---:|:---|
| ❌ | "Clean up the scene." |
| ✓ | "Read the scene hierarchy and list objects with 'TEMP' or 'OLD' in their names. I'll confirm which ones to delete before you remove anything." |

*Deletion is the one tool where pausing for confirmation is always worth it. Dry_run costs nothing.*

---

### `create_empty`
Creates an empty GameObject, typically used as an organisational container before placing children into it.

| | |
|:---:|:---|
| ❌ | "Make a folder for the lights." |
| ✓ | "Create an empty container called 'Lighting' at the scene root at position [0,0,0], then we'll parent all the street lights under it." |

---

## 3. Lighting

### `create_light`
Creates a Point, Spot, Directional, or Area light with full control over colour, intensity, range, and shadows.

| | |
|:---:|:---|
| ❌ | "Add a warm light to the tavern." |
| ✓ | "Create a Point light inside /World/Village/Tavern at position [8, 2.5, 14], colour [1.0, 0.75, 0.4], intensity 2.0, range 8m, soft shadows." |

| | |
|:---:|:---|
| ❌ | "Light up the market area." |
| ✓ | "Create four Point lights at the corners of the market (roughly [10,3,10], [20,3,10], [10,3,20], [20,3,20]), all warm orange, intensity 1.5, range 12m, parented to /World/Village/Lighting." |

*For four lights, consider using `propose_changes` so you can review the positions in the Operation Queue before committing.*

---

### `set_ambient_light`
Sets the scene's global ambient light mode (colour, gradient, or skybox-driven) and intensity.

| | |
|:---:|:---|
| ❌ | "Make the ambient light more blue at night." |
| ✓ | "Set ambient light to Gradient mode: sky colour [0.05, 0.05, 0.2], equator [0.03, 0.03, 0.1], ground [0.01, 0.01, 0.05]." |

---

### `set_skybox`
Assigns a skybox material to the scene, or clears it.

| | |
|:---:|:---|
| ❌ | "Use the night sky." |
| ✓ | "Set the skybox to Assets/Materials/Skyboxes/NightOvercast.mat." |

---

### `set_fog`
Configures Unity's built-in scene fog (linear, exponential, or exponential-squared).

| | |
|:---:|:---|
| ❌ | "Add some fog." |
| ✓ | "Enable linear fog: colour [0.6, 0.6, 0.65], start 30m, end 120m. This is for the mist-in-the-valley look we discussed." |

---

## 4. The Proposal Workflow

The proposal workflow is the human-in-the-loop safety net for bulk or risky changes. The agent submits a plan, you review it in **Window → MCP Bridge → Operation Queue**, then confirm or reject before anything touches the scene.

**Use proposals whenever you're placing more than a handful of objects, doing anything destructive, or working in an area you care about.**

### `propose_changes`
Submits a batch of operations for review. Nothing is executed yet.

| | |
|:---:|:---|
| ❌ | "Just go ahead and place all 15 houses." |
| ✓ | "Build a proposal for the 15 houses and submit it for my review. Don't execute until I confirm." |

| | |
|:---:|:---|
| ❌ | "Propose a light for each room." (no summary, no structure) |
| ✓ | "Create a proposal titled 'Dungeon Zone 1 Lighting — 8 torches' with one create_light operation per anchor point. List each torch's position in the description so I can check them in the queue." |

---

### `execute_proposal`
Executes an approved proposal. All operations run as a single undoable Undo group.

| | |
|:---:|:---|
| ❌ | "Execute it." (Claude doesn't know which proposal) |
| ✓ | "Execute proposal [paste the ID Claude gave you when it submitted]." |

*Claude retains the proposal ID within a conversation. You only need to paste it explicitly if you're resuming after a session break.*

---

### `discard_proposal`
Rejects a pending proposal without executing it.

| | |
|:---:|:---|
| ✓ | "Discard the proposal — I want to change the torch positions before we commit." |

---

### `get_pending_proposals`
Lists all proposals waiting in the queue.

| | |
|:---:|:---|
| ✓ | "Check for any pending proposals before we start — I want to make sure the queue is clear." |

---

## 5. Procedural Placement

These tools place many objects in a single call. Always prefer them over asking Claude to loop through `place_prefab` manually.

### `place_prefab_batch`
Places multiple different prefabs in one round-trip, each with its own position, rotation, and parent.

| | |
|:---:|:---|
| ❌ | "Place a barrel at [5,0,5], another at [7,0,5], and a crate at [6,0,7]." (three separate place_prefab calls) |
| ✓ | "Use place_prefab_batch to place those three objects in one call." |

---

### `scatter_prefabs`
Randomly distributes copies of one prefab within a circular area.

| | |
|:---:|:---|
| ❌ | "Scatter some trees around." |
| ✓ | "Scatter 12 pine trees within a 20m radius of [60, 0, 40], minimum 3m spacing, aligned to the terrain surface. Seed 42 so we can reproduce the layout." |

| | |
|:---:|:---|
| ❌ | "Add some rocks to the hillside." |
| ✓ | "Scatter 8 MediumRock_A prefabs within 10m of [80, 5, 55], align_to_normal=true so they tilt with the slope." |

*Without a seed, reruns produce different layouts. Use a seed when you want to be able to reproduce the exact result.*

---

### `place_along_path`
Places copies of a prefab at regular intervals along a polyline. Perfect for fences, walls, hedgerows, and road markings.

| | |
|:---:|:---|
| ❌ | "Put fence posts along the north wall." |
| ✓ | "Place FencePost.prefab at 2m intervals along the path [0,0,30] → [40,0,30] → [40,0,0], aligned to path direction, parented to /World/Village/Fences." |

---

### `fill_grid`
Fills a rectangular area with a prefab on a regular grid. Good for market stalls, crops, paving tiles, and regular formations.

| | |
|:---:|:---|
| ❌ | "Fill the market square with stalls." |
| ✓ | "Fill a 4×3 grid of MarketStall_A.prefab starting at [10, 0, 15], cells 3.5m × 3.5m, with alternating row offset for a staggered look." |

---

## 6. Components

### `get_component_catalog`
Lists all component types available in the project — built-in Unity types plus your own MonoBehaviour scripts.

| | |
|:---:|:---|
| ❌ | "Does the project have a QuestTrigger component?" |
| ✓ | "Check the component catalog for any quest-related or trigger-related script names." |

*Use this before `add_component_to_object` to confirm the exact type name.*

---

### `add_component_to_object`
Adds a component to an existing scene object.

| | |
|:---:|:---|
| ❌ | "Add a trigger to the chest." |
| ✓ | "Add a BoxCollider to /World/Dungeon/Chest_01, set isTrigger=true and size [0.8, 0.6, 0.5]." |

| | |
|:---:|:---|
| ❌ | "Make the NPC interactable." |
| ✓ | "Check the component catalog for an 'Interactable' or 'InteractionZone' script, then add it to /World/Village/Merchant_A with a trigger radius of 2.5m." |

---

## 7. PCG (Procedural Content Generation)

The PCG tools build and validate Unity PCGGraph assets. They are for setting up procedural generation rules, not for direct placement.

### `get_pcg_node_catalog`
Lists available PCG node types. Call this before building a graph.

| | |
|:---:|:---|
| ✓ | "Read the PCG node catalog so we know what node types are available before designing the forest floor graph." |

---

### `search_prefabs_for_pcg`
Finds prefabs by tag or category and returns the asset GUIDs needed for PCG prefab bindings.

| | |
|:---:|:---|
| ❌ | "Use the rock prefabs for PCG." |
| ✓ | "Search prefabs for category 'rock' and tag 'ground_cover' to get the GUIDs for the terrain scatter graph." |

---

### `validate_pcg_graph`
Validates a PCGGraph document without creating any assets. Run this before `create_pcg_graph`.

| | |
|:---:|:---|
| ❌ | "Create the PCG graph." (without validating first) |
| ✓ | "Validate the graph document first. Only call create_pcg_graph if validation passes with no errors." |

---

### `create_pcg_graph`
Creates a PCGGraph .asset file from a graph description.

| | |
|:---:|:---|
| ✓ | "Create the validated forest floor PCG graph, save it to Assets/PCGGraphs/ForestFloor.asset." |

---

### `get_pcg_graphs`
Lists existing PCGGraph assets in the project.

| | |
|:---:|:---|
| ✓ | "List the existing PCG graphs so I can see what's already been set up before we create a new one." |

---

## 8. Scaffold System

The scaffold system translates a level design document into a Unity scene skeleton. The workflow always goes: **create zones → add gameplay markers → link zones → resolve markers → bake**.

### `create_zone_scaffold`
Creates a ScaffoldZone with auto-generated anchor points based on zone type and dimensions.

| | |
|:---:|:---|
| ❌ | "Create a spiral staircase zone." |
| ✓ | "Create a SpiralStaircase scaffold named 'Zone1_Descent' at position [0, 0, 0]: 12 steps, 0.3m riser height, 4m width, 0.75 turns. Design note: 'Main descent into the buried catacombs'." |

| | |
|:---:|:---|
| ❌ | "Make a room zone." |
| ✓ | "Create a RectangularRoom scaffold named 'Zone2_Antechamber': width 8m, depth 6m, height 3.5m, entry facing south at [0, 0, 20]." |

*Zone type and dimensions are required. Without them the tool has no basis for generating the correct anchor pattern.*

---

### `get_scaffold_anchors`
Returns all anchor points in the scene with their exact paths. You need these paths to place gameplay markers.

| | |
|:---:|:---|
| ❌ | "Put an enemy at the midpoint of the staircase." |
| ✓ | "Read the scaffold anchors for Zone1_Descent, then place a Spawn marker at the _mid anchor." |

---

### `create_gameplay_marker`
Places a functional intent marker at an anchor point. Markers are resolved to actual prefabs during the bake phase.

| | |
|:---:|:---|
| ❌ | "Add some enemies to the descent zone." |
| ✓ | "Create a Spawn marker at /Zone1_Descent/step_04 with tags ['enemy', 'melee', 'patrol'], detection radius 8m." |

| | |
|:---:|:---|
| ❌ | "Put a loot chest somewhere in the staircase." |
| ✓ | "Create an Loot marker at anchor /Zone1_Descent/_mid with tags ['chest', 'mid_tier'], detection radius 0." |

---

### `update_gameplay_marker`
Updates tags or config on an existing marker. Use this to adjust intent without recreating the marker.

| | |
|:---:|:---|
| ❌ | "Delete the spawn marker and make a new one with a different tag." |
| ✓ | "Update the marker at /Zone1_Descent/step_04 — add the 'ranged' tag and remove 'melee'." |

---

### `link_zones`
Connects two zones with a transition connector so pathfinding and zone metadata flow correctly.

| | |
|:---:|:---|
| ✓ | "Link Zone1_Descent's exit to Zone2_Antechamber's entry with a 1.5m wide doorway connector." |

---

### `resolve_scaffold_markers`
Scores available prefabs against each marker's tags and returns ranked candidates. You pick from the list; the actual prefabs are placed during bake.

| | |
|:---:|:---|
| ❌ | "Bake the scaffold." (skipping resolution) |
| ✓ | "Resolve scaffold markers for Zone1_Descent, showing the top 3 prefab candidates per marker. I want to review the choices before baking." |

---

### `execute_bake`
Places the chosen prefabs at all resolved marker positions. This is the point of no return for the scaffold — after baking, the zone has real geometry.

| | |
|:---:|:---|
| ❌ | "Execute the bake." (before confirming all marker assignments) |
| ✓ | "All marker assignments look good. Execute the bake for Zone1_Descent only." |

---

### `hide_scaffold` / `show_scaffold`
Toggles scaffold object visibility without removing them from the scene.

| | |
|:---:|:---|
| ✓ | "Hide the scaffold so I can see how the placed prefabs look without the guide geometry." |
| ✓ | "Show the scaffold again — I want to check the anchor positions." |

---

## 9. Cutscenes

### `get_cutscene_templates`
Lists available templates and their required parameters. Always call this before `create_cutscene_sequence`.

| | |
|:---:|:---|
| ❌ | "Create a dialogue cutscene for the merchant." |
| ✓ | "Check available cutscene templates, then create the appropriate one for an NPC dialogue with the merchant." |

---

### `create_cutscene_sequence`
Instantiates a template to produce camera GameObjects, a Timeline asset, and signal emitters.

| | |
|:---:|:---|
| ❌ | "Make a cutscene where the player talks to the merchant." |
| ✓ | "Create a DialogueCamera sequence called 'Merchant_Greeting': player at /Player, NPC at /World/Village/Merchant_A, duration 7 seconds, camera on the left side, blend in 0.4s." |

---

### `preview_cutscene_sequence`
Triggers a preview playback in the Editor without entering Play Mode.

| | |
|:---:|:---|
| ✓ | "Preview the Merchant_Greeting cutscene so I can check the camera angle." |

---

### `update_cutscene_sequence`
Adjusts parameters on an existing sequence without recreating it.

| | |
|:---:|:---|
| ❌ | "The cutscene is too long, redo it." |
| ✓ | "Update Merchant_Greeting: change duration to 5 seconds and blend_out to 0.8s." |

---

## 10. Foundation — Object Inspection and Mutation

### `find_objects`
Searches the scene by tag, component type, name pattern, layer, or proximity. The output gives you the exact hierarchy paths you need for every other tool.

| | |
|:---:|:---|
| ❌ | "Where are the torches?" |
| ✓ | "Find all objects with component 'Light' near position [30, 0, 20] within 15m." |

| | |
|:---:|:---|
| ❌ | "Find all interactive objects." |
| ✓ | "Find all objects tagged 'Interactable' in the scene. Then find objects with component 'Rigidbody' near [0,0,0] within 50m." |

*Each call requires at least one filter. Combine filters to narrow results — tag + near_position is a common pattern.*

---

### `get_object_properties`
Reads serialised property values from a component on an existing object.

| | |
|:---:|:---|
| ❌ | "What are the light settings on the main lamp?" |
| ✓ | "Read the intensity, range, and color properties of the Light component on /World/Village/Lighting/Lamp_Main." |

---

### `set_object_properties`
Writes serialised property values to a component. Use for any property that doesn't have a dedicated tool (e.g. tweaking a custom script field, adjusting a collider offset).

| | |
|:---:|:---|
| ❌ | "Make the patrol NPC faster." |
| ✓ | "Set the 'patrolSpeed' property on the NavMeshAgent component of /World/Village/Guard_01 to 3.5." |

| | |
|:---:|:---|
| ❌ | "Change the light colour." |
| ✓ | "Set the 'color' property on the Light component of /World/Dungeon/Torch_07 to [1.0, 0.5, 0.1]." |

---

### `set_object_metadata`
Sets tag, layer, name, static flags, and active state on one or more objects in a single call. Prefer this over `set_object_properties` for these specific fields.

| | |
|:---:|:---|
| ❌ | "Tag all the crates as pickable." (implies one-by-one) |
| ✓ | "Set tag 'Pickable' and layer 'Interactables' on all five crate objects in /World/Dock in one call." |

---

### `reparent_objects`
Moves objects to a different parent in the hierarchy.

| | |
|:---:|:---|
| ❌ | "Reorganise the scene so lights are grouped together." |
| ✓ | "Reparent /World/Torch_01, /World/Torch_02, and /World/Torch_03 under /World/Lighting/Torches." |

---

### `duplicate_objects`
Copies one or more objects with an optional position/rotation offset.

| | |
|:---:|:---|
| ❌ | "Copy that building and put it across the street." |
| ✓ | "Duplicate /World/Village/Townhouse_A with a position offset of [16, 0, 0] and Y rotation offset of 180°." |

---

## 11. Dressing Workflow

### `snap_to_surface`
Raycasts from each object's position downward (or in any direction) and moves it to the hit surface. The fastest fix for floating props.

| | |
|:---:|:---|
| ❌ | "Some of the rocks are floating, fix them." |
| ✓ | "Snap all objects matching the path /World/Props/Rocks/* to the surface. Use align_to_normal=true so they tilt with the terrain." |

| | |
|:---:|:---|
| ❌ | "Snap the crates to the floor." |
| ✓ | "Snap these crate paths to the surface with surface_offset=0.02 — their pivots are centred, not at the base." |

---

### `align_and_distribute`
Spaces, stacks, aligns in a line, or arranges objects in a circle. Operates on objects you've already placed.

| | |
|:---:|:---|
| ❌ | "Space out the market stalls evenly." |
| ✓ | "Use distribute_between mode to space the five market stalls evenly between positions [10,0,20] and [40,0,20] along the X axis." |

| | |
|:---:|:---|
| ❌ | "Put the barrels in a circle." |
| ✓ | "Arrange these six barrels in a circle mode, radius 3m, centred on [25, 0, 15]." |

---

### `randomize_transforms`
Adds bounded random variation to position, rotation, and/or scale. Great for breaking up the uniformity of scattered or grid-placed objects.

| | |
|:---:|:---|
| ❌ | "Make the rocks look less uniform." |
| ✓ | "Randomize the rocks at /World/Props/Rocks/*: rotation Y ±180°, scale ×0.8–1.3, no position offset. Use seed 7 for reproducibility." |

| | |
|:---:|:---|
| ❌ | "Add variation to the fence posts." |
| ✓ | "Randomize the fence posts: rotation Y ±5°, no scale variation, no position change. Subtle tilt only." |

---

### `group_objects`
Creates a named empty container and reparents selected objects into it. Use this after any bulk placement to keep the hierarchy tidy.

| | |
|:---:|:---|
| ❌ | "Group everything." |
| ✓ | "Group the twelve market stall objects into a container called 'Market_Stalls_Row_A', pivot at centroid, parented to /World/Village/Market." |

---

### `create_trigger_volume`
Creates a trigger collider — box, sphere, or capsule — optionally with a script component attached.

| | |
|:---:|:---|
| ❌ | "Add a trigger for the quest area." |
| ✓ | "Create a box trigger at [30, 1, 45], size [10, 3, 10], tagged 'QuestTrigger', with a QuestActivator script component, property questId = 'find_the_merchant'." |

---

### `apply_material`
Assigns a material to MeshRenderer components on one or more objects.

| | |
|:---:|:---|
| ❌ | "Make the walls darker." |
| ✓ | "Apply the material 'Stone_Dark_Worn' to all objects matching /World/Dungeon/Zone1/Wall_*." |

| | |
|:---:|:---|
| ❌ | "Randomise the floor materials." |
| ✓ | "Apply material with tag 'dungeon_floor' to the floor tiles using random_from_tag=true so each tile gets a different variant." |

---

### `dress_area`
The most powerful single-call dressing tool. Scores the prefab catalog against an intent string, generates valid positions using Poisson disk sampling, avoids existing objects, snaps to surface, and submits a proposal for review.

| | |
|:---:|:---|
| ❌ | "Make this area look good." |
| ✓ | "Dress the area at [60, 0, 40] with a 15m radius: intent 'abandoned storage yard with broken crates, rusted barrels, and overgrown weeds', density medium." |

| | |
|:---:|:---|
| ❌ | "Add dungeon stuff to zone 2." |
| ✓ | "Dress Zone2_Antechamber at its centre position with radius 6m, intent 'sparse ancient dungeon antechamber — crumbled pillars, dust piles, a few scattered bones', density sparse." |

*`dress_area` always produces a proposal, not direct placement. Review it in the Operation Queue before executing.*

---

## 12. Open World

### `query_terrain`
Reads terrain height, surface normal, slope angle, and painted layer weights at one or more world positions. Call this **before** placing anything on terrain, especially buildings and structures.

| | |
|:---:|:---|
| ❌ | "Place the watchtower at [80, 0, 120]." |
| ✓ | "Query the terrain at [80, 0, 120] first. If slope is under 10° and the dominant layer is 'Grass' or 'DirtPath', place the watchtower there. Otherwise suggest a nearby flatter position." |

| | |
|:---:|:---|
| ❌ | "Is this a good spot for a village?" |
| ✓ | "Query these five candidate positions and tell me which ones have slope under 5° and grass or dirt as the dominant layer: [100,0,50], [120,0,60], [90,0,80], [110,0,90], [130,0,55]." |

---

### `bake_navmesh`
Triggers a NavMesh bake for NavMeshSurface components. Run this after any placement session that adds or moves obstacle geometry.

| | |
|:---:|:---|
| ❌ | "The navmesh is out of date." |
| ✓ | "Bake all NavMesh surfaces now that we've finished placing the village buildings." |

| | |
|:---:|:---|
| ❌ | (forgetting to bake at all after major placement) |
| ✓ | "After executing the proposal, bake the navmesh so the AI navigation stays current." |

---

### `set_navmesh_area`
Assigns a NavMesh area type to objects (via NavMeshModifier) or creates a volumetric override (via NavMeshModifierVolume).

| | |
|:---:|:---|
| ❌ | "Mark the water as not walkable." |
| ✓ | "Set NavMesh area 'Water' on /World/River/WaterPlane_01, then bake." |

| | |
|:---:|:---|
| ❌ | "Block off the tribunal courtyard." |
| ✓ | "Create a 'Not Walkable' NavMesh modifier volume at [30, 1, 45], size [12, 4, 12], named 'NavBlock_TribunalCourtyard'. Then bake." |

---

### `get_loaded_scenes`
Lists all scenes currently open in the Editor with their paths and dirty status.

| | |
|:---:|:---|
| ✓ | "Check which scenes are loaded before we start moving objects — I want to make sure Village_NW is open." |

---

### `manage_scene`
Loads or unloads additive scenes, moves objects between scenes, or creates new scene files.

| | |
|:---:|:---|
| ❌ | "Load the other scene." |
| ✓ | "Load Assets/Scenes/Village_NW.unity additively." |

| | |
|:---:|:---|
| ❌ | "Move those barrels to the right scene." |
| ✓ | "Move /Village_NE/Props/Barrel_07 and /Village_NE/Props/Barrel_08 to the Village_NW scene." |

---

## 13. Production Quality

### `validate_scene`
Runs a configurable diagnostic pass and returns a list of issues with hierarchy paths. Run this at the end of a working session.

| | |
|:---:|:---|
| ❌ | "Is the scene ready to ship?" |
| ✓ | "Run validate_scene with all checks enabled, scoped to /World/Village_NE. List every issue with its path." |

| | |
|:---:|:---|
| ❌ | "Check for problems." |
| ✓ | "Validate just the critical checks: missing_scripts and missing_materials. I'll deal with warnings separately." |

*After getting the report, you can ask Claude to fix individual categories: "Fix all the untagged triggers by setting their tag to 'ZoneTrigger'".*

---

### `create_audio_zone`
Creates either an AudioReverbZone (acoustic shaping, no audio clip needed) or a 3D AudioSource (ambient sound playback).

| | |
|:---:|:---|
| ❌ | "Add cave sounds to the dungeon entrance." |
| ✓ | "Create a reverb_zone at [10, 2, 0] with preset 'dungeon', min_distance 4m, max_distance 18m, parented to /World/Audio." |

| | |
|:---:|:---|
| ❌ | "Put some water sounds near the river." |
| ✓ | "Create an ambient_source at [80, 0, 60], audio_clip_path 'Assets/Audio/Ambient/River_Loop.wav', min_distance 3m, max_distance 20m, volume 0.65." |

---

### `create_postprocessing_volume`
Creates a URP Volume with a VolumeProfile containing specified effect overrides. Use for biome transitions and area-specific visual identity.

| | |
|:---:|:---|
| ❌ | "Make the dungeon look darker." |
| ✓ | "Create a local post-processing volume at [20, 5, 30], size [40, 12, 40], blend_distance 8, priority 1, profile_name 'Dungeon_Zone1': ColorAdjustments with saturation -30 and postExposure -0.4, Vignette intensity 0.35." |

| | |
|:---:|:---|
| ❌ | "Add some bloom to the scene." |
| ✓ | "Create a global post-processing volume, priority 0, profile_name 'SceneBase': Tonemapping mode ACES, ColorAdjustments contrast 8 and saturation 5, Bloom intensity 0.2 and threshold 1.1." |

*Use `is_global=true` for scene-wide baseline settings. Use local volumes with blend_distance for area-specific transitions.*

---

## General Principles

**Always discover before acting.** `get_scene_context` → `get_prefab_catalog` → `get_scene_hierarchy` at the start of a session. Skipping this is the single most common source of errors.

**Give positions, not descriptions.** "Near the entrance" means nothing. "[12, 0, 8]" is unambiguous. If you don't have exact coordinates, ask Claude to read the hierarchy and report back with positions before it places anything.

**Use proposals for anything you'd want to undo easily.** Not just bulk placements — also any change to an area you care about. The Operation Queue window is there for a reason.

**Dry-run before deletes.** The `delete_object` tool removes an object and all its children. Always pass `dry_run=true` first when deleting anything that might contain important sub-objects.

**Bake the navmesh after layout changes.** NavMesh data goes stale the moment you place or move obstacle geometry. Ending a session with `bake_navmesh(all=True)` is a good habit.

**Finish with validation.** `validate_scene(checks=["missing_scripts", "missing_materials", "untagged_triggers"])` takes seconds and catches the most common mistakes before they become problems.
