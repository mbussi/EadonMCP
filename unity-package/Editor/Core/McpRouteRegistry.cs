using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>Handles one bridge route. Called on a thread-pool thread: use MainThreadDispatcher for Unity APIs.</summary>
    public delegate Task<McpResponse> McpRouteHandler(HttpListenerRequest request);

    /// <summary>
    /// Routes added by other assemblies (extensions), so they can serve tools
    /// without the bridge referencing them.
    ///
    /// Register from an [InitializeOnLoad] static constructor; the registry is
    /// rebuilt on every domain reload. Read a JSON body with McpRouter.ParseBodyAsync.
    ///
    ///     [InitializeOnLoad]
    ///     static class MyRoutes
    ///     {
    ///         static MyRoutes() =>
    ///             McpRouteRegistry.Register("POST", "/tools/my/thing", MyHandler.DoThingAsync, "My Extension");
    ///     }
    ///
    /// The bridge's own routes are matched first, so an extension cannot replace them.
    /// </summary>
    public static class McpRouteRegistry
    {
        /// <summary>One registered route.</summary>
        public readonly struct Route
        {
            public readonly string          Method;
            public readonly string          Path;
            public readonly string          Owner;
            public readonly McpRouteHandler Handler;

            public Route(string method, string path, string owner, McpRouteHandler handler)
            {
                Method  = method;
                Path    = path;
                Owner   = owner;
                Handler = handler;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Route> Routes = new Dictionary<string, Route>();

        /// <summary>Adds a route. Registering the same method and path again replaces it, with a warning.</summary>
        public static void Register(string method, string path, McpRouteHandler handler, string owner)
        {
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(path) || handler == null)
                throw new ArgumentException("method, path and handler are required.");

            method = method.Trim().ToUpperInvariant();
            path   = Normalise(path);
            var key = Key(method, path);

            lock (Gate)
            {
                if (Routes.TryGetValue(key, out var existing))
                    Debug.LogWarning($"[MCP Bridge] {owner} replaced the route {method} {path} registered by {existing.Owner}.");
                Routes[key] = new Route(method, path, owner, handler);
            }
        }

        /// <summary>Finds a registered route. Safe to call from any thread.</summary>
        public static bool TryGetHandler(string method, string path, out McpRouteHandler handler)
        {
            lock (Gate)
            {
                if (Routes.TryGetValue(Key(method.ToUpperInvariant(), Normalise(path)), out var route))
                {
                    handler = route.Handler;
                    return true;
                }
            }
            handler = null;
            return false;
        }

        /// <summary>A snapshot of every registered route.</summary>
        public static List<Route> GetRoutes()
        {
            lock (Gate)
                return new List<Route>(Routes.Values);
        }

        private static string Normalise(string path)
        {
            path = path.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(path) ? "/" : path;
        }

        private static string Key(string method, string path) => method + " " + path;
    }
}
