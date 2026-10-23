"""
Jobs: requests Unity is still working on, and background operations.

Unity answers every request within about 20 s. A request that takes longer
(a large batch, a slow navmesh bake, Unity busy compiling) becomes a job: the
tool call waits for it up to about 45 s and then returns its job ID. A
lighting bake always runs as a job. These two tools follow and cancel jobs.
"""

from __future__ import annotations

import json
import urllib.parse

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def get_job_status(job_id: str | None = None) -> str:
    """
    Check a job: a request Unity is still working on, or a background lighting bake.

    Use it when a tool returned STILL_RUNNING with a job ID, or after bake_lighting.
    Never repeat a request that is still running: it would run twice.

    With a job_id: returns the job's state ("queued" = waiting for Unity's main
    thread, "running", "done", "failed", "cancelled"), progress when the
    operation reports it, and once finished its result (the same success/data
    or error the request would have returned).
    Without a job_id: lists recent jobs, newest first.

    Unity keeps the last 50 jobs; a script reload clears them.

    job_id: e.g. "job-12-a1b2c3" (optional)
    """
    try:
        query = f"?job_id={urllib.parse.quote(job_id)}" if job_id else ""
        data = await UnityClient.instance().get(f"/jobs{query}")
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def cancel_job(job_id: str) -> str:
    """
    Cancel a job that Unity has not started yet, or stop a background lighting bake.

    A queued job (waiting for Unity's main thread) is cancelled at once and never
    runs. A job Unity has already started cannot be stopped safely halfway and is
    refused, except a lighting bake, which Unity stops.

    job_id: the job's ID, from get_job_status or a STILL_RUNNING message
    """
    try:
        data = await UnityClient.instance().post("/jobs/cancel", {"job_id": job_id})
        return json.dumps(data, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
