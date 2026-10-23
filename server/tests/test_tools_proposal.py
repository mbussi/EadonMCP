"""
Tests for proposal tools (Phase 5).
"""

import pytest
import json
import httpx
import respx

from unity_mcp.tools.proposal import (
    propose_changes,
    execute_proposal,
    discard_proposal,
    get_pending_proposals,
)
from unity_mcp.unity_client import UnityClient


BASE_URL   = "http://localhost:11000"
SAMPLE_OPS = [
    {"id": 1, "tool": "create_empty",  "description": "Create container", "params": {"name": "Container"}},
    {"id": 2, "tool": "place_prefab",  "description": "Place house",      "params": {"prefab_path": "Assets/House.prefab", "position": [0, 0, 0]}},
]


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
# propose_changes
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_propose_changes_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/proposal/submit").mock(
            return_value=_ok({"proposal_id": "abc123", "operation_count": 2})
        )
        await propose_changes("Test proposal", SAMPLE_OPS)
    assert route.called


@pytest.mark.asyncio
async def test_propose_changes_sends_summary_and_operations(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/proposal/submit").mock(
            return_value=_ok({"proposal_id": "abc123", "operation_count": 2})
        )
        await propose_changes("My proposal", SAMPLE_OPS)
    sent = json.loads(route.calls[0].request.content)
    assert sent["summary"]            == "My proposal"
    assert len(sent["operations"])    == 2
    assert sent["operations"][0]["tool"] == "create_empty"


@pytest.mark.asyncio
async def test_propose_changes_includes_proposal_id_in_response(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/proposal/submit").mock(
            return_value=_ok({"proposal_id": "xyz789", "operation_count": 1})
        )
        result = await propose_changes("Test", SAMPLE_OPS[:1])
    assert "xyz789" in result


# ---------------------------------------------------------------------------
# execute_proposal
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_execute_proposal_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/proposal/execute").mock(
            return_value=_ok({"proposal_id": "abc123", "executed_count": 2, "failed_count": 0, "results": []})
        )
        await execute_proposal("abc123")
    assert route.called


@pytest.mark.asyncio
async def test_execute_proposal_sends_proposal_id(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/proposal/execute").mock(
            return_value=_ok({"proposal_id": "abc123", "executed_count": 1, "failed_count": 0, "results": []})
        )
        await execute_proposal("abc123")
    sent = json.loads(route.calls[0].request.content)
    assert sent["proposal_id"] == "abc123"


@pytest.mark.asyncio
async def test_execute_proposal_reports_success_count(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/proposal/execute").mock(
            return_value=_ok({"proposal_id": "abc123", "executed_count": 3, "failed_count": 0, "results": []})
        )
        result = await execute_proposal("abc123")
    assert "3 succeeded" in result


@pytest.mark.asyncio
async def test_execute_proposal_reports_failures(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/proposal/execute").mock(
            return_value=_ok({
                "proposal_id": "abc123",
                "executed_count": 1,
                "failed_count": 1,
                "results": [{"operation_id": 2, "success": False, "error": "PREFAB_NOT_FOUND"}]
            })
        )
        result = await execute_proposal("abc123")
    assert "1 failed" in result
    assert "PREFAB_NOT_FOUND" in result


@pytest.mark.asyncio
async def test_execute_proposal_returns_error_for_unknown_id(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/proposal/execute").mock(
            return_value=_err("PROPOSAL_NOT_FOUND", "No proposal with id 'bad_id'")
        )
        result = await execute_proposal("bad_id")
    assert "PROPOSAL_NOT_FOUND" in result


# ---------------------------------------------------------------------------
# discard_proposal
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_discard_proposal_posts_to_correct_endpoint(client):
    with respx.mock:
        route = respx.post(f"{BASE_URL}/tools/proposal/discard").mock(
            return_value=_ok({"proposal_id": "abc123"})
        )
        await discard_proposal("abc123")
    assert route.called


@pytest.mark.asyncio
async def test_discard_proposal_confirms_no_changes(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/proposal/discard").mock(
            return_value=_ok({"proposal_id": "abc123"})
        )
        result = await discard_proposal("abc123")
    assert "No changes" in result


# ---------------------------------------------------------------------------
# get_pending_proposals
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_pending_proposals_returns_json_string(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/tools/proposal/pending").mock(
            return_value=_ok({"proposals": [
                {"proposal_id": "abc123", "summary": "Test", "operation_count": 2}
            ]})
        )
        result = await get_pending_proposals()
    parsed = json.loads(result)
    assert len(parsed) == 1
    assert parsed[0]["proposal_id"] == "abc123"


@pytest.mark.asyncio
async def test_get_pending_proposals_returns_message_when_empty(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/tools/proposal/pending").mock(
            return_value=_ok({"proposals": []})
        )
        result = await get_pending_proposals()
    assert "No pending" in result
