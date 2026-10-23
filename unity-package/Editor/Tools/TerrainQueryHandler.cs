using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/world/query_terrain
    ///
    /// Samples one or more world-space positions against active Unity Terrain tiles,
    /// returning height, surface normal, slope angle, and terrain layer weights for
    /// each position. Supports multi-tile open-world setups: the handler finds which
    /// tile covers each point independently.
    ///
    /// Positions outside all active terrain tiles receive error="NO_TERRAIN" and null
    /// fields; the rest of the batch continues normally.
    ///
    /// All sampling runs in a single main-thread dispatch, making bulk queries fast
    /// regardless of batch size.
    /// </summary>
    public static class TerrainQueryHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/world/query_terrain
        // -----------------------------------------------------------------------

        public static Task<McpResponse> QueryTerrainAsync(QueryTerrainRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (req?.Positions == null || req.Positions.Length == 0)
                    return McpResponse.Fail("INVALID_PARAMS", "positions must not be empty.");

                var results = new List<TerrainSampleData>(req.Positions.Length);

                foreach (var pos in req.Positions)
                {
                    if (pos == null || pos.Length < 3)
                    {
                        results.Add(new TerrainSampleData
                        {
                            Position = pos,
                            Error    = "INVALID_POSITION"
                        });
                        continue;
                    }

                    var worldPos = new Vector3(pos[0], pos[1], pos[2]);
                    var terrain  = FindTerrainAt(worldPos);

                    if (terrain == null)
                    {
                        results.Add(new TerrainSampleData
                        {
                            Position = new[] { pos[0], pos[1], pos[2] },
                            Error    = "NO_TERRAIN"
                        });
                        continue;
                    }

                    var data   = terrain.terrainData;
                    var origin = terrain.transform.position;

                    // Normalised UV coordinates on this tile (clamped to [0,1])
                    var u = Mathf.Clamp01((worldPos.x - origin.x) / data.size.x);
                    var v = Mathf.Clamp01((worldPos.z - origin.z) / data.size.z);

                    // Surface height in world space
                    var height = terrain.SampleHeight(worldPos);

                    // Interpolated surface normal (world space)
                    var normal = data.GetInterpolatedNormal(u, v);

                    // Steepness in degrees (0 = flat, 90 = vertical cliff)
                    var slope = data.GetSteepness(u, v);

                    // Per-layer alpha weights (optional — skip on high-res terrain when not needed)
                    TerrainLayerWeightData[] layerWeights = null;
                    if (req.IncludeLayerWeights &&
                        data.terrainLayers != null &&
                        data.terrainLayers.Length > 0)
                    {
                        // Alphamap uses pixel coordinates, not normalised UV
                        var alphaX    = Mathf.Clamp((int)(u * data.alphamapWidth),  0, data.alphamapWidth  - 1);
                        var alphaZ    = Mathf.Clamp((int)(v * data.alphamapHeight), 0, data.alphamapHeight - 1);
                        // GetAlphamaps returns [z, x, layerIndex] — note the axis order
                        var alphamaps = data.GetAlphamaps(alphaX, alphaZ, 1, 1);

                        layerWeights = new TerrainLayerWeightData[data.terrainLayers.Length];
                        for (int i = 0; i < data.terrainLayers.Length; i++)
                        {
                            layerWeights[i] = new TerrainLayerWeightData
                            {
                                LayerName = data.terrainLayers[i].name,
                                Weight    = alphamaps[0, 0, i]
                            };
                        }
                    }

                    results.Add(new TerrainSampleData
                    {
                        Position      = new[] { pos[0], pos[1], pos[2] },
                        SurfaceHeight = height,
                        Normal        = new[] { normal.x, normal.y, normal.z },
                        SlopeDegrees  = slope,
                        TerrainName   = terrain.name,
                        LayerWeights  = layerWeights,
                        Error         = null
                    });
                }

                return McpResponse.Ok(new TerrainQueryResultData { Results = results.ToArray() });
            });

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the first active Terrain tile whose XZ bounds contain worldPos.
        /// Open-world terrain is typically split into tiles; this finds the right one.
        /// </summary>
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
    }
}
