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
    /// ArrivalFlythrough — camera sweeps along a multi-waypoint path while always
    /// facing a fixed world-space point (the area's visual centre).
    ///
    /// Scene objects created:
    ///   Path_{name}             CinemachineSmoothPath / SplineContainer
    ///   Cart_{name}             CinemachineDollyCart  / CinemachineSplineCart
    ///   LookTarget_{name}       empty GO at look_at_position
    ///   VCam_Flythrough_{name}  VCam following cart, looking at LookTarget
    ///   PlayableDirector
    ///
    /// Timeline tracks:
    ///   AnimationTrack    drives Cart position 0→1 over duration
    ///   CinemachineTrack  full duration with ease in/out
    ///   ActivationTrack
    ///   SignalTrack       "arrival_start" at t=0, "arrival_complete" at end
    /// </summary>
    internal static class ArrivalFlythroughTemplate
    {
        internal static McpResponse Execute(CreateSequenceRequest req)
        {
#if !CM3
            return McpResponse.Fail("CINEMACHINE_NOT_FOUND", "Cinemachine 3 is required.");
#else
            var p          = req.Params;
            // var rawWp      = p?["waypoints"]?.ToObject<float[][]>();
            // var rawLookAt  = p?["look_at_position"]?.ToObject<float[]>();
            // var duration   = p?["duration"]?.Value<float>()  ?? 12f;
            // var easeIn     = p?["ease_in"]?.Value<float>()   ?? 0.15f;
            // var easeOut    = p?["ease_out"]?.Value<float>()  ?? 0.25f;
            var rawWp     = p?["waypoints"]?.ToObject<float[][]>();       // unchanged
            var rawLookAt = p?["look_at_position"]?.ToObject<float[]>();  // unchanged
            var duration  = (float?)p?["duration"] ?? 12f;
            var easeIn    = (float?)p?["ease_in"]  ?? 0.15f;
            var easeOut   = (float?)p?["ease_out"] ?? 0.25f;

            if (rawWp == null || rawWp.Length < 3)
                return McpResponse.Fail("INVALID_PARAMS",
                    "params.waypoints must be an array of at least 3 [x,y,z] positions " +
                    "for a compelling arc.");
            if (rawLookAt == null || rawLookAt.Length < 3)
                return McpResponse.Fail("INVALID_PARAMS",
                    "params.look_at_position ([x,y,z]) is required — " +
                    "provide the area's visual centre of mass.");
            if (duration <= 0f)
                return McpResponse.Fail("INVALID_PARAMS", "params.duration must be > 0.");

            easeIn  = Mathf.Clamp01(easeIn);
            easeOut = Mathf.Clamp01(easeOut);
            if (easeIn + easeOut > 0.9f)
            {
                easeIn  *= 0.9f / (easeIn + easeOut);
                easeOut  = 0.9f - easeIn;
            }

            var lookAtPos = new Vector3(rawLookAt[0], rawLookAt[1], rawLookAt[2]);

            var warnings     = new List<string>();
            var container    = CutsceneTemplateBase.EnsureSequenceContainer(req.SequenceName);
            var timelinePath = CutsceneTemplateBase.ResolveTimelinePath(req.TimelinePath, req.SequenceName);
            var settings     = CutsceneTemplateBase.RecordSettings(container, "ArrivalFlythrough");
            settings.easeIn  = easeIn;
            settings.easeOut = easeOut;

            Undo.SetCurrentGroupName($"MCP Create {req.SequenceName}");
            int undoGroup = Undo.GetCurrentGroup();

            string vcamPath;
            string[] signalsCreated;

#if CM3
            Build_CM3(req.SequenceName, container, rawWp, lookAtPos,
                duration, easeIn, easeOut, timelinePath,
                out vcamPath, out signalsCreated, warnings);
#else
            vcamPath       = string.Empty;
            signalsCreated = Array.Empty<string>();
#endif
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
            Vector3      lookAtPos,
            float        duration,
            float        easeIn,
            float        easeOut,
            string       timelinePath,
            out string   vcamPath,
            out string[] signalsCreated,
            List<string> warnings)
        {
            var splineGo = CreateChild(container, $"Path_{name}");
            splineGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var splineContainer = splineGo.AddComponent<SplineContainer>();
            splineContainer.Spline.Clear();
            foreach (var wp in waypoints)
                splineContainer.Spline.Add(new BezierKnot(
                    new Unity.Mathematics.float3(wp[0], wp[1], wp[2])));

            var cartGo = CreateChild(container, $"Cart_{name}");
            cartGo.transform.position = new Vector3(
                waypoints[0][0], waypoints[0][1], waypoints[0][2]);
            var cart = cartGo.AddComponent<CinemachineSplineCart>();
            cart.SplineSettings.Spline   = splineContainer;
            cart.SplineSettings.Position = 0f;
            cart.SplineSettings.Units    = PathIndexUnit.Normalized;
            cartGo.AddComponent<Animator>();

            var lookTargetGo = CreateChild(container, $"LookTarget_{name}");
            lookTargetGo.transform.position = lookAtPos;

            var vcamGo = CreateChild(container, $"VCam_Flythrough_{name}");
            vcamGo.SetActive(false);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = cartGo.transform;
            vcam.Target.LookAtTarget   = lookTargetGo.transform;
            vcam.Priority              = 11;

            vcamPath = PlacementHandler.GetPath(vcamGo);

            var timeline = CutsceneTemplateBase.CreateTimelineAsset(timelinePath);

            var curve    = BuildEasedCurve(duration, easeIn, easeOut);
            var animClip = new AnimationClip { name = $"CartAnim_{name}" };
            animClip.SetCurve("", typeof(CinemachineSplineCart), "m_SplinePosition", curve);
            AssetDatabase.AddObjectToAsset(animClip, timeline);

            var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Cart");
            var animTC    = animTrack.CreateDefaultClip();
            animTC.start    = 0.0;
            animTC.duration = duration;
            if (animTC.asset is AnimationPlayableAsset apa) apa.clip = animClip;

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

            director.SetGenericBinding(animTrack, cartGo.GetComponent<Animator>());
            AddActivationTrack(timeline, director, vcamGo, duration);
            AddSignalTrack(timeline, duration);
            signalsCreated = new[] { "arrival_start", "arrival_complete" };

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
        }
#endif

        // ------------------------------------------------------------------ //
        //  Shared helpers                                                     //
        // ------------------------------------------------------------------ //

        private static void AddActivationTrack(
            TimelineAsset timeline, PlayableDirector director,
            GameObject vcamGo, float duration)
        {
            var track = timeline.CreateTrack<ActivationTrack>(null, "VCam Active");
            var clip  = track.CreateDefaultClip();
            clip.start    = 0.0;
            clip.duration = duration;
            director.SetGenericBinding(track, vcamGo);
        }

        private static void AddSignalTrack(TimelineAsset timeline, float duration)
        {
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "Events");
            var emitStart   = signalTrack.CreateMarker<SignalEmitter>(0.0);
            emitStart.asset = CutsceneTemplateBase.GetOrCreateSignalAsset("arrival_start");
            var emitEnd     = signalTrack.CreateMarker<SignalEmitter>(duration);
            emitEnd.asset   = CutsceneTemplateBase.GetOrCreateSignalAsset("arrival_complete");
        }

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

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"MCP Create {name}");
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }
    }
}
