// ============================================================
// Append the contents of this file to the bottom of Models.cs
// (just before the closing brace of the namespace block).
// ============================================================

    // -----------------------------------------------------------------------
    // Group 5 — validate_scene
    // -----------------------------------------------------------------------

    using Newtonsoft.Json;

    public class ValidateSceneRequest
    {
        /// <summary>
        /// Check IDs to run. Null or empty runs all checks.
        /// Valid IDs: missing_scripts, missing_materials, untagged_triggers,
        /// duplicate_names, meshrenderer_no_collider, large_light_range, floating_objects
        /// </summary>
        [JsonProperty("checks")]                 public string[] Checks              { get; set; }

        /// <summary>Hierarchy path to restrict the scan. Null scans the full active scene.</summary>
        [JsonProperty("scope_path")]             public string   ScopePath           { get; set; }

        /// <summary>Maximum issues returned per check to avoid flooding the response.</summary>
        [JsonProperty("max_results_per_check")]  public int      MaxResultsPerCheck  { get; set; } = 50;

        /// <summary>Range in metres above which Point/Spot lights are flagged (large_light_range check).</summary>
        [JsonProperty("light_range_threshold")]  public float    LightRangeThreshold { get; set; } = 100f;

        /// <summary>Downward ray distance in metres for the floating_objects check.</summary>
        [JsonProperty("floating_ray_distance")]  public float    FloatingRayDistance { get; set; } = 0.5f;

        /// <summary>
        /// Vertex count above which a MeshRenderer without a LODGroup is flagged
        /// (missing_lod_groups check). Default 2000.
        /// </summary>
        [JsonProperty("lod_vertex_threshold")]   public int      LodVertexThreshold  { get; set; } = 2000;
    }

    public class ValidateSceneResultData
    {
        [JsonProperty("total_issues")] public int                       TotalIssues { get; set; }
        [JsonProperty("checks_run")]   public string[]                  ChecksRun   { get; set; }
        [JsonProperty("results")]      public ValidationCheckResultData[] Results   { get; set; }
    }

    public class ValidationCheckResultData
    {
        [JsonProperty("check_id")]    public string                CheckId     { get; set; }
        [JsonProperty("severity")]    public string                Severity    { get; set; }
        [JsonProperty("description")] public string                Description { get; set; }
        [JsonProperty("count")]       public int                   Count       { get; set; }
        [JsonProperty("issues")]      public ValidationIssueData[] Issues      { get; set; }
    }

    public class ValidationIssueData
    {
        [JsonProperty("path")]   public string Path   { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
    }

    // -----------------------------------------------------------------------
    // Group 5 — create_audio_zone
    // -----------------------------------------------------------------------

    public class CreateAudioZoneRequest
    {
        /// <summary>"reverb_zone" or "ambient_source"</summary>
        [JsonProperty("zone_type")]       public string  ZoneType      { get; set; }

        /// <summary>Reverb preset name. Required for reverb_zone; ignored for ambient_source.</summary>
        [JsonProperty("preset")]          public string  Preset        { get; set; }

        [JsonProperty("position")]        public float[] Position      { get; set; }
        [JsonProperty("min_distance")]    public float   MinDistance   { get; set; } = 1f;
        [JsonProperty("max_distance")]    public float   MaxDistance   { get; set; } = 10f;

        /// <summary>AssetDatabase path to AudioClip. Required for ambient_source.</summary>
        [JsonProperty("audio_clip_path")] public string  AudioClipPath { get; set; }

        /// <summary>Playback volume 0–1 for ambient_source (default 1.0).</summary>
        [JsonProperty("volume")]          public float?  Volume        { get; set; }

        /// <summary>Loop playback for ambient_source (default true).</summary>
        [JsonProperty("loop")]            public bool?   Loop          { get; set; }

        /// <summary>Auto-play on scene start for ambient_source (default true).</summary>
        [JsonProperty("play_on_awake")]   public bool?   PlayOnAwake   { get; set; }

        [JsonProperty("parent_path")]     public string  ParentPath    { get; set; }
        [JsonProperty("name")]            public string  Name          { get; set; }
    }

    public class AudioZoneResultData
    {
        [JsonProperty("object_path")]  public string   ObjectPath  { get; set; }
        [JsonProperty("zone_type")]    public string   ZoneType    { get; set; }
        [JsonProperty("preset",        NullValueHandling = NullValueHandling.Ignore)]
                                       public string   Preset      { get; set; }
        [JsonProperty("min_distance")] public float    MinDistance { get; set; }
        [JsonProperty("max_distance")] public float    MaxDistance { get; set; }
        [JsonProperty("warnings")]     public string[] Warnings    { get; set; }
    }
