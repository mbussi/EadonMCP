# Unity MCP Bridge — Architecture Document

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [High-Level Architecture](#2-high-level-architecture)
3. [Component: Python MCP Server](#3-component-python-mcp-server)
4. [Component: Unity Editor Plugin](#4-component-unity-editor-plugin)
5. [Inter-Component Communication](#5-inter-component-communication)
6. [MCP Tool Catalog](#6-mcp-tool-catalog)
7. [Metadata System](#7-metadata-system)
8. [Proposal and Validation Workflow](#8-proposal-and-validation-workflow)
9. [Threading Model](#9-threading-model)
10. [Testing Strategy](#10-testing-strategy)
11. [Client Configuration](#11-client-configuration)
12. [Future Considerations](#12-future-considerations)
13. [Project Structure](#13-project-structure)
14. [Development Roadmap](#14-development-roadmap)

---

## 1. Project Overview

**Unity MCP Bridge** is a developer tool that exposes Unity Editor functionality to AI assistants (primarily Claude via Claude Desktop or Claude Code) through the Model Context Protocol (MCP). It enables AI-assisted level design workflows including prefab placement, lighting setup, scene inspection, and procedural generation.

### Goals

- Allow an AI agent to read scene state and project assets with full context
- Allow an AI agent to propose and execute editor operations (placement, transforms, lighting)
- Support a human-in-the-loop validation step before changes are applied to the scene
- Remain fully reversible: all AI-driven operations are registered with Unity's Undo system
- Be extensible: new tools can be added without changing the architecture

### Non-Goals (current scope)

- Runtime (Play Mode) interaction
- Cloud-hosted or multi-user operation
- Direct manipulation of C# scripts or asset source files

---

## 2. High-Level Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  AI Client (Claude Desktop / Claude Code)                        │
│                                                                  │
│   User prompt ──▶ Claude ──▶ MCP tool calls ──▶ Claude          │
└────────────────────────────┬─────────────────────────────────────┘
                             │ MCP Protocol (stdio)
                             │
┌────────────────────────────▼─────────────────────────────────────┐
│  Python MCP Server  (runs as subprocess of AI client)            │
│                                                                  │
│   Tool definitions ──▶ HTTP request builder ──▶ Response parser  │
└────────────────────────────┬─────────────────────────────────────┘
                             │ HTTP/JSON  (localhost)
                             │
┌────────────────────────────▼─────────────────────────────────────┐
│  Unity Editor Plugin  (C#, Editor-only assembly)                 │
│                                                                  │
│   HttpListener ──▶ MainThreadDispatcher ──▶ Tool Handlers        │
│                                          ──▶ OperationQueue UI   │
│                                          ──▶ Unity Editor API    │
└──────────────────────────────────────────────────────────────────┘
```

### Component responsibilities at a glance

| Component | Language | Responsibility |
|---|---|---|
| Python MCP Server | Python | MCP protocol, tool schema declarations, HTTP client to Unity |
| Unity Editor Plugin | C# | HTTP listener, main-thread dispatch, all Unity API calls, operation queue UI |

The Python server is intentionally thin. It knows nothing about Unity internals; it only translates MCP calls to HTTP and relays responses. All domain logic lives in C# where the Unity API is available and where the author has expertise.

---

## 3. Component: Python MCP Server

### Responsibilities

- Declare MCP tools (names, descriptions, parameter schemas) that the AI client discovers on startup
- Receive tool call requests from the AI client via stdio (MCP protocol)
- Forward requests as HTTP JSON calls to the Unity plugin
- Parse and return responses; translate Unity-side errors into MCP errors

### Technology

- **Language:** Python 3.11+
- **MCP SDK:** `mcp` (Anthropic's official Python SDK)
- **HTTP client:** `httpx` (async-friendly, clean API)
- **Testing:** `pytest` + `pytest-asyncio`
- **Package management:** `uv` (recommended) or `pip` with `pyproject.toml`

### Structure

```
mcp-server/
├── src/
│   └── unity_mcp/
│       ├── server.py          # MCP server entry point, tool registration
│       ├── unity_client.py    # Async HTTP client for Unity plugin
│       ├── models.py          # Pydantic models for requests/responses
│       └── tools/
│           ├── scene.py       # Scene query tools
│           ├── placement.py   # Prefab/object placement tools
│           ├── lighting.py    # Light creation and setup tools
│           ├── procedural.py  # Bulk and generative tools
│           └── proposal.py    # Proposal and dry-run tools
├── tests/
│   ├── conftest.py
│   ├── test_scene.py
│   ├── test_placement.py
│   ├── test_lighting.py
│   └── test_unity_client.py
└── pyproject.toml
```

### Key design principles

**Tools are thin wrappers.** Each tool function validates parameters, calls `unity_client`, and returns the result. Logic and validation beyond parameter types belongs in C#.

**Rich tool descriptions.** The `description` field of every tool is the AI's primary guide for when and how to use it. Descriptions must be precise about coordinate space, expected value ranges, and side effects.

**Consistent error surface.** `unity_client.py` maps HTTP error codes and Unity exception messages to typed Python exceptions, which the tool layer converts into MCP error responses with actionable messages.

### Tool registration pattern

```python
# Example: server.py
from mcp.server import Server
from mcp.server.stdio import stdio_server
from unity_mcp.tools.placement import register_placement_tools

app = Server("unity-mcp")
register_placement_tools(app)
# ... register other tool groups

async def main():
    async with stdio_server() as streams:
        await app.run(*streams)
```

```python
# Example: tools/placement.py
from mcp.server import Server
from unity_mcp.unity_client import UnityClient
from unity_mcp.models import PlacePrefabRequest

def register_placement_tools(server: Server):

    @server.tool()
    async def place_prefab(
        prefab_path: str,
        position: list[float],
        rotation: list[float] = [0, 0, 0],
        scale: list[float] = [1, 1, 1],
        parent_path: str | None = None,
        dry_run: bool = False
    ) -> dict:
        """
        Place a prefab instance in the current scene.

        prefab_path: AssetDatabase path, e.g. "Assets/Prefabs/City/Townhouse_A.prefab"
        position: World-space [x, y, z]
        rotation: Euler angles in degrees [x, y, z]
        scale: Local scale [x, y, z]
        parent_path: Scene hierarchy path of the parent object, if any
        dry_run: If true, validates and returns the operation without executing it
        """
        client = UnityClient.instance()
        request = PlacePrefabRequest(
            prefab_path=prefab_path,
            position=position,
            rotation=rotation,
            scale=scale,
            parent_path=parent_path,
            dry_run=dry_run
        )
        return await client.post("/tools/placement/place_prefab", request)
```

---

## 4. Component: Unity Editor Plugin

### Responsibilities

- Start and stop an HTTP listener when the Unity Editor opens/closes
- Receive JSON requests on a background thread, dispatch them to the main thread
- Execute tool handler logic using the Unity Editor API
- Register all mutating operations with the Undo system
- Provide an EditorWindow for the operation approval queue
- Expose project metadata (prefab catalog, scene context) for AI consumption

### Technology

- **Language:** C# (.NET Standard 2.1, Unity 2021.3 LTS or later)
- **HTTP:** `System.Net.HttpListener`
- **Serialization:** `Newtonsoft.Json` (via Unity package `com.unity.nuget.newtonsoft-json`)
- **Threading:** `System.Threading.Tasks`, `SynchronizationContext`
- **Testing:** Unity Test Runner (NUnit, EditMode assembly)

### Structure

```
Assets/
└── UnityMcpBridge/
    ├── Editor/
    │   ├── Core/
    │   │   ├── McpHttpListener.cs        # HttpListener lifecycle
    │   │   ├── MainThreadDispatcher.cs   # Background → main thread queue
    │   │   ├── McpRouter.cs              # Maps HTTP routes to handlers
    │   │   └── McpBridgeSettings.cs      # ScriptableObject: port, options
    │   ├── Tools/
    │   │   ├── SceneQueryHandler.cs      # Read-only scene inspection
    │   │   ├── PlacementHandler.cs       # Prefab and object placement
    │   │   ├── LightingHandler.cs        # Light creation and config
    │   │   ├── ProceduralHandler.cs      # Bulk/scatter/path operations
    │   │   └── ProposalHandler.cs        # Dry-run and proposal tools
    │   ├── Metadata/
    │   │   ├── PrefabMetadata.cs         # ScriptableObject per prefab
    │   │   ├── SceneContext.cs           # ScriptableObject: level setting
    │   │   └── MetadataRegistry.cs       # Aggregates and queries metadata
    │   └── UI/
    │       ├── McpOperationQueueWindow.cs # EditorWindow: approval UI
    │       └── McpStatusBar.cs           # Editor toolbar: server status
    └── Tests/
        └── EditMode/
            ├── PlacementHandlerTests.cs
            ├── LightingHandlerTests.cs
            ├── MetadataRegistryTests.cs
            └── MainThreadDispatcherTests.cs
```

### Startup lifecycle

```csharp
[InitializeOnLoad]
public static class McpHttpListener
{
    static McpHttpListener()
    {
        // Called every time Unity recompiles or opens the Editor
        EditorApplication.quitting += Stop;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        Start();
    }

    private static void Start() { /* begin HttpListener on configured port */ }
    private static void Stop() { /* dispose listener cleanly */ }
}
```

### Request handling pattern

Every incoming HTTP request follows the same pipeline:

```
[Background Thread]                    [Main Thread]
  HttpListener.GetContext()
       │
       ▼
  Parse JSON body into request DTO
       │
       ▼
  MainThreadDispatcher.Enqueue(work)
       │                                   │
       │ ◀── awaits TaskCompletionSource ──▶│  EditorApplication.update fires
                                            │  Dequeue and execute work item
                                            │  Call handler method
                                            │  Set TaskCompletionSource result
       │
       ▼
  Serialize response DTO to JSON
       │
       ▼
  Write HTTP response
```

---

## 5. Inter-Component Communication

### Protocol

Plain HTTP/JSON over localhost. The Python server is always the client; Unity is always the server.

### Authentication

Every request must carry `Authorization: Bearer <token>`. Unity writes the token to `~/.unity-mcp/bridge-token` the first time the bridge starts; it is one token per machine, shared by every Unity project on it. The Python server reads that file, or `UNITY_MCP_TOKEN` when Unity runs on another machine. The in-editor agent reads it directly.

The bridge also refuses any request with an `Origin` header (it comes from a web page, never from an MCP client) and any POST that isn't `application/json`. Without these checks, any web page open in a browser on the same machine could write a C# file into `Assets/` and trigger a refresh.

The listener binds to `localhost` unless **Allow Remote Connections** is ticked in **Project Settings > Eadon > MCP Bridge** (see `REMOTE_SETUP.md`).

### Base URL

`http://localhost:{port}` — default port `11000`, configurable in **Project Settings > Eadon > MCP Bridge**.

### Request/Response envelope

All requests are HTTP POST with `Content-Type: application/json`.

**Request body:**
```json
{
  "dry_run": false,
  "params": { }
}
```

**Success response (HTTP 200):**
```json
{
  "success": true,
  "data": { }
}
```

**Error response (HTTP 400 / 500):**
```json
{
  "success": false,
  "error": {
    "code": "PREFAB_NOT_FOUND",
    "message": "No prefab found at path: Assets/Prefabs/Missing.prefab"
  }
}
```

**Still running (HTTP 202):** a request that has not finished after 20 s is handed off as a job (see §9, Long requests and jobs):
```json
{
  "success": true,
  "pending": true,
  "data": { "job_id": "job-12-a1b2c3", "tool": "place_prefab_batch", "state": "running" }
}
```

### Route map

| HTTP route | Handler class | Description |
|---|---|---|
| `GET /status` | `McpRouter` | Ping; returns Unity version and open scene name |
| `GET /scene/hierarchy` | `SceneQueryHandler` | Full scene object tree |
| `GET /scene/context` | `SceneQueryHandler` | Active `SceneContext` metadata |
| `GET /assets/prefabs` | `SceneQueryHandler` | Prefab catalog with metadata |
| `GET /assets/materials` | `SceneQueryHandler` | Materials list with paths |
| `POST /tools/placement/place_prefab` | `PlacementHandler` | Place a prefab instance |
| `POST /tools/placement/set_transform` | `PlacementHandler` | Move/rotate/scale an object |
| `POST /tools/placement/delete_object` | `PlacementHandler` | Delete a scene object |
| `POST /tools/placement/create_empty` | `PlacementHandler` | Create an empty GameObject |
| `POST /tools/placement/place_batch` | `PlacementHandler` | Place multiple prefabs in one call |
| `POST /tools/placement/scatter` | `ProceduralHandler` | Scatter prefabs in an area |
| `POST /tools/placement/along_path` | `ProceduralHandler` | Place prefabs along a polyline |
| `POST /tools/lighting/create_light` | `LightingHandler` | Create a light source |
| `POST /tools/lighting/set_ambient` | `LightingHandler` | Configure ambient/skybox lighting |
| `POST /tools/lighting/set_fog` | `LightingHandler` | Configure fog settings |
| `POST /tools/proposal/submit` | `ProposalHandler` | Push a proposal to the queue window |
| `POST /tools/proposal/execute` | `ProposalHandler` | Execute an approved proposal |
| `GET /tools/catalog` | `McpToolCatalog` | Every tool the bridge offers (`source=all\|builtin\|extensions`, `detail=summary\|full`) |
| `GET /tools/describe` | `McpToolCatalog` | One tool's full guide and argument schema (`name=…`) |
| `GET /jobs` | `McpJobStore` | One job with its result (`job_id=…`), or the recent jobs |
| `POST /jobs/cancel` | `McpJobStore` | Cancel a queued job, or stop a light bake: `{ "job_id": … }` |
| `POST /tools/call` | `McpToolRegistry` | Run an extension tool by name: `{ "name": …, "arguments": { … } }` |

Extensions add routes of their own through `McpRouteRegistry`, usually by registering a tool with `McpToolRegistry` (see §6).

---

## 6. MCP Tool Catalog

### Summaries, full guides and extension tools

Every tool has two descriptions: a **summary** (the first paragraph of its docstring) and a **full guide** (the whole docstring, with examples and option lists). The summary has to be enough to pick the tool; the guide is there when the details matter.

- **Built-in tools** are described once, in the Python server. `server/scripts/export_tool_catalog.py` exports their docstrings and argument schemas to `Editor/Catalog/builtin-tools.json` in the package, and `check_contract.py` fails when that file is out of date.
- **Extension tools** are registered inside Unity with `McpToolRegistry` (name, summary, guide, schema, route, handler). The PCG tools in `extensions/pcg/` are the reference.
- `McpToolCatalog` merges both and serves them on `GET /tools/catalog` and `GET /tools/describe`.

Who sees what:

| Client | Tool list | Full guide |
|---|---|---|
| MCP clients (Claude Desktop, Claude Code) | Built-in tools with their full docstrings, plus `list_unity_tools` and `call_unity_tool` | `list_unity_tools(name)` for extension tools |
| In-editor agent | Every tool, built-in and extension, as a summary ending in "Full guide with examples: describe_tool(\"name\")" | `describe_tool(name)` |

The agent's **Full Tool Descriptions** setting sends the full guides instead of the summaries (about 35k tokens per request instead of 8k).

Extension tools work everywhere a built-in tool does: the agent offers them directly, MCP clients reach them through `call_unity_tool`, and `propose_changes` operations can name them. The agent and `call_unity_tool` send arguments exactly as the schema names them, so the request types read those names.

### Scene Inspection (read-only)

| Tool | Description |
|---|---|
| `get_scene_hierarchy` | Returns the full object tree of the active scene, including names, hierarchy paths, component types, and transforms. The AI should call this before any placement to understand what already exists. |
| `get_scene_context` | Returns the active `SceneContext` metadata: level name, setting (city, dungeon, forest…), style, and any AI-facing notes the author has written. |
| `get_prefab_catalog` | Returns all prefabs that have a `PrefabMetadata` asset, including tags, categories, descriptions, and placement rules. The AI uses this to choose appropriate prefabs for the current context. |
| `get_assets` | Returns materials, textures, or other asset types matching a filter. Useful for assigning materials during placement. |
| `find_objects` | Query scene objects by name pattern, tag, component type, or spatial bounds. |

### Placement

| Tool | Description |
|---|---|
| `place_prefab` | Instantiate a prefab at a world-space position with given rotation and scale. Optionally parent to an existing scene object. Supports `dry_run`. |
| `place_prefab_batch` | Place multiple prefabs in a single round-trip. Accepts an array of placement descriptors. Essential for procedural generation to avoid per-object latency. |
| `set_transform` | Set position, rotation, and/or scale of an existing scene object by hierarchy path. |
| `create_empty` | Create an empty GameObject, optionally parented. Useful for organizational containers. |
| `delete_object` | Delete a scene object by hierarchy path. Registered with Undo. |

### Procedural Placement

| Tool | Description |
|---|---|
| `scatter_prefabs` | Distribute N instances of a prefab randomly within a defined area (box or circle), with optional surface alignment and minimum spacing. |
| `place_along_path` | Place prefabs at regular intervals along a polyline defined by waypoints. Useful for walls, fences, roads, and corridors. |
| `fill_grid` | Fill a rectangular grid area with a prefab at defined cell spacing. |

### Lighting

| Tool | Description |
|---|---|
| `create_light` | Create a light source (Directional, Point, Spot, Area) with specified position, rotation, colour, intensity, range, and shadow mode. |
| `set_ambient_light` | Configure ambient lighting mode (flat colour, gradient, or skybox) and colour values. |
| `set_skybox` | Assign a skybox material to the scene. |
| `set_fog` | Enable/disable fog and configure colour, mode, and density. |
| `bake_lighting` | Start a background lighting bake and return its job ID at once (`get_job_status` follows it, `cancel_job` stops it), or clear baked data. |

### Proposal and Validation

| Tool | Description |
|---|---|
| `propose_changes` | Submit a structured list of proposed operations to the Unity operation queue for human review. Does not execute anything. Returns a proposal ID. |
| `execute_proposal` | Execute a previously submitted and approved proposal by ID. |
| `discard_proposal` | Discard a pending proposal without executing. |

---

## 7. Metadata System

The metadata system gives the AI rich, author-defined context to reason about prefabs and scenes. It is entirely opt-in: the bridge works without it, but quality of AI decisions degrades significantly.

### PrefabMetadata ScriptableObject

```csharp
[CreateAssetMenu(menuName = "MCP Bridge/Prefab Metadata")]
public class PrefabMetadata : ScriptableObject
{
    [Header("Identity")]
    public GameObject prefab;
    public string displayName;

    [Header("Classification")]
    public string category;           // "building", "prop", "vegetation", "light-fixture"
    public string[] tags;             // "urban", "medieval", "ruined", "indoor", etc.

    [Header("Context")]
    [TextArea(3, 8)]
    public string description;        // Free text for the AI. Be specific and evocative.
                                      // Example: "A narrow 3-storey brick townhouse. Suits
                                      // densely packed 19th-century European city streets."
    public string[] compatibleWith;   // Tags of assets this pairs well with
    public string[] incompatibleWith; // Tags this clashes with ("farm", "futuristic", etc.)

    [Header("Placement Rules")]
    public PlacementRules placement;

    [Header("Scale Reference")]
    public Vector3 approximateSize;   // Bounding box in world units, for spacing decisions
}

[Serializable]
public class PlacementRules
{
    public bool requiresFlatGround = true;
    public bool canStack = false;
    public bool indoorOnly = false;
    public bool outdoorOnly = false;
    public float minimumSpacing = 0f;    // Metres between instances
    public Vector3 recommendedSpacing;   // Per-axis preferred spacing
}
```

Metadata assets are stored alongside their prefabs by convention:
```
Assets/Prefabs/City/Townhouse_A.prefab
Assets/Prefabs/City/Townhouse_A.metadata.asset
```

### SceneContext ScriptableObject

One per level (or scene). Referenced from the bridge settings (**Project Settings > Eadon > MCP Bridge**) or auto-discovered by name convention.

```csharp
[CreateAssetMenu(menuName = "MCP Bridge/Scene Context")]
public class SceneContext : ScriptableObject
{
    public string levelName;
    public string setting;           // "city", "dungeon", "forest", "spaceship", etc.
    public string architecturalStyle;// "19th century European", "brutalist", "gothic", etc.
    public string era;               // "medieval", "contemporary", "far future", etc.
    public string[] activeTags;      // Tags the AI should prefer when selecting prefabs
    public string[] excludedTags;    // Tags the AI must not use in this scene

    [TextArea(3, 10)]
    public string notes;             // Freeform design intent, constraints, and tone.
                                     // The AI reads this to anchor all its decisions.
}
```

### MetadataRegistry

A static editor class that loads all `PrefabMetadata` assets via `AssetDatabase.FindAssets` and exposes query methods used by tool handlers and the `get_prefab_catalog` endpoint.

```csharp
public static class MetadataRegistry
{
    public static IReadOnlyList<PrefabMetadata> All { get; }
    public static IReadOnlyList<PrefabMetadata> WithTag(string tag);
    public static IReadOnlyList<PrefabMetadata> CompatibleWith(SceneContext context);
    public static PrefabMetadata ForPrefab(string assetPath);
    public static void Reload(); // Called after asset import
}
```

---

## 8. Proposal and Validation Workflow

Human review before execution is a first-class feature, not an afterthought. Three layers work together and can be used independently or in combination.

### Layer 1: MCP Client-Side Approval

Both Claude Desktop and Claude Code support per-tool approval prompts. For high-risk tools (`delete_object`, `place_prefab_batch`, procedural tools), the tool description should note that they are mutating operations. The user can configure the MCP client to require confirmation before any tool call.

**No implementation required.** This is configured in the AI client, not the server.

### Layer 2: Conversational Proposal via `propose_changes`

The AI calls `propose_changes` with a structured operation list before executing anything substantial. It presents the plan to the user in the conversation. The user responds to approve, reject, or modify. Only then does the AI call `execute_proposal` or individual tools.

Example AI-generated proposal payload:

```json
{
  "proposal_id": "prop_20240615_001",
  "summary": "Setting up a residential street corner with three townhouses and two lamp posts",
  "operations": [
    {
      "id": 1,
      "tool": "place_prefab",
      "description": "Place Townhouse_A at (10, 0, 5), facing east",
      "params": { "prefab_path": "Assets/Prefabs/City/Townhouse_A.prefab", "position": [10, 0, 5], "rotation": [0, 90, 0] }
    },
    {
      "id": 2,
      "tool": "place_prefab",
      "description": "Place Townhouse_B at (16, 0, 5), facing east",
      "params": { "prefab_path": "Assets/Prefabs/City/Townhouse_B.prefab", "position": [16, 0, 5], "rotation": [0, 90, 0] }
    },
    {
      "id": 3,
      "tool": "create_light",
      "description": "Add warm street lamp at corner (10, 0, 0), point light, intensity 1.2",
      "params": { "type": "Point", "position": [10, 3.5, 0], "color": [1.0, 0.85, 0.6], "intensity": 1.2 }
    }
  ]
}
```

The user can respond: *"Do operations 1 and 3, skip 2 — I'll place the second building manually."*

### Layer 3: Unity Operation Queue Window

For bulk operations (procedural generation, batch placement), an `EditorWindow` provides in-editor visibility and granular control.

**Behaviour:**

- `propose_changes` pushes the operation list to the queue window and returns immediately
- The window displays each operation with its human-readable description
- The user can approve, edit parameters, or reject individual operations
- "Execute Selected" / "Execute All" / "Discard All" buttons trigger `execute_proposal`
- Executed operations are grouped under a single Undo entry labelled with the proposal summary
- The window shows a ghost preview option (where feasible) using `Handles` drawing in SceneView

**Implementation note:** The operation queue is stored in memory (no serialization needed for this phase). It is not preserved across domain reloads; any pending proposals are cleared on recompile.

### `dry_run` flag

Every mutating tool accepts an optional `dry_run: bool` parameter. When `true`, the Unity handler:

1. Validates all parameters (path exists, position is finite, parent path is valid, etc.)
2. Computes what the result would be (final transform, generated object name, etc.)
3. Returns the expected result without touching the scene

This enables the AI to validate a plan incrementally before committing, and supports unit testing of handler logic without scene side effects.

---

## 9. Threading Model

Unity's Editor API is not thread-safe. All calls to `UnityEngine` and `UnityEditor` namespaces must occur on the main thread. `HttpListener` runs callbacks on thread-pool threads. Bridging these is the central threading concern of the plugin.

### Main thread dispatch

```csharp
public class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> _queue = new();

    // Called from EditorApplication.update — runs on main thread
    public static void Update()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }

    // Called from any thread
    public static Task<T> EnqueueAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Enqueue(() =>
        {
            try   { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
```

The `HttpListener` background thread enqueues work and `await`s the `TaskCompletionSource`. The main thread drains the queue on each `EditorApplication.update` tick. This gives a maximum latency of one editor frame (~16ms at 60fps) for any operation.

### Long requests and jobs

Every request is followed by an `McpJob`, from arrival to result. `McpHttpListener` makes it the current job (`McpJob.Current`, an `AsyncLocal`) while the request is routed, and `MainThreadDispatcher` attaches each queued work item to the current job. The job is **queued** until Unity starts the work, then **running**, then **done**, **failed** or **cancelled**.

If a request has not finished after `McpHttpListener.HandoffSeconds` (20 s), the listener answers HTTP 202 with `"pending": true` and the job, puts the job in `McpJobStore`, and lets the work carry on. The listener's threads do not wait for the main thread, so this answer comes even while Unity is frozen by a long operation, a compile or a dialog.

- **Python server:** `UnityClient` sees the pending answer and polls `GET /jobs?job_id=…` every second. If the job finishes within 45 s, the tool returns the result as if the request had answered directly. Otherwise it raises `UnityJobPending` (code `STILL_RUNNING`), whose message gives the job ID, points to `get_job_status` and warns not to repeat the request.
- **In-editor agent:** `AgentLoop` polls the job until it finishes, showing "Waiting for Unity…". Cancelling the chat cancels the job if it is still queued.
- **Cancelling:** a queued job is cancelled at once and its work is skipped. A running job is refused (Unity cannot stop an operation safely halfway) unless it registered a stop with `AllowCancelWhileRunning`, as light bakes do.
- **Background jobs:** an operation that continues after its request returns creates its own job with `McpJob.StartBackground` and completes it itself. `bake_lighting` does this: it starts `Lightmapping.BakeAsync`, returns the job ID, reports `Lightmapping.buildProgress` on the job, and completes it when the bake finishes, fails or is cancelled.
- **Retention:** the store keeps the last 50 jobs; when full, the oldest finished job goes first, so a running job is never dropped. It lives in memory, so a script reload clears it.

Every transport failure in the Python client (timeout, dropped connection) becomes a `UnityConnectionError` that says the request may already have run.

### Domain reload safety

Unity reloads the C# domain on every script recompile. The listener must be torn down cleanly before this happens and restarted after. `AssemblyReloadEvents.beforeAssemblyReload` and `[InitializeOnLoad]` handle this.

Static state (the operation queue, pending proposals, jobs) is cleared on domain reload. This is acceptable for an editor tool; any pending proposals should be resolved before recompiling. `get_job_status` for a job lost this way answers `JOB_NOT_FOUND` and says the job may or may not have run.

---

## 10. Testing Strategy

### Python MCP Server

**Framework:** `pytest` + `pytest-asyncio`

**Approach:** Mock `UnityClient` at the boundary. Test that each tool correctly serializes parameters into the expected HTTP request body and correctly deserializes a mocked Unity response. Do not test the MCP SDK itself.

```python
# tests/test_placement.py
import pytest
from unittest.mock import AsyncMock, patch
from unity_mcp.tools.placement import place_prefab

@pytest.mark.asyncio
async def test_place_prefab_sends_correct_request():
    mock_response = {"success": True, "data": {"instance_path": "/Environment/Townhouse_A"}}
    with patch("unity_mcp.tools.placement.UnityClient.post", new=AsyncMock(return_value=mock_response)) as mock_post:
        result = await place_prefab(
            prefab_path="Assets/Prefabs/City/Townhouse_A.prefab",
            position=[10.0, 0.0, 5.0]
        )
        mock_post.assert_called_once()
        call_body = mock_post.call_args[0][1]
        assert call_body.prefab_path == "Assets/Prefabs/City/Townhouse_A.prefab"
        assert call_body.position == [10.0, 0.0, 5.0]
        assert result["data"]["instance_path"] == "/Environment/Townhouse_A"
```

**What to test:**
- Correct HTTP route is targeted for each tool
- Parameters are serialized as expected (including defaults)
- `dry_run=True` is forwarded correctly
- Unity error responses are surfaced as MCP errors, not silent failures
- Edge cases: empty scene hierarchy, missing prefab paths, out-of-range values

### Unity Editor Plugin

**Framework:** Unity Test Runner, NUnit, EditMode assembly

**Approach:** Test handler logic directly, bypassing HTTP. Instantiate handlers with mock or real `AssetDatabase` data. Use `dry_run` mode extensively to test logic without scene side effects.

```csharp
// Tests/EditMode/PlacementHandlerTests.cs
[TestFixture]
public class PlacementHandlerTests
{
    private PlacementHandler _handler;

    [SetUp]
    public void SetUp() => _handler = new PlacementHandler();

    [Test]
    public void PlacePrefab_WithDryRun_DoesNotModifyScene()
    {
        var request = new PlacePrefabRequest
        {
            PrefabPath = "Assets/Tests/TestCube.prefab",
            Position = new float[] { 0, 0, 0 },
            DryRun = true
        };
        int objectsBefore = SceneManager.GetActiveScene().rootCount;

        var response = _handler.PlacePrefab(request);

        Assert.IsTrue(response.Success);
        Assert.AreEqual(objectsBefore, SceneManager.GetActiveScene().rootCount);
    }

    [Test]
    public void PlacePrefab_WithInvalidPath_ReturnsError()
    {
        var request = new PlacePrefabRequest
        {
            PrefabPath = "Assets/Does/Not/Exist.prefab",
            Position = new float[] { 0, 0, 0 }
        };

        var response = _handler.PlacePrefab(request);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("PREFAB_NOT_FOUND", response.Error.Code);
    }
}
```

**What to test:**
- Handler logic for all tool categories
- Metadata registry queries (tag filtering, context compatibility)
- Main thread dispatcher (enqueue, execute, result propagation)
- Error code mapping for all expected failure cases
- `dry_run` parity: same validation path as live execution

**Test scene setup:** Maintain a dedicated test scene asset (`Assets/UnityMcpBridge/Tests/TestScene.unity`) with a known, minimal hierarchy. Tests that need scene state should load this scene in `[SetUp]` and unload it in `[TearDown]`.

---

## 11. Client Configuration

### Claude Desktop

Add the server to `~/.config/claude/claude_desktop_config.json` (macOS/Linux) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "uv",
      "args": ["run", "--project", "/path/to/unity-mcp/mcp-server", "unity-mcp"],
      "env": {
        "UNITY_MCP_PORT": "11000"
      }
    }
  }
}
```

The server launches as a subprocess on Claude Desktop startup and communicates over stdio. The environment variable `UNITY_MCP_PORT` must match the port configured in Unity's `McpBridgeSettings`.

### Claude Code

Claude Code discovers MCP servers from a `.mcp.json` file in the workspace root. Since `.mcp.json` lives at the Unity project root, the path to the MCP server is relative to that location:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "uv",
      "args": ["run", "--project", "../../mcp-server", "unity-mcp"],
      "env": {
        "UNITY_MCP_PORT": "11000"
      }
    }
  }
}
```

When Claude Code is opened with the Unity project as the workspace root, the MCP server is discovered and started automatically. The relative path `../../mcp-server` navigates from `UnityProject/` up to the repo root and into `mcp-server/` — adjust if your repository layout differs.

### McpBridgeSettings (Unity)

A project setting, stored in `ProjectSettings/EadonMcpBridgeSettings.asset` and edited in **Project Settings > Eadon > MCP Bridge**. It lives under ProjectSettings, not Assets, because the bridge is a package and these values belong to the project:

```csharp
[FilePath("ProjectSettings/EadonMcpBridgeSettings.asset", FilePathAttribute.Location.ProjectFolder)]
public class McpBridgeSettings : ScriptableSingleton<McpBridgeSettings>
{
    public int  port = 11000;
    public bool allowRemoteConnections = false;
    public bool startOnLoad = true;
    public SceneContext sceneContext;
    public List<ZoneContextEntry> zoneContexts;
}
```

---

## 12. Future Considerations

### Local Agent Frameworks

The architecture is designed to accommodate local agent runtimes (LM Studio, Ollama + an agent harness, CrewAI, etc.) without changes to the Unity plugin. The HTTP interface between Python and Unity is the stable contract; only the transport from agent to Python changes.

For local agents that do not use the MCP protocol natively, two options exist:

**Option A: HTTP directly to Unity**
Bypass the Python server entirely. The Unity plugin already speaks plain HTTP/JSON, so any agent that can make HTTP calls can drive it directly. This is the simplest path.

**Option B: SSE transport in the Python server**
Replace stdio transport with SSE (Server-Sent Events) transport. The Python `mcp` SDK supports this. The MCP server then accepts HTTP connections from any MCP-compatible client, including self-hosted agent frameworks. Enable this via a configuration flag (`UNITY_MCP_TRANSPORT=sse`).

**What to design for now:**
- Keep tool handler logic stateless where possible
- Do not assume a single connected client
- Use the `proposal_id` pattern (UUIDs, not sequential integers) to avoid collisions if multiple clients are ever connected

### Extending the Tool Set

New tools follow the same pattern:

1. Add a route constant to `McpRouter`
2. Add a handler method to an existing handler class (or create a new one for a new domain)
3. Add a corresponding tool function in the Python server's `tools/` directory
4. Add tests on both sides

No changes to the HTTP listener, dispatcher, or server entry point are required.

### Undo Grouping

Currently, each operation creates its own Undo entry. For proposal-based workflows, it is preferable to group all operations from one proposal under a single Undo step. This is achievable with `Undo.IncrementCurrentGroup()` and `Undo.SetCurrentGroupName()` and should be implemented in `execute_proposal`.

### Scene Snapshot / Diffing

A future enhancement is a `get_scene_snapshot` tool that returns a compact, serialisable representation of the scene, and a `diff_scenes` tool that computes what changed between two snapshots. This would allow the AI to verify its own changes and detect unexpected side effects.

---

## 13. Project Structure

```
unity-mcp-bridge/
│
├── README.md
├── ARCHITECTURE.md                    # This document
│
├── mcp-server/                        # Python MCP server
│   ├── src/
│   │   └── unity_mcp/
│   │       ├── __init__.py
│   │       ├── server.py
│   │       ├── unity_client.py
│   │       ├── models.py
│   │       └── tools/
│   │           ├── scene.py
│   │           ├── placement.py
│   │           ├── lighting.py
│   │           ├── procedural.py
│   │           └── proposal.py
│   ├── tests/
│   │   ├── conftest.py
│   │   ├── test_scene.py
│   │   ├── test_placement.py
│   │   ├── test_lighting.py
│   │   └── test_unity_client.py
│   └── pyproject.toml
│
└── UnityProject/                      # Your Unity project root
    ├── .mcp.json                      # Claude Code MCP server config (project root)
    └── Assets/
        └── UnityMcpBridge/
            ├── Editor/
            │   ├── Core/
            │   │   ├── McpHttpListener.cs
            │   │   ├── MainThreadDispatcher.cs
            │   │   ├── McpRouter.cs
            │   │   └── McpBridgeSettings.cs
            │   ├── Tools/
            │   │   ├── SceneQueryHandler.cs
            │   │   ├── PlacementHandler.cs
            │   │   ├── LightingHandler.cs
            │   │   ├── ProceduralHandler.cs
            │   │   └── ProposalHandler.cs
            │   ├── Metadata/
            │   │   ├── PrefabMetadata.cs
            │   │   ├── SceneContext.cs
            │   │   └── MetadataRegistry.cs
            │   └── UI/
            │       ├── McpOperationQueueWindow.cs
            │       └── McpStatusBar.cs
            ├── Tests/
            │   └── EditMode/
            │       ├── PlacementHandlerTests.cs
            │       ├── LightingHandlerTests.cs
            │       ├── MetadataRegistryTests.cs
            │       └── MainThreadDispatcherTests.cs
            └── McpBridgeSettings.asset
```

---

## 14. Development Roadmap

### Phase 1 — Scaffold (prove the connection)

**Goal:** One tool call, end-to-end, working in Claude Desktop.

- [ ] Unity: `McpHttpListener` starts on Editor load, responds to `GET /status`
- [ ] Unity: `MainThreadDispatcher` wired to `EditorApplication.update`
- [ ] Python: MCP server with one tool: `ping_unity` — calls `/status` and returns scene name
- [ ] Claude Desktop: configured, server appears in tools list, `ping_unity` works
- [ ] Tests: `MainThreadDispatcher` enqueue/dequeue, `ping` handler, Python client mock

### Phase 2 — Scene inspection

**Goal:** The AI can read the scene before acting.

- [ ] `get_scene_hierarchy` — full object tree with transforms
- [ ] `get_scene_context` — active `SceneContext` metadata
- [ ] `get_prefab_catalog` — `PrefabMetadata` registry, `MetadataRegistry.All`
- [ ] `SceneContext` and `PrefabMetadata` ScriptableObjects with Inspector UI
- [ ] Tests: hierarchy serialization, metadata filtering, context compatibility query

### Phase 3 — Basic placement

**Goal:** The AI can place and move objects in the scene.

- [ ] `place_prefab` with `dry_run` support and Undo registration
- [ ] `set_transform`, `delete_object`, `create_empty`
- [ ] All mutating tools registered with `Undo`
- [ ] Tests: placement, transform mutation, deletion, dry-run parity

### Phase 4 — Lighting

**Goal:** The AI can set up scene lighting.

- [ ] `create_light` (Point, Spot, Directional, Area)
- [ ] `set_ambient_light`, `set_skybox`, `set_fog`
- [ ] Tests: light creation parameters, ambient modes

### Phase 5 — Proposal workflow

**Goal:** Human-in-the-loop validation is fully operational.

- [ ] `propose_changes` → operation queue in memory
- [ ] `McpOperationQueueWindow` EditorWindow
- [ ] `execute_proposal` with grouped Undo
- [ ] `discard_proposal`
- [ ] Tests: queue operations, approval/discard flow, grouped Undo

### Phase 6 — Procedural tools

**Goal:** Bulk and generative operations for efficient level dressing.

- [ ] `place_prefab_batch`
- [ ] `scatter_prefabs`
- [ ] `place_along_path`
- [ ] `fill_grid`
- [ ] Tests: scatter distribution, path spacing, batch Undo grouping

---

*Document version 1.1 — locked-in decisions: Newtonsoft.Json for Unity serialization; `.mcp.json` at Unity project root. Update as the implementation evolves.*
