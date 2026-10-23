// ============================================================
// Append the contents of this file to the bottom of Models.cs
// (just before the closing brace of the namespace block).
// ============================================================

    // -----------------------------------------------------------------------
    // Group 5 — create_postprocessing_volume
    // -----------------------------------------------------------------------

    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public class CreatePostProcessingVolumeRequest
    {
        /// <summary>World-space centre. Required for non-global volumes.</summary>
        [JsonProperty("position")]       public float[]               Position      { get; set; }

        /// <summary>Box extents [w, h, d] in world units. Null = point volume.</summary>
        [JsonProperty("size")]           public float[]               Size          { get; set; }

        /// <summary>Distance over which the volume blends in, in world units.</summary>
        [JsonProperty("blend_distance")] public float                 BlendDistance { get; set; } = 10f;

        /// <summary>True = affects the entire scene regardless of camera position.</summary>
        [JsonProperty("is_global")]      public bool                  IsGlobal      { get; set; }

        /// <summary>Higher values win when volumes overlap.</summary>
        [JsonProperty("priority")]       public float                 Priority      { get; set; }

        /// <summary>AssetDatabase path to an existing VolumeProfile to reuse and modify.</summary>
        [JsonProperty("profile_path")]   public string                ProfilePath   { get; set; }

        /// <summary>Name for a newly-created profile, saved under Assets/Volumes/.</summary>
        [JsonProperty("profile_name")]   public string                ProfileName   { get; set; }

        [JsonProperty("overrides")]      public VolumeOverrideData[]  Overrides     { get; set; }
        [JsonProperty("parent_path")]    public string                ParentPath    { get; set; }
        [JsonProperty("name")]           public string                Name          { get; set; }
    }

    public class VolumeOverrideData
    {
        /// <summary>
        /// C# class name of the VolumeComponent to add, e.g. "Bloom", "ColorAdjustments".
        /// Names are matched case-insensitively against UnityEngine.Rendering.Universal.* types.
        /// </summary>
        [JsonProperty("effect")]     public string                      Effect     { get; set; }

        /// <summary>
        /// Property names (C# field names) and their values.
        /// Use the exact field name from the VolumeComponent subclass (case-insensitive).
        /// Float/int/bool/enum(string or int)/Color([r,g,b,a]) all supported.
        /// </summary>
        [JsonProperty("properties")] public Dictionary<string, JToken>  Properties { get; set; }
    }

    public class CreatePostProcessingVolumeResultData
    {
        [JsonProperty("object_path")]        public string   ObjectPath       { get; set; }
        [JsonProperty("profile_path")]       public string   ProfilePath      { get; set; }
        [JsonProperty("is_global")]          public bool     IsGlobal         { get; set; }
        [JsonProperty("blend_distance")]     public float    BlendDistance    { get; set; }
        [JsonProperty("overrides_applied")]  public string[] OverridesApplied { get; set; }
        [JsonProperty("overrides_failed")]   public string[] OverridesFailed  { get; set; }
        [JsonProperty("warnings")]           public string[] Warnings         { get; set; }
    }
