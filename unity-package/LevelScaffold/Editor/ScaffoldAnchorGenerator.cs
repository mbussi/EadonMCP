using UnityEditor;
using UnityEngine;

namespace LevelScaffold.Editor
{
    /// <summary>
    /// Generates ScaffoldAnchor child GameObjects for a ScaffoldZone.
    ///
    /// The generator is deterministic — the same zone type and dimensions always
    /// produce the same named anchors at the same local positions. All operations
    /// are registered with Unity's Undo system.
    ///
    /// Called from:
    ///   - ScaffoldZoneEditor.Refresh Anchors button (interactive)
    ///   - MCP ScaffoldHandler.CreateZoneAsync (agent-driven, Phase 2)
    /// </summary>
    public static class ScaffoldAnchorGenerator
    {
        /// <summary>
        /// Destroys all existing ScaffoldAnchor children and regenerates them from
        /// the zone's current ZoneType and Dimensions.
        /// Custom zones are skipped — they have no auto-generated anchors.
        /// </summary>
        public static void GenerateAnchors(ScaffoldZone zone)
        {
            if (zone == null) return;

            Undo.SetCurrentGroupName($"Refresh Anchors — {zone.gameObject.name}");
            int undoGroup = Undo.GetCurrentGroup();

            // Remove existing anchors
            foreach (var anchor in zone.GetComponentsInChildren<ScaffoldAnchor>())
                Undo.DestroyObjectImmediate(anchor.gameObject);

            switch (zone.ZoneType)
            {
                case ScaffoldZoneType.SpiralStaircase:  GenerateSpiralStaircase(zone); break;
                case ScaffoldZoneType.StraightCorridor: GenerateStraightCorridor(zone); break;
                case ScaffoldZoneType.CircularChamber:  GenerateCircularChamber(zone); break;
                case ScaffoldZoneType.RectangularRoom:  GenerateRectangularRoom(zone); break;
                case ScaffoldZoneType.CrawlTunnel:      GenerateCrawlTunnel(zone); break;
                case ScaffoldZoneType.OpenArea:         GenerateOpenArea(zone); break;
                case ScaffoldZoneType.Custom:           break; // designer adds anchors manually
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(zone.gameObject);
        }

        // ------------------------------------------------------------------ //
        //  SpiralStaircase                                                    //
        //                                                                     //
        //  Origin = top of staircase. Descends on local -Y.                  //
        //  Anchors: _entry (top), step_NN (all steps), _mid, _exit (bottom)  //
        // ------------------------------------------------------------------ //

        private static void GenerateSpiralStaircase(ScaffoldZone zone)
        {
            var d       = zone.Dimensions;
            int   steps = Mathf.Max(1, d.StepCount);
            float r     = d.Width * 0.5f;
            float angInc= d.Turns * 2f * Mathf.PI / steps;

            // Entry at top-of-staircase local origin
            CreateAnchor(zone, "_entry", ScaffoldAnchorType.Entry, Vector3.zero,
                Quaternion.LookRotation(Vector3.forward));

            // All individual step anchors (zero-padded two-digit index)
            for (int i = 1; i <= steps; i++)
            {
                float angle = i * angInc;
                float y     = -(i * d.RiserHeight);
                var   pos   = new Vector3(Mathf.Sin(angle) * r, y, Mathf.Cos(angle) * r);
                CreateAnchor(zone, $"step_{i:D2}", ScaffoldAnchorType.Step, pos);
            }

            // _mid — step nearest the halfway point
            int midIdx = Mathf.Max(1, steps / 2);
            {
                float angle = midIdx * angInc;
                float y     = -(midIdx * d.RiserHeight);
                var   pos   = new Vector3(Mathf.Sin(angle) * r, y, Mathf.Cos(angle) * r);
                CreateAnchor(zone, "_mid", ScaffoldAnchorType.Named, pos);
            }

            // Exit at the bottom step
            {
                float angle = steps * angInc;
                float y     = -(steps * d.RiserHeight);
                var   pos   = new Vector3(Mathf.Sin(angle) * r, y, Mathf.Cos(angle) * r);
                CreateAnchor(zone, "_exit", ScaffoldAnchorType.Exit, pos,
                    Quaternion.LookRotation(-Vector3.forward));
            }
        }

        // ------------------------------------------------------------------ //
        //  StraightCorridor                                                   //
        //                                                                     //
        //  Origin = entry face. Extends forward (+Z) by Depth.               //
        // ------------------------------------------------------------------ //

        private static void GenerateStraightCorridor(ScaffoldZone zone)
        {
            var d = zone.Dimensions;
            float l = d.Depth;
            float w = d.Width;
            float h = d.Height;

            CreateAnchor(zone, "_entry",          ScaffoldAnchorType.Entry,   V(0,       0,       0    ), Fwd());
            CreateAnchor(zone, "_exit",           ScaffoldAnchorType.Exit,    V(0,       0,       l    ), Fwd());
            CreateAnchor(zone, "_mid",            ScaffoldAnchorType.Named,   V(0,       0,       l*0.5f));
            CreateAnchor(zone, "_quarter",        ScaffoldAnchorType.Named,   V(0,       0,       l*0.25f));
            CreateAnchor(zone, "_three_quarter",  ScaffoldAnchorType.Named,   V(0,       0,       l*0.75f));
            CreateAnchor(zone, "_left_wall_mid",  ScaffoldAnchorType.WallRef, V(-w*0.5f, h*0.5f, l*0.5f));
            CreateAnchor(zone, "_right_wall_mid", ScaffoldAnchorType.WallRef, V( w*0.5f, h*0.5f, l*0.5f));
        }

        // ------------------------------------------------------------------ //
        //  CircularChamber                                                    //
        //                                                                     //
        //  Origin = centre of chamber at floor level.                        //
        //  Entry default = south perimeter; Exit = north perimeter.          //
        // ------------------------------------------------------------------ //

        private static void GenerateCircularChamber(ScaffoldZone zone)
        {
            var d = zone.Dimensions;
            float r = d.EffectiveRadius;

            CreateAnchor(zone, "_entry",   ScaffoldAnchorType.Entry,  V(0,  0, -r), Fwd(-Vector3.forward)); // south, facing in
            CreateAnchor(zone, "_exit",    ScaffoldAnchorType.Exit,   V(0,  0,  r), Fwd( Vector3.forward)); // north, facing out
            CreateAnchor(zone, "_center",  ScaffoldAnchorType.Named,  Vector3.zero);
            CreateAnchor(zone, "_far_end", ScaffoldAnchorType.Named,  V(0,  0,  r));                        // diametrically opposite entry
            CreateAnchor(zone, "_north",   ScaffoldAnchorType.Named,  V(0,  0,  r));
            CreateAnchor(zone, "_south",   ScaffoldAnchorType.Named,  V(0,  0, -r));
            CreateAnchor(zone, "_east",    ScaffoldAnchorType.Named,  V( r, 0,  0));
            CreateAnchor(zone, "_west",    ScaffoldAnchorType.Named,  V(-r, 0,  0));

            // 8 evenly-spaced perimeter points, 0° = north (+Z), clockwise
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                var   pos   = new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r);
                CreateAnchor(zone, $"_perimeter_{i:D2}", ScaffoldAnchorType.Named, pos);
            }
        }

        // ------------------------------------------------------------------ //
        //  RectangularRoom                                                    //
        //                                                                     //
        //  Origin = south face centre at floor level.                        //
        // ------------------------------------------------------------------ //

        private static void GenerateRectangularRoom(ScaffoldZone zone)
        {
            var d  = zone.Dimensions;
            float hw = d.Width * 0.5f;
            float l  = d.Depth;
            float hl = l * 0.5f;

            CreateAnchor(zone, "_entry",          ScaffoldAnchorType.Entry,   V(0,    0,  0    ), Fwd());
            CreateAnchor(zone, "_exit",           ScaffoldAnchorType.Exit,    V(0,    0,  l    ), Fwd());
            CreateAnchor(zone, "_center",         ScaffoldAnchorType.Named,   V(0,    0,  hl   ));
            CreateAnchor(zone, "_corner_sw",      ScaffoldAnchorType.Named,   V(-hw,  0,  0    ));
            CreateAnchor(zone, "_corner_se",      ScaffoldAnchorType.Named,   V( hw,  0,  0    ));
            CreateAnchor(zone, "_corner_nw",      ScaffoldAnchorType.Named,   V(-hw,  0,  l    ));
            CreateAnchor(zone, "_corner_ne",      ScaffoldAnchorType.Named,   V( hw,  0,  l    ));
            CreateAnchor(zone, "_wall_south_mid", ScaffoldAnchorType.WallRef, V(0,    0,  0    ));
            CreateAnchor(zone, "_wall_north_mid", ScaffoldAnchorType.WallRef, V(0,    0,  l    ));
            CreateAnchor(zone, "_wall_east_mid",  ScaffoldAnchorType.WallRef, V( hw,  0,  hl   ));
            CreateAnchor(zone, "_wall_west_mid",  ScaffoldAnchorType.WallRef, V(-hw,  0,  hl   ));
        }

        // ------------------------------------------------------------------ //
        //  CrawlTunnel                                                        //
        // ------------------------------------------------------------------ //

        private static void GenerateCrawlTunnel(ScaffoldZone zone)
        {
            float l = zone.Dimensions.Depth;
            CreateAnchor(zone, "_entry", ScaffoldAnchorType.Entry, V(0, 0, 0   ), Fwd());
            CreateAnchor(zone, "_mid",   ScaffoldAnchorType.Named, V(0, 0, l*0.5f));
            CreateAnchor(zone, "_exit",  ScaffoldAnchorType.Exit,  V(0, 0, l   ), Fwd());
        }

        // ------------------------------------------------------------------ //
        //  OpenArea                                                           //
        // ------------------------------------------------------------------ //

        private static void GenerateOpenArea(ScaffoldZone zone)
        {
            var d = zone.Dimensions;

            if (d.Radius > 0f)
            {
                float r   = d.Radius;
                float d45 = r * 0.7071f;
                CreateAnchor(zone, "_center",     ScaffoldAnchorType.Named, Vector3.zero);
                CreateAnchor(zone, "_edge_north", ScaffoldAnchorType.Named, V(0,    0,  r  ));
                CreateAnchor(zone, "_edge_south", ScaffoldAnchorType.Named, V(0,    0, -r  ));
                CreateAnchor(zone, "_edge_east",  ScaffoldAnchorType.Named, V( r,   0,  0  ));
                CreateAnchor(zone, "_edge_west",  ScaffoldAnchorType.Named, V(-r,   0,  0  ));
                CreateAnchor(zone, "_edge_ne",    ScaffoldAnchorType.Named, V( d45, 0,  d45));
                CreateAnchor(zone, "_edge_nw",    ScaffoldAnchorType.Named, V(-d45, 0,  d45));
                CreateAnchor(zone, "_edge_se",    ScaffoldAnchorType.Named, V( d45, 0, -d45));
                CreateAnchor(zone, "_edge_sw",    ScaffoldAnchorType.Named, V(-d45, 0, -d45));
            }
            else
            {
                float hw = d.Width * 0.5f, l = d.Depth, hl = l * 0.5f;
                CreateAnchor(zone, "_center",     ScaffoldAnchorType.Named, V(0,    0,  hl ));
                CreateAnchor(zone, "_edge_north", ScaffoldAnchorType.Named, V(0,    0,  l  ));
                CreateAnchor(zone, "_edge_south", ScaffoldAnchorType.Named, V(0,    0,  0  ));
                CreateAnchor(zone, "_edge_east",  ScaffoldAnchorType.Named, V( hw,  0,  hl ));
                CreateAnchor(zone, "_edge_west",  ScaffoldAnchorType.Named, V(-hw,  0,  hl ));
                CreateAnchor(zone, "_edge_ne",    ScaffoldAnchorType.Named, V( hw,  0,  l  ));
                CreateAnchor(zone, "_edge_nw",    ScaffoldAnchorType.Named, V(-hw,  0,  l  ));
                CreateAnchor(zone, "_edge_se",    ScaffoldAnchorType.Named, V( hw,  0,  0  ));
                CreateAnchor(zone, "_edge_sw",    ScaffoldAnchorType.Named, V(-hw,  0,  0  ));
            }
        }

        // ------------------------------------------------------------------ //
        //  Factory                                                            //
        // ------------------------------------------------------------------ //

        private static void CreateAnchor(
            ScaffoldZone       zone,
            string             label,
            ScaffoldAnchorType type,
            Vector3            localPosition,
            Quaternion         localRotation = default)
        {
            var go = new GameObject(label);
            Undo.RegisterCreatedObjectUndo(go, $"Create {label}");

            go.transform.SetParent(zone.transform, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation == default ? Quaternion.identity : localRotation;
            go.hideFlags = HideFlags.DontSaveInBuild;

            var anchor = go.AddComponent<ScaffoldAnchor>();
            anchor.AnchorType  = type;
            anchor.AnchorLabel = label;
        }

        // ------------------------------------------------------------------ //
        //  Shorthand helpers                                                  //
        // ------------------------------------------------------------------ //

        static Vector3    V(float x, float y, float z) => new Vector3(x, y, z);
        static Quaternion Fwd(Vector3? dir = null)      => Quaternion.LookRotation(dir ?? Vector3.forward);
    }
}
