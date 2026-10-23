using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Inspector for AgentSettings. Draws the asset's fields as usual and adds the
    /// API key as a password field after Base URL. The key is kept in this
    /// machine's EditorPrefs (AgentSettings.ApiKey), never in the asset.
    /// </summary>
    [CustomEditor(typeof(AgentSettings))]
    public class AgentSettingsEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ApiKeyLabel = new GUIContent(
            "API Key",
            "Stored on this machine (EditorPrefs), not in this asset, so copying or sharing " +
            "the project does not share the key.\n\n" +
            "Leave empty to use the ANTHROPIC_API_KEY or OPENAI_API_KEY environment variable.");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var settings = (AgentSettings)target;
            var property = serializedObject.GetIterator();
            var enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                    EditorGUILayout.PropertyField(property, true);

                if (property.propertyPath == "baseUrl")
                    DrawApiKey(settings);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawApiKey(AgentSettings settings)
        {
            var current = settings.ApiKey;

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUILayout.PasswordField(ApiKeyLabel, current);
            if (EditorGUI.EndChangeCheck())
                settings.ApiKey = edited;
        }
    }
}
