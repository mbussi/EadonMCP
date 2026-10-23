"""
Tests for UnityClient.

All HTTP interactions are intercepted by respx so no real Unity Editor
is required. Tests are grouped by HTTP method and failure mode.
"""

import pytest
import httpx
import respx

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


BASE_URL = "http://localhost:11000"


@pytest.fixture
def client() -> UnityClient:
    """A UnityClient pointed at the standard test base URL."""
    return UnityClient(base_url=BASE_URL)


# ---------------------------------------------------------------------------
# GET — success path
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_returns_data_on_success(client: UnityClient):
    payload = {"unity_version": "2022.3.0f1", "scene_name": "Level_01", "scene_path": "Assets/Scenes/Level_01.unity", "is_playing": False}
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(
            return_value=httpx.Response(200, json={"success": True, "data": payload})
        )
        result = await client.get("/status")

    assert result["unity_version"] == "2022.3.0f1"
    assert result["scene_name"] == "Level_01"


@pytest.mark.asyncio
async def test_get_returns_empty_dict_when_data_key_is_absent(client: UnityClient):
    """success:true with no data key is valid — void operations return nothing."""
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(
            return_value=httpx.Response(200, json={"success": True})
        )
        result = await client.get("/status")

    assert result == {}


# ---------------------------------------------------------------------------
# GET — failure paths
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_raises_unity_error_on_failure_response(client: UnityClient):
    with respx.mock:
        respx.get(f"{BASE_URL}/bad-route").mock(
            return_value=httpx.Response(
                404,
                json={"success": False, "error": {"code": "NOT_FOUND", "message": "No such route"}},
            )
        )
        with pytest.raises(UnityError) as exc_info:
            await client.get("/bad-route")

    assert exc_info.value.code == "NOT_FOUND"
    assert "No such route" in exc_info.value.message


@pytest.mark.asyncio
async def test_get_raises_connection_error_when_unity_is_not_running(client: UnityClient):
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=httpx.ConnectError("Connection refused"))
        with pytest.raises(UnityConnectionError):
            await client.get("/status")


@pytest.mark.asyncio
async def test_get_raises_unity_error_with_unknown_code_when_error_key_missing(client: UnityClient):
    """Malformed error payloads should still raise UnityError, not KeyError."""
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(
            return_value=httpx.Response(500, json={"success": False})
        )
        with pytest.raises(UnityError) as exc_info:
            await client.get("/status")

    assert exc_info.value.code == "UNKNOWN"


# ---------------------------------------------------------------------------
# POST — success path
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_post_sends_json_body_and_returns_data(client: UnityClient):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=httpx.Response(
                200,
                json={"success": True, "data": {"instance_path": "/Level/House"}},
            )
        )
        result = await client.post(
            "/tools/placement/place_prefab",
            {"prefab_path": "Assets/Prefabs/House.prefab", "position": [0, 0, 0]},
        )

    assert result["instance_path"] == "/Level/House"
    assert route.calls[0].request.headers["content-type"] == "application/json"


@pytest.mark.asyncio
async def test_post_serialises_pydantic_model(client: UnityClient):
    """Pydantic models are serialised via model_dump before sending."""
    from pydantic import BaseModel

    class SampleRequest(BaseModel):
        prefab_path: str
        position: list[float]

    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/test").mock(
            return_value=httpx.Response(200, json={"success": True, "data": {}})
        )
        await client.post("/tools/test", SampleRequest(prefab_path="Assets/X.prefab", position=[1.0, 2.0, 3.0]))

    import json
    sent = json.loads(route.calls[0].request.content)
    assert sent["prefab_path"] == "Assets/X.prefab"
    assert sent["position"] == [1.0, 2.0, 3.0]


@pytest.mark.asyncio
async def test_post_raises_unity_error_on_failure(client: UnityClient):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_prefab").mock(
            return_value=httpx.Response(
                400,
                json={"success": False, "error": {"code": "PREFAB_NOT_FOUND", "message": "Path not found"}},
            )
        )
        with pytest.raises(UnityError) as exc_info:
            await client.post("/tools/placement/place_prefab", {})

    assert exc_info.value.code == "PREFAB_NOT_FOUND"


# ---------------------------------------------------------------------------
# Singleton and configuration
# ---------------------------------------------------------------------------

def test_instance_returns_same_object():
    a = UnityClient.instance()
    b = UnityClient.instance()
    assert a is b


def test_instance_uses_env_port(monkeypatch):
    monkeypatch.setenv("UNITY_MCP_PORT", "12345")
    client = UnityClient()
    assert "12345" in client.base_url


def test_base_url_trailing_slash_is_stripped():
    client = UnityClient(base_url="http://localhost:11000/")
    assert not client.base_url.endswith("/")
