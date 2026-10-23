"""
Lighting tools — Phase 4.

Covers individual light creation and scene-wide settings (ambient, skybox, fog).

Note on URP: create_light works fully in URP. set_fog uses RenderSettings which
has partial URP support — it controls the legacy fog pass. For full URP fog,
use a Volume with a Fog override instead. set_ambient and set_skybox work
normally in URP.
"""

import json
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def create_light(
    light_type: str,
    position: list[float],
    rotation: list[float] | None = None,
    color: list[float] | None = None,
    intensity: float | None = None,
    range: float | None = None,
    spot_angle: float | None = None,
    shadows: str | None = None,
    name: str | None = None,
    parent_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Create a light source in the scene.

    light_type:  "Point", "Spot", or "Directional".
                 Area lights are not supported as they require baked lighting.
    position:    World-space position [x, y, z].
    rotation:    Euler angles in degrees [x, y, z]. For directional and spot
                 lights, rotation controls the direction of the light.
    color:       Light colour as linear RGB [r, g, b], values 0.0–1.0.
                 Defaults to white [1, 1, 1].
    intensity:   Light intensity. Defaults to 1.0.
    range:       Radius of influence in metres (Point and Spot only). Defaults to 10.0.
    spot_angle:  Cone angle in degrees (Spot only). Defaults to 30.0.
    shadows:     Shadow mode: "None", "Hard", or "Soft". Defaults to "Soft".
    name:        Name of the new GameObject. Defaults to "<Type> Light".
    parent_path: Scene hierarchy path of the parent object.
    dry_run:     If true, validates and returns the expected result without creating anything.
    """
    try:
        data = await UnityClient.instance().post("/tools/lighting/create_light", {
            "light_type":  light_type,
            "position":    position,
            "rotation":    rotation,
            "color":       color,
            "intensity":   intensity,
            "range":       range,
            "spot_angle":  spot_angle,
            "shadows":     shadows,
            "name":        name,
            "parent_path": parent_path,
            "dry_run":     dry_run,
        })
        dry    = data.get("dry_run", False)
        prefix = "[DRY RUN] Would have created" if dry else "Created"
        ltype  = data.get("light_type", light_type)
        dname  = data.get("display_name", name or f"{light_type} Light")
        path   = data.get("instance_path", "")
        pos    = data.get("position")
        pos_s  = f" at ({pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})" if pos else ""
        suffix = " No changes made." if dry else ""
        return f"{prefix} {ltype} Light '{dname}'{pos_s}. Scene path: '{path}'.{suffix}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_ambient_light(
    mode: str,
    color: list[float] | None = None,
    sky_color: list[float] | None = None,
    equator_color: list[float] | None = None,
    ground_color: list[float] | None = None,
    intensity: float | None = None,
    dry_run: bool = False,
) -> str:
    """
    Configure the scene's ambient lighting.

    mode:          Ambient light mode:
                     "Flat"     — single flat colour (use `color`).
                     "Trilight" — sky/equator/ground gradient
                                  (use `sky_color`, `equator_color`, `ground_color`).
                     "Skybox"   — driven by the active skybox (no colour input needed).
    color:         Flat ambient colour, linear RGB [r, g, b]. Used in Flat mode.
    sky_color:     Sky colour for Trilight mode.
    equator_color: Equator colour for Trilight mode.
    ground_color:  Ground colour for Trilight mode.
    intensity:     Ambient intensity multiplier. Defaults to 1.0.
    dry_run:       If true, validates without applying changes.
    """
    try:
        data = await UnityClient.instance().post("/tools/lighting/set_ambient", {
            "mode":          mode,
            "color":         color,
            "sky_color":     sky_color,
            "equator_color": equator_color,
            "ground_color":  ground_color,
            "intensity":     intensity,
            "dry_run":       dry_run,
        })
        dry    = data.get("dry_run", False)
        prefix = "[DRY RUN] Would have set" if dry else "Set"
        suffix = " No changes made." if dry else ""
        return f"{prefix} ambient light mode to '{mode}'.{suffix}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_skybox(
    material_path: str | None = None,
    dry_run: bool = False,
) -> str:
    """
    Assign a skybox material to the scene, or clear the current skybox.

    material_path: AssetDatabase path to a skybox Material asset,
                   e.g. "Assets/Materials/Skyboxes/NightSky.mat".
                   Pass null or omit to clear the skybox.
    dry_run:       If true, validates the material path without applying the change.
    """
    try:
        data = await UnityClient.instance().post("/tools/lighting/set_skybox", {
            "material_path": material_path,
            "dry_run":       dry_run,
        })
        dry    = data.get("dry_run", False)
        prefix = "[DRY RUN] Would have assigned" if dry else "Assigned"
        mat    = material_path or "(none — skybox cleared)"
        suffix = " No changes made." if dry else ""
        return f"{prefix} skybox: '{mat}'.{suffix}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def set_fog(
    enabled: bool,
    color: list[float] | None = None,
    mode: str | None = None,
    density: float | None = None,
    start: float | None = None,
    end: float | None = None,
    dry_run: bool = False,
) -> str:
    """
    Configure scene fog via RenderSettings.

    Note: In URP projects, this controls the legacy fog pass. For full
    URP fog, add a Fog override to a Volume in the scene instead.

    enabled: True to enable fog, false to disable.
    color:   Fog colour, linear RGB [r, g, b].
    mode:    Fog falloff mode: "Linear", "Exponential", or "ExponentialSquared".
    density: Fog density (Exponential and ExponentialSquared modes).
    start:   Fog start distance in metres (Linear mode only).
    end:     Fog end distance in metres (Linear mode only).
    dry_run: If true, validates without applying changes.
    """
    try:
        data = await UnityClient.instance().post("/tools/lighting/set_fog", {
            "enabled": enabled,
            "color":   color,
            "mode":    mode,
            "density": density,
            "start":   start,
            "end":     end,
            "dry_run": dry_run,
        })
        dry    = data.get("dry_run", False)
        state  = "enabled" if enabled else "disabled"
        prefix = f"[DRY RUN] Would have set fog to {state}" if dry else f"Fog {state}"
        suffix = ". No changes made." if dry else "."
        extras = f" Mode: {mode}." if mode else ""
        return f"{prefix}{extras}{suffix}"
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_reflection_probe(
    position:    list[float],
    size:        list[float]   | None = None,
    mode:        str                  = "baked",
    importance:  int                  = 1,
    intensity:   float         | None = None,
    hdr:         bool          | None = None,
    resolution:  int                  = 128,
    parent_path: str           | None = None,
    name:        str           | None = None,
) -> str:
    """
    Create a ReflectionProbe at a world position with a configurable influence volume.

    In open-world levels, reflection probes are placed by the dozen — one per room,
    one per archway, one per metallic or wet surface — always with the same setup.
    This tool removes per-probe Inspector work.

    After placing a batch of probes, trigger a lighting bake:
        bake_lighting(mode="async", bake_reflection_probes=True)

    Parameters
    ----------
    position   : [x, y, z] world-space centre of the probe and its influence box
    size       : [w, h, d] extents of the influence volume in world units.
                 Default [10, 5, 10]. The probe's influence is a box centred on position.
    mode       : "baked"    — captures lighting at bake time (default; cheapest at runtime)
                 "realtime" — re-captures every frame (expensive; use sparingly)
                 "custom"   — uses a manually assigned cubemap texture
    importance : priority when probe volumes overlap; higher wins. Default 1.
                 Use 2+ for small interior probes that should override a larger outdoor probe.
    intensity  : reflection intensity multiplier 0.0–1.0. Default 1.0.
    hdr        : use HDR cubemap format. Default True.
    resolution : cubemap capture resolution. Must be power of 2 from 16 to 2048.
                 Default 128. Non-power-of-2 values are snapped to the nearest valid size.
                 Guideline: 128 for typical rooms, 256 for large or visually prominent spaces,
                 512+ only for hero shots.
    parent_path: hierarchy path of a parent object
    name       : GameObject name. Default "ReflectionProbe".

    Returns
    -------
    { object_path, mode, importance, size: [w,h,d], resolution }

    Example — outfit a tavern interior
    ------------------------------------
    create_reflection_probe(
        position=[8.0, 1.5, 14.0],
        size=[6.0, 3.5, 8.0],
        mode="baked",
        importance=2,
        resolution=256,
        parent_path="/World/Village/Tavern/Lighting",
        name="ReflectionProbe_TavernMain"
    )
    """
    try:
        data = await UnityClient.instance().post("/tools/world/create_reflection_probe", {
            "position":    position,
            "size":        size,
            "mode":        mode,
            "importance":  importance,
            "intensity":   intensity,
            "hdr":         hdr,
            "resolution":  resolution,
            "parent_path": parent_path,
            "name":        name,
        })
        return (
            f"Created {data.get('mode', mode)} ReflectionProbe at {position} "
            f"(importance {data.get('importance', importance)}, "
            f"size {data.get('size')}, "
            f"resolution {data.get('resolution', resolution)}). "
            f"Scene path: '{data.get('object_path')}'."
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
