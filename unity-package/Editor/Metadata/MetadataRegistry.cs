using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Metadata
{
    /// <summary>
    /// Aggregates all PrefabMetadata assets in the project and exposes
    /// query methods used by SceneQueryHandler.
    ///
    /// The registry is populated lazily on first access and can be refreshed
    /// explicitly via Reload(). Call Reload() after importing new metadata assets
    /// if you need the catalog to update without recompiling.
    /// </summary>
    public static class MetadataRegistry
    {
        private static List<PrefabMetadata> _cache;

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>All PrefabMetadata assets found in the project.</summary>
        public static IReadOnlyList<PrefabMetadata> All
        {
            get
            {
                if (_cache == null) Reload();
                return _cache;
            }
        }

        /// <summary>
        /// Returns the PrefabMetadata for a given prefab asset path, or null if
        /// no metadata asset has been created for it.
        /// </summary>
        public static PrefabMetadata ForPrefab(string assetPath)
        {
            if (_cache == null) Reload();
            return _cache.FirstOrDefault(m =>
                m.prefab != null &&
                AssetDatabase.GetAssetPath(m.prefab) == assetPath);
        }

        /// <summary>All metadata assets whose tags contain the given tag.</summary>
        public static IReadOnlyList<PrefabMetadata> WithTag(string tag) =>
            All.Where(m => m.tags != null && System.Array.IndexOf(m.tags, tag) >= 0)
               .ToList();

        /// <summary>
        /// All metadata assets that are compatible with the given SceneContext —
        /// i.e. they share at least one active tag and have none of the excluded tags.
        /// </summary>
        public static IReadOnlyList<PrefabMetadata> CompatibleWith(SceneContext context)
        {
            if (context == null) return All.ToList();

            return All.Where(m =>
            {
                if (m.tags == null) return false;

                // Must not have any excluded tag
                if (context.excludedTags != null && context.excludedTags.Length > 0)
                    if (m.tags.Any(t => System.Array.IndexOf(context.excludedTags, t) >= 0))
                        return false;

                // Must share at least one active tag (if active tags are specified)
                if (context.activeTags != null && context.activeTags.Length > 0)
                    return m.tags.Any(t => System.Array.IndexOf(context.activeTags, t) >= 0);

                return true;
            }).ToList();
        }

        /// <summary>
        /// Clears and repopulates the cache from AssetDatabase.
        /// Call this after importing new PrefabMetadata assets at runtime.
        /// </summary>
        public static void Reload()
        {
            var guids = AssetDatabase.FindAssets("t:PrefabMetadata");
            _cache = guids
                .Select(guid => AssetDatabase.LoadAssetAtPath<PrefabMetadata>(
                    AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m != null)
                .ToList();
        }
    }
}
