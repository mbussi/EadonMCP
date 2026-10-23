using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Agent
{
    public enum AgentProvider
    {
        Anthropic,
        OpenAICompatible
    }

    /// <summary>
    /// Configuration for the embedded agent chat window.
    /// Create via Assets > Create > Eadon > MCP Bridge > Agent Settings.
    /// Multiple instances can exist — one per provider or model preset.
    /// Assign the active one in Eadon > MCP Bridge > Agent Chat > Settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Eadon/MCP Bridge/Agent Settings")]
    public class AgentSettings : ScriptableObject
    {
        // -----------------------------------------------------------------------
        // Provider
        // -----------------------------------------------------------------------

        [Header("Provider")]
        [Tooltip("Anthropic: uses the /v1/messages API format.\n" +
                 "OpenAI-Compatible: uses /v1/chat/completions — works with LM Studio, Ollama, etc.")]
        public AgentProvider provider = AgentProvider.Anthropic;

        // -----------------------------------------------------------------------
        // Connection
        // -----------------------------------------------------------------------

        [Header("Connection")]
        [Tooltip("API base URL.\n" +
                 "Anthropic:   https://api.anthropic.com\n" +
                 "LM Studio:   http://localhost:1234\n" +
                 "Ollama:      http://localhost:11434\n" +
                 "OpenRouter:  https://openrouter.ai/api")]
        public string baseUrl = "https://api.anthropic.com";

        // The API key is not stored in this asset. The inspector (AgentSettingsEditor)
        // shows it as a password field and keeps it in this machine's EditorPrefs,
        // see ApiKey. This field only exists so a key saved in the asset by an
        // earlier version can be moved out of it (MoveKeyOutOfAsset).
        [SerializeField, HideInInspector]
        private string apiKey = "";

        // -----------------------------------------------------------------------
        // Model
        // -----------------------------------------------------------------------

        [Header("Model")]
        [Tooltip("Model name sent in the API request.\n" +
                 "Anthropic examples: claude-sonnet-4-6, claude-opus-4-6\n" +
                 "LM Studio: use the model identifier shown in the app")]
        public string modelName = "claude-sonnet-4-6";

        [Tooltip("Maximum tokens to generate per response. 8192 is a safe default.")]
        public int maxTokens = 8192;

        [Range(0f, 1f)]
        [Tooltip("Sampling temperature. Lower = more focused; higher = more varied.")]
        public float temperature = 0.7f;

        // -----------------------------------------------------------------------
        // Behaviour
        // -----------------------------------------------------------------------

        [Header("Behaviour")]
        [Tooltip("Automatically fetch scene context, prefab catalog, and hierarchy " +
                 "when the window opens and inject them into the system prompt. " +
                 "Costs a few round-trips but gives Claude immediate awareness of your scene.")]
        public bool autoLoadSceneContext = true;

        [Tooltip("Maximum hierarchy depth to load on window open. " +
                 "2 gives a useful overview without flooding the context.")]
        public int autoLoadHierarchyDepth = 2;

        [Tooltip("Ask before running any tool that changes the scene or the project.\n\n" +
                 "Off: the agent only asks before writing files, refreshing assets (which compiles " +
                 "scripts), deleting objects, opening or saving scenes, executing proposals and " +
                 "executing bakes.")]
        public bool confirmAllSceneChanges = false;

        [Header("System Prompt")]
        [TextArea(4, 10)]
        [Tooltip("System prompt sent with every request. " +
                 "Scene context is appended automatically when autoLoadSceneContext is enabled.")]
        public string systemPrompt =
            "You are an AI assistant embedded in the Unity Editor. " +
            "You have access to tools that let you inspect and modify the active Unity scene. " +
            "Before making changes, always verify the current scene state first. " +
            "Use propose_changes for bulk operations so the designer can review before executing. " +
            "Keep responses concise and actionable — the designer is working, not reading essays.\n\n" +
            "Do not introduce yourself or list your capabilities. Answer the user's request directly.\n\n" +
            "IMPORTANT — C# scripts: after writing script files with write_project_file, " +
            "compose your final summary response first, THEN call refresh_assets as the " +
            "last tool call. This prevents Unity's recompilation from disconnecting the " +
            "conversation before your response is delivered.";
        
        // -----------------------------------------------------------------------
        // Bridge
        // -----------------------------------------------------------------------

        [Header("Bridge")]
        [Tooltip("Port the Unity MCP HTTP bridge is listening on. " +
                 "Must match the port in Project Settings > Eadon > MCP Bridge (default 11000).")]
        public int bridgePort = 11000;

        // -----------------------------------------------------------------------
        // API key storage
        // -----------------------------------------------------------------------

        private const string ApiKeyPrefsPrefix = "UnityMcpBridge.AgentApiKey.";

        /// <summary>
        /// The API key for this settings asset, stored in EditorPrefs on this machine
        /// (keyed by the asset's GUID), never in the asset. Empty when none is set.
        /// Main thread only.
        /// </summary>
        public string ApiKey
        {
            get
            {
                var prefsKey = ApiKeyPrefsKey;
                return prefsKey == null ? string.Empty : EditorPrefs.GetString(prefsKey, string.Empty);
            }
            set
            {
                var prefsKey = ApiKeyPrefsKey;
                if (prefsKey == null) return;
                if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(prefsKey);
                else                                  EditorPrefs.SetString(prefsKey, value.Trim());
            }
        }

        /// <summary>Null until the asset has been saved, because the key is tied to its GUID.</summary>
        private string ApiKeyPrefsKey =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(this, out string guid, out long _) &&
            !string.IsNullOrEmpty(guid)
                ? ApiKeyPrefsPrefix + guid
                : null;

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(apiKey))
                EditorApplication.delayCall += MoveKeyOutOfAsset;
        }

        /// <summary>Moves keys out of every AgentSettings asset when the Editor loads.</summary>
        [InitializeOnLoadMethod]
        private static void MoveKeysOutOfAllAssets()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var guid in AssetDatabase.FindAssets("t:AgentSettings"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<AgentSettings>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null) asset.MoveKeyOutOfAsset();
                }
            };
        }

        /// <summary>
        /// Moves a key saved in the asset by an earlier version into EditorPrefs and
        /// blanks the asset field. A key already in EditorPrefs is kept.
        /// </summary>
        private void MoveKeyOutOfAsset()
        {
            if (this == null || string.IsNullOrEmpty(apiKey) || ApiKeyPrefsKey == null) return;

            if (string.IsNullOrEmpty(ApiKey))
                ApiKey = apiKey;

            apiKey = string.Empty;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);

            Debug.LogWarning(
                $"[MCP Agent] Moved the API key out of '{name}' into this machine's EditorPrefs. " +
                "It was stored in plain text in the asset, so replace it with a new key from your provider.");
        }

        // -----------------------------------------------------------------------
        // API key resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the API key to use: the key set in the inspector takes priority,
        /// then falls back to the provider-appropriate environment variable.
        /// </summary>
        public string ResolveApiKey()
        {
            var stored = ApiKey;
            if (!string.IsNullOrWhiteSpace(stored))
                return stored.Trim();

            var envVar = provider == AgentProvider.Anthropic
                ? "ANTHROPIC_API_KEY"
                : "OPENAI_API_KEY";

            return System.Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
        }

        public string BridgeBaseUrl => $"http://localhost:{bridgePort}";
    }
}
