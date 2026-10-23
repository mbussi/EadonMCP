using UnityEngine;

namespace LevelScaffold
{
    /// <summary>
    /// Marks a named spatial reference point inside a ScaffoldZone.
    ///
    /// Anchor GameObjects are created automatically by ScaffoldAnchorGenerator
    /// when a zone is created or refreshed, and can also be added manually
    /// via the MCP create_reference_point tool.
    ///
    /// The hierarchy path of an anchor (e.g. /ForgottenTunnels/Zone1/step_05)
    /// is what the MCP tools use to reference positions for marker placement.
    /// </summary>
    [AddComponentMenu("Level Scaffold/Scaffold Anchor")]
    public class ScaffoldAnchor : MonoBehaviour
    {
        [Tooltip("Controls gizmo shape and colour in the scene view.")]
        public ScaffoldAnchorType AnchorType = ScaffoldAnchorType.Named;

        [Tooltip("Unique label within this zone. Should match the GameObject name.\n" +
                 "Examples: _entry, _mid, step_05, _left_wall_mid")]
        public string AnchorLabel = string.Empty;

        void Awake()
        {
            gameObject.hideFlags = HideFlags.DontSaveInBuild;
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------ //
        //  Gizmo colours (matching the spec palette)                          //
        // ------------------------------------------------------------------ //

        static readonly Color EntryColor      = new Color(0.20f, 0.80f, 0.30f, 1.0f);  // green
        static readonly Color StepColor       = new Color(0.50f, 0.50f, 0.50f, 0.70f); // grey
        static readonly Color WallRefColor    = new Color(0.30f, 0.60f, 1.00f, 0.80f); // blue
        static readonly Color ConnectionColor = new Color(0.30f, 0.90f, 0.90f, 1.00f); // cyan
        static readonly Color NamedColor      = new Color(1.00f, 1.00f, 1.00f, 0.90f); // white

        // ------------------------------------------------------------------ //
        //  Gizmo drawing                                                      //
        // ------------------------------------------------------------------ //

        void OnDrawGizmos()
        {
            if (!ScaffoldGizmoState.ShowAnchors) return;

            var col = GetColor();
            var pos = transform.position;
            Gizmos.color = col;

            switch (AnchorType)
            {
                case ScaffoldAnchorType.Entry:
                    Gizmos.DrawSphere(pos, 0.06f);
                    DrawArrow(pos, transform.forward, col, 0.25f);
                    break;

                case ScaffoldAnchorType.Exit:
                    Gizmos.DrawSphere(pos, 0.06f);
                    DrawArrow(pos, -transform.forward, col, 0.25f);
                    break;

                case ScaffoldAnchorType.Step:
                    Gizmos.DrawSphere(pos, 0.03f);
                    break;

                case ScaffoldAnchorType.WallRef:
                    // Tick mark: vertical line + small cap
                    Gizmos.DrawLine(pos, pos + transform.up * 0.18f);
                    Gizmos.DrawSphere(pos + transform.up * 0.18f, 0.025f);
                    break;

                case ScaffoldAnchorType.Connection:
                    DrawDiamond(pos, col, 0.09f);
                    break;

                default: // Named
                    Gizmos.DrawSphere(pos, 0.05f);
                    break;
            }

            // Label: shown for all anchor types except Step (too many to label)
            if (ScaffoldGizmoState.ShowLabels && AnchorType != ScaffoldAnchorType.Step)
                UnityEditor.Handles.Label(pos + Vector3.up * 0.14f, AnchorLabel);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetColor();
            col.a = 0.5f;
            Gizmos.color = col;
            Gizmos.DrawWireSphere(transform.position, 0.10f);
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.22f,
                $"{AnchorType}: {AnchorLabel}");
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        private Color GetColor() => AnchorType switch
        {
            ScaffoldAnchorType.Entry      => EntryColor,
            ScaffoldAnchorType.Exit       => EntryColor,
            ScaffoldAnchorType.Step       => StepColor,
            ScaffoldAnchorType.WallRef    => WallRefColor,
            ScaffoldAnchorType.Connection => ConnectionColor,
            _                             => NamedColor,
        };

        private static void DrawArrow(Vector3 origin, Vector3 direction, Color color, float length)
        {
            direction = direction.normalized;
            var tip   = origin + direction * length;

            Gizmos.color = color;
            Gizmos.DrawLine(origin, tip);

            // Arrow head: two angled lines in screen-space-ish right direction
            var right = Vector3.Cross(direction, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            float headLen = length * 0.35f;
            float headOut = length * 0.15f;
            Gizmos.DrawLine(tip, tip - direction * headLen + right  * headOut);
            Gizmos.DrawLine(tip, tip - direction * headLen - right  * headOut);
        }

        private static void DrawDiamond(Vector3 center, Color color, float size)
        {
            Gizmos.color = color;
            var up    = Vector3.up    * size;
            var right = Vector3.right * size;
            Gizmos.DrawLine(center + up,    center + right);
            Gizmos.DrawLine(center + right, center - up);
            Gizmos.DrawLine(center - up,    center - right);
            Gizmos.DrawLine(center - right, center + up);
            // Depth cross
            var fwd = Vector3.forward * size;
            Gizmos.DrawLine(center + fwd, center - fwd);
        }
#endif
    }
}
