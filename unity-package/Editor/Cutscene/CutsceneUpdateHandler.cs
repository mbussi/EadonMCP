using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Cutscene;
#if CM2
using Cinemachine;
using Cinemachine.Timeline;
#elif CM3
using Unity.Cinemachine;
using UnityEngine.Splines;
#endif

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles Phase 3 cutscene operations.
    ///
    /// Routes to add in McpRouter:
    ///   POST /tools/cutscene/preview_sequence  → PreviewSequenceAsync(req)
    ///   POST /tools/cutscene/update_sequence   → UpdateSequenceAsync(req)
    /// </summary>
    public static class CutsceneUpdateHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/cutscene/preview_sequence
        // -----------------------------------------------------------------------

        public static Task<McpResponse> PreviewSequenceAsync(PreviewSequenceRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ContainerPath))
                    return McpResponse.Fail("INVALID_PARAMS", "container_path is required.");

                var containerGo = PlacementHandler.FindByPath(req.ContainerPath);
                if (containerGo == null)
                    return McpResponse.Fail("NOT_FOUND",
                        $"No object at '{req.ContainerPath}'. " +
                        "Call get_scene_hierarchy to find the correct path.");

                // Search container and direct children for PlayableDirector
                var director = containerGo.GetComponent<PlayableDirector>()
                    ?? containerGo.GetComponentInChildren<PlayableDirector>(true);

                if (director == null)
                    return McpResponse.Fail("NO_DIRECTOR",
                        $"No PlayableDirector found on '{req.ContainerPath}'. " +
                        "Was the sequence created with create_cutscene_sequence?");

                // Focus the container in the Hierarchy so the Timeline window snaps to it
                Selection.activeGameObject = containerGo;

                // Open the Timeline window (no-op if already open)
                EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");

                // Reset to t=0 and play
                director.time = 0.0;
                director.RebuildGraph();
                director.Play();

                float duration = director.playableAsset != null
                    ? (float)director.duration : 0f;

                return McpResponse.Ok(new PreviewSequenceResultData
                {
                    ContainerPath = req.ContainerPath,
                    Duration      = duration,
                    Message       =
                        $"Playback started ({duration:F1}s). " +
                        "The Timeline window shows progress. " +
                        "Call preview_sequence again to restart from t=0.",
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/cutscene/update_sequence
        // -----------------------------------------------------------------------

        public static Task<McpResponse> UpdateSequenceAsync(UpdateSequenceRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ContainerPath))
                    return McpResponse.Fail("INVALID_PARAMS", "container_path is required.");
                if (string.IsNullOrWhiteSpace(req.Template))
                    return McpResponse.Fail("INVALID_PARAMS",
                        "template is required so the handler knows which child objects to update. " +
                        "Pass the same template name used when the sequence was created.");

                var containerGo = PlacementHandler.FindByPath(req.ContainerPath);
                if (containerGo == null)
                    return McpResponse.Fail("NOT_FOUND",
                        $"No object at '{req.ContainerPath}'.");

                var director = containerGo.GetComponent<PlayableDirector>();
                if (director == null)
                    return McpResponse.Fail("NO_DIRECTOR",
                        $"No PlayableDirector on '{req.ContainerPath}'.");

                var timeline = director.playableAsset as TimelineAsset;
                if (timeline == null)
                    return McpResponse.Fail("NO_TIMELINE",
                        "PlayableDirector has no TimelineAsset bound.");

                var p        = req.Params ?? new JObject();
                var updated  = new List<string>();
                var warnings = new List<string>();

                // Sequence name = container's own GameObject name
                var seqName = containerGo.name;

                Undo.SetCurrentGroupName($"MCP Update {seqName}");
                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(timeline, "MCP Update Timeline");

                // ── Common: duration + blends ────────────────────────────── //
                // Read new values (null = don't change)
                float? newDuration = (float?)p["duration"];
                float? newBlendIn  = (float?)p["blend_in"];
                float? newBlendOut = (float?)p["blend_out"];
                float? newEaseIn   = (float?)p["ease_in"];
                float? newEaseOut  = (float?)p["ease_out"];

                // Get current values from the Cinemachine clip
                float curDuration = 0f, curBlendIn = 0f, curBlendOut = 0f;
#if CM2 || CM3
                var cineTrack = GetTrack<CinemachineTrack>(timeline);
                if (cineTrack != null)
                {
                    var clips = cineTrack.GetClips().ToArray();
                    if (clips.Length > 0)
                    {
                        curDuration = (float)clips[0].duration;
                        curBlendIn  = (float)clips[0].easeInDuration;
                        curBlendOut = (float)clips[0].easeOutDuration;
                    }
                }
#endif

                float duration = newDuration ?? curDuration;
                float blendIn  = newBlendIn  ?? newEaseIn  ?? curBlendIn;
                float blendOut = newBlendOut ?? newEaseOut ?? curBlendOut;

                if (duration <= 0f) duration = curDuration; // guard against 0

                bool durationChanged = newDuration.HasValue;
                bool blendChanged    = newBlendIn.HasValue || newBlendOut.HasValue ||
                                       newEaseIn.HasValue  || newEaseOut.HasValue;

                if (durationChanged || blendChanged)
                    ApplyTimingToTimeline(timeline, duration, blendIn, blendOut,
                        updated, warnings);

                // ── Template-specific updates ────────────────────────────── //
                switch (req.Template)
                {
                    case "DialogueCamera":
                        UpdateDialogueCamera(containerGo, seqName, p, updated, warnings);
                        break;

                    case "AreaReveal":
                        UpdatePathSequence(containerGo, timeline, seqName, p,
                            duration, blendIn, blendOut,
                            "reveal_start", "reveal_complete",
                            updated, warnings);
                        break;

                    case "FocusCut":
                        UpdateFocusCut(containerGo, seqName, p, updated, warnings);
                        break;

                    case "ArrivalFlythrough":
                        UpdatePathSequence(containerGo, timeline, seqName, p,
                            duration, blendIn, blendOut,
                            "arrival_start", "arrival_complete",
                            updated, warnings);
                        UpdateArrivalLookTarget(containerGo, seqName, p, updated);
                        break;

                    default:
                        warnings.Add(
                            $"Template '{req.Template}' is not recognised — " +
                            "only common timing fields were updated.");
                        break;
                }

                // ── Save and rebuild ─────────────────────────────────────── //
                EditorUtility.SetDirty(timeline);
                EditorUtility.SetDirty(containerGo);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                director.RebuildGraph();

                Undo.CollapseUndoOperations(undoGroup);

                return McpResponse.Ok(new UpdateSequenceResultData
                {
                    ContainerPath = req.ContainerPath,
                    UpdatedFields = updated.ToArray(),
                    Warnings      = warnings.ToArray(),
                });
            });

        // ------------------------------------------------------------------ //
        //  Timing — Cinemachine clip, ActivationTrack, SignalTrack           //
        // ------------------------------------------------------------------ //

        private static void ApplyTimingToTimeline(
            TimelineAsset timeline,
            float         duration,
            float         blendIn,
            float         blendOut,
            List<string>  updated,
            List<string>  warnings)
        {
            // Cinemachine clip
#if CM2 || CM3
            var cineTrack = GetTrack<CinemachineTrack>(timeline);
            if (cineTrack != null)
            {
                foreach (var clip in cineTrack.GetClips())
                {
                    clip.duration        = duration;
                    clip.easeInDuration  = blendIn;
                    clip.easeOutDuration = blendOut;
                }
                if (!updated.Contains("duration"))   updated.Add("duration");
                if (!updated.Contains("blend_times")) updated.Add("blend_times");
            }
            else
            {
                warnings.Add("Could not find CinemachineTrack to update duration.");
            }
#else
            warnings.Add("Cinemachine is not installed, so the camera clip timing was not updated.");
#endif

            // ActivationTrack
            var activationTrack = GetTrack<ActivationTrack>(timeline);
            if (activationTrack != null)
                foreach (var clip in activationTrack.GetClips())
                    clip.duration = duration;

            // SignalTrack — reposition markers by name
            var signalTrack = GetTrack<SignalTrack>(timeline);
            if (signalTrack != null)
            {
                foreach (var marker in signalTrack.GetMarkers().OfType<SignalEmitter>())
                {
                    if (marker.asset == null) continue;
                    var sigName = Path.GetFileNameWithoutExtension(
                        AssetDatabase.GetAssetPath(marker.asset));

                    switch (sigName)
                    {
                        // Blend-relative signals
                        case "dialogue_start":
                        case "focus_held":
                            marker.time = blendIn;
                            break;

                        case "dialogue_end":
                            marker.time = duration - blendOut;
                            break;

                        // End-of-sequence signals
                        case "reveal_complete":
                        case "arrival_complete":
                            marker.time = duration;
                            break;

                        // Start-of-sequence signals stay at 0
                        case "reveal_start":
                        case "arrival_start":
                            marker.time = 0.0;
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  DialogueCamera — look_offset → move LookTarget GO                 //
        // ------------------------------------------------------------------ //

        private static void UpdateDialogueCamera(
            GameObject   container,
            string       seqName,
            JObject      p,
            List<string> updated,
            List<string> warnings)
        {
            var rawOffset = p["look_offset"]?.ToObject<float[]>();
            if (rawOffset == null) return;

            var lookTargetGo = container.transform.Find($"LookTarget_{seqName}")?.gameObject;
            if (lookTargetGo == null)
            {
                warnings.Add($"LookTarget_{seqName} not found — look_offset not applied.");
                return;
            }

            // Find the NPC (VCam m_LookAt references it)
            // We need the NPC position to reconstruct the look-at world position.
            // Since LookTarget is already positioned in world space, we derive the
            // NPC root from the LookTarget minus the current offset.
            // Simplest: just read the look_offset and update LookTarget's position.
            // But we don't know the NPC position without re-parsing the original params.
            // Instead, treat rawOffset as a raw world-space adjustment: keep LookTarget
            // on the NPC's XZ and apply the Y (height) from the new offset.

            // The LookTarget was placed at: npcPos + lookOffset (at creation time).
            // Without knowing npcPos, we can't fully recalculate. The safest update:
            // shift LookTarget vertically by the new look_offset.y value.
            Undo.RecordObject(lookTargetGo.transform, "MCP Update LookTarget");
            var pos = lookTargetGo.transform.position;
            lookTargetGo.transform.position = new Vector3(
                rawOffset.Length > 0 ? pos.x + rawOffset[0] : pos.x,
                rawOffset.Length > 1 ? rawOffset[1]          : pos.y, // treat Y as absolute height
                rawOffset.Length > 2 ? pos.z + rawOffset[2] : pos.z);
            EditorUtility.SetDirty(lookTargetGo);
            updated.Add("look_offset");
        }

        // ------------------------------------------------------------------ //
        //  AreaReveal + ArrivalFlythrough path updates                       //
        // ------------------------------------------------------------------ //

        private static void UpdatePathSequence(
            GameObject   container,
            TimelineAsset timeline,
            string       seqName,
            JObject      p,
            float        duration,
            float        easeIn,
            float        easeOut,
            string       startSignal,
            string       endSignal,
            List<string> updated,
            List<string> warnings)
        {
            // Waypoints
            var rawWp = p["waypoints"]?.ToObject<float[][]>();
            if (rawWp != null)
            {
                if (rawWp.Length < 2)
                {
                    warnings.Add("waypoints must have at least 2 entries — skipped.");
                }
                else
                {
                    bool applied = false;
#if CM2
                    var pathGo = container.transform.Find($"Path_{seqName}")?.gameObject;
                    var path   = pathGo?.GetComponent<CinemachineSmoothPath>();
                    if (path != null)
                    {
                        Undo.RecordObject(path, "MCP Update Waypoints");
                        path.m_Waypoints = rawWp
                            .Select(wp => new CinemachineSmoothPath.Waypoint
                            {
                                position = new Vector3(wp[0], wp[1], wp[2]),
                                roll     = 0f,
                            })
                            .ToArray();
                        EditorUtility.SetDirty(pathGo);
                        applied = true;
                    }
#elif CM3
                    var pathGo = container.transform.Find($"Path_{seqName}")?.gameObject;
                    var sc     = pathGo?.GetComponent<SplineContainer>();
                    if (sc != null)
                    {
                        Undo.RecordObject(pathGo, "MCP Update Waypoints");
                        sc.Spline.Clear();
                        foreach (var wp in rawWp)
                            sc.Spline.Add(new BezierKnot(
                                new Unity.Mathematics.float3(wp[0], wp[1], wp[2])));
                        EditorUtility.SetDirty(pathGo);
                        applied = true;
                    }
#endif
                    if (applied) updated.Add("waypoints");
                    else warnings.Add($"Path_{seqName} not found — waypoints not applied.");
                }
            }

            // Re-bake the animation curve if duration or ease values changed
            bool easeChanged = p.ContainsKey("ease_in")  || p.ContainsKey("ease_out") ||
                               p.ContainsKey("blend_in") || p.ContainsKey("blend_out");
            if (p.ContainsKey("duration") || easeChanged)
                RebakeCartAnimation(container, timeline, seqName, duration, easeIn, easeOut,
                    updated, warnings);
        }

        private static void RebakeCartAnimation(
            GameObject    container,
            TimelineAsset timeline,
            string        seqName,
            float         duration,
            float         easeIn,
            float         easeOut,
            List<string>  updated,
            List<string>  warnings)
        {
            var animTrack = GetTrack<AnimationTrack>(timeline);
            if (animTrack == null)
            {
                warnings.Add("No AnimationTrack found — cart animation not updated.");
                return;
            }

            foreach (var trackClip in animTrack.GetClips())
            {
                if (trackClip.asset is not AnimationPlayableAsset apa || apa.clip == null)
                    continue;

                trackClip.start    = 0.0;
                trackClip.duration = duration;

                var curve = BuildEasedCurve(duration, easeIn, easeOut);
#if CM2
                apa.clip.SetCurve("", typeof(CinemachineDollyCart), "m_Position", curve);
#elif CM3
                apa.clip.SetCurve("", typeof(CinemachineSplineCart), "m_SplinePosition", curve);
#endif
                EditorUtility.SetDirty(apa.clip);
            }

            if (!updated.Contains("cart_animation")) updated.Add("cart_animation");
        }

        // ------------------------------------------------------------------ //
        //  ArrivalFlythrough — look_at_position → move LookTarget GO         //
        // ------------------------------------------------------------------ //

        private static void UpdateArrivalLookTarget(
            GameObject   container,
            string       seqName,
            JObject      p,
            List<string> updated)
        {
            var rawPos = p["look_at_position"]?.ToObject<float[]>();
            if (rawPos == null || rawPos.Length < 3) return;

            var lookTargetGo = container.transform.Find($"LookTarget_{seqName}")?.gameObject;
            if (lookTargetGo == null) return;

            Undo.RecordObject(lookTargetGo.transform, "MCP Update LookTarget");
            lookTargetGo.transform.position = new Vector3(rawPos[0], rawPos[1], rawPos[2]);
            EditorUtility.SetDirty(lookTargetGo);
            updated.Add("look_at_position");
        }

        // ------------------------------------------------------------------ //
        //  FocusCut — camera_offset → reposition VCam relative to target     //
        // ------------------------------------------------------------------ //

        private static void UpdateFocusCut(
            GameObject   container,
            string       seqName,
            JObject      p,
            List<string> updated,
            List<string> warnings)
        {
            var rawOffset = p["camera_offset"]?.ToObject<float[]>();
            if (rawOffset == null || rawOffset.Length < 3) return;

            var vcamGo = container.transform.Find($"VCam_Focus_{seqName}")?.gameObject;
            if (vcamGo == null)
            {
                warnings.Add($"VCam_Focus_{seqName} not found — camera_offset not applied.");
                return;
            }

            // Find the look-at target from the virtual camera
            Vector3 targetPos = Vector3.zero;
            bool    foundTarget = false;

#if CM2
            var vcam = vcamGo.GetComponent<CinemachineVirtualCamera>();
            if (vcam?.m_LookAt != null)
            { targetPos = vcam.m_LookAt.position; foundTarget = true; }
#elif CM3
            var vcam = vcamGo.GetComponent<CinemachineCamera>();
            if (vcam?.Target.LookAtTarget != null)
            { targetPos = vcam.Target.LookAtTarget.position; foundTarget = true; }
#endif
            if (!foundTarget)
            {
                warnings.Add(
                    "Could not find the focus target from the VCam component. " +
                    "VCam positioned using offset from world origin instead.");
            }

            Undo.RecordObject(vcamGo.transform, "MCP Update Camera Offset");
            vcamGo.transform.position = targetPos +
                new Vector3(rawOffset[0], rawOffset[1], rawOffset[2]);
            EditorUtility.SetDirty(vcamGo);
            updated.Add("camera_offset");
        }

        // ------------------------------------------------------------------ //
        //  Shared utilities                                                   //
        // ------------------------------------------------------------------ //

        private static T GetTrack<T>(TimelineAsset timeline) where T : TrackAsset =>
            timeline.GetRootTracks().OfType<T>().FirstOrDefault();

        /// <summary>
        /// 4-keyframe eased curve: flat at start, linear in middle, flat at end.
        /// easeIn and easeOut are fractions of duration (0–1).
        /// </summary>
        private static AnimationCurve BuildEasedCurve(float duration, float easeIn, float easeOut)
        {
            float t1       = easeIn  * duration;
            float t2       = (1f - easeOut) * duration;
            float midSlope = 1f / Mathf.Max(0.001f, t2 - t1);
            return new AnimationCurve(
                new Keyframe(0f,       0f,           0f,        midSlope),
                new Keyframe(t1,       easeIn,       midSlope,  midSlope),
                new Keyframe(t2,       1f - easeOut, midSlope,  0f),
                new Keyframe(duration, 1f,           0f,        0f));
        }
    }
}
