# Wiring — Project File Tools

One new C# handler, one Python module, one Models append.
Wiring for both the Python MCP server and the embedded agent window.

---

## 1. New files — copy as-is

| File | Destination |
|---|---|
| `ProjectFileHandler.cs` | `Assets/UnityMcpBridge/Editor/Tools/` |
| `project_files.py` | `src/unity_mcp/tools/` |

---

## 2. Append to Models.cs

Paste the entire contents of `Models_ProjectFiles_Additions.cs` at the bottom
of `Models.cs`, just before the final closing `}` of the namespace block.

---

## 3. McpRouter.cs — three new routes

Add after the existing routes and before the final NOT_FOUND return:

```csharp
                // Project File Tools
                if (method == "POST" && path == "/tools/project/write_file")
                    return await ProjectFileHandler.WriteFileAsync(
                        await ParseBodyAsync<WriteProjectFileRequest>(request));
                if (method == "POST" && path == "/tools/project/read_file")
                    return await ProjectFileHandler.ReadFileAsync(
                        await ParseBodyAsync<ReadProjectFileRequest>(request));
                if (method == "POST" && path == "/tools/project/list_files")
                    return await ProjectFileHandler.ListFilesAsync(
                        await ParseBodyAsync<ListProjectFilesRequest>(request));
```

---

## 4. Python MCP server — server.py

Add import:
```python
from unity_mcp.tools.project_files import write_project_file, read_project_file, list_project_files,
```

Register tools:
```python
mcp.tool()(write_project_file)
mcp.tool()(read_project_file)
mcp.tool()(list_project_files)
```

---

## 5. Embedded agent window — ToolRegistry.cs

Add to `RouteMap`:
```csharp
{ "write_project_file", ("POST", "/tools/project/write_file") },
{ "read_project_file",  ("POST", "/tools/project/read_file")  },
{ "list_project_files", ("POST", "/tools/project/list_files") },
```

Add to `GetAll()` (paste inside the list, alongside the other tools):
```csharp
T("write_project_file",
  "Create or overwrite a file in the Unity project's Assets folder. " +
  "After writing, Unity's AssetDatabase is refreshed automatically. " +
  "For C# scripts, Unity begins recompilation immediately. " +
  "Always check whether a file exists with read_project_file before overwriting.",
  Req(Obj(P("path",               "string", "Path relative to Assets/, e.g. 'Scripts/MyBehaviour.cs'"),
          P("content",            "string", "Full text content to write (UTF-8)"),
          P("overwrite",          "boolean","Replace if exists (default true)"),
          P("create_directories", "boolean","Create parent dirs if needed (default true)")),
      "path", "content")),

T("read_project_file",
  "Read the text content of a file from the Assets folder. " +
  "Returns exists=false (not an error) if the file is not found. " +
  "Use this to inspect a script before modifying it.",
  Req(Obj(P("path", "string", "Path relative to Assets/")),
      "path")),

T("list_project_files",
  "List files in a directory under Assets/. " +
  ".meta files are excluded automatically. " +
  "Use pattern='*.cs' to list only scripts, '*.prefab' for prefabs, etc.",
  Obj(P("path",      "string", "Directory relative to Assets/ (empty = Assets root)"),
      P("pattern",   "string", "Glob pattern e.g. '*.cs' (default '*')"),
      P("recursive", "boolean","Include subdirectories (default false)"))),
```

---

## Verification

After recompiling:

1. **write — basic**
   ```
   write_project_file(
       path="Scripts/Test/HelloWorld.cs",
       content="using UnityEngine;\npublic class HelloWorld : MonoBehaviour { }"
   )
   ```
   Expect: `{ created: true, asset_path: "Assets/Scripts/Test/HelloWorld.cs" }`.
   The file should appear in the Project window under Scripts/Test/
   and Unity should begin compiling.

2. **write — overwrite guard**
   Call again without `overwrite=true` (it defaults to true, so explicitly pass false):
   ```
   write_project_file(path="Scripts/Test/HelloWorld.cs",
                      content="...", overwrite=False)
   ```
   Expect: `FILE_EXISTS` error.

3. **read — exists**
   ```
   read_project_file("Scripts/Test/HelloWorld.cs")
   ```
   Expect: `{ exists: true, content: "using UnityEngine;..." }`.

4. **read — not found**
   ```
   read_project_file("Scripts/DoesNotExist.cs")
   ```
   Expect: `{ exists: false }` — not an error, just a clean miss.

5. **list**
   ```
   list_project_files("Scripts/Test", "*.cs")
   ```
   Expect: `{ files: ["Assets/Scripts/Test/HelloWorld.cs"], count: 1 }`.

6. **path traversal rejected**
   ```
   write_project_file(path="../../etc/passwd", content="test")
   ```
   Expect: `INVALID_PATH` error.
