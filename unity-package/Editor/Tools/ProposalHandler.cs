using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.UI;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles the proposal workflow: submit, execute, discard, and list.
    ///
    /// ExecuteProposalAsync routes each operation back through the existing
    /// Phase 3 / Phase 4 handlers by matching the tool name to the correct
    /// request type. All operations in a proposal execute as a single Undo group.
    /// </summary>
    public static class ProposalHandler
    {
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

                // Convert incoming DTOs to domain objects
                var items = Array.ConvertAll(req.Operations, op => new ProposalOperationItem
                {
                    Id          = op.Id,
                    Tool        = op.Tool,
                    Description = op.Description,
                    Params      = op.Params
                });

                var proposal = ProposalStore.Add(req.Summary, items);

                return McpResponse.Ok(new ProposalSubmittedData
                {
                    ProposalId     = proposal.Id,
                    Summary        = proposal.Summary,
                    OperationCount = proposal.Operations.Length,
                    Status         = "pending"
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/proposal/execute
        // -----------------------------------------------------------------------

        public static async Task<McpResponse> ExecuteProposalAsync(ExecuteProposalRequest req)
        {
            // Validate and retrieve — on main thread
            Proposal proposal = null;
            var validationError = await MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ProposalId))
                    return McpResponse.Fail("INVALID_PARAMS", "proposal_id must not be empty.");

                if (!ProposalStore.TryGet(req.ProposalId, out proposal))
                    return McpResponse.Fail("PROPOSAL_NOT_FOUND",
                        $"No proposal with id '{req.ProposalId}'.");

                if (proposal.Status != ProposalStatus.Pending)
                    return McpResponse.Fail("PROPOSAL_NOT_PENDING",
                        $"Proposal '{req.ProposalId}' is {proposal.Status.ToString().ToLower()}, not pending.");

                // Open a named Undo group so the entire proposal can be reversed
                // with a single Ctrl+Z
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"MCP: {proposal.Summary}");

                return null; // null = no error, proceed
            });

            if (validationError != null) return validationError;

            // Execute each operation (each handler uses its own MainThreadDispatcher call)
            var results = new List<OperationResultData>();
            foreach (var op in proposal.Operations)
            {
                var (success, error) = await ExecuteOperationAsync(op);
                results.Add(new OperationResultData
                {
                    OperationId = op.Id,
                    Success     = success,
                    Error       = error
                });
            }

            // Mark proposal as executed and collapse Undo group
            await MainThreadDispatcher.EnqueueAsync(() =>
            {
                ProposalStore.UpdateStatus(req.ProposalId, ProposalStatus.Executed);
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            });

            int executedCount = results.FindAll(r => r.Success).Count;
            int failedCount   = results.Count - executedCount;

            return McpResponse.Ok(new ExecuteProposalResultData
            {
                ProposalId    = req.ProposalId,
                ExecutedCount = executedCount,
                FailedCount   = failedCount,
                Results       = results.ToArray()
            });
        }

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

                if (proposal.Status != ProposalStatus.Pending)
                    return McpResponse.Fail("PROPOSAL_NOT_PENDING",
                        $"Proposal '{req.ProposalId}' is already {proposal.Status.ToString().ToLower()}.");

                ProposalStore.UpdateStatus(req.ProposalId, ProposalStatus.Discarded);
                return McpResponse.Ok(new { proposal_id = req.ProposalId, status = "discarded" });
            });

        // -----------------------------------------------------------------------
        // GET /tools/proposal/pending
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetPendingProposalsAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var pending = ProposalStore.GetPending();
                var data = System.Array.ConvertAll(pending.ToArray(), p => new PendingProposalData
                {
                    ProposalId     = p.Id,
                    Summary        = p.Summary,
                    OperationCount = p.Operations.Length,
                    CreatedAt      = p.CreatedAt.ToString("HH:mm:ss"),
                    Operations     = System.Array.ConvertAll(p.Operations, op => new PendingOperationData
                    {
                        Id          = op.Id,
                        Tool        = op.Tool,
                        Description = op.Description
                    })
                });
                return McpResponse.Ok(new { proposals = data });
            });

        // -----------------------------------------------------------------------
        // Operation routing
        // -----------------------------------------------------------------------

        private static async Task<McpResponse> RunExtensionToolAsync(ProposalOperationItem op)
        {
            if (McpToolRegistry.TryGet(op.Tool, out var tool))
                return await tool.Handler(op.Params);
            return McpResponse.Fail("UNKNOWN_TOOL", $"No handler registered for tool '{op.Tool}'.");
        }

        private static async Task<(bool success, string error)> ExecuteOperationAsync(
            ProposalOperationItem op)
        {
            try
            {
                if (op.Params == null)
                    return (false, $"Operation {op.Id}: params is null.");

                McpResponse response = op.Tool?.ToLowerInvariant() switch
                {
                    // Phase 3
                    "place_prefab"   => await PlacementHandler.PlacePrefabAsync(
                                            DeserialiseAs<PlacePrefabRequest>(op.Params, dryRun: false)),
                    "set_transform"  => await PlacementHandler.SetTransformAsync(
                                            DeserialiseAs<SetTransformRequest>(op.Params, dryRun: false)),
                    "delete_object"  => await PlacementHandler.DeleteObjectAsync(
                                            DeserialiseAs<DeleteObjectRequest>(op.Params, dryRun: false)),
                    "create_empty"   => await PlacementHandler.CreateEmptyAsync(
                                            DeserialiseAs<CreateEmptyRequest>(op.Params, dryRun: false)),

                    // Phase 4
                    "create_light"      => await LightingHandler.CreateLightAsync(
                                              DeserialiseAs<CreateLightRequest>(op.Params, dryRun: false)),
                    "set_ambient_light" => await LightingHandler.SetAmbientLightAsync(
                                              DeserialiseAs<SetAmbientLightRequest>(op.Params, dryRun: false)),
                    "set_skybox"        => await LightingHandler.SetSkyboxAsync(
                                              DeserialiseAs<SetSkyboxRequest>(op.Params, dryRun: false)),
                    "set_fog"           => await LightingHandler.SetFogAsync(
                                              DeserialiseAs<SetFogRequest>(op.Params, dryRun: false)),

                    // Tools registered by extensions (McpToolRegistry)
                    _ => await RunExtensionToolAsync(op)
                };

                return (response.Success, response.Error?.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Proposal operation {op.Id} ({op.Tool}) threw: {ex.Message}");
                return (false, ex.Message);
            }
        }

        
        /// <summary>
        /// Submit a proposal directly from the main thread (no EnqueueAsync).
        /// Called by DressingHandler.DressAreaAsync after spatial computation.
        /// The caller must already be running on the main thread.
        /// </summary>
        internal static ProposalSubmittedData SubmitInternal(ProposeChangesRequest req)
        {
            var items = System.Array.ConvertAll(req.Operations, op => new ProposalOperationItem
            {
                Id          = op.Id,
                Tool        = op.Tool,
                Description = op.Description,
                Params      = op.Params,
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
        
        /// <summary>
        /// Deserialises a JObject into T and forces DryRun = false.
        /// Proposal operations always execute live — the proposal IS the validation step.
        /// </summary>
        private static T DeserialiseAs<T>(JObject json, bool dryRun) where T : class, new()
        {
            var obj = json.ToObject<T>() ?? new T();

            // Force DryRun = false via reflection so we don't need a shared interface
            var prop = typeof(T).GetProperty("DryRun");
            prop?.SetValue(obj, false);

            return obj;
        }
    }
}
