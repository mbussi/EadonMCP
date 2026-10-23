using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles multi-scene editor operations for streaming open-world setups.
    ///
    ///   GET  /scene/scenes        — list all currently loaded scenes
    ///   POST /tools/scene/manage  — load, unload, move objects, or create scenes
    ///
    /// All operations run on the main thread via MainThreadDispatcher and register
    /// mutations with Unity's Undo system where applicable.
    /// </summary>
    public static class SceneManagementHandler
    {
        // -----------------------------------------------------------------------
        // GET /scene/scenes
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetLoadedScenesAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(ListScenes);

        // -----------------------------------------------------------------------
        // POST /tools/scene/manage
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ManageSceneAsync(ManageSceneRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                switch (req?.Operation?.ToLowerInvariant())
                {
                    case "list":         return ListScenes();
                    case "load":         return LoadScene(req);
                    case "unload":       return UnloadScene(req);
                    case "move_objects": return MoveObjects(req);
                    case "create":       return CreateScene(req);
                    default:
                        return McpResponse.Fail("INVALID_PARAMS",
                            $"Unknown operation '{req?.Operation}'. " +
                            "Valid values: list, load, unload, move_objects, create.");
                }
            });

        // -----------------------------------------------------------------------
        // list
        // -----------------------------------------------------------------------

        private static McpResponse ListScenes()
        {
            var activeScene = SceneManager.GetActiveScene();
            var scenes      = new List<SceneInfoData>(EditorSceneManager.sceneCount);

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                scenes.Add(new SceneInfoData
                {
                    Name     = s.name,
                    Path     = s.path,
                    IsActive = s == activeScene,
                    IsDirty  = s.isDirty
                });
            }

            return McpResponse.Ok(new SceneListData { Scenes = scenes.ToArray() });
        }

        // -----------------------------------------------------------------------
        // load
        // -----------------------------------------------------------------------

        private static McpResponse LoadScene(ManageSceneRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ScenePath))
                return McpResponse.Fail("INVALID_PARAMS", "scene_path is required for the load operation.");

            var scene = EditorSceneManager.OpenScene(req.ScenePath, OpenSceneMode.Additive);

            if (!scene.IsValid())
                return McpResponse.Fail("SCENE_NOT_FOUND",
                    $"Could not load scene at '{req.ScenePath}'. " +
                    "Verify the asset path (e.g. 'Assets/Scenes/Village_NE.unity').");

            if (req.SetActive == true)
                SceneManager.SetActiveScene(scene);

            return McpResponse.Ok(new SceneLoadData
            {
                SceneName = scene.name,
                ScenePath = scene.path,
                Loaded    = true,
                IsActive  = SceneManager.GetActiveScene() == scene
            });
        }

        // -----------------------------------------------------------------------
        // unload
        // -----------------------------------------------------------------------

        private static McpResponse UnloadScene(ManageSceneRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ScenePath))
                return McpResponse.Fail("INVALID_PARAMS", "scene_path is required for the unload operation.");

            if (EditorSceneManager.sceneCount == 1)
                return McpResponse.Fail("CANNOT_UNLOAD_LAST",
                    "Cannot unload the only loaded scene. Load another scene first.");

            // Find scene by exact path or by name
            Scene target = default;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.path == req.ScenePath || s.name == req.ScenePath)
                {
                    target = s;
                    break;
                }
            }

            if (!target.IsValid())
                return McpResponse.Fail("SCENE_NOT_LOADED",
                    $"No loaded scene found matching '{req.ScenePath}'. " +
                    "Use get_loaded_scenes to see what is currently open.");

            bool closed = EditorSceneManager.CloseScene(target, removeScene: true);
            if (!closed)
                return McpResponse.Fail("UNLOAD_FAILED",
                    $"EditorSceneManager.CloseScene returned false for '{req.ScenePath}'. " +
                    "The scene may have unsaved changes; save or discard them first.");

            return McpResponse.Ok(new { unloaded = true, scene_path = req.ScenePath });
        }

        // -----------------------------------------------------------------------
        // move_objects
        // -----------------------------------------------------------------------

        private static McpResponse MoveObjects(ManageSceneRequest req)
        {
            if (req.ObjectPaths == null || req.ObjectPaths.Length == 0)
                return McpResponse.Fail("INVALID_PARAMS", "object_paths is required for move_objects.");
            if (string.IsNullOrWhiteSpace(req.TargetSceneName))
                return McpResponse.Fail("INVALID_PARAMS", "target_scene_name is required for move_objects.");

            // Find target scene (accept both name and path)
            Scene targetScene = default;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.name == req.TargetSceneName || s.path == req.TargetSceneName)
                {
                    targetScene = s;
                    break;
                }
            }

            if (!targetScene.IsValid())
                return McpResponse.Fail("SCENE_NOT_LOADED",
                    $"Target scene '{req.TargetSceneName}' is not currently loaded. " +
                    "Use manage_scene with operation=load first.");

            var moved   = new List<MovedObjectData>(req.ObjectPaths.Length);
            var warnings = new List<string>();
            var pathSet  = new HashSet<string>(req.ObjectPaths);

            foreach (var path in req.ObjectPaths)
            {
                var go = PlacementHandler.FindByPath(path);
                if (go == null)
                {
                    warnings.Add($"Object not found: '{path}' — skipped.");
                    continue;
                }

                // Objects with parents in other scenes must be detached first,
                // unless the parent is also in the move list.
                if (go.transform.parent != null)
                {
                    var parentPath = PlacementHandler.GetPath(go.transform.parent.gameObject);
                    if (!pathSet.Contains(parentPath))
                    {
                        Undo.SetTransformParent(go.transform, null, "MCP Detach for scene move");
                        warnings.Add(
                            $"'{path}' was detached from parent '{parentPath}' before moving — " +
                            "parent was not included in object_paths.");
                    }
                }

                var fromScene = go.scene.name;
                Undo.RecordObject(go, "MCP Move to Scene");
                SceneManager.MoveGameObjectToScene(go, targetScene);

                moved.Add(new MovedObjectData
                {
                    Path      = path,
                    FromScene = fromScene,
                    ToScene   = targetScene.name
                });
            }

            return McpResponse.Ok(new SceneMoveObjectsData
            {
                MovedCount = moved.Count,
                Moved      = moved.ToArray(),
                Warnings   = warnings.ToArray()
            });
        }

        // -----------------------------------------------------------------------
        // create
        // -----------------------------------------------------------------------

        private static McpResponse CreateScene(ManageSceneRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ScenePath))
                return McpResponse.Fail("INVALID_PARAMS", "scene_path is required for create.");

            // Ensure the target directory exists in the AssetDatabase
            var dir = Path.GetDirectoryName(req.ScenePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                CreateFolderRecursive(dir);

            // NewSceneMode.Single replaces all loaded scenes; Additive adds alongside them.
            var sceneMode = (req.SetActive == true) ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene     = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);

            // Save unless explicitly told not to
            bool shouldSave = req.SaveAfterCreate != false;
            if (shouldSave)
            {
                bool saved = EditorSceneManager.SaveScene(scene, req.ScenePath);
                if (!saved)
                    return McpResponse.Fail("SAVE_FAILED",
                        $"Scene was created in memory but could not be saved to '{req.ScenePath}'. " +
                        "Check that the path is under Assets/ and the directory exists.");
            }

            return McpResponse.Ok(new SceneLoadData
            {
                SceneName = scene.name,
                ScenePath = scene.path,
                Loaded    = true,
                IsActive  = SceneManager.GetActiveScene() == scene
            });
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static void CreateFolderRecursive(string path)
        {
            var parts   = path.Replace('\\', '/').Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
