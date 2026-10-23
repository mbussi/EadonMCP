using System;
using System.Collections.Generic;

namespace UnityMcpBridge.Editor.Core
{
    /// <summary>
    /// Thrown by PlacementHandler.FindByPath when a path matches more than one object
    /// (same-named siblings without an index, or the same path in several loaded scenes
    /// without a scene prefix). McpRouter turns it into an AMBIGUOUS_PATH error whose
    /// message lists the exact paths to use instead.
    /// </summary>
    public sealed class AmbiguousPathException : Exception
    {
        public AmbiguousPathException(string path, IReadOnlyList<string> candidates)
            : base($"The path '{path}' matches {candidates.Count} objects. Use one of: " +
                   string.Join(", ", candidates) + ".")
        {
            Path       = path;
            Candidates = candidates;
        }

        public string Path { get; }
        public IReadOnlyList<string> Candidates { get; }
    }
}
