using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Defines tool schemas (name, description, parameter JSON schema) and the
    /// HTTP route map that the tool executor uses to call the Unity bridge.
    ///
    /// To add a tool:
    ///   1. Add an entry to RouteMap with its HTTP method and path.
    ///   2. Add a ToolSchema to the list returned by GetAll().
    ///
    /// Route paths come directly from McpRouter.cs — they must match exactly.
    /// </summary>
    public static class ToolRegistry
    {
        // -----------------------------------------------------------------------
        // Route map: tool name → (HTTP method, bridge path)
        // -----------------------------------------------------------------------

        public static readonly Dictionary<string, (string Method, string Path)> RouteMap =
            new Dictionary<string, (string, string)>
        {
            // Scene state
            { "ping_unity",              ("GET",  "/status") },
            { "get_scene_hierarchy",     ("GET",  "/scene/hierarchy") },
            { "get_scene_context",       ("GET",  "/scene/context") },
            { "get_prefab_catalog",      ("GET",  "/assets/prefabs") },

            // Placement
            { "place_prefab",            ("POST", "/tools/placement/place_prefab") },
            { "set_transform",           ("POST", "/tools/placement/set_transform") },
            { "delete_object",           ("POST", "/tools/placement/delete_object") },
            { "create_empty",            ("POST", "/tools/placement/create_empty") },

            // Foundation — object inspection & mutation
            { "find_objects",            ("POST", "/tools/scene/find_objects") },
            { "get_object_properties",   ("POST", "/tools/objects/get_properties") },
            { "set_object_properties",   ("POST", "/tools/objects/set_properties") },
            { "set_object_metadata",     ("POST", "/tools/objects/set_metadata") },
            { "reparent_objects",        ("POST", "/tools/objects/reparent") },
            { "duplicate_objects",       ("POST", "/tools/objects/duplicate") },

            // Procedural placement
            { "place_prefab_batch",      ("POST", "/tools/placement/place_batch") },
            { "scatter_prefabs",         ("POST", "/tools/procedural/scatter") },
            { "place_along_path",        ("POST", "/tools/procedural/along_path") },
            { "fill_grid",               ("POST", "/tools/procedural/fill_grid") },

            // Dressing
            { "snap_to_surface",         ("POST", "/tools/placement/snap_to_surface") },
            { "align_and_distribute",    ("POST", "/tools/placement/align_and_distribute") },
            { "randomize_transforms",    ("POST", "/tools/placement/randomize_transforms") },
            { "group_objects",           ("POST", "/tools/placement/group_objects") },
            { "create_trigger_volume",   ("POST", "/tools/placement/create_trigger_volume") },
            { "apply_material",          ("POST", "/tools/placement/apply_material") },
            { "dress_area",              ("POST", "/tools/placement/dress_area") },

            // Proposals
            { "propose_changes",         ("POST", "/tools/proposal/submit") },
            { "execute_proposal",        ("POST", "/tools/proposal/execute") },
            { "discard_proposal",        ("POST", "/tools/proposal/discard") },
            { "get_pending_proposals",   ("GET",  "/tools/proposal/pending") },

            // Lighting
            { "create_light",            ("POST", "/tools/lighting/create_light") },
            { "set_ambient_light",       ("POST", "/tools/lighting/set_ambient") },
            { "create_reflection_probe", ("POST", "/tools/world/create_reflection_probe") },
            { "bake_lighting",           ("POST", "/tools/world/bake_lighting") },

            // Open world
            { "query_terrain",           ("POST", "/tools/world/query_terrain") },
            { "paint_terrain_layer",     ("POST", "/tools/world/paint_terrain_layer") },
            { "bake_navmesh",            ("POST", "/tools/world/bake_navmesh") },
            { "set_navmesh_area",        ("POST", "/tools/world/set_navmesh_area") },
            { "get_loaded_scenes",       ("GET",  "/scene/scenes") },
            { "manage_scene",            ("POST", "/tools/scene/manage") },
            { "replace_prefab",          ("POST", "/tools/placement/replace_prefab") },

            // Scaffold
            { "get_scaffold_zones",      ("GET",  "/assets/scaffold/zones") },
            { "get_scaffold_anchors",    ("GET",  "/assets/scaffold/anchors") },
            { "create_zone_scaffold",    ("POST", "/tools/scaffold/create_zone") },
            { "create_gameplay_marker",  ("POST", "/tools/scaffold/create_marker") },
            { "resolve_scaffold_markers",("POST", "/tools/scaffold/resolve_markers") },
            { "execute_bake",            ("POST", "/tools/scaffold/execute_bake") },
            { "create_patrol_route",     ("POST", "/tools/scaffold/create_patrol_route") },
            { "get_patrol_routes",       ("GET",  "/assets/scaffold/patrol_routes") },

            // Production quality
            { "validate_scene",          ("POST", "/tools/scene/validate") },
            { "create_audio_zone",       ("POST", "/tools/world/create_audio_zone") },

            // Metadata authoring
            { "create_prefab_metadata",  ("POST", "/tools/metadata/create_prefab_metadata") },
            { "update_prefab_metadata",  ("POST", "/tools/metadata/update_prefab_metadata") },
            { "update_scene_context",    ("POST", "/tools/metadata/update_scene_context") },
            { "get_zone_context",        ("GET",  "/scene/context") },

            // Visual feedback
            { "capture_scene_view",      ("POST", "/tools/debug/capture_scene_view") },
            
            // Project files
            { "write_project_file", ("POST", "/tools/project/write_file") },
            { "read_project_file",  ("POST", "/tools/project/read_file")  },
            { "list_project_files", ("POST", "/tools/project/list_files") },
            { "refresh_assets",     ("POST", "/tools/project/refresh_assets") },
        };

        // -----------------------------------------------------------------------
        // Confirmation (in-editor agent)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Tools that only read. Every tool not listed here counts as a change,
        /// including tools added later, so a new tool is never silently exempt.
        /// propose_changes and discard_proposal only touch the proposal queue.
        /// </summary>
        public static readonly HashSet<string> ReadOnlyTools = new HashSet<string>
        {
            "ping_unity", "get_scene_hierarchy", "get_scene_context", "get_prefab_catalog",
            "find_objects", "get_object_properties", "propose_changes", "discard_proposal",
            "get_pending_proposals", "query_terrain", "get_loaded_scenes", "get_scaffold_zones",
            "get_scaffold_anchors", "get_patrol_routes", "validate_scene", "get_zone_context",
            "capture_scene_view", "read_project_file", "list_project_files",
        };

        /// <summary>
        /// Tools the agent always asks about before running: they write code or files,
        /// trigger a compile, delete, open or save scenes, or run a whole batch at once.
        /// </summary>
        public static readonly HashSet<string> AlwaysConfirmTools = new HashSet<string>
        {
            "write_project_file", "refresh_assets", "delete_object",
            "manage_scene", "execute_proposal", "execute_bake",
        };

        /// <summary>True when the agent must ask the user before running this tool.</summary>
        public static bool NeedsConfirmation(string toolName, bool confirmAllChanges) =>
            AlwaysConfirmTools.Contains(toolName) ||
            (confirmAllChanges && !ReadOnlyTools.Contains(toolName));

        // -----------------------------------------------------------------------
        // Tool schemas
        // -----------------------------------------------------------------------

        public static List<ToolSchema> GetAll() => new()
        {
            // -- Scene state -------------------------------------------------------
            T("ping_unity",
              "Verify the connection to Unity and return the active scene name and Unity version. " +
              "Call this first in any session.",
              Obj()),

            T("get_scene_hierarchy",
              "Return the active scene's full object tree. Use max_depth=2 for a quick overview " +
              "on large scenes.",
              Obj(P("max_depth", "integer", "Maximum depth to traverse (-1 = full tree)"))),

            T("get_scene_context",
              "Read the SceneContext design document: level name, setting, active/excluded tags, " +
              "and designer notes. Read this before any placement work.",
              Obj()),

            T("get_prefab_catalog",
              "List available prefabs with metadata. Filter by search_path to narrow results.",
              Obj(P("search_path", "string", "AssetDatabase folder to search (default: Assets)"))),

            // -- Placement --------------------------------------------------------
            T("place_prefab",
              "Place a prefab instance in the current scene.",
              Req(
                Obj(P("prefab_path", "string",    "AssetDatabase path to the prefab"),
                    P("position",    "array",     "[x, y, z] world position"),
                    P("rotation",    "array",     "[x, y, z] Euler angles (optional)"),
                    P("scale",       "array",     "[x, y, z] scale (optional)"),
                    P("parent_path", "string",    "Hierarchy path of parent (optional)"),
                    P("dry_run",     "boolean",   "Preview without placing")),
                "prefab_path", "position")),

            T("set_transform",
              "Move, rotate, or rescale an existing scene object. Omit axes you don't want to change.",
              Req(Obj(P("object_path", "string", "Hierarchy path of the object"),
                      P("position",    "array",  "[x,y,z] new world position (optional)"),
                      P("rotation",    "array",  "[x,y,z] new Euler angles (optional)"),
                      P("scale",       "array",  "[x,y,z] new local scale (optional)")),
                  "object_path")),

            T("delete_object",
              "Delete an object and all its children from the scene.",
              Req(Obj(P("object_path", "string", "Hierarchy path to delete"),
                      P("dry_run",     "boolean","Preview without deleting")),
                  "object_path")),

            T("create_empty",
              "Create an empty GameObject as an organisational container.",
              Req(Obj(P("name",        "string", "GameObject name"),
                      P("position",    "array",  "[x,y,z] world position (optional)"),
                      P("parent_path", "string", "Hierarchy path of parent (optional)")),
                  "name")),

            // -- Foundation -------------------------------------------------------
            T("find_objects",
              "Search the scene by tag, component type, name pattern, layer, or proximity. " +
              "At least one filter is required.",
              Obj(P("tag",            "string", "Unity tag to match"),
                  P("component_type", "string", "Component type name"),
                  P("name_pattern",   "string", "Substring of GameObject name"),
                  P("near_position",  "array",  "[x,y,z] world position for proximity search"),
                  P("near_radius",    "number", "Radius in metres for proximity search"))),

            T("set_object_metadata",
              "Set tag, layer, name, static flags, and/or active state on one or more objects.",
              Req(Obj(P("object_paths","array",  "List of hierarchy paths"),
                      P("tag",         "string", "Unity tag to assign"),
                      P("layer",       "string", "Layer name to assign"),
                      P("is_active",   "boolean","Show/hide the object")),
                  "object_paths")),

            T("reparent_objects",
              "Move objects to a different parent in the hierarchy.",
              Req(Obj(P("object_paths", "array", "Hierarchy paths to reparent"),
                      P("new_parent_path", "string","Target parent path (null = scene root)")),
                  "object_paths")),

            T("duplicate_objects",
              "Copy objects with an optional position/rotation offset.",
              Req(Obj(P("object_paths",      "array", "Paths to duplicate"),
                      P("position_offset",   "array", "[x,y,z] offset applied to each copy"),
                      P("rotation_offset",   "array", "[x,y,z] Euler offset per copy")),
                  "object_paths")),

            // -- Dressing ---------------------------------------------------------
            T("dress_area",
              "Intent-driven area dressing: score prefab catalog against an intent string, " +
              "place objects using Poisson disk sampling, snap to terrain, then submit a proposal " +
              "for review. Always produces a proposal — never places directly.",
              Req(Obj(P("center",       "array",  "[x,y,z] centre of the area"),
                      P("radius",       "number", "Area radius in metres"),
                      P("intent",       "string", "Natural language description of desired result"),
                      P("density",      "string", "'sparse', 'medium', or 'dense'"),
                      P("include_tags", "array",  "Only use prefabs with these tags"),
                      P("exclude_tags", "array",  "Exclude prefabs with these tags")),
                  "center", "radius", "intent")),

            T("snap_to_surface",
              "Raycast each object downward and move it to the hit surface.",
              Req(Obj(P("object_paths",    "array",  "Paths to snap"),
                      P("align_to_normal","boolean","Tilt object to match terrain slope"),
                      P("surface_offset", "number", "Y offset above surface in metres")),
                  "object_paths")),

            T("scatter_prefabs",
              "Randomly scatter a prefab within a circular area using Poisson disk sampling.",
              Req(Obj(P("prefab_path",  "string", "Prefab to scatter"),
                      P("center",      "array",  "[x,y,z] area centre"),
                      P("radius",      "number", "Area radius in metres"),
                      P("count",       "integer","Maximum instance count"),
                      P("min_spacing", "number", "Minimum distance between instances"),
                      P("seed",        "integer","Random seed for reproducibility")),
                  "prefab_path", "center", "radius")),

            // -- Proposals --------------------------------------------------------
            T("propose_changes",
              "Submit a batch of operations for human review in the Operation Queue window. " +
              "Nothing executes until approve_proposal is called.",
              Req(Obj(P("summary",    "string", "Human-readable description of the proposal"),
                      P("operations", "array",  "Array of operation objects")),
                  "summary", "operations")),

            T("execute_proposal",
              "Execute an approved proposal from the Operation Queue.",
              Req(Obj(P("proposal_id", "string", "ID returned when the proposal was submitted")),
                  "proposal_id")),

            T("get_pending_proposals",
              "List all proposals currently waiting in the Operation Queue.",
              Obj()),

            // -- Production quality -----------------------------------------------
            T("validate_scene",
              "Run diagnostic checks on the active scene and return a structured issue report. " +
              "Available checks: missing_scripts, missing_materials, untagged_triggers, " +
              "duplicate_names, meshrenderer_no_collider, large_light_range, floating_objects, " +
              "missing_lod_groups, overlapping_statics.",
              Obj(P("checks",               "array",  "Check IDs to run (null = run all)"),
                  P("scope_path",           "string", "Hierarchy path to restrict scan"),
                  P("max_results_per_check","integer","Cap on results per check (default 50)"),
                  P("lod_vertex_threshold", "integer","Vertex threshold for missing_lod_groups check"))),

            // -- Open world -------------------------------------------------------
            T("query_terrain",
              "Sample terrain height, surface normal, slope, and layer weights at world positions. " +
              "Call this before placing objects on terrain.",
              Req(Obj(P("positions",             "array",  "Array of [x,y,z] positions to sample"),
                      P("include_layer_weights", "boolean","Include texture layer weights (default true)")),
                  "positions")),

            T("bake_navmesh",
              "Trigger a NavMesh bake. Use all=true after layout changes.",
              Obj(P("all",             "boolean","Bake all NavMeshSurface components"),
                  P("agent_type_name","string", "Only bake surfaces for this agent type"))),

            T("bake_lighting",
              "Trigger a lighting bake. Use mode='async' for large scenes.",
              Obj(P("mode", "string", "'async' (default), 'sync', or 'clear'"))),

            T("replace_prefab",
              "Replace every instance of one prefab with another, preserving transform, name, and parent.",
              Req(Obj(P("old_prefab_path","string", "Prefab to replace"),
                      P("new_prefab_path","string", "Replacement prefab"),
                      P("scope_path",     "string", "Restrict to this hierarchy subtree"),
                      P("dry_run",        "boolean","Preview without modifying scene")),
                  "old_prefab_path", "new_prefab_path")),

            // -- Metadata authoring -----------------------------------------------
            T("create_prefab_metadata",
              "Create a PrefabMetadata asset for a prefab. Improves dress_area quality immediately.",
              Req(Obj(P("prefab_path",  "string", "AssetDatabase path of the prefab"),
                      P("tags",         "array",  "Searchable tags (single words)"),
                      P("description",  "string", "Plain-English description for AI context"),
                      P("category",     "string", "Broad category: prop, building, vegetation..."),
                      P("display_name", "string", "Human-readable name"),
                      P("overwrite",    "boolean","Replace existing metadata")),
                  "prefab_path")),

            T("update_scene_context",
              "Write to the active SceneContext design document. Only provided fields change.",
              Obj(P("add_active_tags",   "array", "Add tags to the active list"),
                  P("remove_active_tags","array", "Remove tags from the active list"),
                  P("append_notes",      "string","Append to design notes (adds a newline separator)"),
                  P("notes",             "string","Replace the full notes field"))),

            // -- Visual feedback --------------------------------------------------
            T("capture_scene_view",
              "Render the active Unity Scene View and return it as an image. " +
              "Use focus_path to frame a specific object before capturing.",
              Obj(P("width",      "integer","Capture width in pixels (default 1280)"),
                  P("height",     "integer","Capture height in pixels (default 720)"),
                  P("quality",    "integer","JPEG quality 1-100 (default 85)"),
                  P("focus_path", "string", "Hierarchy path to frame before capture"))),
            
            // -- Project files ----------------------------------------------------
            T("write_project_file",
                "Create or overwrite a file in the Unity project's Assets folder. " +
                "Does NOT trigger recompilation automatically (import_after_write defaults to false). " +
                "Write all files first, compose your final response, then call refresh_assets. " +
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
            
            T("refresh_assets",
                "Refresh the Unity AssetDatabase and trigger compilation of any changed scripts. " +
                "Call this AFTER writing all files and AFTER your final response text has been composed. " +
                "Do not call this in the middle of a tool sequence — it will cause a domain reload.",
                Obj()),
        };

        // -----------------------------------------------------------------------
        // Helpers to build JSON Schema objects concisely
        // -----------------------------------------------------------------------

        private static ToolSchema T(string name, string description, JObject inputSchema) =>
            new() { Name = name, Description = description, InputSchema = inputSchema };

        /// <summary>Build an input schema object with the given properties.</summary>
        private static JObject Obj(params JProperty[] props)
        {
            var properties = new JObject();
            foreach (var p in props) properties.Add(p);
            return new JObject
            {
                ["type"]       = "object",
                ["properties"] = properties,
                ["required"]   = new JArray()
            };
        }

        /// <summary>Build an input schema with required fields marked.</summary>
        private static JObject Req(JObject schema, params string[] required)
        {
            schema["required"] = new JArray(required);
            return schema;
        }

        /// <summary>Build a property entry.</summary>
        private static JProperty P(string name, string type, string description) =>
            new(name, new JObject
            {
                ["type"]        = type,
                ["description"] = description
            });
    }
}
