using Newtonsoft.Json;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Improvements Phase 4 — capture_scene_view
    // -----------------------------------------------------------------------

    public class CaptureSceneViewRequest
    {
        /// <summary>Capture width in pixels. Default 1280.</summary>
        [JsonProperty("width")]      public int    Width      { get; set; } = 1280;

        /// <summary>Capture height in pixels. Default 720.</summary>
        [JsonProperty("height")]     public int    Height     { get; set; } = 720;

        /// <summary>JPEG quality 1–100. Default 85.</summary>
        [JsonProperty("quality")]    public int    Quality    { get; set; } = 85;

        /// <summary>
        /// If provided, frames the Scene View to show this hierarchy path before
        /// capturing. The view pivot and size are set from the object's renderer bounds.
        /// </summary>
        [JsonProperty("focus_path")] public string FocusPath  { get; set; }
    }

    public class CaptureSceneViewResultData
    {
        [JsonProperty("image_base64")] public string   ImageBase64 { get; set; }
        [JsonProperty("format")]       public string   Format      { get; set; }
        [JsonProperty("width")]        public int      Width       { get; set; }
        [JsonProperty("height")]       public int      Height      { get; set; }

        [JsonProperty("focused_on", NullValueHandling = NullValueHandling.Ignore)]
        public string   FocusedOn   { get; set; }

        [JsonProperty("warnings")]     public string[] Warnings    { get; set; }
    }
}