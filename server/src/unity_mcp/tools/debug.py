"""
Debug and visual feedback tools for the Unity MCP Bridge.

  capture_scene_view — render the active Scene View and return it as an image
                       that Claude receives directly in context

This module is intentionally separate from all other tool modules because
capture_scene_view is the only tool in the bridge that returns an Image
rather than a JSON string. Keeping it isolated makes the architectural
exception visible and prevents its unusual import (Image from
mcp.server.fastmcp) from being mixed into standard tool modules.
"""

from __future__ import annotations

import base64
from typing import Optional

from mcp.server.fastmcp import Image

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def capture_scene_view(
    width:      int           = 1280,
    height:     int           = 720,
    quality:    int           = 85,
    focus_path: Optional[str] = None,
):
    """
    Render the active Unity Scene View and return the result as an image.

    Claude receives the image directly in context and can describe what it
    sees, identify layout issues, and suggest specific adjustments — closing
    the visual feedback loop that otherwise requires manual viewport inspection
    after every operation.

    The capture renders the scene camera without editor gizmos and handles,
    giving a clean view of the actual scene geometry. This is intentional:
    the undecorated scene is what the player sees, and it is less cluttered
    for spatial review than the gizmo-heavy editor view.

    ── TYPICAL USAGE ────────────────────────────────────────────────────────

    After any placement or dressing operation:

        # Place props, then review
        execute_proposal("prop_dress_a3f2")
        capture_scene_view(focus_path="/World/Village/Market",
                           width=1920, height=1080)
        # Claude: "The north corner looks sparse — the stalls are clustered
        #          in the south. Should I scatter a few more crates at [22,0,18]?"

    ── PARAMETERS ───────────────────────────────────────────────────────────

    width      : capture width in pixels (default 1280)
    height     : capture height in pixels (default 720)
    quality    : JPEG quality 1–100 (default 85).
                 85 is a good balance of quality and response size.
                 Use 60–70 for a quick overview; 95+ for detailed inspection.
    focus_path : hierarchy path of an object to frame before capturing.
                 The Scene View pivot and size are set from the object's
                 renderer bounds. If the path is not found, the current
                 view orientation is used and a warning is returned with
                 the image.

    ── RETURN TYPE ──────────────────────────────────────────────────────────

    Returns Image on success — Claude sees the scene image directly. When
    Unity reports warnings (e.g. focus_path not found), they follow the
    image as text.
    Returns str on error — Claude sees the error message as text.

    This is the only tool in the bridge that returns Image rather than a
    JSON string. All other tools return json.dumps(result).

    ── REQUIREMENTS ─────────────────────────────────────────────────────────

    A Scene View window must be open and active in the Unity Editor.
    The capture fails with NO_SCENE_VIEW if no Scene View is available
    (e.g. if only the Game View is open, or Unity is minimised).
    """
    client = UnityClient.instance()
    try:
        data = await client.post("/tools/debug/capture_scene_view", {
            "width":      width,
            "height":     height,
            "quality":    quality,
            "focus_path": focus_path,
        })

        # Decode base64 → raw bytes → Image for Claude
        image_bytes = base64.b64decode(data["image_base64"])
        image = Image(data=image_bytes, format="jpeg")

        warnings = [w for w in (data.get("warnings") or []) if w]
        if warnings:
            return [image, "Warnings: " + "; ".join(warnings)]
        return image

    except (UnityConnectionError, UnityError) as e:
        # Return a plain string on error so Claude gets a readable message
        # rather than an unhandled exception
        return f"Cannot capture scene view: {e}"
