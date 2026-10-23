using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// File system tools scoped to the Unity project's Assets folder.
    ///
    ///   POST /tools/project/write_file  — create or overwrite a file, then refresh AssetDatabase
    ///   POST /tools/project/read_file   — read an existing file's text content
    ///   POST /tools/project/list_files  — list files in a directory
    ///
    /// All paths are relative to Assets/ (Application.dataPath). A leading "Assets/"
    /// is accepted and stripped. Paths containing ".." are rejected to prevent
    /// writes outside the project.
    ///
    /// After writing, AssetDatabase.ImportAsset is called so Unity picks up the file
    /// immediately. For C# scripts, Unity will begin recompilation automatically.
    /// </summary>
    public static class ProjectFileHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/project/write_file
        // -----------------------------------------------------------------------

        public static Task<McpResponse> WriteFileAsync(WriteProjectFileRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Path))
                    return McpResponse.Fail("INVALID_PARAMS", "path must not be empty.");
                if (req.Content == null)
                    return McpResponse.Fail("INVALID_PARAMS", "content must not be null.");

                if (!TryResolvePath(req.Path, out var relativePath, out var fullPath, out var error))
                    return McpResponse.Fail("INVALID_PATH", error);

                bool existed = File.Exists(fullPath);

                if (existed && !req.Overwrite)
                    return McpResponse.Fail("FILE_EXISTS",
                        $"'Assets/{relativePath}' already exists. " +
                        "Set overwrite=true to replace it, or use read_project_file to inspect it first.");

                // Create any intermediate directories
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    if (!req.CreateDirectories)
                        return McpResponse.Fail("DIRECTORY_NOT_FOUND",
                            $"Directory '{Path.GetDirectoryName("Assets/" + relativePath)}' does not exist. " +
                            "Set create_directories=true to create it.");
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(fullPath, req.Content, Encoding.UTF8);

                var assetPath = "Assets/" + relativePath;

                // Only trigger import (and therefore recompile/domain reload) when
                // explicitly requested. Default is false so the agent can finish its
                // response before the domain reload kills the conversation.
                if (req.ImportAfterWrite)
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                
                return McpResponse.Ok(new WriteProjectFileResultData
                {
                    AssetPath    = assetPath,
                    Created      = !existed,
                    Overwrote    = existed,
                    BytesWritten = Encoding.UTF8.GetByteCount(req.Content)
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/project/read_file
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ReadFileAsync(ReadProjectFileRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.Path))
                    return McpResponse.Fail("INVALID_PARAMS", "path must not be empty.");

                if (!TryResolvePath(req.Path, out var relativePath, out var fullPath, out var error))
                    return McpResponse.Fail("INVALID_PATH", error);

                var assetPath = "Assets/" + relativePath;

                if (!File.Exists(fullPath))
                    return McpResponse.Ok(new ReadProjectFileResultData
                    {
                        AssetPath = assetPath,
                        Exists    = false
                    });

                var content = File.ReadAllText(fullPath, Encoding.UTF8);
                return McpResponse.Ok(new ReadProjectFileResultData
                {
                    AssetPath = assetPath,
                    Exists    = true,
                    Content   = content,
                    SizeBytes = Encoding.UTF8.GetByteCount(content)
                });
            });

        // -----------------------------------------------------------------------
        // POST /tools/project/list_files
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ListFilesAsync(ListProjectFilesRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                // The same check as the other file tools; an empty path is the Assets folder.
                if (!TryResolvePath(req?.Path ?? string.Empty, out var rawPath, out var fullDir, out var pathError,
                                    allowAssetsRoot: true))
                    return McpResponse.Fail("INVALID_PATH", pathError);

                if (!Directory.Exists(fullDir))
                    return McpResponse.Fail("DIRECTORY_NOT_FOUND",
                        $"Directory '{(string.IsNullOrEmpty(rawPath) ? "Assets" : "Assets/" + rawPath)}' " +
                        "does not exist.");

                var pattern   = string.IsNullOrWhiteSpace(req?.Pattern) ? "*" : req.Pattern.Trim();
                if (pattern.Contains("..") || pattern.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                    return McpResponse.Fail("INVALID_PATH",
                        "pattern is a file name pattern such as \"*.cs\"; it must not contain a path.");
                var searchOpt = req?.Recursive == true
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var files = Directory.GetFiles(fullDir, pattern, searchOpt)
                    .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    .Select(f => "Assets/" + Path.GetRelativePath(AssetsRoot, f).Replace('\\', '/'))
                    .OrderBy(f => f)
                    .ToArray();

                var dirAsset = string.IsNullOrEmpty(rawPath) ? "Assets" : "Assets/" + rawPath;
                return McpResponse.Ok(new ListProjectFilesResultData
                {
                    Directory = dirAsset,
                    Files     = files,
                    Count     = files.Length
                });
            });

        // -----------------------------------------------------------------------
        // Path helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Validates and resolves a user-supplied path.
        /// Returns false and sets error if the path is invalid or outside Assets/.
        /// </summary>
        /// <summary>The Assets folder as a full path, without a trailing separator.</summary>
        private static string AssetsRoot =>
            Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        internal static bool TryResolvePath(
            string     input,
            out string relativePath,
            out string fullPath,
            out string error,
            bool       allowAssetsRoot = false)
        {
            relativePath = string.Empty;
            fullPath     = string.Empty;
            error        = string.Empty;

            var raw = (input ?? string.Empty).Trim().Replace('\\', '/');

            // An absolute path (C:/..., //server/..., /Users/...) is never accepted: on
            // Windows Path.Combine would return it unchanged, outside the project.
            if (Path.IsPathRooted(raw) && !raw.StartsWith("/") || raw.StartsWith("//") ||
                (raw.Length >= 2 && raw[1] == ':'))
            {
                error = "Use a path inside the Assets folder, such as \"Assets/Scripts/Foo.cs\", not an absolute path.";
                return false;
            }

            // Strip leading slashes and "Assets/" (case-insensitive) so callers can pass either form
            var clean = raw.TrimStart('/');
            if (clean.Equals("assets", StringComparison.OrdinalIgnoreCase))
                clean = string.Empty;
            else if (clean.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(7);
            clean = clean.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(clean) && !allowAssetsRoot)
            {
                error = "Path resolves to empty after stripping 'Assets/'.";
                return false;
            }

            if (clean.Contains(".."))
            {
                error = "Path must not contain '..' segments.";
                return false;
            }

            var root     = AssetsRoot;
            var resolved = string.IsNullOrEmpty(clean)
                ? root
                : Path.GetFullPath(Path.Combine(root, clean.Replace('/', Path.DirectorySeparatorChar)));

            // Inside Assets means Assets itself or below it: compare whole folder names, so
            // a sibling such as "AssetsBackup" does not pass.
            bool inside = resolved.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                          resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!inside || (!allowAssetsRoot && resolved.Equals(root, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Path must remain within the Assets folder.";
                return false;
            }

            relativePath = clean;
            fullPath     = resolved;
            return true;
        }
        
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
    }
}
