using System;
using UnityEditor;

namespace UnityMcpBridge.Editor.Metadata
{
    /// <summary>
    /// Clears MetadataRegistry's cache when PrefabMetadata assets change on disk, so
    /// metadata created, deleted or moved by hand reaches dress_area and the prefab
    /// catalog without a script reload. Imported assets are checked by type; deleted
    /// and moved ".asset" files clear it unconditionally, since a deleted asset's type
    /// can no longer be read. Reloading is lazy, so a spurious clear costs one search.
    /// </summary>
    internal sealed class MetadataCacheInvalidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Touches(deletedAssets) || Touches(movedAssets) || Touches(movedFromAssetPaths))
            {
                MetadataRegistry.Invalidate();
                return;
            }

            foreach (var path in importedAssets)
            {
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(PrefabMetadata))
                {
                    MetadataRegistry.Invalidate();
                    return;
                }
            }
        }

        private static bool Touches(string[] paths)
        {
            foreach (var path in paths)
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
