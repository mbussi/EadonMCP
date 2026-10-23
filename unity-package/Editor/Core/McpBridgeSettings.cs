using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Core
{
    [CreateAssetMenu(menuName = "MCP Bridge/Settings")]
    public class McpBridgeSettings : ScriptableObject
    {
        internal const string AssetPath = "Assets/UnityMCP/Source/Editor/McpBridgeSettings.asset";

        [Tooltip("TCP port the HTTP listener binds to. Must match UNITY_MCP_PORT in the MCP server config.")]
        public int port = 11000;

        [Tooltip("Accept connections from other machines on the network, not just this one.\n\n" +
                 "Callers still need the bridge token (Window > MCP Bridge > Copy Bridge Token). " +
                 "The connection is plain HTTP, so the token travels unencrypted: use this on a trusted network only.\n\n" +
                 "Takes effect when the listener restarts (Window > MCP Bridge > Restart Listener).")]
        public bool allowRemoteConnections = false;

        [Tooltip("Automatically start the HTTP listener when the Editor loads.")]
        public bool startOnLoad = true;

        [Tooltip(
            "The SceneContext for the current level. If left empty, the bridge will try to auto-discover " +
            "a SceneContext asset in the project. Assign one explicitly to ensure the correct context " +
            "is used when multiple SceneContext assets exist.")]
        public SceneContext sceneContext;

        [Tooltip("Per-zone SceneContext overrides. Zone name must match a ScaffoldZone name exactly. " +
                 "When get_zone_context is called with a zone name, the first matching entry is returned. " +
                 "Falls back to sceneContext if no match is found.")]
        public List<ZoneContextEntry> zoneContexts = new List<ZoneContextEntry>();

        // -----------------------------------------------------------------------
        // Singleton accessor
        // -----------------------------------------------------------------------

        private static McpBridgeSettings _instance;

        public static McpBridgeSettings GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            _instance = AssetDatabase.LoadAssetAtPath<McpBridgeSettings>(AssetPath);
            if (_instance != null)
                return _instance;

            _instance = CreateInstance<McpBridgeSettings>();
            EnsureFolderExists(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(_instance, AssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MCP Bridge] Created default settings at {AssetPath}");
            return _instance;
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
            string name   = Path.GetFileName(folderPath);
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    /// <summary>Maps a scaffold zone name to a dedicated SceneContext asset.</summary>
    [System.Serializable]
    public class ZoneContextEntry
    {
        [Tooltip("Must match a ScaffoldZone name exactly (case-insensitive lookup).")]
        public string       zoneName;
        public SceneContext context;
    }
}
