using UnityEngine;

namespace UnityMcpBridge.Editor.Metadata
{
    /// <summary>
    /// Describes a level's design intent so that the AI can make contextually
    /// appropriate choices. Create one per scene (or share one across a group
    /// of related scenes) and assign it in Project Settings > Eadon > MCP Bridge.
    ///
    /// The AI reads this at the start of every session before selecting prefabs
    /// or proposing changes. The more specific the Notes field, the better the
    /// AI's suggestions will be.
    /// </summary>
    [CreateAssetMenu(menuName = "Eadon/MCP Bridge/Scene Context")]
    public class SceneContext : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name of the level. Used in AI responses to identify the scene.")]
        public string levelName;

        // -----------------------------------------------------------------------

        [Header("Setting")]
        [Tooltip("The broad environment type: city, dungeon, forest, desert, spaceship, etc.")]
        public string setting;

        [Tooltip("Architectural or visual style: 19th century European, brutalist, gothic, cyberpunk, etc.")]
        public string architecturalStyle;

        [Tooltip("Historical or fictional era: medieval, victorian, contemporary, far future, etc.")]
        public string era;

        // -----------------------------------------------------------------------

        [Header("Asset Preferences")]
        [Tooltip(
            "Tags the AI should prefer when selecting prefabs for this scene. " +
            "These should match tags used in PrefabMetadata assets.")]
        public string[] activeTags;

        [Tooltip(
            "Tags the AI must avoid in this scene. " +
            "Prefabs with any of these tags will not be suggested.")]
        public string[] excludedTags;

        // -----------------------------------------------------------------------

        [Header("Design Notes")]
        [Tooltip(
            "Freeform notes about the level's intent, constraints, atmosphere, and tone. " +
            "The AI reads this verbatim — write it as you would a design brief. " +
            "Example: 'This is a run-down industrial district on the outskirts of a Victorian city. " +
            "Buildings should look weathered and asymmetric. Avoid anything that looks pristine or modern. " +
            "Street-level clutter (barrels, crates, broken fences) is encouraged.'")]
        [TextArea(5, 12)]
        public string notes;
    }
}
