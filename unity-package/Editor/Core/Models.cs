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

        /// <summary>
        /// True when the request is still running: Data is then the job (job_id, state),
        /// to poll with GET /jobs?job_id=…. Sent with HTTP 202.
        /// </summary>
        [JsonProperty("pending", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Pending { get; private set; }

        public static McpResponse Ok(object data) =>
            new McpResponse { Success = true, Data = data };

        public static McpResponse JobPending(object job) =>
            new McpResponse { Success = true, Pending = true, Data = job };

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