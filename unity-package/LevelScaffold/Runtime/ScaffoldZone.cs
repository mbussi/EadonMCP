using UnityEngine;

namespace LevelScaffold
{
    /// <summary>
    /// Root component for a scaffold zone. Stores the zone definition, draws the
    /// volume gizmo in the scene view, and acts as the parent container for all
    /// ScaffoldAnchor and GameplayMarker children.
    ///
    /// Anchors are created automatically by ScaffoldAnchorGenerator (Editor) via the
    /// Refresh Anchors button or the MCP create_zone_scaffold tool. ScaffoldZone itself
    /// does not generate anchors — this keeps the runtime component free of Editor dependencies.
    /// </summary>
    [AddComponentMenu("Eadon/MCP Bridge/Level Scaffolding/Scaffold Zone")]
    public class ScaffoldZone : MonoBehaviour
    {
        [Tooltip("Determines anchor generation pattern, gizmo shape, and gizmo colour.")]
        public ScaffoldZoneType ZoneType = ScaffoldZoneType.StraightCorridor;

        [Tooltip("Type-specific dimensions. Unused fields for the selected type are ignored.")]
        public ZoneDimensions Dimensions;

        [Tooltip("Verbatim excerpt from the design document. " +
                 "Preserved permanently as the authoritative record of design intent for this zone. " +
                 "Displayed prominently at the top of the inspector.")]
        [TextArea(3, 8)]
        public string DesignNote = string.Empty;

        [Tooltip("Semantic tags for ambient environmental dressing, e.g. [\"dungeon\", \"damp\", \"medieval\"]. " +
                 "Queried against PrefabMetadata.tags[] during the zone-dressing step of the bake workflow.")]
        public string[] DressingTags = new string[0];

        [Tooltip("Adjacent zones connected via the link_zones MCP tool. Populated automatically.")]
        public ScaffoldZone[] ConnectedZones = new ScaffoldZone[0];

        void Awake()
        {
            gameObject.hideFlags = HideFlags.DontSaveInBuild;
        }

        /// <summary>Called when the zone type changes in the inspector to reset dimensions.</summary>
        public void ResetDimensionsToDefaults()
        {
            Dimensions = ZoneDimensions.DefaultsFor(ZoneType);
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------ //
        //  Zone type colour palette (matching SCAFFOLD_SYSTEM.md spec)        //
        // ------------------------------------------------------------------ //

        static readonly Color StaircaseColor  = new Color(0.30f, 0.60f, 1.00f, 1.0f); // blue
        static readonly Color CorridorColor   = new Color(1.00f, 0.70f, 0.10f, 1.0f); // amber
        static readonly Color ChamberColor    = new Color(0.70f, 0.30f, 0.90f, 1.0f); // purple
        static readonly Color OpenAreaColor   = new Color(0.30f, 0.90f, 0.30f, 1.0f); // green
        static readonly Color ConnectionColor = new Color(0.60f, 0.60f, 0.60f, 0.8f); // grey

        // ------------------------------------------------------------------ //
        //  OnDrawGizmos — always visible                                      //
        // ------------------------------------------------------------------ //

        void OnDrawGizmos()
        {
            if (!ScaffoldGizmoState.ShowZones) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color  = GetZoneColor();
            DrawVolumeWireframe();
            Gizmos.matrix = Matrix4x4.identity;

            if (ScaffoldGizmoState.ShowLabels)
                UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, gameObject.name);
        }

        // ------------------------------------------------------------------ //
        //  OnDrawGizmosSelected — semi-transparent fill + dimension labels    //
        // ------------------------------------------------------------------ //

        void OnDrawGizmosSelected()
        {
            var col = GetZoneColor();
            col.a = 0.07f;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color  = col;
            DrawVolumeFilled();
            Gizmos.matrix = Matrix4x4.identity;
            DrawDimensionLabel();
        }

        // ------------------------------------------------------------------ //
        //  Volume shapes                                                      //
        // ------------------------------------------------------------------ //

        private void DrawVolumeWireframe()
        {
            var d = Dimensions;
            switch (ZoneType)
            {
                case ScaffoldZoneType.StraightCorridor:
                case ScaffoldZoneType.CrawlTunnel:
                case ScaffoldZoneType.RectangularRoom:
                {
                    // Origin is entry face. Volume extends forward (local +Z) by Depth.
                    var size   = new Vector3(d.Width, d.Height, d.Depth);
                    var centre = new Vector3(0f, d.Height * 0.5f, d.Depth * 0.5f);
                    Gizmos.DrawWireCube(centre, size);
                    break;
                }

                case ScaffoldZoneType.CircularChamber:
                {
                    // Centred on origin at floor level. Top circle + bottom circle + pillars.
                    float r = d.EffectiveRadius;
                    DrawWireCircle(Vector3.zero,                   r, 24);
                    DrawWireCircle(new Vector3(0f, d.Height, 0f), r, 24);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI * 0.25f;
                        var edge = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                        Gizmos.DrawLine(edge, edge + Vector3.up * d.Height);
                    }
                    break;
                }

                case ScaffoldZoneType.SpiralStaircase:
                {
                    // Origin is top. Descends on local -Y.
                    float totalH = d.StaircaseTotalHeight;
                    float r      = d.Width * 0.5f;
                    DrawWireCircle(Vector3.zero,                    r, 16);
                    DrawWireCircle(new Vector3(0f, -totalH, 0f),   r, 16);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        var edge = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                        Gizmos.DrawLine(edge, edge - Vector3.up * totalH);
                    }
                    // Spiral path (clamped to 60 segments for perf)
                    int   segs   = Mathf.Min(d.StepCount, 60);
                    float angInc = d.Turns * 2f * Mathf.PI / Mathf.Max(1, segs);
                    float yInc   = totalH / Mathf.Max(1, segs);
                    var   prev   = new Vector3(Mathf.Sin(0f) * r, 0f, Mathf.Cos(0f) * r);
                    for (int i = 1; i <= segs; i++)
                    {
                        float angle = i * angInc;
                        var   cur   = new Vector3(Mathf.Sin(angle) * r, -i * yInc, Mathf.Cos(angle) * r);
                        Gizmos.DrawLine(prev, cur);
                        prev = cur;
                    }
                    break;
                }

                case ScaffoldZoneType.OpenArea:
                {
                    if (d.Radius > 0f)
                        DrawWireCircle(Vector3.zero, d.Radius, 24);
                    else
                    {
                        var size   = new Vector3(d.Width, 0.05f, d.Depth);
                        var centre = new Vector3(0f, 0f, d.Depth * 0.5f);
                        Gizmos.DrawWireCube(centre, size);
                    }
                    break;
                }

                case ScaffoldZoneType.Custom:
                    Gizmos.DrawWireSphere(Vector3.zero, 0.25f);
                    break;
            }
        }

        private void DrawVolumeFilled()
        {
            var d = Dimensions;
            switch (ZoneType)
            {
                case ScaffoldZoneType.StraightCorridor:
                case ScaffoldZoneType.CrawlTunnel:
                case ScaffoldZoneType.RectangularRoom:
                    Gizmos.DrawCube(
                        new Vector3(0f, d.Height * 0.5f, d.Depth * 0.5f),
                        new Vector3(d.Width, d.Height, d.Depth));
                    break;

                case ScaffoldZoneType.CircularChamber:
                    Gizmos.DrawSphere(new Vector3(0f, d.Height * 0.5f, 0f), d.EffectiveRadius);
                    break;

                case ScaffoldZoneType.SpiralStaircase:
                    Gizmos.DrawSphere(
                        new Vector3(0f, -d.StaircaseTotalHeight * 0.5f, 0f),
                        d.Width * 0.5f);
                    break;
            }
        }

        private void DrawDimensionLabel()
        {
            var d = Dimensions;
            string text = ZoneType switch
            {
                ScaffoldZoneType.SpiralStaircase  =>
                    $"{d.StepCount} steps · {d.Width:F1}m wide · {d.Turns:F1} turns · h{d.StaircaseTotalHeight:F1}m",
                ScaffoldZoneType.CircularChamber  =>
                    $"Ø{d.Diameter:F1}m · h{d.Height:F1}m",
                ScaffoldZoneType.OpenArea when d.Radius > 0f =>
                    $"r{d.Radius:F1}m",
                ScaffoldZoneType.Custom =>
                    "(custom zone)",
                _ =>
                    $"{d.Width:F1} × {d.Depth:F1} × {d.Height:F1}m",
            };

            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.55f,
                text,
                UnityEditor.EditorStyles.miniLabel);
        }

        private static void DrawWireCircle(Vector3 centre, float radius, int segments)
        {
            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = a0 + step;
                var p0 = centre + new Vector3(Mathf.Sin(a0) * radius, 0f, Mathf.Cos(a0) * radius);
                var p1 = centre + new Vector3(Mathf.Sin(a1) * radius, 0f, Mathf.Cos(a1) * radius);
                Gizmos.DrawLine(p0, p1);
            }
        }

        private Color GetZoneColor() => ZoneType switch
        {
            ScaffoldZoneType.SpiralStaircase  => StaircaseColor,
            ScaffoldZoneType.StraightCorridor => CorridorColor,
            ScaffoldZoneType.CrawlTunnel      => CorridorColor,
            ScaffoldZoneType.CircularChamber  => ChamberColor,
            ScaffoldZoneType.RectangularRoom  => ChamberColor,
            ScaffoldZoneType.OpenArea         => OpenAreaColor,
            _                                 => ConnectionColor,
        };
#endif
    }
}
