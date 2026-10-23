using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Handles POST /tools/debug/capture_scene_view
    ///
    /// Renders the active Scene View camera to an off-screen RenderTexture,
    /// reads the pixels into a Texture2D, encodes as JPEG, and returns the
    /// result as a base64 string. The Python layer decodes this to raw bytes
    /// and returns an MCP Image so Claude receives the image directly in context.
    ///
    /// This is the only tool in the bridge that returns image data rather than
    /// a JSON payload. It is architecturally distinct from all other tools for
    /// that reason.
    ///
    /// Optional focus_path adjusts the Scene View pivot and size before capture
    /// so that the requested object is centred in frame. This uses SceneView.pivot
    /// and SceneView.size which are synchronously updated before Render() is called.
    ///
    /// Memory management: RenderTexture and Texture2D are allocated per-call and
    /// destroyed in a finally block to prevent Editor memory leaks.
    /// </summary>
    public static class SceneViewCaptureHandler
    {
        // -----------------------------------------------------------------------
        // POST /tools/debug/capture_scene_view
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CaptureSceneViewAsync(
            CaptureSceneViewRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return McpResponse.Fail("NO_SCENE_VIEW",
                        "No active Scene View window found. " +
                        "Open a Scene View tab in the Unity Editor (Window > General > Scene).");

                var cam = sceneView.camera;
                if (cam == null)
                    return McpResponse.Fail("NO_SCENE_CAMERA",
                        "The Scene View has no associated camera. " +
                        "This can happen if the Scene View is minimised or docked in an unusual layout.");

                int w       = req?.Width  > 0 ? req.Width  : 1280;
                int h       = req?.Height > 0 ? req.Height : 720;
                int quality = req?.Quality > 0 ? Mathf.Clamp(req.Quality, 1, 100) : 85;

                var warnings = new List<string>();
                string focusedOn = null;

                // ── Optional: frame the requested object ──────────────────────
                if (!string.IsNullOrEmpty(req?.FocusPath))
                {
                    var target = PlacementHandler.FindByPath(req.FocusPath);
                    if (target != null)
                    {
                        // Compute bounds from all child renderers
                        var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: false);
                        if (renderers.Length > 0)
                        {
                            var bounds = renderers[0].bounds;
                            for (int i = 1; i < renderers.Length; i++)
                                bounds.Encapsulate(renderers[i].bounds);

                            // Set scene view pivot to the centre of the object and
                            // size to comfortably frame it. Both are synchronous.
                            sceneView.pivot = bounds.center;
                            sceneView.size  = Mathf.Max(
                                bounds.extents.x,
                                bounds.extents.y,
                                bounds.extents.z) * 2.5f;
                        }
                        else
                        {
                            // No renderers — just move the pivot to the transform position
                            sceneView.pivot = target.transform.position;
                            sceneView.size  = 5f;
                        }
                        focusedOn = req.FocusPath;
                    }
                    else
                    {
                        warnings.Add(
                            $"focus_path '{req.FocusPath}' not found in the active scene — " +
                            "capturing the current Scene View orientation.");
                    }
                }

                // ── Capture ───────────────────────────────────────────────────
                var rt       = new RenderTexture(w, h, 24);
                Texture2D tex = null;
                var prevRT   = cam.targetTexture;
                var prevActive = RenderTexture.active;

                try
                {
                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    tex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();

                    var bytes  = tex.EncodeToJPG(quality);
                    var base64 = Convert.ToBase64String(bytes);

                    return McpResponse.Ok(new CaptureSceneViewResultData
                    {
                        ImageBase64 = base64,
                        Format      = "jpeg",
                        Width       = w,
                        Height      = h,
                        FocusedOn   = focusedOn,
                        Warnings    = warnings.ToArray()
                    });
                }
                finally
                {
                    // Always restore state and release allocations
                    cam.targetTexture    = prevRT;
                    RenderTexture.active = prevActive;
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            });
    }
}
