using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Project-wide bridge settings, stored in ProjectSettings/EadonMcpBridgeSettings.asset
    /// and edited in Project Settings > Eadon > MCP Bridge (McpBridgeSettingsProvider).
    ///
    /// Stored under ProjectSettings rather than Assets because the bridge ships as a
    /// package: the package folder is not the project's to write to, and these values
    /// belong to the project, not to the package.
    /// </summary>
    [FilePath("ProjectSettings/EadonMcpBridgeSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class McpBridgeSettings : ScriptableSingleton<McpBridgeSettings>
    {
        [Tooltip("TCP port the HTTP listener binds to. Must match UNITY_MCP_PORT in the MCP server config.")]
        public int port = 11000;

        [Tooltip("Accept connections from other machines on the network, not just this one.\n\n" +
                 "Callers still need the bridge token (Eadon > MCP Bridge > Copy Bridge Token). " +
                 "The connection is plain HTTP, so the token travels unencrypted: use this on a trusted network only.\n\n" +
                 "Takes effect when the listener restarts (Eadon > MCP Bridge > Restart Listener).")]
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
        // Access
        // -----------------------------------------------------------------------

        /// <summary>The settings for this project. Kept for existing callers; same as <c>instance</c>.</summary>
        public static McpBridgeSettings GetOrCreate() => instance;

        /// <summary>Writes the settings to ProjectSettings.</summary>
        public void SaveSettings() => Save(true);

        private void OnEnable()
        {
            // ScriptableSingleton instances are created NotEditable; the settings page edits this one.
            hideFlags &= ~HideFlags.NotEditable;
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
