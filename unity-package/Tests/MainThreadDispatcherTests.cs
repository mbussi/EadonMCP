using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NUnit.Framework;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for MainThreadDispatcher.
    ///
    /// These tests run on the Unity main thread, so Tick() is called manually
    /// rather than waiting for EditorApplication.update. This gives deterministic,
    /// fast tests without depending on the editor update loop frequency.
    /// </summary>
    [TestFixture]
    public class MainThreadDispatcherTests
    {
        // -----------------------------------------------------------------------
        // EnqueueAsync<T> — basic behaviour
        // -----------------------------------------------------------------------

        [Test]
        public void EnqueueAsync_WorkIsNotExecutedBeforeTick()
        {
            bool executed = false;
            MainThreadDispatcher.EnqueueAsync(() => { executed = true; return true; });

            Assert.IsFalse(executed, "Work must not run until Tick is called.");
        }

        [Test]
        public void EnqueueAsync_WorkIsExecutedOnTick()
        {
            bool executed = false;
            MainThreadDispatcher.EnqueueAsync(() => { executed = true; return true; });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(executed);
        }

        [Test]
        public void EnqueueAsync_ReturnsCorrectValue()
        {
            Task<int> task = MainThreadDispatcher.EnqueueAsync(() => 42);
            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, task.Result);
        }

        [Test]
        public void EnqueueAsync_MultipleItemsAreProcessedInFifoOrder()
        {
            var results = new List<int>();
            MainThreadDispatcher.EnqueueAsync(() => { results.Add(1); return true; });
            MainThreadDispatcher.EnqueueAsync(() => { results.Add(2); return true; });
            MainThreadDispatcher.EnqueueAsync(() => { results.Add(3); return true; });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(new[] { 1, 2, 3 }, results);
        }

        [Test]
        public void EnqueueAsync_SecondTickDoesNotReplayAlreadyExecutedWork()
        {
            int callCount = 0;
            MainThreadDispatcher.EnqueueAsync(() => { callCount++; return true; });

            MainThreadDispatcher.Tick();
            MainThreadDispatcher.Tick(); // second tick — nothing should happen

            Assert.AreEqual(1, callCount);
        }

        // -----------------------------------------------------------------------
        // EnqueueAsync<T> — exception handling
        // -----------------------------------------------------------------------

        [Test]
        public void EnqueueAsync_ExceptionIsCapturedInTask_NotThrownOnMainThread()
        {
            Task<int> task = MainThreadDispatcher.EnqueueAsync<int>(() =>
                throw new InvalidOperationException("intentional test error"));

            // Tick must not throw even though the work throws.
            Assert.DoesNotThrow(() => MainThreadDispatcher.Tick());

            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(task.Exception?.InnerException);
        }

        [Test]
        public void EnqueueAsync_ExceptionMessageIsPreserved()
        {
            Task<int> task = MainThreadDispatcher.EnqueueAsync<int>(() =>
                throw new InvalidOperationException("sentinel message"));

            MainThreadDispatcher.Tick();

            StringAssert.Contains("sentinel message", task.Exception?.InnerException?.Message);
        }

        // -----------------------------------------------------------------------
        // EnqueueAsync (non-generic void overload)
        // -----------------------------------------------------------------------

        [Test]
        public void EnqueueAsync_VoidOverload_ExecutesAction()
        {
            bool executed = false;
            Task task = MainThreadDispatcher.EnqueueAsync(() => { executed = true; });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(executed);
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void EnqueueAsync_VoidOverload_ExceptionIsCapturedInTask()
        {
            Task task = MainThreadDispatcher.EnqueueAsync(() =>
                throw new InvalidOperationException("void action error"));

            Assert.DoesNotThrow(() => MainThreadDispatcher.Tick());

            Assert.IsTrue(task.IsFaulted);
        }

        // -----------------------------------------------------------------------
        // Thread safety
        // -----------------------------------------------------------------------

        [Test]
        public void EnqueueAsync_CanBeCalledSafelyFromBackgroundThread()
        {
            Task<int> task = null;

            var thread = new Thread(() =>
            {
                task = MainThreadDispatcher.EnqueueAsync(() => 99);
            });
            thread.Start();
            thread.Join(timeout: TimeSpan.FromSeconds(2));

            MainThreadDispatcher.Tick();

            Assert.IsNotNull(task, "Background thread should have enqueued work.");
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(99, task.Result);
        }

        [Test]
        public void EnqueueAsync_ConcurrentEnqueuesFromMultipleThreads_AllExecute()
        {
            const int threadCount = 8;
            var tasks  = new Task<int>[threadCount];
            var threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int captured = i;
                threads[i] = new Thread(() =>
                {
                    tasks[captured] = MainThreadDispatcher.EnqueueAsync(() => captured);
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join(TimeSpan.FromSeconds(2));

            MainThreadDispatcher.Tick();

            for (int i = 0; i < threadCount; i++)
            {
                Assert.IsNotNull(tasks[i], $"Task {i} was not enqueued.");
                Assert.IsTrue(tasks[i].IsCompleted, $"Task {i} did not complete.");
            }
        }
    }
}
