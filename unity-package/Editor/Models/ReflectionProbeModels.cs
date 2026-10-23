using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 3 — create_reflection_probe
    // -----------------------------------------------------------------------

    public class CreateReflectionProbeRequest
    {
        [JsonProperty("position")]        public float[] Position       { get; set; }

        /// <summary>Influence box extents [w, h, d]. Defaults to [10, 5, 10].</summary>
        [JsonProperty("size")]            public float[] Size           { get; set; }

        /// <summary>"baked" (default), "realtime", or "custom".</summary>
        [JsonProperty("mode")]            public string  Mode           { get; set; } = "baked";

        /// <summary>Priority when probe volumes overlap. Higher wins. Default 1.</summary>
        [JsonProperty("importance")]      public int     Importance     { get; set; } = 1;

        /// <summary>Reflection intensity multiplier 0–1. Default 1.0.</summary>
        [JsonProperty("intensity")]       public float?  Intensity      { get; set; }

        /// <summary>Use HDR capture format. Default true.</summary>
        [JsonProperty("hdr")]             public bool?   Hdr            { get; set; }

        /// <summary>Cubemap resolution. Must be power of 2 from 16–2048. Default 128.</summary>
        [JsonProperty("resolution")]      public int     Resolution     { get; set; } = 128;

        [JsonProperty("parent_path")]     public string  ParentPath     { get; set; }
        [JsonProperty("name")]            public string  Name           { get; set; }
    }

    public class CreateReflectionProbeResultData
    {
        [JsonProperty("object_path")] public string   ObjectPath { get; set; }
        [JsonProperty("mode")]        public string   Mode       { get; set; }
        [JsonProperty("importance")]  public int      Importance { get; set; }
        [JsonProperty("size")]        public float[]  Size       { get; set; }
        [JsonProperty("resolution")]  public int      Resolution { get; set; }
    }
}