using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityMcpBridge.Editor.Components;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles Dressing Phase 2 precision placement tools.
    ///
    /// Routes to add in McpRouter:
    ///   POST /tools/placement/snap_to_surface       → SnapToSurfaceAsync(req)
    ///   POST /tools/placement/align_and_distribute  → AlignAndDistributeAsync(req)
    ///   POST /tools/placement/randomize_transforms  → RandomizeTransformsAsync(req)
    ///   POST /tools/placement/group_objects         → GroupObjectsAsync(req)
    /// </summary>
    public static class DressingHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/placement/snap_to_surface
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SnapToSurfaceAsync(SnapToSurfaceRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                var dir = ToV3(req.CastDirection, Vector3.down).normalized;
                if (dir == Vector3.zero) dir = Vector3.down;

                float maxDist  = req.MaxDistance > 0 ? req.MaxDistance : 100f;
                int   mask     = req.LayerMask ?? Physics.DefaultRaycastLayers;

                var skipped = new List<SnapSkippedItem>();
                int snapped = 0;

                Undo.SetCurrentGroupName("MCP Snap to Surface");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) { skipped.Add(Skip(path, "Object not found.")); continue; }

                    if (!SnapObject(go, dir, maxDist, req.SurfaceOffset,
                                    req.AlignToNormal, mask, out var reason))
                    {
                        skipped.Add(Skip(path, reason));
                        continue;
                    }

                    EditorUtility.SetDirty(go);
                    snapped++;
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new SnapToSurfaceResultData
                {
                    SnappedCount = snapped,
                    Skipped      = skipped.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/align_and_distribute
        // -----------------------------------------------------------------------

        public static Task<McpResponse> AlignAndDistributeAsync(AlignAndDistributeRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");
                if (string.IsNullOrWhiteSpace(req.Mode))
                    return McpResponse.Fail("INVALID_PARAMS",
                        "mode is required: distribute_between | align_to_axis | stack | circle");

                // Resolve all objects up front — fail fast on any missing path
                var objects = new List<GameObject>(req.ObjectPaths.Length);
                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null)
                        return McpResponse.Fail("OBJECT_NOT_FOUND",
                            $"No object at '{path}'. Verify paths with find_objects first.");
                    objects.Add(go);
                }

                var warnings = new List<string>();
                int repositioned = 0;

                Undo.SetCurrentGroupName($"MCP Align ({req.Mode})");
                int undoGroup = Undo.GetCurrentGroup();

                switch (req.Mode)
                {
                    case "distribute_between":
                        repositioned = DistributeBetween(objects, req, warnings);
                        break;
                    case "align_to_axis":
                        repositioned = AlignToAxis(objects, req, warnings);
                        break;
                    case "stack":
                        repositioned = Stack(objects, req, warnings);
                        break;
                    case "circle":
                        repositioned = DistributeOnCircle(objects, req, warnings);
                        break;
                    default:
                        return McpResponse.Fail("INVALID_PARAMS",
                            $"Unknown mode '{req.Mode}'. " +
                            "Valid: distribute_between | align_to_axis | stack | circle");
                }

                foreach (var go in objects) EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new AlignAndDistributeResultData
                {
                    RepositionedCount = repositioned,
                    Warnings          = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/randomize_transforms
        // -----------------------------------------------------------------------

        public static Task<McpResponse> RandomizeTransformsAsync(RandomizeTransformsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                bool hasAnyRange =
                    (req.PositionRange != null && req.PositionRange.Length >= 1) ||
                    (req.RotationRange != null && req.RotationRange.Length >= 1) ||
                    (req.ScaleRange    != null && req.ScaleRange.Length    >= 2);

                if (!hasAnyRange)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "At least one of position_range, rotation_range, or scale_range is required.");

                int actualSeed = req.Seed ?? new System.Random().Next();
                var rng        = new System.Random(actualSeed);
                var warnings   = new List<string>();
                int randomized = 0;

                Undo.SetCurrentGroupName("MCP Randomize Transforms");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) { warnings.Add($"Object not found: '{path}'."); continue; }

                    Undo.RecordObject(go.transform, "MCP Randomize");

                    // Position
                    if (req.PositionRange != null && req.PositionRange.Length >= 1)
                    {
                        float px = req.PositionRange.Length > 0 ? req.PositionRange[0] : 0f;
                        float py = req.PositionRange.Length > 1 ? req.PositionRange[1] : 0f;
                        float pz = req.PositionRange.Length > 2 ? req.PositionRange[2] : 0f;
                        go.transform.position += new Vector3(
                            RandSym(rng, px),
                            RandSym(rng, py),
                            RandSym(rng, pz));
                    }

                    // Rotation
                    if (req.RotationRange != null && req.RotationRange.Length >= 1)
                    {
                        float rx = req.RotationRange.Length > 0 ? req.RotationRange[0] : 0f;
                        float ry = req.RotationRange.Length > 1 ? req.RotationRange[1] : 0f;
                        float rz = req.RotationRange.Length > 2 ? req.RotationRange[2] : 0f;
                        go.transform.eulerAngles += new Vector3(
                            RandSym(rng, rx),
                            RandSym(rng, ry),
                            RandSym(rng, rz));
                    }

                    // Scale (uniform)
                    if (req.ScaleRange != null && req.ScaleRange.Length >= 2)
                    {
                        float s = Mathf.Lerp(req.ScaleRange[0], req.ScaleRange[1],
                                             (float)rng.NextDouble());
                        go.transform.localScale *= s;
                    }

                    EditorUtility.SetDirty(go);
                    randomized++;
                }

                // Optional post-snap
                if (req.SnapAfter)
                {
                    foreach (var path in req.ObjectPaths)
                    {
                        var go = PlacementHandler.FindByPath(path);
                        if (go == null) continue;
                        SnapObject(go, Vector3.down, 100f, 0f, false,
                            Physics.DefaultRaycastLayers, out _);
                        EditorUtility.SetDirty(go);
                    }
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new RandomizeTransformsResultData
                {
                    RandomizedCount = randomized,
                    SeedUsed        = actualSeed,
                    Warnings        = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/group_objects
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GroupObjectsAsync(GroupObjectsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");
                if (string.IsNullOrWhiteSpace(req.GroupName))
                    return McpResponse.Fail("INVALID_PARAMS", "group_name is required.");

                var warnings = new List<string>();

                // Resolve objects
                var objects = new List<GameObject>(req.ObjectPaths.Length);
                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) { warnings.Add($"Object not found: '{path}'."); continue; }
                    objects.Add(go);
                }
                if (objects.Count == 0)
                    return McpResponse.Fail("OBJECTS_NOT_FOUND", "None of the specified objects were found.");

                // Resolve optional explicit parent
                Transform explicitParent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    var p = PlacementHandler.FindByPath(req.ParentPath);
                    if (p == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at parent_path '{req.ParentPath}'.");
                    explicitParent = p.transform;
                }

                // Determine pivot position
                var pivotMode = (req.Pivot ?? "center").ToLowerInvariant();
                Vector3 pivotPos = pivotMode switch
                {
                    "first"        => objects[0].transform.position,
                    "world_origin" => Vector3.zero,
                    _              => ComputeCentroid(objects), // "center"
                };

                // Find parent: explicit or common ancestor of all grouped objects
                Transform containerParent = explicitParent;
                if (containerParent == null && objects.Count > 0)
                    containerParent = FindCommonAncestor(objects);

                // Create container
                Undo.SetCurrentGroupName($"MCP Group '{req.GroupName}'");
                int undoGroup = Undo.GetCurrentGroup();

                var container = new GameObject(req.GroupName);
                Undo.RegisterCreatedObjectUndo(container, "MCP Create Container");
                container.transform.SetPositionAndRotation(pivotPos, Quaternion.identity);

                if (containerParent != null)
                    container.transform.SetParent(containerParent, worldPositionStays: true);

                if (!string.IsNullOrEmpty(req.Tag))
                {
                    try   { container.tag = req.Tag; }
                    catch { warnings.Add($"Tag '{req.Tag}' not found — not applied to container."); }
                }
                if (!string.IsNullOrEmpty(req.Layer))
                {
                    int l = LayerMask.NameToLayer(req.Layer);
                    if (l < 0) warnings.Add($"Layer '{req.Layer}' not found — not applied to container.");
                    else container.layer = l;
                }

                // Reparent objects
                foreach (var go in objects)
                    Undo.SetTransformParent(go.transform, container.transform,
                        worldPositionStays: true, "MCP Group");

                EditorUtility.SetDirty(container);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new GroupObjectsResultData
                {
                    ContainerPath = PlacementHandler.GetPath(container),
                    GroupedCount  = objects.Count,
                    Warnings      = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        //  align_and_distribute — mode implementations
        // -----------------------------------------------------------------------

        private static int DistributeBetween(
            List<GameObject> objects,
            AlignAndDistributeRequest req,
            List<string> warnings)
        {
            if (req.StartPoint == null || req.StartPoint.Length < 3)
            { warnings.Add("start_point [x,y,z] is required for distribute_between mode."); return 0; }
            if (req.EndPoint == null || req.EndPoint.Length < 3)
            { warnings.Add("end_point [x,y,z] is required for distribute_between mode."); return 0; }

            var start = ToV3(req.StartPoint, Vector3.zero);
            var end   = ToV3(req.EndPoint,   Vector3.zero);
            var dir   = (end - start).normalized;
            int n     = objects.Count;

            for (int i = 0; i < n; i++)
            {
                float t   = n > 1 ? (float)i / (n - 1) : 0.5f;
                var   pos = Vector3.Lerp(start, end, t);

                Undo.RecordObject(objects[i].transform, "MCP Distribute");
                objects[i].transform.position = pos;

                if (req.FaceDirection && dir.sqrMagnitude > 0.001f)
                    objects[i].transform.rotation = Quaternion.LookRotation(dir);
            }
            return n;
        }

        private static int AlignToAxis(
            List<GameObject> objects,
            AlignAndDistributeRequest req,
            List<string> warnings)
        {
            if (string.IsNullOrEmpty(req.Axis))
            { warnings.Add("axis ('x','y','z') is required for align_to_axis mode."); return 0; }
            if (!req.Value.HasValue)
            { warnings.Add("value is required for align_to_axis mode."); return 0; }

            var axis    = req.Axis.ToLowerInvariant();
            var pivot   = (req.UsePivot ?? "center").ToLowerInvariant();
            float target = req.Value.Value;

            int count = 0;
            foreach (var go in objects)
            {
                var bounds = GetObjectBounds(go);

                float current = (axis, pivot) switch
                {
                    ("x", "min")    => bounds.min.x,
                    ("x", "max")    => bounds.max.x,
                    ("x", _)        => bounds.center.x,
                    ("y", "min")    => bounds.min.y,
                    ("y", "max")    => bounds.max.y,
                    ("y", _)        => bounds.center.y,
                    ("z", "min")    => bounds.min.z,
                    ("z", "max")    => bounds.max.z,
                    _               => bounds.center.z,
                };

                float delta = target - current;
                Undo.RecordObject(go.transform, "MCP Align to Axis");

                go.transform.position += axis switch
                {
                    "x" => new Vector3(delta, 0, 0),
                    "z" => new Vector3(0, 0, delta),
                    _   => new Vector3(0, delta, 0),
                };
                count++;
            }
            return count;
        }

        private static int Stack(
            List<GameObject> objects,
            AlignAndDistributeRequest req,
            List<string> warnings)
        {
            if (objects.Count == 0) return 0;

            var axis = (req.StackAxis ?? "y").ToLowerInvariant();

            // Starting position: base_position if provided, else first object's current pos
            float stackPos = req.BasePosition != null && req.BasePosition.Length >= 3
                ? axis switch { "x" => req.BasePosition[0], "z" => req.BasePosition[2], _ => req.BasePosition[1] }
                : axis switch { "x" => objects[0].transform.position.x, "z" => objects[0].transform.position.z, _ => objects[0].transform.position.y };

            foreach (var go in objects)
            {
                var bounds = GetObjectBounds(go);

                // Distance from pivot to the bottom of the object along the stack axis
                float pivotToMin = axis switch
                {
                    "x" => go.transform.position.x - bounds.min.x,
                    "z" => go.transform.position.z - bounds.min.z,
                    _   => go.transform.position.y - bounds.min.y,
                };
                float size = axis switch
                {
                    "x" => bounds.size.x,
                    "z" => bounds.size.z,
                    _   => bounds.size.y,
                };

                Undo.RecordObject(go.transform, "MCP Stack");
                var pos = go.transform.position;
                go.transform.position = axis switch
                {
                    "x" => new Vector3(stackPos + pivotToMin, pos.y, pos.z),
                    "z" => new Vector3(pos.x, pos.y, stackPos + pivotToMin),
                    _   => new Vector3(pos.x, stackPos + pivotToMin, pos.z),
                };

                stackPos += size;
            }
            return objects.Count;
        }

        private static int DistributeOnCircle(
            List<GameObject> objects,
            AlignAndDistributeRequest req,
            List<string> warnings)
        {
            if (req.Center == null || req.Center.Length < 3)
            { warnings.Add("center [x,y,z] is required for circle mode."); return 0; }
            if (req.Radius <= 0)
            { warnings.Add("radius > 0 is required for circle mode."); return 0; }

            var   center = ToV3(req.Center, Vector3.zero);
            float radius = req.Radius;
            int   n      = objects.Count;

            for (int i = 0; i < n; i++)
            {
                float angleDeg = (360f * i / n) + req.StartAngleDegrees;
                float rad      = angleDeg * Mathf.Deg2Rad;

                var offset = new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
                var pos    = center + offset;

                Undo.RecordObject(objects[i].transform, "MCP Circle");
                objects[i].transform.position = pos;

                if (req.FacingOutward && offset.sqrMagnitude > 0.001f)
                    objects[i].transform.rotation = Quaternion.LookRotation(offset.normalized);
            }
            return n;
        }

        // -----------------------------------------------------------------------
        // Shared snap helper (used by both SnapToSurfaceAsync and RandomizeTransformsAsync)
        // -----------------------------------------------------------------------

        private static bool SnapObject(
            GameObject go,
            Vector3    direction,
            float      maxDistance,
            float      surfaceOffset,
            bool       alignToNormal,
            int        layerMask,
            out string failReason)
        {
            failReason = null;

            // Offset ray origin slightly opposite to direction to avoid self-intersection
            var origin = go.transform.position + (-direction) * 0.05f;

            if (!Physics.Raycast(origin, direction, out var hit, maxDistance + 0.05f, layerMask))
            {
                failReason = $"No surface found within {maxDistance}m.";
                return false;
            }

            Undo.RecordObject(go.transform, "MCP Snap");
            go.transform.position = hit.point + hit.normal * surfaceOffset;

            if (alignToNormal)
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

            return true;
        }

        // -----------------------------------------------------------------------
        // Bounds helper
        // -----------------------------------------------------------------------

        private static Bounds GetObjectBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
            var colliders = go.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                var b = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);
                return b;
            }
            // Fallback: unit cube at object position
            return new Bounds(go.transform.position, Vector3.one);
        }

        // -----------------------------------------------------------------------
        // Hierarchy helpers
        // -----------------------------------------------------------------------

        private static Vector3 ComputeCentroid(List<GameObject> objects)
        {
            var sum = Vector3.zero;
            foreach (var go in objects) sum += go.transform.position;
            return sum / objects.Count;
        }

        private static Transform FindCommonAncestor(List<GameObject> objects)
        {
            if (objects.Count == 0) return null;

            // Build ancestor chain for the first object
            var chain = new List<Transform>();
            var t = objects[0].transform.parent;
            while (t != null) { chain.Add(t); t = t.parent; }

            // Walk through remaining objects; find the first ancestor in the chain
            for (int i = 1; i < objects.Count; i++)
            {
                var candidate = objects[i].transform.parent;
                while (candidate != null)
                {
                    if (chain.Contains(candidate)) return candidate;
                    candidate = candidate.parent;
                }
            }
            return null; // scene root
        }

        // -----------------------------------------------------------------------
        // POST /tools/placement/create_trigger_volume
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateTriggerVolumeAsync(CreateTriggerVolumeRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Name))
                    return McpResponse.Fail("INVALID_PARAMS", "name is required.");
                if (req.Position == null || req.Position.Length < 3)
                    return McpResponse.Fail("INVALID_PARAMS", "position [x,y,z] is required.");

                var shape = (req.Shape ?? "box").ToLowerInvariant();
                if (shape != "box" && shape != "sphere" && shape != "capsule")
                    return McpResponse.Fail("INVALID_PARAMS",
                        $"Unknown shape '{req.Shape}'. Valid: box | sphere | capsule");

                var warnings = new List<string>();

                Undo.SetCurrentGroupName($"MCP Create Trigger '{req.Name}'");
                int undoGroup = Undo.GetCurrentGroup();

                var go = new GameObject(req.Name);
                Undo.RegisterCreatedObjectUndo(go, "MCP Create Trigger");

                // ── Transform ───────────────────────────────────────────── //
                go.transform.position = new Vector3(
                    req.Position[0], req.Position[1], req.Position[2]);
                if (req.Rotation != null && req.Rotation.Length >= 3)
                    go.transform.eulerAngles = new Vector3(
                        req.Rotation[0], req.Rotation[1], req.Rotation[2]);

                // ── Parent ───────────────────────────────────────────────── //
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    var parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        warnings.Add(
                            $"parent_path '{req.ParentPath}' not found — " +
                            "trigger placed at scene root.");
                    else
                        go.transform.SetParent(parent.transform, worldPositionStays: true);
                }

                // ── Collider ─────────────────────────────────────────────── //
                float[] effectiveSize = null;
                switch (shape)
                {
                    case "box":
                    {
                        var col = go.AddComponent<BoxCollider>();
                        col.isTrigger = true;
                        if (req.Size != null && req.Size.Length >= 3)
                        {
                            col.size = new Vector3(req.Size[0], req.Size[1], req.Size[2]);
                            effectiveSize = new[] { req.Size[0], req.Size[1], req.Size[2] };
                        }
                        else
                        {
                            // Default 1×1×1
                            effectiveSize = new[] { col.size.x, col.size.y, col.size.z };
                        }
                        break;
                    }
                    case "sphere":
                    {
                        var col = go.AddComponent<SphereCollider>();
                        col.isTrigger = true;
                        float r = req.Radius
                            ?? (req.Size != null && req.Size.Length > 0 ? req.Size[0] : 0.5f);
                        col.radius = r;
                        effectiveSize = new[] { r };
                        break;
                    }
                    case "capsule":
                    {
                        var col = go.AddComponent<CapsuleCollider>();
                        col.isTrigger = true;
                        float r = req.Radius
                            ?? (req.Size != null && req.Size.Length > 0 ? req.Size[0] : 0.5f);
                        float h = req.Height
                            ?? (req.Size != null && req.Size.Length > 1 ? req.Size[1] : 2f);
                        col.radius = r;
                        col.height = h;
                        effectiveSize = new[] { r, h };
                        break;
                    }
                }

                // ── Tag ──────────────────────────────────────────────────── //
                if (!string.IsNullOrEmpty(req.Tag))
                {
                    try   { go.tag = req.Tag; }
                    catch { warnings.Add(
                        $"Tag '{req.Tag}' not found — not applied. " +
                        "Create it in Project Settings → Tags first."); }
                }

                // ── Layer ────────────────────────────────────────────────── //
                if (!string.IsNullOrEmpty(req.Layer))
                {
                    int l = LayerMask.NameToLayer(req.Layer);
                    if (l < 0)
                        warnings.Add($"Layer '{req.Layer}' not found — not applied.");
                    else
                        go.layer = l;
                }

                // ── Script component ─────────────────────────────────────── //
                string scriptAdded = null;
                if (!string.IsNullOrEmpty(req.ScriptComponent))
                {
                    var type = ComponentCatalogBuilder.ResolveComponentType(req.ScriptComponent);
                    if (type == null)
                    {
                        warnings.Add(
                            $"Script component '{req.ScriptComponent}' not found in the project. " +
                            "Trigger created without it. Use add_component_to_object afterward.");
                    }
                    else
                    {
                        var comp = Undo.AddComponent(go, type);
                        if (comp != null && req.ScriptProperties != null)
                        {
                            var so = new SerializedObject(comp);
                            foreach (var pv in req.ScriptProperties)
                            {
                                if (string.IsNullOrEmpty(pv?.propertyPath)) continue;
                                var sp = so.FindProperty(pv.propertyPath);
                                if (sp == null)
                                {
                                    warnings.Add(
                                        $"Property '{pv.propertyPath}' not found on " +
                                        $"{req.ScriptComponent} — skipped.");
                                    continue;
                                }
                                if (!TriggerSetProperty(sp, pv.value, out var err))
                                    warnings.Add(
                                        $"Could not set '{pv.propertyPath}': {err}");
                            }
                            so.ApplyModifiedProperties();
                        }
                        scriptAdded = req.ScriptComponent;
                    }
                }

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new CreateTriggerVolumeResultData
                {
                    ObjectPath    = PlacementHandler.GetPath(go),
                    Shape         = shape,
                    EffectiveSize = effectiveSize,
                    Tag           = req.Tag,
                    ScriptAdded   = scriptAdded,
                    Warnings      = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/apply_material
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ApplyMaterialAsync(ApplyMaterialRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.ObjectPaths == null || req.ObjectPaths.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "object_paths must not be empty.");

                bool byName = !string.IsNullOrEmpty(req.MaterialName);
                bool byPath = !string.IsNullOrEmpty(req.MaterialPath);
                bool byTag  = !string.IsNullOrEmpty(req.MaterialTag);

                if (!byName && !byPath && !byTag)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "One of material_name, material_path, or material_tag is required.");

                // ── Build candidate pool ─────────────────────────────────── //
                var candidates = new List<Material>();
                var warnings   = new List<string>();

                if (byPath)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(req.MaterialPath);
                    if (mat == null)
                        return McpResponse.Fail("MATERIAL_NOT_FOUND",
                            $"No material at path '{req.MaterialPath}'. " +
                            "Check the path with the Project window.");
                    candidates.Add(mat);
                }
                else if (byName)
                {
                    // FindAssets with a name filter may return partial matches;
                    // confirm exact name after loading.
                    var guids = AssetDatabase.FindAssets($"t:Material {req.MaterialName}");
                    foreach (var guid in guids)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(
                            AssetDatabase.GUIDToAssetPath(guid));
                        if (mat != null && mat.name == req.MaterialName)
                            candidates.Add(mat);
                    }
                    if (candidates.Count == 0)
                        return McpResponse.Fail("MATERIAL_NOT_FOUND",
                            $"No material named exactly '{req.MaterialName}'. " +
                            "Use get_assets or check the Project window for the correct name.");
                }
                else // byTag — name-contains search
                {
                    var guids = AssetDatabase.FindAssets("t:Material");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var mat  = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat != null &&
                            mat.name.IndexOf(req.MaterialTag,
                                System.StringComparison.OrdinalIgnoreCase) >= 0)
                            candidates.Add(mat);
                    }
                    if (candidates.Count == 0)
                        return McpResponse.Fail("MATERIAL_NOT_FOUND",
                            $"No materials found whose name contains '{req.MaterialTag}'. " +
                            "material_tag is a name-contains search — ensure your material " +
                            "names include this string (e.g. 'Wood_Dark', 'Stone_Worn').");
                }

                int  slot        = Mathf.Max(0, req.MaterialSlot);
                var  rng         = new System.Random();
                var  assignments = new List<MaterialAssignment>();
                int  applied     = 0;

                Undo.SetCurrentGroupName("MCP Apply Material");
                int undoGroup = Undo.GetCurrentGroup();

                foreach (var path in req.ObjectPaths)
                {
                    var go = PlacementHandler.FindByPath(path);
                    if (go == null) { warnings.Add($"Object not found: '{path}'."); continue; }

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer == null)
                    {
                        warnings.Add(
                            $"'{path}' has no MeshRenderer component — skipped. " +
                            "Skinned mesh objects use SkinnedMeshRenderer instead.");
                        continue;
                    }

                    // Pick material: random from pool or first candidate
                    var mat = (req.RandomFromTag && candidates.Count > 1)
                        ? candidates[rng.Next(candidates.Count)]
                        : candidates[0];

                    var mats = renderer.sharedMaterials;
                    int effectiveSlot = slot;
                    if (effectiveSlot >= mats.Length)
                    {
                        warnings.Add(
                            $"'{path}': material_slot {slot} out of range " +
                            $"(object has {mats.Length} material slot(s)). Using slot 0.");
                        effectiveSlot = 0;
                    }

                    Undo.RecordObject(renderer, "MCP Apply Material");
                    mats[effectiveSlot] = mat;
                    renderer.sharedMaterials = mats;

                    EditorUtility.SetDirty(renderer);
                    assignments.Add(new MaterialAssignment { Path = path, Material = mat.name });
                    applied++;
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new ApplyMaterialResultData
                {
                    AppliedCount = applied,
                    Assignments  = assignments.ToArray(),
                    Warnings     = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // TriggerSetProperty — simplified property writer for script components
        // added to trigger volumes. Handles the common scalar and enum types.
        // For Vector3, Color, or ObjectReference properties use set_object_properties
        // on the trigger volume after creation.
        //
        // TODO: once ObjectHandler.SetProperty and DressingHandler.TriggerSetProperty
        // are both stable, extract both to a shared PropertyHelper static class.
        // -----------------------------------------------------------------------

        private static bool TriggerSetProperty(
            SerializedProperty sp, JToken value, out string error)
        {
            error = null;
            if (value == null || value.Type == JTokenType.Null)
            {
                error = "Null values are not supported by the simplified setter. " +
                        "Use set_object_properties for ObjectReference nulling.";
                return false;
            }
            try
            {
                switch (sp.propertyType)
                {
                    case SerializedPropertyType.Float:
                        sp.floatValue = value.Value<float>(); return true;

                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                        sp.intValue = value.Value<int>(); return true;

                    case SerializedPropertyType.Boolean:
                        sp.boolValue = value.Value<bool>(); return true;

                    case SerializedPropertyType.String:
                        sp.stringValue = value.Value<string>() ?? string.Empty; return true;

                    case SerializedPropertyType.Enum:
                    {
                        if (value.Type == JTokenType.Integer)
                        { sp.enumValueIndex = value.Value<int>(); return true; }
                        var name = value.Value<string>();
                        var idx  = System.Array.IndexOf(
                            sp.enumNames ?? System.Array.Empty<string>(), name);
                        if (idx < 0)
                        {
                            error = $"'{name}' is not valid. " +
                                    $"Options: {string.Join(", ", sp.enumNames ?? System.Array.Empty<string>())}";
                            return false;
                        }
                        sp.enumValueIndex = idx;
                        return true;
                    }

                    default:
                        error = $"Property type '{sp.propertyType}' is not supported by " +
                                "the simplified setter. Use set_object_properties instead.";
                        return false;
                }
            }
            catch (System.Exception ex)
            {
                error = $"Value conversion failed: {ex.Message}";
                return false;
            }
        }

                // -----------------------------------------------------------------------
        // POST /tools/placement/dress_area
        // -----------------------------------------------------------------------

        public static Task<McpResponse> DressAreaAsync(DressAreaRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Center == null || req.Center.Length < 3)
                    return McpResponse.Fail("INVALID_PARAMS", "center [x,y,z] is required.");
                if (req.Radius <= 0)
                    return McpResponse.Fail("INVALID_PARAMS", "radius must be > 0.");
                if (string.IsNullOrWhiteSpace(req.Intent))
                    return McpResponse.Fail("INVALID_PARAMS", "intent is required.");

                var center   = new Vector3(req.Center[0], req.Center[1], req.Center[2]);
                var warnings = new List<string>();
                var rng      = new System.Random();

                // ── 1. Score every prefab against intent ─────────────────── //
                var allMeta = MetadataRegistry.All;
                if (allMeta.Count == 0)
                    return McpResponse.Fail("NO_METADATA",
                        "MetadataRegistry is empty — no PrefabMetadata assets found. " +
                        "Create PrefabMetadata ScriptableObjects for the prefabs you want " +
                        "to use in dress_area.");

                SceneContext context = null;
                var ctxGuids = AssetDatabase.FindAssets("t:SceneContext");
                if (ctxGuids.Length > 0)
                    context = AssetDatabase.LoadAssetAtPath<SceneContext>(
                        AssetDatabase.GUIDToAssetPath(ctxGuids[0]));
                var contextTags = context?.activeTags ?? System.Array.Empty<string>();
                
                var intentLower  = (req.Intent ?? string.Empty).ToLowerInvariant();
                var intentTokens = new HashSet<string>(
                    intentLower.Split(
                        new[] { ' ', ',', '.', '-', '\'', '/', '\\' },
                        System.StringSplitOptions.RemoveEmptyEntries));

                var excludeSet = req.ExcludeTags != null
                    ? new HashSet<string>(req.ExcludeTags, System.StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();
                var includeSet = req.IncludeTags != null && req.IncludeTags.Length > 0
                    ? new HashSet<string>(req.IncludeTags, System.StringComparer.OrdinalIgnoreCase)
                    : (HashSet<string>)null;

                // (meta, relevanceScore, assetPath)
                var scored = new List<(PrefabMetadata meta, float score, string path)>();

                foreach (var meta in allMeta)
                {
                    if (meta?.prefab == null) continue;
                    var assetPath = AssetDatabase.GetAssetPath(meta.prefab);
                    if (string.IsNullOrEmpty(assetPath)) continue;

                    var tags = meta.tags ?? System.Array.Empty<string>();

                    // Exclude / include filters
                    if (excludeSet.Count > 0 && tags.Any(t => excludeSet.Contains(t))) continue;
                    if (includeSet != null && !tags.Any(t => includeSet.Contains(t))) continue;

                    // Base score: tag tokens that appear in the intent
                    int tagOverlap = tags.Count(t =>
                        intentTokens.Contains(t.ToLowerInvariant()));
                    // Category match
                    int catMatch = (!string.IsNullOrEmpty(meta.category) &&
                        intentTokens.Contains(meta.category.ToLowerInvariant())) ? 1 : 0;
                    // Display name token match (half weight — loose signal)
                    var nameTokens = (meta.displayName ?? string.Empty)
                        .ToLowerInvariant()
                        .Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                    int nameMatch = nameTokens.Count(t => intentTokens.Contains(t));
                    // SceneContext bonus
                    int ctxBonus = tags.Count(t => contextTags.Any(ct =>
                        string.Equals(ct, t, System.StringComparison.OrdinalIgnoreCase)));

                    float score = tagOverlap + catMatch + nameMatch * 0.5f + ctxBonus;
                    if (score > 0)
                        scored.Add((meta, score, assetPath));
                }

                if (scored.Count == 0)
                    return McpResponse.Fail("NO_MATCHING_PREFABS",
                        $"No prefabs match '{req.Intent}'. " +
                        "Ensure PrefabMetadata tags overlap with words in the intent, " +
                        "or use include_tags to specify prefabs explicitly.");

                // ── 2. Density → min-spacing and count cap ───────────────── //
                float minSpacing;
                int   maxCount;
                switch ((req.Density ?? "medium").ToLowerInvariant())
                {
                    case "sparse": minSpacing = 2.0f; maxCount = 15; break;
                    case "dense":  minSpacing = 0.7f; maxCount = 40; break;
                    default:       minSpacing = 1.2f; maxCount = 25; break;
                }

                // Honour the top prefab's placement.minimumSpacing if larger
                var topMeta = scored.OrderByDescending(s => s.score).First().meta;
                if (topMeta.placement != null &&
                    topMeta.placement.minimumSpacing > minSpacing)
                    minSpacing = topMeta.placement.minimumSpacing;

                // ── 3. Poisson disk sampling ─────────────────────────────── //
                var candidates2D = PoissonDisk(
                    new Vector2(center.x, center.z),
                    req.Radius, minSpacing, rng, maxCount);

                // ── 4. Spatial avoidance ─────────────────────────────────── //
                float avoidR    = req.AvoidExisting ? Mathf.Max(0.1f, req.AvoidanceRadius) : 0f;
                var surviving   = new List<Vector3>();
                int discarded   = 0;

                foreach (var p2 in candidates2D)
                {
                    var probe = new Vector3(p2.x, center.y, p2.y);
                    if (req.AvoidExisting && avoidR > 0 &&
                        Physics.OverlapSphere(probe, avoidR).Length > 0)
                    {
                        discarded++;
                        continue;
                    }
                    surviving.Add(probe);
                }

                if (surviving.Count == 0)
                    return McpResponse.Fail("NO_VALID_POSITIONS",
                        $"All {candidates2D.Count} candidate positions were discarded due to " +
                        $"avoidance_radius={avoidR}m. Reduce avoidance_radius, lower density, " +
                        "or set avoid_existing=false.");

                // ── 5. Surface raycast ───────────────────────────────────── //
                if (req.SnapToSurfaces)
                {
                    for (int i = 0; i < surviving.Count; i++)
                    {
                        var origin = surviving[i] + Vector3.up * 50f;
                        if (Physics.Raycast(origin, Vector3.down, out var hit, 100f))
                            surviving[i] = hit.point;
                    }
                }

                // ── 6. Weighted-random prefab selection ──────────────────── //
                float totalW     = scored.Sum(s => s.score);
                var   cumW       = new float[scored.Count];
                float running    = 0f;
                for (int i = 0; i < scored.Count; i++)
                {
                    running += scored[i].score;
                    cumW[i]  = running;
                }

                PrefabMetadata PickMeta()
                {
                    float r = (float)(rng.NextDouble() * totalW);
                    for (int i = 0; i < cumW.Length; i++)
                        if (r <= cumW[i]) return scored[i].meta;
                    return scored[scored.Count - 1].meta;
                }

                string PathFor(PrefabMetadata m) =>
                    scored.First(s => s.meta == m).path;

                // ── 7. Build proposal operations ─────────────────────────── //
                var groupName = req.GroupName ??
                    $"DressArea_{center.x:F0}_{center.z:F0}";

                var ops         = new List<ProposalOperationData>(surviving.Count);
                var nameCounts  = new Dictionary<string, int>();

                for (int i = 0; i < surviving.Count; i++)
                {
                    var  meta     = PickMeta();
                    var  path     = PathFor(meta);
                    var  pos      = surviving[i];
                    var  display  = meta.displayName ??
                                    System.IO.Path.GetFileNameWithoutExtension(path);
                    float rotY   = (float)(rng.NextDouble() * 360.0);

                    nameCounts.TryGetValue(display, out int n);
                    nameCounts[display] = n + 1;

                    ops.Add(new ProposalOperationData
                    {
                        Id          = i + 1,
                        Tool        = "place_prefab",
                        Description = $"Place {display} at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})",
                        Params      = JObject.FromObject(new PlacePrefabRequest
                        {
                            PrefabPath = path,
                            Position   = new[] { pos.x, pos.y, pos.z },
                            Rotation   = new[] { 0f, rotY, 0f },
                            ParentPath = req.ParentPath,
                        }),
                    });
                }

                // ── 8. Summary ───────────────────────────────────────────── //
                var parts   = nameCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Value}× {kv.Key}");
                var summary =
                    $"{ops.Count} props proposed for \"{req.Intent}\" " +
                    $"at ({center.x:F0}, {center.y:F0}, {center.z:F0}): " +
                    string.Join(", ", parts);

                if (discarded > 0)
                    warnings.Add(
                        $"{discarded} position(s) discarded — too close to existing objects.");

                // ── 9. Submit to proposal queue ──────────────────────────── //
                var submitted = ProposalHandler.SubmitInternal(
                    new ProposeChangesRequest
                    {
                        Summary    = summary,
                        Operations = ops.ToArray(),
                    });

                return McpResponse.Ok(new DressAreaResultData
                {
                    ProposalId     = submitted.ProposalId,
                    Summary        = summary,
                    ProposedCount  = ops.Count,
                    DiscardedCount = discarded,
                    GroupName      = groupName,
                    Warnings       = warnings.ToArray(),
                });
            });

        // -----------------------------------------------------------------------
        // Poisson disk sampling — generates well-distributed points within a
        // circle with guaranteed minimum spacing between any two points.
        // -----------------------------------------------------------------------

        private static List<Vector2> PoissonDisk(
            Vector2       center,
            float         radius,
            float         minSpacing,
            System.Random rng,
            int           maxCount,
            int           attempts = 30)
        {
            var results = new List<Vector2>(maxCount);
            var active  = new List<Vector2>(maxCount);

            var first = center + RandomInCircle(rng, radius);
            results.Add(first);
            active.Add(first);

            while (active.Count > 0 && results.Count < maxCount)
            {
                int  idx   = rng.Next(active.Count);
                var  curr  = active[idx];
                bool found = false;

                for (int a = 0; a < attempts; a++)
                {
                    float dist  = minSpacing * (1f + (float)rng.NextDouble());
                    float angle = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
                    var   cand  = curr + new Vector2(
                                      Mathf.Cos(angle) * dist,
                                      Mathf.Sin(angle) * dist);

                    if ((cand - center).magnitude > radius) continue;

                    bool valid = true;
                    foreach (var p in results)
                    {
                        if ((p - cand).magnitude < minSpacing) { valid = false; break; }
                    }

                    if (valid)
                    {
                        results.Add(cand);
                        active.Add(cand);
                        found = true;
                        break;
                    }
                }

                if (!found) active.RemoveAt(idx);
            }

            return results;
        }

        private static Vector2 RandomInCircle(System.Random rng, float radius)
        {
            float angle = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
            // sqrt(uniform) for uniform area distribution
            float r = (float)System.Math.Sqrt(rng.NextDouble()) * radius;
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        private static float RandSym(System.Random rng, float range) =>
            (float)(rng.NextDouble() * 2.0 - 1.0) * range;

        private static Vector3 ToV3(float[] a, Vector3 fallback) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

        private static SnapSkippedItem Skip(string path, string reason) =>
            new SnapSkippedItem { Path = path, Reason = reason };
    }
}
