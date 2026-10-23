using System;
using UnityEditor;
using UnityEngine;

namespace LevelScaffold.Editor
{
    [CustomEditor(typeof(GameplayMarker))]
    public class GameplayMarkerEditor : UnityEditor.Editor
    {
        SerializedProperty _markerType;
        SerializedProperty _markerLabel;
        SerializedProperty _semanticTags;
        SerializedProperty _detectionRadius;
        SerializedProperty _configJson;
        SerializedProperty _bakeState;
        SerializedProperty _resolvedPrefabGuid;

        static GUIStyle _tagChipStyle;
        static GUIStyle _badgeStyle;
        static GUIStyle _jsonStyle;

        void OnEnable()
        {
            _markerType         = serializedObject.FindProperty("MarkerType");
            _markerLabel        = serializedObject.FindProperty("MarkerLabel");
            _semanticTags       = serializedObject.FindProperty("SemanticTags");
            _detectionRadius    = serializedObject.FindProperty("DetectionRadius");
            _configJson         = serializedObject.FindProperty("ConfigJson");
            _bakeState          = serializedObject.FindProperty("BakeState");
            _resolvedPrefabGuid = serializedObject.FindProperty("ResolvedPrefabGuid");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            var marker = (GameplayMarker)target;

            // ── Header: type + label ───────────────────────────────────── //
            string typeLabel = marker.MarkerType.ToString();
            string nameLabel = string.IsNullOrWhiteSpace(marker.MarkerLabel)
                ? "(no label)" : marker.MarkerLabel;
            EditorGUILayout.LabelField($"{typeLabel}  ·  {nameLabel}", EditorStyles.boldLabel);

            // ── Bake state badge ───────────────────────────────────────── //
            DrawBakeStateBadge(marker.BakeState);
            EditorGUILayout.Space(6f);
            EditorGUILayout.Separator();

            // ── Core fields ────────────────────────────────────────────── //
            EditorGUILayout.PropertyField(_markerType);
            EditorGUILayout.PropertyField(_markerLabel);
            EditorGUILayout.PropertyField(_detectionRadius);
            EditorGUILayout.Space(4f);

            // ── Semantic tags — chip display + editable list ───────────── //
            EditorGUILayout.LabelField("Semantic Tags", EditorStyles.boldLabel);
            DrawTagChips(marker.SemanticTags);
            EditorGUILayout.PropertyField(_semanticTags,
                new GUIContent("Edit Tags"), includeChildren: true);
            EditorGUILayout.Space(6f);

            // ── Config JSON ────────────────────────────────────────────── //
            EditorGUILayout.LabelField("Config JSON", EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(marker.ConfigJson))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextArea(marker.ConfigJson, _jsonStyle,
                        GUILayout.MinHeight(55f), GUILayout.MaxHeight(200f));
                EditorGUILayout.Space(2f);
            }
            EditorGUILayout.PropertyField(_configJson, new GUIContent("Edit JSON"));
            EditorGUILayout.Space(6f);

            // ── Bake state ─────────────────────────────────────────────── //
            EditorGUILayout.Separator();
            EditorGUILayout.PropertyField(_bakeState);
            if (!string.IsNullOrEmpty(marker.ResolvedPrefabGuid))
                EditorGUILayout.LabelField(
                    "Resolved GUID",
                    marker.ResolvedPrefabGuid,
                    EditorStyles.miniLabel);

            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ //
        //  Bake state badge                                                   //
        // ------------------------------------------------------------------ //

        private void DrawBakeStateBadge(BakeState state)
        {
            var (icon, col) = state switch
            {
                BakeState.Pending  => ("●  PENDING",  new Color(1.0f, 0.5f, 0.0f)),
                BakeState.Resolved => ("◆  RESOLVED", new Color(1.0f, 0.9f, 0.1f)),
                BakeState.Baked    => ("✓  BAKED",    new Color(0.1f, 0.8f, 0.7f)),
                _                  => ("?  UNKNOWN",  Color.grey),
            };

            var prev = GUI.contentColor;
            GUI.contentColor = col;
            EditorGUILayout.LabelField(icon, _badgeStyle);
            GUI.contentColor = prev;
        }

        // ------------------------------------------------------------------ //
        //  Tag chips                                                          //
        // ------------------------------------------------------------------ //

        private static void DrawTagChips(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                return;
            }

            // Measure a flow-layout row of chips
            var viewRect = EditorGUILayout.GetControlRect(GUILayout.Height(1f));
            float maxWidth = viewRect.width;

            // Calculate total rows needed
            float x = 0f;
            int rows = 1;
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                float w = _tagChipStyle.CalcSize(new GUIContent(tag)).x + 8f;
                if (x + w > maxWidth && x > 0f) { x = 0f; rows++; }
                x += w + 3f;
            }

            var chipAreaRect = EditorGUILayout.GetControlRect(GUILayout.Height(rows * 20f + 4f));
            x = chipAreaRect.x;
            float y = chipAreaRect.y;

            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                var   content = new GUIContent(tag);
                float w       = _tagChipStyle.CalcSize(content).x + 8f;

                if (x + w > chipAreaRect.xMax && x > chipAreaRect.x)
                {
                    x = chipAreaRect.x;
                    y += 20f;
                }

                GUI.Label(new Rect(x, y, w, 18f), content, _tagChipStyle);
                x += w + 3f;
            }
        }

        // ------------------------------------------------------------------ //
        //  Styles — lazy init                                                 //
        // ------------------------------------------------------------------ //

        private static void EnsureStyles()
        {
            _tagChipStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                padding   = new RectOffset(4, 4, 2, 2),
                normal    = { background = MakeTex(1, 1, new Color(0.22f, 0.40f, 0.60f, 0.65f)) },
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
            };

            _badgeStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };

            _jsonStyle ??= new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 10,
                font     = EditorStyles.miniFont,
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
