using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityMcpBridge.Editor.Core;

#if AI_NAVIGATION
using Unity.AI.Navigation;
#endif

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles NavMesh operations for the Unity AI Navigation package
    /// (com.unity.ai.navigation — NavMeshSurface workflow).
    ///
    ///   POST /tools/world/bake_navmesh     — build NavMeshSurface components
    ///   POST /tools/world/set_navmesh_area — assign area types via NavMeshModifier
    ///                                        or create a NavMeshModifierVolume
    ///
    /// Both routes compile to graceful PACKAGE_NOT_INSTALLED failures when the
    /// com.unity.ai.navigation package is absent. The AI_NAVIGATION scripting
    /// define is set via versionDefines in the bridge .asmdef.
    /// </summary>
    public static class NavMeshHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/world/bake_navmesh
        // -----------------------------------------------------------------------

        public static Task<McpResponse> BakeNavMeshAsync(BakeNavMeshRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
#if !AI_NAVIGATION
                return McpResponse.Fail("PACKAGE_NOT_INSTALLED",
                    "com.unity.ai.navigation is required for NavMesh tools. " +
                    "Install it via Window > Package Manager.");
#else
                var surfaces = FindSurfaces(req);
                if (surfaces.Count == 0)
                    return McpResponse.Fail("NO_SURFACE_FOUND",
                        "No NavMeshSurface components found matching the request. " +
                        "Add a NavMeshSurface component to a scene object, or use all=true " +
                        "to target every surface in the active scene.");

                var resultItems = new List<NavMeshBakeResultItem>(surfaces.Count);
                var warnings    = new List<string>();

                foreach (var surface in surfaces)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    surface.BuildNavMesh();
                    sw.Stop();

                    EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);

                    resultItems.Add(new NavMeshBakeResultItem
                    {
                        ObjectPath = PlacementHandler.GetPath(surface.gameObject),
                        AgentType  = NavMesh.GetSettingsNameFromID(surface.agentTypeID),
                        ElapsedMs  = sw.ElapsedMilliseconds
                    });
                }

                return McpResponse.Ok(new BakeNavMeshResultData
                {
                    BakedCount = resultItems.Count,
                    Surfaces   = resultItems.ToArray(),
                    Warnings   = warnings.ToArray()
                });
#endif
            });

        // -----------------------------------------------------------------------
        // POST /tools/world/set_navmesh_area
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetNavMeshAreaAsync(SetNavMeshAreaRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
#if !AI_NAVIGATION
                return McpResponse.Fail("PACKAGE_NOT_INSTALLED",
                    "com.unity.ai.navigation is required for NavMesh tools. " +
                    "Install it via Window > Package Manager.");
#else
                if (string.IsNullOrWhiteSpace(req?.AreaName))
                    return McpResponse.Fail("INVALID_PARAMS", "area_name must not be empty.");

                if ((req.ObjectPaths == null || req.ObjectPaths.Length == 0) && req.Volume == null)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "At least one of object_paths or volume must be provided.");

                // Resolve area name → index
                var areaIndex = NavMesh.GetAreaFromName(req.AreaName);
                if (areaIndex == -1)
                {
                    var available = string.Join(", ", NavMesh.GetAreaNames());
                    return McpResponse.Fail("AREA_NOT_FOUND",
                        $"Area '{req.AreaName}' not found in project settings. " +
                        $"Available areas: {available}");
                }

                var applied        = new List<NavMeshAreaAppliedData>();
                var volumesCreated = new List<NavMeshAreaAppliedData>();
                var warnings       = new List<string>();

                // ── Apply NavMeshModifier to named objects ────────────────────
                if (req.ObjectPaths != null)
                {
                    foreach (var path in req.ObjectPaths)
                    {
                        var go = PlacementHandler.FindByPath(path);
                        if (go == null)
                        {
                            warnings.Add($"Object not found: '{path}' — skipped.");
                            continue;
                        }

                        // Reuse existing modifier or add a new one
                        var modifier = go.GetComponent<NavMeshModifier>();
                        if (modifier == null)
                            modifier = Undo.AddComponent<NavMeshModifier>(go);
                        else
                            Undo.RecordObject(modifier, "MCP Set NavMesh Area");

                        modifier.overrideArea = req.OverrideArea;
                        modifier.area         = areaIndex;
                        EditorUtility.SetDirty(go);

                        applied.Add(new NavMeshAreaAppliedData
                        {
                            Path      = path,
                            Mode      = "modifier",
                            Area      = req.AreaName,
                            AreaIndex = areaIndex
                        });
                    }
                }

                // ── Create NavMeshModifierVolume ──────────────────────────────
                if (req.Volume != null)
                {
                    var vol  = req.Volume;
                    var name = string.IsNullOrWhiteSpace(vol.Name)
                        ? $"NavMeshVolume_{req.AreaName.Replace(" ", "_")}"
                        : vol.Name;

                    GameObject parent = null;
                    if (!string.IsNullOrEmpty(vol.ParentPath))
                    {
                        parent = PlacementHandler.FindByPath(vol.ParentPath);
                        if (parent == null)
                            warnings.Add($"Parent path not found '{vol.ParentPath}' — creating at scene root.");
                    }

                    var container = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(container, "MCP Create NavMesh Volume");

                    if (parent != null)
                        container.transform.SetParent(parent.transform, worldPositionStays: false);

                    container.transform.position    = ToVector3(vol.Position);
                    container.transform.eulerAngles = ToVector3(vol.Rotation);

                    var volComponent  = Undo.AddComponent<NavMeshModifierVolume>(container);
                    volComponent.area = areaIndex;
                    volComponent.size = ToVector3(vol.Size);

                    volumesCreated.Add(new NavMeshAreaAppliedData
                    {
                        Path      = PlacementHandler.GetPath(container),
                        Mode      = "volume",
                        Area      = req.AreaName,
                        AreaIndex = areaIndex
                    });
                }

                return McpResponse.Ok(new SetNavMeshAreaResultData
                {
                    UpdatedCount   = applied.Count,
                    Applied        = applied.ToArray(),
                    VolumesCreated = volumesCreated.ToArray(),
                    Warnings       = warnings.ToArray()
                });
#endif
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

#if AI_NAVIGATION
        /// <summary>
        /// Resolves the request to a concrete list of NavMeshSurface components.
        /// Priority: explicit surface_paths → agent_type_name filter → all → first found.
        /// </summary>
        private static List<NavMeshSurface> FindSurfaces(BakeNavMeshRequest req)
        {
            var allSurfaces = UnityEngine.Object.FindObjectsOfType<NavMeshSurface>();

            // Explicit paths take highest priority
            if (req.SurfacePaths != null && req.SurfacePaths.Length > 0)
            {
                var result = new List<NavMeshSurface>();
                foreach (var path in req.SurfacePaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) continue;
                    var surface = go.GetComponent<NavMeshSurface>();
                    if (surface != null) result.Add(surface);
                }
                return result;
            }

            // Bake all surfaces in the scene
            if (req.All)
                return new List<NavMeshSurface>(allSurfaces);

            // Filter by agent type name
            if (!string.IsNullOrEmpty(req.AgentTypeName))
            {
                return allSurfaces
                    .Where(s => NavMesh.GetSettingsNameFromID(s.agentTypeID) == req.AgentTypeName)
                    .ToList();
            }

            // Default: bake only the first surface found
            return allSurfaces.Length > 0
                ? new List<NavMeshSurface> { allSurfaces[0] }
                : new List<NavMeshSurface>();
        }
#endif

        private static Vector3 ToVector3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
