using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Tools that extensions add to the bridge. A registered tool is listed by
    /// GET /tools/catalog, described by GET /tools/describe, runnable by name through
    /// POST /tools/call (what the Python server's call_unity_tool uses), offered to the
    /// in-editor agent, and usable in proposals. Its own route is registered too.
    ///
    /// Register from an [InitializeOnLoad] static constructor; the registry is rebuilt
    /// on every domain reload.
    ///
    ///     McpToolRegistry.Register(new McpToolDefinition
    ///     {
    ///         Name        = "count_trees",
    ///         Summary     = "Count the trees in the active scene.",
    ///         InputSchema = JObject.Parse(@"{""type"":""object"",""properties"":{}}"),
    ///         ReadOnly    = true,
    ///         Owner       = "My Extension",
    ///         Handler     = args => MyHandler.CountTreesAsync(),
    ///     });
    /// </summary>
    public static class McpToolRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, McpToolDefinition> Tools =
            new Dictionary<string, McpToolDefinition>(StringComparer.Ordinal);

        /// <summary>Adds an extension tool and its route. A name the bridge already uses is refused.</summary>
        public static void Register(McpToolDefinition tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name) || tool.Handler == null)
                throw new ArgumentException("A tool needs a Name and a Handler.");

            if (McpToolCatalog.IsBuiltIn(tool.Name))
            {
                Debug.LogError($"[MCP Bridge] {tool.Owner} tried to register '{tool.Name}', which is a built-in tool. Choose another name.");
                return;
            }

            tool.Method      = string.IsNullOrWhiteSpace(tool.Method) ? "POST" : tool.Method.Trim().ToUpperInvariant();
            tool.Path        = string.IsNullOrWhiteSpace(tool.Path) ? $"/tools/ext/{tool.Name}" : tool.Path.Trim();
            tool.Owner       = string.IsNullOrWhiteSpace(tool.Owner) ? "Extension" : tool.Owner;
            tool.Summary     = string.IsNullOrWhiteSpace(tool.Summary) ? tool.Name : tool.Summary.Trim();
            tool.InputSchema = tool.InputSchema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() };

            lock (Gate)
            {
                if (Tools.ContainsKey(tool.Name))
                    Debug.LogWarning($"[MCP Bridge] {tool.Owner} replaced the tool '{tool.Name}'.");
                Tools[tool.Name] = tool;
            }

            var method  = tool.Method;
            var handler = tool.Handler;
            McpRouteRegistry.Register(method, tool.Path,
                async request => await handler(await ReadArgumentsAsync(request, method)),
                tool.Owner);
        }

        /// <summary>Finds an extension tool by name. Safe to call from any thread.</summary>
        public static bool TryGet(string name, out McpToolDefinition tool)
        {
            lock (Gate)
                return Tools.TryGetValue(name ?? string.Empty, out tool);
        }

        /// <summary>A snapshot of every extension tool.</summary>
        public static List<McpToolDefinition> GetTools()
        {
            lock (Gate)
                return new List<McpToolDefinition>(Tools.Values);
        }

        /// <summary>Runs an extension tool by name with JSON arguments.</summary>
        public static async Task<McpResponse> CallAsync(string name, JObject arguments)
        {
            if (!TryGet(name, out var tool))
            {
                return McpToolCatalog.IsBuiltIn(name)
                    ? McpResponse.Fail("NOT_AN_EXTENSION_TOOL",
                        $"'{name}' is a built-in tool: call it directly rather than through call_unity_tool.")
                    : McpResponse.Fail("NOT_FOUND", $"No tool named '{name}'. list_unity_tools shows the extension tools.");
            }
            return await tool.Handler(arguments ?? new JObject());
        }

        /// <summary>The tool's arguments from a request: the JSON body (POST) or the query string (GET).</summary>
        private static async Task<JObject> ReadArgumentsAsync(McpRequest request, string method)
        {
            if (method == "GET")
            {
                var query = new JObject();
                foreach (var key in request.QueryString.AllKeys)
                    if (key != null) query[key] = request.QueryString[key];
                return query;
            }
            return await request.ReadJsonAsync();
        }
    }
}
