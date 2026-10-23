using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/bake_lighting
    ///
    /// Starts Unity's progressive lightmapper in the background, or clears baked data.
    /// The natural counterpart to bake_navmesh — both should be called at the end
    /// of any session that makes significant layout changes.
    ///
    /// Modes:
    ///   "async" — start a background bake and return its job ID at once. The editor
    ///             stays responsive; get_job_status shows the bake's progress and result,
    ///             cancel_job stops it.
    ///   "clear" — delete all baked lighting data from the scene
    ///
    /// The old "sync" mode (Lightmapping.Bake) froze the editor for the whole bake and
    /// is refused with a pointer to "async".
    ///
    /// Reflection probes set to Baked are captured as part of every bake.
    /// </summary>
    public static class LightBakeHandler
    {
        private const string Route = "/tools/world/bake_lighting";

        // -----------------------------------------------------------------------
        // POST /tools/world/bake_lighting
        // -----------------------------------------------------------------------

        public static Task<McpResponse> BakeLightingAsync(BakeLightingRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                string mode = req?.Mode?.Trim().ToLowerInvariant() ?? "async";

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

                if (mode == "sync")
                    return McpResponse.Fail("INVALID_PARAMS",
                        "mode \"sync\" is no longer supported: it froze the editor for the whole bake. " +
                        "Use \"async\": the bake runs in the background and get_job_status reports when it has finished.");
                if (mode != "async")
                    return McpResponse.Fail("INVALID_PARAMS", $"Unknown mode '{req.Mode}'. Use \"async\" or \"clear\".");

                // ── Guard against concurrent bakes ────────────────────────────
                if (Lightmapping.isRunning)
                {
                    var running = FindRunningBakeJob();
                    return McpResponse.Fail("BAKE_IN_PROGRESS",
                        "A lighting bake is already in progress" +
                        (running != null ? $" (job {running.Id}: check it with get_job_status, stop it with cancel_job)." : ".") +
                        " Wait for it to finish before starting another.");
                }

                // ── Start the background bake ─────────────────────────────────
                var watcher = new BakeWatcher();
                if (!Lightmapping.BakeAsync())
                {
                    watcher.Dispose();
                    return McpResponse.Fail("BAKE_NOT_STARTED",
                        "Lightmapping.BakeAsync() returned false. " + SetupHint());
                }

                var job = McpJob.StartBackground("bake_lighting", "POST", Route);
                job.SetProgress(0f, "Baking lighting");
                job.AllowCancelWhileRunning(() =>
                {
                    if (!Lightmapping.isRunning) return false;
                    Lightmapping.Cancel();
                    return true;
                });
                watcher.Start(job, req?.BakeReflectionProbes == true);

                return McpResponse.Ok(new BakeLightingResultData
                {
                    Mode    = "async",
                    Started = true,
                    JobId   = job.Id,
                    Message = $"Lighting bake started as job {job.Id}. The editor stays usable while it bakes. " +
                              $"Check progress and the result with get_job_status(\"{job.Id}\"); " +
                              $"cancel_job(\"{job.Id}\") stops it."
                });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static McpJob FindRunningBakeJob() =>
            McpJobStore.GetAll().Find(j => j.Tool == "bake_lighting" && !j.IsFinished);

        private static string SetupHint() =>
            "Check that: (1) at least one light is set to Mixed or Baked mode, " +
            "(2) the scene's Lighting settings have Baked Global Illumination enabled, " +
            "(3) the scene has been saved.";

        /// <summary>
        /// Follows one background bake on the editor update loop: reports progress and
        /// completes the job when Unity finishes, fails or cancels the bake.
        /// </summary>
        private sealed class BakeWatcher
        {
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private McpJob _job;
            private bool   _probes;
            private bool   _completed;
            private bool   _seenRunning;

            public BakeWatcher()
            {
                // Subscribe before the bake starts, so a very quick bake is not missed.
                Lightmapping.bakeCompleted += OnBakeCompleted;
            }

            public void Start(McpJob job, bool probes)
            {
                _job    = job;
                _probes = probes;
                EditorApplication.update += Update;
            }

            public void Dispose()
            {
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                EditorApplication.update   -= Update;
            }

            private void OnBakeCompleted() => _completed = true;

            private void Update()
            {
                if (_job == null) return;

                if (_job.IsFinished)
                {
                    // Cancelled through cancel_job.
                    Dispose();
                    return;
                }

                if (_completed)
                {
                    _job.Complete(McpResponse.Ok(new BakeLightingResultData
                    {
                        Mode      = "async",
                        Started   = true,
                        Completed = true,
                        ElapsedMs = _clock.ElapsedMilliseconds,
                        Message   = $"Lighting bake completed in {_clock.Elapsed.TotalSeconds:0.0} s." +
                                    (_probes ? " Reflection probes set to Baked were captured as part of the bake." : string.Empty)
                    }));
                    Dispose();
                    return;
                }

                if (Lightmapping.isRunning)
                {
                    _seenRunning = true;
                    _job.SetProgress(Lightmapping.buildProgress, "Baking lighting");
                    return;
                }

                // Not running and not completed: stopped from the Lighting window, or failed.
                // Give a bake that has not shown up as running yet a moment to start.
                if (_seenRunning || _clock.Elapsed.TotalSeconds > 5)
                {
                    _job.Complete(McpResponse.Fail("BAKE_STOPPED",
                        "The bake stopped without completing: it was cancelled in the Lighting window, " +
                        "or it failed (see the Console). " + SetupHint()));
                    Dispose();
                }
            }
        }
    }
}
