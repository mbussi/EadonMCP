"""
Procedural placement tools — Phase 6.

These tools place multiple objects in one round-trip, avoiding the per-object
HTTP latency of calling place_prefab in a loop. All operations in a single call
are grouped under one Undo entry.

scatter_prefabs  — distribute instances randomly within a circular area
place_along_path — place instances at regular intervals along a polyline
fill_grid        — fill a rectangular grid with instances
place_prefab_batch — place multiple different prefabs in one call
"""

import json
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def place_prefab_batch(items: list[dict], dry_run: bool = False) -> str:
    """
    Place multiple prefab instances in a single round-trip.

    Prefer this over repeated place_prefab calls when placing more than
    two or three objects — it is significantly faster and groups all
    placements under a single Undo entry.

    items:   List of placement descriptors. Each must have:
               prefab_path: str   — AssetDatabase path to the prefab
               position:    [x,y,z]
             And optionally:
               rotation:    [x,y,z]  (Euler degrees, default [0,0,0])
               scale:       [x,y,z]  (default [1,1,1])
               parent_path: str       (scene hierarchy path, default root)
    dry_run: If true, validates all items and returns expected results without
             creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/placement/place_batch", {
            "items":   items,
            "dry_run": dry_run,
        })
        placed  = data.get("placed_count", 0)
        failed  = data.get("failed_count",  0)
        total   = placed + failed
        dry_str = " [DRY RUN]" if dry_run else ""
        fail_str = f" ({failed} failed)" if failed else ""
        return f"Placed {placed}/{total} objects{fail_str}.{dry_str}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def scatter_prefabs(
    prefab_path: str,
    center: list[float],
    radius: float,
    count: int,
    min_spacing: float = 0.0,
    align_to_surface: bool = False,
    parent_path: str | None = None,
    seed: int | None = None,
    dry_run: bool = False,
) -> str:
    """
    Distribute prefab instances randomly within a circular area.

    prefab_path:      AssetDatabase path to the prefab to scatter.
    center:           World-space centre of the scatter area [x, y, z].
    radius:           Radius of the scatter circle in metres.
    count:            Number of instances to place.
    min_spacing:      Minimum distance in metres between any two instances.
                      Use 0 to allow clustering. The tool will place as many
                      as possible if min_spacing cannot be satisfied for all.
    align_to_surface: If true, raycasts downward from each position to snap
                      instances to the terrain or any collider below.
                      Requires colliders to be present in the scene.
    parent_path:      Scene hierarchy path of the parent object.
    seed:             Optional integer seed for reproducible placement.
    dry_run:          If true, returns the positions that would be used
                      without creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/procedural/scatter", {
            "prefab_path":       prefab_path,
            "center":            center,
            "radius":            radius,
            "count":             count,
            "min_spacing":       min_spacing,
            "align_to_surface":  align_to_surface,
            "parent_path":       parent_path,
            "seed":              seed,
            "dry_run":           dry_run,
        })
        placed  = data.get("placed_count", 0)
        dry_str = " [DRY RUN]" if dry_run else ""
        name    = prefab_path.split("/")[-1].replace(".prefab", "")
        return (
            f"Scattered {placed}/{count} instances of '{name}' "
            f"within {radius}m radius of ({center[0]:.1f}, {center[1]:.1f}, {center[2]:.1f}).{dry_str}"
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def place_along_path(
    prefab_path: str,
    waypoints: list[list[float]],
    spacing: float,
    align_to_path: bool = True,
    parent_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Place prefab instances at regular intervals along a polyline.

    Useful for walls, fences, roads, hedgerows, and corridors. The first
    instance is placed at the first waypoint; subsequent instances are
    spaced evenly along the path until the last waypoint is reached.

    prefab_path:   AssetDatabase path to the prefab.
    waypoints:     List of world-space points defining the path, e.g.
                   [[0,0,0], [10,0,0], [10,0,10]]. Minimum 2 points.
    spacing:       Distance in metres between instances.
    align_to_path: If true, instances rotate to face along the path direction.
                   Useful for directional objects like fence panels.
    parent_path:   Scene hierarchy path of the parent object.
    dry_run:       If true, returns expected placement positions without
                   creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/procedural/along_path", {
            "prefab_path":   prefab_path,
            "waypoints":     waypoints,
            "spacing":       spacing,
            "align_to_path": align_to_path,
            "parent_path":   parent_path,
            "dry_run":       dry_run,
        })
        placed     = data.get("placed_count", 0)
        path_len   = data.get("path_length",  0.0)
        dry_str    = " [DRY RUN]" if dry_run else ""
        name       = prefab_path.split("/")[-1].replace(".prefab", "")
        return (
            f"Placed {placed} instances of '{name}' along path "
            f"({len(waypoints)} waypoints, {path_len:.1f}m total).{dry_str}"
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def fill_grid(
    prefab_path: str,
    origin: list[float],
    cell_size: list[float],
    columns: int,
    rows: int,
    offset_alternate_rows: bool = False,
    parent_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Fill a rectangular grid area with prefab instances.

    origin:                World-space position of the grid's first cell [x, y, z].
    cell_size:             Size of each cell [width, depth] in metres (X and Z axes).
    columns:               Number of columns (X axis).
    rows:                  Number of rows (Z axis).
    offset_alternate_rows: If true, every other row is shifted by half a cell
                           width, producing a brick or hex-like pattern.
    parent_path:           Scene hierarchy path of the parent object.
    dry_run:               If true, returns expected positions without creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/procedural/fill_grid", {
            "prefab_path":            prefab_path,
            "origin":                 origin,
            "cell_size":              cell_size,
            "columns":                columns,
            "rows":                   rows,
            "offset_alternate_rows":  offset_alternate_rows,
            "parent_path":            parent_path,
            "dry_run":                dry_run,
        })
        placed  = data.get("placed_count", 0)
        dry_str = " [DRY RUN]" if dry_run else ""
        name    = prefab_path.split("/")[-1].replace(".prefab", "")
        return (
            f"Filled {columns}×{rows} grid with '{name}' "
            f"({placed} instances, cell size {cell_size[0]}×{cell_size[1]}m).{dry_str}"
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
