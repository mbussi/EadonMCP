"""
Production quality tools for the Unity MCP Bridge — Phase 1.

  validate_scene     — run a configurable diagnostic checklist across the active
                       scene and return a structured report of issues with object paths
  create_audio_zone  — create an AudioReverbZone (with preset) or a spatialised
                       AudioSource for ambient sound, both with configurable radii

Phase 2 (create_postprocessing_volume) will be added as a separate append to this file.
"""

from __future__ import annotations

import json
from typing import Optional

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def validate_scene(
    checks:                Optional[list[str]] = None,
    scope_path:            Optional[str]       = None,
    max_results_per_check: int                 = 50,
    light_range_threshold: float               = 100.0,
    floating_ray_distance: float               = 0.5,
) -> str:
    """
    Run diagnostic checks across the active scene and return a structured issue report.

    Each check returns a list of object hierarchy paths with detail strings. The agent
    can present this report conversationally and use other tools (set_object_metadata,
    snap_to_surface, etc.) to fix the flagged issues.

    Available check IDs
    -------------------
    "missing_scripts"
        error — GameObjects with missing MonoBehaviour script references.
        Common after a script rename, refactor, or package removal.

    "missing_materials"
        error — MeshRenderers with one or more null material slots.
        Common after a material asset is deleted.

    "untagged_triggers"
        warning — Trigger colliders whose GameObject has tag "Untagged".
        Almost always a forgotten configuration step.

    "duplicate_names"
        warning — Siblings with identical names under the same parent.
        Breaks FindByPath and causes confusing hierarchy navigation.

    "meshrenderer_no_collider"
        info — MeshRenderers with no Collider on the same object.
        Opt-in: generates false positives for decorative-only objects.

    "large_light_range"
        info — Point/Spot lights with range > light_range_threshold.
        Catches accidentally large values left from tweaking sessions.

    "floating_objects"
        info — Renderable objects with no surface detected within
        floating_ray_distance below their pivot. Catches props left
        floating after terrain was sculpted beneath them.
        Opt-in: generates false positives for intentionally elevated objects.

    Parameters
    ----------
    checks                : list of check IDs to run. None = run all checks.
    scope_path            : hierarchy path to restrict the scan. None = full active scene.
    max_results_per_check : cap on issues returned per check (default 50).
    light_range_threshold : range in metres for the large_light_range check (default 100.0).
    floating_ray_distance : downward ray distance in metres for floating_objects (default 0.5).

    Returns
    -------
    {
      total_issues : int,
      checks_run   : [str],
      results      : [
        {
          check_id    : str,
          severity    : "error" | "warning" | "info",
          description : str,
          count       : int,
          issues      : [{ path: str, detail: str }]
        }
      ]
    }

    Workflow example
    ----------------
    # End-of-session quality pass
    validate_scene(checks=["missing_scripts", "missing_materials", "untagged_triggers"])
    # → agent presents issues, then fixes tags with set_object_metadata and
    #   snaps floating props with snap_to_surface
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/scene/validate", {
            "checks":                 checks,
            "scope_path":             scope_path,
            "max_results_per_check":  max_results_per_check,
            "light_range_threshold":  light_range_threshold,
            "floating_ray_distance":  floating_ray_distance,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def create_audio_zone(
    zone_type:      str,
    position:       list[float],
    min_distance:   float           = 1.0,
    max_distance:   float           = 10.0,
    preset:         Optional[str]   = None,
    audio_clip_path:Optional[str]   = None,
    volume:         Optional[float] = None,
    loop:           Optional[bool]  = None,
    play_on_awake:  Optional[bool]  = None,
    parent_path:    Optional[str]   = None,
    name:           Optional[str]   = None,
) -> str:
    """
    Create an audio zone at a world-space position.

    zone_type="reverb_zone"
        Creates a GameObject with an AudioReverbZone component.
        Shapes the acoustic environment — adds echo, dampening, and spatial character
        to all audio sources within range. No AudioClip required.
        Use for caves, interiors, underwater, large outdoor spaces.

        preset is required. Available presets:
          "cave"        → Unity Cave reverb
          "forest"      → Forest (muffled, organic)
          "dungeon"     → Stone Corridor (stone, enclosed)
          "interior"    → Room (wooden/plaster interior)
          "exterior"    → Plain (open outdoor)
          "underwater"  → Underwater
          "cathedral"   → Concert Hall (large resonant space)
          "alley"       → Alley (narrow urban)

    zone_type="ambient_source"
        Creates a fully-3D AudioSource with linear rolloff and a SphereCollider trigger
        (trigger-only, for scene-view visualisation — no physics effect).
        Use for streams, campfires, wind, distant machinery.

        audio_clip_path is recommended. If omitted, the source is created without a clip
        and a warning is returned — assign a clip manually in the Inspector.

    Parameters
    ----------
    zone_type       : "reverb_zone" or "ambient_source"
    position        : [x, y, z] world-space centre
    min_distance    : inner radius in metres — full effect / direct sound (default 1.0)
    max_distance    : outer radius in metres — effect fades to zero (default 10.0)
    preset          : reverb preset name (required for reverb_zone)
    audio_clip_path : AssetDatabase path to AudioClip (for ambient_source)
    volume          : 0.0–1.0 playback volume for ambient_source (default 1.0)
    loop            : loop playback for ambient_source (default True)
    play_on_awake   : auto-play on scene start for ambient_source (default True)
    parent_path     : optional hierarchy parent path
    name            : optional GameObject name

    Biome transition example
    ------------------------
    # Dungeon entrance — acoustic + ambient in two calls
    create_audio_zone("reverb_zone", [0,2,0],
                      preset="dungeon", min_distance=3.0, max_distance=15.0)
    create_audio_zone("ambient_source", [0,2,0],
                      audio_clip_path="Assets/Audio/Ambient/DungeonDrips.wav",
                      min_distance=2.0, max_distance=12.0, volume=0.6)
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/create_audio_zone", {
            "zone_type":       zone_type,
            "position":        position,
            "min_distance":    min_distance,
            "max_distance":    max_distance,
            "preset":          preset,
            "audio_clip_path": audio_clip_path,
            "volume":          volume,
            "loop":            loop,
            "play_on_awake":   play_on_awake,
            "parent_path":     parent_path,
            "name":            name,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})




async def create_postprocessing_volume(
    overrides:       list[dict],
    profile_name:    Optional[str]        = None,
    profile_path:    Optional[str]        = None,
    position:        Optional[list[float]]= None,
    size:            Optional[list[float]]= None,
    blend_distance:  float                = 10.0,
    is_global:       bool                 = False,
    priority:        float                = 0.0,
    parent_path:     Optional[str]        = None,
    name:            Optional[str]        = None,
) -> str:
    """
    Create a URP Volume GameObject with a VolumeProfile containing the specified
    post-processing overrides.

    Requires the Unity AI Navigation package — returns PACKAGE_NOT_INSTALLED if
    com.unity.render-pipelines.universal is absent.

    Effect names
    ------------
    Use the exact C# class name from the UnityEngine.Rendering.Universal namespace.
    Names are matched case-insensitively. Common ones:

      Bloom              — intensity, threshold, scatter, tint ([r,g,b,a])
      ColorAdjustments   — postExposure, contrast, colorFilter, hueShift, saturation
      DepthOfField       — mode ("Off"|"Gaussian"|"Bokeh"), focusDistance, aperture,
                           focalLength, gaussianStart, gaussianEnd
      FilmGrain          — type ("Thin1"|"Medium1"|...|"Custom"), intensity, response
      LensDistortion     — intensity, xMultiplier, yMultiplier, center ([x,y]), scale
      MotionBlur         — mode ("CameraVelocity"|"ObjectVelocity"), quality, intensity
      ShadowsMidtonesHighlights — shadows/midtones/highlights ([r,g,b,a])
      Tonemapping        — mode ("None"|"Neutral"|"ACES"|"Custom")
      Vignette           — color ([r,g,b,a]), center ([x,y]), intensity, smoothness
      WhiteBalance       — temperature, tint
      ChromaticAberration — intensity

    Property types
    --------------
    float/int/bool   — pass as JSON number / boolean
    enum             — pass as string (e.g. "ACES") or int index
    Color            — pass as [r, g, b] or [r, g, b, a] array (0.0–1.0 range)
    Vector2          — pass as [x, y] array
    Vector4          — pass as [x, y, z, w] array

    Parameters
    ----------
    overrides      : list of {"effect": "EffectName", "properties": {"field": value}}
    profile_name   : name for new profile asset saved to Assets/Volumes/ (default "PostProcessVolume")
    profile_path   : AssetDatabase path to an existing VolumeProfile to reuse and modify
    position       : [x, y, z] world-space centre (required for non-global volumes)
    size           : [w, h, d] box extents for the volume bounds; omit for a point volume
    blend_distance : distance over which the volume fades in, world units (default 10.0)
    is_global      : True = affects entire scene regardless of camera position (default False)
    priority       : override priority — higher wins when volumes overlap (default 0)
    parent_path    : optional hierarchy parent path
    name           : optional GameObject name

    Responses
    ---------
    overrides_applied : [str] — effect names that were successfully added and configured
    overrides_failed  : [str] — "EffectName: reason" for any effect that failed
    warnings          : [str] — per-property warnings (bad field name, type mismatch)

    Example — dungeon entry atmosphere
    ------------------------------------
    create_postprocessing_volume(
        profile_name="DungeonEntry",
        position=[0, 5, 0],
        size=[20, 10, 20],
        blend_distance=6.0,
        priority=1,
        overrides=[
            {"effect": "ColorAdjustments",
             "properties": {"saturation": -30.0, "postExposure": -0.5}},
            {"effect": "Bloom",
             "properties": {"intensity": 0.1, "threshold": 1.2}},
            {"effect": "Vignette",
             "properties": {"intensity": 0.4, "smoothness": 0.5}},
        ]
    )

    Example — global scene baseline
    ---------------------------------
    create_postprocessing_volume(
        profile_name="SceneBase",
        is_global=True,
        priority=0,
        overrides=[
            {"effect": "Tonemapping",      "properties": {"mode": "ACES"}},
            {"effect": "ColorAdjustments", "properties": {"contrast": 8.0, "saturation": 5.0}},
            {"effect": "FilmGrain",        "properties": {"intensity": 0.12, "type": "Thin1"}},
        ]
    )
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/world/create_postprocessing_volume", {
            "overrides":      overrides,
            "profile_name":   profile_name,
            "profile_path":   profile_path,
            "position":       position,
            "size":           size,
            "blend_distance": blend_distance,
            "is_global":      is_global,
            "priority":       priority,
            "parent_path":    parent_path,
            "name":           name,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
