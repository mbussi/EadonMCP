using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for ProposalHandler and ProposalStore.
    ///
    /// ExecuteProposal is tested end-to-end using create_empty operations,
    /// which have no external dependencies (no prefab paths, no RenderSettings).
    /// </summary>
    [TestFixture]
    public class ProposalHandlerTests
    {
        [SetUp]    public void SetUp()    => ProposalStore.Clear();
        [TearDown] public void TearDown()
        {
            ProposalStore.Clear();
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (go.name.StartsWith("ProposalTest"))
                    Object.DestroyImmediate(go);
        }

        // -----------------------------------------------------------------------
        // ProposalStore
        // -----------------------------------------------------------------------

        [Test]
        public void ProposalStore_Add_AssignsUniqueId()
        {
            var p1 = ProposalStore.Add("Summary 1", new ProposalOperationItem[0]);
            var p2 = ProposalStore.Add("Summary 2", new ProposalOperationItem[0]);
            Assert.AreNotEqual(p1.Id, p2.Id);
        }

        [Test]
        public void ProposalStore_GetPending_ReturnsOnlyPendingProposals()
        {
            var p1 = ProposalStore.Add("P1", new ProposalOperationItem[0]);
            var p2 = ProposalStore.Add("P2", new ProposalOperationItem[0]);
            ProposalStore.UpdateStatus(p1.Id, ProposalStatus.Executed);

            var pending = ProposalStore.GetPending();
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(p2.Id, pending[0].Id);
        }

        [Test]
        public void ProposalStore_Clear_RemovesAllProposals()
        {
            ProposalStore.Add("A", new ProposalOperationItem[0]);
            ProposalStore.Add("B", new ProposalOperationItem[0]);
            ProposalStore.Clear();
            Assert.AreEqual(0, ProposalStore.GetPending().Count);
        }

        private static McpResponse Run(System.Threading.Tasks.Task<McpResponse> task, int maxTicks = 20)
        {
            while (!task.IsCompleted && maxTicks-- > 0)
                MainThreadDispatcher.Tick();
            Assert.IsTrue(task.IsCompleted, "Task did not complete within tick budget.");
            return task.Result;
        }

        private static ProposalOperationItem CreateEmptyOp(int id, string name) => new ProposalOperationItem
        {
            Id          = id,
            Tool        = "create_empty",
            Description = $"Create {name}",
            Params      = JObject.FromObject(new { name, dry_run = true }),   // a proposal forces dry_run off
        };

        // -----------------------------------------------------------------------
        // SubmitProposalAsync
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitProposal_ReturnsProposalId_AndAddsToPendingStore()
        {
            var task = ProposalHandler.SubmitProposalAsync(new ProposeChangesRequest
            {
                Summary    = "Test proposal",
                Operations = new[]
                {
                    new ProposalOperationData
                    {
                        Id          = 1,
                        Tool        = "create_empty",
                        Description = "Create a container",
                        Params      = JObject.FromObject(new { name = "ProposalTestContainer" })
                    }
                }
            });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            var data = (ProposalSubmittedData)task.Result.Data;
            Assert.IsNotEmpty(data.ProposalId);
            Assert.AreEqual("pending", data.Status);
            Assert.AreEqual(1, data.OperationCount);
            Assert.AreEqual(1, ProposalStore.GetPending().Count);
        }

        [Test]
        public void SubmitProposal_RefusesToolsUndoCannotReverse_AndListsTheAllowedOnes()
        {
            var response = Run(ProposalHandler.SubmitProposalAsync(new ProposeChangesRequest
            {
                Summary    = "Mixed",
                Operations = new[]
                {
                    new ProposalOperationData { Id = 1, Tool = "create_empty", Params = new JObject { ["name"] = "ProposalTestX" } },
                    new ProposalOperationData { Id = 2, Tool = "write_project_file", Params = new JObject() },
                    new ProposalOperationData { Id = 3, Tool = "teleport_object", Params = new JObject() },
                }
            }));

            Assert.IsFalse(response.Success);
            Assert.AreEqual("INVALID_OPERATION", response.Error.Code);
            StringAssert.Contains("Operation 2: 'write_project_file'", response.Error.Message);
            StringAssert.Contains("Operation 3: 'teleport_object'", response.Error.Message);
            StringAssert.DoesNotContain("Operation 1", response.Error.Message);
            StringAssert.Contains("scatter_prefabs", response.Error.Message);
            Assert.AreEqual(0, ProposalStore.GetPending().Count);
        }

        [Test]
        public void SubmitProposal_ReturnsError_WhenSummaryIsEmpty()
        {
            var task = ProposalHandler.SubmitProposalAsync(new ProposeChangesRequest
                { Summary = "", Operations = new ProposalOperationData[0] });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void SubmitProposal_ReturnsError_WhenOperationsIsEmpty()
        {
            var task = ProposalHandler.SubmitProposalAsync(new ProposeChangesRequest
                { Summary = "Valid summary", Operations = new ProposalOperationData[0] });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // DiscardProposalAsync
        // -----------------------------------------------------------------------

        [Test]
        public void DiscardProposal_UpdatesStatusToDiscarded()
        {
            var proposal = ProposalStore.Add("To discard", new ProposalOperationItem[0]);

            var task = ProposalHandler.DiscardProposalAsync(
                new DiscardProposalRequest { ProposalId = proposal.Id });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(0, ProposalStore.GetPending().Count);
            ProposalStore.TryGet(proposal.Id, out var p);
            Assert.AreEqual(ProposalStatus.Discarded, p.Status);
        }

        [Test]
        public void DiscardProposal_ReturnsError_ForUnknownId()
        {
            var task = ProposalHandler.DiscardProposalAsync(
                new DiscardProposalRequest { ProposalId = "ghost" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PROPOSAL_NOT_FOUND", task.Result.Error.Code);
        }

        [Test]
        public void DiscardProposal_ReturnsError_WhenAlreadyExecuted()
        {
            var proposal = ProposalStore.Add("Already done", new ProposalOperationItem[0]);
            ProposalStore.UpdateStatus(proposal.Id, ProposalStatus.Executed);

            var task = ProposalHandler.DiscardProposalAsync(
                new DiscardProposalRequest { ProposalId = proposal.Id });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PROPOSAL_NOT_PENDING", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // ExecuteProposalAsync — errors
        // -----------------------------------------------------------------------

        [Test]
        public void ExecuteProposal_ReturnsError_ForUnknownId()
        {
            var task = ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = "ghost" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PROPOSAL_NOT_FOUND", task.Result.Error.Code);
        }

        [Test]
        public void ExecuteProposal_ReturnsError_WhenAlreadyDiscarded()
        {
            var proposal = ProposalStore.Add("Already discarded", new ProposalOperationItem[0]);
            ProposalStore.UpdateStatus(proposal.Id, ProposalStatus.Discarded);

            var task = ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = proposal.Id });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PROPOSAL_NOT_PENDING", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // ExecuteProposalAsync — approval, run once, undo, all or nothing
        // -----------------------------------------------------------------------

        [Test]
        public void ExecuteProposal_RefusesAProposalTheUserHasNotApproved()
        {
            var proposal = ProposalStore.Add("Not approved", new[] { CreateEmptyOp(1, "ProposalTestA") });

            var response = Run(ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = proposal.Id }));

            Assert.AreEqual("PROPOSAL_NOT_APPROVED", response.Error.Code);
            StringAssert.Contains("Operation Queue", response.Error.Message);
            Assert.IsNull(GameObject.Find("ProposalTestA"));
            ProposalStore.TryGet(proposal.Id, out var p);
            Assert.AreEqual(ProposalStatus.Pending, p.Status);
        }

        [Test]
        public void ExecuteProposal_RunsAnApprovedProposal_AndMarksItExecuted()
        {
            var proposal = ProposalStore.Add("Create two containers",
                new[] { CreateEmptyOp(1, "ProposalTestA"), CreateEmptyOp(2, "ProposalTestB") });
            Assert.IsTrue(ProposalHandler.Approve(proposal.Id));

            var response = Run(ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = proposal.Id }));

            Assert.IsTrue(response.Success, response.Error?.Message);
            var result = (ExecuteProposalResultData)response.Data;
            Assert.AreEqual(2, result.ExecutedCount);
            Assert.IsNotNull(GameObject.Find("ProposalTestA"));
            Assert.IsNotNull(GameObject.Find("ProposalTestB"));

            ProposalStore.TryGet(proposal.Id, out var p);
            Assert.AreEqual(ProposalStatus.Executed, p.Status);
            Assert.AreEqual("done", p.Results[1].Outcome);
        }

        [Test]
        public void ExecuteFromQueue_NeedsNoSeparateApproval()
        {
            var proposal = ProposalStore.Add("From the window", new[] { CreateEmptyOp(1, "ProposalTestA") });

            var response = Run(ProposalHandler.ExecuteFromQueueAsync(proposal.Id));

            Assert.IsTrue(response.Success, response.Error?.Message);
            Assert.IsNotNull(GameObject.Find("ProposalTestA"));
        }

        [Test]
        public void ExecuteProposal_TwiceBeforeUnityRunsIt_RunsOnce()
        {
            var proposal = ProposalStore.Add("Double click", new[] { CreateEmptyOp(1, "ProposalTestA") });

            var first  = ProposalHandler.ExecuteFromQueueAsync(proposal.Id);
            var second = ProposalHandler.ExecuteFromQueueAsync(proposal.Id);
            var a = Run(first);
            var b = Run(second);

            Assert.IsTrue(a.Success, a.Error?.Message);
            Assert.AreEqual("PROPOSAL_NOT_PENDING", b.Error.Code);

            int count = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (go.name == "ProposalTestA") count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void ExecuteProposal_IsOneUndoStep()
        {
            var proposal = ProposalStore.Add("Undo as one",
                new[] { CreateEmptyOp(1, "ProposalTestA"), CreateEmptyOp(2, "ProposalTestB") });

            Assert.IsTrue(Run(ProposalHandler.ExecuteFromQueueAsync(proposal.Id)).Success);
            Undo.PerformUndo();

            Assert.IsNull(GameObject.Find("ProposalTestA"));
            Assert.IsNull(GameObject.Find("ProposalTestB"));
        }

        [Test]
        public void ExecuteProposal_FailedOperation_UndoesEverythingAndStops()
        {
            var proposal = ProposalStore.Add("Fails in the middle", new[]
            {
                CreateEmptyOp(1, "ProposalTestA"),
                new ProposalOperationItem
                {
                    Id = 2, Tool = "set_transform", Description = "Move a missing object",
                    Params = JObject.FromObject(new { object_path = "/ProposalTestMissing", position = new[] { 1f, 2f, 3f } })
                },
                CreateEmptyOp(3, "ProposalTestC"),
            });

            var response = Run(ProposalHandler.ExecuteFromQueueAsync(proposal.Id));

            Assert.IsFalse(response.Success);
            Assert.AreEqual("PROPOSAL_FAILED", response.Error.Code);
            StringAssert.Contains("Operation 2 (set_transform", response.Error.Message);
            Assert.IsNull(GameObject.Find("ProposalTestA"), "Operation 1 should have been undone.");
            Assert.IsNull(GameObject.Find("ProposalTestC"), "Operation 3 should not have run.");

            ProposalStore.TryGet(proposal.Id, out var p);
            Assert.AreEqual(ProposalStatus.Failed, p.Status);
            Assert.AreEqual("undone",  p.Results[0].Outcome);
            Assert.AreEqual("failed",  p.Results[1].Outcome);
            Assert.AreEqual("not run", p.Results[2].Outcome);
        }

        [Test]
        public void ExecuteProposal_UnknownTool_FailsAndUndoes()
        {
            var proposal = ProposalStore.Add("Bad tool", new[]
            {
                CreateEmptyOp(1, "ProposalTestA"),
                new ProposalOperationItem { Id = 2, Tool = "teleport_object", Params = new JObject() },
            });

            var response = Run(ProposalHandler.ExecuteFromQueueAsync(proposal.Id));

            Assert.AreEqual("PROPOSAL_FAILED", response.Error.Code);
            Assert.IsNull(GameObject.Find("ProposalTestA"));
        }

        // -----------------------------------------------------------------------
        // GetPendingProposalsAsync
        // -----------------------------------------------------------------------

        [Test]
        public void GetPendingProposals_ListsPendingAndApproved()
        {
            ProposalStore.Add("First",  new ProposalOperationItem[0]);
            var second = ProposalStore.Add("Second", new ProposalOperationItem[0]);
            ProposalHandler.Approve(second.Id);
            var third = ProposalStore.Add("Third", new ProposalOperationItem[0]);
            ProposalStore.UpdateStatus(third.Id, ProposalStatus.Discarded);

            var json = JObject.FromObject(Run(ProposalHandler.GetPendingProposalsAsync()).Data);

            Assert.AreEqual(2, ((JArray)json["proposals"]).Count);
            Assert.AreEqual("approved", (string)json["proposals"][1]["status"]);
        }

        [Test]
        public void GetPendingProposals_WithAnId_ReturnsParamsAndResults()
        {
            var proposal = ProposalStore.Add("Detail", new[] { CreateEmptyOp(1, "ProposalTestA") });
            Run(ProposalHandler.ExecuteFromQueueAsync(proposal.Id));

            var json = JObject.FromObject(Run(ProposalHandler.GetPendingProposalsAsync(proposal.Id)).Data);

            Assert.AreEqual("executed", (string)json["status"]);
            Assert.AreEqual("ProposalTestA", (string)json["operations"][0]["params"]["name"]);
            Assert.AreEqual("done", (string)json["results"][0]["outcome"]);
        }
    }
}
