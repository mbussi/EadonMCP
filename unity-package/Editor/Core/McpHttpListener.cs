using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Manages the lifecycle of the HTTP server that receives tool calls from
    /// the Python MCP server.
    ///
    /// Lifecycle
    /// ---------
    /// • Starts automatically when Unity loads (InitializeOnLoad static constructor).
    /// • Stops cleanly before script recompilation (AssemblyReloadEvents).
    /// • Stops on Editor quit (EditorApplication.quitting).
    ///
    /// Threading
    /// ---------
    /// The listen loop runs on a background thread. Each accepted request is
    /// handed to the thread pool. Handlers that need Unity API access go through
    /// MainThreadDispatcher.EnqueueAsync.
    ///
    /// Long requests
    /// -------------
    /// Every request is followed by an McpJob. A request still unfinished after
    /// HandoffSeconds is answered with HTTP 202, "pending": true and the job
    /// (its ID and state); the work carries on and its result is kept in
    /// McpJobStore for GET /jobs?job_id=…. The listener's threads do not depend
    /// on the main thread, so this answer comes even while Unity is frozen.
    ///
    /// Security
    /// --------
    /// • Every request must carry "Authorization: Bearer &lt;token&gt;" (see McpBridgeAuth).
    /// • Requests with an Origin header are refused: they come from a web page in a
    ///   browser, never from an MCP client.
    /// • POST bodies must be application/json, so a page cannot send a plain-text
    ///   "simple request" that skips the browser's permission check.
    /// • The listener binds to localhost unless McpBridgeSettings.allowRemoteConnections
    ///   is ticked.
    /// </summary>
    [InitializeOnLoad]
    public static class McpHttpListener
    {
        /// <summary>Seconds a request may take before the caller gets a job ID instead of the result.</summary>
        public const int HandoffSeconds = 20;

        private static HttpListener _listener;
        private static Thread       _listenerThread;
        private static volatile bool _running;

        // -----------------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------------

        static McpHttpListener()
        {
            EditorApplication.quitting                  += Stop;
            AssemblyReloadEvents.beforeAssemblyReload   += Stop;

            if (McpBridgeSettings.GetOrCreate().startOnLoad)
                Start();
        }

        /// <summary>Starts the HTTP listener on the port specified in McpBridgeSettings.</summary>
        public static void Start()
        {
            if (_running)
            {
                Debug.LogWarning("[MCP Bridge] Listener is already running.");
                return;
            }

            var settings = McpBridgeSettings.GetOrCreate();
            int  port    = settings.port;
            bool remote  = settings.allowRemoteConnections;

            // Create or load the token on the main thread, before any request arrives.
            _ = McpBridgeAuth.Token;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(remote ? $"http://*:{port}/" : $"http://localhost:{port}/");
                _listener.Start();
                _running = true;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name         = "McpBridgeListenerThread"
                };
                _listenerThread.Start();

                if (remote)
                    Debug.LogWarning(
                        $"[MCP Bridge] Listening on port {port} on all network interfaces (Allow Remote Connections is on). " +
                        "Callers need the bridge token; it travels unencrypted, so use this on a trusted network only.");
                else
                    Debug.Log($"[MCP Bridge] Listening on http://localhost:{port}/");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Failed to start listener on port {port}: {ex.Message}");
                _running = false;
                _listener?.Close();
                _listener = null;
            }
        }

        /// <summary>Stops the HTTP listener and releases all resources.</summary>
        public static void Stop()
        {
            if (!_running) return;

            _running = false;

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Bridge] Error during stop: {ex.Message}");
            }
            finally
            {
                _listener = null;
                Debug.Log("[MCP Bridge] Listener stopped.");
            }
        }

        /// <summary>True while the listener is accepting connections.</summary>
        public static bool IsRunning => _running;

        /// <summary>Restarts the listener so port and Allow Remote Connections changes take effect.</summary>
        [MenuItem("Eadon/MCP Bridge/Restart Listener")]
        public static void Restart()
        {
            Stop();
            Start();
        }

        // -----------------------------------------------------------------------
        // Listen loop  (background thread)
        // -----------------------------------------------------------------------

        private static void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    // Blocks until a request arrives or the listener is stopped.
                    context = _listener.GetContext();
                }
                catch (HttpListenerException) when (!_running)
                {
                    // Normal shutdown: listener was stopped while waiting.
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogError($"[MCP Bridge] Listener error: {ex.Message}");
                    break;
                }

                // Hand off to the thread pool immediately so the loop keeps
                // accepting new connections without waiting for the handler.
                ThreadPool.QueueUserWorkItem(_ => HandleRequestAsync(context));
            }
        }

        // -----------------------------------------------------------------------
        // Request handling  (thread-pool thread)
        // -----------------------------------------------------------------------

        private static async void HandleRequestAsync(HttpListenerContext context)
        {
            McpResponse response;
            int statusCode;

            try
            {
                response = CheckRequest(context.Request);
                if (response == null)
                {
                    var job = CreateJob(context.Request);
                    McpJob.Current = job;
                    var routeTask = McpRouter.RouteAsync(context.Request);
                    McpJob.Current = null;

                    if (await Task.WhenAny(routeTask, Task.Delay(TimeSpan.FromSeconds(HandoffSeconds))) != routeTask)
                    {
                        // Too long to wait for: hand the caller the job and keep the result for later.
                        McpJobStore.Add(job);
                        _ = routeTask.ContinueWith(t => job.Complete(t.Status == TaskStatus.RanToCompletion
                            ? t.Result
                            : McpResponse.Fail("INTERNAL_ERROR", t.Exception?.GetBaseException().Message ?? "The operation failed.")));
                        await WriteResponseAsync(context, McpResponse.JobPending(job.ToJson(includeResult: false)), 202);
                        return;
                    }

                    response = await routeTask;
                    job.Complete(response);
                }
                statusCode = response.Success ? 200 : MapErrorToStatus(response.Error?.Code);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Bridge] Unexpected error handling request: {ex}");
                response   = McpResponse.Fail("INTERNAL_ERROR", ex.Message);
                statusCode = 500;
            }

            await WriteResponseAsync(context, response, statusCode);
        }

        /// <summary>A job for the request, named after the tool that owns its route when there is one.</summary>
        private static McpJob CreateJob(HttpListenerRequest request)
        {
            var method = request.HttpMethod.ToUpperInvariant();
            var path   = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;
            McpToolCatalog.TryGetByRoute(method, path, out var tool);
            return new McpJob(method, path, tool?.Name);
        }

        /// <summary>
        /// Returns a failure response when the request must be refused, or null when
        /// it may be routed.
        /// </summary>
        private static McpResponse CheckRequest(HttpListenerRequest request)
        {
            if (!string.IsNullOrEmpty(request.Headers["Origin"]))
                return McpResponse.Fail("FORBIDDEN",
                    "Requests from web pages are not accepted by the MCP bridge.");

            if (!McpBridgeAuth.IsAuthorized(request))
                return McpResponse.Fail("UNAUTHORIZED",
                    "Missing or wrong bridge token. Send 'Authorization: Bearer <token>'. " +
                    $"On the Unity machine the token is in {McpBridgeAuth.TokenFilePath}; " +
                    "Eadon > MCP Bridge > Copy Bridge Token copies it. " +
                    "A client on another machine sets it in UNITY_MCP_TOKEN.");

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                var contentType = request.ContentType ?? string.Empty;
                if (!contentType.TrimStart().StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                    return McpResponse.Fail("UNSUPPORTED_MEDIA_TYPE",
                        "POST requests must use Content-Type: application/json.");
            }

            return null;
        }

        private static async Task WriteResponseAsync(HttpListenerContext context, McpResponse response, int statusCode)
        {
            try
            {
                var json  = JsonConvert.SerializeObject(response);
                // No BOM — httpx and most HTTP clients do not expect one.
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);

                context.Response.StatusCode      = statusCode;
                context.Response.ContentType     = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;

                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Bridge] Failed to write response: {ex.Message}");
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static int MapErrorToStatus(string errorCode) => errorCode switch
        {
            "UNAUTHORIZED"           => 401,
            "FORBIDDEN"              => 403,
            "NOT_FOUND"              => 404,
            "UNSUPPORTED_MEDIA_TYPE" => 415,
            "INTERNAL_ERROR"         => 500,
            _                        => 400
        };
    }
}
