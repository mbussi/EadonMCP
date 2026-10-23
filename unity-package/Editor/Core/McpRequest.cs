using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// A request to the bridge's router: what McpRouter, McpRouteRegistry handlers and
    /// extension tools read. It comes from HTTP (McpHttpListener) or is built from a
    /// tool's JSON arguments (a proposal operation), so every tool runs the same way
    /// however it is called.
    /// </summary>
    public sealed class McpRequest
    {
        private readonly HttpListenerRequest _http;
        private string _body;
        private bool   _bodyRead;

        private McpRequest(string method, string path, NameValueCollection query, HttpListenerRequest http, string body)
        {
            HttpMethod  = (method ?? "GET").Trim().ToUpperInvariant();
            Path        = NormalisePath(path);
            QueryString = query ?? new NameValueCollection();
            _http       = http;
            _body       = body;
            _bodyRead   = http == null;
        }

        /// <summary>"GET" or "POST" (upper case).</summary>
        public string HttpMethod { get; }

        /// <summary>The path without the query string or a trailing slash, e.g. "/tools/placement/place_prefab".</summary>
        public string Path { get; }

        /// <summary>The query-string parameters (GET arguments).</summary>
        public NameValueCollection QueryString { get; }

        /// <summary>True when the request came over HTTP rather than from a proposal.</summary>
        public bool IsHttp => _http != null;

        // -----------------------------------------------------------------------
        // Construction
        // -----------------------------------------------------------------------

        /// <summary>Wraps an HTTP request. The body is read on first use.</summary>
        public static McpRequest FromHttp(HttpListenerRequest request) =>
            new McpRequest(request.HttpMethod, request.Url?.AbsolutePath, request.QueryString, request, null);

        /// <summary>
        /// Builds the request a tool call with these arguments would send: a JSON body for
        /// POST, query parameters for GET.
        /// </summary>
        public static McpRequest FromArguments(string method, string path, JObject arguments)
        {
            arguments ??= new JObject();
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var query = new NameValueCollection();
                foreach (var property in arguments.Properties())
                    query[property.Name] = property.Value.Type == JTokenType.String
                        ? (string)property.Value
                        : property.Value.ToString(Formatting.None);
                return new McpRequest("GET", path, query, null, null);
            }
            return new McpRequest(method, path, null, null, arguments.ToString(Formatting.None));
        }

        // -----------------------------------------------------------------------
        // Body
        // -----------------------------------------------------------------------

        /// <summary>The raw body ("" when there is none). An HTTP body is read once and kept.</summary>
        public async Task<string> ReadBodyAsync()
        {
            if (_bodyRead) return _body ?? string.Empty;

            if (_http.ContentLength64 == 0)
            {
                _body = string.Empty;
            }
            else
            {
                using var reader = new StreamReader(_http.InputStream, Encoding.UTF8);
                _body = await reader.ReadToEndAsync();
            }
            _bodyRead = true;
            return _body;
        }

        /// <summary>The body as a JSON object (empty when there is no body).</summary>
        public async Task<JObject> ReadJsonAsync()
        {
            var json = await ReadBodyAsync();
            return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        }

        private static string NormalisePath(string path)
        {
            path = (path ?? string.Empty).Trim().TrimEnd('/');
            return string.IsNullOrEmpty(path) ? "/" : path;
        }
    }
}
