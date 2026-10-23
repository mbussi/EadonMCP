using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    }

    public class ProposalOperationItem
    {
        public int     Id          { get; set; }
        public string  Tool        { get; set; }
        public string  Description { get; set; }

        /// <summary>
        /// Raw JSON parameters for this operation. Deserialised into the
        /// appropriate request DTO when the operation is executed.
        /// </summary>
        [JsonProperty("params")]
        public JObject Params { get; set; }
    }

    public enum ProposalStatus { Pending, Executed, Discarded }

    // -----------------------------------------------------------------------
    // Store
    // -----------------------------------------------------------------------

    /// <summary>
    /// In-memory store for pending proposals.
    ///
    /// State is intentionally not persisted — all proposals are cleared on
    /// domain reload (i.e. when Unity recompiles scripts). Any pending
    /// proposals should be resolved before modifying code.
    ///
    /// All public methods are called from the Unity main thread via
    /// MainThreadDispatcher, so no locking is needed.
    /// </summary>
    public static class ProposalStore
    {
        private static readonly Dictionary<string, Proposal> _store =
            new Dictionary<string, Proposal>();

        /// <summary>Fired on the main thread whenever the store changes.</summary>
        public static event Action OnChanged;

        // -----------------------------------------------------------------------
        // Write
        // -----------------------------------------------------------------------

        /// <summary>Creates and stores a new proposal. Returns the new proposal.</summary>
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
            _store[proposal.Id] = proposal;
            OnChanged?.Invoke();
            return proposal;
        }

        /// <summary>Updates the status of a proposal and fires OnChanged.</summary>
        public static void UpdateStatus(string id, ProposalStatus status)
        {
            if (_store.TryGetValue(id, out var p))
            {
                p.Status = status;
                OnChanged?.Invoke();
            }
        }

        /// <summary>Removes all proposals. Used in tests and on manual reset.</summary>
        public static void Clear()
        {
            _store.Clear();
            OnChanged?.Invoke();
        }

        // -----------------------------------------------------------------------
        // Read
        // -----------------------------------------------------------------------

        public static bool TryGet(string id, out Proposal proposal) =>
            _store.TryGetValue(id, out proposal);

        /// <summary>All pending proposals in creation order.</summary>
        public static IReadOnlyList<Proposal> GetPending() =>
            _store.Values
                  .Where(p => p.Status == ProposalStatus.Pending)
                  .OrderBy(p => p.CreatedAt)
                  .ToList();
    }
}
