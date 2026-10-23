using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Cutscene;
using UnityMcpBridge.Editor.Tools;
#if CM3
using Unity.Cinemachine;
using UnityEngine.Splines;
#endif

namespace UnityMcpBridge.Editor.Cutscene.Templates
{
    /// <summary>
    /// AreaReveal — camera sweeps along a waypoint path to show a location.
    ///
    /// Scene objects created:
    ///   Path_{name}         SplineContainer
    ///   Cart_{name}         CinemachineSplineCart
    ///   VCam_Reveal_{name}  CinemachineCamera — follows cart, looks at target or along path
    ///   PlayableDirector    bound to .playable asset
    ///
    /// Timeline tracks:
    ///   AnimationTrack      drives Cart position 0→1 over the full duration
    ///   CinemachineTrack    activates VCam for full duration (with ease in/out)
    ///   ActivationTrack     enables/disables VCam GameObject
    ///   SignalTrack         "reveal_start" at t=0, "reveal_complete" at end
    /// </summary>
    internal static class AreaRevealTemplate
    {
        internal static McpResponse Execute(CreateSequenceRequest req)
        {
#if !CM3
            return McpResponse.Fail("CINEMACHINE_NOT_FOUND", "Cinemachine 3 is required.");
#else
            var p         = req.Params;
            // var rawWp     = p?["waypoints"]?.ToObject<float[][]>();
            // var lookPath  = p?["look_at_path"]?.Value<string>();
            // var duration  = p?["duration"]?.Value<float>()  ?? 8f;
            // var easeIn    = p?["ease_in"]?.Value<float>()   ?? 0.2f;
            // var easeOut   = p?["ease_out"]?.Value<float>()  ?? 0.2f;
            var rawWp    = p?["waypoints"]?.ToObject<float[][]>();  // unchanged
            var lookPath = (string)p?["look_at_path"];
            var duration = (float?)p?["duration"] ?? 8f;
            var easeIn   = (float?)p?["ease_in"]  ?? 0.2f;
            var easeOut  = (float?)p?["ease_out"] ?? 0.2f;

            if (rawWp == null || rawWp.Length < 2)
                return McpResponse.Fail("INVALID_PARAMS",
                    "params.waypoints must be an array of at least 2 [x,y,z] positions.");
            if (duration <= 0f)
                return McpResponse.Fail("INVALID_PARAMS", "params.duration must be > 0.");

            easeIn  = Mathf.Clamp01(easeIn);
            easeOut = Mathf.Clamp01(easeOut);
            if (easeIn + easeOut > 0.9f)
            {
                easeIn  *= 0.9f / (easeIn + easeOut);
                easeOut  = 0.9f - easeIn;
            }

            // Validate optional look-at object
            GameObject lookAtGo = null;
            if (!string.IsNullOrEmpty(lookPath))
            {
                lookAtGo = PlacementHandler.FindByPath(lookPath);
                if (lookAtGo == null)
                    return McpResponse.Fail("OBJECT_NOT_FOUND",
                        $"No object at look_at_path '{lookPath}'.");
            }

            var warnings     = new List<string>();
            var container    = CutsceneTemplateBase.EnsureSequenceContainer(req.SequenceName);
            var timelinePath = CutsceneTemplateBase.ResolveTimelinePath(req.TimelinePath, req.SequenceName);
            var settings     = CutsceneTemplateBase.RecordSettings(container, "AreaReveal");
            settings.easeIn  = easeIn;
            settings.easeOut = easeOut;

            Undo.SetCurrentGroupName($"MCP Create {req.SequenceName}");
            int undoGroup = Undo.GetCurrentGroup();

            string vcamPath;
            string[] signalsCreated;

#if CM3
            Build_CM3(req.SequenceName, container, rawWp, lookAtGo,
                duration, easeIn, easeOut, timelinePath,
                out vcamPath, out signalsCreated, warnings);
#else
            vcamPath       = string.Empty;
            signalsCreated = Array.Empty<string>();
#endif
            if (lookAtGo == null)
                warnings.Add(
                    "No look_at_path provided — camera will face along the path direction. " +
                    "Provide look_at_path for a specific point of interest.");

            EditorUtility.SetDirty(container);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(undoGroup);

            return McpResponse.Ok(new CreateSequenceResultData
            {
                SequenceName          = req.SequenceName,
                ContainerPath         = PlacementHandler.GetPath(container),
                TimelinePath          = timelinePath,
                VcamPaths             = new[] { vcamPath },
                SignalsCreated        = signalsCreated,
                CinemachineBrainFound = CutsceneTemplateBase.DetectCinemachineBrain(),
                Warnings              = warnings.ToArray(),
            });
#endif
        }

        // ------------------------------------------------------------------ //
        //  CM3                                                                //
        // ------------------------------------------------------------------ //

#if CM3
        private static void Build_CM3(
            string       name,
            GameObject   container,
            float[][]    waypoints,
            GameObject   lookAtGo,
            float        duration,
            float        easeIn,
            float        easeOut,
            string       timelinePath,
            out string   vcamPath,
            out string[] signalsCreated,
            List<string> warnings)
        {
            // SplineContainer (path at world origin so local == world)
            var splineGo = CreateChild(container, $"Path_{name}");
            splineGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var splineContainer = splineGo.AddComponent<SplineContainer>();
            splineContainer.Spline.Clear();
            foreach (var wp in waypoints)
                splineContainer.Spline.Add(new BezierKnot(
                    new Unity.Mathematics.float3(wp[0], wp[1], wp[2])));

            // CinemachineSplineCart
            var cartGo = CreateChild(container, $"Cart_{name}");
            cartGo.transform.position = new Vector3(
                waypoints[0][0], waypoints[0][1], waypoints[0][2]);
            var cart = cartGo.AddComponent<CinemachineSplineCart>();
            cart.SplineSettings.Spline    = splineContainer;
            cart.SplineSettings.Position  = 0f;
            cart.SplineSettings.Units     = PathIndexUnit.Normalized;
            cartGo.AddComponent<Animator>();

            // VCam
            var vcamGo = CreateChild(container, $"VCam_Reveal_{name}");
            vcamGo.SetActive(false);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = cartGo.transform;
            vcam.Priority              = 11;

            if (lookAtGo != null)
                vcam.Target.LookAtTarget = lookAtGo.transform;
            // If null: camera faces along cart direction (no override look target)

            vcamPath = PlacementHandler.GetPath(vcamGo);

            // Timeline
            var timeline = CutsceneTemplateBase.CreateTimelineAsset(timelinePath);
            AddCartAnimationTrack_CM3(timeline, cartGo, name, duration, easeIn, easeOut);

            var cineTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            var cineClip  = cineTrack.CreateDefaultClip();
            cineClip.start           = 0.0;
            cineClip.duration        = duration;
            // ease_in / ease_out are fractions of the duration; the clip's ease is in seconds.
            cineClip.easeInDuration  = easeIn  * duration;
            cineClip.easeOutDuration = easeOut * duration;

            var director = container.AddComponent<PlayableDirector>();
            Undo.RecordObject(container, "MCP Add PlayableDirector");
            director.playableAsset  = timeline;
            director.playOnAwake    = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;

            var shot = cineClip.asset as CinemachineShot;
            if (shot != null)
            {
                var refName = GUID.Generate().ToString();
                shot.VirtualCamera.exposedName = refName;
                director.SetReferenceValue(refName, vcam);
            }

            AddActivationTrack(timeline, director, vcamGo, duration);
            AddSignalTrack(timeline, "reveal_start", 0.0, "reveal_complete", duration);

            // Bind AnimationTrack to cart Animator
            // (done after director is created so we can call SetGenericBinding)
            BindCartAnimTrack_CM3(timeline, director, cartGo);

            signalsCreated = new[] { "reveal_start", "reveal_complete" };
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
        }

        private static void AddCartAnimationTrack_CM3(
            TimelineAsset timeline,
            GameObject    cartGo,
            string        name,
            float         duration,
            float         easeIn,
            float         easeOut)
        {
            var curve    = BuildEasedCurve(duration, easeIn, easeOut);
            var animClip = new AnimationClip { name = $"CartAnim_{name}" };
            // CM3 SplineCart position property (serialized field name)
            animClip.SetCurve("", typeof(CinemachineSplineCart), "m_SplinePosition", curve);
            AssetDatabase.AddObjectToAsset(animClip, timeline);

            var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Cart");
            var trackClip = animTrack.CreateDefaultClip();
            trackClip.start    = 0.0;
            trackClip.duration = duration;
            if (trackClip.asset is AnimationPlayableAsset apa)
                apa.clip = animClip;
        }

        private static void BindCartAnimTrack_CM3(
            TimelineAsset    timeline,
            PlayableDirector director,
            GameObject       cartGo)
        {
            // Find the AnimationTrack we added and bind it to the cart's Animator
            foreach (var track in timeline.GetRootTracks())
            {
                if (track is AnimationTrack at && track.name.StartsWith("Cart"))
                {
                    director.SetGenericBinding(at, cartGo.GetComponent<Animator>());
                    break;
                }
            }
        }
#endif

        // ------------------------------------------------------------------ //
        //  Shared track helpers (Timeline + Signal API) //
        // ------------------------------------------------------------------ //

        private static void AddActivationTrack(
            TimelineAsset    timeline,
            PlayableDirector director,
            GameObject       vcamGo,
            float            duration)
        {
            var activationTrack = timeline.CreateTrack<ActivationTrack>(null, "VCam Active");
            var activationClip  = activationTrack.CreateDefaultClip();
            activationClip.start    = 0.0;
            activationClip.duration = duration;
            director.SetGenericBinding(activationTrack, vcamGo);
        }

        private static void AddSignalTrack(
            TimelineAsset timeline,
            string        startSignal, double startTime,
            string        endSignal,   double endTime)
        {
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "Events");
            var emitStart   = signalTrack.CreateMarker<SignalEmitter>(startTime);
            emitStart.asset = CutsceneTemplateBase.GetOrCreateSignalAsset(startSignal);
            var emitEnd     = signalTrack.CreateMarker<SignalEmitter>(endTime);
            emitEnd.asset   = CutsceneTemplateBase.GetOrCreateSignalAsset(endSignal);
        }

        // ------------------------------------------------------------------ //
        //  Animation curve helper                                             //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Builds a curve that runs from 0 at t=0 to 1 at t=duration,
        /// with flat tangents at the ease ends and linear in the middle.
        /// ease_in and ease_out are fractions of duration (0–1).
        /// </summary>
        private static AnimationCurve BuildEasedCurve(float duration, float easeIn, float easeOut)
        {
            float t0 = 0f;
            float t1 = easeIn  * duration;
            float t2 = (1f - easeOut) * duration;
            float t3 = duration;

            // Value: position 0→1
            // Approximate: ease in and out as flat tangent at ends, linear in middle
            float midSlope = 1f / Mathf.Max(0.001f, t2 - t1);

            var curve = new AnimationCurve(
                new Keyframe(t0, 0f,       0f,        midSlope),
                new Keyframe(t1, easeIn,   midSlope,  midSlope),
                new Keyframe(t2, 1f - easeOut, midSlope, 0f),
                new Keyframe(t3, 1f,       0f,        0f)
            );
            return curve;
        }

        // ------------------------------------------------------------------ //
        //  Scene creation helpers                                             //
        // ------------------------------------------------------------------ //

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"MCP Create {name}");
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }
    }
}
