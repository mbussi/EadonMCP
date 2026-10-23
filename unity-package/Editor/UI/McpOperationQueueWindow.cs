using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.UI
{
    /// <summary>
    /// Displays MCP proposals and lets the level designer review them before any
    /// change is applied to the scene.
    ///
    ///   Approve — lets Claude run the proposal with execute_proposal.
    ///   Execute — runs it now, as one undo step; if any operation fails, everything
    ///             the proposal did is undone.
    ///   Discard — drops it.
    ///
    /// Each operation folds out to show the exact arguments it will run with.
    ///
    /// Open via: Eadon > MCP Bridge > Operation Queue
    /// </summary>
    public class McpOperationQueueWindow : EditorWindow
    {
        private const int RecentShown = 5;

        [MenuItem("Eadon/MCP Bridge/Operation Queue")]
        public static void ShowWindow() =>
            GetWindow<McpOperationQueueWindow>("MCP Operation Queue");

        private Vector2 _scrollPos;
        private bool    _showRecent;

        /// <summary>Operations whose arguments are unfolded, by "proposalId/operationId".</summary>
        private readonly HashSet<string> _unfolded = new HashSet<string>();

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
            EditorGUILayout.Space(6);
            GUILayout.Label("Proposals", EditorStyles.boldLabel);
            DrawSeparator();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var open = ProposalStore.GetOpen();
            if (open.Count == 0)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox(
                    "No proposals waiting for review.\n\n" +
                    "Ask Claude to propose changes — it will submit a plan here for your review " +
                    "before anything is applied to the scene.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space(4);
                foreach (var proposal in open)
                {
                    DrawProposalCard(proposal);
                    EditorGUILayout.Space(6);
                }
            }

            DrawRecent();
            EditorGUILayout.EndScrollView();
        }

        private void DrawProposalCard(Proposal proposal)
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
                    $"ID: {proposal.Id}  •  {proposal.Operations.Length} operation(s)  •  {StateLabel(proposal.Status)}",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4);
                foreach (var op in proposal.Operations)
                    DrawOperation(proposal, op);
                EditorGUILayout.Space(6);

                DrawButtons(proposal);
            }
        }

        private void DrawOperation(Proposal proposal, ProposalOperationItem op)
        {
            var key      = $"{proposal.Id}/{op.Id}";
            var unfolded = _unfolded.Contains(key);
            var label    = $"{op.Id}. [{op.Tool}]  {op.Description}";

            var now = EditorGUILayout.Foldout(unfolded, label, true);
            if (now != unfolded)
            {
                if (now) _unfolded.Add(key); else _unfolded.Remove(key);
            }
            if (!now) return;

            var json = op.Params == null ? "{}" : op.Params.ToString(Formatting.Indented);
            using (new EditorGUI.IndentLevelScope())
            {
                var style  = EditorStyles.wordWrappedMiniLabel;
                var height = style.CalcHeight(new GUIContent(json), position.width - 60);
                EditorGUILayout.SelectableLabel(json, style, GUILayout.Height(height));
            }
        }

        private static void DrawButtons(Proposal proposal)
        {
            if (proposal.Status == ProposalStatus.Executing)
            {
                EditorGUILayout.HelpBox("Executing…", MessageType.None);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var prevBg = GUI.backgroundColor;

                using (new EditorGUI.DisabledScope(proposal.Status != ProposalStatus.Pending))
                {
                    GUI.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
                    if (GUILayout.Button(new GUIContent("Approve",
                            "Let Claude run this proposal with execute_proposal."), GUILayout.Height(24)))
                        ProposalHandler.Approve(proposal.Id);
                }

                GUI.backgroundColor = new Color(0.45f, 0.85f, 0.45f);
                if (GUILayout.Button(new GUIContent("Execute",
                        "Run it now, as one undo step. If an operation fails, everything is undone."),
                        GUILayout.Height(24)))
                    _ = ProposalHandler.ExecuteFromQueueAsync(proposal.Id);

                GUI.backgroundColor = new Color(0.85f, 0.45f, 0.45f);
                if (GUILayout.Button("Discard", GUILayout.Width(72), GUILayout.Height(24)))
                    _ = ProposalHandler.DiscardProposalAsync(
                        new DiscardProposalRequest { ProposalId = proposal.Id });

                GUI.backgroundColor = prevBg;
            }
        }

        private void DrawRecent()
        {
            var finished = ProposalStore.GetFinished();
            if (finished.Count == 0) return;

            EditorGUILayout.Space(8);
            _showRecent = EditorGUILayout.Foldout(_showRecent, $"Recent ({finished.Count})", true);
            if (!_showRecent) return;

            for (int i = 0; i < finished.Count && i < RecentShown; i++)
            {
                var p = finished[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label($"{p.Summary}  •  {StateLabel(p.Status)}  •  {p.FinishedAt?.ToString("HH:mm:ss")}",
                        EditorStyles.wordWrappedMiniLabel);
                    if (!string.IsNullOrEmpty(p.Failure))
                        EditorGUILayout.HelpBox(p.Failure, MessageType.Warning);
                }
            }
        }

        private static string StateLabel(ProposalStatus status) => status switch
        {
            ProposalStatus.Pending   => "waiting for review",
            ProposalStatus.Approved  => "approved: Claude may execute it",
            ProposalStatus.Executing => "executing",
            ProposalStatus.Executed  => "executed",
            ProposalStatus.Failed    => "failed",
            ProposalStatus.Discarded => "discarded",
            _                        => status.ToString(),
        };

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }
    }
}
