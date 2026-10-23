using UnityEngine;

namespace LevelScaffold
{
    /// <summary>
    /// Records a single piece of functional intent at a known position in a ScaffoldZone.
    ///
    /// A GameplayMarker does NOT reference any specific prefab. The connection to actual
    /// assets is made exclusively during the bake step, when the MCP BakeHandler matches
    /// SemanticTags against PrefabMetadata.tags[].
    ///
    /// ConfigJson stores marker-type-specific data (enemy stats, trap conditions, loot tables)
    /// as a JSON string. This data is preserved as design intent and consumed by your game
    /// systems at runtime, or by the bake handler when it instantiates and configures prefabs.
    /// </summary>
    [AddComponentMenu("Eadon/MCP Bridge/Level Scaffolding/Gameplay Marker")]
    public class GameplayMarker : MonoBehaviour
    {
        [Tooltip("Type of functional intent this marker represents.")]
        public GameplayMarkerType MarkerType = GameplayMarkerType.Custom;

        [Tooltip("Unique label from the design document.\n" +
                 "Examples: AncientDrowner_Mid, SqueakyStep_05, Loot_Skeleton_Bottom")]
        public string MarkerLabel = string.Empty;

        [Tooltip("Tags matched against PrefabMetadata.tags[] during the bake resolve step.\n" +
                 "Examples: [\"enemy\", \"drowner\", \"undead\", \"sound_sensitive\"]")]
        public string[] SemanticTags = new string[0];

        [Tooltip("World-space radius of the detection/trigger sphere drawn in the scene view.\n" +
                 "Use 0 to suppress the radius sphere.")]
        [Min(0f)]
        public float DetectionRadius = 0f;

        [Tooltip("JSON object with marker-type-specific configuration.\n\n" +
                 "EnemySpawn:    { enemy_type, health, damage, detection_type, behavior }\n" +
                 "Trap:          { trap_type, alert_radius, trigger_condition, fixable, fix_item }\n" +
                 "LootContainer: { container_type, loot_items[] }\n" +
                 "Interaction:   { interaction_type, requirement, reward, state_key }")]
        [TextArea(3, 10)]
        public string ConfigJson = string.Empty;

        [Tooltip("Current state in the bake workflow. Updated by the MCP BakeHandler.")]
        public BakeState BakeState = BakeState.Pending;

        [Tooltip("Populated by the resolve step with the chosen prefab GUID. Read-only at runtime.")]
        public string ResolvedPrefabGuid = string.Empty;

        void Awake()
        {
            gameObject.hideFlags = HideFlags.DontSaveInBuild;
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------ //
        //  Bake-state colours                                                 //
        // ------------------------------------------------------------------ //

        static readonly Color PendingColor  = new Color(1.0f, 0.50f, 0.00f, 1.0f); // orange
        static readonly Color ResolvedColor = new Color(1.0f, 0.90f, 0.10f, 1.0f); // yellow
        static readonly Color BakedColor    = new Color(0.1f, 0.80f, 0.70f, 0.4f); // teal, dimmed

        // ------------------------------------------------------------------ //
        //  Gizmo drawing                                                      //
        // ------------------------------------------------------------------ //

        void OnDrawGizmos()
        {
            if (!ScaffoldGizmoState.ShowMarkers) return;

            var col = GetStateColor();
            var pos = transform.position;
            Gizmos.color = col;

            DrawTypeIcon(pos, col);

            // Detection radius
            if (ScaffoldGizmoState.ShowRadii && DetectionRadius > 0f)
            {
                var fill = col;
                fill.a = 0.07f;
                Gizmos.color = fill;
                Gizmos.DrawSphere(pos, DetectionRadius);

                var wire = col;
                wire.a = 0.35f;
                Gizmos.color = wire;
                Gizmos.DrawWireSphere(pos, DetectionRadius);
            }

            // Label
            if (ScaffoldGizmoState.ShowLabels)
                UnityEditor.Handles.Label(pos + Vector3.up * 0.17f, MarkerLabel);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetStateColor();
            col.a = 0.5f;
            Gizmos.color = col;
            Gizmos.DrawWireSphere(transform.position, 0.13f);

            if (!string.IsNullOrEmpty(MarkerLabel))
                UnityEditor.Handles.Label(
                    transform.position + Vector3.up * 0.25f,
                    $"[{BakeState}] {MarkerLabel}");
        }

        // ------------------------------------------------------------------ //
        //  Type-specific icon shapes                                          //
        // ------------------------------------------------------------------ //

        private void DrawTypeIcon(Vector3 pos, Color col)
        {
            switch (MarkerType)
            {
                case GameplayMarkerType.EnemySpawn:
                    // Solid sphere — enemies are bold
                    Gizmos.DrawSphere(pos, 0.10f);
                    break;

                case GameplayMarkerType.Trap:
                    // Diamond shape
                    DrawDiamond(pos, col, 0.08f);
                    break;

                case GameplayMarkerType.LootContainer:
                    // Gold-tinted diamond
                    var goldCol = new Color(0.85f, 0.75f, 0.10f, col.a);
                    DrawDiamond(pos, goldCol, 0.09f);
                    Gizmos.color = goldCol;
                    Gizmos.DrawSphere(pos, 0.03f); // centre dot
                    break;

                case GameplayMarkerType.Interaction:
                    // Wire sphere — interactions are gentle
                    Gizmos.DrawWireSphere(pos, 0.10f);
                    Gizmos.DrawSphere(pos, 0.04f);
                    break;

                case GameplayMarkerType.AmbientDressing:
                    // Small cube — dressing is environmental
                    Gizmos.DrawWireCube(pos, Vector3.one * 0.12f);
                    break;

                default: // Custom
                    Gizmos.DrawCube(pos, Vector3.one * 0.08f);
                    break;
            }
        }

        private Color GetStateColor() => BakeState switch
        {
            BakeState.Resolved => ResolvedColor,
            BakeState.Baked    => BakedColor,
            _                  => PendingColor,
        };

        private static void DrawDiamond(Vector3 center, Color color, float size)
        {
            Gizmos.color = color;
            var up    = Vector3.up    * size;
            var right = Vector3.right * size;
            var fwd   = Vector3.forward * (size * 0.5f);
            // Horizontal square
            Gizmos.DrawLine(center + up,    center + right);
            Gizmos.DrawLine(center + right, center - up);
            Gizmos.DrawLine(center - up,    center - right);
            Gizmos.DrawLine(center - right, center + up);
            // Vertical spike
            Gizmos.DrawLine(center + up * 1.6f, center);
        }
#endif
    }
}
