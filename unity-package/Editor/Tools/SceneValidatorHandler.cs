using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/scene/validate
    ///
    /// Runs a configurable set of diagnostic checks across the active scene (or a
    /// scoped subtree) and returns a structured report of issues. Each issue includes
    /// the hierarchy path of the offending object and a human-readable detail string
    /// that the agent can present and act on.
    ///
    /// Checks are independent of one another — an unknown check ID is silently skipped
    /// rather than failing the whole request. Issues per check are capped at
    /// MaxResultsPerCheck to keep responses manageable on large scenes.
    /// </summary>
    public static class SceneValidatorHandler
    {
        // -----------------------------------------------------------------------
        // Check registry
        // -----------------------------------------------------------------------

        // Ordered list used when req.Checks is null (run everything)
        private static readonly string[] AllCheckIds =
        {
            "missing_scripts",
            "missing_materials",
            "untagged_triggers",
            "duplicate_names",
            "meshrenderer_no_collider",
            "large_light_range",
            "floating_objects",
            "missing_lod_groups",
            "overlapping_statics",
        };

        // Metadata: severity and description per check ID
        private static readonly Dictionary<string, (string Severity, string Description)> CheckMeta =
            new Dictionary<string, (string, string)>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "missing_scripts",          ("error",   "Objects with missing MonoBehaviour script references") },
                { "missing_materials",        ("error",   "MeshRenderers with null or unassigned material slots") },
                { "untagged_triggers",        ("warning", "Trigger colliders with tag 'Untagged'") },
                { "duplicate_names",          ("warning", "Siblings with identical names (breaks FindByPath)") },
                { "meshrenderer_no_collider",  ("info",   "MeshRenderers without a Collider on the same object") },
                { "large_light_range",        ("info",   "Point or Spot lights with range exceeding the threshold") },
                { "floating_objects",         ("info",   "Renderable objects with no surface detected below them") },
                { "missing_lod_groups",       ("warning","High-vertex MeshRenderers with no LODGroup on self or parent") },
                { "overlapping_statics",      ("info",   "Static objects whose colliders physically intersect other static geometry") },            };

        // -----------------------------------------------------------------------
        // POST /tools/scene/validate
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ValidateSceneAsync(ValidateSceneRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var checksToRun = (req?.Checks == null || req.Checks.Length == 0)
                    ? AllCheckIds
                    : req.Checks;

                int maxResults      = System.Math.Max(1, req?.MaxResultsPerCheck ?? 50);
                float rangeThresh   = req?.LightRangeThreshold  > 0 ? req.LightRangeThreshold  : 100f;
                float floatDistance = req?.FloatingRayDistance  > 0 ? req.FloatingRayDistance   : 0.5f;
                int   lodVertThreshold = req?.LodVertexThreshold  > 0 ? req.LodVertexThreshold   : 2000;
                
                var allObjects = CollectObjects(req?.ScopePath);
                var results    = new List<ValidationCheckResultData>(checksToRun.Length);
                int total      = 0;

                foreach (var checkId in checksToRun)
                {
                    if (!CheckMeta.TryGetValue(checkId, out var meta))
                        continue; // unknown ID — skip silently

                    var issues = RunCheck(checkId, allObjects, rangeThresh, floatDistance, lodVertThreshold, maxResults);
                    results.Add(new ValidationCheckResultData
                    {
                        CheckId     = checkId,
                        Severity    = meta.Severity,
                        Description = meta.Description,
                        Count       = issues.Count,
                        Issues      = issues.ToArray()
                    });

                    total += issues.Count;
                }

                return McpResponse.Ok(new ValidateSceneResultData
                {
                    TotalIssues = total,
                    ChecksRun   = checksToRun,
                    Results     = results.ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // Check dispatcher
        // -----------------------------------------------------------------------

        private static List<ValidationIssueData> RunCheck(
            string           checkId,
            List<GameObject> objects,
            float            lightRangeThreshold,
            float            floatingRayDistance,
            int              lodVertexThreshold,
            int              maxResults)        {
            switch (checkId.ToLowerInvariant())
            {
                case "missing_scripts":          return CheckMissingScripts(objects, maxResults);
                case "missing_materials":        return CheckMissingMaterials(objects, maxResults);
                case "untagged_triggers":        return CheckUntaggedTriggers(objects, maxResults);
                case "duplicate_names":          return CheckDuplicateNames(objects, maxResults);
                case "meshrenderer_no_collider":  return CheckMeshRendererNoCollider(objects, maxResults);
                case "large_light_range":        return CheckLargeLightRange(objects, lightRangeThreshold, maxResults);
                case "floating_objects":         return CheckFloatingObjects(objects, floatingRayDistance, maxResults);
                case "missing_lod_groups":       return CheckMissingLodGroups(objects, lodVertexThreshold, maxResults);
                case "overlapping_statics":      return CheckOverlappingStatics(objects, maxResults);
                default:                         return new List<ValidationIssueData>();
            }
        }

        // -----------------------------------------------------------------------
        // Object collection
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns all GameObjects in scope: either the entire active scene, or all
        /// descendants of the object at scopePath (including inactive objects).
        /// </summary>
        private static List<GameObject> CollectObjects(string scopePath)
        {
            var result = new List<GameObject>();

            if (!string.IsNullOrEmpty(scopePath))
            {
                var root = PlacementHandler.FindByPath(scopePath);
                if (root == null) return result;
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                    result.Add(t.gameObject);
            }
            else
            {
                foreach (var rootGo in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in rootGo.GetComponentsInChildren<Transform>(includeInactive: true))
                    result.Add(t.gameObject);
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Individual checks
        // -----------------------------------------------------------------------

        /// <summary>
        /// Finds GameObjects with one or more MonoBehaviour components whose script
        /// asset is missing (common after a script rename or package removal).
        /// </summary>
        private static List<ValidationIssueData> CheckMissingScripts(
            List<GameObject> objects, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count > 0)
                    issues.Add(new ValidationIssueData
                    {
                        Path   = PlacementHandler.GetPath(go),
                        Detail = $"{count} missing script component{(count == 1 ? "" : "s")}"
                    });
            }
            return issues;
        }

        /// <summary>
        /// Finds MeshRenderers that have null entries in their sharedMaterials array
        /// (common after a material asset is deleted or a package is removed).
        /// </summary>
        private static List<ValidationIssueData> CheckMissingMaterials(
            List<GameObject> objects, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                int nullCount = mr.sharedMaterials.Count(m => m == null);
                if (nullCount > 0)
                    issues.Add(new ValidationIssueData
                    {
                        Path   = PlacementHandler.GetPath(go),
                        Detail = $"{nullCount} null material slot{(nullCount == 1 ? "" : "s")} on MeshRenderer"
                    });
            }
            return issues;
        }

        /// <summary>
        /// Finds trigger colliders (isTrigger=true) whose GameObject has tag "Untagged".
        /// These are almost always a forgotten configuration step.
        /// </summary>
        private static List<ValidationIssueData> CheckUntaggedTriggers(
            List<GameObject> objects, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;
                if (!go.CompareTag("Untagged")) continue;

                var colliders = go.GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (!col.isTrigger) continue;
                    issues.Add(new ValidationIssueData
                    {
                        Path   = PlacementHandler.GetPath(go),
                        Detail = $"{col.GetType().Name} isTrigger=true, tag=Untagged"
                    });
                    break; // one issue per object even if multiple trigger colliders exist
                }
            }
            return issues;
        }

        /// <summary>
        /// Finds siblings (children of the same parent) that share an identical name.
        /// Duplicate names break FindByPath and are confusing in the hierarchy.
        /// </summary>
        private static List<ValidationIssueData> CheckDuplicateNames(
            List<GameObject> objects, int maxResults)
        {
            var issues = new List<ValidationIssueData>();

            // Group by parent transform (null = root level). For each parent, collect
            // paths per name. Flag any name that appears more than once.
            var byParent = new Dictionary<Transform, Dictionary<string, List<string>>>();

            foreach (var go in objects)
            {
                var parent = go.transform.parent; // null is a valid Dictionary key
                if (!byParent.ContainsKey(parent))
                    byParent[parent] = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

                if (!byParent[parent].ContainsKey(go.name))
                    byParent[parent][go.name] = new List<string>();

                byParent[parent][go.name].Add(PlacementHandler.GetPath(go));
            }

            foreach (var parentEntry in byParent)
            foreach (var nameEntry in parentEntry.Value)
            {
                if (nameEntry.Value.Count <= 1) continue;
                foreach (var path in nameEntry.Value)
                {
                    if (issues.Count >= maxResults) goto Done;
                    issues.Add(new ValidationIssueData
                    {
                        Path   = path,
                        Detail = $"Name '{nameEntry.Key}' shared with {nameEntry.Value.Count - 1} sibling(s)"
                    });
                }
            }
            Done:
            return issues;
        }

        /// <summary>
        /// Finds GameObjects that have a MeshRenderer but no Collider on the same object.
        /// Useful for props that should be physically interactive. Generates false positives
        /// for purely decorative objects — keep this check opt-in.
        /// </summary>
        private static List<ValidationIssueData> CheckMeshRendererNoCollider(
            List<GameObject> objects, int maxResults)
        {
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            var issues = new List<ValidationIssueData>();

            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;
                if (go.GetComponent<MeshRenderer>() == null) continue;
                if (go.GetComponent<Collider>()     != null) continue;
                if (go.layer == ignoreRaycastLayer)          continue;

                issues.Add(new ValidationIssueData
                {
                    Path   = PlacementHandler.GetPath(go),
                    Detail = "MeshRenderer present but no Collider on this object"
                });
            }
            return issues;
        }

        /// <summary>
        /// Finds Point and Spot lights whose range exceeds lightRangeThreshold.
        /// Catches values accidentally set very high during tweaking sessions.
        /// </summary>
        private static List<ValidationIssueData> CheckLargeLightRange(
            List<GameObject> objects, float threshold, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;
                var light = go.GetComponent<Light>();
                if (light == null) continue;
                if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                if (light.range > threshold)
                    issues.Add(new ValidationIssueData
                    {
                        Path   = PlacementHandler.GetPath(go),
                        Detail = $"{light.type} light range {light.range:F1}m exceeds threshold {threshold:F1}m"
                    });
            }
            return issues;
        }

        /// <summary>
        /// Finds renderable objects with no surface detected directly below them within
        /// floatingRayDistance. Catches props left floating after terrain was sculpted
        /// beneath them. Skips triggers, non-kinematic Rigidbodies, and disabled renderers.
        /// </summary>
        private static List<ValidationIssueData> CheckFloatingObjects(
            List<GameObject> objects, float rayDistance, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;

                // Only check objects with an active Renderer
                var renderer = go.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;

                // Triggers are intentionally non-physical — skip
                var col = go.GetComponent<Collider>();
                if (col != null && col.isTrigger) continue;

                // Non-kinematic Rigidbodies may be in the air legitimately (e.g. thrown objects)
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic) continue;

                // Cast downward from just above the pivot
                var origin = go.transform.position + Vector3.up * 0.1f;
                if (!Physics.Raycast(origin, Vector3.down, rayDistance + 0.1f))
                    issues.Add(new ValidationIssueData
                    {
                        Path   = PlacementHandler.GetPath(go),
                        Detail = $"No surface within {rayDistance:F2}m below object pivot"
                    });
            }
            return issues;
        }
        
        /// <summary>
        /// Finds MeshRenderers whose mesh has more vertices than lodVertexThreshold
        /// but no LODGroup anywhere in the parent hierarchy.
        /// </summary>
        private static List<ValidationIssueData> CheckMissingLodGroups(
            List<GameObject> objects, int lodVertexThreshold, int maxResults)
        {
            var issues = new List<ValidationIssueData>();
            foreach (var go in objects)
            {
                if (issues.Count >= maxResults) break;

                var mf = go.GetComponent<MeshFilter>();
                if (mf?.sharedMesh == null) continue;
                if (mf.sharedMesh.vertexCount < lodVertexThreshold) continue;
                if (go.GetComponentInParent<LODGroup>() != null) continue;

                issues.Add(new ValidationIssueData
                {
                    Path   = PlacementHandler.GetPath(go),
                    Detail = $"{mf.sharedMesh.vertexCount} verts — no LODGroup on self or in parent chain"
                });
            }
            return issues;
        }

        /// <summary>
        /// Finds pairs of static GameObjects whose non-trigger colliders overlap.
        /// Only reports each overlapping pair once. Parent/child pairs are excluded.
        /// Run this scoped to a specific area — it is expensive on large scenes.
        /// </summary>
        private static List<ValidationIssueData> CheckOverlappingStatics(
            List<GameObject> objects, int maxResults)
        {
            var issues     = new List<ValidationIssueData>();
            var reportedPairs = new HashSet<string>();

            // Only check static objects with non-trigger colliders
            var staticObjects = objects
                .Where(go =>
                {
                    if (!go.isStatic) return false;
                    var col = go.GetComponent<Collider>();
                    return col != null && !col.isTrigger;
                })
                .ToList();

            foreach (var go in staticObjects)
            {
                if (issues.Count >= maxResults) break;

                var col    = go.GetComponent<Collider>();
                var bounds = col.bounds;

                // Shrink slightly to avoid false positives from shared faces
                var hits = Physics.OverlapBox(
                    bounds.center,
                    bounds.extents * 0.95f,
                    go.transform.rotation,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                foreach (var hit in hits)
                {
                    if (hit.gameObject == go)       continue;
                    if (!hit.gameObject.isStatic)   continue;
                    if (hit.isTrigger)              continue;

                    // Skip parent/child pairs
                    if (go.transform.IsChildOf(hit.transform) ||
                        hit.transform.IsChildOf(go.transform)) continue;

                    // Avoid reporting the same pair twice (A→B and B→A)
                    var pathA   = PlacementHandler.GetPath(go);
                    var pathB   = PlacementHandler.GetPath(hit.gameObject);
                    var pairKey = string.CompareOrdinal(pathA, pathB) < 0
                        ? $"{pathA}|{pathB}"
                        : $"{pathB}|{pathA}";

                    if (!reportedPairs.Add(pairKey)) continue;

                    issues.Add(new ValidationIssueData
                    {
                        Path   = pathA,
                        Detail = $"Overlaps static geometry '{pathB}'"
                    });
                    break; // one issue per object
                }
            }
            return issues;
        }
    }
}
