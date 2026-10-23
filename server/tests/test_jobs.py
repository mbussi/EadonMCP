"""
Tests for long requests: the client following a handed-off job, transport
errors, and the get_job_status / cancel_job tools.
"""

import json

import httpx
import pytest
import respx

from unity_mcp.tools.jobs import cancel_job, get_job_status
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError, UnityJobPending


BASE_URL = "http://localhost:11000"


@pytest.fixture(autouse=True)
def client(tmp_path) -> UnityClient:
    instance = UnityClient(base_url=BASE_URL, token_path=tmp_path / "no-token",
                           job_wait_seconds=0.5, job_poll_seconds=0.01)
    UnityClient._instance = instance
    return instance


def pending(job_id="job-1-abc", state="running", tool="place_prefab_batch"):
    return httpx.Response(202, json={"success": True, "pending": True,
                                     "data": {"job_id": job_id, "state": state, "tool": tool}})


def job_status(state, result=None, job_id="job-1-abc"):
    data = {"job_id": job_id, "state": state}
    if result is not None:
        data["result"] = result
    return httpx.Response(200, json={"success": True, "data": data})


# ---------------------------------------------------------------------------
# Following a handed-off job
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_pending_request_returns_the_job_result(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(return_value=pending())
        respx.get(f"{BASE_URL}/jobs").mock(side_effect=[
            job_status("queued"),
            job_status("running"),
            job_status("done", {"success": True, "data": {"placed": 40}}),
        ])
        data = await client.post("/tools/placement/place_batch", {"placements": []})

    assert data == {"placed": 40}


@pytest.mark.asyncio
async def test_pending_request_polls_with_the_job_id(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/scene/hierarchy").mock(return_value=pending(job_id="job-7-xyz"))
        route = respx.get(f"{BASE_URL}/jobs").mock(
            return_value=job_status("done", {"success": True, "data": {}}, job_id="job-7-xyz"))
        await client.get("/scene/hierarchy")

    assert route.calls.last.request.url.params["job_id"] == "job-7-xyz"


@pytest.mark.asyncio
async def test_failed_job_raises_its_error(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(return_value=pending())
        respx.get(f"{BASE_URL}/jobs").mock(return_value=job_status(
            "failed", {"success": False, "error": {"code": "PREFAB_NOT_FOUND", "message": "No prefab."}}))
        with pytest.raises(UnityError) as info:
            await client.post("/tools/placement/place_batch", {})

    assert info.value.code == "PREFAB_NOT_FOUND"


@pytest.mark.asyncio
async def test_job_still_running_after_the_wait_raises_job_pending(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(return_value=pending())
        respx.get(f"{BASE_URL}/jobs").mock(return_value=job_status("running"))
        with pytest.raises(UnityJobPending) as info:
            await client.post("/tools/placement/place_batch", {})

    assert info.value.code == "STILL_RUNNING"
    assert info.value.job_id == "job-1-abc"
    assert 'get_job_status("job-1-abc")' in info.value.message
    assert "run twice" in info.value.message


@pytest.mark.asyncio
async def test_unity_lost_while_polling_says_which_job(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/placement/place_batch").mock(return_value=pending())
        respx.get(f"{BASE_URL}/jobs").mock(side_effect=httpx.ConnectError("refused"))
        with pytest.raises(UnityConnectionError) as info:
            await client.post("/tools/placement/place_batch", {})

    assert "job-1-abc" in str(info.value)


# ---------------------------------------------------------------------------
# Transport errors
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_timeout_becomes_a_connection_error_that_warns_about_repeats(client):
    with respx.mock:
        respx.post(f"{BASE_URL}/tools/world/bake_navmesh").mock(side_effect=httpx.ReadTimeout(""))
        with pytest.raises(UnityConnectionError) as info:
            await client.post("/tools/world/bake_navmesh", {})

    assert "did not answer" in str(info.value)
    assert "before repeating" in str(info.value)


@pytest.mark.asyncio
async def test_dropped_connection_becomes_a_connection_error(client):
    with respx.mock:
        respx.get(f"{BASE_URL}/status").mock(side_effect=httpx.RemoteProtocolError("closed"))
        with pytest.raises(UnityConnectionError) as info:
            await client.get("/status")

    assert "connection to Unity was lost" in str(info.value)


# ---------------------------------------------------------------------------
# get_job_status / cancel_job
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_job_status_with_an_id():
    with respx.mock:
        route = respx.get(f"{BASE_URL}/jobs").mock(return_value=job_status("running"))
        result = await get_job_status("job-1-abc")

    assert route.calls.last.request.url.params["job_id"] == "job-1-abc"
    assert json.loads(result)["state"] == "running"


@pytest.mark.asyncio
async def test_get_job_status_without_an_id_lists_jobs():
    with respx.mock:
        route = respx.get(f"{BASE_URL}/jobs").mock(return_value=httpx.Response(
            200, json={"success": True, "data": {"count": 0, "jobs": []}}))
        result = await get_job_status()

    assert "job_id" not in route.calls.last.request.url.params
    assert json.loads(result) == {"count": 0, "jobs": []}


@pytest.mark.asyncio
async def test_cancel_job_posts_the_id():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["body"] = json.loads(request.content)
        return job_status("cancelled")

    with respx.mock:
        respx.post(f"{BASE_URL}/jobs/cancel").mock(side_effect=handler)
        await cancel_job("job-1-abc")

    assert seen["body"] == {"job_id": "job-1-abc"}


@pytest.mark.asyncio
async def test_cancel_job_reports_a_refusal():
    with respx.mock:
        respx.post(f"{BASE_URL}/jobs/cancel").mock(return_value=httpx.Response(
            400, json={"success": False, "error": {"code": "CANNOT_CANCEL", "message": "Already started."}}))
        result = await cancel_job("job-1-abc")

    assert "CANNOT_CANCEL" in result
