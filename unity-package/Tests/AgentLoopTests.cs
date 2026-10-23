using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcpBridge.Editor.Agent;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for the in-editor agent loop, with a fake model and a fake tool
    /// runner: cut-off replies, stop reasons, the turn cap, empty replies, Stop, the
    /// conversation repair after a script reload, and screenshot trimming.
    /// </summary>
    [TestFixture]
    public class AgentLoopTests
    {
        /// <summary>Returns the scripted replies in order and records what it was sent.</summary>
        private sealed class FakeModel : IAgentClient
        {
            private readonly Queue<AgentResponse> _replies;
            public readonly List<int> HistorySizes = new List<int>();
            public System.Action OnSend;

            public FakeModel(params AgentResponse[] replies) => _replies = new Queue<AgentResponse>(replies);

            public Task<AgentResponse> SendAsync(string systemPrompt, List<ConversationMessage> history,
                List<ToolSchema> tools, CancellationToken ct = default)
            {
                OnSend?.Invoke();
                HistorySizes.Add(history.Count);
                return Task.FromResult(_replies.Count > 0
                    ? _replies.Dequeue()
                    : new AgentResponse { StopReason = "end_turn", Text = "done" });
            }
        }

        private AgentSettings _settings;
        private List<string> _ran;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<AgentSettings>();
            _settings.maxToolRounds = 25;
            _ran = new List<string>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        private AgentLoop Loop(FakeModel model)
        {
            var loop = new AgentLoop(_settings, model);
            loop.ToolExecutor = (tu, ct) =>
            {
                _ran.Add(tu.Name);
                return Task.FromResult(new ToolExecutionResult { TextContent = "{\"success\":true}" });
            };
            return loop;
        }

        private static AgentResponse ToolCall(string id, string name = "get_scene_hierarchy") => new AgentResponse
        {
            StopReason = "tool_use",
            ToolUses   = new List<ToolUse> { new ToolUse { Id = id, Name = name, Input = new JObject() } },
        };

        private static AgentTurnResult Run(AgentLoop loop, string text = "hello", CancellationToken ct = default) =>
            loop.RunTurnAsync(text, null, ct).GetAwaiter().GetResult();

        // -----------------------------------------------------------------------
        // Stop reasons
        // -----------------------------------------------------------------------

        [Test]
        public void CutOffReply_DoesNotRunItsToolCall()
        {
            var loop = Loop(new FakeModel(new AgentResponse
            {
                StopReason = "max_tokens",
                Text       = "Placing the stalls",
                ToolUses   = new List<ToolUse> { new ToolUse { Id = "t1", Name = "get_scene_hierarchy", Input = new JObject() } },
            }));

            var result = Run(loop);

            Assert.IsEmpty(_ran);
            StringAssert.Contains("cut off", result.Notice);
            Assert.AreEqual(MessageRole.Assistant, loop.History[^1].Role);
            Assert.IsNull(loop.History[^1].ToolUses);
        }

        [Test]
        public void Anthropic_MaxTokensWithToolCall_StaysMaxTokens()
        {
            Assert.AreEqual("max_tokens", AnthropicAgentClient.NormaliseStopReason("max_tokens", hasToolCalls: true));
            Assert.AreEqual("tool_use",   AnthropicAgentClient.NormaliseStopReason("tool_use",   hasToolCalls: true));
            Assert.AreEqual("refusal",    AnthropicAgentClient.NormaliseStopReason("refusal",    hasToolCalls: false));
        }

        [Test]
        public void OpenAI_LengthIsCutOff_AndStopWithToolCallsIsToolUse()
        {
            Assert.AreEqual("max_tokens", OpenAIAgentClient.NormaliseStopReason("length", hasToolCalls: true));
            Assert.AreEqual("tool_use",   OpenAIAgentClient.NormaliseStopReason("stop",   hasToolCalls: true));
            Assert.AreEqual("end_turn",   OpenAIAgentClient.NormaliseStopReason("stop",   hasToolCalls: false));
        }

        [Test]
        public void UnknownStopReason_EndsTheTurnInsteadOfLooping()
        {
            var model = new FakeModel(new AgentResponse { StopReason = "pause_turn", Text = "partial" });
            var result = Run(Loop(model));

            Assert.AreEqual(1, model.HistorySizes.Count);
            StringAssert.Contains("pause_turn", result.Notice);
        }

        [Test]
        public void EmptyReply_IsNotStored()
        {
            var loop   = Loop(new FakeModel(new AgentResponse { StopReason = "end_turn", Text = "" }));
            var result = Run(loop);

            Assert.AreEqual("(no reply)", result.Notice);
            Assert.AreEqual(1, loop.History.Count, "only the user message");
        }

        // -----------------------------------------------------------------------
        // Turn cap and Stop
        // -----------------------------------------------------------------------

        [Test]
        public void TurnCap_StopsAfterMaxToolRounds()
        {
            _settings.maxToolRounds = 3;
            var model = new FakeModel(ToolCall("a"), ToolCall("b"), ToolCall("c"), ToolCall("d"), ToolCall("e"));
            var loop  = Loop(model);

            var result = Run(loop);

            Assert.AreEqual(3, _ran.Count);
            StringAssert.Contains("Max Tool Rounds", result.Notice);
            Assert.AreEqual(MessageRole.ToolResult, loop.History[^1].Role, "the conversation ends valid");
        }

        [Test]
        public void Stop_DuringToolCalls_LeavesEveryCallAnswered()
        {
            var cts   = new CancellationTokenSource();
            var model = new FakeModel(new AgentResponse
            {
                StopReason = "tool_use",
                ToolUses   = new List<ToolUse>
                {
                    new ToolUse { Id = "t1", Name = "get_scene_hierarchy", Input = new JObject() },
                    new ToolUse { Id = "t2", Name = "get_scene_context",   Input = new JObject() },
                },
            });
            var loop = Loop(model);
            loop.ToolExecutor = (tu, ct) =>
            {
                _ran.Add(tu.Name);
                cts.Cancel();   // the user presses Stop while the first call runs
                return Task.FromResult(new ToolExecutionResult { TextContent = "{}" });
            };

            Assert.Throws<System.OperationCanceledException>(() => Run(loop, ct: cts.Token));

            Assert.AreEqual(1, _ran.Count);
            var results = loop.History[^1].ToolResults;
            Assert.AreEqual(2, results.Count);
            StringAssert.Contains("Cancelled by the user", results[1].TextContent);
        }

        // -----------------------------------------------------------------------
        // Repair and reload
        // -----------------------------------------------------------------------

        [Test]
        public void Repair_GivesUnansweredToolCallsTheInterruptedResult()
        {
            var history = new List<ConversationMessage>
            {
                new ConversationMessage { Role = MessageRole.User, Text = "write a script" },
                new ConversationMessage
                {
                    Role     = MessageRole.Assistant,
                    ToolUses = new List<ToolUse>
                    {
                        new ToolUse { Id = "w1", Name = "write_project_file", Input = new JObject() },
                        new ToolUse { Id = "r1", Name = "refresh_assets",     Input = new JObject() },
                    },
                },
            };

            AgentLoop.RepairHistory(history);

            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(MessageRole.ToolResult, history[2].Role);
            Assert.AreEqual(2, history[2].ToolResults.Count);
            StringAssert.Contains("reloaded scripts", history[2].ToolResults[1].TextContent);
        }

        [Test]
        public void Repair_DropsEmptyMessagesAndOrphanResults()
        {
            var history = new List<ConversationMessage>
            {
                new ConversationMessage { Role = MessageRole.User, Text = "hi" },
                new ConversationMessage { Role = MessageRole.Assistant, Text = "" },
                new ConversationMessage
                {
                    Role        = MessageRole.ToolResult,
                    ToolResults = new List<ToolResult> { new ToolResult { ToolUseId = "x", TextContent = "{}" } },
                },
                new ConversationMessage { Role = MessageRole.Assistant, Text = "hello" },
            };

            AgentLoop.RepairHistory(history);

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("hello", history[1].Text);
        }

        [Test]
        public void RestoredHistory_IsRepaired()
        {
            var before = Loop(new FakeModel());
            before.History.Add(new ConversationMessage { Role = MessageRole.User, Text = "go" });
            before.History.Add(new ConversationMessage
            {
                Role     = MessageRole.Assistant,
                ToolUses = new List<ToolUse> { new ToolUse { Id = "r1", Name = "refresh_assets", Input = new JObject() } },
            });
            var json = before.GetHistoryJson();

            var after = Loop(new FakeModel());
            after.RestoreHistoryJson(json);

            Assert.AreEqual(MessageRole.ToolResult, after.History[^1].Role);
        }

        // -----------------------------------------------------------------------
        // Screenshots
        // -----------------------------------------------------------------------

        [Test]
        public void OnlyTheLatestScreenshotIsKept()
        {
            var history = new List<ConversationMessage>
            {
                ImageResult("i1"), ImageResult("i2"), ImageResult("i3"),
            };

            AgentLoop.KeepOnlyLatestImage(history);

            Assert.IsFalse(history[0].ToolResults[0].HasImage);
            Assert.IsFalse(history[1].ToolResults[0].HasImage);
            Assert.IsTrue(history[2].ToolResults[0].HasImage);
            StringAssert.Contains("image removed", history[0].ToolResults[0].TextContent);
        }

        [Test]
        public void SavedHistory_KeepsNoImages()
        {
            var loop = Loop(new FakeModel());
            loop.History.Add(ImageResult("i1"));

            StringAssert.DoesNotContain("AQID", loop.GetHistoryJson());   // base64 of {1,2,3}
            Assert.IsTrue(loop.History[0].ToolResults[0].HasImage, "the live conversation keeps it");
        }

        private static ConversationMessage ImageResult(string id) => new ConversationMessage
        {
            Role        = MessageRole.ToolResult,
            ToolResults = new List<ToolResult>
            {
                new ToolResult { ToolUseId = id, TextContent = "{\"format\":\"jpeg\"}", ImageBytes = new byte[] { 1, 2, 3 } },
            },
        };
    }
}
