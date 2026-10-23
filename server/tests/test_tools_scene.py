"""
Tests for scene inspection tools (Phase 2).

Tool functions are imported and called directly — no MCP protocol involved.
HTTP is mocked with respx so no Unity Editor is required.
"""

import json
import pytest
import httpx
import respx

from unity_mcp.tools.scene import get_scene_hierarchy, get_scene_context, get_prefab_catalog
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"


@pytest.fixture
def client() -> UnityClient:
    instance = UnityClient(base_url=BASE_URL)
    UnityClient._instance = instance
    return instance


# ---------------------------------------------------------------------------
# get_scene_hierarchy
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_scene_hierarchy_returns_json_string(client):
    payload = {
        "scene_name": "Level_01",
        "object_count": 2,
        "root_objects": [
            {"name": "Environment", "path": "Environment", "children": []}
        ]
    }
    with respx.mock:
        respx.get(f"{BASE_URL}/scene/hierarchy").mock(
            return_value=httpx.Response(200, json={"success": True, "data": payload})
        )
        result = await get_scene_hierarchy()

    parsed = json.loads(result)
    assert parsed["scene_name"] == "Level_01"
    assert parsed["object_count"] == 2


@pytest.mark.asyncio
async def test_get_scene_hierarchy_passes_max_depth(client):
    with respx.mock:
        route = respx.get(f"{BASE_URL}/scene/hierarchy").mock(
            return_value=httpx.Response(200, json={"success": True, "data": {}})
        )
        await get_scene_hierarchy(max_depth=2)

    assert "max_depth=2" in str(route.calls[0].request.url)


@pytest.mark.asyncio
async def test_get_scene_hierarchy_no_max_depth_param_when_unlimited(client):
    with respx.mock:
        route = respx.get(f"{BASE_URL}/scene/hierarchy").mock(
            return_value=httpx.Response(200, json={"success": True, "data": {}})
        )
        await get_scene_hierarchy(max_depth=-1)

    assert "max_depth" not in str(route.calls[0].request.url)


@pytest.mark.asyncio
async def test_get_scene_hierarchy_returns_error_string_on_connection_failure(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/scene/hierarchy").mock(
            side_effect=httpx.ConnectError("refused")
        )
        result = await get_scene_hierarchy()

    assert "Cannot connect" in result


# ---------------------------------------------------------------------------
# get_scene_context
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_scene_context_returns_json_string(client):
    payload = {
        "level_name": "Old Town",
        "setting": "city",
        "architectural_style": "19th century European",
        "active_tags": ["urban", "stone"],
        "excluded_tags": ["rural", "modern"]
    }
    with respx.mock:
        respx.get(f"{BASE_URL}/scene/context").mock(
            return_value=httpx.Response(200, json={"success": True, "data": payload})
        )
        result = await get_scene_context()

    parsed = json.loads(result)
    assert parsed["setting"] == "city"
    assert "urban" in parsed["active_tags"]


@pytest.mark.asyncio
async def test_get_scene_context_returns_error_string_on_unity_error(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/scene/context").mock(
            return_value=httpx.Response(
                400,
                json={"success": False, "error": {"code": "NO_CONTEXT", "message": "No SceneContext configured"}}
            )
        )
        result = await get_scene_context()

    assert "Unity returned an error" in result
    assert "NO_CONTEXT" in result


# ---------------------------------------------------------------------------
# get_prefab_catalog
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_prefab_catalog_returns_json_string(client):
    payload = {
        "prefabs": [
            {
                "asset_path": "Assets/Prefabs/City/Townhouse_A.prefab",
                "display_name": "Townhouse A",
                "has_metadata": True,
                "category": "building",
                "tags": ["urban", "residential"]
            }
        ]
    }
    with respx.mock:
        respx.get(f"{BASE_URL}/assets/prefabs").mock(
            return_value=httpx.Response(200, json={"success": True, "data": payload})
        )
        result = await get_prefab_catalog()

    parsed = json.loads(result)
    assert len(parsed["prefabs"]) == 1
    assert parsed["prefabs"][0]["category"] == "building"


@pytest.mark.asyncio
async def test_get_prefab_catalog_encodes_search_path(client):
    with respx.mock:
        route = respx.get(f"{BASE_URL}/assets/prefabs").mock(
            return_value=httpx.Response(200, json={"success": True, "data": {"prefabs": []}})
        )
        await get_prefab_catalog(search_path="Assets/Prefabs/City")

    url = str(route.calls[0].request.url)
    assert "Assets" in url
    assert "Prefabs" in url


@pytest.mark.asyncio
async def test_get_prefab_catalog_default_search_path_is_assets(client):
    with respx.mock:
        route = respx.get(f"{BASE_URL}/assets/prefabs").mock(
            return_value=httpx.Response(200, json={"success": True, "data": {"prefabs": []}})
        )
        await get_prefab_catalog()

    assert "search_path=Assets" in str(route.calls[0].request.url)
