using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/bake_lighting
    ///
    /// Triggers Unity's progressive lightmapper or clears baked data.
    /// The natural counterpart to bake_navmesh — both should be called at the end
    /// of any session that makes significant layout changes.
    ///
    /// Modes:
    ///   "async" — trigger a bake and return immediately; monitor in Lighting window
    ///   "sync"  — block until the bake completes; only suitable for small scenes
    ///   "clear" — delete all baked lighting data from the scene
    ///
    /// Note: Lightmapping.BakeAsync() includes reflection probes when they are
    /// configured for baked mode. The bake_reflection_probes parameter in sync mode
    /// additionally calls Lightmapping.BakeAllReflectionProbesSnapshots() after
    /// the main bake completes.
    /// </summary>
    public static class LightBakeHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/world/bake_lighting
        // -----------------------------------------------------------------------

        public static Task<McpResponse> BakeLightingAsync(BakeLightingRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                string mode = req?.Mode?.ToLowerInvariant() ?? "async";

                // ── Clear mode ────────────────────────────────────────────────
                if (mode == "clear")
                {
                    Lightmapping.Clear();
                    return McpResponse.Ok(new BakeLightingResultData
                    {
                        Mode    = "clear",
                        Started = true,
                        Message = "Baked lighting data cleared from the scene."
                    });
                }

                // ── Guard against concurrent bakes ────────────────────────────
                if (Lightmapping.isRunning)
                    return McpResponse.Fail("BAKE_IN_PROGRESS",
                        "A lighting bake is already in progress. " +
                        "Wait for it to finish in Window > Rendering > Lighting before starting another.");

                // ── Sync mode ─────────────────────────────────────────────────
                if (mode == "sync")
                {
                    var sw      = System.Diagnostics.Stopwatch.StartNew();
                    bool success = Lightmapping.Bake();
                    sw.Stop();

                    if (!success)
                        return McpResponse.Fail("BAKE_FAILED",
                            "Lightmapping.Bake() returned false. " + SetupHint());

                    return McpResponse.Ok(new BakeLightingResultData
                    {
                        Mode      = "sync",
                        Started   = true,
                        Completed = true,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        Message   = $"Lighting bake completed in {sw.ElapsedMilliseconds}ms. " +
                                    "Reflection probes configured for Baked mode were captured as part of this bake."
                    });
                }

                // ── Async mode (default) ──────────────────────────────────────
                bool started = Lightmapping.BakeAsync();
                if (!started)
                    return McpResponse.Fail("BAKE_NOT_STARTED",
                        "Lightmapping.BakeAsync() returned false. " + SetupHint());

                string note = req.BakeReflectionProbes
                    ? " Reflection probes configured for baked mode will be captured during this bake."
                    : string.Empty;

                return McpResponse.Ok(new BakeLightingResultData
                {
                    Mode    = "async",
                    Started = true,
                    Message = $"Lighting bake started.{note} " +
                              "Monitor progress in Window > Rendering > Lighting."
                });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string SetupHint() =>
            "Check that: (1) at least one light is set to Mixed or Baked mode, " +
            "(2) the scene's Lighting settings have Baked Global Illumination enabled, " +
            "(3) the scene has been saved.";
    }
}
