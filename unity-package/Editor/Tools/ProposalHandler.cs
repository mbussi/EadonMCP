using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// The proposal workflow: submit, approve, execute, discard, and list.
    ///
    /// A proposal is a review step. Claude (or dress_area) submits a list of
    /// operations; the user reviews them in the Operation Queue window and either
    /// executes the proposal there or approves it, after which execute_proposal may
    /// run it. execute_proposal refuses a proposal the user has not approved.
    ///
    /// Execution:
    ///   • runs once: the proposal is marked executing before its first operation, so
    ///     a second execute is refused;
    ///   • runs each operation exactly as the tool would run if called directly (built-in
    ///     tools through McpRouter, extension tools through their handler), with
    ///     dry_run forced off;
    ///   • is one undo step: the undo group at the start is saved and everything after
    ///     it is collapsed into it;
    ///   • is all or nothing: at the first failed operation everything the proposal did
    ///     is undone, and the proposal is marked failed with that operation and its error.
    ///
    /// Only tools whose changes Undo reverses can be part of a proposal
    /// (McpToolCatalog.ProposalRefusal); others are refused when the proposal is submitted.
    /// </summary>
    public static class ProposalHandler
    {
        private const string QueueMenu = "Eadon > MCP Bridge > Operation Queue";

        // -----------------------------------------------------------------------
        // POST /tools/proposal/submit
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SubmitProposalAsync(ProposeChangesRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Summary))
                    return McpResponse.Fail("INVALID_PARAMS", "summary must not be empty.");

                if (req.Operations == null || req.Operations.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "operations must not be empty.");

                var problems = CheckOperations(req.Operations);
                if (problems != null)
                    return McpResponse.Fail("INVALID_OPERATION", problems);

                return McpResponse.Ok(SubmitInternal(req));
            });

        /// <summary>
        /// Null when every operation can run in a proposal; otherwise one line per problem
        /// and the list of tools a proposal can run.
        /// </summary>
        private static string CheckOperations(ProposalOperationData[] operations)
        {
            var problems = new StringBuilder();
            var ids      = new HashSet<int>();
            foreach (var op in operations)
            {
                if (op == null) { problems.AppendLine("An operation is empty."); continue; }
                if (!ids.Add(op.Id))
                    problems.AppendLine($"Operation id {op.Id} is used more than once.");
                if (string.IsNullOrWhiteSpace(op.Tool))
                {
                    problems.AppendLine($"Operation {op.Id} has no tool.");
                    continue;
                }
                var refusal = McpToolCatalog.ProposalRefusal(op.Tool.Trim());
                if (refusal != null)
                    problems.AppendLine($"Operation {op.Id}: '{op.Tool}' cannot be part of a proposal: {refusal}.");
            }

            if (problems.Length == 0) return null;
            problems.Append("Tools a proposal can run: ")
                    .Append(string.Join(", ", McpToolCatalog.ProposalToolNames()))
                    .Append('.');
            return problems.ToString();
        }

        /// <summary>
        /// Submit a proposal directly from the main thread (no EnqueueAsync).
        /// Called by DressingHandler.DressAreaAsync after spatial computation.
        /// The caller must already be running on the main thread.
        /// </summary>
        internal static ProposalSubmittedData SubmitInternal(ProposeChangesRequest req)
        {
            var items = Array.ConvertAll(req.Operations, op => new ProposalOperationItem
            {
                Id          = op.Id,
                Tool        = op.Tool?.Trim(),
                Description = op.Description,
                Params      = op.Params ?? new JObject(),
            });

            var proposal = ProposalStore.Add(req.Summary, items);

            return new ProposalSubmittedData
            {
                ProposalId     = proposal.Id,
                Summary        = proposal.Summary,
                OperationCount = proposal.Operations.Length,
                Status         = "pending",
            };
        }

        // -----------------------------------------------------------------------
        // Approve (Operation Queue window only: approval is the user's decision)
        // -----------------------------------------------------------------------

        /// <summary>Marks a pending proposal approved, so execute_proposal may run it. Main thread.</summary>
        public static bool Approve(string proposalId)
        {
            if (!ProposalStore.TryGet(proposalId, out var proposal) || proposal.Status != ProposalStatus.Pending)
                return false;
            ProposalStore.UpdateStatus(proposalId, ProposalStatus.Approved);
            return true;
        }

        // -----------------------------------------------------------------------
        // POST /tools/proposal/execute
        // -----------------------------------------------------------------------

        /// <summary>execute_proposal: runs only a proposal the user has approved.</summary>
        public static Task<McpResponse> ExecuteProposalAsync(ExecuteProposalRequest req) =>
            ExecuteAsync(req?.ProposalId, requireApproval: true);

        /// <summary>The Operation Queue window's Execute: the user's click is the approval.</summary>
        public static Task<McpResponse> ExecuteFromQueueAsync(string proposalId) =>
            ExecuteAsync(proposalId, requireApproval: false);

        private static async Task<McpResponse> ExecuteAsync(string proposalId, bool requireApproval)
        {
            // Check and claim on the main thread, in one step, so a second execute is refused.
            Proposal proposal = null;
            int undoGroup = -1;
            var claimError = await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(proposalId))
                    return McpResponse.Fail("INVALID_PARAMS", "proposal_id must not be empty.");

                if (!ProposalStore.TryGet(proposalId, out proposal))
                    return McpResponse.Fail("PROPOSAL_NOT_FOUND", $"No proposal with id '{proposalId}'.");

                if (proposal.Status == ProposalStatus.Pending && requireApproval)
                    return McpResponse.Fail("PROPOSAL_NOT_APPROVED",
                        $"Proposal '{proposalId}' is waiting for the user's review. Ask the user to open " +
                        $"{QueueMenu} and either Approve it (then call execute_proposal again) or Execute it there.");

                if (!proposal.IsOpen)
                    return McpResponse.Fail("PROPOSAL_NOT_PENDING",
                        $"Proposal '{proposalId}' is {StatusName(proposal.Status)}, not pending or approved.");

                ProposalStore.UpdateStatus(proposalId, ProposalStatus.Executing);

                // One undo step for the whole proposal: everything after this group is
                // collapsed into it (or reverted down to it on failure).
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName(proposal));
                undoGroup = Undo.GetCurrentGroup();
                return null;
            });
            if (claimError != null) return claimError;

            // Run the operations in order; stop at the first failure.
            var results = new List<OperationResultData>();
            ProposalOperationItem failedOp = null;
            string failedError = null;

            foreach (var op in proposal.Operations)
            {
                var (success, error) = await RunOperationAsync(op);
                results.Add(new OperationResultData
                {
                    OperationId = op.Id,
                    Tool        = op.Tool,
                    Success     = success,
                    Outcome     = success ? "done" : "failed",
                    Error       = error,
                });
                if (!success)
                {
                    failedOp    = op;
                    failedError = error;
                    break;
                }
            }

            if (failedOp == null)
            {
                var data = await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.SetCurrentGroupName(UndoName(proposal));   // operations may rename the group
                    Undo.IncrementCurrentGroup();                    // the next edit is a separate step

                    var array = results.ToArray();
                    ProposalStore.Finish(proposal.Id, ProposalStatus.Executed, array, null);
                    return McpResponse.Ok(new ExecuteProposalResultData
                    {
                        ProposalId    = proposal.Id,
                        ExecutedCount = array.Length,
                        FailedCount   = 0,
                        Results       = array,
                    });
                });
                return data;
            }

            // Failed: undo everything the proposal did, then report the failed operation.
            return await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Undo.IncrementCurrentGroup();

                foreach (var r in results)
                    if (r.Success) r.Outcome = "undone";
                foreach (var op in proposal.Operations.Skip(results.Count))
                    results.Add(new OperationResultData { OperationId = op.Id, Tool = op.Tool, Outcome = "not run" });

                var failure = $"Operation {failedOp.Id} ({failedOp.Tool}" +
                              (string.IsNullOrWhiteSpace(failedOp.Description) ? "" : $": {failedOp.Description}") +
                              $") failed: {failedError?.TrimEnd('.', ' ')}. " +
                              "Everything the proposal had done was undone, so the scene is unchanged.";
                ProposalStore.Finish(proposal.Id, ProposalStatus.Failed, results.ToArray(), failure);

                return McpResponse.Fail("PROPOSAL_FAILED",
                    failure + " Fix the operation and submit a new proposal.");
            });
        }

        /// <summary>Runs one operation exactly as the tool would run if called directly, with dry_run off.</summary>
        private static async Task<(bool success, string error)> RunOperationAsync(ProposalOperationItem op)
        {
            try
            {
                var refusal = McpToolCatalog.ProposalRefusal(op.Tool);
                if (refusal != null)
                    return (false, $"'{op.Tool}' cannot be part of a proposal: {refusal}.");
                McpToolCatalog.TryGet(op.Tool, out var tool);

                var args = (JObject)(op.Params?.DeepClone() ?? new JObject());
                if (args.ContainsKey("dry_run") || tool.InputSchema?["properties"]?["dry_run"] != null)
                    args["dry_run"] = false;   // the proposal is the review step

                var response = tool.IsBuiltIn
                    ? await McpRouter.RouteAsync(McpRequest.FromArguments(tool.Method, tool.Path, args))
                    : await tool.Handler(args);

                if (response == null)
                    return (false, "The tool returned no response.");
                return response.Success ? (true, null) : (false, $"[{response.Error?.Code}] {response.Error?.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Proposal operation {op.Id} ({op.Tool}) threw: {ex}");
                return (false, ex.Message);
            }
        }

        private static string UndoName(Proposal proposal) => $"MCP: {proposal.Summary}";

        // -----------------------------------------------------------------------
        // POST /tools/proposal/discard
        // -----------------------------------------------------------------------

        public static Task<McpResponse> DiscardProposalAsync(DiscardProposalRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ProposalId))
                    return McpResponse.Fail("INVALID_PARAMS", "proposal_id must not be empty.");

                if (!ProposalStore.TryGet(req.ProposalId, out var proposal))
                    return McpResponse.Fail("PROPOSAL_NOT_FOUND",
                        $"No proposal with id '{req.ProposalId}'.");

                if (!proposal.IsOpen)
                    return McpResponse.Fail("PROPOSAL_NOT_PENDING",
                        $"Proposal '{req.ProposalId}' is already {StatusName(proposal.Status)}.");

                ProposalStore.UpdateStatus(req.ProposalId, ProposalStatus.Discarded);
                return McpResponse.Ok(new { proposal_id = req.ProposalId, status = "discarded" });
            });

        // -----------------------------------------------------------------------
        // GET /tools/proposal/pending
        // -----------------------------------------------------------------------

        /// <summary>
        /// Without an ID: the open proposals (pending, approved, executing).
        /// With an ID: that proposal in full (any status), with parameters and, once it
        /// has run, its results.
        /// </summary>
        public static Task<McpResponse> GetPendingProposalsAsync(string proposalId = null) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (!string.IsNullOrWhiteSpace(proposalId))
                {
                    return ProposalStore.TryGet(proposalId.Trim(), out var one)
                        ? McpResponse.Ok(ToData(one, detail: true))
                        : McpResponse.Fail("PROPOSAL_NOT_FOUND",
                            $"No proposal with id '{proposalId}'. Unity keeps open proposals and the last " +
                            $"{ProposalStore.FinishedKept} finished ones until Unity is closed.");
                }

                var data = ProposalStore.GetOpen().Select(p => ToData(p, detail: false)).ToArray();
                return McpResponse.Ok(new { proposals = data });
            });

        private static PendingProposalData ToData(Proposal p, bool detail) => new PendingProposalData
        {
            ProposalId     = p.Id,
            Summary        = p.Summary,
            Status         = StatusName(p.Status),
            OperationCount = p.Operations.Length,
            CreatedAt      = p.CreatedAt.ToString("HH:mm:ss"),
            FinishedAt     = p.FinishedAt?.ToString("HH:mm:ss"),
            Operations     = Array.ConvertAll(p.Operations, op => new PendingOperationData
            {
                Id          = op.Id,
                Tool        = op.Tool,
                Description = op.Description,
                Params      = detail ? op.Params : null,
            }),
            Results = detail ? p.Results : null,
            Failure = p.Failure,
        };

        public static string StatusName(ProposalStatus status) => status.ToString().ToLowerInvariant();
    }
}
