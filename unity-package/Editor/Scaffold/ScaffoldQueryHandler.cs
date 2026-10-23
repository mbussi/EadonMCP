using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LevelScaffold;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles read-only scaffold queries.
    ///
    /// Routes to add in McpRouter:
    ///   GET /assets/scaffold/zones    → GetZonesAsync()
    ///   GET /assets/scaffold/anchors  → GetAnchorsAsync()
    ///   GET /assets/scaffold/markers  → GetMarkersAsync()
    /// </summary>
    public static class ScaffoldQueryHandler
    {
        // -----------------------------------------------------------------------
        // GET /assets/scaffold/zones
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetZonesAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var zones    = FindAll<ScaffoldZone>();
                var entries  = new List<ScaffoldZoneData>(zones.Length);

                foreach (var zone in zones)
                {
                    var anchors = zone.GetComponentsInChildren<ScaffoldAnchor>(true);
                    var markers = zone.GetComponentsInChildren<GameplayMarker>(true);
                    var t       = zone.transform;

                    entries.Add(new ScaffoldZoneData
                    {
                        ZoneName     = zone.gameObject.name,
                        ZonePath     = PlacementHandler.GetPath(zone.gameObject),
                        ZoneType     = zone.ZoneType.ToString(),
                        DesignNote   = zone.DesignNote,
                        DressingTags = zone.DressingTags ?? Array.Empty<string>(),
                        AnchorCount  = anchors.Length,
                        MarkerCount  = markers.Length,
                        Position     = V3(t.position),
                        Rotation     = V3(t.eulerAngles),
                    });
                }

                return McpResponse.Ok(new ScaffoldZoneListData
                {
                    Zones = entries.ToArray(),
                    Count = entries.Count,
                });
            });

        // -----------------------------------------------------------------------
        // GET /assets/scaffold/anchors
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetAnchorsAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var anchors = FindAll<ScaffoldAnchor>();
                var entries = new List<ScaffoldAnchorData>(anchors.Length);

                foreach (var anchor in anchors)
                {
                    // Walk up to find the containing ScaffoldZone
                    var zone     = anchor.GetComponentInParent<ScaffoldZone>();
                    var zonePath = zone != null
                        ? PlacementHandler.GetPath(zone.gameObject)
                        : string.Empty;

                    entries.Add(new ScaffoldAnchorData
                    {
                        Label      = anchor.AnchorLabel,
                        AnchorPath = PlacementHandler.GetPath(anchor.gameObject),
                        AnchorType = anchor.AnchorType.ToString(),
                        Position   = V3(anchor.transform.position),
                        ZonePath   = zonePath,
                    });
                }

                return McpResponse.Ok(new ScaffoldAnchorListData
                {
                    Anchors = entries.ToArray(),
                    Count   = entries.Count,
                });
            });

        // -----------------------------------------------------------------------
        // GET /assets/scaffold/markers
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetMarkersAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var markers = FindAll<GameplayMarker>();
                var entries = new List<ScaffoldMarkerData>(markers.Length);

                foreach (var marker in markers)
                {
                    var zone     = marker.GetComponentInParent<ScaffoldZone>();
                    var zonePath = zone != null
                        ? PlacementHandler.GetPath(zone.gameObject)
                        : string.Empty;

                    entries.Add(new ScaffoldMarkerData
                    {
                        Label           = marker.MarkerLabel,
                        MarkerPath      = PlacementHandler.GetPath(marker.gameObject),
                        MarkerType      = marker.MarkerType.ToString(),
                        SemanticTags    = marker.SemanticTags ?? Array.Empty<string>(),
                        DetectionRadius = marker.DetectionRadius,
                        BakeState       = marker.BakeState.ToString(),
                        Position        = V3(marker.transform.position),
                        ZonePath        = zonePath,
                    });
                }

                return McpResponse.Ok(new ScaffoldMarkerListData
                {
                    Markers = entries.ToArray(),
                    Count   = entries.Count,
                });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static T[] FindAll<T>() where T : Component
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(includeInactive: true);
#endif
        }

        private static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
