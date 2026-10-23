using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Runs a tool. Receives the tool's arguments as JSON (the request body for POST
    /// tools, the query string for GET tools) and may be called from any thread:
    /// use MainThreadDispatcher for Unity APIs.
    /// </summary>
    public delegate Task<McpResponse> McpToolHandler(JObject arguments);

    /// <summary>
    /// A tool the bridge offers: what it is called, what it does, what arguments it
    /// takes, and the route that runs it. Built-in tools come from
    /// Catalog/builtin-tools.json; extensions register theirs with McpToolRegistry.
    /// </summary>
    public sealed class McpToolDefinition
    {
        /// <summary>Unique tool name, snake_case, e.g. "create_pcg_graph".</summary>
        public string Name;

        /// <summary>One or two sentences: what the tool does. Sent to the agent by default.</summary>
        public string Summary;

        /// <summary>The full usage guide with examples. Returned by describe_tool. Optional.</summary>
        public string Description;

        /// <summary>JSON schema of the arguments (an object schema with "properties").</summary>
        public JObject InputSchema;

        /// <summary>"GET" or "POST". POST tools receive a JSON body; GET tools a query string.</summary>
        public string Method = "POST";

        /// <summary>The bridge route. Defaults to /tools/ext/{name} for extension tools.</summary>
        public string Path;

        /// <summary>True when the tool only reads. The agent never asks before running it.</summary>
        public bool ReadOnly;

        /// <summary>True when the agent must always ask before running it, whatever its settings.</summary>
        public bool AlwaysConfirm;

        /// <summary>Who registered the tool, e.g. "PCG extension". "Eadon MCP Bridge" for built-ins.</summary>
        public string Owner;

        /// <summary>Runs the tool. Null for built-in tools, which the router serves.</summary>
        public McpToolHandler Handler;

        /// <summary>True for the bridge's own tools.</summary>
        public bool IsBuiltIn => Handler == null;

        /// <summary>The full description, or the summary when there is none.</summary>
        public string FullDescription => string.IsNullOrWhiteSpace(Description) ? Summary : Description;
    }
}
