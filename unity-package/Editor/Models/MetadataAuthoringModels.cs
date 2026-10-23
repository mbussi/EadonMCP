using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 1 — Metadata Authoring
    // -----------------------------------------------------------------------

    /// <summary>
    /// Shared field set for both create and update operations.
    /// For CREATE: null fields are set to sensible defaults (empty string / empty array).
    /// For UPDATE: null fields are left unchanged on the existing asset.
    /// </summary>
    public class PrefabMetadataRequest
    {
        [JsonProperty("prefab_path")]      public string   PrefabPath      { get; set; }
        [JsonProperty("display_name")]     public string   DisplayName     { get; set; }
        [JsonProperty("category")]         public string   Category        { get; set; }
        [JsonProperty("tags")]             public string[] Tags            { get; set; }
        [JsonProperty("description")]      public string   Description     { get; set; }
        [JsonProperty("compatible_with")]  public string[] CompatibleWith  { get; set; }
        [JsonProperty("incompatible_with")]public string[] IncompatibleWith{ get; set; }

        // Placement rules — nullable so update can distinguish "not provided" from false
        [JsonProperty("requires_flat_ground")] public bool?  RequiresFlatGround { get; set; }
        [JsonProperty("can_stack")]            public bool?  CanStack           { get; set; }
        [JsonProperty("indoor_only")]          public bool?  IndoorOnly         { get; set; }
        [JsonProperty("outdoor_only")]         public bool?  OutdoorOnly        { get; set; }
        [JsonProperty("minimum_spacing")]      public float? MinimumSpacing     { get; set; }

        [JsonProperty("approximate_size")]     public float[] ApproximateSize   { get; set; }
    }

    public class CreatePrefabMetadataRequest : PrefabMetadataRequest
    {
        /// <summary>Replace existing metadata if present. Default false.</summary>
        [JsonProperty("overwrite")] public bool Overwrite { get; set; }
    }

    /// <summary>
    /// Update request — identical field set; all fields except prefab_path are optional.
    /// Omitted fields leave the existing asset unchanged.
    /// </summary>
    public class UpdatePrefabMetadataRequest : PrefabMetadataRequest { }

    public class MetadataOperationResultData
    {
        [JsonProperty("prefab_path")]    public string   PrefabPath    { get; set; }
        [JsonProperty("metadata_path")]  public string   MetadataPath  { get; set; }
        [JsonProperty("created",         NullValueHandling = NullValueHandling.Ignore)]
                                         public bool?    Created       { get; set; }
        [JsonProperty("overwritten",     NullValueHandling = NullValueHandling.Ignore)]
                                         public bool?    Overwritten   { get; set; }
        [JsonProperty("updated_fields",  NullValueHandling = NullValueHandling.Ignore)]
                                         public string[] UpdatedFields { get; set; }
    }

    // -----------------------------------------------------------------------

    public class UpdateSceneContextRequest
    {
        [JsonProperty("level_name")]           public string   LevelName          { get; set; }
        [JsonProperty("setting")]              public string   Setting            { get; set; }
        [JsonProperty("architectural_style")]  public string   ArchitecturalStyle { get; set; }
        [JsonProperty("era")]                  public string   Era                { get; set; }

        /// <summary>Full replacement for the active tags list.</summary>
        [JsonProperty("active_tags")]          public string[] ActiveTags         { get; set; }

        /// <summary>Tags to add to the existing list (ignored if active_tags is set).</summary>
        [JsonProperty("add_active_tags")]      public string[] AddActiveTags      { get; set; }

        /// <summary>Tags to remove from the existing list (ignored if active_tags is set).</summary>
        [JsonProperty("remove_active_tags")]   public string[] RemoveActiveTags   { get; set; }

        /// <summary>Full replacement for the excluded tags list.</summary>
        [JsonProperty("excluded_tags")]        public string[] ExcludedTags       { get; set; }

        /// <summary>Full replacement for the notes field.</summary>
        [JsonProperty("notes")]                public string   Notes              { get; set; }

        /// <summary>Append to the existing notes (ignored if notes is set).</summary>
        [JsonProperty("append_notes")]         public string   AppendNotes        { get; set; }
    }

    public class SceneContextUpdateResultData
    {
        [JsonProperty("context_path")]   public string   ContextPath   { get; set; }
        [JsonProperty("updated_fields")] public string[] UpdatedFields { get; set; }
    }
}