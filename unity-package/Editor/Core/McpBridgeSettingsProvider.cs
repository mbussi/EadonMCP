using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>The Project Settings > Eadon > MCP Bridge page.</summary>
    public class McpBridgeSettingsProvider : SettingsProvider
    {
        private const string PagePath = "Project/Eadon/MCP Bridge";

        private SerializedObject serializedSettings;

        private McpBridgeSettingsProvider()
            : base(PagePath, SettingsScope.Project,
                   new HashSet<string> { "MCP", "bridge", "port", "agent", "scene context", "remote" })
        {
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new McpBridgeSettingsProvider();

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            serializedSettings = new SerializedObject(McpBridgeSettings.instance);
        }

        public override void OnGUI(string searchContext)
        {
            if (serializedSettings == null || serializedSettings.targetObject == null)
                serializedSettings = new SerializedObject(McpBridgeSettings.instance);

            serializedSettings.Update();

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Listener", EditorStyles.boldLabel);
                Draw("port");
                Draw("allowRemoteConnections");
                Draw("startOnLoad");

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(McpHttpListener.IsRunning ? "Listener running" : "Listener stopped");
                    if (GUILayout.Button("Restart Listener", GUILayout.Width(140)))
                        McpHttpListener.Restart();
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Scene Context", EditorStyles.boldLabel);
                Draw("sceneContext");
                Draw("zoneContexts");
            }

            if (serializedSettings.ApplyModifiedProperties())
                McpBridgeSettings.instance.SaveSettings();
        }

        private void Draw(string propertyName)
        {
            var property = serializedSettings.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }
    }
}
