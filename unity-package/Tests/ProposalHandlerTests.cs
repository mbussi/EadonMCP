using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
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
        // ExecuteProposalAsync — end-to-end
        // -----------------------------------------------------------------------

        [Test]
        public void ExecuteProposal_CreatesObjectsAndMarksProposalExecuted()
        {
            // Submit a proposal with two create_empty operations
            var proposal = ProposalStore.Add("Create two containers", new[]
            {
                new ProposalOperationItem
                {
                    Id          = 1,
                    Tool        = "create_empty",
                    Description = "Create container A",
                    Params      = JObject.FromObject(new { name = "ProposalTestA", dry_run = false })
                },
                new ProposalOperationItem
                {
                    Id          = 2,
                    Tool        = "create_empty",
                    Description = "Create container B",
                    Params      = JObject.FromObject(new { name = "ProposalTestB", dry_run = false })
                }
            });

            // Execute and drain the main thread queue (may need multiple ticks
            // since ExecuteProposalAsync awaits each operation individually)
            var task = ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = proposal.Id });

            // Spin until the task completes (each operation enqueues on main thread)
            int maxTicks = 20;
            while (!task.IsCompleted && maxTicks-- > 0)
                MainThreadDispatcher.Tick();

            Assert.IsTrue(task.IsCompleted, "Task did not complete within tick budget.");
            Assert.IsTrue(task.Result.Success);

            var result = (ExecuteProposalResultData)task.Result.Data;
            Assert.AreEqual(2, result.ExecutedCount);
            Assert.AreEqual(0, result.FailedCount);

            // Objects created
            Assert.IsNotNull(GameObject.Find("ProposalTestA"));
            Assert.IsNotNull(GameObject.Find("ProposalTestB"));

            // Proposal marked as executed
            ProposalStore.TryGet(proposal.Id, out var p);
            Assert.AreEqual(ProposalStatus.Executed, p.Status);
        }

        [Test]
        public void ExecuteProposal_ReportsFailure_ForUnknownTool()
        {
            var proposal = ProposalStore.Add("Bad tool", new[]
            {
                new ProposalOperationItem
                {
                    Id     = 1, Tool = "teleport_object",
                    Description = "This tool does not exist",
                    Params = JObject.FromObject(new { })
                }
            });

            var task = ProposalHandler.ExecuteProposalAsync(
                new ExecuteProposalRequest { ProposalId = proposal.Id });

            int maxTicks = 10;
            while (!task.IsCompleted && maxTicks-- > 0)
                MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success); // proposal executed, but operation failed
            var result = (ExecuteProposalResultData)task.Result.Data;
            Assert.AreEqual(0, result.ExecutedCount);
            Assert.AreEqual(1, result.FailedCount);
        }

        // -----------------------------------------------------------------------
        // GetPendingProposalsAsync
        // -----------------------------------------------------------------------

        [Test]
        public void GetPendingProposals_ReturnsAllPendingProposals()
        {
            ProposalStore.Add("First",  new ProposalOperationItem[0]);
            ProposalStore.Add("Second", new ProposalOperationItem[0]);

            var task = ProposalHandler.GetPendingProposalsAsync();
            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
        }
    }
}
