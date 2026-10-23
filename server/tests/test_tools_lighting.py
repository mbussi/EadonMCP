"""
Tests for lighting tools (Phase 4).
"""

import pytest
import json
import httpx
import respx

from unity_mcp.tools.lighting import create_light, set_ambient_light, set_skybox, set_fog
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"


@pytest.fixture
def client() -> UnityClient:
    instance = UnityClient(base_url=BASE_URL)
    UnityClient._instance = instance
    return instance


def _ok(data: dict):
    return httpx.Response(200, json={"success": True, "data": data})


def _err(code: str, msg: str):
    return httpx.Response(400, json={"success": False, "error": {"code": code, "message": msg}})


# ---------------------------------------------------------------------------
# create_light
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_create_light_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/create_light").mock(
            return_value=_ok({"display_name": "Point Light", "instance_path": "Lighting/Point Light",
                              "light_type": "Point", "position": [0.0, 3.0, 0.0], "dry_run": False})
        )
        await create_light("Point", [0.0, 3.0, 0.0])
    assert route.called


@pytest.mark.asyncio
async def test_create_light_sends_all_parameters(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/create_light").mock(
            return_value=_ok({"display_name": "Lamp", "instance_path": "Lamp",
                              "light_type": "Spot", "position": [1.0, 2.0, 3.0], "dry_run": False})
        )
        await create_light(
            light_type="Spot",
            position=[1.0, 2.0, 3.0],
            rotation=[0.0, 0.0, 0.0],
            color=[1.0, 0.8, 0.6],
            intensity=2.0,
            range=15.0,
            spot_angle=45.0,
            shadows="Hard",
            name="Lamp",
        )
    sent = json.loads(route.calls[0].request.content)
    assert sent["light_type"]  == "Spot"
    assert sent["color"]       == [1.0, 0.8, 0.6]
    assert sent["intensity"]   == 2.0
    assert sent["range"]       == 15.0
    assert sent["spot_angle"]  == 45.0
    assert sent["shadows"]     == "Hard"
    assert sent["name"]        == "Lamp"


@pytest.mark.asyncio
async def test_create_light_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/create_light").mock(
            return_value=_ok({"display_name": "Point Light", "instance_path": "Point Light",
                              "light_type": "Point", "position": [0.0, 0.0, 0.0], "dry_run": True})
        )
        result = await create_light("Point", [0.0, 0.0, 0.0], dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_create_light_returns_error_on_invalid_type(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/lighting/create_light").mock(
            return_value=_err("INVALID_LIGHT_TYPE", "Unknown type 'Laser'")
        )
        result = await create_light("Laser", [0.0, 0.0, 0.0])
    assert "INVALID_LIGHT_TYPE" in result


@pytest.mark.asyncio
async def test_create_light_returns_error_on_invalid_parent(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/lighting/create_light").mock(
            return_value=_err("PARENT_NOT_FOUND", "No object at path 'Ghost'")
        )
        result = await create_light("Point", [0.0, 0.0, 0.0], parent_path="Ghost")
    assert "PARENT_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# set_ambient_light
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_set_ambient_light_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_ambient").mock(
            return_value=_ok({"mode": "Flat", "dry_run": False})
        )
        await set_ambient_light("Flat", color=[0.2, 0.2, 0.3])
    assert route.called


@pytest.mark.asyncio
async def test_set_ambient_light_sends_trilight_colors(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_ambient").mock(
            return_value=_ok({"mode": "Trilight", "dry_run": False})
        )
        await set_ambient_light(
            "Trilight",
            sky_color=[0.5, 0.7, 1.0],
            equator_color=[0.4, 0.4, 0.4],
            ground_color=[0.2, 0.15, 0.1],
        )
    sent = json.loads(route.calls[0].request.content)
    assert sent["sky_color"]     == [0.5, 0.7, 1.0]
    assert sent["equator_color"] == [0.4, 0.4, 0.4]
    assert sent["ground_color"]  == [0.2, 0.15, 0.1]


@pytest.mark.asyncio
async def test_set_ambient_light_returns_error_on_invalid_mode(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/lighting/set_ambient").mock(
            return_value=_err("INVALID_MODE", "Unknown mode 'Rainbow'")
        )
        result = await set_ambient_light("Rainbow")
    assert "INVALID_MODE" in result


# ---------------------------------------------------------------------------
# set_skybox
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_set_skybox_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_skybox").mock(
            return_value=_ok({"material_path": "Assets/Sky.mat", "dry_run": False})
        )
        await set_skybox("Assets/Sky.mat")
    assert route.called


@pytest.mark.asyncio
async def test_set_skybox_forwards_dry_run(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_skybox").mock(
            return_value=_ok({"material_path": "Assets/Sky.mat", "dry_run": True})
        )
        result = await set_skybox("Assets/Sky.mat", dry_run=True)
    assert json.loads(route.calls[0].request.content)["dry_run"] is True
    assert "[DRY RUN]" in result


@pytest.mark.asyncio
async def test_set_skybox_returns_error_on_invalid_material(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/lighting/set_skybox").mock(
            return_value=_err("MATERIAL_NOT_FOUND", "No material at path")
        )
        result = await set_skybox("Assets/Missing.mat")
    assert "MATERIAL_NOT_FOUND" in result


@pytest.mark.asyncio
async def test_set_skybox_accepts_null_to_clear(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_skybox").mock(
            return_value=_ok({"material_path": None, "dry_run": False})
        )
        result = await set_skybox(None)
    sent = json.loads(route.calls[0].request.content)
    assert sent["material_path"] is None
    assert "cleared" in result


# ---------------------------------------------------------------------------
# set_fog
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_set_fog_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_fog").mock(
            return_value=_ok({"enabled": True, "dry_run": False})
        )
        await set_fog(True, mode="Exponential", density=0.02)
    assert route.called


@pytest.mark.asyncio
async def test_set_fog_sends_all_parameters(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_fog").mock(
            return_value=_ok({"enabled": True, "dry_run": False})
        )
        await set_fog(True, color=[0.8, 0.8, 0.9], mode="Linear", start=10.0, end=200.0)
    sent = json.loads(route.calls[0].request.content)
    assert sent["enabled"] is True
    assert sent["color"]   == [0.8, 0.8, 0.9]
    assert sent["mode"]    == "Linear"
    assert sent["start"]   == 10.0
    assert sent["end"]     == 200.0


@pytest.mark.asyncio
async def test_set_fog_returns_error_on_invalid_mode(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/lighting/set_fog").mock(
            return_value=_err("INVALID_FOG_MODE", "Unknown mode 'Volumetric'")
        )
        result = await set_fog(True, mode="Volumetric")
    assert "INVALID_FOG_MODE" in result


@pytest.mark.asyncio
async def test_set_fog_disable_sends_enabled_false(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/lighting/set_fog").mock(
            return_value=_ok({"enabled": False, "dry_run": False})
        )
        result = await set_fog(False)
    sent = json.loads(route.calls[0].request.content)
    assert sent["enabled"] is False
    assert "disabled" in result
