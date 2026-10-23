using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>The life of a job. Queued and Running are unfinished; the others are final.</summary>
    public enum McpJobState
    {
        Queued,
        Running,
        Done,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// One bridge request, followed from arrival to result.
    ///
    /// The listener creates a job for every request and makes it the current job
    /// (McpJob.Current) while the request is routed. MainThreadDispatcher reads the
    /// current job when work is queued, so the job knows when Unity starts on it and
    /// queued work can be cancelled. A request that is still unfinished after
    /// McpHttpListener.HandoffSeconds is handed off: the job goes into McpJobStore and
    /// the caller gets its ID to poll with GET /jobs.
    ///
    /// Background operations (a light bake) create a job with StartBackground and
    /// complete it themselves.
    ///
    /// Thread safety: state is read from the listener's threads and written from the
    /// main thread, so every access takes the lock.
    /// </summary>
    public sealed class McpJob
    {
        private static readonly AsyncLocal<McpJob> CurrentJob = new AsyncLocal<McpJob>();
        private static int _counter;

        private readonly object _gate = new object();
        private McpJobState _state = McpJobState.Queued;
        private DateTime    _startedUtc;
        private DateTime    _finishedUtc;
        private McpResponse _result;
        private float       _progress = -1f;
        private string      _progressMessage;
        private string      _tool;
        private Func<bool>  _cancelRunning;
        private bool        _undoStepStarted;

        public McpJob(string method, string path, string tool = null)
        {
            Id         = $"job-{Interlocked.Increment(ref _counter)}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            Method     = method;
            Path       = path;
            _tool      = tool;
            CreatedUtc = DateTime.UtcNow;
        }

        /// <summary>The job of the request being handled on this logical thread, or null.</summary>
        public static McpJob Current
        {
            get => CurrentJob.Value;
            set => CurrentJob.Value = value;
        }

        public string   Id         { get; }
        public string   Method     { get; }
        public string   Path       { get; }
        public DateTime CreatedUtc { get; }

        public string Tool
        {
            get { lock (_gate) return _tool; }
            set { lock (_gate) _tool = value; }
        }

        public McpJobState State
        {
            get { lock (_gate) return _state; }
        }

        public bool IsFinished
        {
            get { lock (_gate) return _state >= McpJobState.Done; }
        }

        /// <summary>The final response, once the job has finished.</summary>
        public McpResponse Result
        {
            get { lock (_gate) return _result; }
        }

        // -----------------------------------------------------------------------
        // Transitions
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called by MainThreadDispatcher before it runs this job's work. Returns false
        /// when the job was cancelled while queued: the work must be skipped.
        /// </summary>
        public bool TryStart()
        {
            lock (_gate)
            {
                if (_state == McpJobState.Cancelled) return false;
                if (_state == McpJobState.Queued)
                {
                    _state      = McpJobState.Running;
                    _startedUtc = DateTime.UtcNow;
                }
                return true;
            }
        }

        /// <summary>
        /// True the first time only. MainThreadDispatcher calls it when Unity starts this
        /// job's work, and starts a new undo step when it returns true, so one Ctrl+Z
        /// undoes one request and not every request since the user's last click.
        /// </summary>
        internal bool TryBeginUndoStep()
        {
            lock (_gate)
            {
                if (_undoStepStarted) return false;
                _undoStepStarted = true;
                return true;
            }
        }

        /// <summary>
        /// Records the final response. Ignored once the job has finished (a cancelled
        /// job keeps its Cancelled state).
        /// </summary>
        public void Complete(McpResponse response)
        {
            lock (_gate)
            {
                if (_state >= McpJobState.Done) return;
                if (_startedUtc == default) _startedUtc = DateTime.UtcNow;
                _result      = response ?? McpResponse.Fail("INTERNAL_ERROR", "The operation returned no response.");
                _state       = _result.Success ? McpJobState.Done : McpJobState.Failed;
                _finishedUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Reports progress (0–1) and what is happening, for get_job_status.</summary>
        public void SetProgress(float progress, string message = null)
        {
            lock (_gate)
            {
                _progress = progress;
                if (message != null) _progressMessage = message;
            }
        }

        /// <summary>
        /// Lets a running job be cancelled. The callback runs on the main thread and
        /// returns true when the operation was stopped.
        /// </summary>
        public void AllowCancelWhileRunning(Func<bool> cancel)
        {
            lock (_gate) _cancelRunning = cancel;
        }

        /// <summary>
        /// Cancels a queued job at once. A running job is cancelled only if it allows it,
        /// and then on the main thread. Returns a failure that explains why not otherwise.
        /// </summary>
        public McpResponse RequestCancel()
        {
            Func<bool> cancelRunning;
            lock (_gate)
            {
                switch (_state)
                {
                    case McpJobState.Queued:
                        _state       = McpJobState.Cancelled;
                        _finishedUtc = DateTime.UtcNow;
                        _result      = McpResponse.Fail("CANCELLED", "The job was cancelled before Unity started it.");
                        return McpResponse.Ok(ToJson(includeResult: false));
                    case McpJobState.Running when _cancelRunning == null:
                        return McpResponse.Fail("CANNOT_CANCEL",
                            "Unity has already started this job and cannot stop it safely halfway. " +
                            "Wait for it with get_job_status.");
                    case McpJobState.Running:
                        cancelRunning = _cancelRunning;
                        break;
                    default:
                        return McpResponse.Fail("ALREADY_FINISHED", $"The job has already finished ({StateName(_state)}).");
                }
            }

            // A cancellable operation (a light bake) does not block the main thread.
            // The stop runs outside any request's job, so it cannot itself be cancelled.
            var requester = Current;
            Current = null;
            MainThreadDispatcher.EnqueueAsync(() =>
            {
                if (!cancelRunning()) return;
                lock (_gate)
                {
                    if (_state >= McpJobState.Done) return;
                    _state       = McpJobState.Cancelled;
                    _finishedUtc = DateTime.UtcNow;
                    _result      = McpResponse.Fail("CANCELLED", "The job was cancelled while running.");
                }
            });
            Current = requester;
            return McpResponse.Ok(new JObject
            {
                ["job_id"]  = Id,
                ["state"]   = "cancelling",
                ["message"] = "Unity is stopping the job. get_job_status shows when it has stopped.",
            });
        }

        // -----------------------------------------------------------------------
        // JSON
        // -----------------------------------------------------------------------

        /// <summary>The job as GET /jobs returns it. includeResult adds the final response.</summary>
        public JObject ToJson(bool includeResult)
        {
            lock (_gate)
            {
                var now  = DateTime.UtcNow;
                var json = new JObject
                {
                    ["job_id"] = Id,
                    ["tool"]   = _tool,
                    ["route"]  = $"{Method} {Path}",
                    ["state"]  = StateName(_state),
                };

                if (_state == McpJobState.Queued)
                {
                    json["queued_seconds"] = Seconds(now - CreatedUtc);
                    json["note"] = "Waiting for Unity's main thread: Unity may be compiling, baking, " +
                                   "or showing a dialog. cancel_job can still cancel it.";
                }
                else if (_state == McpJobState.Running)
                {
                    json["running_seconds"] = Seconds(now - _startedUtc);
                }
                else
                {
                    json["seconds"] = Seconds(_finishedUtc - CreatedUtc);
                }

                if (_progress >= 0f && _state == McpJobState.Running)
                    json["progress"] = Math.Round(_progress, 3);
                if (!string.IsNullOrEmpty(_progressMessage) && _state == McpJobState.Running)
                    json["progress_message"] = _progressMessage;

                if (includeResult && _result != null)
                    json["result"] = JObject.FromObject(_result);
                return json;
            }
        }

        public static string StateName(McpJobState state) => state.ToString().ToLowerInvariant();

        private static double Seconds(TimeSpan span) => Math.Round(Math.Max(0, span.TotalSeconds), 1);

        // -----------------------------------------------------------------------
        // Background jobs
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a running job for an operation that continues in the background after
        /// its request returns (a light bake), and stores it so get_job_status finds it.
        /// The operation completes it with Complete.
        /// </summary>
        public static McpJob StartBackground(string tool, string method, string path)
        {
            var job = new McpJob(method, path, tool);
            job.TryStart();
            McpJobStore.Add(job);
            return job;
        }
    }
}
