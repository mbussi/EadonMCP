using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Calls any OpenAI-compatible chat completions endpoint.
    /// Compatible with LM Studio, Ollama, OpenRouter, Together AI, and any
    /// other provider implementing /v1/chat/completions with tool calling.
    ///
    /// Image tool results (from capture_scene_view) are appended as a separate
    /// user message since OpenAI tool results do not natively support image content.
    /// </summary>
    public class OpenAIAgentClient : IAgentClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

        private readonly AgentSettings settings;

        public OpenAIAgentClient(AgentSettings settings)
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

            // Build messages — OpenAI format
            var messages = new JArray();
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

            foreach (var msg in history)
                foreach (var m in ToOpenAIMessages(msg))
                    messages.Add(m);

            // Build tools — OpenAI wraps each in {"type":"function","function":{...}}
            var toolsArray = new JArray();
            foreach (var t in tools)
            {
                toolsArray.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"]        = t.Name,
                        ["description"] = t.Description,
                        ["parameters"]  = t.InputSchema
                    }
                });
            }

            var body = new JObject
            {
                ["model"]       = settings.modelName,
                ["max_tokens"]  = settings.maxTokens,
                ["temperature"] = settings.temperature,
                ["messages"]    = messages,
                ["tools"]       = toolsArray,
                ["tool_choice"] = "auto"
            };

            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{settings.baseUrl.TrimEnd('/')}/v1/chat/completions");

                if (!string.IsNullOrEmpty(key))
                    request.Headers.Add("Authorization", $"Bearer {key}");

                request.Content = new StringContent(
                    body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                var response = await Http.SendAsync(request, ct);
                var json     = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Error($"API error {(int)response.StatusCode}: {json}");

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

        private static IEnumerable<JObject> ToOpenAIMessages(ConversationMessage msg)
        {
            switch (msg.Role)
            {
                case MessageRole.User:
                    yield return new JObject
                    {
                        ["role"]    = "user",
                        ["content"] = msg.Text ?? string.Empty
                    };
                    break;

                case MessageRole.Assistant:
                {
                    var assistantMsg = new JObject { ["role"] = "assistant" };

                    if (!string.IsNullOrEmpty(msg.Text))
                        assistantMsg["content"] = msg.Text;

                    if (msg.ToolUses?.Count > 0)
                    {
                        var calls = new JArray();
                        foreach (var tu in msg.ToolUses)
                            calls.Add(new JObject
                            {
                                ["id"]   = tu.Id,
                                ["type"] = "function",
                                ["function"] = new JObject
                                {
                                    ["name"]      = tu.Name,
                                    ["arguments"] = tu.Input?.ToString(Formatting.None) ?? "{}"
                                }
                            });
                        assistantMsg["tool_calls"] = calls;
                    }

                    yield return assistantMsg;
                    break;
                }

                case MessageRole.ToolResult:
                {
                    if (msg.ToolResults == null) break;

                    foreach (var tr in msg.ToolResults)
                    {
                        // Primary tool result message
                        yield return new JObject
                        {
                            ["role"]         = "tool",
                            ["tool_call_id"] = tr.ToolUseId,
                            ["content"]      = tr.TextContent ?? string.Empty
                        };

                        // OpenAI tool results don't natively carry images.
                        // Append the image as a follow-up user message instead.
                        if (tr.HasImage)
                        {
                            yield return new JObject
                            {
                                ["role"] = "user",
                                ["content"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["type"] = "image_url",
                                        ["image_url"] = new JObject
                                        {
                                            ["url"] = $"data:{tr.ImageMime};base64," +
                                                      Convert.ToBase64String(tr.ImageBytes)
                                        }
                                    },
                                    new JObject
                                    {
                                        ["type"] = "text",
                                        ["text"] = "This is the scene view captured by capture_scene_view."
                                    }
                                }
                            };
                        }
                    }
                    break;
                }
            }
        }

        // -----------------------------------------------------------------------
        // Response parsing
        // -----------------------------------------------------------------------

        private static AgentResponse ParseResponse(string json)
        {
            try
            {
                var obj          = JObject.Parse(json);
                var choice       = obj["choices"]?[0];
                var finishReason = choice?["finish_reason"]?.Value<string>() ?? "stop";
                var message      = choice?["message"];

                var text     = message?["content"]?.Value<string>() ?? string.Empty;
                var toolUses = new List<ToolUse>();

                var toolCalls = message?["tool_calls"] as JArray;
                if (toolCalls != null)
                    foreach (var tc in toolCalls)
                    {
                        JObject input;
                        try   { input = JObject.Parse(tc["function"]?["arguments"]?.Value<string>() ?? "{}"); }
                        catch { input = new JObject(); }

                        toolUses.Add(new ToolUse
                        {
                            Id    = tc["id"]?.Value<string>(),
                            Name  = tc["function"]?["name"]?.Value<string>(),
                            Input = input
                        });
                    }

                return new AgentResponse
                {
                    Text       = text,
                    StopReason = toolUses.Count > 0 ? "tool_use" : "end_turn",
                    ToolUses   = toolUses
                };
            }
            catch (Exception ex)
            {
                return Error($"Failed to parse response: {ex.Message}\n{json}");
            }
        }

        private static AgentResponse Error(string message) => new()
        {
            StopReason = "error",
            Error      = message
        };
    }
}
