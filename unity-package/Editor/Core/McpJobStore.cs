using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// The jobs a caller can ask about: requests handed off because they took longer
    /// than McpHttpListener.HandoffSeconds, and background operations such as light
    /// bakes. Keeps the last <see cref="Capacity"/> jobs; when full, the oldest finished
    /// job goes first, so an unfinished job is never dropped while it runs.
    ///
    /// Served by GET /jobs and POST /jobs/cancel. Lives in memory: a script reload
    /// clears it.
    /// </summary>
    public static class McpJobStore
    {
        public const int Capacity = 50;

        private static readonly object Gate = new object();
        private static readonly List<McpJob> Jobs = new List<McpJob>();

        public static void Add(McpJob job)
        {
            lock (Gate)
            {
                if (Jobs.Contains(job)) return;
                Jobs.Add(job);
                while (Jobs.Count > Capacity)
                {
                    var oldestFinished = Jobs.FindIndex(j => j.IsFinished);
                    Jobs.RemoveAt(oldestFinished >= 0 ? oldestFinished : 0);
                }
            }
        }

        public static bool TryGet(string id, out McpJob job)
        {
            lock (Gate)
            {
                job = Jobs.Find(j => j.Id == id);
                return job != null;
            }
        }

        /// <summary>The jobs, newest first.</summary>
        public static List<McpJob> GetAll()
        {
            lock (Gate)
            {
                var copy = new List<McpJob>(Jobs);
                copy.Reverse();
                return copy;
            }
        }

        /// <summary>Removes every job (tests).</summary>
        public static void Clear()
        {
            lock (Gate) Jobs.Clear();
        }

        // -----------------------------------------------------------------------
        // Routes
        // -----------------------------------------------------------------------

        /// <summary>GET /jobs?job_id=… — one job with its result; without an ID, the recent jobs.</summary>
        public static McpResponse GetStatus(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                var list = new JArray();
                foreach (var job in GetAll()) list.Add(job.ToJson(includeResult: false));
                return McpResponse.Ok(new JObject { ["count"] = list.Count, ["jobs"] = list });
            }

            return TryGet(id.Trim(), out var found)
                ? McpResponse.Ok(found.ToJson(includeResult: true))
                : NotFound(id);
        }

        /// <summary>POST /jobs/cancel { "job_id": … }</summary>
        public static McpResponse Cancel(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return McpResponse.Fail("INVALID_PARAMS", "job_id is required.");
            return TryGet(id.Trim(), out var job) ? job.RequestCancel() : NotFound(id);
        }

        private static McpResponse NotFound(string id) =>
            McpResponse.Fail("JOB_NOT_FOUND",
                $"No job '{id}'. Unity keeps the last {Capacity} jobs, and a script reload clears them " +
                "(an interrupted job may or may not have run: check the scene before repeating it).");
    }
}
