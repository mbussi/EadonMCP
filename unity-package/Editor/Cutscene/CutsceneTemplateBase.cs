using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
#if CM3
using Unity.Cinemachine;
#endif

namespace UnityMcpBridge.Editor.Cutscene
{
    /// <summary>
    /// Static helpers shared by all cutscene template classes.
    ///
    /// Responsibilities:
    ///   - Ensure the /Cutscenes scene container and Assets/Cutscenes/ folder exist
    ///   - Create and cache SignalAsset ScriptableObjects
    ///   - Detect CinemachineBrain presence
    ///   - Report installed package versions
    ///   - Wrap per-sequence work in a single Undo group
    /// </summary>
    internal static class CutsceneTemplateBase
    {
        // ------------------------------------------------------------------ //
        //  Scene hierarchy                                                    //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns (or creates) the /Cutscenes root GameObject at scene root.
        /// All sequence containers live under this object.
        /// </summary>
        public static GameObject EnsureCutscenesRoot()
        {
            var root = GameObject.Find("Cutscenes");
            if (root != null) return root;

            root = new GameObject("Cutscenes");
            Undo.RegisterCreatedObjectUndo(root, "MCP Create Cutscenes");
            return root;
        }

        /// <summary>
        /// Returns (or creates) /Cutscenes/{sequenceName} as a child of the
        /// Cutscenes root. This is the container for all scene objects that
        /// belong to one sequence.
        /// </summary>
        public static GameObject EnsureSequenceContainer(string sequenceName)
        {
            var cutscenesRoot = EnsureCutscenesRoot();

            // Check for existing container with this name
            var existing = cutscenesRoot.transform.Find(sequenceName);
            if (existing != null) return existing.gameObject;

            var container = new GameObject(sequenceName);
            Undo.RegisterCreatedObjectUndo(container, $"MCP Create {sequenceName}");
            container.transform.SetParent(cutscenesRoot.transform, worldPositionStays: false);
            return container;
        }

        /// <summary>
        /// The container's CutsceneSequenceSettings (added if missing), recorded for Undo
        /// and stamped with the template. Templates fill in the settings they were built
        /// from, so update_cutscene_sequence can recompute changes the same way.
        /// </summary>
        public static global::UnityMcpBridge.CutsceneSequenceSettings RecordSettings(GameObject container, string template)
        {
            var settings = container.GetComponent<global::UnityMcpBridge.CutsceneSequenceSettings>();
            if (settings == null)
                settings = Undo.AddComponent<global::UnityMcpBridge.CutsceneSequenceSettings>(container);
            else
                Undo.RecordObject(settings, "MCP Cutscene Settings");
            settings.template = template;
            return settings;
        }

        // ------------------------------------------------------------------ //
        //  Asset creation                                                     //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Creates a new TimelineAsset at the given project path.
        /// The parent folder is created if it does not exist.
        /// </summary>
        public static TimelineAsset CreateTimelineAsset(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                EnsureFolder(dir);

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, assetPath);
            return timeline;
        }

        /// <summary>
        /// Returns the SignalAsset for the given name, creating it in
        /// Assets/Cutscenes/Signals/ if it does not already exist.
        /// Signal assets are reused across all sequences.
        /// </summary>
        public static SignalAsset GetOrCreateSignalAsset(string signalName)
        {
            const string folder    = "Assets/Cutscenes/Signals";
            var          assetPath = $"{folder}/{signalName}.signal";

            var existing = AssetDatabase.LoadAssetAtPath<SignalAsset>(assetPath);
            if (existing != null) return existing;

            EnsureFolder(folder);
            var asset = ScriptableObject.CreateInstance<SignalAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        /// <summary>
        /// Resolves the Timeline asset path from the request or builds a default.
        /// Default: Assets/Cutscenes/{sequenceName}.playable
        /// </summary>
        public static string ResolveTimelinePath(string requestedPath, string sequenceName)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath)) return requestedPath;
            return $"Assets/Cutscenes/{sequenceName}.playable";
        }

        // ------------------------------------------------------------------ //
        //  Cinemachine                                                        //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns true if any camera in the active scene has a CinemachineBrain.
        /// If false, the agent should warn the developer to add one.
        /// </summary>
        public static bool DetectCinemachineBrain()
        {
#if CM3
#if UNITY_2022_2_OR_NEWER
            return Object.FindFirstObjectByType<CinemachineBrain>() != null;
#else
            return Object.FindObjectOfType<CinemachineBrain>() != null;
#endif
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ //
        //  Package version reporting                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the installed Cinemachine package version string, or null if
        /// Cinemachine is not installed.
        /// </summary>
        public static string GetCinemachineVersion()
        {
#if CM3
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo
                    .FindForAssetPath("Packages/com.unity.cinemachine");
                return info?.version;
            }
            catch { return null; }
#else
            return null;
#endif
        }

        /// <summary>
        /// Returns the installed Timeline package version string, or null if not installed.
        /// </summary>
        public static string GetTimelineVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo
                    .FindForAssetPath("Packages/com.unity.timeline");
                return info?.version;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ //
        //  Filesystem helpers                                                 //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Creates the full folder path in the AssetDatabase, one segment at a time.
        /// Equivalent to Directory.CreateDirectory but for Unity's asset system.
        /// </summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var child  = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        // ------------------------------------------------------------------ //
        //  Utilities                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>Returns the last segment of a hierarchy path (the object name).</summary>
        public static string LastSegment(string hierarchyPath) =>
            string.IsNullOrEmpty(hierarchyPath)
                ? string.Empty
                : hierarchyPath.TrimEnd('/').Split('/')[^1];
    }
}
