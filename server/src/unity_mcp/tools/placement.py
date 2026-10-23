"""
Object placement tools — Phase 3.

All mutating tools support dry_run=True, which validates parameters and
returns the expected result without touching the scene. Use this to verify
a plan before committing, or to populate a proposal for human review.

All live operations are registered with Unity's Undo system — they can be
reversed with Ctrl+Z / Cmd+Z in the Editor.
"""

import json
from typing import Optional

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


def _fmt(data: dict) -> str:
    """Format a placement response dict as a human-readable confirmation string."""
    dry = data.get("dry_run", False)
    prefix = "[DRY RUN] Would have" if dry else ""
    suffix = " No changes made." if dry else ""

    action = data.get("action", "operated on object")
    name   = data.get("display_name", "unknown")
    path   = data.get("instance_path") or data.get("object_path", "")
    pos    = data.get("position")

    pos_str = f" at ({pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})" if pos else ""
    path_str = f" Scene path: '{path}'." if path else ""

    return f"{prefix} {action} '{name}'{pos_str}.{path_str}{suffix}".strip()


async def place_prefab(
    prefab_path: str,
    position: list[float],
    rotation: list[float] | None = None,
    scale: list[float] | None = None,
    parent_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Place a prefab instance in the current scene.

    prefab_path: AssetDatabase path, e.g. "Assets/Prefabs/City/Townhouse_A.prefab".
                 Use get_prefab_catalog to discover valid paths.
    position:    World-space position [x, y, z].
    rotation:    Euler angles in degrees [x, y, z]. Defaults to [0, 0, 0].
    scale:       Local scale [x, y, z]. Defaults to [1, 1, 1].
    parent_path: Scene hierarchy path of the parent object, e.g. "Environment/Buildings".
                 Use get_scene_hierarchy to discover valid paths. Leave empty for root.
    dry_run:     If true, validates and returns the expected result without modifying the scene.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/place_prefab", {
            "prefab_path": prefab_path,
            "position":    position,
            "rotation":    rotation,
            "scale":       scale,
            "parent_path": parent_path,
            "dry_run":     dry_run,
        })
        data["action"] = "placed"
        return _fmt(data)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_transform(
    object_path: str,
    position: list[float] | None = None,
    rotation: list[float] | None = None,
    scale: list[float] | None = None,
    dry_run: bool = False,
) -> str:
    """
    Set the world-space transform of an existing scene object.

    object_path: Scene hierarchy path, e.g. "Environment/Buildings/Townhouse_A".
                 Use get_scene_hierarchy to discover valid paths.
    position:    New world-space position [x, y, z]. Omit to leave unchanged.
    rotation:    New Euler angles in degrees [x, y, z]. Omit to leave unchanged.
    scale:       New local scale [x, y, z]. Omit to leave unchanged.
    dry_run:     If true, returns the resulting transform without applying it.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/set_transform", {
            "object_path": object_path,
            "position":    position,
            "rotation":    rotation,
            "scale":       scale,
            "dry_run":     dry_run,
        })
        data["action"] = "updated transform of"
        return _fmt(data)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def delete_object(
    object_path: str,
    dry_run: bool = False,
) -> str:
    """
    Delete a scene object and all its children.

    object_path: Scene hierarchy path of the object to delete, e.g. "Environment/TempMarker".
                 Use get_scene_hierarchy to discover valid paths.
    dry_run:     If true, confirms the object exists without deleting it.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/delete_object", {
            "object_path": object_path,
            "dry_run":     dry_run,
        })
        data["action"] = "deleted"
        return _fmt(data)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_empty(
    name: str,
    position: list[float] | None = None,
    parent_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Create an empty GameObject in the scene.

    Useful for creating organisational containers (e.g. "Environment", "Lighting")
    before placing child objects inside them.

    name:        Name of the new GameObject.
    position:    World-space position [x, y, z]. Defaults to [0, 0, 0].
    parent_path: Scene hierarchy path of the parent object. Leave empty for root.
    dry_run:     If true, validates and returns the expected result without creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/create_empty", {
            "name":        name,
            "position":    position,
            "parent_path": parent_path,
            "dry_run":     dry_run,
        })
        data["action"] = "created"
        return _fmt(data)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def replace_prefab(
    old_prefab_path:    str,
    new_prefab_path:    str,
    scope_path:         Optional[str]  = None,
    preserve_transform: Optional[bool] = None,
    preserve_name:      Optional[bool] = None,
    preserve_parent:    Optional[bool] = None,
    dry_run:            bool           = False,
) -> str:
    """
    Replace every instance of one prefab with another, preserving each
    instance's transform, name, and hierarchy parent by default.

    All replacements are grouped under a single named Undo entry
    ("{old} → {new}"), so the entire operation can be reversed at once
    with Cmd+Z in the Editor.

    Only prefab instance roots are matched — children inside a prefab and
    nested variant instances are not replaced individually.

    Parameters
    ----------
    old_prefab_path    : AssetDatabase path of the prefab being replaced
    new_prefab_path    : AssetDatabase path of the replacement prefab
    scope_path         : hierarchy path to restrict the search (default: whole scene)
    preserve_transform : keep original position, rotation, and scale (default True)
    preserve_name      : keep the instance's original GameObject name (default True)
    preserve_parent    : keep the original hierarchy parent (default True)
    dry_run            : return what would be replaced without modifying the scene

    Returns
    -------
    { replaced_count, dry_run, replaced: [{old_path, new_path}], warnings }

    The new_path typically equals old_path when names are preserved — only the
    underlying prefab asset changes.

    Example — placeholder to final asset swap
    ------------------------------------------
    # Art drop arrived — replace all placeholder houses
    replace_prefab(
        old_prefab_path="Assets/Prefabs/Placeholders/House_Block.prefab",
        new_prefab_path="Assets/Prefabs/Buildings/Townhouse_A.prefab",
        scope_path="/World/Village",
        dry_run=True   # check count first
    )
    # → { replaced_count: 12, dry_run: True, ... }

    replace_prefab(
        old_prefab_path="Assets/Prefabs/Placeholders/House_Block.prefab",
        new_prefab_path="Assets/Prefabs/Buildings/Townhouse_A.prefab",
        scope_path="/World/Village"
    )
    # → { replaced_count: 12, dry_run: False, ... }
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/placement/replace_prefab", {
            "old_prefab_path":    old_prefab_path,
            "new_prefab_path":    new_prefab_path,
            "scope_path":         scope_path,
            "preserve_transform": preserve_transform,
            "preserve_name":      preserve_name,
            "preserve_parent":    preserve_parent,
            "dry_run":            dry_run,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
