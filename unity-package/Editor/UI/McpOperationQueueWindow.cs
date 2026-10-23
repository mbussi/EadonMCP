using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.UI
{
    /// <summary>
    /// Displays pending MCP proposals and lets the level designer approve or
    /// discard them before any changes are applied to the scene.
    ///
    /// Open via: Eadon > MCP Bridge > Operation Queue
    /// </summary>
    public class McpOperationQueueWindow : EditorWindow
    {
        [MenuItem("Eadon/MCP Bridge/Operation Queue")]
        public static void ShowWindow() =>
            GetWindow<McpOperationQueueWindow>("MCP Operation Queue");

        private Vector2 _scrollPos;

        // -----------------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------------

        private void OnEnable()
        {
            ProposalStore.OnChanged += OnStoreChanged;
        }

        private void OnDisable()
        {
            ProposalStore.OnChanged -= OnStoreChanged;
        }

        private void OnStoreChanged() => Repaint();

        // -----------------------------------------------------------------------
        // GUI
        // -----------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();

            var pending = ProposalStore.GetPending();

            if (pending.Count == 0)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox(
                    "No pending proposals.\n\n" +
                    "Ask Claude to propose changes — it will submit a plan here for your review " +
                    "before anything is applied to the scene.",
                    MessageType.Info);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(4);

            foreach (var proposal in pending)
            {
                DrawProposalCard(proposal);
                EditorGUILayout.Space(6);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Pending Proposals", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(64)))
                    GUI.changed = true;
            }
            DrawSeparator();
        }

        private static void DrawProposalCard(Proposal proposal)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Title row
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(proposal.Summary, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(proposal.CreatedAt.ToString("HH:mm:ss"), EditorStyles.miniLabel);
                }

                GUILayout.Label(
                    $"ID: {proposal.Id}  •  {proposal.Operations.Length} operation(s)",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4);

                // Operations list
                foreach (var op in proposal.Operations)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"  {op.Id}.", GUILayout.Width(28));
                        GUILayout.Label(
                            $"[{op.Tool}]  {op.Description}",
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }

                EditorGUILayout.Space(6);

                // Action buttons
                using (new EditorGUILayout.HorizontalScope())
                {
                    var prevBg = GUI.backgroundColor;

                    GUI.backgroundColor = new Color(0.45f, 0.85f, 0.45f);
                    if (GUILayout.Button("Execute All", GUILayout.Height(24)))
                        _ = ProposalHandler.ExecuteProposalAsync(
                            new ExecuteProposalRequest { ProposalId = proposal.Id });

                    GUI.backgroundColor = new Color(0.85f, 0.45f, 0.45f);
                    if (GUILayout.Button("Discard", GUILayout.Width(72), GUILayout.Height(24)))
                        _ = ProposalHandler.DiscardProposalAsync(
                            new DiscardProposalRequest { ProposalId = proposal.Id });

                    GUI.backgroundColor = prevBg;
                }
            }
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }
    }
}
