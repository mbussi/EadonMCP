"""
Tests for list_unity_tools and call_unity_tool, the generic tools that reach
the tools Unity extensions register with the bridge.
"""

import json

import httpx
import pytest
import respx

from unity_mcp.tools.catalog import call_unity_tool, list_unity_tools
from unity_mcp.unity_client import UnityClient


BASE_URL = "http://localhost:11000"


@pytest.fixture(autouse=True)
def client(tmp_path) -> UnityClient:
    instance = UnityClient(base_url=BASE_URL, token_path=tmp_path / "no-token")
    UnityClient._instance = instance
    return instance


@pytest.mark.asyncio
async def test_list_unity_tools_lists_extension_tools():
    catalog = {"count": 1, "tools": [{"name": "get_pcg_graphs", "summary": "Lists graphs."}]}
    with respx.mock:
        route = respx.get(f"{BASE_URL}/tools/catalog").mock(
            return_value=httpx.Response(200, json={"success": True, "data": catalog}))
        result = await list_unity_tools()

    assert route.calls.last.request.url.params["source"] == "extensions"
    assert json.loads(result) == catalog


@pytest.mark.asyncio
async def test_list_unity_tools_with_name_describes_the_tool():
    tool = {"name": "create_pcg_graph", "description": "Full guide."}
    with respx.mock:
        route = respx.get(f"{BASE_URL}/tools/describe").mock(
            return_value=httpx.Response(200, json={"success": True, "data": tool}))
        result = await list_unity_tools("create_pcg_graph")

    assert route.calls.last.request.url.params["name"] == "create_pcg_graph"
    assert json.loads(result) == tool


@pytest.mark.asyncio
async def test_call_unity_tool_posts_name_and_arguments():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["body"] = json.loads(request.content)
        return httpx.Response(200, json={"success": True, "data": {"valid": True}})

    with respx.mock:
        respx.post(f"{BASE_URL}/tools/call").mock(side_effect=handler)
        result = await call_unity_tool("validate_pcg_graph", {"pcgg_json": "{}"})

    assert seen["body"] == {"name": "validate_pcg_graph", "arguments": {"pcgg_json": "{}"}}
    assert json.loads(result) == {"valid": True}


@pytest.mark.asyncio
async def test_call_unity_tool_defaults_to_empty_arguments():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["body"] = json.loads(request.content)
        return httpx.Response(200, json={"success": True, "data": {}})

    with respx.mock:
        respx.post(f"{BASE_URL}/tools/call").mock(side_effect=handler)
        await call_unity_tool("get_pcg_graphs")

    assert seen["body"] == {"name": "get_pcg_graphs", "arguments": {}}


@pytest.mark.asyncio
async def test_call_unity_tool_reports_unity_errors():
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/call").mock(return_value=httpx.Response(
            200, json={"success": False, "error": {"code": "NOT_FOUND", "message": "No tool named 'x'."}}))
        result = await call_unity_tool("x")

    assert result.startswith("Unity returned an error")
