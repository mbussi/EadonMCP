"""
Tests for procedural placement tools (Phase 6).
"""

import pytest
import json
import httpx
import respx

from unity_mcp.tools.procedural import (
    place_prefab_batch,
    scatter_prefabs,
    place_along_path,
    fill_grid,
)
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"
PREFAB   = "Assets/Prefabs/City/Townhouse_A.prefab"


@pytest.fixture
def client() -> UnityClient:
    instance = UnityClient(base_url=BASE_URL)
    UnityClient._instance = instance
    return instance


def _ok(data):
    return httpx.Response(200, json={"success": True, "data": data})

def _err(code, msg):
    return httpx.Response(400, json={"success": False, "error": {"code": code, "message": msg}})


# ---------------------------------------------------------------------------
# place_prefab_batch
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_place_prefab_batch_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(
            return_value=_ok({"placed_count": 2, "failed_count": 0, "instance_paths": ["A", "B"]})
        )
        await place_prefab_batch([
            {"prefab_path": PREFAB, "position": [0, 0, 0]},
            {"prefab_path": PREFAB, "position": [5, 0, 0]},
        ])
    assert route.called


@pytest.mark.asyncio
async def test_place_prefab_batch_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(
            return_value=_ok({"placed_count": 1, "failed_count": 0, "instance_paths": ["A"]})
        )
        result = await place_prefab_batch([{"prefab_path": PREFAB, "position": [0, 0, 0]}], dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_place_prefab_batch_reports_failures(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(
            return_value=_ok({"placed_count": 1, "failed_count": 1, "instance_paths": ["A"]})
        )
        result = await place_prefab_batch([
            {"prefab_path": PREFAB,             "position": [0, 0, 0]},
            {"prefab_path": "Assets/Ghost.prefab","position": [5, 0, 0]},
        ])
    assert "1 failed" in result


# ---------------------------------------------------------------------------
# scatter_prefabs
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_scatter_prefabs_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/scatter").mock(
            return_value=_ok({"placed_count": 10})
        )
        await scatter_prefabs(PREFAB, [0.0, 0.0, 0.0], radius=20.0, count=10)
    assert route.called


@pytest.mark.asyncio
async def test_scatter_prefabs_sends_all_parameters(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/scatter").mock(
            return_value=_ok({"placed_count": 5})
        )
        await scatter_prefabs(
            PREFAB, [1.0, 0.0, 2.0], radius=15.0, count=5,
            min_spacing=3.0, align_to_surface=True, seed=42
        )
    sent = json.loads(route.calls[0].request.content)
    assert sent["radius"]           == 15.0
    assert sent["count"]            == 5
    assert sent["min_spacing"]      == 3.0
    assert sent["align_to_surface"] is True
    assert sent["seed"]             == 42


@pytest.mark.asyncio
async def test_scatter_prefabs_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/scatter").mock(
            return_value=_ok({"placed_count": 5})
        )
        result = await scatter_prefabs(PREFAB, [0, 0, 0], 10, 5, dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_scatter_prefabs_returns_error_on_invalid_prefab(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/procedural/scatter").mock(
            return_value=_err("PREFAB_NOT_FOUND", "No prefab at path")
        )
        result = await scatter_prefabs("Assets/Ghost.prefab", [0, 0, 0], 10, 5)
    assert "PREFAB_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# place_along_path
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_place_along_path_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/along_path").mock(
            return_value=_ok({"placed_count": 6, "path_length": 10.0})
        )
        await place_along_path(PREFAB, [[0,0,0],[10,0,0]], spacing=2.0)
    assert route.called


@pytest.mark.asyncio
async def test_place_along_path_sends_waypoints_and_spacing(client):
    waypoints = [[0,0,0],[5,0,0],[10,0,5]]
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/along_path").mock(
            return_value=_ok({"placed_count": 8, "path_length": 16.2})
        )
        await place_along_path(PREFAB, waypoints, spacing=2.0, align_to_path=False)
    sent = json.loads(route.calls[0].request.content)
    assert sent["waypoints"]     == waypoints
    assert sent["spacing"]       == 2.0
    assert sent["align_to_path"] is False


@pytest.mark.asyncio
async def test_place_along_path_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/along_path").mock(
            return_value=_ok({"placed_count": 5, "path_length": 8.0})
        )
        result = await place_along_path(PREFAB, [[0,0,0],[8,0,0]], spacing=2.0, dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_place_along_path_returns_error_on_invalid_spacing(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/procedural/along_path").mock(
            return_value=_err("INVALID_PARAMS", "spacing must be positive")
        )
        result = await place_along_path(PREFAB, [[0,0,0],[10,0,0]], spacing=0.0)
    assert "INVALID_PARAMS" in result


# ---------------------------------------------------------------------------
# fill_grid
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_fill_grid_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/fill_grid").mock(
            return_value=_ok({"placed_count": 12})
        )
        await fill_grid(PREFAB, [0,0,0], [2,2], columns=4, rows=3)
    assert route.called


@pytest.mark.asyncio
async def test_fill_grid_sends_all_parameters(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/fill_grid").mock(
            return_value=_ok({"placed_count": 6})
        )
        await fill_grid(
            PREFAB, [1,0,2], [3,3],
            columns=3, rows=2,
            offset_alternate_rows=True,
        )
    sent = json.loads(route.calls[0].request.content)
    assert sent["origin"]                == [1, 0, 2]
    assert sent["cell_size"]             == [3, 3]
    assert sent["columns"]               == 3
    assert sent["rows"]                  == 2
    assert sent["offset_alternate_rows"] is True


@pytest.mark.asyncio
async def test_fill_grid_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/procedural/fill_grid").mock(
            return_value=_ok({"placed_count": 6})
        )
        result = await fill_grid(PREFAB, [0,0,0], [2,2], columns=3, rows=2, dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_fill_grid_returns_error_on_invalid_dimensions(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/procedural/fill_grid").mock(
            return_value=_err("INVALID_PARAMS", "columns must be positive")
        )
        result = await fill_grid(PREFAB, [0,0,0], [2,2], columns=0, rows=3)
    assert "INVALID_PARAMS" in result
