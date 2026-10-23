using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 2 — paint_terrain_layer
    // -----------------------------------------------------------------------

    public class PaintTerrainLayerRequest
    {
        [JsonProperty("position")]     public float[] Position    { get; set; }
        [JsonProperty("radius")]       public float   Radius      { get; set; }
        [JsonProperty("layer_name")]   public string  LayerName   { get; set; }

        /// <summary>Brush strength 0–1. Default 0.5.</summary>
        [JsonProperty("opacity")]      public float   Opacity     { get; set; } = 0.5f;

        /// <summary>"smooth" (S-curve, default), "linear", or "constant".</summary>
        [JsonProperty("falloff")]      public string  Falloff     { get; set; } = "smooth";

        /// <summary>"add" (increase layer, default) or "set" (force layer to opacity).</summary>
        [JsonProperty("blend_mode")]   public string  BlendMode   { get; set; } = "add";

        /// <summary>Target a specific terrain tile by name. Null = tile covering position.</summary>
        [JsonProperty("terrain_name")] public string  TerrainName { get; set; }
    }

    public class PaintTerrainLayerResultData
    {
        [JsonProperty("terrain_name")]    public string   TerrainName    { get; set; }
        [JsonProperty("layer_painted")]   public string   LayerPainted   { get; set; }
        [JsonProperty("layer_index")]     public int      LayerIndex     { get; set; }
        [JsonProperty("pixels_affected")] public int      PixelsAffected { get; set; }
        [JsonProperty("warnings")]        public string[] Warnings       { get; set; }
    }

    // -----------------------------------------------------------------------
    // Improvements Phase 2 — bake_lighting
    // -----------------------------------------------------------------------

    public class BakeLightingRequest
    {
        /// <summary>"async" (default: start a background bake and return its job) or "clear".</summary>
        [JsonProperty("mode")]                   public string Mode                { get; set; } = "async";

        /// <summary>Only changes the message: reflection probes set to Baked are captured by every bake.</summary>
        [JsonProperty("bake_reflection_probes")] public bool   BakeReflectionProbes { get; set; }
    }

    public class BakeLightingResultData
    {
        [JsonProperty("mode")]                                            public string Mode      { get; set; }
        [JsonProperty("started")]                                         public bool   Started   { get; set; }
        [JsonProperty("completed", NullValueHandling = NullValueHandling.Ignore)] public bool? Completed  { get; set; }
        [JsonProperty("elapsed_ms",NullValueHandling = NullValueHandling.Ignore)] public long? ElapsedMs  { get; set; }
        [JsonProperty("job_id",    NullValueHandling = NullValueHandling.Ignore)] public string JobId     { get; set; }
        [JsonProperty("message")]                                         public string Message   { get; set; }
    }
}