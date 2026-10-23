using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Common interface for AI API clients. Implementations translate the
    /// format-neutral conversation model to the provider's wire format and back.
    ///
    /// Current implementations:
    ///   AnthropicAgentClient   — /v1/messages (Claude models)
    ///   OpenAIAgentClient      — /v1/chat/completions (LM Studio, Ollama, etc.)
    /// </summary>
    public interface IAgentClient
    {
        /// <summary>
        /// Send a conversation turn to the model and return the normalised response.
        /// </summary>
        /// <param name="systemPrompt">System message injected before the conversation.</param>
        /// <param name="history">Full conversation history (user + assistant turns so far).</param>
        /// <param name="tools">Tool schemas available to the model this turn.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<AgentResponse> SendAsync(
            string                   systemPrompt,
            List<ConversationMessage> history,
            List<ToolSchema>         tools,
            CancellationToken        ct = default);
    }
}
