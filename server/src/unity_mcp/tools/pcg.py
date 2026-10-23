"""
PCG graph tools — node catalog discovery, graph creation, and prefab search.

get_pcg_node_catalog   — discover all PCG node types with ports and parameters
get_pcg_graphs         — list existing PCGGraph assets in the project
validate_pcg_graph     — validate a PCGG JSON document without creating assets
create_pcg_graph       — create a PCGGraph asset from a PCGG JSON document
search_prefabs_for_pcg — find prefabs by tag for populating SpawnActorNode
"""

import json

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def get_pcg_node_catalog() -> str:
    """
    Returns every PCG node type available in this Unity project.

    Call this FIRST before designing any PCG graph. The catalog contains
    for each node:
      canonical_name — the 'type' string to use in PCGG JSON
      category      — Sampling | Filter | Transform | Flow | Utility | Output | Parameters
      node_class     — "Algorithm" (portable) | "SceneBinding" (needs manual setup)
                      | "EditorOnly" | "Unregistered"
      inputs[]      — { field_name, display_name, port_type }
                      field_name goes in edge "toPort"
      outputs[]     — { field_name, display_name, port_type }
                      field_name goes in edge "fromPort"
      parameters[]  — { field_name, type, default_value, range_hint, tooltip }
      mutates_input  — if true, executor deep-clones input before this node runs

    Port types: "PCGPointData" (main), "bool", "float", "int",
    "PCGSurfaceData", "PCGSplineData" — only connect matching types.

    SceneBinding nodes (SpawnActor, TerrainSource, etc.) will be created by
    the importer but asset/scene references must be assigned manually after import.
    """
    try:
        data = await UnityClient.instance().get("/assets/pcg/node-catalog")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_pcg_graphs() -> str:
    """
    Lists all PCGGraph assets found in the Unity project.

    Returns name, asset_path, node_count, edge_count, and parameter_count for each.
    Call this before creating a graph to check for duplicates or to discover
    existing subgraph assets.
    """
    try:
        data = await UnityClient.instance().get("/assets/pcg/graphs")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def validate_pcg_graph(pcgg_json: str) -> str:
    """
    Validates a PCGG-format JSON graph document without creating any Unity assets.

    Run this before create_pcg_graph to catch errors:
      - Unknown canonical node type names
      - Edge fromPort/toPort field names that don't match actual node fields
      - Type mismatches between connected ports
      - Warnings for SceneBinding nodes that need manual asset assignment

    pcgg_json: A PCGGDocument JSON string. See create_pcg_graph for the full format.

    Returns: { valid, warnings[], errors[] }
    Errors block creation. Warnings are informational.
    """
    try:
        data = await UnityClient.instance().post("/tools/pcg/validate-graph", {
            "pcgg_json": pcgg_json,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_pcg_graph(
    pcgg_json: str,
    prefab_bindings: list[dict] | None = None,
    asset_path: str | None = None,
) -> str:
    """
    Creates a PCGGraph Unity .asset file from a PCGG-format JSON document,
    with optional automatic prefab assignment for SpawnActorNode instances.

    ── PCGG JSON FORMAT ──────────────────────────────────────────────────────

    {
      "version": 1,
      "graphName": "ForestFloor",
      "description": "Organic vegetation scatter on sloped terrain",
      "nodes": [
        {
          "type": "PoissonDiskSampler",
          "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
          "fields": [
            { "name": "MinDistance", "value": { "kind": "float", "floatVal": 1.5 } },
            { "name": "Bounds",      "value": { "kind": "bounds", "bounds": [0,0,0, 50,1,50] } }
          ]
        },
        {
          "type": "SlopeFilter",
          "guid": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
          "fields": [
            { "name": "MaxAngle", "value": { "kind": "float", "floatVal": 35.0 } }
          ]
        },
        {
          "type": "SpawnActor",
          "guid": "c3d4e5f6-a7b8-9012-cdef-123456789012",
          "fields": []
        }
      ],
      "edges": [
        { "fromGuid": "a1b2...", "fromPort": "OutputPoints",
          "toGuid":   "b2c3...", "toPort":   "InputPoints" },
        { "fromGuid": "b2c3...", "fromPort": "OutputPoints",
          "toGuid":   "c3d4...", "toPort":   "InputPoints" }
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

    { "kind": "float",  "floatVal":  1.5 }
    { "kind": "int",    "intVal":    10  }
    { "kind": "bool",   "boolVal":   true }
    { "kind": "string", "stringVal": "name" }
    { "kind": "vector3", "vec3": [x, y, z] }
    { "kind": "color",   "color": [r, g, b, a] }
    { "kind": "bounds",  "bounds": [cx,cy,cz, sx,sy,sz] }
    { "kind": "enum",    "enumVal": "ValueName" }

    ── PREFAB BINDINGS ───────────────────────────────────────────────────────

    Use prefab_bindings to populate SpawnActorNode.Prefabs automatically.
    Get asset_guid values from search_prefabs_for_pcg.

    prefab_bindings = [
      {
        "node_guid": "c3d4e5f6-...",
        "prefabs": [
          { "asset_guid": "abc123...", "weight": 1.0 },
          { "asset_guid": "def456...", "weight": 1.0 }
        ]
      }
    ]

    ── WORKFLOW ──────────────────────────────────────────────────────────────

    get_pcg_node_catalog → design graph → validate_pcg_graph → create_pcg_graph
    → open in Unity to assign prefabs/scene refs on SceneBinding nodes

    asset_path: Optional output path, e.g. "Assets/PCGGraphs/ForestFloor.asset".
                Defaults to "Assets/PCGGraphs/{graphName}.asset".
    """
    try:
        data = await UnityClient.instance().post("/tools/pcg/create-graph", {
            "pcgg_json":       pcgg_json,
            "prefab_bindings": _normalise_bindings(prefab_bindings),
            "asset_path":      asset_path,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


def _normalise_bindings(bindings: list[dict] | None) -> list[dict]:
    """
    Unity reads { "node_guid", "prefabs": [{ "asset_guid", "weight" }] }. Older
    docs said nodeGuid / assetGuid, so accept those spellings too.
    """
    def rename(d: dict, old: str, new: str) -> dict:
        if isinstance(d, dict) and new not in d and old in d:
            return {(new if k == old else k): v for k, v in d.items()}
        return d

    result = []
    for binding in bindings or []:
        binding = rename(binding, "nodeGuid", "node_guid")
        if isinstance(binding, dict) and isinstance(binding.get("prefabs"), list):
            binding = {**binding, "prefabs": [rename(p, "assetGuid", "asset_guid") for p in binding["prefabs"]]}
        result.append(binding)
    return result


async def search_prefabs_for_pcg(
    tags: list[str],
    category: str | None = None,
    max_results: int = 20,
) -> str:
    """
    Searches PrefabMetadata assets by tag and/or category.

    Use this when a prompt names a TYPE of object ("fence", "tree", "rock")
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
    category:    Optional AND filter, e.g. "prop", "vegetation", "building"
    max_results: Maximum results (default 20)
    """
    try:
        data = await UnityClient.instance().post("/tools/pcg/search-prefabs", {
            "tags":       tags,
            "category":   category or "",
            "max_results": max_results,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
