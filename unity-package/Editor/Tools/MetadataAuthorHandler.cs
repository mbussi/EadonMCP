using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Metadata;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Closes the metadata authoring loop: Claude can now create and update the
    /// PrefabMetadata and SceneContext assets that the rest of the system's quality
    /// depends on.
    ///
    ///   POST /tools/metadata/create_prefab_metadata — create a new .metadata.asset
    ///   POST /tools/metadata/update_prefab_metadata — update fields on existing asset
    ///   POST /tools/metadata/update_scene_context   — write to the active SceneContext
    ///
    /// Both metadata operations call MetadataRegistry.Reload() immediately after saving
    /// so the new data is visible to get_prefab_catalog and dress_area within the same
    /// conversation without requiring an Editor restart.
    ///
    /// TODO: ResolveActiveSceneContext duplicates SceneQueryHandler.ResolveSceneContext.
    ///       Extract to a shared helper (e.g. MetadataRegistry or McpBridgeSettings)
    ///       when convenient.
    /// </summary>
    public static class MetadataAuthorHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/metadata/create_prefab_metadata
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreatePrefabMetadataAsync(
            CreatePrefabMetadataRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(req.PrefabPath);
                if (prefab == null)
                    return McpResponse.Fail("PREFAB_NOT_FOUND",
                        $"No prefab found at '{req.PrefabPath}'. " +
                        "Verify the exact AssetDatabase path (e.g. Assets/Prefabs/Props/Barrel.prefab).");

                // Check for existing metadata
                var existing     = MetadataRegistry.ForPrefab(req.PrefabPath);
                bool overwritten = false;

                if (existing != null && !req.Overwrite)
                {
                    var existingPath = AssetDatabase.GetAssetPath(existing);
                    return McpResponse.Fail("METADATA_EXISTS",
                        $"PrefabMetadata already exists at '{existingPath}'. " +
                        "Set overwrite=true to replace it, or use update_prefab_metadata to modify specific fields.");
                }

                if (existing != null)
                {
                    // Remove existing asset before creating the replacement
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(existing));
                    overwritten = true;
                }

                // Asset path convention: Prefab_Name.metadata.asset next to the prefab
                var dir      = Path.GetDirectoryName(req.PrefabPath)?.Replace('\\', '/');
                var baseName = Path.GetFileNameWithoutExtension(req.PrefabPath);
                var metaPath = $"{dir}/{baseName}.metadata.asset";

                // Ensure the directory exists (should always be true if prefab exists, but be safe)
                EnsureFolder(dir);

                var metadata = ScriptableObject.CreateInstance<PrefabMetadata>();
                PopulateMetadata(metadata, req, prefab, isCreate: true);

                AssetDatabase.CreateAsset(metadata, metaPath);
                AssetDatabase.SaveAssets();
                MetadataRegistry.Reload();

                return McpResponse.Ok(new MetadataOperationResultData
                {
                    PrefabPath   = req.PrefabPath,
                    MetadataPath = metaPath,
                    Created      = true,
                    Overwritten  = overwritten ? true : (bool?)null
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/metadata/update_prefab_metadata
        // -----------------------------------------------------------------------

        public static Task<McpResponse> UpdatePrefabMetadataAsync(
            UpdatePrefabMetadataRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.PrefabPath))
                    return McpResponse.Fail("INVALID_PARAMS", "prefab_path must not be empty.");

                var existing = MetadataRegistry.ForPrefab(req.PrefabPath);
                if (existing == null)
                    return McpResponse.Fail("METADATA_NOT_FOUND",
                        $"No PrefabMetadata found for '{req.PrefabPath}'. " +
                        "Use create_prefab_metadata to create one first.");

                var metadataPath  = AssetDatabase.GetAssetPath(existing);
                var updatedFields = new List<string>();

                Undo.RecordObject(existing, "MCP Update PrefabMetadata");
                UpdateMetadataFields(existing, req, updatedFields);
                EditorUtility.SetDirty(existing);

                AssetDatabase.SaveAssets();
                MetadataRegistry.Reload();

                return McpResponse.Ok(new MetadataOperationResultData
                {
                    PrefabPath    = req.PrefabPath,
                    MetadataPath  = metadataPath,
                    UpdatedFields = updatedFields.ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/metadata/update_scene_context
        // -----------------------------------------------------------------------

        public static Task<McpResponse> UpdateSceneContextAsync(
            UpdateSceneContextRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req == null)
                    return McpResponse.Fail("INVALID_PARAMS", "Request body must not be empty.");

                var context = ResolveActiveSceneContext();
                if (context == null)
                    return McpResponse.Fail("NO_SCENE_CONTEXT",
                        "No SceneContext asset is configured or discoverable. " +
                        "Create one via Assets > Create > Eadon > MCP Bridge > Scene Context, " +
                        "then assign it in Project Settings > Eadon > MCP Bridge.");

                var contextPath   = AssetDatabase.GetAssetPath(context);
                var updatedFields = new List<string>();

                Undo.RecordObject(context, "MCP Update SceneContext");

                // Identity fields — null means "leave unchanged"
                if (req.LevelName          != null) { context.levelName          = req.LevelName;          updatedFields.Add("level_name"); }
                if (req.Setting            != null) { context.setting            = req.Setting;            updatedFields.Add("setting"); }
                if (req.ArchitecturalStyle != null) { context.architecturalStyle = req.ArchitecturalStyle; updatedFields.Add("architectural_style"); }
                if (req.Era                != null) { context.era                = req.Era;                updatedFields.Add("era"); }

                // Active tags: full replacement takes priority over add/remove
                if (req.ActiveTags != null)
                {
                    context.activeTags = req.ActiveTags;
                    updatedFields.Add("active_tags");
                }
                else if (req.AddActiveTags != null || req.RemoveActiveTags != null)
                {
                    var current     = new List<string>(context.activeTags ?? System.Array.Empty<string>());
                    bool tagChanged = false;

                    if (req.AddActiveTags != null)
                        foreach (var tag in req.AddActiveTags)
                            if (!current.Contains(tag)) { current.Add(tag); tagChanged = true; }

                    if (req.RemoveActiveTags != null)
                        foreach (var tag in req.RemoveActiveTags)
                            tagChanged |= current.Remove(tag);

                    if (tagChanged)
                    {
                        context.activeTags = current.ToArray();
                        updatedFields.Add("active_tags");
                    }
                }

                // Excluded tags: full replacement only
                if (req.ExcludedTags != null)
                {
                    context.excludedTags = req.ExcludedTags;
                    updatedFields.Add("excluded_tags");
                }

                // Notes: full replacement takes priority over append
                if (req.Notes != null)
                {
                    context.notes = req.Notes;
                    updatedFields.Add("notes");
                }
                else if (req.AppendNotes != null)
                {
                    var existing = context.notes ?? string.Empty;
                    context.notes = string.IsNullOrEmpty(existing)
                        ? req.AppendNotes
                        : existing + "\n\n" + req.AppendNotes;
                    updatedFields.Add("notes");
                }

                EditorUtility.SetDirty(context);
                AssetDatabase.SaveAssets();

                return McpResponse.Ok(new SceneContextUpdateResultData
                {
                    ContextPath   = contextPath,
                    UpdatedFields = updatedFields.ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Populates a PrefabMetadata instance from a request.
        /// When isCreate=true, null fields are replaced with empty defaults.
        /// When isCreate=false (update), null fields are left unchanged.
        /// </summary>
        private static void PopulateMetadata(
            PrefabMetadata       metadata,
            PrefabMetadataRequest req,
            GameObject           prefab,
            bool                 isCreate)
        {
            if (isCreate)
            {
                // Create — set everything; null → sensible default
                metadata.prefab           = prefab;
                metadata.displayName      = req.DisplayName     ?? string.Empty;
                metadata.category         = req.Category        ?? string.Empty;
                metadata.tags             = req.Tags            ?? System.Array.Empty<string>();
                metadata.description      = req.Description     ?? string.Empty;
                metadata.compatibleWith   = req.CompatibleWith  ?? System.Array.Empty<string>();
                metadata.incompatibleWith = req.IncompatibleWith ?? System.Array.Empty<string>();

                if (req.ApproximateSize != null && req.ApproximateSize.Length >= 3)
                    metadata.approximateSize = new Vector3(
                        req.ApproximateSize[0], req.ApproximateSize[1], req.ApproximateSize[2]);

                if (metadata.placement == null)
                    metadata.placement = new PlacementRules();

                if (req.RequiresFlatGround.HasValue) metadata.placement.requiresFlatGround = req.RequiresFlatGround.Value;
                if (req.CanStack.HasValue)           metadata.placement.canStack           = req.CanStack.Value;
                if (req.IndoorOnly.HasValue)         metadata.placement.indoorOnly         = req.IndoorOnly.Value;
                if (req.OutdoorOnly.HasValue)        metadata.placement.outdoorOnly        = req.OutdoorOnly.Value;
                if (req.MinimumSpacing.HasValue)     metadata.placement.minimumSpacing     = req.MinimumSpacing.Value;
            }
            else
            {
                // This branch is not used — update goes through UpdateMetadataFields
                UpdateMetadataFields(metadata, req, new List<string>());
            }
        }

        /// <summary>
        /// Updates only the fields that are explicitly provided in the request
        /// (null = leave existing value unchanged). Records changed field names.
        /// </summary>
        private static void UpdateMetadataFields(
            PrefabMetadata        metadata,
            PrefabMetadataRequest req,
            List<string>          updatedFields)
        {
            if (req.DisplayName      != null) { metadata.displayName      = req.DisplayName;      updatedFields.Add("display_name"); }
            if (req.Category         != null) { metadata.category         = req.Category;         updatedFields.Add("category"); }
            if (req.Tags             != null) { metadata.tags             = req.Tags;             updatedFields.Add("tags"); }
            if (req.Description      != null) { metadata.description      = req.Description;      updatedFields.Add("description"); }
            if (req.CompatibleWith   != null) { metadata.compatibleWith   = req.CompatibleWith;   updatedFields.Add("compatible_with"); }
            if (req.IncompatibleWith != null) { metadata.incompatibleWith = req.IncompatibleWith; updatedFields.Add("incompatible_with"); }

            if (req.ApproximateSize != null && req.ApproximateSize.Length >= 3)
            {
                metadata.approximateSize = new Vector3(
                    req.ApproximateSize[0], req.ApproximateSize[1], req.ApproximateSize[2]);
                updatedFields.Add("approximate_size");
            }

            if (req.RequiresFlatGround.HasValue || req.CanStack.HasValue ||
                req.IndoorOnly.HasValue         || req.OutdoorOnly.HasValue ||
                req.MinimumSpacing.HasValue)
            {
                if (metadata.placement == null)
                    metadata.placement = new PlacementRules();

                if (req.RequiresFlatGround.HasValue) { metadata.placement.requiresFlatGround = req.RequiresFlatGround.Value; updatedFields.Add("requires_flat_ground"); }
                if (req.CanStack.HasValue)           { metadata.placement.canStack           = req.CanStack.Value;           updatedFields.Add("can_stack"); }
                if (req.IndoorOnly.HasValue)         { metadata.placement.indoorOnly         = req.IndoorOnly.Value;         updatedFields.Add("indoor_only"); }
                if (req.OutdoorOnly.HasValue)        { metadata.placement.outdoorOnly        = req.OutdoorOnly.Value;        updatedFields.Add("outdoor_only"); }
                if (req.MinimumSpacing.HasValue)     { metadata.placement.minimumSpacing     = req.MinimumSpacing.Value;     updatedFields.Add("minimum_spacing"); }
            }
        }

        /// <summary>
        /// Replicates SceneQueryHandler.ResolveSceneContext — reads from McpBridgeSettings
        /// first, then falls back to auto-discovery. Duplicated here to avoid coupling;
        /// extract to a shared helper when it appears in a third location.
        /// </summary>
        private static SceneContext ResolveActiveSceneContext()
        {
            var ctx = McpBridgeSettings.GetOrCreate().sceneContext;
            if (ctx != null) return ctx;

            var guids = AssetDatabase.FindAssets("t:SceneContext");
            if (guids.Length == 0) return null;

            return AssetDatabase.LoadAssetAtPath<SceneContext>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
