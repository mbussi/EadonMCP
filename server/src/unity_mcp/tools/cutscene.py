"""
Cutscene tools — template-based camera sequence creation.

get_cutscene_templates    — list available templates with their parameter schemas
create_cutscene_sequence  — instantiate a template and create all Unity assets

Cutscenes are created from named templates that cover the common camera patterns
in open-world games. The agent fills in template parameters; the handler creates
the Cinemachine virtual cameras, Timeline .playable asset, and signal emitters.
The designer then connects the PlayableDirector to a gameplay trigger.

Requires: com.unity.cinemachine and com.unity.timeline Unity packages.
"""

import json

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def get_cutscene_templates() -> str:
    """
    Lists all available cutscene templates with their parameter schemas.

    Call this before create_cutscene_sequence to discover what templates are
    available and what parameters each one requires.

    Returns:
      cinemachine_version  — installed Cinemachine package version, or null if absent
      timeline_version     — installed Timeline package version
      templates[]          — list of templates, each with:
        name               — pass this as the 'template' argument to create_cutscene_sequence
        description        — what the template creates and when to use it
        parameters[]       — each parameter with name, type, required, default, description

    The cutscene tools need Cinemachine 3.x (Unity 6 ships with it; 2.x is not
    supported). Without it, templates is empty and message explains why.

    Phase 1 available templates:
      DialogueCamera — over-shoulder / two-shot for NPC dialogue
    """
    try:
        data = await UnityClient.instance().get("/assets/cutscene/templates")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def create_cutscene_sequence(
    template: str,
    sequence_name: str,
    params: dict,
    timeline_path: str | None = None,
) -> str:
    """
    Creates a cutscene sequence from a named template.

    Instantiates the template to produce:
      - A container GameObject under /Cutscenes/{sequence_name}/ in the scene
      - One or more Cinemachine virtual camera GameObjects
      - A Timeline .playable asset with Cinemachine, Activation, and Signal tracks
      - Signal assets in Assets/Cutscenes/Signals/ (reused across sequences)

    ── TEMPLATE: DialogueCamera ─────────────────────────────────────────────

    Creates an over-shoulder / two-shot dialogue camera. Blends from the active
    follow camera, holds during dialogue, then blends back.

    Required params:
      player_path (string)  — hierarchy path of the player root, e.g. "/Player"
      npc_path    (string)  — hierarchy path of the NPC, e.g. "/Village/Merchant_A"
      duration    (float)   — total sequence length in seconds, e.g. 8.0

    Optional params:
      camera_side (string)  — "left" | "right" | "front" | "auto" (default "auto")
                              "auto" places the camera on the side facing the current
                              Scene view camera at creation time.
      blend_in    (float)   — seconds to blend in from follow cam (default 0.5)
      blend_out   (float)   — seconds to blend back to follow cam (default 0.5)
      look_offset (float[3])— local offset from NPC root to look at (default [0,1.6,0])

    Example:
      template      = "DialogueCamera"
      sequence_name = "DialogueCamera_MerchantA"
      params        = {
        "player_path": "/Player",
        "npc_path":    "/Village/Merchant_A",
        "camera_side": "right",
        "duration":    8.0,
        "blend_in":    0.5,
        "blend_out":   0.5,
        "look_offset": [0, 1.7, 0]
      }

    ── AFTER CREATION ────────────────────────────────────────────────────────

    The sequence is created but not connected to any gameplay trigger.
    The designer must:

    1. Connect to a trigger (ALWAYS required):
         GetComponent<PlayableDirector>().Play();
       Wire this to an AnimationEvent, UnityEvent, proximity trigger, etc.

    2. CinemachineBrain (if cinemachine_brain_found is false):
         Add a CinemachineBrain component to the Main Camera.

    3. Signal receivers (when using dialogue_start / dialogue_end signals):
         Add a SignalReceiver to the player or dialogue manager.
         Wire dialogue_start → open dialogue UI
         Wire dialogue_end   → close dialogue UI, return control

    ── PARAMETERS ───────────────────────────────────────────────────────────

    template:      Template name from get_cutscene_templates (e.g. "DialogueCamera")
    sequence_name: Name for the scene container, e.g. "DialogueCamera_MerchantA"
    params:        Template-specific parameter dict (see each template above)
    timeline_path: Optional asset path for the .playable file.
                   Defaults to "Assets/Cutscenes/{sequence_name}.playable"

    Returns:
      { sequence_name, container_path, timeline_path, vcam_paths[],
        signals_created[], cinemachine_brain_found, warnings[] }
    """
    try:
        data = await UnityClient.instance().post("/tools/cutscene/create_sequence", {
            "template":      template,
            "sequence_name": sequence_name,
            "timeline_path": timeline_path,
            "params":        params,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"




async def preview_cutscene_sequence(container_path: str) -> str:
    """
    Plays back an existing cutscene sequence in the Unity Editor.

    Selects the container in the Hierarchy, opens the Timeline window,
    resets playback to t=0, and calls PlayableDirector.Play().

    container_path: full hierarchy path of the sequence container,
                    e.g. "/Cutscenes/DialogueCamera_MerchantA"
                    Use get_scene_hierarchy to find the path if unsure.

    Useful for:
      - Checking that a newly created sequence looks right before committing
      - Reviewing a sequence after update_cutscene_sequence changes
      - Confirming signal timing and camera blend feel

    Returns: { container_path, duration, message }

    NOTE: Playback runs in the Editor scene view. The designer can stop it
    at any time by clicking the Stop button in the Timeline window.
    Calling preview_cutscene_sequence again resets to t=0 and replays.
    """
    try:
        data = await UnityClient.instance().post(
            "/tools/cutscene/preview_sequence",
            {"container_path": container_path},
        )
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def update_cutscene_sequence(
    container_path: str,
    template: str,
    params: dict,
) -> str:
    """
    Modifies an existing cutscene sequence in place.

    Updates only the fields you provide — everything else is unchanged.
    All changes are registered with Unity's Undo system.

    ── WHAT CAN BE UPDATED ──────────────────────────────────────────────────

    ALL templates:
      duration    (float)   — total sequence length in seconds
      blend_in    (float)   — seconds to blend in (DialogueCamera, FocusCut)
      blend_out   (float)   — seconds to blend out (DialogueCamera, FocusCut)
      ease_in     (float)   — fraction of the duration for ease-in  (AreaReveal, ArrivalFlythrough)
      ease_out    (float)   — fraction of the duration for ease-out (AreaReveal, ArrivalFlythrough)

    When duration or ease values change, the Timeline clip lengths, ActivationTrack
    clip, signal marker positions, and cart animation curve are all updated together.

    AreaReveal:
      waypoints   (float[3][])  — new camera path positions (min 2)
                                  Updates the SplineContainer knots and rebakes
                                  the cart animation curve.

    ArrivalFlythrough:
      waypoints          (float[3][])  — new camera path (min 3)
      look_at_position   (float[3])    — new fixed world-space look target

    FocusCut:
      camera_offset  (float[3])  — new offset from the target object to the camera,
                                   e.g. [0, 1.5, -2.0]

    DialogueCamera:
      look_offset  (float[3])  — new offset from the NPC to the look target, as at
                                 creation: the look target moves to the NPC's
                                 position plus the offset, and the camera height
                                 follows its Y. Sequences created before the
                                 settings were stored cannot be updated this way.

    ── EXAMPLE ──────────────────────────────────────────────────────────────

    # Extend an AreaReveal and push the path further out
    update_cutscene_sequence(
        container_path = "/Cutscenes/AreaReveal_DungeonEntrance",
        template       = "AreaReveal",
        params         = {
            "waypoints": [[0,10,0], [15,6,-15], [30,4,-28]],
            "duration":  9.0,
            "ease_out":  0.3
        }
    )

    # Tighten a FocusCut blend
    update_cutscene_sequence(
        container_path = "/Cutscenes/FocusCut_AncientKey",
        template       = "FocusCut",
        params         = {
            "blend_in":  0.2,
            "blend_out": 0.3,
            "duration":  2.0
        }
    )

    ── PARAMETERS ───────────────────────────────────────────────────────────

    container_path: full hierarchy path returned by create_cutscene_sequence
    template:       the template name used at creation (e.g. "AreaReveal")
    params:         dict containing only the fields you want to change

    Returns: { container_path, updated_fields[], warnings[] }
    """
    try:
        data = await UnityClient.instance().post(
            "/tools/cutscene/update_sequence",
            {
                "container_path": container_path,
                "template":       template,
                "params":         params,
            },
        )
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


