using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/create_reflection_probe
    ///
    /// Creates a ReflectionProbe component at a world position with configurable
    /// influence bounds, mode, resolution, and importance. The probe is registered
    /// with Unity's Undo system and parented optionally.
    ///
    /// In open-world levels, reflection probes are placed by the dozen — one per
    /// interior room, one per covered archway, one per puddle or metallic surface —
    /// and the setup is always the same. This tool eliminates per-probe Inspector work.
    ///
    /// Resolutions not in the valid set (powers of 2, 16–2048) are clamped to the
    /// nearest valid value rather than failing, since "close enough" is almost always
    /// fine for probe placement.
    /// </summary>
    public static class ReflectionProbeHandler
    {
        private static readonly int[] ValidResolutions = { 16, 32, 64, 128, 256, 512, 1024, 2048 };

        // -----------------------------------------------------------------------
        // POST /tools/world/create_reflection_probe
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateReflectionProbeAsync(
            CreateReflectionProbeRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Position == null || req.Position.Length < 3)
                    return McpResponse.Fail("INVALID_PARAMS",
                        "position must be a [x, y, z] array.");

                if (!TryParseMode(req.Mode, out var probeMode))
                    return McpResponse.Fail("INVALID_MODE",
                        $"Unknown mode '{req.Mode}'. Valid values: baked, realtime, custom.");

                // Snap resolution to nearest valid power-of-2 value
                int resolution = SnapResolution(req.Resolution > 0 ? req.Resolution : 128);

                // Resolve optional parent
                GameObject parent = null;
                if (!string.IsNullOrEmpty(req.ParentPath))
                {
                    parent = PlacementHandler.FindByPath(req.ParentPath);
                    if (parent == null)
                        return McpResponse.Fail("PARENT_NOT_FOUND",
                            $"No object at path '{req.ParentPath}'.");
                }

                var name = string.IsNullOrWhiteSpace(req.Name) ? "ReflectionProbe" : req.Name;
                var go   = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "MCP Create Reflection Probe");

                go.transform.position = new Vector3(
                    req.Position[0], req.Position[1], req.Position[2]);

                if (parent != null)
                    go.transform.SetParent(parent.transform, worldPositionStays: true);

                var probe        = Undo.AddComponent<ReflectionProbe>(go);
                probe.mode       = probeMode;
                probe.importance = req.Importance > 0 ? req.Importance : 1;
                probe.intensity  = req.Intensity ?? 1.0f;
                probe.hdr        = req.Hdr       ?? true;
                probe.resolution = resolution;

                // Influence box — default [10, 5, 10] if not specified
                if (req.Size != null && req.Size.Length >= 3)
                    probe.size = new Vector3(req.Size[0], req.Size[1], req.Size[2]);
                else
                    probe.size = new Vector3(10f, 5f, 10f);

                EditorUtility.SetDirty(go);

                return McpResponse.Ok(new CreateReflectionProbeResultData
                {
                    ObjectPath = PlacementHandler.GetPath(go),
                    Mode       = probeMode.ToString().ToLowerInvariant(),
                    Importance = probe.importance,
                    Size       = new[] { probe.size.x, probe.size.y, probe.size.z },
                    Resolution = resolution
                });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool TryParseMode(string mode, out ReflectionProbeMode result)
        {
            switch (mode?.ToLowerInvariant())
            {
                case "realtime": result = ReflectionProbeMode.Realtime; return true;
                case "custom":   result = ReflectionProbeMode.Custom;   return true;
                case "baked":
                case null:
                case "":         result = ReflectionProbeMode.Baked;    return true;
                default:         result = ReflectionProbeMode.Baked;    return false;
            }
        }

        private static int SnapResolution(int requested) =>
            ValidResolutions.OrderBy(r => Math.Abs(r - requested)).First();
    }
}
