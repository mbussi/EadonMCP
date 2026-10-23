using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;
using System.Threading.Tasks;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles object placement, transform mutation, and deletion.
    ///
    /// Each public method accepts a pre-parsed request DTO — body parsing is
    /// done by McpRouter before dispatch. This keeps handlers free of HTTP
    /// concerns and makes them directly testable without mocking HttpListenerRequest.
    ///
    /// All mutating operations are registered with Unity's Undo system.
    /// </summary>
    public static class PlacementHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/placement/place_prefab
        // -----------------------------------------------------------------------

        public static Task<McpResponse> PlacePrefabAsync(PlacePrefabRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.PrefabPath);
                if (prefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND", $"No prefab at '{req.PrefabPath}'.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND", $"No object at path '{req.ParentPath}'.");
                }

                var position = ToVector3(req.Position);
                var rotation = Quaternion.Euler(ToVector3(req.Rotation));
                var scale    = req.Scale != null ? ToVector3(req.Scale) : Vector3.one;

                if (req.DryRun)
                    return McpResponse.Ok(new PlacedObjectData
                    {
                        InstancePath = BuildExpectedPath(parent, prefab.name),
                        DisplayName  = prefab.name,
                        Position     = Vec3(position),
                        Rotation     = Vec3(rotation.eulerAngles),
                        Scale        = Vec3(scale),
                        DryRun       = true
                    });

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position   = position;
                instance.transform.rotation   = rotation;
                instance.transform.localScale = scale;

                if (parent != null)
                    instance.transform.SetParent(parent.transform, worldPositionStays: true);

                Undo.RegisterCreatedObjectUndo(instance, $"MCP Place {prefab.name}");

                return McpResponse.Ok(new PlacedObjectData
                {
                    InstancePath = GetPath(instance),
                    DisplayName  = instance.name,
                    Position     = Vec3(instance.transform.position),
                    Rotation     = Vec3(instance.transform.eulerAngles),
                    Scale        = Vec3(instance.transform.localScale),
                    DryRun       = false
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/set_transform
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SetTransformAsync(SetTransformRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ObjectPath))
                    return McpResponse.Fail("INVALID_PARAMS", "object_path must not be empty.");

                var go = FindByPath(req.ObjectPath);
                if (go == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND", $"No object at path '{req.ObjectPath}'.");

                if (req.DryRun)
                    return McpResponse.Ok(new TransformResultData
                    {
                        ObjectPath  = req.ObjectPath,
                        DisplayName = go.name,
                        Position    = req.Position ?? Vec3(go.transform.position),
                        Rotation    = req.Rotation ?? Vec3(go.transform.eulerAngles),
                        Scale       = req.Scale    ?? Vec3(go.transform.localScale),
                        DryRun      = true
                    });

                Undo.RecordObject(go.transform, $"MCP Set Transform: {go.name}");

                if (req.Position != null) go.transform.position    = ToVector3(req.Position);
                if (req.Rotation != null) go.transform.eulerAngles = ToVector3(req.Rotation);
                if (req.Scale    != null) go.transform.localScale  = ToVector3(req.Scale);

                return McpResponse.Ok(new TransformResultData
                {
                    ObjectPath  = req.ObjectPath,
                    DisplayName = go.name,
                    Position    = Vec3(go.transform.position),
                    Rotation    = Vec3(go.transform.eulerAngles),
                    Scale       = Vec3(go.transform.localScale),
                    DryRun      = false
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/delete_object
        // -----------------------------------------------------------------------

        public static Task<McpResponse> DeleteObjectAsync(DeleteObjectRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ObjectPath))
                    return McpResponse.Fail("INVALID_PARAMS", "object_path must not be empty.");

                var go = FindByPath(req.ObjectPath);
                if (go == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND", $"No object at path '{req.ObjectPath}'.");

                string name = go.name;

                if (req.DryRun)
                    return McpResponse.Ok(new DeleteResultData
                        { ObjectPath = req.ObjectPath, DisplayName = name, DryRun = true });

                Undo.DestroyObjectImmediate(go);

                return McpResponse.Ok(new DeleteResultData
                    { ObjectPath = req.ObjectPath, DisplayName = name, DryRun = false });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/create_empty
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateEmptyAsync(CreateEmptyRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Name))
                    return McpResponse.Fail("INVALID_PARAMS", "name must not be empty.");

                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND", $"No object at path '{req.ParentPath}'.");
                }

                var position = ToVector3(req.Position);

                if (req.DryRun)
                    return McpResponse.Ok(new PlacedObjectData
                    {
                        InstancePath = BuildExpectedPath(parent, req.Name),
                        DisplayName  = req.Name,
                        Position     = Vec3(position),
                        Rotation     = new[] { 0f, 0f, 0f },
                        Scale        = new[] { 1f, 1f, 1f },
                        DryRun       = true
                    });

                var go = new GameObject(req.Name);
                go.transform.position = position;

                if (parent != null)
                    go.transform.SetParent(parent.transform, worldPositionStays: true);

                Undo.RegisterCreatedObjectUndo(go, $"MCP Create {req.Name}");

                return McpResponse.Ok(new PlacedObjectData
                {
                    InstancePath = GetPath(go),
                    DisplayName  = go.name,
                    Position     = Vec3(go.transform.position),
                    Rotation     = Vec3(go.transform.eulerAngles),
                    Scale        = Vec3(go.transform.localScale),
                    DryRun       = false
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/placement/replace_prefab
        // -----------------------------------------------------------------------

        /// <summary>
        /// Finds every instance of old_prefab_path in the scene and replaces each
        /// with an instance of new_prefab_path, preserving transform, name, and parent
        /// by default. All operations are grouped under a single named Undo entry.
        ///
        /// Only direct prefab instance roots are matched — variant instances and child
        /// objects within prefabs are not replaced individually.
        /// </summary>
        public static Task<McpResponse> ReplacePrefabAsync(ReplacePrefabRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.OldPrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "old_prefab_path must not be empty.");
                if (string.IsNullOrWhiteSpace(req?.NewPrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "new_prefab_path must not be empty.");

                var oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.OldPrefabPath);
                if (oldPrefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND",
                        $"No prefab found at '{req.OldPrefabPath}'.");

                var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.NewPrefabPath);
                if (newPrefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND",
                        $"No prefab found at '{req.NewPrefabPath}'.");

                // Collect all instances of old_prefab in scope.
                // Only match prefab instance roots (not child objects inside a prefab).
                // TODO: GetObjectsInScope duplicates CollectObjects in SceneValidatorHandler —
                //       extract to a shared helper when convenient.
                var scopeObjects = GetObjectsInScope(req.ScopePath);
                var instances    = scopeObjects
                    .Where(go =>
                        PrefabUtility.IsPartOfPrefabInstance(go) &&
                        PrefabUtility.GetNearestPrefabInstanceRoot(go) == go &&
                        PrefabUtility.GetCorrespondingObjectFromSource(go) == oldPrefab)
                    .ToList();

                if (instances.Count == 0)
                {
                    string scope = string.IsNullOrEmpty(req.ScopePath) ? "the whole scene" : $"'{req.ScopePath}'";
                    return McpResponse.Ok(new ReplacePrefabResultData
                    {
                        ReplacedCount = 0,
                        DryRun        = req.DryRun,
                        Replaced      = System.Array.Empty<ReplaceResultItem>(),
                        Warnings      = new[] { $"No instances of '{req.OldPrefabPath}' found in {scope}." }
                    });
                }

                // Dry run — report what would be replaced without touching the scene
                if (req.DryRun)
                {
                    return McpResponse.Ok(new ReplacePrefabResultData
                    {
                        ReplacedCount = instances.Count,
                        DryRun        = true,
                        Replaced      = instances.Select(go => new ReplaceResultItem
                        {
                            OldPath = GetPath(go),
                            NewPath = GetPath(go)   // path will be the same (name preserved)
                        }).ToArray(),
                        Warnings = System.Array.Empty<string>()
                    });
                }

                // Execute replacements under a single named Undo group
                var oldName = System.IO.Path.GetFileNameWithoutExtension(req.OldPrefabPath);
                var newName = System.IO.Path.GetFileNameWithoutExtension(req.NewPrefabPath);
                Undo.SetCurrentGroupName($"MCP Replace '{oldName}' → '{newName}'");
                int undoGroup = Undo.GetCurrentGroup();

                var replaced = new System.Collections.Generic.List<ReplaceResultItem>(instances.Count);
                var warnings = new System.Collections.Generic.List<string>();

                foreach (var instance in instances)
                {
                    // Record state before deletion
                    var pos    = instance.transform.position;
                    var rot    = instance.transform.rotation;
                    var scale  = instance.transform.localScale;
                    var parent = instance.transform.parent;
                    var name   = instance.name;
                    var oldPath= GetPath(instance);

                    Undo.DestroyObjectImmediate(instance);

                    var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
                    Undo.RegisterCreatedObjectUndo(newInstance, "MCP Replace Prefab");

                    if (req.PreserveParent != false && parent != null)
                        newInstance.transform.SetParent(parent, worldPositionStays: false);

                    if (req.PreserveTransform != false)
                    {
                        newInstance.transform.position   = pos;
                        newInstance.transform.rotation   = rot;
                        newInstance.transform.localScale = scale;
                    }

                    if (req.PreserveName != false)
                        newInstance.name = name;

                    replaced.Add(new ReplaceResultItem
                    {
                        OldPath = oldPath,
                        NewPath = GetPath(newInstance)
                    });
                }

                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new ReplacePrefabResultData
                {
                    ReplacedCount = replaced.Count,
                    DryRun        = false,
                    Replaced      = replaced.ToArray(),
                    Warnings      = warnings.ToArray()
                });
            });

        /// <summary>
        /// Returns all GameObjects in the active scene, restricted to the subtree
        /// under scopePath when provided. Includes inactive objects.
        /// </summary>
        private static System.Collections.Generic.List<GameObject> GetObjectsInScope(string scopePath)
        {
            var result = new System.Collections.Generic.List<GameObject>();
            if (!string.IsNullOrEmpty(scopePath))
            {
                var root = FindByPath(scopePath);
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
        // Shared utilities
        // -----------------------------------------------------------------------

        /// <summary>
        /// Finds a scene object by its hierarchy path (e.g. "Environment/Buildings/House").
        /// Returns null if any segment of the path is not found.
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var parts   = path.TrimStart('/').Split('/');
            var roots   = SceneManager.GetActiveScene().GetRootGameObjects();
            var current = System.Array.Find(roots, go => go.name == parts[0])?.transform;

            for (int i = 1; i < parts.Length && current != null; i++)
            {
                Transform next = null;
                for (int c = 0; c < current.childCount; c++)
                {
                    if (current.GetChild(c).name == parts[i])
                    { next = current.GetChild(c); break; }
                }
                current = next;
            }

            return current?.gameObject;
        }

        /// <summary>Returns the full scene hierarchy path of a GameObject.</summary>
        public static string GetPath(GameObject go)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var t = go.transform; t != null; t = t.parent)
                parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static string BuildExpectedPath(GameObject parent, string childName) =>
            parent == null ? childName : GetPath(parent) + "/" + childName;

        private static Vector3 ToVector3(float[] a) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        private static float[] Vec3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
