# Unity MCP Bridge — Developer Guide: Adding New Tools

This guide explains how to add a new tool to the bridge end-to-end: from the Python MCP server through to the Unity C# handler, with tests on both sides.

---

## 1. Architecture recap

```
Claude Desktop
     │  MCP protocol (stdio)
     ▼
Python MCP Server          ← tool schema declarations, HTTP client
     │  HTTP/JSON (localhost:11000)
     ▼
Unity Editor Plugin        ← HttpListener, routing, Unity API calls
```

Each tool is implemented in two places:

| Concern | Where |
|---|---|
| Tool name, description, parameter schema | Python function docstring + type hints |
| HTTP request serialisation | Python (`unity_client.post` / `.get`) |
| Request parsing, routing | C# `McpRouter` |
| Unity API logic | C# handler class |
| Request/response DTOs | C# `Models.cs` |
| Tests | `mcp-server/tests/` (pytest) and `Unity Test Runner` (NUnit) |

The Python side is intentionally thin. All domain logic lives in C#.

---

## 2. Data flow for a single tool call

```
1.  Claude calls the tool with arguments
2.  FastMCP validates arguments against the JSON schema
        (generated from Python type hints)
3.  Python tool function calls UnityClient.instance().post(path, payload)
4.  httpx sends HTTP POST to http://localhost:11000/tools/...
5.  McpHttpListener receives the request on a background thread
6.  McpRouter.RouteAsync reads the URL, calls ParseBodyAsync<T>
        to deserialise the body into a request DTO
7.  Router calls the appropriate handler method
8.  Handler calls MainThreadDispatcher.EnqueueAsync<McpResponse>(() => { ... })
        to marshal the Unity API work to the main thread
9.  The lambda runs on the main thread (next EditorApplication.update tick)
10. Handler returns McpResponse.Ok(data) or McpResponse.Fail(code, message)
11. McpHttpListener serialises the response and writes it to the HTTP stream
12. Python _unwrap() checks success, raises UnityError on failure
13. Python tool function formats the result and returns a string
14. Claude receives the string and incorporates it into its response
```

---

## 3. Adding a Python tool

### 3.1 Create the tool function

Tool functions live in `mcp-server/src/unity_mcp/tools/`. Each phase has its own module (`scene.py`, `placement.py`, etc.). Add to an existing module or create a new one.

**Rules:**
- Plain `async def` — no `@mcp.tool()` decorator here
- Docstring is the tool's description as Claude sees it — write it precisely
- Type hints generate the JSON parameter schema — be accurate
- Always wrap in `try/except` for `UnityConnectionError` and `UnityError`
- Return a human-readable string for confirmation messages
- Return `json.dumps(data)` for structured data Claude needs to reason about

```python
# mcp-server/src/unity_mcp/tools/placement.py  (example addition)

async def rename_object(
    object_path: str,
    new_name: str,
    dry_run: bool = False,
) -> str:
    """
    Rename an existing scene object.

    object_path: Scene hierarchy path of the object to rename,
                 e.g. "Environment/Buildings/OldName".
                 Use get_scene_hierarchy to discover valid paths.
    new_name:    The new name for the object.
    dry_run:     If true, validates the object exists and returns the
                 expected new path without making any changes.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/rename_object", {
            "object_path": object_path,
            "new_name":    new_name,
            "dry_run":     dry_run,
        })
        dry    = data.get("dry_run", False)
        prefix = "[DRY RUN] Would rename" if dry else "Renamed"
        suffix = " No changes made." if dry else ""
        old    = data.get("object_path", object_path)
        new    = data.get("new_path",    "")
        return f"{prefix} '{old}' → '{new}'.{suffix}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
```

**Returning structured data** (when Claude needs to read individual fields):

```python
async def find_objects(
    tag: str | None = None,
    component: str | None = None,
    name_contains: str | None = None,
) -> str:
    """
    Search scene objects by Unity tag, component type name, or name substring.
    Returns a list of matching hierarchy paths.

    tag:           Unity tag to filter by, e.g. "Enemy", "Interactable".
    component:     Component type name to filter by, e.g. "Light", "Collider".
    name_contains: Case-insensitive substring the object name must contain.
    """
    try:
        data = await UnityClient.instance().get(
            "/scene/find"
            + (f"?tag={tag}"              if tag            else "")
            + (f"&component={component}"  if component      else "")
            + (f"&name={name_contains}"   if name_contains  else "")
        )
        objects = data.get("objects", [])
        if not objects:
            return "No matching objects found."
        return json.dumps(objects, indent=2)  # Claude can iterate over this
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
```

### 3.2 Register in server.py

Import the function and register it:

```python
# mcp-server/src/unity_mcp/server.py

from unity_mcp.tools.placement import (
    place_prefab,
    rename_object,   # ← add to existing import
    # ...
)

# Add at the end of the Phase N section:
mcp.tool()(rename_object)
```

That's all the Python side requires.

---

## 4. Adding a C# handler

Four steps: DTOs → handler method → route → tests.

### 4.1 Add request and response DTOs to Models.cs

**Request DTO** (for POST endpoints):

```csharp
// Models.cs — add inside the namespace

public class RenameObjectRequest
{
    [JsonProperty("object_path")] public string ObjectPath { get; set; }
    [JsonProperty("new_name")]    public string NewName    { get; set; }
    [JsonProperty("dry_run")]     public bool   DryRun     { get; set; }
}
```

**Response DTO** (the `data` object returned on success):

```csharp
public class RenameResultData
{
    [JsonProperty("object_path")] public string ObjectPath { get; set; }
    [JsonProperty("new_path")]    public string NewPath    { get; set; }
    [JsonProperty("dry_run")]     public bool   DryRun     { get; set; }
}
```

**GET endpoints** don't need a request DTO — query parameters are parsed directly in the router.

**Naming conventions:**
- Request DTOs: `<OperationName>Request`
- Response DTOs: `<OperationName>ResultData` or descriptive data class
- JSON property names: `snake_case` (match Python dict keys exactly)
- C# property names: `PascalCase`

**Nullable fields** — use `float?` / `int?` / `string?` for optional parameters that should remain unchanged when omitted:

```csharp
// null means "don't change this field"
[JsonProperty("position")] public float[] Position { get; set; }
[JsonProperty("rotation")] public float[] Rotation { get; set; }
```

### 4.2 Write the handler method

Handlers live in `Assets/UnityMCP/Source/Editor/Tools/`. Add to an existing handler class or create a new one.

**POST handler pattern:**

```csharp
// PlacementHandler.cs — add as a new static method

public static Task<McpResponse> RenameObjectAsync(RenameObjectRequest req) =>
    MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        // 1. Validate inputs
        if (string.IsNullOrWhiteSpace(req?.ObjectPath))
            return McpResponse.Fail("INVALID_PARAMS", "object_path must not be empty.");
        if (string.IsNullOrWhiteSpace(req.NewName))
            return McpResponse.Fail("INVALID_PARAMS", "new_name must not be empty.");

        // 2. Look up objects / assets
        var go = FindByPath(req.ObjectPath);
        if (go == null)
            return McpResponse.Fail("OBJECT_NOT_FOUND", $"No object at path '{req.ObjectPath}'.");

        // 3. dry_run — validate only, no mutations
        if (req.DryRun)
        {
            string expectedParent = req.ObjectPath.Contains('/')
                ? req.ObjectPath[..req.ObjectPath.LastIndexOf('/')]
                : string.Empty;
            string expectedPath = string.IsNullOrEmpty(expectedParent)
                ? req.NewName
                : expectedParent + "/" + req.NewName;

            return McpResponse.Ok(new RenameResultData
            {
                ObjectPath = req.ObjectPath,
                NewPath    = expectedPath,
                DryRun     = true
            });
        }

        // 4. Register with Undo (all mutating operations must do this)
        Undo.RecordObject(go, $"MCP Rename {go.name}");

        // 5. Apply the change
        string oldPath = GetPath(go);
        go.name = req.NewName;

        // 6. Return the result
        return McpResponse.Ok(new RenameResultData
        {
            ObjectPath = oldPath,
            NewPath    = GetPath(go),
            DryRun     = false
        });
    });
```

**GET handler pattern** (no DTO, query params as arguments):

```csharp
// SceneQueryHandler.cs — add as a new static method

public static Task<McpResponse> FindObjectsAsync(string tag, string component, string nameContains) =>
    MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        var results = new List<string>();

        // Walk the entire scene hierarchy
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
            CollectMatching(root.transform, tag, component, nameContains, results);

        return McpResponse.Ok(new { objects = results.ToArray() });
    });

private static void CollectMatching(
    Transform t, string tag, string component, string nameContains,
    List<string> results)
{
    var go = t.gameObject;
    bool match = true;

    if (!string.IsNullOrEmpty(tag)          && go.tag != tag)
        match = false;
    if (!string.IsNullOrEmpty(component)    && go.GetComponent(component) == null)
        match = false;
    if (!string.IsNullOrEmpty(nameContains) && !go.name.ToLower().Contains(nameContains.ToLower()))
        match = false;

    if (match) results.Add(GetObjectPath(go)); // use PlacementHandler.GetPath

    for (int i = 0; i < t.childCount; i++)
        CollectMatching(t.GetChild(i), tag, component, nameContains, results);
}
```

### 4.3 Register the route in McpRouter.cs

Add to `RouteAsync` in the appropriate phase section:

```csharp
// POST endpoint
if (method == "POST" && path == "/tools/placement/rename_object")
    return await PlacementHandler.RenameObjectAsync(
        await ParseBodyAsync<RenameObjectRequest>(request));

// GET endpoint with query parameters
if (method == "GET" && path == "/scene/find")
    return await SceneQueryHandler.FindObjectsAsync(
        request.QueryString["tag"],
        request.QueryString["component"],
        request.QueryString["name"]);
```

**That's all four steps.** Rebuild in Unity and restart Claude Desktop — the new tool appears automatically.

---

## 5. Adding tests

### 5.1 Python tests (pytest + respx)

Test that the tool sends the right HTTP request and handles responses correctly. Do not test Unity logic here — only the Python→HTTP layer.

```python
# mcp-server/tests/test_tools_placement.py  (add to existing file)

@pytest.mark.asyncio
async def test_rename_object_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/rename_object").mock(
            return_value=httpx.Response(200, json={
                "success": True,
                "data": {"object_path": "Env/OldName", "new_path": "Env/NewName", "dry_run": False}
            })
        )
        await rename_object("Env/OldName", "NewName")
    assert route.called


@pytest.mark.asyncio
async def test_rename_object_sends_correct_payload(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/rename_object").mock(
            return_value=httpx.Response(200, json={
                "success": True,
                "data": {"object_path": "Env/OldName", "new_path": "Env/NewName", "dry_run": False}
            })
        )
        await rename_object("Env/OldName", "NewName")
    sent = json.loads(route.calls[0].request.content)
    assert sent["object_path"] == "Env/OldName"
    assert sent["new_name"]    == "NewName"


@pytest.mark.asyncio
async def test_rename_object_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/rename_object").mock(
            return_value=httpx.Response(200, json={
                "success": True,
                "data": {"object_path": "Env/OldName", "new_path": "Env/NewName", "dry_run": True}
            })
        )
        result = await rename_object("Env/OldName", "NewName", dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_rename_object_returns_error_string_on_object_not_found(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/rename_object").mock(
            return_value=httpx.Response(400, json={
                "success": False,
                "error": {"code": "OBJECT_NOT_FOUND", "message": "No object at path"}
            })
        )
        result = await rename_object("Env/Ghost", "NewName")
    assert "OBJECT_NOT_FOUND" in result
```

**What to always test:**
- Correct HTTP endpoint and method are called
- Correct payload is sent (keys and values)
- `dry_run` flag is forwarded in the payload
- `[DRY RUN]` appears in the returned string when `dry_run=True`
- Unity error responses surface as non-raising strings
- Connection errors surface as non-raising strings

Run tests with: `uv run pytest`

### 5.2 C# tests (Unity Test Runner, NUnit EditMode)

Test the handler logic directly — no HTTP involved. Call the handler method, call `MainThreadDispatcher.Tick()`, then assert on the result.

```csharp
// UnityMCP/Source/Tests/PlacementHandlerTests.cs  (add to existing class)

[Test]
public void RenameObject_DryRun_DoesNotRenameObject()
{
    var go = new GameObject("OriginalName");
    try
    {
        var task = PlacementHandler.RenameObjectAsync(new RenameObjectRequest
            { ObjectPath = "OriginalName", NewName = "NewName", DryRun = true });

        MainThreadDispatcher.Tick();

        Assert.AreEqual("OriginalName", go.name); // unchanged
        Assert.IsTrue(task.Result.Success);
        Assert.IsTrue(((RenameResultData)task.Result.Data).DryRun);
    }
    finally { Object.DestroyImmediate(go); }
}

[Test]
public void RenameObject_ReturnsError_WhenObjectNotFound()
{
    var task = PlacementHandler.RenameObjectAsync(new RenameObjectRequest
        { ObjectPath = "Ghost/Object", NewName = "NewName" });

    MainThreadDispatcher.Tick();

    Assert.IsFalse(task.Result.Success);
    Assert.AreEqual("OBJECT_NOT_FOUND", task.Result.Error.Code);
}

[Test]
public void RenameObject_ReturnsError_WhenNewNameIsEmpty()
{
    var go = new GameObject("Target");
    try
    {
        var task = PlacementHandler.RenameObjectAsync(new RenameObjectRequest
            { ObjectPath = "Target", NewName = "" });

        MainThreadDispatcher.Tick();

        Assert.IsFalse(task.Result.Success);
        Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
    }
    finally { Object.DestroyImmediate(go); }
}
```

**Key testing patterns:**

```csharp
// 1. Always drain the dispatcher after calling a handler
var task = MyHandler.DoSomethingAsync(request);
MainThreadDispatcher.Tick();   // processes the entire async chain synchronously
Assert.IsTrue(task.Result.Success);

// 2. Cast Data to the concrete response type
var data = (MyResponseData)task.Result.Data;
Assert.AreEqual("expected", data.SomeField);

// 3. For tests that create GameObjects, clean up in TearDown or finally{}
[TearDown]
public void TearDown()
{
    foreach (var go in Object.FindObjectsOfType<GameObject>())
        if (go.name.StartsWith("MyTest"))
            Object.DestroyImmediate(go);
}

// 4. For tests that touch RenderSettings, save and restore
private Color _savedAmbientLight;
[SetUp]    public void SetUp()    { _savedAmbientLight = RenderSettings.ambientLight; }
[TearDown] public void TearDown() { RenderSettings.ambientLight = _savedAmbientLight; }
```

---

## 6. Reference: patterns and conventions

### Error codes

Use consistent, screaming-snake-case error codes. Established codes:

| Code | When to use |
|---|---|
| `INVALID_PARAMS` | Missing required field, negative value, empty string |
| `PREFAB_NOT_FOUND` | `AssetDatabase.LoadAssetAtPath` returned null for a prefab |
| `MATERIAL_NOT_FOUND` | Material asset not found |
| `OBJECT_NOT_FOUND` | `FindByPath` returned null |
| `PARENT_NOT_FOUND` | Parent path provided but not found in hierarchy |
| `PROPOSAL_NOT_FOUND` | No proposal with the given ID |
| `PROPOSAL_NOT_PENDING` | Proposal exists but is already executed or discarded |
| `INTERNAL_ERROR` | Unhandled exception (caught by the router's outer try/catch) |
| `NOT_FOUND` | No route matched the HTTP method + path |

Define new codes for new domains. Keep them specific enough that the AI can report them meaningfully.

### The dry_run pattern

Every mutating tool should support `dry_run`. The contract:
- Validate all parameters exactly as the live path would
- Return the same response shape as the live path, with `dry_run: true`
- Make zero changes to the scene or project

```csharp
// Validate first (same in both paths)
var go = FindByPath(req.ObjectPath);
if (go == null)
    return McpResponse.Fail("OBJECT_NOT_FOUND", $"No object at '{req.ObjectPath}'.");

// Branch on dry_run before any mutation
if (req.DryRun)
    return McpResponse.Ok(new MyResultData { ..., DryRun = true });

// Live path — mutate here
Undo.RecordObject(go, "MCP ...");
// ... apply changes ...
return McpResponse.Ok(new MyResultData { ..., DryRun = false });
```

### Undo registration

All operations that create, delete, or modify scene objects must register with Undo:

```csharp
// New object
Undo.RegisterCreatedObjectUndo(go, "MCP Create ...");

// Modifying an existing object (call BEFORE making changes)
Undo.RecordObject(go.transform, "MCP Modify ...");
Undo.RecordObject(go,           "MCP Rename ...");

// Deletion
Undo.DestroyObjectImmediate(go);

// Batch: group multiple operations under one Undo entry
Undo.IncrementCurrentGroup();
Undo.SetCurrentGroupName("MCP Batch ...");
// ... all RegisterCreatedObjectUndo / RecordObject calls go here ...
// No explicit collapse needed — they're already in the same group
```

### RenderSettings changes

Modifying `RenderSettings` (ambient, fog, skybox) cannot use `Undo.RecordObject`. Mark the scene dirty instead, guarding against play mode:

```csharp
RenderSettings.fog = true;
if (!Application.isPlaying)
    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
```

### The MainThreadDispatcher contract

- All Unity API calls must happen inside `MainThreadDispatcher.EnqueueAsync`
- The entire handler method can be a single `EnqueueAsync` call if all work is synchronous
- If work is split across multiple awaits, each Unity API segment needs its own `EnqueueAsync`
- **Do not** nest `EnqueueAsync` calls — the inner call will deadlock because the dispatcher does not re-enter
- **Do not** use `Task.Run` for Unity API work — it runs on a thread pool thread where Unity APIs are unsafe

```csharp
// CORRECT — single dispatch for synchronous Unity work
public static Task<McpResponse> DoThingAsync(MyRequest req) =>
    MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        var go = FindByPath(req.Path); // Unity API — safe, we're on main thread
        go.name = req.NewName;
        return McpResponse.Ok(new { name = go.name });
    });

// CORRECT — multiple dispatches for async chains
public static async Task<McpResponse> DoComplexThingAsync(MyRequest req)
{
    var validation = await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        var go = FindByPath(req.Path);
        return go == null ? McpResponse.Fail("OBJECT_NOT_FOUND", "...") : null;
    });
    if (validation != null) return validation;

    // ... other async work (e.g. file I/O) ...

    return await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        // More Unity API work
        return McpResponse.Ok(new { });
    });
}

// WRONG — nested EnqueueAsync deadlocks
public static Task<McpResponse> BrokenAsync(MyRequest req) =>
    MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
    {
        var inner = MainThreadDispatcher.EnqueueAsync(() => { }); // DEADLOCK
        inner.Wait();
        return McpResponse.Ok(null);
    });
```

### Tool function docstrings

The docstring is what Claude reads when deciding how and when to use the tool. Write it as a precise specification:

```python
async def my_tool(
    required_param: str,
    optional_param: float | None = None,
    dry_run: bool = False,
) -> str:
    """
    One sentence: what this tool does and when to use it.

    required_param: What this is, valid values or format, example value.
    optional_param: What this is. Omit to [default behaviour].
    dry_run:        If true, [what validation happens] without [what doesn't happen].
    """
```

Rules:
- First line: one sentence, no period, describes the primary action
- Each parameter: one line, describes format and effect of omitting it
- `dry_run`: always describe exactly what is and isn't validated/executed
- Do not describe implementation details
- Do not reference internal class or method names

---

## 7. Checklist

Use this when adding any new tool:

**Python:**
- [ ] Async function with accurate type hints and precise docstring
- [ ] `try/except UnityConnectionError` and `UnityError` — returns string, does not raise
- [ ] Dry run info reflected in return string (`"[DRY RUN]"` prefix)
- [ ] Registered in `server.py` with `mcp.tool()(function_name)`

**C# Models.cs:**
- [ ] Request DTO with `[JsonProperty("snake_case")]` on every property
- [ ] Response DTO (or anonymous object if simple)
- [ ] Nullable types (`float?`, `string?`) for optional fields that should leave existing values unchanged

**C# Handler:**
- [ ] Method accepts DTO, returns `Task<McpResponse>`
- [ ] All Unity API calls inside `MainThreadDispatcher.EnqueueAsync`
- [ ] Validates all inputs before any Unity API call
- [ ] `DryRun` branch validates fully but makes zero mutations
- [ ] Mutating operations call `Undo.RegisterCreatedObjectUndo` / `Undo.RecordObject` / `Undo.DestroyObjectImmediate`
- [ ] `RenderSettings` changes guarded with `if (!Application.isPlaying)`

**C# McpRouter:**
- [ ] Route entry for correct HTTP method and path
- [ ] POST: `await ParseBodyAsync<RequestDto>(request)` passed to handler
- [ ] GET: query parameters read directly from `request.QueryString`

**Tests (Python):**
- [ ] Correct endpoint called
- [ ] Correct payload sent
- [ ] `dry_run` forwarded
- [ ] Error responses returned as strings without raising

**Tests (C#):**
- [ ] `MainThreadDispatcher.Tick()` called after every handler invocation
- [ ] `dry_run=true` test verifies no scene mutation
- [ ] At least one error case per distinct error code
- [ ] Scene objects cleaned up in `[TearDown]`
- [ ] `RenderSettings` saved and restored if modified

