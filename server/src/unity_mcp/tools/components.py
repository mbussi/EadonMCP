"""
Component tools — catalog discovery and adding components to scene objects.

get_component_catalog    — discover all available component types with their
                           exact serialized property paths and default values
add_component_to_object  — add a component to a scene object and set properties
"""

import json

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


def normalise_property_values(properties: list[dict] | None) -> list[dict]:
    """
    Unity reads each property as { "property_path", "value" }. Older docs said
    "propertyPath", so accept that spelling too rather than silently failing.
    """
    result = []
    for item in properties or []:
        if isinstance(item, dict) and "property_path" not in item and "propertyPath" in item:
            item = {("property_path" if k == "propertyPath" else k): v for k, v in item.items()}
        result.append(item)
    return result


async def get_component_catalog() -> str:
    """
    Returns every component type available in this Unity project.

    Includes built-in Unity components (Rigidbody, BoxCollider, Light, AudioSource,
    Camera, NavMeshAgent, etc.) and user MonoBehaviour scripts from project assemblies.

    For each component:
      type_name         — use as component_type_name in add_component_to_object
      display_name      — human-readable label
      category         — Physics | Rendering | Audio | Animation | Navigation |
                         Camera | Effects | UserScript | Physics2D
      source           — "BuiltIn" or "UserScript"
      script_path       — asset path for UserScript components, null for BuiltIn
      disallow_multiple — true if only one instance is allowed per GameObject
      properties[]     — serialized properties with:
        property_path   — EXACT key to use in add_component_to_object properties
        display_name    — Inspector label (e.g. "Mass", "Use Gravity")
        type           — Float | Integer | Boolean | String | Enum:Name[Val0|Val1]
                         | Vector2 | Vector3 | Color | ObjectReference:Type | etc.
        default_value   — C# default as a string (informational)
        enum_names[]    — valid enum value names (only present for Enum properties)

    CRITICAL — property paths:
      For built-in components the serialized path is NOT the C# property name.
      The catalog gives you the exact internal path:
        Rigidbody.mass     → property_path "m_Mass"
        Light.intensity    → property_path "m_Intensity"
        Camera.fieldOfView → property_path "field of view"
      Always use property_path from this catalog — never guess.
      For user scripts, property_path matches the C# field name.

    Call once per session and cache; only changes when new scripts are added.
    """
    try:
        data = await UnityClient.instance().get("/assets/components/catalog")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def add_component_to_object(
    object_path: str,
    component_type_name: str,
    properties: list[dict] | None = None,
    update_if_exists: bool = False,
) -> str:
    """
    Adds a component to a scene GameObject and sets its properties atomically.
    The operation is registered with Unity's Undo system.

    WORKFLOW:
      1. Call get_component_catalog() to find the type and valid property paths.
      2. Call get_scene_hierarchy() if you need to confirm the object path.
      3. Call add_component_to_object() with the type and desired properties.

    PROPERTY FORMAT:
      properties is a list of { "property_path": "...", "value": ... }.
      Use property_path exactly as returned by get_component_catalog.

      Value formats by type:
        Float, Integer  → number:         9.81
        Boolean         → boolean:        true
        String          → string:         "idle"
        Enum            → string:         "Continuous"  (use enum_names from catalog)
        Vector2         → array:          [1.0, 0.0]
        Vector3         → array:          [0.0, -9.81, 0.0]
        Color           → array [r,g,b,a]: [1.0, 0.5, 0.0, 1.0]  (0–1 range)
        Bounds          → array [cx,cy,cz,sx,sy,sz]: [0, 0, 0, 2, 2, 2]
        Rect            → array [x,y,w,h]: [0, 0, 100, 50]
        LayerMask       → integer bitmask: 1
        ObjectReference → object: { "asset_guid": "abc123..." }

    EXAMPLES:
      Add a Rigidbody with custom mass:
        object_path = "/Environment/Barrel"
        component_type_name = "Rigidbody"
        properties = [
          { "property_path": "m_Mass",       "value": 5.0   },
          { "property_path": "m_UseGravity",  "value": false }
        ]

      Add a warm point light:
        component_type_name = "Light"
        properties = [
          { "property_path": "m_Type",      "value": "Point"              },
          { "property_path": "m_Color",     "value": [1.0, 0.85, 0.6, 1.0] },
          { "property_path": "m_Intensity", "value": 2.0                  },
          { "property_path": "m_Range",     "value": 10.0                 }
        ]

    object_path:          Hierarchy path, e.g. "/Player", "/Environment/Barrel"
    component_type_name:  type_name from get_component_catalog
    properties:           List of { property_path, value } (omit to keep defaults)
    update_if_exists:     If true, update an existing component instead of erroring
    """
    try:
        data = await UnityClient.instance().post("/tools/components/add", {
            "object_path":        object_path,
            "component_type_name": component_type_name,
            "properties":        normalise_property_values(properties),
            "update_if_exists":    update_if_exists,
        })
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
