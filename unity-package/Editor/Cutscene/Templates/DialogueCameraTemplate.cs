using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
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
    /// Creates a two-shot / over-shoulder dialogue camera sequence.
    ///
    /// Scene objects created:
    ///   /Cutscenes/{name}/
    ///     VCam_Dialogue_{name}   [CinemachineCamera]          — disabled, activated by Timeline
    ///     LookTarget_{name}      [empty GameObject]          — NPC look-at anchor
    ///     PlayableDirector                                   — bound to .playable asset
    ///
    /// Timeline tracks:
    ///   CinemachineTrack  — controls the dialogue VCam for the full sequence duration
    ///   ActivationTrack   — enables/disables the VCam GameObject
    ///   SignalTrack       — fires dialogue_start (at blend-in) and dialogue_end (at blend-out)
    /// </summary>
    internal static class DialogueCameraTemplate
    {
        // ------------------------------------------------------------------ //
        //  Entry point (called from main thread via CutsceneHandler)         //
        // ------------------------------------------------------------------ //

        internal static McpResponse Execute(CreateSequenceRequest req)
        {
#if !CM3
            return McpResponse.Fail("CINEMACHINE_NOT_FOUND", "Cinemachine 3 is required.");
#else
            var warnings = new List<string>();

            // ── Parse params ────────────────────────────────────────────── //
            var p = req.Params;
            // var playerPath = p?["player_path"]?.Value<string>();
            // var npcPath    = p?["npc_path"]?.Value<string>();
            // var cameraSide = p?["camera_side"]?.Value<string>() ?? "auto";
            // var duration   = p?["duration"]?.Value<float>()     ?? 8f;
            // var blendIn    = p?["blend_in"]?.Value<float>()     ?? 0.5f;
            // var blendOut   = p?["blend_out"]?.Value<float>()    ?? 0.5f;
            // var rawOffset  = p?["look_offset"]?.ToObject<float[]>() ?? new[] { 0f, 1.6f, 0f };
            var playerPath = (string)p?["player_path"];
            var npcPath    = (string)p?["npc_path"];
            var cameraSide = (string)p?["camera_side"] ?? "auto";
            var duration   = (float?)p?["duration"]   ?? 8f;
            var blendIn    = (float?)p?["blend_in"]   ?? 0.5f;
            var blendOut   = (float?)p?["blend_out"]  ?? 0.5f;
            var rawOffset  = p?["look_offset"]?.ToObject<float[]>() ?? new[] { 0f, 1.6f, 0f };

            if (string.IsNullOrWhiteSpace(playerPath))
                return McpResponse.Fail("INVALID_PARAMS", "params.player_path is required.");
            if (string.IsNullOrWhiteSpace(npcPath))
                return McpResponse.Fail("INVALID_PARAMS", "params.npc_path is required.");
            if (duration <= 0f)
                return McpResponse.Fail("INVALID_PARAMS", "params.duration must be greater than 0.");

            // Clamp blends so they don't exceed duration
            blendIn  = Mathf.Min(blendIn,  duration * 0.4f);
            blendOut = Mathf.Min(blendOut, duration * 0.4f);

            // ── Find scene objects ───────────────────────────────────────── //
            var playerGo = PlacementHandler.FindByPath(playerPath);
            if (playerGo == null)
                return McpResponse.Fail("OBJECT_NOT_FOUND",
                    $"No object at player_path '{playerPath}'. " +
                    "Use get_scene_hierarchy to find the correct path.");

            var npcGo = PlacementHandler.FindByPath(npcPath);
            if (npcGo == null)
                return McpResponse.Fail("OBJECT_NOT_FOUND",
                    $"No object at npc_path '{npcPath}'. " +
                    "Use get_scene_hierarchy to find the correct path.");

            // ── Camera position ──────────────────────────────────────────── //
            var lookOffset = new Vector3(
                rawOffset.Length > 0 ? rawOffset[0] : 0f,
                rawOffset.Length > 1 ? rawOffset[1] : 1.6f,
                rawOffset.Length > 2 ? rawOffset[2] : 0f);

            var cameraPos  = ComputeCameraPosition(
                playerGo.transform.position,
                npcGo.transform.position,
                cameraSide, lookOffset.y, out var resolvedSide);

            var lookAtPos  = npcGo.transform.position + lookOffset;

            if (cameraSide == "auto")
                warnings.Add($"camera_side 'auto' resolved to '{resolvedSide}'.");

            // ── Begin Undo group ─────────────────────────────────────────── //
            Undo.SetCurrentGroupName($"MCP Create {req.SequenceName}");
            int undoGroup = Undo.GetCurrentGroup();

            // ── Scene structure ──────────────────────────────────────────── //
            var container  = CutsceneTemplateBase.EnsureSequenceContainer(req.SequenceName);
            var settings   = CutsceneTemplateBase.RecordSettings(container, "DialogueCamera");
            settings.player     = playerGo.transform;
            settings.npc        = npcGo.transform;
            settings.cameraSide = resolvedSide;
            settings.lookOffset = lookOffset;
            var timelinePath = CutsceneTemplateBase.ResolveTimelinePath(
                req.TimelinePath, req.SequenceName);

            // Look target
            var lookTargetGo = new GameObject($"LookTarget_{req.SequenceName}");
            Undo.RegisterCreatedObjectUndo(lookTargetGo, "MCP Create LookTarget");
            lookTargetGo.transform.SetParent(container.transform, worldPositionStays: false);
            lookTargetGo.transform.position = lookAtPos;

            // ── Template-specific camera and Timeline ─────────────────────── //
            string vcamPath;
            string[] signalsCreated;
#if CM3
            Build_CM3(req.SequenceName, container, cameraPos, lookTargetGo.transform,
                      duration, blendIn, blendOut, timelinePath,
                      out vcamPath, out signalsCreated, warnings);
#else
            vcamPath = string.Empty;
            signalsCreated = Array.Empty<string>();
#endif

            // ── Finish ──────────────────────────────────────────────────── //
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
        //  CM3 implementation                                                //
        // ------------------------------------------------------------------ //

#if CM3
        private static void Build_CM3(
            string       sequenceName,
            GameObject   container,
            Vector3      cameraPos,
            Transform    lookTarget,
            float        duration,
            float        blendIn,
            float        blendOut,
            string       timelinePath,
            out string   vcamPath,
            out string[] signalsCreated,
            List<string> warnings)
        {
            // ── Virtual camera ───────────────────────────────────────────── //
            var vcamGo = new GameObject($"VCam_Dialogue_{sequenceName}");
            Undo.RegisterCreatedObjectUndo(vcamGo, "MCP Create VCam");
            vcamGo.transform.SetParent(container.transform, worldPositionStays: false);
            vcamGo.transform.position = cameraPos;
            vcamGo.SetActive(false);           // ActivationTrack will enable it

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.LookAtTarget = lookTarget; // aim at NPC; no follow (stationary cam)
            vcam.Priority            = 11;         // higher than a typical follow cam (10)

            vcamPath = PlacementHandler.GetPath(vcamGo);

            // ── Timeline asset ────────────────────────────────────────────── //
            var timeline = CutsceneTemplateBase.CreateTimelineAsset(timelinePath);

            var cineTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            var cineClip  = cineTrack.CreateDefaultClip();
            cineClip.start           = 0.0;
            cineClip.duration        = duration;
            cineClip.easeInDuration  = blendIn;
            cineClip.easeOutDuration = blendOut;

            var activationTrack = timeline.CreateTrack<ActivationTrack>(null, "VCam Active");
            var activationClip  = activationTrack.CreateDefaultClip();
            activationClip.start    = 0.0;
            activationClip.duration = duration;

            var signalTrack  = timeline.CreateTrack<SignalTrack>(null, "Events");
            var sigStart     = CutsceneTemplateBase.GetOrCreateSignalAsset("dialogue_start");
            var sigEnd       = CutsceneTemplateBase.GetOrCreateSignalAsset("dialogue_end");
            var emitterStart = signalTrack.CreateMarker<SignalEmitter>(blendIn);
            emitterStart.asset = sigStart;
            var emitterEnd   = signalTrack.CreateMarker<SignalEmitter>(duration - blendOut);
            emitterEnd.asset   = sigEnd;

            signalsCreated = new[] { "dialogue_start", "dialogue_end" };

            // ── PlayableDirector ─────────────────────────────────────────── //
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
            else
            {
                warnings.Add(
                    "Could not bind VCam to CinemachineTrack clip (CinemachineShot asset was null). " +
                    "Open the Timeline asset and drag the virtual camera onto the Cinemachine clip manually.");
            }

            director.SetGenericBinding(activationTrack, vcamGo);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
        }
#endif

        // ------------------------------------------------------------------ //
        //  Camera position calculation                                       //
        // ------------------------------------------------------------------ //

        internal static Vector3 ComputeCameraPosition(
            Vector3  playerPos,
            Vector3  npcPos,
            string   cameraSide,
            float    heightAbove,
            out string resolvedSide)
        {
            const float LateralDist = 2.5f;

            var forward = npcPos - playerPos;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            var right = Vector3.Cross(forward, Vector3.up).normalized;

            // Resolve "auto" using the current Scene view camera
            resolvedSide = cameraSide;
            if (cameraSide == "auto")
            {
                var sceneViewCam = UnityEditor.SceneView.lastActiveSceneView?.camera;
                if (sceneViewCam != null)
                {
                    float dot = Vector3.Dot(right,
                        sceneViewCam.transform.position - npcPos);
                    resolvedSide = dot >= 0f ? "right" : "left";
                }
                else
                {
                    resolvedSide = "right";
                }
            }

            return resolvedSide switch
            {
                "left"  => npcPos - right * LateralDist + Vector3.up * heightAbove,
                "front" => npcPos + forward * LateralDist + Vector3.up * heightAbove,
                _       => npcPos + right * LateralDist + Vector3.up * heightAbove, // "right"
            };
        }
    }
}
