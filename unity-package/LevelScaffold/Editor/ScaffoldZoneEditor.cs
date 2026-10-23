using UnityEditor;
using UnityEngine;

namespace LevelScaffold.Editor
{
    [CustomEditor(typeof(ScaffoldZone))]
    public class ScaffoldZoneEditor : UnityEditor.Editor
    {
        SerializedProperty _zoneType;
        SerializedProperty _dimensions;
        SerializedProperty _designNote;
        SerializedProperty _dressingTags;
        SerializedProperty _connectedZones;

        ScaffoldZoneType _prevZoneType;

        static GUIStyle _noteStyle;
        static GUIStyle _sectionHeaderStyle;

        void OnEnable()
        {
            _zoneType       = serializedObject.FindProperty("ZoneType");
            _dimensions     = serializedObject.FindProperty("Dimensions");
            _designNote     = serializedObject.FindProperty("DesignNote");
            _dressingTags   = serializedObject.FindProperty("DressingTags");
            _connectedZones = serializedObject.FindProperty("ConnectedZones");
            _prevZoneType   = ((ScaffoldZone)target).ZoneType;
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            var zone = (ScaffoldZone)target;

            // ── Design note (the most important field — shown first) ─────── //
            if (!string.IsNullOrWhiteSpace(zone.DesignNote))
            {
                EditorGUILayout.LabelField("Design Note", _sectionHeaderStyle);
                EditorGUILayout.LabelField(zone.DesignNote, _noteStyle,
                    GUILayout.MinHeight(48f));
                EditorGUILayout.Space(6f);
                EditorGUILayout.Separator();
            }

            // ── Zone type ──────────────────────────────────────────────── //
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_zoneType);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                // When zone type changes, offer to reset dimensions to sensible defaults
                var newType = zone.ZoneType;
                if (newType != _prevZoneType)
                {
                    bool reset = EditorUtility.DisplayDialog(
                        "Reset Dimensions?",
                        $"Reset dimensions to defaults for {newType}?",
                        "Reset", "Keep Current");
                    if (reset)
                    {
                        Undo.RecordObject(zone, "Reset Zone Dimensions");
                        zone.ResetDimensionsToDefaults();
                        EditorUtility.SetDirty(zone);
                    }
                    _prevZoneType = newType;
                }
                serializedObject.Update();
            }

            // ── Dimensions — only the fields relevant to the chosen type ── //
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Dimensions", _sectionHeaderStyle);
            using (new EditorGUI.IndentLevelScope())
                DrawRelevantDimensions(zone.ZoneType);

            EditorGUILayout.Space(6f);

            // ── Design note (editable) ─────────────────────────────────── //
            EditorGUILayout.PropertyField(_designNote, new GUIContent("Design Note"));

            // ── Dressing tags ──────────────────────────────────────────── //
            EditorGUILayout.PropertyField(_dressingTags, new GUIContent("Dressing Tags"),
                includeChildren: true);

            // ── Connected zones ────────────────────────────────────────── //
            EditorGUILayout.PropertyField(_connectedZones, new GUIContent("Connected Zones"),
                includeChildren: true);

            serializedObject.ApplyModifiedProperties();

            // ── Anchor stats ───────────────────────────────────────────── //
            EditorGUILayout.Space(6f);
            var anchors = zone.GetComponentsInChildren<ScaffoldAnchor>(includeInactive: true);
            var markers = zone.GetComponentsInChildren<GameplayMarker>(includeInactive: true);
            int pending  = 0, resolved = 0, baked = 0;
            foreach (var m in markers)
            {
                switch (m.BakeState)
                {
                    case BakeState.Pending:  pending++;  break;
                    case BakeState.Resolved: resolved++; break;
                    case BakeState.Baked:    baked++;    break;
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Anchors: {anchors.Length}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Markers: {markers.Length}  ({pending}▶ {resolved}◆ {baked}✓)",
                    EditorStyles.miniLabel);
            }

            // ── Refresh Anchors button ─────────────────────────────────── //
            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(zone.ZoneType == ScaffoldZoneType.Custom))
            {
                if (GUILayout.Button("↺  Refresh Anchors", GUILayout.Height(28f)))
                    TryRefreshAnchors(zone, anchors.Length);
            }

            if (zone.ZoneType == ScaffoldZoneType.Custom)
                EditorGUILayout.HelpBox(
                    "Custom zones have no auto-generated anchors. " +
                    "Add ScaffoldAnchor children manually.", MessageType.Info);
        }

        private static void TryRefreshAnchors(ScaffoldZone zone, int existingCount)
        {
            bool confirmed = existingCount == 0 || EditorUtility.DisplayDialog(
                "Refresh Anchors",
                $"This will destroy and recreate all {existingCount} anchor(s) on " +
                $"'{zone.gameObject.name}'.\n\nGameplayMarker children that are NOT " +
                "direct children of anchor objects will be preserved.\n\nContinue?",
                "Refresh", "Cancel");

            if (confirmed)
                ScaffoldAnchorGenerator.GenerateAnchors(zone);
        }

        // ------------------------------------------------------------------ //
        //  Draw only the relevant dimension fields per zone type              //
        // ------------------------------------------------------------------ //

        private void DrawRelevantDimensions(ScaffoldZoneType type)
        {
            switch (type)
            {
                case ScaffoldZoneType.SpiralStaircase:
                    PropField("Width");
                    PropField("StepCount");
                    PropField("RiserHeight");
                    PropField("Turns");
                    float totalH = ((ScaffoldZone)target).Dimensions.StaircaseTotalHeight;
                    EditorGUILayout.LabelField($"Total descent: {totalH:F2} m", EditorStyles.miniLabel);
                    break;

                case ScaffoldZoneType.StraightCorridor:
                case ScaffoldZoneType.CrawlTunnel:
                    PropField("Width");
                    PropField("Depth",  "Length");
                    PropField("Height");
                    break;

                case ScaffoldZoneType.CircularChamber:
                    PropField("Diameter");
                    PropField("Height");
                    break;

                case ScaffoldZoneType.RectangularRoom:
                    PropField("Width");
                    PropField("Depth");
                    PropField("Height");
                    break;

                case ScaffoldZoneType.OpenArea:
                    EditorGUILayout.HelpBox(
                        "Set Radius > 0 for a circular area, or leave Radius = 0 for a box.", MessageType.None);
                    PropField("Radius", "Radius (0 = use box)");
                    PropField("Width");
                    PropField("Depth");
                    break;

                default:
                    EditorGUILayout.PropertyField(_dimensions, includeChildren: true);
                    break;
            }
        }

        private void PropField(string fieldName, string label = null)
        {
            var prop = _dimensions.FindPropertyRelative(fieldName);
            if (prop == null) return;
            if (label != null)
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
            else
                EditorGUILayout.PropertyField(prop);
        }

        // ------------------------------------------------------------------ //
        //  Styles — lazy initialisation (GUIStyle requires a GUI context)    //
        // ------------------------------------------------------------------ //

        private static void EnsureStyles()
        {
            _noteStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize  = 11,
                padding   = new RectOffset(6, 6, 4, 4),
                normal    = { background = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.15f, 0.4f)) },
            };

            _sectionHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
            };
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
