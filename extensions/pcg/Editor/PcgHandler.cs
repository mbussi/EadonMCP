using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PCGFramework.PCGG;
using PCGFramework.Runtime.Core;
using PCGFramework.Runtime.Nodes;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Metadata;
using UnityMcpBridge.Editor.PCG;
using PcgPrefabBinding = UnityMcpBridge.Editor.Core.PcgPrefabBinding;
using PcgPrefabEntry = UnityMcpBridge.Editor.Core.PcgPrefabEntry;
using PcgPrefabResult = UnityMcpBridge.Editor.Core.PcgPrefabResult;
using PcgPrefabSearchResponse = UnityMcpBridge.Editor.Core.PcgPrefabSearchResponse;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Routes to register in McpRouter:
    ///   GET  /assets/pcg/node-catalog   → GetNodeCatalogAsync()
    ///   GET  /assets/pcg/graphs         → GetGraphListAsync()
    ///   POST /tools/pcg/validate-graph  → ValidateGraphAsync(req)
    ///   POST /tools/pcg/create-graph    → CreateGraphAsync(req)
    ///   POST /tools/pcg/search-prefabs  → SearchPrefabsAsync(req)
    /// </summary>
    public static class PcgHandler
    {
        // -----------------------------------------------------------------------
        // GET /assets/pcg/node-catalog
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetNodeCatalogAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
                McpResponse.Ok(PcgNodeCatalogBuilder.Build()));

        // -----------------------------------------------------------------------
        // GET /assets/pcg/graphs
        // -----------------------------------------------------------------------

        public static Task<McpResponse> GetGraphListAsync() =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var guids    = AssetDatabase.FindAssets("t:PCGGraph");
                var response = new PcgGraphListResponse();

                foreach (var guid in guids)
                {
                    var path  = AssetDatabase.GUIDToAssetPath(guid);
                    var graph = AssetDatabase.LoadAssetAtPath<PCGGraph>(path);
                    if (graph == null) continue;

                    response.graphs.Add(new PcgGraphEntry
                    {
                        name           = graph.name,
                        assetPath      = path,
                        nodeCount      = graph.Nodes?.Count  ?? 0,
                        edgeCount      = graph.Edges?.Count  ?? 0,
                        parameterCount = graph.Parameters?.Count ?? 0,
                    });
                }

                return McpResponse.Ok(response);
            });

        // -----------------------------------------------------------------------
        // POST /tools/pcg/validate-graph
        // -----------------------------------------------------------------------

        public static Task<McpResponse> ValidateGraphAsync(PcgValidateRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.pcggJson))
                    return McpResponse.Fail("INVALID_PARAMS", "pcgg_json must not be empty.");

                var result = new PcgValidateResponse();

                PCGGDocument doc;
                try { doc = JsonConvert.DeserializeObject<PCGGDocument>(req.pcggJson); }
                catch (Exception ex)
                {
                    result.errors.Add($"JSON parse error: {ex.Message}");
                    result.valid = false;
                    return McpResponse.Ok(result);
                }

                if (doc == null)
                {
                    result.errors.Add("Document is null after parsing.");
                    result.valid = false;
                    return McpResponse.Ok(result);
                }

                if (doc.version > PCGGConstants.CURRENT_VERSION)
                    result.errors.Add($"Document version {doc.version} exceeds supported version {PCGGConstants.CURRENT_VERSION}.");

                // Validate node types and build a guid→Type map for edge checking
                var resolvedTypes = new Dictionary<string, Type>();
                foreach (var node in doc.nodes)
                {
                    if (string.IsNullOrEmpty(node.guid))
                    { result.errors.Add($"Node '{node.type}' has an empty guid."); continue; }

                    var unityType = PCGGNodeRegistry.UnityTypeFor(node.type);
                    if (unityType == null)
                        result.errors.Add($"Unknown node type '{node.type}' (guid: {Abbrev(node.guid)}). Check get_pcg_node_catalog.");
                    else
                        resolvedTypes[node.guid] = unityType;

                    var reg = PCGGNodeRegistry.GetByCanonical(node.type);
                    if (reg?.Class == PCGGNodeRegistry.NodeClass.SceneBinding)
                        result.warnings.Add($"Node '{node.type}' ({Abbrev(node.guid)}) is SceneBinding — asset/scene refs need manual assignment after import.");
                }

                // Validate edges
                var flags = BindingFlags.Instance | BindingFlags.Public;
                foreach (var edge in doc.edges)
                {
                    var fromNode = doc.nodes?.Find(n => n.guid == edge.fromGuid);
                    var toNode   = doc.nodes?.Find(n => n.guid == edge.toGuid);

                    if (fromNode == null) { result.errors.Add($"Edge references unknown fromGuid '{Abbrev(edge.fromGuid)}'."); continue; }
                    if (toNode   == null) { result.errors.Add($"Edge references unknown toGuid '{Abbrev(edge.toGuid)}'."); continue; }

                    if (resolvedTypes.TryGetValue(edge.fromGuid, out var fromType))
                    {
                        var fromField = fromType.GetField(edge.fromPort, flags);
                        if (fromField == null)
                            result.errors.Add($"'{fromNode.type}' has no field '{edge.fromPort}'. Check outputs[].field_name in the catalog.");
                        else if (!fromField.IsDefined(typeof(PCGOutputAttribute), true))
                            result.errors.Add($"'{edge.fromPort}' on '{fromNode.type}' is not a [PCGOutput] port.");
                        else if (resolvedTypes.TryGetValue(edge.toGuid, out var toType))
                        {
                            var toField = toType.GetField(edge.toPort, flags);
                            if (toField == null)
                                result.errors.Add($"'{toNode.type}' has no field '{edge.toPort}'. Check inputs[].field_name in the catalog.");
                            else if (!toField.IsDefined(typeof(PCGInputAttribute), true))
                                result.errors.Add($"'{edge.toPort}' on '{toNode.type}' is not a [PCGInput] port.");
                            else if (fromField.FieldType != toField.FieldType)
                                result.errors.Add($"Type mismatch: '{fromNode.type}.{edge.fromPort}' ({fromField.FieldType.Name}) → '{toNode.type}.{edge.toPort}' ({toField.FieldType.Name}).");
                        }
                    }
                }

                result.valid = result.errors.Count == 0;
                return McpResponse.Ok(result);
            });

        // -----------------------------------------------------------------------
        // POST /tools/pcg/create-graph
        // -----------------------------------------------------------------------

        public static Task<McpResponse> CreateGraphAsync(PcgCreateRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                if (string.IsNullOrWhiteSpace(req?.pcggJson))
                    return McpResponse.Fail("INVALID_PARAMS", "pcgg_json must not be empty.");

                // Resolve asset path
                string assetPath = req.assetPath;
                if (string.IsNullOrEmpty(assetPath))
                {
                    PCGGDocument docForName;
                    try { docForName = JsonConvert.DeserializeObject<PCGGDocument>(req.pcggJson); }
                    catch { docForName = null; }
                    assetPath = $"Assets/PCGGraphs/{SanitiseFileName(docForName?.graphName ?? "NewPCGGraph")}.asset";
                }

                var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    CreateFolderRecursive(dir);

                PCGGImporter.ImportResult importResult;
                try { importResult = PCGGImporter.ImportToNewAsset(req.pcggJson, assetPath); }
                catch (Exception ex)
                { return McpResponse.Fail("IMPORT_ERROR", $"PCGGImporter threw: {ex.Message}"); }

                if (!importResult.Success || importResult.Graph == null)
                {
                    var failResponse = new PcgCreateResponse();
                    failResponse.nodesCreated = importResult.NodesCreated;
                    failResponse.edgesWired   = importResult.EdgesWired;
                    if (importResult.Warnings != null) failResponse.warnings.AddRange(importResult.Warnings);
                    if (importResult.Errors   != null) failResponse.errors.AddRange(importResult.Errors);
                    return McpResponse.Ok(failResponse);
                }

                // Auto-layout if the document has no canvas positions
                PCGGDocument doc;
                try { doc = JsonConvert.DeserializeObject<PCGGDocument>(req.pcggJson); }
                catch { doc = null; }

                if (doc != null && !HasNonZeroPositions(doc))
                    AutoLayout(importResult.Graph, doc);

                // Apply prefab bindings
                var response = new PcgCreateResponse
                {
                    assetPath    = assetPath,
                    nodesCreated = importResult.NodesCreated,
                    edgesWired   = importResult.EdgesWired,
                };
                if (importResult.Warnings != null) response.warnings.AddRange(importResult.Warnings);
                if (importResult.Errors   != null) response.errors.AddRange(importResult.Errors);

                if (req.prefabBindings != null && req.prefabBindings.Count > 0)
                    ApplyPrefabBindings(importResult.Graph, req.prefabBindings, response.warnings, response.errors);

                EditorUtility.SetDirty(importResult.Graph);
                AssetDatabase.SaveAssets();

                return McpResponse.Ok(response);
            });

        // -----------------------------------------------------------------------
        // POST /tools/pcg/search-prefabs
        // -----------------------------------------------------------------------

        public static Task<McpResponse> SearchPrefabsAsync(PcgSearchPrefabsRequest req) =>
            MainThreadDispatcher.EnqueueAsync<McpResponse>(() =>
            {
                var tagFilter = new List<string>();
                if (req?.tags != null)
                    foreach (var t in req.tags)
                        if (!string.IsNullOrWhiteSpace(t)) tagFilter.Add(t.Trim().ToLowerInvariant());

                var categoryFilter = (req?.category ?? string.Empty).Trim().ToLowerInvariant();
                var maxResults     = req?.maxResults > 0 ? req.maxResults : 20;

                var guids    = AssetDatabase.FindAssets("t:PrefabMetadata");
                var response = new PcgPrefabSearchResponse();

                foreach (var guid in guids)
                {
                    var metaPath = AssetDatabase.GUIDToAssetPath(guid);
                    var meta     = AssetDatabase.LoadAssetAtPath<PrefabMetadata>(metaPath);
                    if (meta?.prefab == null) continue;

                    if (!string.IsNullOrEmpty(categoryFilter) &&
                        !(meta.category ?? string.Empty).ToLowerInvariant().Contains(categoryFilter))
                        continue;

                    if (tagFilter.Count > 0)
                    {
                        bool anyMatch = false;
                        foreach (var required in tagFilter)
                            foreach (var t in meta.tags ?? Array.Empty<string>())
                                if (t.ToLowerInvariant().Contains(required)) { anyMatch = true; break; }
                        if (!anyMatch) continue;
                    }

                    var prefabPath = AssetDatabase.GetAssetPath(meta.prefab);
                    var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                    if (string.IsNullOrEmpty(prefabGuid)) continue;

                    var size  = meta.approximateSize;
                    var rules = meta.placement;
                    response.prefabs.Add(new PcgPrefabResult
                    {
                        assetGuid        = prefabGuid,
                        assetPath        = prefabPath,
                        displayName      = !string.IsNullOrEmpty(meta.displayName) ? meta.displayName : meta.prefab.name,
                        category         = meta.category    ?? string.Empty,
                        tags             = meta.tags        ?? Array.Empty<string>(),
                        description      = meta.description ?? string.Empty,
                        approximateSize  = new[] { size.x, size.y, size.z },
                        compatibleWith   = meta.compatibleWith   ?? Array.Empty<string>(),
                        incompatibleWith = meta.incompatibleWith ?? Array.Empty<string>(),
                        placement = rules == null ? new PlacementRulesData() : new PlacementRulesData
                        {
                            RequiresFlatGround = rules.requiresFlatGround,
                            CanStack           = rules.canStack,
                            IndoorOnly         = rules.indoorOnly,
                            OutdoorOnly        = rules.outdoorOnly,
                            MinimumSpacing     = rules.minimumSpacing,
                            RecommendedSpacing = new[] { rules.recommendedSpacing.x, rules.recommendedSpacing.y, rules.recommendedSpacing.z },
                        },
                    });

                    if (response.prefabs.Count >= maxResults) break;
                }

                return McpResponse.Ok(response);
            });

        // -----------------------------------------------------------------------
        // Prefab binding helpers
        // -----------------------------------------------------------------------

        private static void ApplyPrefabBindings(
            PCGGraph             graph,
            List<PcgPrefabBinding> bindings,
            List<string>         warnings,
            List<string>         errors)
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding?.nodeGuid)) continue;

                var node = graph.FindNode(binding.nodeGuid) as SpawnActorNode;
                if (node == null)
                {
                    warnings.Add($"SpawnActorNode '{Abbrev(binding.nodeGuid)}' not found in imported graph.");
                    continue;
                }

                node.Prefabs.Clear();
                foreach (var entry in binding.prefabs ?? new List<PcgPrefabEntry>())
                {
                    if (string.IsNullOrEmpty(entry?.assetGuid)) continue;
                    var path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                    if (string.IsNullOrEmpty(path)) { warnings.Add($"No asset for GUID '{Abbrev(entry.assetGuid)}'."); continue; }
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null) { warnings.Add($"Could not load GameObject at '{path}'."); continue; }
                    node.Prefabs.Add(new PCGPrefabEntry { Prefab = go, Weight = Mathf.Max(0.001f, entry.weight) });
                }
                EditorUtility.SetDirty(node);
            }
        }

        // -----------------------------------------------------------------------
        // Auto-layout
        // -----------------------------------------------------------------------

        private static bool HasNonZeroPositions(PCGGDocument doc)
        {
            foreach (var n in doc.nodes)
                if (n.canvasPosition != null && n.canvasPosition.Length >= 2 &&
                    (Math.Abs(n.canvasPosition[0]) > 0.5f || Math.Abs(n.canvasPosition[1]) > 0.5f))
                    return true;
            return false;
        }

        private static void AutoLayout(PCGGraph graph, PCGGDocument doc)
        {
            var levelOf  = new Dictionary<string, int>();
            var inDegree = new Dictionary<string, int>();
            var children = new Dictionary<string, List<string>>();

            foreach (var n in doc.nodes) { inDegree[n.guid] = 0; children[n.guid] = new List<string>(); }
            foreach (var e in doc.edges)
            {
                if (inDegree.ContainsKey(e.toGuid))   inDegree[e.toGuid]++;
                if (children.ContainsKey(e.fromGuid)) children[e.fromGuid].Add(e.toGuid);
            }

            var queue = new Queue<string>();
            foreach (var kv in inDegree)
                if (kv.Value == 0) { levelOf[kv.Key] = 0; queue.Enqueue(kv.Key); }

            while (queue.Count > 0)
            {
                var g = queue.Dequeue();
                foreach (var child in children[g])
                {
                    var nl = levelOf[g] + 1;
                    if (!levelOf.TryGetValue(child, out var el) || nl > el) levelOf[child] = nl;
                    if (--inDegree[child] == 0) queue.Enqueue(child);
                }
            }

            var countAtLevel = new Dictionary<int, int>();
            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                var level = levelOf.TryGetValue(node.NodeGuid, out var l) ? l : 0;
                if (!countAtLevel.ContainsKey(level)) countAtLevel[level] = 0;
                node.GraphPosition = new Vector2(level * 280f, countAtLevel[level]++ * 160f);
            }
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        private static void CreateFolderRecursive(string path)
        {
            var parts   = path.Replace('\\', '/').Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string SanitiseFileName(string name) =>
            string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();

        private static string Abbrev(string guid) =>
            guid?.Length >= 8 ? guid[..8] : guid ?? "(null)";
    }
}
