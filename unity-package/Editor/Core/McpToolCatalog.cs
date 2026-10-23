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
            DescribeToolName,
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
