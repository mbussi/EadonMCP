using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    // -----------------------------------------------------------------------
    // Domain objects
    // -----------------------------------------------------------------------

    public class Proposal
    {
        public string                  Id         { get; set; }
        public string                  Summary    { get; set; }
        public ProposalOperationItem[] Operations { get; set; }
        public ProposalStatus          Status     { get; set; }
        public DateTime                CreatedAt  { get; set; }

        /// <summary>When it finished (executed, failed or discarded).</summary>
        public DateTime?               FinishedAt { get; set; }

        /// <summary>One entry per operation once the proposal has run.</summary>
        public OperationResultData[]   Results    { get; set; }

        /// <summary>Why it failed: the operation and its error.</summary>
        public string                  Failure    { get; set; }

        /// <summary>True while it waits for a decision (pending or approved).</summary>
        [JsonIgnore]
        public bool IsOpen => Status == ProposalStatus.Pending || Status == ProposalStatus.Approved;
    }

    public class ProposalOperationItem
    {
        public int     Id          { get; set; }
        public string  Tool        { get; set; }
        public string  Description { get; set; }

        /// <summary>
        /// The tool's arguments, exactly as the tool takes them. A proposal runs the
        /// tool with them as if it had been called directly (dry_run forced off).
        /// </summary>
        [JsonProperty("params")]
        public JObject Params { get; set; }
    }

    /// <summary>
    /// Pending: waiting for review. Approved: the user approved it in the Operation
    /// Queue, so execute_proposal may run it. Executing: running now (a second execute
    /// is refused). Executed, Failed (undone as a whole), Discarded: final.
    /// </summary>
    public enum ProposalStatus { Pending, Approved, Executing, Executed, Failed, Discarded }

    // -----------------------------------------------------------------------
    // Store
    // -----------------------------------------------------------------------

    /// <summary>
    /// The proposals: open ones (pending, approved) and the last
    /// <see cref="FinishedKept"/> finished ones, so Claude can read the outcome.
    ///
    /// Kept in SessionState, so proposals survive a script reload (but not quitting
    /// Unity). A proposal that was executing when scripts reloaded is marked failed.
    ///
    /// All public methods run on the Unity main thread (via MainThreadDispatcher or
    /// from editor UI), so no locking is needed.
    /// </summary>
    [InitializeOnLoad]
    public static class ProposalStore
    {
        public const int FinishedKept = 20;

        private const string SessionKey = "Eadon.McpBridge.Proposals";

        private static readonly List<Proposal> _store = new List<Proposal>();

        /// <summary>Fired on the main thread whenever the store changes.</summary>
        public static event Action OnChanged;

        static ProposalStore()
        {
            Load();
        }

        // -----------------------------------------------------------------------
        // Write
        // -----------------------------------------------------------------------

        /// <summary>Creates and stores a new pending proposal. Returns the new proposal.</summary>
        public static Proposal Add(string summary, ProposalOperationItem[] operations)
        {
            var proposal = new Proposal
            {
                Id         = Guid.NewGuid().ToString("N").Substring(0, 8),
                Summary    = summary,
                Operations = operations ?? Array.Empty<ProposalOperationItem>(),
                Status     = ProposalStatus.Pending,
                CreatedAt  = DateTime.Now
            };
            _store.Add(proposal);
            Changed();
            return proposal;
        }

        /// <summary>Updates the status of a proposal and fires OnChanged.</summary>
        public static void UpdateStatus(string id, ProposalStatus status)
        {
            if (!TryGet(id, out var p)) return;
            p.Status = status;
            if (!p.IsOpen && status != ProposalStatus.Executing)
                p.FinishedAt = DateTime.Now;
            Changed();
        }

        /// <summary>Records how a proposal ended (executed or failed) with its results.</summary>
        public static void Finish(string id, ProposalStatus status, OperationResultData[] results, string failure)
        {
            if (!TryGet(id, out var p)) return;
            p.Status     = status;
            p.Results    = results;
            p.Failure    = failure;
            p.FinishedAt = DateTime.Now;
            Changed();
        }

        /// <summary>Removes all proposals. Used in tests and on manual reset.</summary>
        public static void Clear()
        {
            _store.Clear();
            Changed();
        }

        // -----------------------------------------------------------------------
        // Read
        // -----------------------------------------------------------------------

        public static bool TryGet(string id, out Proposal proposal)
        {
            proposal = _store.Find(p => p.Id == id);
            return proposal != null;
        }

        /// <summary>Proposals waiting for review (pending) in creation order.</summary>
        public static IReadOnlyList<Proposal> GetPending() =>
            _store.Where(p => p.Status == ProposalStatus.Pending).OrderBy(p => p.CreatedAt).ToList();

        /// <summary>Open proposals (pending and approved) and any executing one, in creation order.</summary>
        public static IReadOnlyList<Proposal> GetOpen() =>
            _store.Where(p => p.IsOpen || p.Status == ProposalStatus.Executing).OrderBy(p => p.CreatedAt).ToList();

        /// <summary>Finished proposals, most recent first.</summary>
        public static IReadOnlyList<Proposal> GetFinished() =>
            _store.Where(p => !p.IsOpen && p.Status != ProposalStatus.Executing)
                  .OrderByDescending(p => p.FinishedAt ?? p.CreatedAt).ToList();

        // -----------------------------------------------------------------------
        // Persistence
        // -----------------------------------------------------------------------

        private static void Changed()
        {
            Trim();
            Save();
            OnChanged?.Invoke();
        }

        /// <summary>Drops finished proposals beyond FinishedKept, oldest first.</summary>
        private static void Trim()
        {
            var finished = GetFinished();
            for (int i = FinishedKept; i < finished.Count; i++)
                _store.Remove(finished[i]);
        }

        private static void Save()
        {
            try
            {
                SessionState.SetString(SessionKey, JsonConvert.SerializeObject(_store));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Bridge] Could not keep proposals for the next script reload: {ex.Message}");
            }
        }

        private static void Load()
        {
            try
            {
                var json = SessionState.GetString(SessionKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return;

                var saved = JsonConvert.DeserializeObject<List<Proposal>>(json);
                if (saved == null) return;

                foreach (var p in saved)
                {
                    if (p.Status != ProposalStatus.Executing) continue;
                    p.Status     = ProposalStatus.Failed;
                    p.FinishedAt = DateTime.Now;
                    p.Failure    = "Unity reloaded scripts while the proposal was executing. " +
                                   "Check the scene: some operations may have run.";
                }
                _store.AddRange(saved);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Bridge] Could not restore proposals after the script reload: {ex.Message}");
            }
        }
    }
}
