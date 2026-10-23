using UnityEditor;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Registers the PCG tools with the bridge. This assembly only compiles when the
    /// Eadon MCP Bridge package is installed (see UnityMcpBridge.Pcg.Editor.asmdef), and it
    /// lives next to PCGFramework, so the bridge itself never depends on PCG.
    /// </summary>
    [InitializeOnLoad]
    internal static class PcgRoutes
    {
        private const string Owner = "PCG extension";

        static PcgRoutes()
        {
            McpRouteRegistry.Register("GET", "/assets/pcg/node-catalog",
                _ => PcgHandler.GetNodeCatalogAsync(), Owner);
            McpRouteRegistry.Register("GET", "/assets/pcg/graphs",
                _ => PcgHandler.GetGraphListAsync(), Owner);
            McpRouteRegistry.Register("POST", "/tools/pcg/validate-graph",
                async request => await PcgHandler.ValidateGraphAsync(
                    await McpRouter.ParseBodyAsync<PcgValidateRequest>(request)), Owner);
            McpRouteRegistry.Register("POST", "/tools/pcg/create-graph",
                async request => await PcgHandler.CreateGraphAsync(
                    await McpRouter.ParseBodyAsync<PcgCreateRequest>(request)), Owner);
            McpRouteRegistry.Register("POST", "/tools/pcg/search-prefabs",
                async request => await PcgHandler.SearchPrefabsAsync(
                    await McpRouter.ParseBodyAsync<PcgSearchPrefabsRequest>(request)), Owner);
        }
    }
}
