"""
Metadata authoring tools for the Unity MCP Bridge.

These tools close the loop that determines the quality of every other tool in
the system. dress_area, resolve_scaffold_markers, and get_prefab_catalog all
produce better results when PrefabMetadata assets are richly authored. These
tools mean that authoring can happen in the same conversation as placement.

  create_prefab_metadata — create a new PrefabMetadata asset for a prefab
  update_prefab_metadata — update specific fields on an existing asset
  update_scene_context   — write to the active SceneContext design document

After any create or update call, MetadataRegistry is reloaded immediately so
get_prefab_catalog and dress_area reflect the new data without an Editor restart.
"""

from __future__ import annotations

import json
from typing import Optional

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def create_prefab_metadata(
    prefab_path:          str,
    tags:                 Optional[list[str]] = None,
    description:          Optional[str]       = None,
    category:             Optional[str]       = None,
    display_name:         Optional[str]       = None,
    compatible_with:      Optional[list[str]] = None,
    incompatible_with:    Optional[list[str]] = None,
    requires_flat_ground: Optional[bool]      = None,
    can_stack:            Optional[bool]      = None,
    indoor_only:          Optional[bool]      = None,
    outdoor_only:         Optional[bool]      = None,
    minimum_spacing:      Optional[float]     = None,
    approximate_size:     Optional[list[float]] = None,
    overwrite:            bool                = False,
) -> str:
    """
    Create a PrefabMetadata ScriptableObject asset alongside a prefab.

    The asset is saved as Prefab_Name.metadata.asset in the same directory as
    the prefab, following the project convention. After creation, MetadataRegistry
    is reloaded so dress_area, resolve_scaffold_markers, and get_prefab_catalog
    immediately reflect the new metadata.

    The richer the metadata — especially description and tags — the better
    the AI's placement and scoring decisions will be.

    Parameters
    ----------
    prefab_path         : AssetDatabase path, e.g. "Assets/Prefabs/Props/Barrel.prefab"
    tags                : searchable tags, e.g. ["storage", "wooden", "breakable"]
    description         : plain-English description including appearance, scale, and
                          ideal placement context. Be specific — this is read verbatim
                          by dress_area and resolve_scaffold_markers.
    category            : broad category: "building", "prop", "vegetation",
                          "light-fixture", "terrain", etc.
    display_name        : human-readable name shown in catalog and responses.
                          Defaults to the prefab filename if omitted.
    compatible_with     : tags of other assets this pairs well with
    incompatible_with   : tags this asset clashes with (agent avoids combining these)
    requires_flat_ground: placement rule — must be placed on a flat surface (default True)
    can_stack           : multiple instances may be stacked vertically (default False)
    indoor_only         : only valid inside enclosed spaces (default False)
    outdoor_only        : only valid in open outdoor areas (default False)
    minimum_spacing     : minimum distance in metres between instances of this prefab
    approximate_size    : bounding box [width, height, depth] in world units
    overwrite           : replace existing metadata if one already exists (default False)

    Returns
    -------
    { prefab_path, metadata_path, created: true, overwritten: true|null }

    Workflow
    --------
    # After a new prefab art drop:
    get_prefab_catalog("Assets/Prefabs/Props/NewDrop")
    # → lists prefabs with has_metadata: false
    create_prefab_metadata(
        "Assets/Prefabs/Props/NewDrop/Barrel_Weathered.prefab",
        tags=["storage", "wooden", "weathered", "breakable"],
        description="A short weathered oak barrel. Suitable for taverns, docks, and storage yards. Pairs well with crates and rope coils.",
        category="prop",
        requires_flat_ground=True,
        minimum_spacing=0.3,
        approximate_size=[0.7, 0.9, 0.7]
    )
    # → metadata created, immediately available to dress_area
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/metadata/create_prefab_metadata", {
            "prefab_path":          prefab_path,
            "display_name":         display_name,
            "category":             category,
            "tags":                 tags,
            "description":          description,
            "compatible_with":      compatible_with,
            "incompatible_with":    incompatible_with,
            "requires_flat_ground": requires_flat_ground,
            "can_stack":            can_stack,
            "indoor_only":          indoor_only,
            "outdoor_only":         outdoor_only,
            "minimum_spacing":      minimum_spacing,
            "approximate_size":     approximate_size,
            "overwrite":            overwrite,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def update_prefab_metadata(
    prefab_path:          str,
    tags:                 Optional[list[str]]   = None,
    description:          Optional[str]         = None,
    category:             Optional[str]         = None,
    display_name:         Optional[str]         = None,
    compatible_with:      Optional[list[str]]   = None,
    incompatible_with:    Optional[list[str]]   = None,
    requires_flat_ground: Optional[bool]        = None,
    can_stack:            Optional[bool]        = None,
    indoor_only:          Optional[bool]        = None,
    outdoor_only:         Optional[bool]        = None,
    minimum_spacing:      Optional[float]       = None,
    approximate_size:     Optional[list[float]] = None,
) -> str:
    """
    Update specific fields on an existing PrefabMetadata asset.

    Only the fields you provide are changed — omitted fields are left exactly
    as they are on the existing asset. MetadataRegistry is reloaded after saving.

    Use this to refine metadata iteratively:
      - Add tags after new context or biome requirements emerge
      - Improve descriptions as the art style solidifies
      - Adjust placement rules after testing in-engine
      - Add compatible_with / incompatible_with entries as relationships become clear

    Parameters
    ----------
    prefab_path  : AssetDatabase path used to locate the existing metadata asset
    (all other parameters): see create_prefab_metadata — null = leave unchanged

    Returns
    -------
    { prefab_path, metadata_path, updated_fields: ["tags", "description", ...] }

    Example — add tags and expand description
    -----------------------------------------
    update_prefab_metadata(
        "Assets/Prefabs/Props/Barrel_Weathered.prefab",
        tags=["storage", "wooden", "weathered", "breakable", "tavern", "dock"],
        compatible_with=["crate", "rope-coil", "lantern"]
    )
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/metadata/update_prefab_metadata", {
            "prefab_path":          prefab_path,
            "display_name":         display_name,
            "category":             category,
            "tags":                 tags,
            "description":          description,
            "compatible_with":      compatible_with,
            "incompatible_with":    incompatible_with,
            "requires_flat_ground": requires_flat_ground,
            "can_stack":            can_stack,
            "indoor_only":          indoor_only,
            "outdoor_only":         outdoor_only,
            "minimum_spacing":      minimum_spacing,
            "approximate_size":     approximate_size,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def update_scene_context(
    level_name:           Optional[str]       = None,
    setting:              Optional[str]       = None,
    architectural_style:  Optional[str]       = None,
    era:                  Optional[str]       = None,
    active_tags:          Optional[list[str]] = None,
    add_active_tags:      Optional[list[str]] = None,
    remove_active_tags:   Optional[list[str]] = None,
    excluded_tags:        Optional[list[str]] = None,
    notes:                Optional[str]       = None,
    append_notes:         Optional[str]       = None,
) -> str:
    """
    Write to the active SceneContext asset — the level design document that
    Claude reads at the start of every session to guide placement decisions.

    Only provided fields are changed. The updated asset is saved to disk immediately
    so get_scene_context reflects the changes in the same session.

    Tag parameters
    --------------
    active_tags        : full replacement (overwrites existing list)
    add_active_tags    : append tags (ignored if active_tags is provided)
    remove_active_tags : remove specific tags (ignored if active_tags is provided)
    excluded_tags      : full replacement of the excluded list

    Notes parameters
    ----------------
    notes        : full replacement of the design notes field
    append_notes : append to existing notes (ignored if notes is provided);
                   adds a blank line separator before the new text

    Returns
    -------
    { context_path, updated_fields: ["active_tags", "notes", ...] }

    Example — session notes workflow
    ---------------------------------
    # At the end of a session, record what was done and what's still needed:
    update_scene_context(
        add_active_tags=["ruined", "overgrown"],
        remove_active_tags=["pristine"],
        append_notes="2024-06-08: Added ruined east wing. "
                     "Avoid intact furniture in Zone3 eastward. "
                     "LOD groups still needed on Townhouse_C and WarehouseA."
    )
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/metadata/update_scene_context", {
            "level_name":           level_name,
            "setting":              setting,
            "architectural_style":  architectural_style,
            "era":                  era,
            "active_tags":          active_tags,
            "add_active_tags":      add_active_tags,
            "remove_active_tags":   remove_active_tags,
            "excluded_tags":        excluded_tags,
            "notes":                notes,
            "append_notes":         append_notes,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
