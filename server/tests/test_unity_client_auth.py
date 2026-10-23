"""
Tests for the bridge token (Unity side) and the network transport token
(MCP server side).

UnityClient must send "Authorization: Bearer <token>" to Unity, reading the
token from UNITY_MCP_TOKEN or the token file. The sse / streamable-http
transports must refuse requests without UNITY_MCP_SERVER_TOKEN, and must
refuse to listen beyond this machine without one.
"""

import sys

import httpx
import pytest
import respx
from starlette.applications import Starlette
from starlette.responses import PlainTextResponse
from starlette.routing import Route
from starlette.testclient import TestClient

from unity_mcp import server
from unity_mcp.unity_client import UnityClient, UnityError


BASE_URL = "http://localhost:11000"


@pytest.fixture(autouse=True)
def no_token_env(monkeypatch):
    monkeypatch.delenv("UNITY_MCP_TOKEN", raising=False)
    monkeypatch.delenv("UNITY_MCP_SERVER_TOKEN", raising=False)


@pytest.fixture
def token_file(tmp_path):
    path = tmp_path / "bridge-token"
    path.write_text("file-token\n", encoding="utf-8")
    return path


def ok(request: httpx.Request) -> httpx.Response:
    return httpx.Response(200, json={"success": True, "data": {"auth": request.headers.get("Authorization")}})


# ---------------------------------------------------------------------------
# UnityClient — sending the bridge token
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_sends_token_from_file(token_file):
    client = UnityClient(base_url=BASE_URL, token_path=token_file)
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=ok)
        result = await client.get("/status")
    assert result["auth"] == "Bearer file-token"


@pytest.mark.asyncio
async def test_post_sends_token_and_json_content_type(token_file):
    client = UnityClient(base_url=BASE_URL, token_path=token_file)
    seen = {}

    def capture(request: httpx.Request) -> httpx.Response:
        seen["auth"] = request.headers.get("Authorization")
        seen["type"] = request.headers.get("Content-Type")
        return httpx.Response(200, json={"success": True})

    with respx.mock:
        respx.post(f"{BASE_URL}/tools/x").mock(side_effect=capture)
        await client.post("/tools/x", {"a": 1})

    assert seen["auth"] == "Bearer file-token"
    assert seen["type"].startswith("application/json")


@pytest.mark.asyncio
async def test_env_token_wins_over_file(monkeypatch, token_file):
    monkeypatch.setenv("UNITY_MCP_TOKEN", "  env-token ")
    client = UnityClient(base_url=BASE_URL, token_path=token_file)
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=ok)
        result = await client.get("/status")
    assert result["auth"] == "Bearer env-token"


@pytest.mark.asyncio
async def test_token_is_reread_so_a_regenerated_token_is_picked_up(token_file):
    client = UnityClient(base_url=BASE_URL, token_path=token_file)
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=ok)
        first = await client.get("/status")
        token_file.write_text("new-token", encoding="utf-8")
        second = await client.get("/status")
    assert first["auth"] == "Bearer file-token"
    assert second["auth"] == "Bearer new-token"


@pytest.mark.asyncio
async def test_no_header_when_no_token_anywhere(tmp_path):
    client = UnityClient(base_url=BASE_URL, token_path=tmp_path / "missing")
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=ok)
        result = await client.get("/status")
    assert result["auth"] is None


@pytest.mark.asyncio
async def test_unauthorized_error_explains_where_the_token_comes_from(tmp_path):
    client = UnityClient(base_url=BASE_URL, token_path=tmp_path / "missing")
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(
            return_value=httpx.Response(401, json={
                "success": False,
                "error": {"code": "UNAUTHORIZED", "message": "Missing or wrong bridge token."},
            })
        )
        with pytest.raises(UnityError) as exc:
            await client.get("/status")

    assert exc.value.code == "UNAUTHORIZED"
    assert "UNITY_MCP_TOKEN" in exc.value.message


@pytest.mark.asyncio
async def test_non_json_response_becomes_unity_error(token_file):
    client = UnityClient(base_url=BASE_URL, token_path=token_file)
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(return_value=httpx.Response(400, text="<html>Bad Request</html>"))
        with pytest.raises(UnityError) as exc:
            await client.get("/status")
    assert exc.value.code == "HTTP_400"


# ---------------------------------------------------------------------------
# Network transport — server token
# ---------------------------------------------------------------------------

def protected_app(token: str = "server-token"):
    app = Starlette(routes=[Route("/sse", lambda request: PlainTextResponse("ok"))])
    return server.BearerTokenMiddleware(app, token)


def test_middleware_refuses_missing_token():
    with TestClient(protected_app()) as client:
        assert client.get("/sse").status_code == 401


def test_middleware_refuses_wrong_token():
    with TestClient(protected_app()) as client:
        assert client.get("/sse", headers={"Authorization": "Bearer nope"}).status_code == 401


def test_middleware_allows_right_token():
    with TestClient(protected_app()) as client:
        response = client.get("/sse", headers={"Authorization": "bearer server-token"})
    assert response.status_code == 200
    assert response.text == "ok"


def test_refuses_to_listen_on_the_network_without_a_server_token(monkeypatch):
    monkeypatch.setattr(sys, "argv", ["unity-mcp", "--transport", "sse", "--host", "0.0.0.0"])
    monkeypatch.setattr(server, "run_network", lambda *a: pytest.fail("must not start"))
    with pytest.raises(SystemExit) as exc:
        server.main()
    assert exc.value.code == 2


def test_listens_on_the_network_with_a_server_token(monkeypatch):
    calls = []
    monkeypatch.setenv("UNITY_MCP_SERVER_TOKEN", "server-token")
    monkeypatch.setattr(sys, "argv", ["unity-mcp", "--transport", "streamable-http", "--host", "0.0.0.0"])
    monkeypatch.setattr(server, "run_network", lambda *a: calls.append(a))
    server.main()
    assert calls == [("streamable-http", "0.0.0.0", 8000, "server-token")]


def test_network_transport_defaults_to_this_machine_only(monkeypatch):
    calls = []
    monkeypatch.delenv("UNITY_MCP_SERVER_HOST", raising=False)
    monkeypatch.setattr(sys, "argv", ["unity-mcp", "--transport", "sse"])
    monkeypatch.setattr(server, "run_network", lambda *a: calls.append(a))
    server.main()
    assert calls == [("sse", "127.0.0.1", 8000, "")]
