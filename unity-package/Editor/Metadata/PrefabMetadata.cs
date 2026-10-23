using System;
using UnityEngine;

namespace UnityMcpBridge.Editor.Metadata
{
    /// <summary>
    /// Describes a prefab for AI consumption. Create one asset per prefab that
    /// you want the AI to reason about, and keep it alongside the prefab in the
    /// project folder.
    ///
    /// Convention: Assets/Prefabs/City/Townhouse_A.prefab
    ///             Assets/Prefabs/City/Townhouse_A.metadata.asset
    ///
    /// The richer the metadata — especially the Description field — the better
    /// the AI's placement decisions will be.
    /// </summary>
    [CreateAssetMenu(menuName = "MCP Bridge/Prefab Metadata")]
    public class PrefabMetadata : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The prefab this metadata describes. Must be set for the registry to find it.")]
        public GameObject prefab;

        [Tooltip("Human-readable name used in AI responses. Defaults to the prefab filename if left empty.")]
        public string displayName;

        // -----------------------------------------------------------------------

        [Header("Classification")]
        [Tooltip("Broad category: building, prop, vegetation, light-fixture, terrain, etc.")]
        public string category;

        [Tooltip("Searchable tags: urban, medieval, ruined, indoor, stone, wooden, etc.")]
        public string[] tags;

        // -----------------------------------------------------------------------

        [Header("Context")]
        [Tooltip(
            "Plain-English description for the AI. Be specific about appearance, scale, and ideal usage. " +
            "Example: 'A narrow 3-storey brick townhouse. Suits densely packed 19th-century European city " +
            "streets. Pairs well with cobblestone roads and wrought-iron lamp posts.'")]
        [TextArea(3, 8)]
        public string description;

        [Tooltip("Tags of other assets this prefab pairs well with.")]
        public string[] compatibleWith;

        [Tooltip("Tags this prefab clashes with. The AI will avoid combining these.")]
        public string[] incompatibleWith;

        // -----------------------------------------------------------------------

        [Header("Placement Rules")]
        public PlacementRules placement;

        // -----------------------------------------------------------------------

        [Header("Scale Reference")]
        [Tooltip("Approximate bounding box in world units. Used by the AI for spacing decisions.")]
        public Vector3 approximateSize;
    }

    [Serializable]
    public class PlacementRules
    {
        [Tooltip("Object must be placed on a flat surface.")]
        public bool requiresFlatGround = true;

        [Tooltip("Multiple instances can be stacked vertically.")]
        public bool canStack = false;

        [Tooltip("Only valid inside enclosed spaces.")]
        public bool indoorOnly = false;

        [Tooltip("Only valid in open outdoor areas.")]
        public bool outdoorOnly = false;

        [Tooltip("Minimum distance in metres between instances of this prefab.")]
        public float minimumSpacing = 0f;

        [Tooltip("Preferred per-axis spacing when tiling or repeating this prefab.")]
        public Vector3 recommendedSpacing;
    }
}
