using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LevelScaffold;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles the two-phase bake workflow that translates a completed scaffold
    /// into actual prefab instances.
    ///
    /// Phase 1 — Resolve:  match each marker's SemanticTags against every
    ///           PrefabMetadata in the project, score by tag overlap + scene
    ///           context bonus, return ranked candidates for agent review.
    ///
    /// Phase 2 — Execute:  instantiate the confirmed prefab at each marker's
    ///           world position, parent it to the zone container, set
    ///           BakeState = Baked on the marker, and register everything
    ///           with Unity's Undo system.
    ///
    /// Routes to add in McpRouter:
    ///   POST /tools/scaffold/resolve_markers  → ResolveMarkersAsync(req)
    ///   POST /tools/scaffold/execute_bake     → ExecuteBakeAsync(req)
    /// </summary>
    public static class BakeHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/scaffold/resolve_markers
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ResolveMarkersAsync(ResolveMarkersRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                // Ensure the metadata registry is populated
                MetadataRegistry.Reload();

                var allMetadata = MetadataRegistry.All;
                if (allMetadata.Count == 0)
                    return McpResponse.Ok(new ResolveMarkersResultData
                    {
                        Proposals    = Array.Empty<BakeProposal>(),
                        Unresolvable = Array.Empty<UnresolvableMarker>(),
                    });

                // SceneContext for context bonus (null-safe — just means no bonus)
                var context     = McpBridgeSettings.GetOrCreate().sceneContext;
                var contextTags = context?.activeTags ?? Array.Empty<string>();

                int maxCandidates = req?.MaxCandidatesPerMarker > 0
                    ? req.MaxCandidatesPerMarker : 3;

                // Collect matching markers
                var markers = FilterMarkers(
                    FindAll<GameplayMarker>(),
                    req?.ZonePath,
                    req?.MarkerTypes);

                var proposals    = new List<BakeProposal>(markers.Length);
                var unresolvable = new List<UnresolvableMarker>();

                foreach (var marker in markers)
                {
                    var markerTags = marker.SemanticTags ?? Array.Empty<string>();
                    var candidates = new List<BakeCandidate>(allMetadata.Count);

                    foreach (var meta in allMetadata)
                    {
                        if (meta.prefab == null) continue;

                        int overlap = TagOverlap(meta.tags, markerTags);
                        if (overlap == 0) continue;

                        int bonus      = TagOverlap(meta.tags, contextTags);
                        int totalScore = overlap + bonus;

                        var prefabPath = AssetDatabase.GetAssetPath(meta.prefab);
                        candidates.Add(new BakeCandidate
                        {
                            AssetGuid         = AssetDatabase.AssetPathToGUID(prefabPath),
                            AssetPath         = prefabPath,
                            DisplayName       = !string.IsNullOrEmpty(meta.displayName)
                                                    ? meta.displayName
                                                    : meta.prefab.name,
                            TagOverlapCount   = overlap,
                            SceneContextBonus = bonus,
                            TotalScore        = totalScore,
                        });
                    }

                    var markerPath = PlacementHandler.GetPath(marker.gameObject);

                    if (candidates.Count == 0)
                    {
                        unresolvable.Add(new UnresolvableMarker
                        {
                            MarkerPath   = markerPath,
                            MarkerLabel  = marker.MarkerLabel,
                            SemanticTags = markerTags,
                        });
                    }
                    else
                    {
                        // Sort descending by total score, then by asset path for stable ordering
                        candidates.Sort((a, b) =>
                            b.TotalScore != a.TotalScore
                                ? b.TotalScore.CompareTo(a.TotalScore)
                                : string.Compare(a.AssetPath, b.AssetPath, StringComparison.Ordinal));

                        proposals.Add(new BakeProposal
                        {
                            MarkerPath   = markerPath,
                            MarkerLabel  = marker.MarkerLabel,
                            MarkerType   = marker.MarkerType.ToString(),
                            SemanticTags = markerTags,
                            Candidates   = candidates.Take(maxCandidates).ToArray(),
                        });
                    }
                }

                return McpResponse.Ok(new ResolveMarkersResultData
                {
                    Proposals    = proposals.ToArray(),
                    Unresolvable = unresolvable.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/scaffold/execute_bake
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ExecuteBakeAsync(ExecuteBakeRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Confirmations == null || req.Confirmations.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "confirmations is required. " +
                        "Call resolve_markers first, review the candidates, then pass " +
                        "confirmed { marker_path, asset_guid } pairs to execute_bake.");

                var instances     = new List<BakedInstance>(req.Confirmations.Length);
                var warnings      = new List<string>();
                var affectedZones = new HashSet<ScaffoldZone>();

                Undo.SetCurrentGroupName("MCP Execute Bake");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var conf in req.Confirmations)
                {
                    if (string.IsNullOrEmpty(conf?.MarkerPath))
                    { warnings.Add("Skipping entry with empty marker_path."); continue; }
                    if (string.IsNullOrEmpty(conf.AssetGuid))
                    { warnings.Add($"Skipping '{conf.MarkerPath}' — asset_guid is empty."); continue; }

                    // ── Resolve marker ──────────────────────────────────── //
                    var markerGo = PlacementHandler.FindByPath(conf.MarkerPath);
                    if (markerGo == null)
                    {
                        warnings.Add($"Marker not found: '{conf.MarkerPath}'. " +
                                     "Call get_scaffold_markers to list valid paths.");
                        continue;
                    }

                    var marker = markerGo.GetComponent<GameplayMarker>();
                    if (marker == null)
                    {
                        warnings.Add($"'{conf.MarkerPath}' has no GameplayMarker component.");
                        continue;
                    }

                    // ── Resolve prefab ──────────────────────────────────── //
                    var prefabPath = AssetDatabase.GUIDToAssetPath(conf.AssetGuid);
                    if (string.IsNullOrEmpty(prefabPath))
                    {
                        warnings.Add($"No asset for GUID '{conf.AssetGuid}' " +
                                     $"(marker: {marker.MarkerLabel}). " +
                                     "Verify the GUID came from resolve_markers candidates.");
                        continue;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        warnings.Add($"Could not load GameObject at '{prefabPath}'.");
                        continue;
                    }

                    // ── Instantiate ─────────────────────────────────────── //
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Undo.RegisterCreatedObjectUndo(instance, "MCP Bake Marker");

                    instance.transform.SetPositionAndRotation(
                        marker.transform.position,
                        marker.transform.rotation);
                    instance.name = $"{marker.MarkerLabel}_Instance";

                    // Parent to the zone container (sibling of the marker)
                    var zone = marker.GetComponentInParent<ScaffoldZone>();
                    if (zone != null)
                    {
                        instance.transform.SetParent(zone.transform, worldPositionStays: true);
                        affectedZones.Add(zone);
                    }

                    // ── Update marker state ──────────────────────────────── //
                    Undo.RecordObject(marker, "MCP Bake Marker");
                    marker.BakeState         = BakeState.Baked;
                    marker.ResolvedPrefabGuid = conf.AssetGuid;
                    EditorUtility.SetDirty(markerGo);

                    instances.Add(new BakedInstance
                    {
                        MarkerLabel  = marker.MarkerLabel,
                        InstancePath = PlacementHandler.GetPath(instance),
                    });
                }

                // ── Optionally hide the scaffold gizmos ──────────────────── //
                // Not by deactivating zones: the baked instances are children of their
                // zone and would disappear with it. The gizmo switches are global.
                if (req.HideScaffoldAfter && affectedZones.Count > 0)
                    global::LevelScaffold.Editor.ScaffoldOverlay.SetAllVisible(false);

                if (instances.Count > 0 || warnings.Count > 0)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new ExecuteBakeResultData
                {
                    BakedCount = instances.Count,
                    Instances  = instances.ToArray(),
                    Warnings   = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Filters GameplayMarker instances to those that are Pending and match
        /// the optional zone and marker-type filters.
        /// </summary>
        private static GameplayMarker[] FilterMarkers(
            GameplayMarker[] all,
            string           zonePath,
            string[]         markerTypes)
        {
            return all.Where(m =>
            {
                // Only Pending markers are eligible for resolution
                if (m.BakeState != BakeState.Pending) return false;

                // Zone filter
                if (!string.IsNullOrEmpty(zonePath))
                {
                    var zone = m.GetComponentInParent<ScaffoldZone>();
                    if (zone == null) return false;
                    if (PlacementHandler.GetPath(zone.gameObject) != zonePath) return false;
                }

                // Marker type filter
                if (markerTypes != null && markerTypes.Length > 0)
                    if (Array.IndexOf(markerTypes, m.MarkerType.ToString()) < 0) return false;

                return true;
            }).ToArray();
        }

        /// <summary>
        /// Counts how many strings in setA appear in setB.
        /// Case-sensitive (tags should be consistent).
        /// Returns 0 if either array is null or empty.
        /// </summary>
        private static int TagOverlap(string[] setA, string[] setB)
        {
            if (setA == null || setB == null ||
                setA.Length == 0 || setB.Length == 0) return 0;

            int count = 0;
            foreach (var tag in setA)
                if (Array.IndexOf(setB, tag) >= 0) count++;
            return count;
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
    }
}
