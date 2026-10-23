using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LevelScaffold;
using LevelScaffold.Editor;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles mutating scaffold operations.
    ///
    /// All operations are registered with Unity's Undo system.
    ///
    /// Routes to add in McpRouter:
    ///   POST /tools/scaffold/create_zone             → CreateZoneAsync(req)
    ///   POST /tools/scaffold/link_zones              → LinkZonesAsync(req)
    ///   POST /tools/scaffold/create_reference_point  → CreateReferencePointAsync(req)
    ///   POST /tools/scaffold/create_marker           → CreateMarkerAsync(req)
    ///   POST /tools/scaffold/update_marker           → UpdateMarkerAsync(req)
    ///   POST /tools/scaffold/hide_scaffold           → HideScaffoldAsync()
    ///   POST /tools/scaffold/show_scaffold           → ShowScaffoldAsync()
    /// </summary>
    public static class ScaffoldHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/scaffold/create_zone
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateZoneAsync(CreateZoneRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ZoneName))
                    return McpResponse.Fail("INVALID_PARAMS", "zone_name must not be empty.");

                if (!Enum.TryParse<ScaffoldZoneType>(req.ZoneType, ignoreCase: true, out var zoneType))
                    return McpResponse.Fail("INVALID_ZONE_TYPE",
                        $"Unknown zone_type '{req.ZoneType}'. Valid values: " +
                        string.Join(", ", Enum.GetNames(typeof(ScaffoldZoneType))));

                // Resolve optional parent
                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at path '{req.ParentPath}'. " +
                            "Use get_scene_hierarchy to find valid paths, or create the parent first.");
                }

                // Create the zone GameObject
                var go = new GameObject(req.ZoneName);
                Undo.RegisterCreatedObjectUndo(go, $"MCP Create Zone {req.ZoneName}");

                go.transform.position    = ToV3(req.EntryPosition);
                go.transform.eulerAngles = ToV3(req.EntryRotation);
                go.hideFlags             = HideFlags.DontSaveInBuild;

                if (parent != null)
                    go.transform.SetParent(parent.transform, worldPositionStays: true);

                // Add and configure ScaffoldZone component
                var zone = go.AddComponent<ScaffoldZone>();
                zone.ZoneType     = zoneType;
                zone.Dimensions   = BuildDimensions(req.Dimensions, zoneType);
                zone.DesignNote   = req.DesignNote   ?? string.Empty;
                zone.DressingTags = req.DressingTags ?? Array.Empty<string>();

                // Generate anchors
                ScaffoldAnchorGenerator.GenerateAnchors(zone);

                var anchors      = zone.GetComponentsInChildren<ScaffoldAnchor>(true);
                var anchorPaths  = anchors
                    .Select(a => PlacementHandler.GetPath(a.gameObject))
                    .ToArray();

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Undo.SetCurrentGroupName($"MCP Create Zone {req.ZoneName}");

                return McpResponse.Ok(new CreateZoneResultData
                {
                    ZonePath       = PlacementHandler.GetPath(go),
                    ZoneType       = zoneType.ToString(),
                    AnchorsCreated = anchors.Length,
                    AnchorPaths    = anchorPaths,
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/link_zones
        // -----------------------------------------------------------------------

        public static Task<McpResponse> LinkZonesAsync(LinkZonesRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.FromZonePath))
                    return McpResponse.Fail("INVALID_PARAMS", "from_zone_path is required.");
                if (string.IsNullOrWhiteSpace(req.ToZonePath))
                    return McpResponse.Fail("INVALID_PARAMS", "to_zone_path is required.");
                if (string.IsNullOrWhiteSpace(req.ConnectionLabel))
                    return McpResponse.Fail("INVALID_PARAMS", "connection_label is required.");

                var fromGo = PlacementHandler.FindByPath(req.FromZonePath);
                if (fromGo == null)
                    return McpResponse.Fail("ZONE_NOT_FOUND", $"No object at '{req.FromZonePath}'.");

                var toGo = PlacementHandler.FindByPath(req.ToZonePath);
                if (toGo == null)
                    return McpResponse.Fail("ZONE_NOT_FOUND", $"No object at '{req.ToZonePath}'.");

                var fromZone = fromGo.GetComponent<ScaffoldZone>();
                var toZone   = toGo.GetComponent<ScaffoldZone>();
                if (fromZone == null) return McpResponse.Fail("NOT_A_ZONE", $"'{req.FromZonePath}' has no ScaffoldZone component.");
                if (toZone   == null) return McpResponse.Fail("NOT_A_ZONE", $"'{req.ToZonePath}' has no ScaffoldZone component.");

                if (!Enum.TryParse<ScaffoldZoneType>(req.TransitionType, ignoreCase: true, out var transType))
                    transType = ScaffoldZoneType.StraightCorridor;

                // Determine connector entry position and facing direction
                var exitAnchor  = FindAnchorByLabel(fromZone, "_exit");
                var entryAnchor = FindAnchorByLabel(toZone,   "_entry");

                var connectorPos = exitAnchor != null
                    ? exitAnchor.transform.position
                    : fromGo.transform.position;

                var connectorFwd = Vector3.forward;
                if (exitAnchor != null && entryAnchor != null)
                {
                    var delta = entryAnchor.transform.position - exitAnchor.transform.position;
                    if (delta.sqrMagnitude > 0.001f) connectorFwd = delta.normalized;
                }
                else if (exitAnchor != null)
                {
                    connectorFwd = exitAnchor.transform.forward;
                }
                else
                {
                    connectorFwd = fromGo.transform.forward;
                }

                // Same parent as from_zone
                var connectorParent = fromGo.transform.parent;

                var connGo = new GameObject(req.ConnectionLabel);
                Undo.RegisterCreatedObjectUndo(connGo, $"MCP Link Zones {req.ConnectionLabel}");
                connGo.transform.position = connectorPos;
                connGo.transform.rotation = Quaternion.LookRotation(connectorFwd);
                connGo.hideFlags          = HideFlags.DontSaveInBuild;
                if (connectorParent != null)
                    connGo.transform.SetParent(connectorParent, worldPositionStays: true);

                var connZone = connGo.AddComponent<ScaffoldZone>();
                connZone.ZoneType   = transType;
                connZone.Dimensions = BuildDimensions(req.Dimensions, transType);
                ScaffoldAnchorGenerator.GenerateAnchors(connZone);

                // Link ConnectedZones arrays
                Undo.RecordObject(fromZone, "MCP Link Zones");
                Undo.RecordObject(toZone,   "MCP Link Zones");

                fromZone.ConnectedZones = AppendZone(fromZone.ConnectedZones, connZone);
                connZone.ConnectedZones = new ScaffoldZone[] { fromZone, toZone };
                toZone.ConnectedZones   = AppendZone(toZone.ConnectedZones, connZone);

                EditorUtility.SetDirty(fromGo);
                EditorUtility.SetDirty(toGo);
                EditorUtility.SetDirty(connGo);
                EditorSceneManager.MarkSceneDirty(fromGo.scene);
                Undo.SetCurrentGroupName($"MCP Link {fromZone.gameObject.name} → {toZone.gameObject.name}");

                var connExit  = FindAnchorByLabel(connZone,  "_exit");
                var toEntry   = FindAnchorByLabel(toZone,    "_entry");
                var fromExit  = exitAnchor;

                return McpResponse.Ok(new LinkZonesResultData
                {
                    ConnectionPath = PlacementHandler.GetPath(connGo),
                    FromAnchor     = fromExit != null ? PlacementHandler.GetPath(fromExit.gameObject) : string.Empty,
                    ToAnchor       = toEntry  != null ? PlacementHandler.GetPath(toEntry.gameObject)  : string.Empty,
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/create_reference_point
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateReferencePointAsync(CreateReferencePointRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Label))
                    return McpResponse.Fail("INVALID_PARAMS", "label is required.");

                // Resolve parent zone
                ScaffoldZone parentZone;
                Vector3      worldPos;

                if (!string.IsNullOrEmpty(req.AnchorPath))
                {
                    // Position relative to an existing anchor
                    var anchorGo = PlacementHandler.FindByPath(req.AnchorPath);
                    if (anchorGo == null)
                        return McpResponse.Fail("ANCHOR_NOT_FOUND", $"No object at '{req.AnchorPath}'.");
                    parentZone = anchorGo.GetComponentInParent<ScaffoldZone>();
                    worldPos   = anchorGo.transform.position + ToV3(req.PositionOffset);
                }
                else if (req.WorldPosition != null && req.WorldPosition.Length >= 3)
                {
                    worldPos   = ToV3(req.WorldPosition) + ToV3(req.PositionOffset);
                    parentZone = null;
                    if (!string.IsNullOrEmpty(req.ZonePath))
                    {
                        var zoneGo = PlacementHandler.FindByPath(req.ZonePath);
                        parentZone = zoneGo?.GetComponent<ScaffoldZone>();
                    }
                }
                else
                {
                    return McpResponse.Fail("INVALID_PARAMS",
                        "Provide either anchor_path (+ optional position_offset) " +
                        "or world_position to specify the anchor location.");
                }

                if (!Enum.TryParse<ScaffoldAnchorType>(req.AnchorType ?? "Named",
                    ignoreCase: true, out var anchorType))
                    anchorType = ScaffoldAnchorType.Named;

                // Create the anchor GameObject
                var go = new GameObject(req.Label);
                Undo.RegisterCreatedObjectUndo(go, $"MCP Create Reference Point {req.Label}");
                go.transform.position = worldPos;
                go.hideFlags          = HideFlags.DontSaveInBuild;

                if (parentZone != null)
                    go.transform.SetParent(parentZone.transform, worldPositionStays: true);

                var anchor = go.AddComponent<ScaffoldAnchor>();
                anchor.AnchorType  = anchorType;
                anchor.AnchorLabel = req.Label;

                EditorUtility.SetDirty(go);
                if (parentZone != null) EditorSceneManager.MarkSceneDirty(parentZone.gameObject.scene);

                return McpResponse.Ok(new CreateReferencePointResultData
                {
                    AnchorPath = PlacementHandler.GetPath(go),
                    AnchorType = anchorType.ToString(),
                    Position   = V3(worldPos),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/create_marker
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateMarkerAsync(CreateMarkerRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.MarkerLabel))
                    return McpResponse.Fail("INVALID_PARAMS", "marker_label is required.");

                // Resolve world position and parent zone
                Vector3     worldPos;
                ScaffoldZone parentZone = null;

                if (!string.IsNullOrEmpty(req.AnchorPath))
                {
                    var anchorGo = PlacementHandler.FindByPath(req.AnchorPath);
                    if (anchorGo == null)
                        return McpResponse.Fail("ANCHOR_NOT_FOUND",
                            $"No object at anchor_path '{req.AnchorPath}'. " +
                            "Call get_scaffold_anchors to list valid anchor paths.");

                    worldPos   = anchorGo.transform.position + ToV3(req.PositionOffset);
                    parentZone = anchorGo.GetComponentInParent<ScaffoldZone>();
                }
                else if (req.WorldPosition != null && req.WorldPosition.Length >= 3)
                {
                    worldPos = ToV3(req.WorldPosition) + ToV3(req.PositionOffset);
                }
                else
                {
                    return McpResponse.Fail("INVALID_PARAMS",
                        "Provide anchor_path or world_position to place the marker.");
                }

                if (!Enum.TryParse<GameplayMarkerType>(req.MarkerType ?? "Custom",
                    ignoreCase: true, out var markerType))
                    markerType = GameplayMarkerType.Custom;

                // Create marker GameObject
                var go = new GameObject(req.MarkerLabel);
                Undo.RegisterCreatedObjectUndo(go, $"MCP Create Marker {req.MarkerLabel}");
                go.transform.position = worldPos;
                go.hideFlags          = HideFlags.DontSaveInBuild;

                // Parent: zone container (sibling of anchors), not the anchor itself
                if (parentZone != null)
                    go.transform.SetParent(parentZone.transform, worldPositionStays: true);

                // Configure marker
                var marker = go.AddComponent<GameplayMarker>();
                marker.MarkerType      = markerType;
                marker.MarkerLabel     = req.MarkerLabel;
                marker.SemanticTags    = req.SemanticTags    ?? Array.Empty<string>();
                marker.DetectionRadius = req.DetectionRadius;
                marker.BakeState       = BakeState.Pending;
                marker.ConfigJson      = req.Config != null
                    ? req.Config.ToString(Formatting.Indented)
                    : string.Empty;

                EditorUtility.SetDirty(go);
                if (parentZone != null)
                    EditorSceneManager.MarkSceneDirty(parentZone.gameObject.scene);

                return McpResponse.Ok(new CreateMarkerResultData
                {
                    MarkerPath = PlacementHandler.GetPath(go),
                    MarkerType = markerType.ToString(),
                    Position   = V3(worldPos),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/update_marker
        // -----------------------------------------------------------------------

        public static Task<McpResponse> UpdateMarkerAsync(UpdateMarkerRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.MarkerPath))
                    return McpResponse.Fail("INVALID_PARAMS", "marker_path is required.");

                var go = PlacementHandler.FindByPath(req.MarkerPath);
                if (go == null)
                    return McpResponse.Fail("MARKER_NOT_FOUND",
                        $"No object at '{req.MarkerPath}'. " +
                        "Call get_scaffold_markers to list valid paths.");

                var marker = go.GetComponent<GameplayMarker>();
                if (marker == null)
                    return McpResponse.Fail("NOT_A_MARKER",
                        $"'{req.MarkerPath}' does not have a GameplayMarker component.");

                Undo.RecordObject(marker, $"MCP Update Marker {marker.MarkerLabel}");

                var updated = new List<string>();

                if (req.SemanticTags != null)
                {
                    marker.SemanticTags = req.SemanticTags;
                    updated.Add("semantic_tags");
                }
                if (req.Config != null)
                {
                    marker.ConfigJson = req.Config.ToString(Formatting.Indented);
                    updated.Add("config");
                }
                if (req.DetectionRadius.HasValue)
                {
                    marker.DetectionRadius = req.DetectionRadius.Value;
                    updated.Add("detection_radius");
                }

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);

                return McpResponse.Ok(new UpdateMarkerResultData
                {
                    MarkerPath    = PlacementHandler.GetPath(go),
                    UpdatedFields = updated.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/hide_scaffold
        // POST /tools/scaffold/show_scaffold
        // -----------------------------------------------------------------------

        public static Task<McpResponse> HideScaffoldAsync() => SetScaffoldActiveAsync(false);
        public static Task<McpResponse> ShowScaffoldAsync() => SetScaffoldActiveAsync(true);

        /// <summary>
        /// Scaffold zones, anchors and markers are drawn as gizmos only, so hiding them is
        /// turning their gizmos off (the Level Scaffold overlay's switches). Zones stay
        /// active, so baked content parented under a zone stays visible.
        ///
        /// show_scaffold also reactivates root zones that an earlier version of
        /// hide_scaffold deactivated.
        /// </summary>
        private static Task<McpResponse> SetScaffoldActiveAsync(bool active) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                global::LevelScaffold.Editor.ScaffoldOverlay.SetAllVisible(active);

                var zones = FindAll<ScaffoldZone>();
                int count = zones.Length;
                int reactivated = 0;
                if (active)
                {
                    foreach (var zone in zones)
                    {
                        if (zone.gameObject.activeSelf) continue;
                        if (zone.transform.parent != null && zone.transform.parent.GetComponentInParent<ScaffoldZone>(true) != null)
                            continue;
                        Undo.RecordObject(zone.gameObject, "MCP Show Scaffold");
                        zone.gameObject.SetActive(true);
                        reactivated++;
                    }
                }

                SceneView.RepaintAll();

                return McpResponse.Ok(new
                {
                    active          = active,
                    zones_affected  = count,
                    zones_reactivated = reactivated,
                    message         = active
                        ? $"Scaffold gizmos visible ({count} zones)." +
                          (reactivated > 0 ? $" Reactivated {reactivated} zone(s) an earlier hide had deactivated." : string.Empty)
                        : $"Scaffold gizmos hidden ({count} zones). Baked content stays visible. " +
                          "Call show_scaffold to show them again.",
                });
            });

        // -----------------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------------

        private static LevelScaffold.ZoneDimensions BuildDimensions(
            ScaffoldDimensionsRequest dto,
            ScaffoldZoneType          zoneType)
        {
            // Start with sensible defaults for this zone type, then overlay any
            // values the caller explicitly provided (non-zero values win).
            var dims = ZoneDimensions.DefaultsFor(zoneType);

            if (dto == null) return dims;

            if (dto.Width       > 0f) dims.Width       = dto.Width;
            if (dto.Depth       > 0f) dims.Depth       = dto.Depth;
            if (dto.Height      > 0f) dims.Height      = dto.Height;
            if (dto.Diameter    > 0f) dims.Diameter    = dto.Diameter;
            if (dto.StepCount   > 0)  dims.StepCount   = dto.StepCount;
            if (dto.RiserHeight > 0f) dims.RiserHeight = dto.RiserHeight;
            if (dto.Turns       > 0f) dims.Turns       = dto.Turns;
            if (dto.Radius      > 0f) dims.Radius      = dto.Radius;

            return dims;
        }

        private static ScaffoldZone[] AppendZone(ScaffoldZone[] existing, ScaffoldZone zone)
        {
            if (existing == null) return new[] { zone };
            var list = new List<ScaffoldZone>(existing);
            if (!list.Contains(zone)) list.Add(zone);
            return list.ToArray();
        }

        private static ScaffoldAnchor FindAnchorByLabel(ScaffoldZone zone, string label)
        {
            foreach (var anchor in zone.GetComponentsInChildren<ScaffoldAnchor>(true))
                if (anchor.AnchorLabel == label) return anchor;
            return null;
        }

        private static T[] FindAll<T>() where T : Component
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<T>(includeInactive: true);
#endif
        }

        private static Vector3 ToV3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        private static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
