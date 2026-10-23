using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace LevelScaffold.Editor
{
    /// <summary>
    /// Scene view toolbar overlay for the Level Scaffold system.
    ///
    /// Provides per-category visibility toggles for zone volumes, anchors,
    /// gameplay markers, detection radii, and scene labels.
    ///
    /// Toggle states are written to ScaffoldGizmoState (Runtime assembly) so that
    /// the MonoBehaviour OnDrawGizmos methods can read them without depending on the
    /// Editor assembly. States are persisted across editor sessions via SessionState.
    /// </summary>
    [Overlay(typeof(SceneView), "level-scaffold", "Level Scaffold")]
    public class ScaffoldOverlay : Overlay
    {
        // ------------------------------------------------------------------ //
        //  SessionState keys                                                  //
        // ------------------------------------------------------------------ //

        const string k_Zones   = "LevelScaffold.Overlay.ShowZones";
        const string k_Anchors = "LevelScaffold.Overlay.ShowAnchors";
        const string k_Markers = "LevelScaffold.Overlay.ShowMarkers";
        const string k_Radii   = "LevelScaffold.Overlay.ShowRadii";
        const string k_Labels  = "LevelScaffold.Overlay.ShowLabels";

        // ------------------------------------------------------------------ //
        //  Lifecycle                                                          //
        // ------------------------------------------------------------------ //

        public override void OnCreated()
        {
            // Restore persisted state into ScaffoldGizmoState
            ScaffoldGizmoState.ShowZones   = SessionState.GetBool(k_Zones,   true);
            ScaffoldGizmoState.ShowAnchors = SessionState.GetBool(k_Anchors, true);
            ScaffoldGizmoState.ShowMarkers = SessionState.GetBool(k_Markers, true);
            ScaffoldGizmoState.ShowRadii   = SessionState.GetBool(k_Radii,   true);
            ScaffoldGizmoState.ShowLabels  = SessionState.GetBool(k_Labels,  true);
        }

        // ------------------------------------------------------------------ //
        //  Panel content — IMGUI wrapped in a VisualElement                  //
        // ------------------------------------------------------------------ //

        public override VisualElement CreatePanelContent()
        {
            // IMGUIContainer allows us to use EditorGUILayout inside the overlay
            var container = new IMGUIContainer(DrawPanelIMGUI);
            container.style.minWidth  = 140;
            container.style.minHeight = 160;
            return container;
        }

        private void DrawPanelIMGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);
                EditorGUILayout.Space(2f);

                DrawToggle("Zones",   k_Zones,   ref ScaffoldGizmoState.ShowZones);
                DrawToggle("Anchors", k_Anchors, ref ScaffoldGizmoState.ShowAnchors);
                DrawToggle("Markers", k_Markers, ref ScaffoldGizmoState.ShowMarkers);
                DrawToggle("Radii",   k_Radii,   ref ScaffoldGizmoState.ShowRadii);
                DrawToggle("Labels",  k_Labels,  ref ScaffoldGizmoState.ShowLabels);

                EditorGUILayout.Space(8f);
                DrawBakeStatusSection();
            }
        }

        // ------------------------------------------------------------------ //
        //  Toggle helper                                                      //
        // ------------------------------------------------------------------ //

        private static void DrawToggle(string label, string key, ref bool state)
        {
            EditorGUI.BeginChangeCheck();
            bool newVal = EditorGUILayout.Toggle(label, state);
            if (EditorGUI.EndChangeCheck())
            {
                state = newVal;
                SessionState.SetBool(key, newVal);
                SceneView.RepaintAll();
            }
        }

        // ------------------------------------------------------------------ //
        //  Bake status badge                                                  //
        // ------------------------------------------------------------------ //

        private static void DrawBakeStatusSection()
        {
            EditorGUILayout.LabelField("Bake Status", EditorStyles.boldLabel);

            int pending = 0, resolved = 0, baked = 0;

#if UNITY_2022_2_OR_NEWER
            var markers = Object.FindObjectsByType<GameplayMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var markers = Object.FindObjectsOfType<GameplayMarker>(includeInactive: true);
#endif
            foreach (var m in markers)
            {
                switch (m.BakeState)
                {
                    case BakeState.Pending:  pending++;  break;
                    case BakeState.Resolved: resolved++; break;
                    case BakeState.Baked:    baked++;    break;
                }
            }

            if (pending + resolved + baked == 0)
            {
                EditorGUILayout.LabelField("No markers in scene", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                var prev = GUI.contentColor;

                GUI.contentColor = new Color(1.0f, 0.5f, 0.0f);
                EditorGUILayout.LabelField($"●  {pending,3}  pending");

                GUI.contentColor = new Color(1.0f, 0.9f, 0.1f);
                EditorGUILayout.LabelField($"◆  {resolved,3}  resolved");

                GUI.contentColor = new Color(0.1f, 0.8f, 0.7f);
                EditorGUILayout.LabelField($"✓  {baked,3}  baked");

                GUI.contentColor = prev;
            }
        }
    }
}
