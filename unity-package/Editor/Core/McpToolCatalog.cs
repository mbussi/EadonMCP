using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Every tool the bridge offers: the built-in tools (described in
    /// Catalog/builtin-tools.json, generated from the Python server by
    /// server/scripts/export_tool_catalog.py), describe_tool, and the tools
    /// extensions register with McpToolRegistry.
    ///
    /// Served by GET /tools/catalog and GET /tools/describe, and used by the in-editor
    /// agent to build its tool list.
    /// </summary>
    [InitializeOnLoad]
    public static class McpToolCatalog
    {
        public const string BuiltInOwner = "Eadon MCP Bridge";
        public const string DescribeToolName = "describe_tool";

        private const string CatalogRelativePath = "Editor/Catalog/builtin-tools.json";

        /// <summary>Built-in tools that only read. Every other built-in tool counts as a change.</summary>
        private static readonly HashSet<string> ReadOnlyBuiltIns = new HashSet<string>
        {
            "ping_unity", "get_scene_hierarchy", "get_scene_context", "get_prefab_catalog",
            "find_objects", "get_object_properties", "propose_changes", "discard_proposal",
            "get_pending_proposals", "query_terrain", "get_loaded_scenes", "get_scaffold_zones",
            "get_scaffold_anchors", "get_scaffold_markers", "get_patrol_routes", "validate_scene",
            "get_zone_context", "capture_scene_view", "read_project_file", "list_project_files",
            "get_component_catalog", "get_cutscene_templates", "resolve_scaffold_markers",
            "get_job_status", DescribeToolName,
        };

        /// <summary>
        /// Built-in tools the agent always asks about: they write code or files, trigger a
        /// compile, delete, open or save scenes, or run a whole batch at once.
        /// </summary>
        private static readonly HashSet<string> AlwaysConfirmBuiltIns = new HashSet<string>
        {
            "write_project_file", "refresh_assets", "delete_object",
            "manage_scene", "execute_proposal", "execute_bake",
        };

        /// <summary>
        /// Built-in tools a proposal may run: scene changes that Unity's Undo reverses, so a
        /// proposal is one undo step and a failed one can be undone as a whole. Every other
        /// tool is refused when the proposal is submitted (see ProposalRefusal).
        /// </summary>
        private static readonly HashSet<string> ProposalBuiltIns = new HashSet<string>
        {
            "place_prefab", "place_prefab_batch", "scatter_prefabs", "place_along_path", "fill_grid",
            "replace_prefab", "set_transform", "delete_object", "create_empty", "duplicate_objects",
            "reparent_objects", "group_objects", "snap_to_surface", "align_and_distribute",
            "randomize_transforms", "add_component_to_object", "set_object_properties",
            "set_object_metadata", "apply_material", "create_trigger_volume", "create_light",
            "set_ambient_light", "set_skybox", "set_fog", "create_reflection_probe", "create_audio_zone",
            "set_navmesh_area", "paint_terrain_layer", "create_zone_scaffold", "link_zones",
            "create_reference_point", "create_gameplay_marker", "update_gameplay_marker",
            "hide_scaffold", "show_scaffold", "execute_bake", "create_patrol_route",
        };

        /// <summary>Why a built-in tool cannot be part of a proposal.</summary>
        private static readonly Dictionary<string, string> ProposalRefusals = new Dictionary<string, string>
        {
            ["propose_changes"]  = "proposals cannot contain proposals",
            ["execute_proposal"] = "proposals cannot contain proposals",
            ["discard_proposal"] = "proposals cannot contain proposals",
            ["dress_area"]       = "it submits a proposal of its own",
            ["cancel_job"]       = "it acts on jobs, not on the scene",
            ["write_project_file"]       = "it writes a file, which Undo cannot reverse",
            ["refresh_assets"]           = "it recompiles scripts, which reloads Unity in the middle of the proposal",
            ["manage_scene"]             = "it opens, closes or creates scenes",
            ["bake_lighting"]            = "it runs in the background and writes lighting data assets",
            ["bake_navmesh"]             = "it writes navmesh data assets, which Undo cannot reverse",
            ["create_postprocessing_volume"] = "it can create a volume profile asset, which Undo cannot remove",
            ["create_cutscene_sequence"] = "it creates timeline assets, which Undo cannot remove",
            ["update_cutscene_sequence"] = "it changes and saves timeline assets",
            ["preview_cutscene_sequence"] = "it only previews a cutscene and changes nothing",
            ["create_prefab_metadata"]   = "it creates a metadata asset, which Undo cannot remove",
            ["update_prefab_metadata"]   = "it changes and saves a metadata asset",
            ["update_scene_context"]     = "it changes and saves the scene context asset",
        };

        private static readonly object Gate = new object();
        private static string _catalogFile;
        private static List<McpToolDefinition> _builtIns;
        private static HashSet<string> _builtInNames;

        static McpToolCatalog()
        {
            // Resolve the file on the main thread; the routes read it from the listener's threads.
            _catalogFile = FindCatalogFile();
        }

        // -----------------------------------------------------------------------
        // Queries
        // -----------------------------------------------------------------------

        /// <summary>True for the bridge's own tools, including describe_tool.</summary>
        public static bool IsBuiltIn(string name)
        {
            EnsureLoaded();
            return name != null && _builtInNames.Contains(name);
        }

        /// <summary>The built-in tools, then the extension tools.</summary>
        public static List<McpToolDefinition> GetAll()
        {
            EnsureLoaded();
            var all = new List<McpToolDefinition>(_builtIns);
            all.AddRange(McpToolRegistry.GetTools());
            return all;
        }

        /// <summary>Finds any tool, built-in or extension.</summary>
        public static bool TryGet(string name, out McpToolDefinition tool)
        {
            EnsureLoaded();
            tool = _builtIns.Find(t => t.Name == name);
            return tool != null || McpToolRegistry.TryGet(name, out tool);
        }

        /// <summary>Finds the tool that calls a route (method and path). Safe from any thread.</summary>
        public static bool TryGetByRoute(string method, string path, out McpToolDefinition tool)
        {
            EnsureLoaded();
            tool = _builtIns.Find(t => t.Method == method && t.Path == path)
                   ?? McpToolRegistry.GetTools().Find(t => t.Method == method && t.Path == path);
            return tool != null;
        }

        /// <summary>
        /// Null when a proposal may run the tool; otherwise why not, phrased to follow
        /// "'name' cannot be part of a proposal: ".
        /// </summary>
        public static string ProposalRefusal(string name)
        {
            if (!TryGet(name, out var tool))
                return "there is no such tool";
            if (tool.IsBuiltIn)
            {
                if (ProposalBuiltIns.Contains(tool.Name)) return null;
                if (ProposalRefusals.TryGetValue(tool.Name, out var reason)) return reason;
                return tool.ReadOnly
                    ? "it only reads, so it changes nothing; call it directly"
                    : "it is not one of the tools a proposal can run (scene changes that Undo reverses)";
            }
            return tool.AllowInProposals
                ? null
                : $"{tool.Owner} does not allow it in proposals (it may change assets or files that Undo cannot reverse)";
        }

        /// <summary>The names of every tool a proposal may run, sorted.</summary>
        public static List<string> ProposalToolNames()
        {
            var names = new List<string>();
            foreach (var tool in GetAll())
                if (ProposalRefusal(tool.Name) == null) names.Add(tool.Name);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// True when the agent must ask before running the tool. Unknown tools always ask.
        /// </summary>
        public static bool NeedsConfirmation(string name, bool confirmAllChanges)
        {
            if (!TryGet(name, out var tool)) return true;
            return tool.AlwaysConfirm || (confirmAllChanges && !tool.ReadOnly);
        }

        /// <summary>The JSON for one tool, as /tools/catalog and /tools/describe return it.</summary>
        public static JObject ToJson(McpToolDefinition tool, bool fullDescription)
        {
            var json = new JObject
            {
                ["name"]           = tool.Name,
                ["summary"]        = tool.Summary,
                ["input_schema"]   = tool.InputSchema,
                ["method"]         = tool.Method,
                ["path"]           = tool.Path,
                ["read_only"]      = tool.ReadOnly,
                ["always_confirm"] = tool.AlwaysConfirm,
                ["source"]         = tool.IsBuiltIn ? BuiltInOwner : tool.Owner,
            };
            if (fullDescription)
                json["description"] = tool.FullDescription;
            return json;
        }

        // -----------------------------------------------------------------------
        // Routes
        // -----------------------------------------------------------------------

        /// <summary>GET /tools/catalog?source=all|builtin|extensions&amp;detail=summary|full</summary>
        public static McpResponse GetCatalog(string source, string detail)
        {
            source = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim().ToLowerInvariant();
            var full = string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase);

            var tools = new JArray();
            foreach (var tool in GetAll())
            {
                if (source == "builtin" && !tool.IsBuiltIn) continue;
                if (source == "extensions" && tool.IsBuiltIn) continue;
                tools.Add(ToJson(tool, full));
            }
            return McpResponse.Ok(new JObject { ["count"] = tools.Count, ["tools"] = tools });
        }

        /// <summary>GET /tools/describe?name=…</summary>
        public static McpResponse Describe(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return McpResponse.Fail("INVALID_PARAMS", "name is required.");
            if (!TryGet(name.Trim(), out var tool))
                return McpResponse.Fail("NOT_FOUND", $"No tool named '{name}'.");
            return McpResponse.Ok(ToJson(tool, fullDescription: true));
        }

        // -----------------------------------------------------------------------
        // Loading
        // -----------------------------------------------------------------------

        private static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_builtIns != null) return;

                var tools = new List<McpToolDefinition> { DescribeTool() };
                try
                {
                    if (_catalogFile == null || !File.Exists(_catalogFile))
                        throw new FileNotFoundException($"Could not find {CatalogRelativePath} in the Eadon MCP Bridge package.");

                    var root = JObject.Parse(File.ReadAllText(_catalogFile));
                    foreach (var entry in (JArray)root["tools"])
                    {
                        var name = (string)entry["name"];
                        tools.Add(new McpToolDefinition
                        {
                            Name          = name,
                            Summary       = (string)entry["summary"],
                            Description   = (string)entry["description"],
                            InputSchema   = (JObject)entry["input_schema"],
                            Method        = (string)entry["method"],
                            Path          = (string)entry["path"],
                            ReadOnly      = ReadOnlyBuiltIns.Contains(name),
                            AlwaysConfirm = AlwaysConfirmBuiltIns.Contains(name),
                            Owner         = BuiltInOwner,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCP Bridge] Could not load the built-in tool catalog: {ex.Message}");
                }

                _builtIns     = tools;
                _builtInNames = new HashSet<string>();
                foreach (var t in tools) _builtInNames.Add(t.Name);
            }
        }

        private static McpToolDefinition DescribeTool() => new McpToolDefinition
        {
            Name        = DescribeToolName,
            Summary     = "Return a tool's full usage guide, with examples, and its argument schema.",
            Description = "Return a tool's full usage guide, with examples, and its argument schema.\n\n" +
                          "The tool list only carries a short summary of each tool. Call describe_tool " +
                          "before the first use of a tool whose summary isn't enough, especially tools " +
                          "with many options.\n\nname: the tool's name, e.g. \"dress_area\".",
            InputSchema = JObject.Parse(
                @"{""type"":""object"",""properties"":{""name"":{""type"":""string"",""description"":""The tool's name""}},""required"":[""name""]}"),
            Method   = "GET",
            Path     = "/tools/describe",
            ReadOnly = true,
            Owner    = BuiltInOwner,
        };

        private static string FindCatalogFile()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpToolCatalog).Assembly);
                if (package != null)
                    return Path.Combine(package.resolvedPath, CatalogRelativePath);

                // Not installed as a package (e.g. copied into Assets): find the file by name.
                foreach (var guid in AssetDatabase.FindAssets("builtin-tools"))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assetPath.EndsWith("Catalog/builtin-tools.json", StringComparison.Ordinal))
                        return Path.GetFullPath(assetPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Could not locate the built-in tool catalog: {ex.Message}");
            }
            return null;
        }
    }
}
