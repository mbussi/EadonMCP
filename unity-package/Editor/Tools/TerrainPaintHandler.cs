using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/paint_terrain_layer
    ///
    /// Paints a terrain texture layer at a world position using a circular brush.
    /// Supports three falloff curves (smooth, linear, constant) and two blend modes
    /// (add, set). Correctly normalises all layer weights after painting so the
    /// alphamap sums to 1.0 at every affected pixel.
    ///
    /// Companion to TerrainQueryHandler: query reads the terrain, this writes it.
    /// The most natural usage is immediately after executing a dress_area proposal —
    /// paint the terrain layer that matches the props that were placed.
    /// </summary>
    public static class TerrainPaintHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/world/paint_terrain_layer
        // -----------------------------------------------------------------------

        public static Task<McpResponse> PaintTerrainLayerAsync(PaintTerrainLayerRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Position == null || req.Position.Length < 3)
                    return McpResponse.Fail("INVALID_PARAMS", "position must be a [x, y, z] array.");
                if (string.IsNullOrWhiteSpace(req.LayerName))
                    return McpResponse.Fail("INVALID_PARAMS", "layer_name must not be empty.");
                if (req.Radius <= 0f)
                    return McpResponse.Fail("INVALID_PARAMS", "radius must be greater than 0.");

                var worldPos = new Vector3(req.Position[0], req.Position[1], req.Position[2]);
                var warnings = new List<string>();

                // ── Find the target terrain ───────────────────────────────────
                Terrain terrain;
                if (!string.IsNullOrEmpty(req.TerrainName))
                {
                    terrain = FindTerrainByName(req.TerrainName);
                    if (terrain == null)
                        return McpResponse.Fail("TERRAIN_NOT_FOUND",
                            $"No active terrain named '{req.TerrainName}'. " +
                            $"Available: {string.Join(", ", GetActiveTerrainNames())}");
                }
                else
                {
                    terrain = FindTerrainAt(worldPos);
                    if (terrain == null)
                        return McpResponse.Fail("NO_TERRAIN",
                            $"No terrain tile covers position [{worldPos.x:F1}, {worldPos.z:F1}]. " +
                            "Use terrain_name to target a tile explicitly.");
                }

                var data   = terrain.terrainData;
                var origin = terrain.transform.position;

                // ── Resolve layer by name ─────────────────────────────────────
                int targetLayer = -1;
                var layerNames  = new List<string>(data.terrainLayers.Length);
                for (int i = 0; i < data.terrainLayers.Length; i++)
                {
                    layerNames.Add(data.terrainLayers[i].name);
                    if (string.Equals(data.terrainLayers[i].name, req.LayerName,
                                      System.StringComparison.OrdinalIgnoreCase))
                        targetLayer = i;
                }

                if (targetLayer == -1)
                    return McpResponse.Fail("LAYER_NOT_FOUND",
                        $"Terrain layer '{req.LayerName}' not found on '{terrain.name}'. " +
                        $"Available layers: {string.Join(", ", layerNames)}");

                // ── Convert world brush to alphamap pixel space ───────────────
                // UV: normalised position on this tile (0–1)
                float u = Mathf.Clamp01((worldPos.x - origin.x) / data.size.x);
                float v = Mathf.Clamp01((worldPos.z - origin.z) / data.size.z);

                // Centre pixel in alphamap coordinates
                int centerX = Mathf.RoundToInt(u * data.alphamapWidth);
                int centerZ = Mathf.RoundToInt(v * data.alphamapHeight);

                // Radius in pixels (handle non-square alphamaps separately per axis)
                int radiusX = Mathf.CeilToInt(req.Radius / data.size.x * data.alphamapWidth);
                int radiusZ = Mathf.CeilToInt(req.Radius / data.size.z * data.alphamapHeight);

                // Clamp region to alphamap bounds
                int x0 = Mathf.Max(0, centerX - radiusX);
                int z0 = Mathf.Max(0, centerZ - radiusZ);
                int x1 = Mathf.Min(data.alphamapWidth  - 1, centerX + radiusX);
                int z1 = Mathf.Min(data.alphamapHeight - 1, centerZ + radiusZ);

                int w = x1 - x0 + 1;
                int h = z1 - z0 + 1;

                if (w <= 0 || h <= 0)
                    return McpResponse.Fail("OUT_OF_BOUNDS",
                        "The brush position is entirely outside this terrain tile's bounds.");

                // ── Read, paint, normalise, write ─────────────────────────────
                // GetAlphamaps(x, y, w, h): x=column (world X), y=row (world Z)
                // Returns float[height, width, layerCount]
                var alphamaps = data.GetAlphamaps(x0, z0, w, h);

                float   opacity   = Mathf.Clamp01(req.Opacity);
                string  falloff   = req.Falloff   ?? "smooth";
                string  blendMode = req.BlendMode ?? "add";
                int     layerCount= data.terrainLayers.Length;
                int     affected  = 0;

                for (int px = 0; px < w; px++)
                for (int pz = 0; pz < h; pz++)
                {
                    // Normalised elliptical distance (1.0 at brush edge in world space)
                    float dx   = (float)(x0 + px - centerX) / radiusX;
                    float dz   = (float)(z0 + pz - centerZ) / radiusZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);

                    if (dist > 1f) continue;

                    float str = opacity * ComputeFalloff(dist, falloff);

                    if (blendMode == "set")
                        alphamaps[pz, px, targetLayer] = str;
                    else // "add"
                        alphamaps[pz, px, targetLayer] =
                            Mathf.Clamp01(alphamaps[pz, px, targetLayer] + str);

                    NormalizeAlphamaps(alphamaps, pz, px, targetLayer, layerCount);
                    affected++;
                }

                // Undo.RecordObject must be called before SetAlphamaps to be effective
                Undo.RecordObject(data, "MCP Paint Terrain Layer");
                data.SetAlphamaps(x0, z0, alphamaps);
                EditorUtility.SetDirty(data);

                return McpResponse.Ok(new PaintTerrainLayerResultData
                {
                    TerrainName    = terrain.name,
                    LayerPainted   = data.terrainLayers[targetLayer].name,
                    LayerIndex     = targetLayer,
                    PixelsAffected = affected,
                    Warnings       = warnings.ToArray()
                });
            });

        // -----------------------------------------------------------------------
        // Brush helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns a 0–1 multiplier for the given normalised brush distance.
        /// dist=0 is the centre, dist=1 is the edge.
        /// </summary>
        private static float ComputeFalloff(float dist, string mode)
        {
            switch (mode?.ToLowerInvariant())
            {
                case "constant": return 1f;
                case "linear":   return 1f - dist;
                case "smooth":
                default:
                    // Smoothstep: ease-in/ease-out between centre and edge
                    float t = 1f - dist;
                    return t * t * (3f - 2f * t);
            }
        }

        /// <summary>
        /// Renormalises all layers at pixel [z, x] so they sum to 1.0,
        /// keeping the target layer at its new painted value and scaling
        /// all other layers proportionally.
        /// </summary>
        private static void NormalizeAlphamaps(
            float[,,] maps, int z, int x, int targetLayer, int layerCount)
        {
            float targetVal = Mathf.Clamp01(maps[z, x, targetLayer]);
            float otherSum  = 0f;

            for (int i = 0; i < layerCount; i++)
                if (i != targetLayer) otherSum += Mathf.Max(0f, maps[z, x, i]);

            maps[z, x, targetLayer] = targetVal;

            float remainder = 1f - targetVal;

            if (otherSum < 1e-5f)
            {
                // All other layers are zero — distribute remainder evenly
                float each = layerCount > 1 ? remainder / (layerCount - 1) : 0f;
                for (int i = 0; i < layerCount; i++)
                    if (i != targetLayer) maps[z, x, i] = each;
            }
            else
            {
                float scale = remainder / otherSum;
                for (int i = 0; i < layerCount; i++)
                    if (i != targetLayer)
                        maps[z, x, i] = Mathf.Clamp01(maps[z, x, i] * scale);
            }
        }

        // -----------------------------------------------------------------------
        // Terrain discovery helpers
        // -----------------------------------------------------------------------

        private static Terrain FindTerrainAt(Vector3 worldPos)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                var origin = terrain.transform.position;
                var size   = terrain.terrainData.size;
                if (worldPos.x >= origin.x && worldPos.x <= origin.x + size.x &&
                    worldPos.z >= origin.z && worldPos.z <= origin.z + size.z)
                    return terrain;
            }
            return null;
        }

        private static Terrain FindTerrainByName(string name)
        {
            foreach (var terrain in Terrain.activeTerrains)
                if (string.Equals(terrain.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return terrain;
            return null;
        }

        private static string[] GetActiveTerrainNames()
        {
            var names = new string[Terrain.activeTerrains.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = Terrain.activeTerrains[i].name;
            return names;
        }
    }
}
