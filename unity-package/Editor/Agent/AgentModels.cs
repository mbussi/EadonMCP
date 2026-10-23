using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Agent
{
    // -----------------------------------------------------------------------
    // Conversation
    // -----------------------------------------------------------------------

    public enum MessageRole { User, Assistant, ToolResult }

    /// <summary>
    /// Format-neutral conversation message. Each client translates this to
    /// the correct wire format (Anthropic or OpenAI).
    /// </summary>
    public class ConversationMessage
    {
        public MessageRole       Role        { get; set; }
        public string            Text        { get; set; }

        /// <summary>Tool calls returned by the assistant. Populated on Role=Assistant.</summary>
        public List<ToolUse>     ToolUses    { get; set; }

        /// <summary>Results to return to the model. Populated on Role=ToolResult.</summary>
        public List<ToolResult>  ToolResults { get; set; }
    }

    // -----------------------------------------------------------------------
    // Tool use / results
    // -----------------------------------------------------------------------

    public class ToolUse
    {
        public string   Id    { get; set; }
        public string   Name  { get; set; }
        public JObject  Input { get; set; }
    }

    public class ToolResult
    {
        public string  ToolUseId   { get; set; }
        public string  TextContent { get; set; }

        /// <summary>Non-null when the tool returned an image (e.g. capture_scene_view).</summary>
        public byte[]  ImageBytes  { get; set; }
        public string  ImageMime   { get; set; } = "image/jpeg";

        public bool HasImage => ImageBytes != null && ImageBytes.Length > 0;
    }

    // -----------------------------------------------------------------------
    // Agent response (normalised across providers)
    // -----------------------------------------------------------------------

    public class AgentResponse
    {
        public string         Text       { get; set; }
        public string         StopReason { get; set; }  // "end_turn" | "tool_use" | "error"
        public List<ToolUse>  ToolUses   { get; set; } = new();
        public string         Error      { get; set; }

        public bool IsToolUse  => StopReason == "tool_use";
        public bool IsEndTurn  => StopReason == "end_turn";
        public bool IsError    => StopReason == "error";
    }

    // -----------------------------------------------------------------------
    // Tool schema
    // -----------------------------------------------------------------------

    public class ToolSchema
    {
        public string  Name        { get; set; }
        public string  Description { get; set; }

        /// <summary>
        /// JSON Schema object for the tool's input parameters.
        /// Shape: { "type": "object", "properties": {...}, "required": [...] }
        /// </summary>
        public JObject InputSchema  { get; set; }
    }

    // -----------------------------------------------------------------------
    // Tool execution result
    // -----------------------------------------------------------------------

    /// <summary>
    /// Result from executing a tool call against the Unity bridge HTTP server.
    /// Carries both the text response and any image data (from capture_scene_view).
    /// </summary>
    public class ToolExecutionResult
    {
        public string TextContent  { get; set; }
        public byte[] ImageBytes   { get; set; }
        public string ImageMime    { get; set; } = "image/jpeg";
        public bool   Success      { get; set; } = true;
        public string ErrorMessage { get; set; }

        public bool HasImage => ImageBytes != null && ImageBytes.Length > 0;
    }
}
