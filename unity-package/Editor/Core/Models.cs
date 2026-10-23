using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Envelope
    // -----------------------------------------------------------------------

    public class McpResponse
    {
        [JsonProperty("success")]
        public bool Success { get; private set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; private set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public McpError Error { get; private set; }

        public static McpResponse Ok(object data) =>
            new McpResponse { Success = true, Data = data };

        public static McpResponse Fail(string code, string message) =>
            new McpResponse { Success = false, Error = new McpError { Code = code, Message = message } };
    }

    public class McpError
    {
        [JsonProperty("code")]    public string Code    { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    public class StatusData
    {
        [JsonProperty("unity_version")] public string UnityVersion { get; set; }
        [JsonProperty("scene_name")]    public string SceneName    { get; set; }
        [JsonProperty("scene_path")]    public string ScenePath    { get; set; }
        [JsonProperty("is_playing")]    public bool   IsPlaying    { get; set; }
    }
}