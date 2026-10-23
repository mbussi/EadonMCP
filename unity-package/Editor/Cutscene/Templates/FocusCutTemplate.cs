using System;
using System.Collections.Generic;
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
#endif

namespace UnityMcpBridge.Editor.Cutscene.Templates
{
    /// <summary>
    /// FocusCut — quick dramatic cut to a target object, hold, then blend back.
    ///
    /// Scene objects created:
    ///   VCam_Focus_{name}   Stationary VCam at (target + camera_offset), looking at target
    ///   PlayableDirector    bound to .playable asset
    ///
    /// Timeline tracks:
    ///   CinemachineTrack    full duration (blend_in + hold + blend_out) with ease in/out
    ///   ActivationTrack     same
    ///   SignalTrack         "focus_held" fires at t=blend_in (camera is fully on the target)
    /// </summary>
    internal static class FocusCutTemplate
    {
        internal static McpResponse Execute(CreateSequenceRequest req)
        {
#if !CM3
            return McpResponse.Fail("CINEMACHINE_NOT_FOUND", "Cinemachine 3 is required.");
#else
            var p            = req.Params;
            // var targetPath   = p?["target_path"]?.Value<string>();
            // var rawOffset    = p?["camera_offset"]?.ToObject<float[]>() ?? new[] { 0f, 1.2f, -3.0f };
            // var holdDuration = p?["duration"]?.Value<float>()           ?? 3f;
            // var blendIn      = p?["blend_in"]?.Value<float>()           ?? 0.3f;
            // var blendOut     = p?["blend_out"]?.Value<float>()          ?? 0.5f;
            var targetPath   = (string)p?["target_path"];
            var rawOffset    = p?["camera_offset"]?.ToObject<float[]>() ?? new[] { 0f, 1.2f, -3.0f };  // unchanged
            var holdDuration = (float?)p?["duration"]  ?? 3f;
            var blendIn      = (float?)p?["blend_in"]  ?? 0.3f;
            var blendOut     = (float?)p?["blend_out"] ?? 0.5f;

            if (string.IsNullOrWhiteSpace(targetPath))
                return McpResponse.Fail("INVALID_PARAMS", "params.target_path is required.");
            if (holdDuration <= 0f)
                return McpResponse.Fail("INVALID_PARAMS", "params.duration must be > 0.");

            var targetGo = PlacementHandler.FindByPath(targetPath);
            if (targetGo == null)
                return McpResponse.Fail("OBJECT_NOT_FOUND",
                    $"No object at target_path '{targetPath}'. " +
                    "Use get_scene_hierarchy to find the correct path.");

            var cameraOffset = new Vector3(
                rawOffset.Length > 0 ? rawOffset[0] : 0f,
                rawOffset.Length > 1 ? rawOffset[1] : 1.2f,
                rawOffset.Length > 2 ? rawOffset[2] : -3.0f);

            var cameraPos   = targetGo.transform.position + cameraOffset;
            float totalDuration = blendIn + holdDuration + blendOut;

            var warnings     = new List<string>();
            var container    = CutsceneTemplateBase.EnsureSequenceContainer(req.SequenceName);
            var timelinePath = CutsceneTemplateBase.ResolveTimelinePath(req.TimelinePath, req.SequenceName);
            CutsceneTemplateBase.RecordSettings(container, "FocusCut");

            Undo.SetCurrentGroupName($"MCP Create {req.SequenceName}");
            int undoGroup = Undo.GetCurrentGroup();

            string vcamPath;
            string[] signalsCreated;

#if CM3
            Build_CM3(req.SequenceName, container, cameraPos, targetGo,
                totalDuration, blendIn, blendOut, timelinePath,
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
            Vector3      cameraPos,
            GameObject   targetGo,
            float        totalDuration,
            float        blendIn,
            float        blendOut,
            string       timelinePath,
            out string   vcamPath,
            out string[] signalsCreated,
            List<string> warnings)
        {
            var vcamGo = CreateChild(container, $"VCam_Focus_{name}");
            vcamGo.transform.position = cameraPos;
            vcamGo.SetActive(false);

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.LookAtTarget = targetGo.transform;
            vcam.Priority            = 11;

            vcamPath = PlacementHandler.GetPath(vcamGo);

            var timeline  = CutsceneTemplateBase.CreateTimelineAsset(timelinePath);
            var cineTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            var cineClip  = cineTrack.CreateDefaultClip();
            cineClip.start           = 0.0;
            cineClip.duration        = totalDuration;
            cineClip.easeInDuration  = blendIn;
            cineClip.easeOutDuration = blendOut;

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

            AddActivationTrack(timeline, director, vcamGo, totalDuration);
            AddSignalTrack(timeline, blendIn);
            signalsCreated = new[] { "focus_held" };

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

        private static void AddSignalTrack(TimelineAsset timeline, float blendIn)
        {
            var signalTrack  = timeline.CreateTrack<SignalTrack>(null, "Events");
            var emitFocused  = signalTrack.CreateMarker<SignalEmitter>(blendIn);
            emitFocused.asset = CutsceneTemplateBase.GetOrCreateSignalAsset("focus_held");
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
