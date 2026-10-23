using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles patrol route operations:
    ///
    ///   POST /tools/scaffold/create_patrol_route — create a named route with waypoints
    ///   GET  /assets/scaffold/patrol_routes      — list all routes in the active scene
    ///
    /// Patrol routes are stored as scene hierarchy structures:
    ///
    ///   PatrolRoute_{name}          ← container, PatrolRouteData component
    ///     WP_00                     ← waypoint, PatrolWaypoint component
    ///     WP_01_{label}             ← waypoint with label (visible in hierarchy)
    ///     ...
    ///
    /// This is framework-agnostic: the game's AI system can discover routes at runtime
    /// by finding GameObjects named "PatrolRoute_*" and reading child positions, without
    /// any dependency on the bridge's editor-only components. See PatrolRouteComponents.cs
    /// for runtime integration notes.
    /// </summary>
    public static class PatrolRouteHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/scaffold/create_patrol_route
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreatePatrolRouteAsync(
            CreatePatrolRouteRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Name))
                    return McpResponse.Fail("INVALID_PARAMS", "name must not be empty.");

                if (req.Waypoints == null || req.Waypoints.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "waypoints must contain at least one entry.");

                // Validate that all waypoints have positions
                for (int i = 0; i < req.Waypoints.Length; i++)
                {
                    if (req.Waypoints[i]?.Position == null || req.Waypoints[i].Position.Length < 3)
                        return McpResponse.Fail("INVALID_PARAMS",
                            $"Waypoint {i} is missing a valid position [x, y, z].");
                }

                // ── Resolve parent ────────────────────────────────────────────
                // zone_path takes priority over parent_path
                GameObject parent = null;

                if (!string.IsNullOrEmpty(req.ZonePath))
                {
                    parent = PlacementHandler.FindByPath(req.ZonePath);
                    if (parent == null)
                        return McpResponse.Fail("ZONE_NOT_FOUND",
                            $"No object found at zone_path '{req.ZonePath}'. " +
                            "Create the scaffold zone first, or use parent_path for a non-zone parent.");
                }
                else if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object found at parent_path '{req.ParentPath}'.");
                }

                // ── Create route container ────────────────────────────────────
                var containerName = $"PatrolRoute_{req.Name}";
                var container     = new GameObject(containerName);
                Undo.RegisterCreatedObjectUndo(container, "MCP Create Patrol Route");

                if (parent != null)
                    container.transform.SetParent(parent.transform, worldPositionStays: false);

                var routeData             = container.AddComponent<PatrolRouteData>();
                routeData.loop            = req.Loop ?? true;
                routeData.defaultIdleTime = req.DefaultIdleTime > 0 ? req.DefaultIdleTime : 1f;
                routeData.zonePath        = req.ZonePath ?? string.Empty;

                // ── Create waypoint children ──────────────────────────────────
                var waypointResults = new List<WaypointResultItem>(req.Waypoints.Length);

                for (int i = 0; i < req.Waypoints.Length; i++)
                {
                    var def = req.Waypoints[i];

                    // Name: "WP_00" or "WP_00_label" when a label is provided
                    var wpName = string.IsNullOrWhiteSpace(def.Label)
                        ? $"WP_{i:D2}"
                        : $"WP_{i:D2}_{SanitizeLabel(def.Label)}";

                    var wpGo = new GameObject(wpName);
                    Undo.RegisterCreatedObjectUndo(wpGo, "MCP Create Waypoint");
                    wpGo.transform.SetParent(container.transform, worldPositionStays: false);
                    wpGo.transform.position = new Vector3(
                        def.Position[0], def.Position[1], def.Position[2]);

                    var wp          = wpGo.AddComponent<PatrolWaypoint>();
                    wp.index        = i;
                    wp.idleTime     = def.IdleTime ?? -1f;
                    wp.label        = def.Label ?? string.Empty;

                    if (def.LookDirection != null && def.LookDirection.Length >= 3)
                    {
                        wp.hasLookDirection = true;
                        wp.lookDirection    = new Vector3(
                            def.LookDirection[0], def.LookDirection[1], def.LookDirection[2]);
                    }

                    waypointResults.Add(new WaypointResultItem
                    {
                        Path  = PlacementHandler.GetPath(wpGo),
                        Label = string.IsNullOrEmpty(wp.label) ? null : wp.label
                    });
                }

                return McpResponse.Ok(new CreatePatrolRouteResultData
                {
                    RoutePath     = PlacementHandler.GetPath(container),
                    WaypointCount = req.Waypoints.Length,
                    Loop          = routeData.loop,
                    ZonePath      = string.IsNullOrEmpty(req.ZonePath) ? null : req.ZonePath,
                    Waypoints     = waypointResults.ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // GET /assets/scaffold/patrol_routes
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetPatrolRoutesAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var allRoutes = Object.FindObjectsOfType<PatrolRouteData>();
                var results   = new List<PatrolRouteResultItem>(allRoutes.Length);

                foreach (var route in allRoutes)
                {
                    var waypoints = route.GetComponentsInChildren<PatrolWaypoint>(includeInactive: true);

                    // Sort by index field, fall back to sibling order if indices are equal
                    var sorted = waypoints
                        .OrderBy(wp => wp.index)
                        .ThenBy(wp => wp.transform.GetSiblingIndex())
                        .ToArray();

                    var wpData = sorted.Select(wp => new WaypointDataItem
                    {
                        Path     = PlacementHandler.GetPath(wp.gameObject),
                        Index    = wp.index,
                        Position = new[]
                        {
                            wp.transform.position.x,
                            wp.transform.position.y,
                            wp.transform.position.z
                        },
                        // Resolve effective idle time: per-waypoint or route default
                        IdleTime      = wp.idleTime >= 0f ? wp.idleTime : route.defaultIdleTime,
                        Label         = string.IsNullOrEmpty(wp.label)        ? null : wp.label,
                        LookDirection = wp.hasLookDirection
                            ? new[] { wp.lookDirection.x, wp.lookDirection.y, wp.lookDirection.z }
                            : null
                    }).ToArray();

                    // Derive a clean display name from the container's GameObject name
                    var routeName = route.gameObject.name.StartsWith("PatrolRoute_")
                        ? route.gameObject.name.Substring("PatrolRoute_".Length)
                        : route.gameObject.name;

                    results.Add(new PatrolRouteResultItem
                    {
                        RoutePath     = PlacementHandler.GetPath(route.gameObject),
                        Name          = routeName,
                        ZonePath      = string.IsNullOrEmpty(route.zonePath) ? null : route.zonePath,
                        Loop          = route.loop,
                        WaypointCount = sorted.Length,
                        Waypoints     = wpData
                    });
                }

                return McpResponse.Ok(new PatrolRouteListData { Routes = results.ToArray() });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Removes characters from a label that would make a GameObject name
        /// unusable in FindByPath (spaces become underscores; slashes are removed).
        /// </summary>
        private static string SanitizeLabel(string label) =>
            label.Replace(' ', '_').Replace('/', '-').Replace('\\', '-');
    }
}
