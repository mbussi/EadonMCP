"""
Tests for the Python-to-Unity key fixes.

Unity's request types read snake_case JSON names. These tests pin the keys
each fixed tool sends, and check that the older camelCase spelling of nested
items (propertyPath) is still accepted. The PCG tools now live in the PCG
extension and accept both spellings on the Unity side.
"""

import base64
import json
from pathlib import Path

import httpx
import pytest
import respx

from mcp.server.fastmcp import Image

from unity_mcp.tools.components import add_component_to_object
from unity_mcp.tools.debug import capture_scene_view
from unity_mcp.tools.dressing import create_trigger_volume
from unity_mcp.tools.objects import set_object_properties
from unity_mcp.tools.scene import get_zone_context
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"


@pytest.fixture(autouse=True)
def client(tmp_path) -> UnityClient:
    instance = UnityClient(base_url=BASE_URL, token_path=tmp_path / "no-token")
    UnityClient._instance = instance
    return instance


def capture(store: dict, data: dict | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        store["url"] = str(request.url)
        store["body"] = json.loads(request.content) if request.content else None
        return httpx.Response(200, json={"success": True, "data": data or {}})
    return handler


# ---------------------------------------------------------------------------
# Components and properties
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_add_component_sends_snake_case_keys():
    seen = {}
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/components/add").mock(side_effect=capture(seen))
        await add_component_to_object("/Barrel", "Rigidbody",
                                      [{"property_path": "m_Mass", "value": 5.0}], True)

    assert seen["body"] == {
        "object_path": "/Barrel",
        "component_type_name": "Rigidbody",
        "properties": [{"property_path": "m_Mass", "value": 5.0}],
        "update_if_exists": True,
    }


@pytest.mark.asyncio
async def test_add_component_accepts_old_property_path_spelling():
    seen = {}
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/components/add").mock(side_effect=capture(seen))
        await add_component_to_object("/Barrel", "Rigidbody", [{"propertyPath": "m_Mass", "value": 5.0}])

    assert seen["body"]["properties"] == [{"property_path": "m_Mass", "value": 5.0}]


@pytest.mark.asyncio
async def test_set_object_properties_accepts_old_property_path_spelling():
    seen = {}
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/objects/set_properties").mock(side_effect=capture(seen))
        await set_object_properties("/Lamp", "Light", [{"propertyPath": "m_Intensity", "value": 1.0}])

    assert seen["body"]["properties"] == [{"property_path": "m_Intensity", "value": 1.0}]


@pytest.mark.asyncio
async def test_trigger_volume_script_properties_accept_old_spelling():
    seen = {}
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/create_trigger_volume").mock(side_effect=capture(seen))
        await create_trigger_volume(name="T", position=[0, 0, 0], script_component="DialogueTrigger",
                                    script_properties=[{"propertyPath": "dialogueId", "value": "x"}])

    assert seen["body"]["script_properties"] == [{"property_path": "dialogueId", "value": "x"}]


# ---------------------------------------------------------------------------
# Scene view capture and zone context
# ---------------------------------------------------------------------------

def _image_data(warnings=None) -> dict:
    data = {"image_base64": base64.b64encode(b"jpeg-bytes").decode(), "format": "jpeg"}
    if warnings is not None:
        data["warnings"] = warnings
    return data


@pytest.mark.asyncio
async def test_capture_returns_image_alone_without_warnings():
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/debug/capture_scene_view").mock(side_effect=capture({}, _image_data([])))
        result = await capture_scene_view()

    assert isinstance(result, Image)


@pytest.mark.asyncio
async def test_capture_returns_warnings_with_the_image():
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/debug/capture_scene_view").mock(
            side_effect=capture({}, _image_data(["focus_path '/Nope' not found"])))
        result = await capture_scene_view(focus_path="/Nope")

    assert isinstance(result[0], Image)
    assert "focus_path '/Nope' not found" in result[1]


@pytest.mark.asyncio
async def test_zone_context_encodes_the_zone_name():
    seen = {}
    with respx.mock:
        respx.get(url__startswith=f"{BASE_URL}/scene/context").mock(side_effect=capture(seen))
        await get_zone_context("Forest Ruins & Camp")

    assert seen["url"].endswith("/scene/context?zone_name=Forest%20Ruins%20%26%20Camp")


# ---------------------------------------------------------------------------
# Registration
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_dress_area_is_registered():
    from unity_mcp.server import mcp

    names = {tool.name for tool in await mcp.list_tools()}
    assert {"dress_area", "create_trigger_volume", "apply_material"} <= names
