using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// The shared secret that every caller of the bridge HTTP API must present.
    ///
    /// The token lives in a file in the user's home folder, not in the project:
    ///   macOS / Linux:  ~/.unity-mcp/bridge-token
    ///   Windows:        %USERPROFILE%\.unity-mcp\bridge-token
    ///
    /// It is one token per machine, shared by every Unity project on it, because
    /// the Python MCP server does not know which project it is talking to. The
    /// Python server reads the same file, so local use needs no configuration.
    /// A client on another machine sets UNITY_MCP_TOKEN to the value shown by
    /// Window > MCP Bridge > Copy Bridge Token.
    ///
    /// Callers send it as:  Authorization: Bearer &lt;token&gt;
    /// </summary>
    public static class McpBridgeAuth
    {
        private const string FolderName = ".unity-mcp";
        private const string FileName   = "bridge-token";

        private static readonly object Gate = new object();
        private static string _token;

        /// <summary>Full path of the token file on this machine.</summary>
        public static string TokenFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                FolderName,
                FileName);

        /// <summary>
        /// The current token. Reads the token file, creating it with a new random
        /// token the first time.
        /// </summary>
        public static string Token
        {
            get
            {
                lock (Gate)
                {
                    if (string.IsNullOrEmpty(_token))
                        _token = LoadOrCreate();
                    return _token;
                }
            }
        }

        /// <summary>
        /// True when the request carries "Authorization: Bearer &lt;token&gt;" with the
        /// current token. Safe to call from the listener's thread-pool threads.
        /// </summary>
        public static bool IsAuthorized(HttpListenerRequest request)
        {
            var header = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header)) return false;

            const string scheme = "Bearer ";
            if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;

            var supplied = header.Substring(scheme.Length).Trim();
            return FixedTimeEquals(supplied, Token);
        }

        /// <summary>Writes a new random token. Every client must pick up the new value.</summary>
        public static string Regenerate()
        {
            lock (Gate)
            {
                _token = NewToken();
                Write(_token);
                return _token;
            }
        }

        // -----------------------------------------------------------------------
        // Menu items
        // -----------------------------------------------------------------------

        [MenuItem("Window/MCP Bridge/Copy Bridge Token")]
        private static void CopyTokenMenu()
        {
            EditorGUIUtility.systemCopyBuffer = Token;
            Debug.Log($"[MCP Bridge] Bridge token copied to the clipboard. It is stored in {TokenFilePath}.");
        }

        [MenuItem("Window/MCP Bridge/Regenerate Bridge Token")]
        private static void RegenerateTokenMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Regenerate Bridge Token",
                    "Every Unity project on this machine shares this token.\n\n" +
                    "MCP servers on this machine read the new token automatically. " +
                    "Clients on other machines stop working until you give them the new token " +
                    "(UNITY_MCP_TOKEN).",
                    "Regenerate",
                    "Cancel"))
                return;

            Regenerate();
            Debug.Log($"[MCP Bridge] Bridge token regenerated ({TokenFilePath}).");
        }

        // -----------------------------------------------------------------------
        // File handling
        // -----------------------------------------------------------------------

        private static string LoadOrCreate()
        {
            var path = TokenFilePath;
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(existing))
                        return existing;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Bridge] Could not read the bridge token at {path}: {ex.Message}");
            }

            var token = NewToken();
            Write(token);
            return token;
        }

        private static void Write(string token)
        {
            var path   = TokenFilePath;
            var folder = Path.GetDirectoryName(path);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, token, new UTF8Encoding(false));
                RestrictToOwner(folder, "700");
                RestrictToOwner(path,   "600");
            }
            catch (Exception ex)
            {
                // The listener still works with the in-memory token; only clients
                // that read the file are affected.
                Debug.LogError($"[MCP Bridge] Could not write the bridge token to {path}: {ex.Message}");
            }
        }

        /// <summary>chmod on macOS and Linux, so other users of the machine cannot read the token.</summary>
        private static void RestrictToOwner(string path, string mode)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            try
            {
                using var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "/bin/chmod",
                    Arguments       = $"{mode} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
                chmod?.WaitForExit(2000);
            }
            catch
            {
                // Best effort: the home folder is already private to the user.
            }
        }

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            // URL-safe base64 without padding: easy to paste into env vars and JSON.
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var x = Encoding.UTF8.GetBytes(a);
            var y = Encoding.UTF8.GetBytes(b);
            var diff = x.Length ^ y.Length;
            for (int i = 0; i < x.Length && i < y.Length; i++)
                diff |= x[i] ^ y[i];
            return diff == 0;
        }
    }
}
