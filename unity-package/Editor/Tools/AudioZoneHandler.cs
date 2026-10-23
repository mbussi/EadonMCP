using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/create_audio_zone
    ///
    /// Creates either:
    ///   reverb_zone    — a GameObject with an AudioReverbZone component using a named
    ///                    preset (cave, forest, dungeon, interior, exterior, etc.)
    ///   ambient_source — a GameObject with a fully-3D AudioSource and a SphereCollider
    ///                    trigger (for scene-view visualisation only)
    ///
    /// Both types use only built-in Unity audio APIs — no optional package required.
    /// </summary>
    public static class AudioZoneHandler
    {
        // -----------------------------------------------------------------------
        // Preset mapping
        // -----------------------------------------------------------------------

        private static readonly Dictionary<string, AudioReverbPreset> PresetMap =
            new Dictionary<string, AudioReverbPreset>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "cave",       AudioReverbPreset.Cave },
                { "forest",     AudioReverbPreset.Forest },
                { "dungeon",    AudioReverbPreset.StoneCorridor },
                { "interior",   AudioReverbPreset.Room },
                { "exterior",   AudioReverbPreset.Plain },
                { "underwater", AudioReverbPreset.Underwater },
                { "cathedral",  AudioReverbPreset.Concerthall },
                { "alley",      AudioReverbPreset.Alley },
            };

        // -----------------------------------------------------------------------
        // POST /tools/world/create_audio_zone
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateAudioZoneAsync(CreateAudioZoneRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.ZoneType))
                    return McpResponse.Fail("INVALID_PARAMS", "zone_type must not be empty.");

                if (req.Position == null || req.Position.Length < 3)
                    return McpResponse.Fail("INVALID_PARAMS", "position must be a [x, y, z] array.");

                var warnings = new List<string>();
                var position = new Vector3(req.Position[0], req.Position[1], req.Position[2]);

                // Resolve optional parent
                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        warnings.Add($"Parent path not found '{req.ParentPath}' — creating at scene root.");
                }

                switch (req.ZoneType.ToLowerInvariant())
                {
                    case "reverb_zone":
                        return CreateReverbZone(req, position, parent, warnings);
                    case "ambient_source":
                        return CreateAmbientSource(req, position, parent, warnings);
                    default:
                        return McpResponse.Fail("INVALID_PARAMS",
                            $"Unknown zone_type '{req.ZoneType}'. " +
                            "Valid values: reverb_zone, ambient_source.");
                }
            });

        // -----------------------------------------------------------------------
        // reverb_zone
        // -----------------------------------------------------------------------

        private static McpResponse CreateReverbZone(
            CreateAudioZoneRequest req,
            Vector3                position,
            GameObject             parent,
            List<string>           warnings)
        {
            if (string.IsNullOrWhiteSpace(req.Preset))
                return McpResponse.Fail("INVALID_PARAMS",
                    "preset is required for zone_type='reverb_zone'.");

            if (!PresetMap.TryGetValue(req.Preset, out var reverbPreset))
            {
                var available = string.Join(", ", PresetMap.Keys);
                return McpResponse.Fail("INVALID_PRESET",
                    $"Unknown preset '{req.Preset}'. Available presets: {available}");
            }

            var name = !string.IsNullOrWhiteSpace(req.Name)
                ? req.Name
                : $"AudioReverbZone_{req.Preset}";

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP Create Audio Reverb Zone");

            go.transform.position = position;
            if (parent != null)
                go.transform.SetParent(parent.transform, worldPositionStays: true);

            var zone          = Undo.AddComponent<AudioReverbZone>(go);
            zone.reverbPreset = reverbPreset;
            zone.minDistance  = req.MinDistance;
            zone.maxDistance  = req.MaxDistance;

            EditorUtility.SetDirty(go);

            return McpResponse.Ok(new AudioZoneResultData
            {
                ObjectPath  = PlacementHandler.GetPath(go),
                ZoneType    = "reverb_zone",
                Preset      = req.Preset,
                MinDistance = req.MinDistance,
                MaxDistance = req.MaxDistance,
                Warnings    = warnings.ToArray()
            });
        }

        // -----------------------------------------------------------------------
        // ambient_source
        // -----------------------------------------------------------------------

        private static McpResponse CreateAmbientSource(
            CreateAudioZoneRequest req,
            Vector3                position,
            GameObject             parent,
            List<string>           warnings)
        {
            var name = !string.IsNullOrWhiteSpace(req.Name)
                ? req.Name
                : "AmbientSource";

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP Create Ambient Audio Source");

            go.transform.position = position;
            if (parent != null)
                go.transform.SetParent(parent.transform, worldPositionStays: true);

            // AudioSource — fully 3D, linear rolloff
            var source          = Undo.AddComponent<AudioSource>(go);
            source.spatialBlend = 1.0f;                       // fully 3D (0=2D, 1=3D)
            source.rolloffMode  = AudioRolloffMode.Linear;
            source.minDistance  = req.MinDistance;
            source.maxDistance  = req.MaxDistance;
            source.volume       = Mathf.Clamp01(req.Volume      ?? 1.0f);
            source.loop         = req.Loop        ?? true;
            source.playOnAwake  = req.PlayOnAwake ?? true;

            // Load AudioClip if a path was provided
            if (!string.IsNullOrEmpty(req.AudioClipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(req.AudioClipPath);
                if (clip != null)
                    source.clip = clip;
                else
                    warnings.Add(
                        $"AudioClip not found at '{req.AudioClipPath}' — " +
                        "source created without a clip. Assign one manually in the Inspector.");
            }

            // SphereCollider at max_distance for scene-view visualisation only.
            // isTrigger=true means it has no physics impact.
            var sphere       = Undo.AddComponent<SphereCollider>(go);
            sphere.isTrigger = true;
            sphere.radius    = req.MaxDistance;

            EditorUtility.SetDirty(go);

            return McpResponse.Ok(new AudioZoneResultData
            {
                ObjectPath  = PlacementHandler.GetPath(go),
                ZoneType    = "ambient_source",
                Preset      = null,
                MinDistance = req.MinDistance,
                MaxDistance = req.MaxDistance,
                Warnings    = warnings.ToArray()
            });
        }
    }
}
