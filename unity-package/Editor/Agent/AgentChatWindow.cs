using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityMcpBridge.Editor.Agent
{
    /// <summary>
    /// Embedded agent chat window. Open via Eadon > MCP Bridge > Agent Chat.
    ///
    /// Talks directly to the Anthropic or OpenAI-compatible API and executes
    /// tool calls against the running MCP bridge HTTP server — no Python server
    /// needed for this workflow.
    ///
    /// All configuration lives in an AgentSettings ScriptableObject that can be
    /// swapped at any time from the settings bar at the top of the window.
    /// </summary>
    public class AgentChatWindow : EditorWindow
    {
        // -----------------------------------------------------------------------
        // Unity menu entry
        // -----------------------------------------------------------------------

        [MenuItem("Eadon/MCP Bridge/Agent Chat")]
        public static void ShowWindow()
        {
            var window = GetWindow<AgentChatWindow>();
            window.titleContent = new GUIContent("MCP Agent");
            window.minSize      = new Vector2(360, 480);
        }

        // -----------------------------------------------------------------------
        // Domain-reload persistence
        // -----------------------------------------------------------------------

        [Serializable]
        private class SavedMessage
        {
            public string role;  // user | assistant | tool | system | error
            public string text;
        }

        [SerializeField] private List<SavedMessage> savedMessages  = new List<SavedMessage>();
        [SerializeField] private string             savedHistoryJson = "";

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        [SerializeField] private AgentSettings settingsAsset;

        private AgentLoop               loop;
        private CancellationTokenSource cts;
        private bool                    isSending;
        // UI elements cached from CreateGUI
        private ScrollView  scrollView;
        private VisualElement messageContainer;
        private TextField   inputField;
        private Button      sendButton;
        private Label       statusLabel;
        private Label       connectionLabel;

        // -----------------------------------------------------------------------
        // Colours (Unity dark theme palette)
        // -----------------------------------------------------------------------

        private static readonly Color BgWindow      = new(0.18f, 0.18f, 0.18f);
        private static readonly Color BgHeader      = new(0.13f, 0.13f, 0.13f);
        private static readonly Color BgMessages    = new(0.20f, 0.20f, 0.20f);
        private static readonly Color BgInputArea   = new(0.15f, 0.15f, 0.15f);
        private static readonly Color BubbleUser    = new(0.14f, 0.26f, 0.42f);
        private static readonly Color BubbleAgent   = new(0.24f, 0.24f, 0.24f);
        private static readonly Color BubbleTool    = new(0.16f, 0.16f, 0.16f);
        private static readonly Color TextPrimary   = new(0.88f, 0.88f, 0.88f);
        private static readonly Color TextSecondary = new(0.55f, 0.55f, 0.55f);
        private static readonly Color Accent        = new(0.29f, 0.57f, 0.90f);
        private static readonly Color AccentDim     = new(0.20f, 0.38f, 0.60f);
        private static readonly Color Green         = new(0.30f, 0.72f, 0.40f);
        private static readonly Color Red           = new(0.85f, 0.30f, 0.30f);

        // -----------------------------------------------------------------------
        // CreateGUI — builds the UIToolkit layout
        // -----------------------------------------------------------------------

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = BgWindow;
            root.style.flexDirection   = FlexDirection.Column;
            // root.style.height          = Length.Percent(100);
            root.style.flexGrow = 1;
            BuildHeader(root);
            BuildMessageArea(root);
            BuildStatusBar(root);
            BuildInputArea(root);

            if (savedMessages != null && savedMessages.Count > 0)
            {
                // Restore after a domain reload — snapshot the list first so
                // the Append* calls don't duplicate entries into savedMessages
                var snapshot = new List<SavedMessage>(savedMessages);
                savedMessages.Clear();
                foreach (var m in snapshot)
                    RestoreMessageToUI(m);
                AppendSystemMessage("↺ Recompile complete.", save: false);
                ScrollToBottom();

                if (settingsAsset != null)
                {
                    loop = new AgentLoop(settingsAsset);
                    loop.ConfirmTool = ConfirmToolAsync;
                    loop.OnStatusChanged += text =>
                        EditorApplication.delayCall += () => statusLabel.text = text;

                    if (!string.IsNullOrEmpty(savedHistoryJson))
                        loop.RestoreHistoryJson(savedHistoryJson);

                    SetConnection("Reconnecting…", false);
                    ReconnectAsync();
                }
            }
            else
            {
                AppendSystemMessage("Open an AgentSettings asset above to connect.", save: false);
                if (settingsAsset != null)
                    InitialiseAsync();
            }
        }

        // -----------------------------------------------------------------------
        // Header
        // -----------------------------------------------------------------------

        private void BuildHeader(VisualElement root)
        {
            var header = new VisualElement();
            header.style.flexDirection   = FlexDirection.Row;
            header.style.alignItems      = Align.Center;
            header.style.backgroundColor = BgHeader;
            header.style.paddingLeft     = 10;
            header.style.paddingRight    = 8;
            header.style.paddingTop      = 6;
            header.style.paddingBottom   = 6;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.10f, 0.10f, 0.10f);

            connectionLabel = new Label("○ Not connected");
            connectionLabel.style.color     = TextSecondary;
            connectionLabel.style.fontSize  = 11;
            connectionLabel.style.flexGrow  = 1;
            connectionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(connectionLabel);

            // Settings picker
            var settingsPicker = new ObjectField();
            settingsPicker.objectType   = typeof(AgentSettings);
            settingsPicker.style.width  = 160;
            settingsPicker.style.height = 20;
            settingsPicker.value        = settingsAsset;
            settingsPicker.RegisterValueChangedCallback(evt =>
            {
                settingsAsset = evt.newValue as AgentSettings;
                loop          = null;
                SetConnection("○ Not connected", false);
                if (settingsAsset != null)
                    InitialiseAsync();
            });
            header.Add(settingsPicker);

            // Clear button
            var clearBtn = new Button(() =>
            {
                loop?.ClearHistory();
                messageContainer.Clear();
                AppendSystemMessage("Conversation cleared.");
            });
            clearBtn.text                = "↺";
            clearBtn.tooltip             = "Clear conversation";
            clearBtn.style.width         = 24;
            clearBtn.style.height        = 20;
            clearBtn.style.marginLeft    = 4;
            clearBtn.style.backgroundColor = BubbleTool;
            clearBtn.style.borderTopWidth = clearBtn.style.borderBottomWidth =
            clearBtn.style.borderLeftWidth= clearBtn.style.borderRightWidth = 0;
            clearBtn.style.color         = TextSecondary;
            header.Add(clearBtn);

            root.Add(header);
        }

        // -----------------------------------------------------------------------
        // Message area
        // -----------------------------------------------------------------------

        private void BuildMessageArea(VisualElement root)
        {
            scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow        = 1;
            scrollView.style.backgroundColor = BgMessages;
            scrollView.style.paddingTop      = 8;
            scrollView.style.paddingBottom   = 8;

            messageContainer = new VisualElement();
            messageContainer.style.flexDirection = FlexDirection.Column;
            messageContainer.style.paddingLeft   = 8;
            messageContainer.style.paddingRight  = 8;
            scrollView.Add(messageContainer);

            root.Add(scrollView);

            AppendSystemMessage("Open an AgentSettings asset above to connect.");
        }

        // -----------------------------------------------------------------------
        // Status bar
        // -----------------------------------------------------------------------

        private void BuildStatusBar(VisualElement root)
        {
            var bar = new VisualElement();
            bar.style.backgroundColor = BgHeader;
            bar.style.paddingLeft     = 10;
            bar.style.paddingRight    = 10;
            bar.style.paddingTop      = 3;
            bar.style.paddingBottom   = 3;
            bar.style.borderTopWidth  = 1;
            bar.style.borderTopColor  = new Color(0.10f, 0.10f, 0.10f);
            bar.style.minHeight       = 20;

            statusLabel = new Label(string.Empty);
            statusLabel.style.color   = TextSecondary;
            statusLabel.style.fontSize = 10;
            bar.Add(statusLabel);

            root.Add(bar);
        }

        // -----------------------------------------------------------------------
        // Input area
        // -----------------------------------------------------------------------

        private void BuildInputArea(VisualElement root)
        {
            var area = new VisualElement();
            area.style.flexDirection   = FlexDirection.Row;
            area.style.backgroundColor = BgInputArea;
            area.style.paddingLeft     = 8;
            area.style.paddingRight    = 8;
            area.style.paddingTop      = 8;
            area.style.paddingBottom   = 8;
            area.style.borderTopWidth  = 1;
            area.style.borderTopColor  = new Color(0.10f, 0.10f, 0.10f);
            area.style.alignItems      = Align.FlexEnd;

            var fieldWrapper = new ScrollView(ScrollViewMode.Vertical);
            fieldWrapper.style.flexGrow   = 1;
            fieldWrapper.style.flexShrink = 1;
            fieldWrapper.style.minWidth   = 0;
            fieldWrapper.style.maxHeight  = 120;
            fieldWrapper.style.marginRight = 6;
            fieldWrapper.verticalScrollerVisibility   = ScrollerVisibility.Auto;
            fieldWrapper.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            inputField = new TextField();
            inputField.multiline             = true;
            inputField.style.width           = Length.Percent(100);
            inputField.style.minHeight       = 40;
            // maxHeight removed — ScrollView owns the height cap
            inputField.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
            inputField.style.color           = TextPrimary;
            inputField.style.borderTopLeftRadius     = 4;
            inputField.style.borderTopRightRadius    = 4;
            inputField.style.borderBottomLeftRadius  = 4;
            inputField.style.borderBottomRightRadius = 4;
            inputField.style.borderTopWidth = inputField.style.borderBottomWidth =
                inputField.style.borderLeftWidth= inputField.style.borderRightWidth = 1;
            inputField.style.borderTopColor = inputField.style.borderBottomColor =
                inputField.style.borderLeftColor= inputField.style.borderRightColor =
                    new Color(0.30f, 0.30f, 0.30f);
            fieldWrapper.Add(inputField);
            
            // Enter to send, Shift+Enter for newline
            inputField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return && !e.shiftKey && !isSending)
                {
                    e.StopPropagation();
                    OnSendClicked();
                }
            });

            // area.Add(inputField);
            area.Add(fieldWrapper);
            
            // Enable word wrap — must target the inner text element after it attaches
            inputField.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var inner = inputField.Q(className: "unity-text-field__input");
                if (inner != null) inner.style.whiteSpace = WhiteSpace.Normal;
            });
            
            sendButton = new Button(OnSendClicked);
            sendButton.text                 = "Send";
            sendButton.style.width          = 54;
            sendButton.style.height         = 34;
            sendButton.style.backgroundColor = Accent;
            sendButton.style.color          = Color.white;
            sendButton.style.borderTopLeftRadius     = 4;
            sendButton.style.borderTopRightRadius    = 4;
            sendButton.style.borderBottomLeftRadius  = 4;
            sendButton.style.borderBottomRightRadius = 4;
            sendButton.style.borderTopWidth = sendButton.style.borderBottomWidth =
            sendButton.style.borderLeftWidth= sendButton.style.borderRightWidth = 0;
            sendButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            area.Add(sendButton);

            root.Add(area);
        }

        // -----------------------------------------------------------------------
        // Send
        // -----------------------------------------------------------------------

        private async void OnSendClicked()
        {
            if (isSending || loop == null || settingsAsset == null) return;

            var text = inputField.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            inputField.value = string.Empty;
            isSending        = true;
            sendButton.SetEnabled(false);
            sendButton.style.backgroundColor = AccentDim;

            AppendUserMessage(text);
            cts = new CancellationTokenSource();

            try
            {
                var result = await loop.RunTurnAsync(
                    text,
                    toolMsg => { AppendToolMessage(toolMsg); ScrollToBottom(); },
                    cts.Token);

                if (result.HasError)
                    AppendErrorMessage(result.Error);
                else
                    AppendAssistantMessage(result.Text, result.Images);
            }
            catch (OperationCanceledException)
            {
                AppendSystemMessage("Cancelled.");
            }
            catch (Exception ex)
            {
                AppendErrorMessage($"Unexpected error: {ex.Message}");
            }
            finally
            {
                isSending   = false;
                sendButton.SetEnabled(true);
                sendButton.style.backgroundColor = Accent;
                statusLabel.text = string.Empty;
                ScrollToBottom();
            }
        }

        // -----------------------------------------------------------------------
        // Initialise
        // -----------------------------------------------------------------------

        private async void InitialiseAsync()
        {
            if (settingsAsset == null) return;

            loop = new AgentLoop(settingsAsset);
            loop.ConfirmTool = ConfirmToolAsync;
            loop.OnStatusChanged += text =>
            {
                // UIToolkit updates must be on main thread;
                // EditorApplication.delayCall dispatches to main thread
                EditorApplication.delayCall += () => statusLabel.text = text;
            };

            SetConnection("○ Connecting…", false);
            var status = await loop.InitialiseAsync();
            SetConnection(status, status.StartsWith("✓"));
            AppendSystemMessage(status);
        }

        // -----------------------------------------------------------------------
        // Message rendering
        // -----------------------------------------------------------------------

        private void AppendUserMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "user", text = text });
            var bubble = CreateBubble(text, BubbleUser, TextPrimary, FlexDirection.RowReverse);
            var sender = bubble.Q<Label>("sender");
            if (sender != null) sender.text = "You";
            messageContainer.Add(bubble);
            ScrollToBottom();
        }

        private void AppendAssistantMessage(string text, List<byte[]> images = null)
        {
            if (!string.IsNullOrEmpty(text))
                savedMessages.Add(new SavedMessage { role = "assistant", text = text });
            var bubble = CreateBubble(text, BubbleAgent, TextPrimary, FlexDirection.Row);
            var sender = bubble.Q<Label>("sender");
            if (sender != null) { sender.text = "Claude"; sender.style.color = Accent; }

            // Inline images (from capture_scene_view)
            if (images != null)
                foreach (var bytes in images)
                    bubble.Add(CreateImageElement(bytes));

            messageContainer.Add(bubble);
            ScrollToBottom();
        }

        private void AppendToolMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "tool", text = text });
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.marginTop      = 2;
            row.style.marginBottom   = 2;
            row.style.paddingLeft    = 8;
            row.style.paddingRight   = 8;

            var label = new Label($"⚙ {text}");
            label.style.color    = TextSecondary;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            row.Add(label);

            messageContainer.Add(row);
            ScrollToBottom();
        }

        private void AppendSystemMessage(string text, bool save = true)
        {
            if (save) savedMessages.Add(new SavedMessage { role = "system", text = text });
            var row = new VisualElement();
            row.style.alignItems     = Align.Center;
            row.style.marginTop      = 6;
            row.style.marginBottom   = 6;

            var label = new Label(text);
            label.style.color    = TextSecondary;
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            messageContainer?.Add(row);
            ScrollToBottom();
        }

        private void AppendErrorMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "error", text = text });
            var bubble = CreateBubble($"⚠ {text}", new Color(0.30f, 0.12f, 0.12f), Red, FlexDirection.Row);
            messageContainer.Add(bubble);
            ScrollToBottom();
        }

        // -----------------------------------------------------------------------
        // Tool confirmation
        // -----------------------------------------------------------------------

        private const int MaxDetailsLength = 1500;

        /// <summary>
        /// Shows the tool call in the conversation with Allow / Allow for this
        /// conversation / Deny, and completes when the user picks one. Cancelling
        /// the turn counts as Deny.
        /// </summary>
        private Task<ToolApproval> ConfirmToolAsync(ToolUse toolUse, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ToolApproval>();
            if (messageContainer == null)
            {
                tcs.SetResult(ToolApproval.Deny);
                return tcs.Task;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 4;
            row.style.marginBottom  = 4;

            var card = new VisualElement();
            card.style.backgroundColor         = new Color(0.30f, 0.24f, 0.12f);
            card.style.borderTopLeftRadius     = 8;
            card.style.borderTopRightRadius    = 8;
            card.style.borderBottomLeftRadius  = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.paddingTop    = 7;
            card.style.paddingBottom = 7;
            card.style.paddingLeft   = 10;
            card.style.paddingRight  = 10;
            card.style.maxWidth      = Length.Percent(92);

            var title = new Label($"Allow {toolUse.Name}?");
            title.style.color = TextPrimary;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            card.Add(title);

            var details = new Label(DescribeInput(toolUse));
            details.style.color      = TextSecondary;
            details.style.fontSize   = 10;
            details.style.whiteSpace = WhiteSpace.Normal;
            details.style.marginBottom = 6;
            card.Add(details);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            card.Add(buttons);

            void Answer(ToolApproval approval)
            {
                if (!tcs.TrySetResult(approval)) return;
                buttons.Clear();
                var result = new Label(approval switch
                {
                    ToolApproval.Allow                => "Allowed.",
                    ToolApproval.AllowForConversation => "Allowed for this conversation.",
                    _                                 => "Denied."
                });
                result.style.color    = approval == ToolApproval.Deny ? Red : Green;
                result.style.fontSize = 10;
                buttons.Add(result);
            }

            buttons.Add(new Button(() => Answer(ToolApproval.Allow)) { text = "Allow" });
            buttons.Add(new Button(() => Answer(ToolApproval.AllowForConversation))
            {
                text    = "Allow for this conversation",
                tooltip = $"Run {toolUse.Name} without asking until the conversation is cleared."
            });
            buttons.Add(new Button(() => Answer(ToolApproval.Deny)) { text = "Deny" });

            if (ct.CanBeCanceled)
                ct.Register(() => EditorApplication.delayCall += () => Answer(ToolApproval.Deny));

            row.Add(card);
            messageContainer.Add(row);
            ScrollToBottom();
            return tcs.Task;
        }

        private static string DescribeInput(ToolUse toolUse)
        {
            if (toolUse.Input == null || toolUse.Input.Count == 0)
                return "(no arguments)";

            var text = toolUse.Input.ToString(Formatting.Indented);
            return text.Length <= MaxDetailsLength
                ? text
                : text.Substring(0, MaxDetailsLength) + $"\n… ({text.Length - MaxDetailsLength} more characters)";
        }

        // -----------------------------------------------------------------------
        // Bubble factory
        // -----------------------------------------------------------------------

        private static VisualElement CreateBubble(
            string        text,
            Color         bg,
            Color         textColor,
            FlexDirection direction)
        {
            var row = new VisualElement();
            row.style.flexDirection = direction;
            row.style.marginTop     = 4;
            row.style.marginBottom  = 4;

            var bubble = new VisualElement();
            bubble.style.backgroundColor       = bg;
            bubble.style.borderTopLeftRadius     = 8;
            bubble.style.borderTopRightRadius    = 8;
            bubble.style.borderBottomLeftRadius  = 8;
            bubble.style.borderBottomRightRadius = 8;
            bubble.style.paddingTop      = 7;
            bubble.style.paddingBottom   = 7;
            bubble.style.paddingLeft     = 10;
            bubble.style.paddingRight    = 10;
            bubble.style.maxWidth        = Length.Percent(82);

            var sender = new Label("Agent");
            sender.name                   = "sender";
            sender.style.color            = TextSecondary;
            sender.style.fontSize         = 9;
            sender.style.marginBottom     = 3;
            sender.style.unityFontStyleAndWeight = FontStyle.Bold;
            bubble.Add(sender);

            var content = new Label(text);
            content.style.color      = textColor;
            content.style.fontSize   = 12;
            content.style.whiteSpace = WhiteSpace.Normal;
            content.style.unityTextAlign = TextAnchor.UpperLeft;
            bubble.Add(content);

            row.Add(bubble);
            return row;
        }

        private static VisualElement CreateImageElement(byte[] imageBytes)
        {
            try
            {
                var tex = new Texture2D(1, 1);
                tex.LoadImage(imageBytes);

                var img = new Image();
                img.image             = tex;
                img.style.maxWidth    = 380;
                img.style.maxHeight   = 280;
                img.style.marginTop   = 6;
                img.style.borderTopLeftRadius     = 4;
                img.style.borderTopRightRadius    = 4;
                img.style.borderBottomLeftRadius  = 4;
                img.style.borderBottomRightRadius = 4;
                img.scaleMode         = ScaleMode.ScaleToFit;
                return img;
            }
            catch
            {
                return new Label("[Image could not be displayed]");
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private void ScrollToBottom()
        {
            scrollView?.schedule.Execute(() =>
                scrollView.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(30);
        }

        private void SetConnection(string text, bool connected)
        {
            if (connectionLabel == null) return;
            connectionLabel.text        = text;
            connectionLabel.style.color = connected ? Green : TextSecondary;
        }

        private void OnDestroy()
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        
        private void OnDisable()
        {
            // Persist state so it survives domain reloads
            if (loop != null)
                savedHistoryJson = loop.GetHistoryJson();
            cts?.Cancel();
        }

        private async void ReconnectAsync()
        {
            if (loop == null || settingsAsset == null) return;
            try
            {
                using var http    = new System.Net.Http.HttpClient();
                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, settingsAsset.BridgeBaseUrl + "/status");
                AgentLoop.AddBridgeToken(request);
                using var response = await http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    SetConnection("✓ Reconnected.", true);
                else
                    SetConnection("⚠ Bridge not responding.", false);
            }
            catch
            {
                SetConnection("⚠ Bridge not responding.", false);
            }
        }

        private void RestoreMessageToUI(SavedMessage m)
        {
            switch (m.role)
            {
                case "user":      AppendUserMessage(m.text);               break;
                case "assistant": AppendAssistantMessage(m.text);          break;
                case "tool":      AppendToolMessage(m.text);               break;
                case "system":    AppendSystemMessage(m.text, save: false); break;
                case "error":     AppendErrorMessage(m.text);              break;
            }
        }
    }
}
