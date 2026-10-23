using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// The in-editor agent's tools: every tool in McpToolCatalog (the built-in tools,
    /// describe_tool, and the tools extensions register), with the route each one calls.
    ///
    /// By default each tool is sent with its short summary plus a pointer to
    /// describe_tool, which returns the full usage guide on demand. That keeps the tool
    /// list at a size local models can hold. AgentSettings.fullToolDescriptions sends
    /// the full guides instead.
    /// </summary>
    public static class ToolRegistry
    {
        /// <summary>The tool list for one request.</summary>
        public static List<ToolSchema> GetAll(bool fullDescriptions)
        {
            var schemas = new List<ToolSchema>();
            foreach (var tool in McpToolCatalog.GetAll())
            {
                schemas.Add(new ToolSchema
                {
                    Name        = tool.Name,
                    Description = fullDescriptions ? tool.FullDescription : ShortDescription(tool),
                    InputSchema = tool.InputSchema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() },
                });
            }
            return schemas;
        }

        /// <summary>The bridge route a tool calls.</summary>
        public static bool TryGetRoute(string toolName, out string method, out string path)
        {
            if (McpToolCatalog.TryGet(toolName, out var tool) && !string.IsNullOrEmpty(tool.Path))
            {
                method = tool.Method;
                path   = tool.Path;
                return true;
            }
            method = path = null;
            return false;
        }

        /// <summary>True when the agent must ask the user before running this tool.</summary>
        public static bool NeedsConfirmation(string toolName, bool confirmAllChanges) =>
            McpToolCatalog.NeedsConfirmation(toolName, confirmAllChanges);

        private static string ShortDescription(McpToolDefinition tool)
        {
            var hasMore = !string.IsNullOrWhiteSpace(tool.Description) &&
                          tool.Description.Trim().Length > (tool.Summary ?? string.Empty).Trim().Length;
            return hasMore
                ? $"{tool.Summary}\n\nFull guide with examples: {McpToolCatalog.DescribeToolName}(\"{tool.Name}\")."
                : tool.Summary;
        }
    }
}
