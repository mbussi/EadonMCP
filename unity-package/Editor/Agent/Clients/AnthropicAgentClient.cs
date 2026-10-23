using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Calls the Anthropic Messages API (api.anthropic.com/v1/messages).
    /// Handles tool_use stop reason and formats tool_result messages including
    /// inline image data for capture_scene_view responses.
    /// </summary>
    public class AnthropicAgentClient : IAgentClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

        private readonly AgentSettings settings;

        public AnthropicAgentClient(AgentSettings settings)
        {
            this.settings = settings;
        }

        public async Task<AgentResponse> SendAsync(
            string systemPrompt,
            List<ConversationMessage> history,
            List<ToolSchema> tools,
            CancellationToken ct = default)
        {
            var key = settings.ResolveApiKey();
            if (string.IsNullOrEmpty(key))
                return Error("No API key configured. Set one in AgentSettings or via the " +
                             "ANTHROPIC_API_KEY environment variable.");

            // Build messages array
            var messages = new JArray();
            foreach (var msg in history)
                messages.Add(ToAnthropicMessage(msg));

            // Build tools array
            var toolsArray = new JArray();
            foreach (var t in tools)
            {
                toolsArray.Add(new JObject
                {
                    ["name"]         = t.Name,
                    ["description"]  = t.Description,
                    ["input_schema"] = t.InputSchema
                });
            }

            var body = new JObject
            {
                ["model"]       = settings.modelName,
                ["max_tokens"]  = settings.maxTokens,
                ["temperature"] = settings.temperature,
                ["system"]      = systemPrompt,
                ["messages"]    = messages,
                ["tools"]       = toolsArray
            };

            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{settings.baseUrl.TrimEnd('/')}/v1/messages");

                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = new StringContent(
                    body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                var response = await Http.SendAsync(request, ct);
                var json     = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Error($"Anthropic API error {(int)response.StatusCode}: {json}");

                return ParseResponse(json);
            }
            catch (TaskCanceledException)
            {
                return Error("Request timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                return Error($"Request failed: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Request serialisation
        // -----------------------------------------------------------------------

        private static JObject ToAnthropicMessage(ConversationMessage msg)
        {
            switch (msg.Role)
            {
                case MessageRole.User:
                    return new JObject
                    {
                        ["role"]    = "user",
                        ["content"] = msg.Text ?? string.Empty
                    };

                case MessageRole.Assistant:
                {
                    var content = new JArray();
                    if (!string.IsNullOrEmpty(msg.Text))
                        content.Add(new JObject { ["type"] = "text", ["text"] = msg.Text });

                    if (msg.ToolUses != null)
                        foreach (var tu in msg.ToolUses)
                            content.Add(new JObject
                            {
                                ["type"]  = "tool_use",
                                ["id"]    = tu.Id,
                                ["name"]  = tu.Name,
                                ["input"] = tu.Input ?? new JObject()
                            });

                    return new JObject { ["role"] = "assistant", ["content"] = content };
                }

                case MessageRole.ToolResult:
                {
                    var content = new JArray();
                    if (msg.ToolResults != null)
                        foreach (var tr in msg.ToolResults)
                        {
                            var resultContent = new JArray();

                            if (tr.HasImage)
                            {
                                // Send image directly so Claude can see the screenshot
                                resultContent.Add(new JObject
                                {
                                    ["type"] = "image",
                                    ["source"] = new JObject
                                    {
                                        ["type"]       = "base64",
                                        ["media_type"] = tr.ImageMime,
                                        ["data"]       = Convert.ToBase64String(tr.ImageBytes)
                                    }
                                });
                            }

                            // The text too (with an image: its warnings, without the base64).
                            if (!string.IsNullOrEmpty(tr.TextContent))
                            {
                                resultContent.Add(new JObject
                                {
                                    ["type"] = "text",
                                    ["text"] = tr.TextContent
                                });
                            }

                            if (resultContent.Count == 0)
                                resultContent.Add(new JObject { ["type"] = "text", ["text"] = "(no output)" });

                            content.Add(new JObject
                            {
                                ["type"]       = "tool_result",
                                ["tool_use_id"] = tr.ToolUseId,
                                ["content"]    = resultContent
                            });
                        }

                    return new JObject { ["role"] = "user", ["content"] = content };
                }

                default:
                    return new JObject { ["role"] = "user", ["content"] = string.Empty };
            }
        }

        // -----------------------------------------------------------------------
        // Response parsing
        // -----------------------------------------------------------------------

        private static AgentResponse ParseResponse(string json)
        {
            try
            {
                var obj        = JObject.Parse(json);
                var stopReason = obj["stop_reason"]?.Value<string>() ?? "end_turn";
                var content    = obj["content"] as JArray ?? new JArray();

                var text     = string.Empty;
                var toolUses = new List<ToolUse>();

                foreach (var block in content)
                {
                    var type = block["type"]?.Value<string>();
                    if (type == "text")
                        text += block["text"]?.Value<string>();
                    else if (type == "tool_use")
                        toolUses.Add(new ToolUse
                        {
                            Id    = block["id"]?.Value<string>(),
                            Name  = block["name"]?.Value<string>(),
                            Input = block["input"] as JObject ?? new JObject()
                        });
                }

                return new AgentResponse
                {
                    Text       = text,
                    StopReason = NormaliseStopReason(stopReason, toolUses.Count > 0),
                    ToolUses   = toolUses
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to parse response: {ex.Message}\n{json}");
            }
        }

        /// <summary>
        /// Maps Anthropic's stop_reason to the loop's: a reply cut off at max_tokens stays
        /// "max_tokens" even when it contains a (possibly incomplete) tool call, so the
        /// call is never run.
        /// </summary>
        internal static string NormaliseStopReason(string stopReason, bool hasToolCalls) => stopReason switch
        {
            "max_tokens"                    => "max_tokens",
            "model_context_window_exceeded" => "max_tokens",
            "tool_use"                      => hasToolCalls ? "tool_use" : "end_turn",
            "end_turn" or "stop_sequence"   => hasToolCalls ? "tool_use" : "end_turn",
            "refusal"                       => "refusal",
            null or ""                      => hasToolCalls ? "tool_use" : "end_turn",
            _                               => stopReason,
        };

        private static AgentResponse Error(string message) => new()
        {
            StopReason = "error",
            Error      = message
        };
    }
}
