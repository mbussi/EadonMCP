using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles bulk and procedural placement operations.
    ///
    /// All methods group their operations under a single named Undo entry so
    /// the entire batch can be reversed with one Ctrl+Z.
    /// </summary>
    public static class ProceduralHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/placement/place_batch
        // -----------------------------------------------------------------------

        public static Task<McpResponse> PlacePrefabBatchAsync(PlacePrefabBatchRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Items == null || req.Items.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "items must not be empty.");

                var instancePaths = new List<string>();
                var failures      = new List<BatchFailure>();

                if (!req.DryRun)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName($"MCP Batch Place ({req.Items.Length} objects)");
                }

                for (int i = 0; i < req.Items.Length; i++)
                {
                    var item = req.Items[i];
                    var (path, error) = PlaceOneItem(item, req.DryRun);
                    if (error != null)
                        failures.Add(new BatchFailure { Index = i, Code = error.Code, Message = error.Message });
                    else
                        instancePaths.Add(path);
                }

                return McpResponse.Ok(new BatchResultData
                {
                    PlacedCount   = instancePaths.Count,
                    FailedCount   = failures.Count,
                    InstancePaths = instancePaths.ToArray(),
                    Failures      = failures.Count > 0 ? failures.ToArray() : null,
                    DryRun        = req.DryRun
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/procedural/scatter
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ScatterPrefabsAsync(ScatterPrefabsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.PrefabPath);
                if (prefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND", $"No prefab at '{req.PrefabPath}'.");

                if (req.Radius <= 0)
                    return McpResponse.Fail("INVALID_PARAMS", "radius must be positive.");
                if (req.Count  <= 0)
                    return McpResponse.Fail("INVALID_PARAMS", "count must be positive.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND", $"No object at path '{req.ParentPath}'.");
                }

                var center    = ToVector3(req.Center);
                var positions = GenerateScatterPositions(
                    center, req.Radius, req.Count, req.MinSpacing, req.AlignToSurface, req.Seed);

                if (req.DryRun)
                    return McpResponse.Ok(new ProceduralResultData
                        { PlacedCount = positions.Count, InstancePaths = Array.Empty<string>(), DryRun = true });

                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"MCP Scatter {positions.Count}× {prefab.name}");

                var paths = new List<string>();
                foreach (var pos in positions)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = pos;
                    if (parent != null) go.transform.SetParent(parent.transform, worldPositionStays: true);
                    Undo.RegisterCreatedObjectUndo(go, string.Empty);
                    paths.Add(PlacementHandler.GetPath(go));
                }

                return McpResponse.Ok(new ProceduralResultData
                    { PlacedCount = paths.Count, InstancePaths = paths.ToArray(), DryRun = false });
            });

        // -----------------------------------------------------------------------
        // POST /tools/procedural/along_path
        // -----------------------------------------------------------------------

        public static Task<McpResponse> PlaceAlongPathAsync(PlaceAlongPathRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.PrefabPath);
                if (prefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND", $"No prefab at '{req.PrefabPath}'.");

                if (req.Waypoints == null || req.Waypoints.Length < 2)
                    return McpResponse.Fail("INVALID_PARAMS", "waypoints must contain at least 2 points.");
                if (req.Spacing <= 0)
                    return McpResponse.Fail("INVALID_PARAMS", "spacing must be positive.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND", $"No object at path '{req.ParentPath}'.");
                }

                var waypoints = req.Waypoints.Select(w => new Vector3(w[0], w[1], w[2])).ToArray();
                var placements = ComputePathPlacements(waypoints, req.Spacing, req.AlignToPath);
                float pathLength = ComputePathLength(waypoints);

                if (req.DryRun)
                    return McpResponse.Ok(new ProceduralResultData
                        { PlacedCount = placements.Count, PathLength = pathLength,
                          InstancePaths = Array.Empty<string>(), DryRun = true });

                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"MCP Path {placements.Count}× {prefab.name}");

                var paths = new List<string>();
                foreach (var (pos, rot) in placements)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = pos;
                    go.transform.rotation = rot;
                    if (parent != null) go.transform.SetParent(parent.transform, worldPositionStays: true);
                    Undo.RegisterCreatedObjectUndo(go, string.Empty);
                    paths.Add(PlacementHandler.GetPath(go));
                }

                return McpResponse.Ok(new ProceduralResultData
                    { PlacedCount = paths.Count, PathLength = pathLength,
                      InstancePaths = paths.ToArray(), DryRun = false });
            });

        // -----------------------------------------------------------------------
        // POST /tools/procedural/fill_grid
        // -----------------------------------------------------------------------

        public static Task<McpResponse> FillGridAsync(FillGridRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.PrefabPath);
                if (prefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND", $"No prefab at '{req.PrefabPath}'.");

                if (req.Columns <= 0) return McpResponse.Fail("INVALID_PARAMS", "columns must be positive.");
                if (req.Rows    <= 0) return McpResponse.Fail("INVALID_PARAMS", "rows must be positive.");
                if (req.CellSize == null || req.CellSize.Length < 2 || req.CellSize[0] <= 0 || req.CellSize[1] <= 0)
                    return McpResponse.Fail("INVALID_PARAMS", "cell_size must be [width, depth] with positive values.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND", $"No object at path '{req.ParentPath}'.");
                }

                var origin   = ToVector3(req.Origin);
                float cellW  = req.CellSize[0];
                float cellD  = req.CellSize[1];
                var positions = new List<Vector3>();

                for (int row = 0; row < req.Rows; row++)
                {
                    float rowOffset = req.OffsetAlternateRows && row % 2 == 1 ? cellW * 0.5f : 0f;
                    for (int col = 0; col < req.Columns; col++)
                        positions.Add(new Vector3(
                            origin.x + col * cellW + rowOffset,
                            origin.y,
                            origin.z + row * cellD));
                }

                if (req.DryRun)
                    return McpResponse.Ok(new ProceduralResultData
                        { PlacedCount = positions.Count, InstancePaths = Array.Empty<string>(), DryRun = true });

                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"MCP Grid {req.Columns}×{req.Rows} {prefab.name}");

                var paths = new List<string>();
                foreach (var pos in positions)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = pos;
                    if (parent != null) go.transform.SetParent(parent.transform, worldPositionStays: true);
                    Undo.RegisterCreatedObjectUndo(go, string.Empty);
                    paths.Add(PlacementHandler.GetPath(go));
                }

                return McpResponse.Ok(new ProceduralResultData
                    { PlacedCount = paths.Count, InstancePaths = paths.ToArray(), DryRun = false });
            });

        // -----------------------------------------------------------------------
        // Algorithms
        // -----------------------------------------------------------------------

        private static List<Vector3> GenerateScatterPositions(
            Vector3 center, float radius, int count, float minSpacing,
            bool alignToSurface, int? seed)
        {
            var rng     = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            var placed  = new List<Vector3>();
            int maxAttempts = count * 15;
            int attempts    = 0;

            while (placed.Count < count && attempts < maxAttempts)
            {
                attempts++;

                // Uniform disk sampling
                float angle = (float)(rng.NextDouble() * 2 * Math.PI);
                float r     = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                float x     = center.x + r * Mathf.Cos(angle);
                float z     = center.z + r * Mathf.Sin(angle);
                float y     = center.y;

                if (alignToSurface)
                {
                    var ray = new Ray(new Vector3(x, center.y + 1000f, z), Vector3.down);
                    if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                        y = hit.point.y;
                }

                var candidate = new Vector3(x, y, z);

                if (minSpacing > 0 && placed.Any(p => Vector3.Distance(p, candidate) < minSpacing))
                    continue;

                placed.Add(candidate);
            }

            return placed;
        }

        private static List<(Vector3 pos, Quaternion rot)> ComputePathPlacements(
            Vector3[] waypoints, float spacing, bool alignToPath)
        {
            var result      = new List<(Vector3, Quaternion)>();
            float accumulated = 0f;
            float nextPlace  = 0f;

            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                Vector3 from   = waypoints[i];
                Vector3 to     = waypoints[i + 1];
                float   segLen = Vector3.Distance(from, to);
                if (segLen < 0.0001f) continue;

                Vector3    dir = (to - from).normalized;
                Quaternion rot = alignToPath ? Quaternion.LookRotation(dir) : Quaternion.identity;

                while (nextPlace <= accumulated + segLen)
                {
                    float   t   = nextPlace - accumulated;
                    Vector3 pos = from + dir * Mathf.Min(t, segLen);
                    result.Add((pos, rot));
                    nextPlace += spacing;
                }

                accumulated += segLen;
            }

            return result;
        }

        private static float ComputePathLength(Vector3[] waypoints)
        {
            float len = 0f;
            for (int i = 0; i < waypoints.Length - 1; i++)
                len += Vector3.Distance(waypoints[i], waypoints[i + 1]);
            return len;
        }

        // -----------------------------------------------------------------------
        // Batch helper
        // -----------------------------------------------------------------------

        private static (string path, McpError error) PlaceOneItem(BatchPlacementItem item, bool dryRun)
        {
            if (string.IsNullOrWhiteSpace(item?.PrefabPath))
                return (null, new McpError { Code = "INVALID_PARAMS", Message = "prefab_path missing in item." });

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.PrefabPath);
            if (prefab == null)
                return (null, new McpError { Code = "PREFAB_NOT_FOUND", Message = $"No prefab at '{item.PrefabPath}'." });

            GameObject parent = null;
            if (!string.IsNullOrEmpty(item.ParentPath))
            {
                parent = PlacementHandler.FindByPath(item.ParentPath);
                if (parent == null)
                    return (null, new McpError { Code = "PARENT_NOT_FOUND", Message = $"No object at '{item.ParentPath}'." });
            }

            var position = ToVector3(item.Position);
            var rotation = Quaternion.Euler(ToVector3(item.Rotation));
            var scale    = item.Scale != null ? ToVector3(item.Scale) : Vector3.one;

            if (dryRun)
            {
                string expectedPath = parent == null ? prefab.name : PlacementHandler.GetPath(parent) + "/" + prefab.name;
                return (expectedPath, null);
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position   = position;
            go.transform.rotation   = rotation;
            go.transform.localScale = scale;
            if (parent != null) go.transform.SetParent(parent.transform, worldPositionStays: true);
            Undo.RegisterCreatedObjectUndo(go, string.Empty);
            return (PlacementHandler.GetPath(go), null);
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        private static Vector3 ToVector3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
