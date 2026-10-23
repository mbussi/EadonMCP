"""
Tests for placement tools (Phase 3).

Focuses on three concerns:
  1. Correct HTTP endpoint and payload for each tool
  2. dry_run flag is always forwarded
  3. Unity errors and connection errors surface gracefully
"""

import pytest
import json
import httpx
import respx

from unity_mcp.tools.placement import place_prefab, set_transform, delete_object, create_empty
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"
PREFAB   = "Assets/Prefabs/City/Townhouse_A.prefab"
OBJ_PATH = "Environment/Buildings/Townhouse_A"


@pytest.fixture
def client() -> UnityClient:
    instance = UnityClient(base_url=BASE_URL)
    UnityClient._instance = instance
    return instance


def _ok(data: dict):
    return httpx.Response(200, json={"success": True, "data": data})


def _err(code: str, message: str):
    return httpx.Response(400, json={"success": False, "error": {"code": code, "message": message}})


# ---------------------------------------------------------------------------
# place_prefab
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_place_prefab_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=_ok({"display_name": "Townhouse_A", "instance_path": OBJ_PATH,
                              "position": [10.0, 0.0, 5.0], "dry_run": False})
        )
        await place_prefab(PREFAB, [10.0, 0.0, 5.0])

    assert route.called


@pytest.mark.asyncio
async def test_place_prefab_sends_all_parameters(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=_ok({"display_name": "Townhouse_A", "instance_path": OBJ_PATH,
                              "position": [1.0, 2.0, 3.0], "dry_run": False})
        )
        await place_prefab(
            prefab_path=PREFAB,
            position=[1.0, 2.0, 3.0],
            rotation=[0.0, 90.0, 0.0],
            scale=[2.0, 2.0, 2.0],
            parent_path="Environment",
        )

    sent = json.loads(route.calls[0].request.content)
    assert sent["prefab_path"] == PREFAB
    assert sent["position"] == [1.0, 2.0, 3.0]
    assert sent["rotation"] == [0.0, 90.0, 0.0]
    assert sent["scale"] == [2.0, 2.0, 2.0]
    assert sent["parent_path"] == "Environment"


@pytest.mark.asyncio
async def test_place_prefab_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=_ok({"display_name": "Townhouse_A", "instance_path": OBJ_PATH,
                              "position": [0.0, 0.0, 0.0], "dry_run": True})
        )
        result = await place_prefab(PREFAB, [0.0, 0.0, 0.0], dry_run=True)

    sent = json.loads(route.calls[0].request.content)
    assert sent["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_place_prefab_returns_error_string_on_prefab_not_found(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=_err("PREFAB_NOT_FOUND", "No prefab at path")
        )
        result = await place_prefab("Assets/Missing.prefab", [0.0, 0.0, 0.0])

    assert "PREFAB_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# set_transform
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_set_transform_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/set_transform").mock(
            return_value=_ok({"display_name": "Townhouse_A", "object_path": OBJ_PATH,
                              "position": [5.0, 0.0, 0.0], "dry_run": False})
        )
        await set_transform(OBJ_PATH, position=[5.0, 0.0, 0.0])

    assert route.called


@pytest.mark.asyncio
async def test_set_transform_sends_only_provided_fields(client):
    """Omitted transform axes should be sent as null so Unity leaves them unchanged."""
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/set_transform").mock(
            return_value=_ok({"display_name": "Townhouse_A", "object_path": OBJ_PATH,
                              "position": [5.0, 0.0, 0.0], "dry_run": False})
        )
        await set_transform(OBJ_PATH, position=[5.0, 0.0, 0.0])

    sent = json.loads(route.calls[0].request.content)
    assert sent["position"] == [5.0, 0.0, 0.0]
    assert sent["rotation"] is None
    assert sent["scale"]    is None


@pytest.mark.asyncio
async def test_set_transform_returns_error_on_object_not_found(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/set_transform").mock(
            return_value=_err("OBJECT_NOT_FOUND", "No object at path")
        )
        result = await set_transform("Environment/Missing", position=[0.0, 0.0, 0.0])

    assert "OBJECT_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# delete_object
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_delete_object_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/delete_object").mock(
            return_value=_ok({"display_name": "Townhouse_A", "object_path": OBJ_PATH, "dry_run": True})
        )
        result = await delete_object(OBJ_PATH, dry_run=True)

    sent = json.loads(route.calls[0].request.content)
    assert sent["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_delete_object_returns_error_on_object_not_found(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/delete_object").mock(
            return_value=_err("OBJECT_NOT_FOUND", "No object at path")
        )
        result = await delete_object("Environment/Missing")

    assert "OBJECT_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# create_empty
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_create_empty_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/create_empty").mock(
            return_value=_ok({"display_name": "Container", "instance_path": "Container",
                              "position": [0.0, 0.0, 0.0], "dry_run": False})
        )
        await create_empty("Container")

    assert route.called


@pytest.mark.asyncio
async def test_create_empty_sends_parent_path(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/create_empty").mock(
            return_value=_ok({"display_name": "Buildings", "instance_path": "Environment/Buildings",
                              "position": [0.0, 0.0, 0.0], "dry_run": False})
        )
        await create_empty("Buildings", parent_path="Environment")

    sent = json.loads(route.calls[0].request.content)
    assert sent["parent_path"] == "Environment"


@pytest.mark.asyncio
async def test_create_empty_dry_run_makes_no_changes(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/create_empty").mock(
            return_value=_ok({"display_name": "Container", "instance_path": "Container",
                              "position": [0.0, 0.0, 0.0], "dry_run": True})
        )
        result = await create_empty("Container", dry_run=True)

    sent = json.loads(route.calls[0].request.content)
    assert sent["dry_run"] is True
    assert "[DRY RUN]" in result
