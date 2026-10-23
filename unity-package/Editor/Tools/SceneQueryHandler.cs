using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles read-only scene and asset inspection endpoints.
    ///
    /// All public methods return Task&lt;McpResponse&gt; and are safe to call from
    /// any thread — Unity API access is marshalled via MainThreadDispatcher.
    /// </summary>
    public static class SceneQueryHandler
    {
        // -----------------------------------------------------------------------
        // GET /scene/hierarchy
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetSceneHierarchyAsync(int maxDepth) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var scene = SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();

                return McpResponse.Ok(new SceneHierarchyData
                {
                    SceneName   = scene.name,
                    ScenePath   = scene.path,
                    ObjectCount = CountAll(roots),
                    RootObjects = roots.Select(go => BuildNode(go, string.Empty, 0, maxDepth)).ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // GET /scene/context
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetSceneContextAsync(string zoneName = null) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var context = string.IsNullOrEmpty(zoneName)
                    ? ResolveSceneContext()
                    : ResolveZoneContext(zoneName);                if (context == null)
                    return McpResponse.Ok(new
                    {
                        configured = false,
                        message =
                            "No SceneContext asset is configured. " +
                            "Create one via Assets > Create > MCP Bridge > Scene Context, " +
                            "then assign it to the SceneContext field in McpBridgeSettings."
                    });

                return McpResponse.Ok(new SceneContextData
                {
                    LevelName          = context.levelName,
                    Setting            = context.setting,
                    ArchitecturalStyle = context.architecturalStyle,
                    Era                = context.era,
                    ActiveTags         = context.activeTags  ?? Array.Empty<string>(),
                    ExcludedTags       = context.excludedTags ?? Array.Empty<string>(),
                    Notes              = context.notes
                });
            });

        // -----------------------------------------------------------------------
        // GET /assets/prefabs
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetPrefabCatalogAsync(string searchPath) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(searchPath))
                    searchPath = "Assets";

                var guids   = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });
                var prefabs = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p)
                    .Select(BuildPrefabData)
                    .ToArray();

                return McpResponse.Ok(new PrefabCatalogData
                {
                    Prefabs      = prefabs,
                    TotalCount   = prefabs.Length,
                    WithMetadata = prefabs.Count(p => p.HasMetadata)
                });
            });

        // -----------------------------------------------------------------------
        // Helpers — scene hierarchy
        // -----------------------------------------------------------------------

        private static SceneObjectData BuildNode(GameObject go, string parentPath, int depth, int maxDepth)
        {
            string path = string.IsNullOrEmpty(parentPath)
                ? go.name
                : parentPath + "/" + go.name;

            var node = new SceneObjectData
            {
                Name       = go.name,
                Path       = path,
                Active     = go.activeSelf,
                Position   = Vec3(go.transform.position),
                Rotation   = Vec3(go.transform.eulerAngles),
                Scale      = Vec3(go.transform.localScale),
                Components = go.GetComponents<Component>()
                               .Select(c => c.GetType().Name)
                               .Where(n => n != "Transform")
                               .ToArray()
            };

            if (maxDepth < 0 || depth < maxDepth)
            {
                node.Children = Enumerable
                    .Range(0, go.transform.childCount)
                    .Select(i => BuildNode(go.transform.GetChild(i).gameObject, path, depth + 1, maxDepth))
                    .ToArray();
            }
            else
            {
                node.Children = Array.Empty<SceneObjectData>();
            }

            return node;
        }

        private static int CountAll(GameObject[] roots) =>
            roots.Sum(go => 1 + CountAll(
                Enumerable.Range(0, go.transform.childCount)
                           .Select(i => go.transform.GetChild(i).gameObject)
                           .ToArray()));

        private static float[] Vec3(Vector3 v) => new[] { v.x, v.y, v.z };

        // -----------------------------------------------------------------------
        // Helpers — prefab catalog
        // -----------------------------------------------------------------------

        private static PrefabData BuildPrefabData(string assetPath)
        {
            var meta = MetadataRegistry.ForPrefab(assetPath);

            var data = new PrefabData
            {
                AssetPath   = assetPath,
                DisplayName = meta?.displayName.Length > 0
                    ? meta.displayName
                    : Path.GetFileNameWithoutExtension(assetPath),
                HasMetadata = meta != null
            };

            if (meta == null) return data;

            data.Category         = meta.category;
            data.Tags             = meta.tags;
            data.Description      = meta.description;
            data.CompatibleWith   = meta.compatibleWith;
            data.IncompatibleWith = meta.incompatibleWith;
            data.ApproximateSize  = Vec3(meta.approximateSize);

            if (meta.placement != null)
            {
                data.Placement = new PlacementRulesData
                {
                    RequiresFlatGround = meta.placement.requiresFlatGround,
                    CanStack           = meta.placement.canStack,
                    IndoorOnly         = meta.placement.indoorOnly,
                    OutdoorOnly        = meta.placement.outdoorOnly,
                    MinimumSpacing     = meta.placement.minimumSpacing,
                    RecommendedSpacing = Vec3(meta.placement.recommendedSpacing)
                };
            }

            return data;
        }

        // -----------------------------------------------------------------------
        // Helpers — scene context
        // -----------------------------------------------------------------------

        private static SceneContext ResolveSceneContext()
        {
            // 1. Explicit reference in settings takes priority
            var ctx = McpBridgeSettings.GetOrCreate().sceneContext;
            if (ctx != null) return ctx;

            // 2. Auto-discover: use the first SceneContext asset found in the project
            var guids = AssetDatabase.FindAssets("t:SceneContext");
            if (guids.Length == 0) return null;

            return AssetDatabase.LoadAssetAtPath<SceneContext>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>
        /// Looks up a zone-specific SceneContext from McpBridgeSettings.zoneContexts.
        /// Falls back to the global ResolveSceneContext() if no matching entry exists.
        /// </summary>
        private static SceneContext ResolveZoneContext(string zoneName)
        {
            var settings = McpBridgeSettings.GetOrCreate();
            if (settings.zoneContexts != null)
            {
                foreach (var entry in settings.zoneContexts)
                {
                    if (entry?.context == null) continue;
                    if (string.Equals(entry.zoneName, zoneName, System.StringComparison.OrdinalIgnoreCase))
                        return entry.context;
                }
            }
            // No zone-specific context — fall back to global
            return ResolveSceneContext();
        }
    }
}
