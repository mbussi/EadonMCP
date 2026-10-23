# Domain Reload Fix

Two coordinated changes. Apply them all before testing.

---

## 1. Models.cs — add `import_after_write` to WriteProjectFileRequest

```
FIND:
        /// <summary>Create intermediate directories if they don't exist. Default true.</summary>
        [JsonProperty("create_directories")]  public bool   CreateDirectories  { get; set; } = true;
    }

REPLACE WITH:
        /// <summary>Create intermediate directories if they don't exist. Default true.</summary>
        [JsonProperty("create_directories")]  public bool   CreateDirectories  { get; set; } = true;

        /// <summary>
        /// Call AssetDatabase.ImportAsset immediately after writing.
        /// Default FALSE — leave this false and call refresh_assets separately
        /// after all files are written and the agent's final response has been sent.
        /// Setting this true will trigger a domain reload mid-conversation.
        /// </summary>
        [JsonProperty("import_after_write")]  public bool   ImportAfterWrite   { get; set; } = false;
    }
```

---

## 2. ProjectFileHandler.cs — two changes

### 2a. Make import conditional

```
FIND:
                File.WriteAllText(fullPath, req.Content, Encoding.UTF8);

                // Tell Unity about the new or changed file
                var assetPath = "Assets/" + relativePath;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

REPLACE WITH:
                File.WriteAllText(fullPath, req.Content, Encoding.UTF8);

                var assetPath = "Assets/" + relativePath;

                // Only trigger import (and therefore recompile/domain reload) when
                // explicitly requested. Default is false so the agent can finish its
                // response before the domain reload kills the conversation.
                if (req.ImportAfterWrite)
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
```

### 2b. Add RefreshAssetsAsync — paste this method inside the class body,
after the ListFilesAsync method and before TryResolvePath:

```csharp
        // -----------------------------------------------------------------------
        // POST /tools/project/refresh_assets
        // -----------------------------------------------------------------------

        /// <summary>
        /// Refresh the AssetDatabase and trigger recompilation of any changed scripts.
        /// Call this after all files have been written and the agent's final text
        /// response has been composed — not during tool-call processing — to avoid
        /// a domain reload killing the in-flight conversation.
        /// </summary>
        public static Task<McpResponse> RefreshAssetsAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                AssetDatabase.Refresh();
                return McpResponse.Ok(new
                {
                    refreshed = true,
                    message   = "AssetDatabase refreshed. " +
                                "Unity will now compile any changed scripts. " +
                                "Wait for the progress bar in the Editor status bar to finish " +
                                "before using the new types."
                });
            });
```

---

## 3. McpRouter.cs — add refresh_assets route

```
FIND:
                // Project File Tools
                if (method == "POST" && path == "/tools/project/write_file")

REPLACE WITH:
                // Project File Tools
                if (method == "POST" && path == "/tools/project/refresh_assets")
                    return await ProjectFileHandler.RefreshAssetsAsync();
                if (method == "POST" && path == "/tools/project/write_file")
```

---

## 4. AgentLoop.cs — add history serialisation methods

Paste these two methods inside the `AgentLoop` class body, after `ClearHistory()`:

```csharp
        /// <summary>Serialise the current conversation history for cross-reload persistence.</summary>
        public string GetHistoryJson()
        {
            try   { return JsonConvert.SerializeObject(history); }
            catch { return "[]"; }
        }

        /// <summary>Restore conversation history after a domain reload.</summary>
        public void RestoreHistoryJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                history = JsonConvert.DeserializeObject<List<ConversationMessage>>(json)
                          ?? new List<ConversationMessage>();
            }
            catch { history = new List<ConversationMessage>(); }
        }
```

---

## 5. AgentChatWindow.cs — serialise messages and reconnect after reload

### 5a. Add SavedMessage inner class and serialised fields

```
FIND:
        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        [SerializeField] private AgentSettings settingsAsset;

        private AgentLoop           loop;
        private CancellationTokenSource cts;
        private bool                isSending;

REPLACE WITH:
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
```

### 5b. Update CreateGUI to restore after reload

```
FIND:
            BuildHeader(root);
            BuildMessageArea(root);
            BuildStatusBar(root);
            BuildInputArea(root);

            // If a settings asset is already assigned, initialise
            if (settingsAsset != null)
                _ = InitialiseAsync();

REPLACE WITH:
            BuildHeader(root);
            BuildMessageArea(root);
            BuildStatusBar(root);
            BuildInputArea(root);

            if (savedMessages != null && savedMessages.Count > 0)
            {
                // Restore after a domain reload
                foreach (var m in savedMessages)
                    RestoreMessageToUI(m);

                AppendSystemMessage("↺ Recompile complete.", save: false);
                ScrollToBottom();

                if (settingsAsset != null)
                {
                    loop = new AgentLoop(settingsAsset);
                    loop.OnStatusChanged += text =>
                        EditorApplication.delayCall += () => statusLabel.text = text;

                    if (!string.IsNullOrEmpty(savedHistoryJson))
                        loop.RestoreHistoryJson(savedHistoryJson);

                    SetConnection("Reconnecting…", false);
                    _ = ReconnectAsync();
                }
            }
            else
            {
                AppendSystemMessage("Open an AgentSettings asset above to connect.", save: false);
                if (settingsAsset != null)
                    InitialiseAsync();
            }
```

### 5c. Update AppendUserMessage to save

```
FIND:
        private void AppendUserMessage(string text)
        {
            var bubble = CreateBubble(text, BubbleUser, TextPrimary, FlexDirection.RowReverse);

REPLACE WITH:
        private void AppendUserMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "user", text = text });
            var bubble = CreateBubble(text, BubbleUser, TextPrimary, FlexDirection.RowReverse);
```

### 5d. Update AppendAssistantMessage to save

```
FIND:
        private void AppendAssistantMessage(string text, List<byte[]> images = null)
        {
            var bubble = CreateBubble(text, BubbleAgent, TextPrimary, FlexDirection.Row);

REPLACE WITH:
        private void AppendAssistantMessage(string text, List<byte[]> images = null)
        {
            if (!string.IsNullOrEmpty(text))
                savedMessages.Add(new SavedMessage { role = "assistant", text = text });
            var bubble = CreateBubble(text, BubbleAgent, TextPrimary, FlexDirection.Row);
```

### 5e. Update AppendToolMessage to save

```
FIND:
        private void AppendToolMessage(string text)
        {
            var row = new VisualElement();

REPLACE WITH:
        private void AppendToolMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "tool", text = text });
            var row = new VisualElement();
```

### 5f. Update AppendSystemMessage — add optional save parameter

```
FIND:
        private void AppendSystemMessage(string text)
        {

REPLACE WITH:
        private void AppendSystemMessage(string text, bool save = true)
        {
            if (save) savedMessages.Add(new SavedMessage { role = "system", text = text });
```

### 5g. Update AppendErrorMessage to save

```
FIND:
        private void AppendErrorMessage(string text)
        {
            var bubble = CreateBubble($"⚠ {text}", new Color(0.30f, 0.12f, 0.12f), Red, FlexDirection.Row);

REPLACE WITH:
        private void AppendErrorMessage(string text)
        {
            savedMessages.Add(new SavedMessage { role = "error", text = text });
            var bubble = CreateBubble($"⚠ {text}", new Color(0.30f, 0.12f, 0.12f), Red, FlexDirection.Row);
```

### 5h. Add OnDisable, ReconnectAsync, and RestoreMessageToUI

Paste these methods inside the class, alongside the other helpers at the bottom
(before or after the `OnDestroy` method):

```csharp
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
                var response = await new System.Net.Http.HttpClient()
                    .GetAsync(settingsAsset.BridgeBaseUrl + "/status");
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
```

Note: `AppendUserMessage`, `AppendAssistantMessage`, etc. will call
`savedMessages.Add(...)` again during restore. To prevent duplicating entries,
temporarily clear and rebuild the list. Replace the foreach in CreateGUI with:

```
FIND:
            if (savedMessages != null && savedMessages.Count > 0)
            {
                // Restore after a domain reload
                foreach (var m in savedMessages)
                    RestoreMessageToUI(m);

REPLACE WITH:
            if (savedMessages != null && savedMessages.Count > 0)
            {
                // Restore after a domain reload — snapshot the list first so
                // the Append* calls don't duplicate entries into savedMessages
                var snapshot = new List<SavedMessage>(savedMessages);
                savedMessages.Clear();
                foreach (var m in snapshot)
                    RestoreMessageToUI(m);
```

---

## 6. Python — project_files.py — add refresh_assets

Append to the bottom of `src/unity_mcp/tools/project_files.py`:

```python
async def refresh_assets() -> str:
    """
    Refresh the Unity AssetDatabase and trigger recompilation of any changed scripts.

    Call this AFTER writing all files and AFTER composing the final response text —
    not in the middle of a tool-call sequence. This avoids a domain reload killing
    the in-flight conversation.

    Correct workflow for creating a C# script:
        1. write_project_file(path="Scripts/Foo.cs", content="...")
        2. Compose and send your final summary response to the user
        3. [separately, if asked] call refresh_assets()

    Or, if the user needs the script compiled as part of the same turn:
        1. write_project_file(...)
        2. refresh_assets()        ← this ends the agent turn (domain reload follows)
        3. (tell the user Unity is compiling)

    Returns
    -------
    { refreshed: true, message: str }
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/project/refresh_assets", {})
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
```

Also update the `write_project_file` docstring to mention the workflow:

```
FIND (in project_files.py):
    Notes
    -----
    - Immediately after writing a script, wait for Unity to finish compiling

REPLACE WITH:
    Notes
    -----
    - Do NOT set import_after_write=True during a multi-step agent conversation.
      Instead, write all files first, send the final response, then call
      refresh_assets() to trigger compilation without killing the conversation.
    - Immediately after writing a script, wait for Unity to finish compiling
```

---

## 7. server.py — register refresh_assets

```
FIND:
from unity_mcp.tools.project_files import write_project_file, read_project_file, list_project_files,

REPLACE WITH:
from unity_mcp.tools.project_files import write_project_file, read_project_file, list_project_files, refresh_assets,
```

```python
mcp.tool()(refresh_assets)
```

---

## 8. ToolRegistry.cs — add refresh_assets + update write_project_file schema

Add to `RouteMap`:
```csharp
{ "refresh_assets",     ("POST", "/tools/project/refresh_assets") },
```

Add to `GetAll()`:
```csharp
T("refresh_assets",
  "Refresh the Unity AssetDatabase and trigger compilation of any changed scripts. " +
  "Call this AFTER writing all files and AFTER your final response text has been composed. " +
  "Do not call this in the middle of a tool sequence — it will cause a domain reload.",
  Obj()),
```

Update the `write_project_file` description in `GetAll()`:
```
FIND:
  "Create or overwrite a file in the Unity project's Assets folder. " +
  "After writing, Unity's AssetDatabase is refreshed automatically. " +
  "For C# scripts, Unity begins recompilation immediately. " +
  "Always check whether a file exists with read_project_file before overwriting.",

REPLACE WITH:
  "Create or overwrite a file in the Unity project's Assets folder. " +
  "Does NOT trigger recompilation automatically (import_after_write defaults to false). " +
  "Write all files first, compose your final response, then call refresh_assets. " +
  "Always check whether a file exists with read_project_file before overwriting.",
```

---

## 9. AgentSettings.cs — update default system prompt

```
FIND:
        public string systemPrompt =
            "You are an AI assistant embedded in the Unity Editor. " +
            "You have access to tools that let you inspect and modify the active Unity scene. " +
            "Before making changes, always verify the current scene state first. " +
            "Use propose_changes for bulk operations so the designer can review before executing. " +
            "Keep responses concise and actionable — the designer is working, not reading essays.";

REPLACE WITH:
        public string systemPrompt =
            "You are an AI assistant embedded in the Unity Editor. " +
            "You have access to tools that let you inspect and modify the active Unity scene. " +
            "Before making changes, always verify the current scene state first. " +
            "Use propose_changes for bulk operations so the designer can review before executing. " +
            "Keep responses concise and actionable — the designer is working, not reading essays.\n\n" +
            "IMPORTANT — C# scripts: after writing script files with write_project_file, " +
            "compose your final summary response first, THEN call refresh_assets as the " +
            "last tool call. This prevents Unity's recompilation from disconnecting the " +
            "conversation before your response is delivered.";
```

---

## Summary of what this achieves

| Scenario | Before | After |
|---|---|---|
| Agent writes a .cs script | Domain reload kills the response | File written, response delivered, then reload triggered by refresh_assets |
| Domain reload from any source | History lost, window unusable | History restored, window reconnects automatically |
| Second conversation after reload | Must close/reopen window | Window shows "↺ Recompile complete" and is immediately usable |
