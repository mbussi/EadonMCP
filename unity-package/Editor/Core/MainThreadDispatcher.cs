using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Marshals work from background threads onto the Unity main thread.
    ///
    /// Threading note
    /// --------------
    /// TaskCompletionSource is intentionally created WITHOUT
    /// TaskCreationOptions.RunContinuationsAsynchronously. This means
    /// continuations run synchronously inline when SetResult is called
    /// (i.e. on the main thread, inside Tick). The behaviour this gives:
    ///
    ///   • In tests: Tick() drains the entire async chain in one call —
    ///     no tick loops needed, task.Result never deadlocks.
    ///
    ///   • In production: Tick() completes the full chain synchronously
    ///     before returning to EditorApplication.update. For typical level-
    ///     design operations (instantiate a few prefabs, set transforms)
    ///     this is imperceptible. For large batch operations consider
    ///     splitting across frames in a future phase.
    ///
    ///   • Avoids the Unity SynchronizationContext deadlock that occurs
    ///     with RunContinuationsAsynchronously: continuations would be
    ///     re-queued via UnitySynchronizationContext back to the main
    ///     thread, blocking indefinitely if task.Result is also on the
    ///     main thread.
    ///
    /// Jobs
    /// ----
    /// Work queued while a request is being handled belongs to that request's
    /// job (McpJob.Current). The job turns from queued to running when Unity
    /// starts the work; if the job was cancelled while queued, the work is
    /// skipped and its task fails with OperationCanceledException.
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        static MainThreadDispatcher()
        {
            EditorApplication.update += Tick;
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Drains the pending work queue. Called automatically by EditorApplication.update.
        /// Call directly in tests to process enqueued work synchronously.
        /// </summary>
        public static void Tick()
        {
            while (_queue.TryDequeue(out Action action))
                action?.Invoke();
        }

        /// <summary>
        /// Enqueues a function to run on the main thread and returns a Task
        /// that resolves to its return value.
        /// </summary>
        public static Task<T> EnqueueAsync<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>();
            var job = McpJob.Current;
            _queue.Enqueue(() =>
            {
                if (job != null && !job.TryStart()) { tcs.SetCanceled(); return; }
                try   { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Enqueues a void action to run on the main thread and returns a Task
        /// that completes when the action has finished.
        /// </summary>
        public static Task EnqueueAsync(Action work)
        {
            var tcs = new TaskCompletionSource<bool>();
            var job = McpJob.Current;
            _queue.Enqueue(() =>
            {
                if (job != null && !job.TryStart()) { tcs.SetCanceled(); return; }
                try   { work(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
