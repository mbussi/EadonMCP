using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tools
{
    /// <summary>
    /// Registers the PCG tools with the bridge, with the descriptions and argument
    /// schemas MCP clients and the in-editor agent see. This assembly only compiles when
    /// the Eadon MCP Bridge package is installed (see UnityMcpBridge.Pcg.Editor.asmdef),
    /// and it lives next to PCGFramework, so the bridge itself never depends on PCG.
    ///
    /// MCP clients reach these tools through the Python server's list_unity_tools and
    /// call_unity_tool; the in-editor agent offers them directly.
    /// </summary>
    [InitializeOnLoad]
    internal static class PcgRoutes
    {
        private const string Owner = "PCG extension";

        static PcgRoutes()
        {
            McpToolRegistry.Register(new McpToolDefinition
            {
                Name        = "get_pcg_node_catalog",
                Summary     = @"Returns every PCG node type available in this Unity project.",
                Description = @"Returns every PCG node type available in this Unity project.

Call this FIRST before designing any PCG graph. The catalog contains
for each node:
  canonical_name — the 'type' string to use in PCGG JSON
  category      — Sampling | Filter | Transform | Flow | Utility | Output | Parameters
  node_class     — ""Algorithm"" (portable) | ""SceneBinding"" (needs manual setup)
                  | ""EditorOnly"" | ""Unregistered""
  inputs[]      — { field_name, display_name, port_type }
                  field_name goes in edge ""toPort""
  outputs[]     — { field_name, display_name, port_type }
                  field_name goes in edge ""fromPort""
  parameters[]  — { field_name, type, default_value, range_hint, tooltip }
  mutates_input  — if true, executor deep-clones input before this node runs

Port types: ""PCGPointData"" (main), ""bool"", ""float"", ""int"",
""PCGSurfaceData"", ""PCGSplineData"" — only connect matching types.

SceneBinding nodes (SpawnActor, TerrainSource, etc.) will be created by
the importer but asset/scene references must be assigned manually after import.",
                InputSchema = JObject.Parse(@"{""properties"":{},""type"":""object""}"),
                Method      = "GET",
                Path        = "/assets/pcg/node-catalog",
                ReadOnly    = true,
                Owner       = Owner,
                Handler     = _ => PcgHandler.GetNodeCatalogAsync(),
            });

            McpToolRegistry.Register(new McpToolDefinition
            {
                Name        = "get_pcg_graphs",
                Summary     = @"Lists all PCGGraph assets found in the Unity project.",
                Description = @"Lists all PCGGraph assets found in the Unity project.

Returns name, asset_path, node_count, edge_count, and parameter_count for each.
Call this before creating a graph to check for duplicates or to discover
existing subgraph assets.",
                InputSchema = JObject.Parse(@"{""properties"":{},""type"":""object""}"),
                Method      = "GET",
                Path        = "/assets/pcg/graphs",
                ReadOnly    = true,
                Owner       = Owner,
                Handler     = _ => PcgHandler.GetGraphListAsync(),
            });

            McpToolRegistry.Register(new McpToolDefinition
            {
                Name        = "validate_pcg_graph",
                Summary     = @"Validates a PCGG-format JSON graph document without creating any Unity assets.",
                Description = @"Validates a PCGG-format JSON graph document without creating any Unity assets.

Run this before create_pcg_graph to catch errors:
  - Unknown canonical node type names
  - Edge fromPort/toPort field names that don't match actual node fields
  - Type mismatches between connected ports
  - Warnings for SceneBinding nodes that need manual asset assignment

pcgg_json: A PCGGDocument JSON string. See create_pcg_graph for the full format.

Returns: { valid, warnings[], errors[] }
Errors block creation. Warnings are informational.",
                InputSchema = JObject.Parse(@"{""properties"":{""pcgg_json"":{""type"":""string""}},""required"":[""pcgg_json""],""type"":""object""}"),
                Method      = "POST",
                Path        = "/tools/pcg/validate-graph",
                ReadOnly    = true,
                Owner       = Owner,
                Handler     = args => PcgHandler.ValidateGraphAsync(args.ToObject<PcgValidateRequest>()),
            });

            McpToolRegistry.Register(new McpToolDefinition
            {
                Name        = "create_pcg_graph",
                Summary     = @"Creates a PCGGraph Unity .asset file from a PCGG-format JSON document,
with optional automatic prefab assignment for SpawnActorNode instances.",
                Description = @"Creates a PCGGraph Unity .asset file from a PCGG-format JSON document,
with optional automatic prefab assignment for SpawnActorNode instances.

── PCGG JSON FORMAT ──────────────────────────────────────────────────────

{
  ""version"": 1,
  ""graphName"": ""ForestFloor"",
  ""description"": ""Organic vegetation scatter on sloped terrain"",
  ""nodes"": [
    {
      ""type"": ""PoissonDiskSampler"",
      ""guid"": ""a1b2c3d4-e5f6-7890-abcd-ef1234567890"",
      ""fields"": [
        { ""name"": ""MinDistance"", ""value"": { ""kind"": ""float"", ""floatVal"": 1.5 } },
        { ""name"": ""Bounds"",      ""value"": { ""kind"": ""bounds"", ""bounds"": [0,0,0, 50,1,50] } }
      ]
    },
    {
      ""type"": ""SlopeFilter"",
      ""guid"": ""b2c3d4e5-f6a7-8901-bcde-f12345678901"",
      ""fields"": [
        { ""name"": ""MaxAngle"", ""value"": { ""kind"": ""float"", ""floatVal"": 35.0 } }
      ]
    },
    {
      ""type"": ""SpawnActor"",
      ""guid"": ""c3d4e5f6-a7b8-9012-cdef-123456789012"",
      ""fields"": []
    }
  ],
  ""edges"": [
    { ""fromGuid"": ""a1b2..."", ""fromPort"": ""OutputPoints"",
      ""toGuid"":   ""b2c3..."", ""toPort"":   ""InputPoints"" },
    { ""fromGuid"": ""b2c3..."", ""fromPort"": ""OutputPoints"",
      ""toGuid"":   ""c3d4..."", ""toPort"":   ""InputPoints"" }
  ]
}

── RULES ─────────────────────────────────────────────────────────────────

1. Call get_pcg_node_catalog first — 'type' must be a canonical_name.
2. Edge fromPort/toPort must exactly match field_name from catalog outputs/inputs.
3. Only connect ports with matching port_type.
4. Generate a UUID4 for each node guid — must be unique within the document.
5. Include only non-default field values — omit fields to keep the C# default.
6. Canvas positions are optional — auto-layout is applied if omitted.

── VALUE KINDS ───────────────────────────────────────────────────────────

{ ""kind"": ""float"",  ""floatVal"":  1.5 }
{ ""kind"": ""int"",    ""intVal"":    10  }
{ ""kind"": ""bool"",   ""boolVal"":   true }
{ ""kind"": ""string"", ""stringVal"": ""name"" }
{ ""kind"": ""vector3"", ""vec3"": [x, y, z] }
{ ""kind"": ""color"",   ""color"": [r, g, b, a] }
{ ""kind"": ""bounds"",  ""bounds"": [cx,cy,cz, sx,sy,sz] }
{ ""kind"": ""enum"",    ""enumVal"": ""ValueName"" }

── PREFAB BINDINGS ───────────────────────────────────────────────────────

Use prefab_bindings to populate SpawnActorNode.Prefabs automatically.
Get asset_guid values from search_prefabs_for_pcg.

prefab_bindings = [
  {
    ""node_guid"": ""c3d4e5f6-..."",
    ""prefabs"": [
      { ""asset_guid"": ""abc123..."", ""weight"": 1.0 },
      { ""asset_guid"": ""def456..."", ""weight"": 1.0 }
    ]
  }
]

── WORKFLOW ──────────────────────────────────────────────────────────────

get_pcg_node_catalog → design graph → validate_pcg_graph → create_pcg_graph
→ open in Unity to assign prefabs/scene refs on SceneBinding nodes

asset_path: Optional output path, e.g. ""Assets/PCGGraphs/ForestFloor.asset"".
            Defaults to ""Assets/PCGGraphs/{graphName}.asset"".",
                InputSchema = JObject.Parse(@"{""properties"":{""pcgg_json"":{""type"":""string""},""prefab_bindings"":{""anyOf"":[{""items"":{""additionalProperties"":true,""type"":""object""},""type"":""array""},{""type"":""null""}],""default"":null},""asset_path"":{""anyOf"":[{""type"":""string""},{""type"":""null""}],""default"":null}},""required"":[""pcgg_json""],""type"":""object""}"),
                Method      = "POST",
                Path        = "/tools/pcg/create-graph",
                ReadOnly    = false,
                Owner       = Owner,
                Handler     = args => PcgHandler.CreateGraphAsync(args.ToObject<PcgCreateRequest>()),
            });

            McpToolRegistry.Register(new McpToolDefinition
            {
                Name        = "search_prefabs_for_pcg",
                Summary     = @"Searches PrefabMetadata assets by tag and/or category.",
                Description = @"Searches PrefabMetadata assets by tag and/or category.

Use this when a prompt names a TYPE of object (""fence"", ""tree"", ""rock"")
rather than a specific asset. Returns asset GUIDs for prefab_bindings in
create_pcg_graph, plus placement guidance to inform graph parameters.

Each result includes:
  asset_guid        — pass into prefab_bindings in create_pcg_graph
  asset_path        — human-readable path
  display_name      — short label
  description      — author-written description; read this to decide fitness
  approximate_size  — [x, y, z] bounding box in world units
                     → for fences: use x as SplineSamplerNode.Spacing
                     → for vegetation: use x as PoissonDiskSamplerNode.MinDistance
  placement        — { minimum_spacing, requires_flat_ground, ... }
  compatible_with   — tags of assets that pair well
  incompatible_with — tags to avoid in the same graph

For natural variation, include 2–4 variants in prefab_bindings at equal weight.

tags:        At least one tag must match (OR logic). Case-insensitive partial match.
category:    Optional AND filter, e.g. ""prop"", ""vegetation"", ""building""
max_results: Maximum results (default 20)",
                InputSchema = JObject.Parse(@"{""properties"":{""tags"":{""items"":{""type"":""string""},""type"":""array""},""category"":{""anyOf"":[{""type"":""string""},{""type"":""null""}],""default"":null},""max_results"":{""default"":20,""type"":""integer""}},""required"":[""tags""],""type"":""object""}"),
                Method      = "POST",
                Path        = "/tools/pcg/search-prefabs",
                ReadOnly    = true,
                Owner       = Owner,
                Handler     = args => PcgHandler.SearchPrefabsAsync(args.ToObject<PcgSearchPrefabsRequest>()),
            });
        }
    }
}
