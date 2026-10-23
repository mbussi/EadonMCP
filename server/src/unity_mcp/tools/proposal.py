"""
Proposal and validation tools — Phase 5.

These tools implement the human-in-the-loop approval workflow. The AI
calls propose_changes to submit a plan, the human reviews it in Unity's
MCP Operation Queue window, then the AI calls execute_proposal to apply
the approved operations as a single undoable action.

Typical session flow
--------------------
1. AI calls get_scene_context + get_prefab_catalog to understand the scene.
2. AI calls propose_changes with a list of operations.
3. Human reviews the proposal in the Operation Queue window (Window > MCP Bridge > Operation Queue).
4. Human approves → AI calls execute_proposal.
   Human rejects → AI calls discard_proposal.
"""

import json
from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def propose_changes(
    summary: str,
    operations: list[dict],
) -> str:
    """
    Submit a list of proposed operations for human review in the Unity Editor.

    Does not execute anything. Returns a proposal_id to pass to execute_proposal
    once the human approves in the MCP Operation Queue window.

    summary:    A brief description of the overall intent, shown as the proposal title.
    operations: List of operation descriptors. Each must have:
                  id          — unique integer within this proposal
                  tool        — tool name, e.g. "place_prefab", "create_light"
                  description — plain-English description shown in the review UI
                  params      — dict of parameters exactly as you would pass to the tool
    """
    try:
        data = await UnityClient.instance().post("/tools/proposal/submit", {
            "summary":    summary,
            "operations": operations,
        })
        proposal_id = data.get("proposal_id", "unknown")
        count       = data.get("operation_count", len(operations))
        return (
            f"Proposal '{proposal_id}' submitted: \"{summary}\" ({count} operation(s)). "
            f"Open Window > MCP Bridge > Operation Queue in Unity to review and execute."
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def execute_proposal(proposal_id: str) -> str:
    """
    Execute a pending proposal by its ID.

    All operations are applied in order as a single Undo group — one Ctrl+Z
    reverses the entire proposal. The proposal must be in Pending status.

    proposal_id: The ID returned by propose_changes.
    """
    try:
        data = await UnityClient.instance().post("/tools/proposal/execute", {
            "proposal_id": proposal_id,
        })
        executed = data.get("executed_count", 0)
        failed   = data.get("failed_count",   0)
        status   = f"{executed} succeeded" + (f", {failed} failed" if failed else "")
        results  = data.get("results", [])
        errors   = [r for r in results if not r.get("success")]
        msg      = f"Executed proposal '{proposal_id}': {status}."
        if errors:
            details = "; ".join(r.get("error", "unknown error") for r in errors)
            msg += f" Failures: {details}"
        return msg
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def discard_proposal(proposal_id: str) -> str:
    """
    Discard a pending proposal without executing it.

    proposal_id: The ID returned by propose_changes.
    """
    try:
        await UnityClient.instance().post("/tools/proposal/discard", {
            "proposal_id": proposal_id,
        })
        return f"Proposal '{proposal_id}' discarded. No changes were made."
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def get_pending_proposals() -> str:
    """
    Return all proposals currently waiting for human review.

    Use this to check whether any proposals are pending before submitting
    a new one, or to retrieve a proposal_id if you have lost track of it.
    """
    try:
        data = await UnityClient.instance().get("/tools/proposal/pending")
        proposals = data.get("proposals", [])
        if not proposals:
            return "No pending proposals."
        return json.dumps(proposals, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
