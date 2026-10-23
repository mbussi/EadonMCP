using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for jobs: how queued main-thread work follows its job, cancelling,
    /// the job store, and the pending envelope.
    /// </summary>
    [TestFixture]
    public class McpJobTests
    {
        [SetUp]
        public void SetUp()
        {
            McpJobStore.Clear();
            McpJob.Current = null;
            MainThreadDispatcher.Tick();
        }

        [TearDown]
        public void TearDown()
        {
            McpJob.Current = null;
            MainThreadDispatcher.Tick();
            McpJobStore.Clear();
        }

        private static Task<McpResponse> EnqueueFor(McpJob job, System.Func<McpResponse> work)
        {
            McpJob.Current = job;
            var task = MainThreadDispatcher.EnqueueAsync(work);
            McpJob.Current = null;
            return task;
        }

        // -----------------------------------------------------------------------
        // Dispatcher and job state
        // -----------------------------------------------------------------------

        [Test]
        public void QueuedWork_StartsItsJob()
        {
            var job  = new McpJob("POST", "/test");
            McpJobState stateDuringWork = McpJobState.Queued;
            var task = EnqueueFor(job, () => { stateDuringWork = job.State; return McpResponse.Ok(null); });

            Assert.AreEqual(McpJobState.Queued, job.State);
            MainThreadDispatcher.Tick();

            Assert.AreEqual(McpJobState.Running, stateDuringWork);
            Assert.IsTrue(task.Result.Success);
        }

        [Test]
        public void CancelledWhileQueued_WorkIsSkipped()
        {
            var job = new McpJob("POST", "/test");
            McpJobStore.Add(job);
            bool ran = false;
            var task = EnqueueFor(job, () => { ran = true; return McpResponse.Ok(null); });

            var cancel = McpJobStore.Cancel(job.Id);
            MainThreadDispatcher.Tick();

            Assert.IsTrue(cancel.Success);
            Assert.IsFalse(ran);
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(McpJobState.Cancelled, job.State);
            Assert.AreEqual("CANCELLED", job.Result.Error.Code);
        }

        [Test]
        public void RunningJob_WithoutCancelSupport_RefusesCancel()
        {
            var job = new McpJob("POST", "/test");
            job.TryStart();
            McpJobStore.Add(job);

            var response = McpJobStore.Cancel(job.Id);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("CANNOT_CANCEL", response.Error.Code);
            Assert.AreEqual(McpJobState.Running, job.State);
        }

        [Test]
        public void RunningJob_WithCancelSupport_IsCancelledOnTheMainThread()
        {
            var job = new McpJob("POST", "/test");
            job.TryStart();
            bool stopped = false;
            job.AllowCancelWhileRunning(() => stopped = true);
            McpJobStore.Add(job);

            var response = McpJobStore.Cancel(job.Id);
            Assert.IsTrue(response.Success);
            MainThreadDispatcher.Tick();

            Assert.IsTrue(stopped);
            Assert.AreEqual(McpJobState.Cancelled, job.State);
        }

        [Test]
        public void FinishedJob_RefusesCancel()
        {
            var job = new McpJob("POST", "/test");
            job.Complete(McpResponse.Ok(null));
            McpJobStore.Add(job);

            Assert.AreEqual("ALREADY_FINISHED", McpJobStore.Cancel(job.Id).Error.Code);
        }

        [Test]
        public void Complete_WithFailure_MarksTheJobFailed()
        {
            var job = new McpJob("POST", "/test");
            job.Complete(McpResponse.Fail("PREFAB_NOT_FOUND", "No prefab."));

            Assert.AreEqual(McpJobState.Failed, job.State);
            var json = job.ToJson(includeResult: true);
            Assert.AreEqual("failed", (string)json["state"]);
            Assert.AreEqual("PREFAB_NOT_FOUND", (string)json["result"]["error"]["code"]);
        }

        [Test]
        public void Complete_AfterCancel_KeepsCancelled()
        {
            var job = new McpJob("POST", "/test");
            McpJobStore.Add(job);
            McpJobStore.Cancel(job.Id);
            job.Complete(McpResponse.Ok(null));

            Assert.AreEqual(McpJobState.Cancelled, job.State);
        }

        // -----------------------------------------------------------------------
        // Undo steps
        // -----------------------------------------------------------------------

        private static McpResponse CreateUndoable(string name, System.Action<GameObject> keep)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP Test Create " + name);
            keep(go);
            return McpResponse.Ok(null);
        }

        [Test]
        public void EachJob_IsItsOwnUndoStep()
        {
            GameObject a = null, b = null;
            try
            {
                Undo.IncrementCurrentGroup();
                EnqueueFor(new McpJob("POST", "/test/a"), () => CreateUndoable("McpUndoTest_A", g => a = g));
                EnqueueFor(new McpJob("POST", "/test/b"), () => CreateUndoable("McpUndoTest_B", g => b = g));
                MainThreadDispatcher.Tick();

                Undo.PerformUndo();

                Assert.IsTrue(b == null, "One undo should remove the second request's object.");
                Assert.IsTrue(a != null, "One undo should leave the first request's object.");
            }
            finally
            {
                if (a != null) Object.DestroyImmediate(a);
                if (b != null) Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void OneJob_WithTwoPiecesOfWork_IsOneUndoStep()
        {
            GameObject a = null, b = null;
            try
            {
                Undo.IncrementCurrentGroup();
                var job = new McpJob("POST", "/test/ab");
                EnqueueFor(job, () => CreateUndoable("McpUndoTest_A", g => a = g));
                EnqueueFor(job, () => CreateUndoable("McpUndoTest_B", g => b = g));
                MainThreadDispatcher.Tick();

                Undo.PerformUndo();

                Assert.IsTrue(a == null && b == null, "One undo should remove everything the request made.");
            }
            finally
            {
                if (a != null) Object.DestroyImmediate(a);
                if (b != null) Object.DestroyImmediate(b);
            }
        }

        // -----------------------------------------------------------------------
        // Store and routes
        // -----------------------------------------------------------------------

        [Test]
        public void Store_KeepsTheLastFiftyAndNeverDropsAnUnfinishedJob()
        {
            var unfinished = new McpJob("POST", "/slow");
            unfinished.TryStart();
            McpJobStore.Add(unfinished);

            for (int i = 0; i < McpJobStore.Capacity + 5; i++)
            {
                var job = new McpJob("POST", "/test");
                job.Complete(McpResponse.Ok(null));
                McpJobStore.Add(job);
            }

            Assert.AreEqual(McpJobStore.Capacity, McpJobStore.GetAll().Count);
            Assert.IsTrue(McpJobStore.TryGet(unfinished.Id, out _));
        }

        [Test]
        public void GetStatus_UnknownJob_ExplainsWhy()
        {
            var response = McpJobStore.GetStatus("job-0-none");
            Assert.AreEqual("JOB_NOT_FOUND", response.Error.Code);
            StringAssert.Contains("reload", response.Error.Message);
        }

        [Test]
        public void GetStatus_WithoutId_ListsJobsNewestFirst()
        {
            var first  = new McpJob("POST", "/a");
            var second = new McpJob("POST", "/b");
            McpJobStore.Add(first);
            McpJobStore.Add(second);

            var json = JObject.FromObject(McpJobStore.GetStatus(null).Data);
            Assert.AreEqual(2, (int)json["count"]);
            Assert.AreEqual(second.Id, (string)json["jobs"][0]["job_id"]);
        }

        [Test]
        public void JobPending_SerializesThePendingFlag_AndOkDoesNot()
        {
            var job     = new McpJob("POST", "/test");
            var pending = JObject.Parse(JsonConvert.SerializeObject(McpResponse.JobPending(job.ToJson(false))));
            var ok      = JObject.Parse(JsonConvert.SerializeObject(McpResponse.Ok(null)));

            Assert.IsTrue((bool)pending["pending"]);
            Assert.AreEqual(job.Id, (string)pending["data"]["job_id"]);
            Assert.IsNull(ok["pending"]);
        }

        [Test]
        public void Catalog_FindsTheToolOfARoute()
        {
            Assert.IsTrue(McpToolCatalog.TryGetByRoute("POST", "/tools/world/bake_lighting", out var tool));
            Assert.AreEqual("bake_lighting", tool.Name);
            Assert.IsTrue(McpToolCatalog.TryGetByRoute("GET", "/jobs", out var status));
            Assert.AreEqual("get_job_status", status.Name);
        }
    }
}
