"""
Proposal and validation tools — Phase 5.

These tools implement the human-in-the-loop review workflow. The AI calls
propose_changes to submit a plan; the user reviews it in Unity's Operation
Queue window (Eadon > MCP Bridge > Operation Queue) and either executes it
there or approves it, after which the AI may call execute_proposal.

A proposal runs once, as a single undo step, and all or nothing: if any
operation fails, everything the proposal did is undone.

Typical session flow
--------------------
1. AI calls get_scene_context + get_prefab_catalog to understand the scene.
2. AI calls propose_changes with a list of operations.
3. The user reviews the proposal in the Operation Queue window and clicks
   Execute (done), Approve (the AI then calls execute_proposal) or Discard.
4. The AI checks the outcome with get_pending_proposals(proposal_id).
"""

import json
import urllib.parse

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def propose_changes(
    summary: str,
    operations: list[dict],
) -> str:
    """
    Submit a list of scene changes for the user to review in the Unity Editor
    before anything happens.

    Does not execute anything. Returns a proposal_id. The user reviews the
    proposal in Eadon > MCP Bridge > Operation Queue, where they can Execute it,
    Approve it (then call execute_proposal) or Discard it.

    A proposal runs as one undo step and all or nothing: if an operation fails,
    everything the proposal did is undone.

    summary:    A brief description of the overall intent, shown as the proposal title.
    operations: List of operation descriptors. Each must have:
                  id          — unique integer within this proposal
                  tool        — tool name, e.g. "place_prefab", "scatter_prefabs"
                  description — plain-English description shown in the review UI
                  params      — dict of arguments exactly as you would pass to the tool
                                (dry_run is ignored: the proposal is the review)

    Only tools whose changes Unity's Undo reverses can be part of a proposal:
    placing, moving, deleting, duplicating, grouping and parenting objects;
    components, properties, materials, lights, ambient light, skybox, fog,
    trigger/audio/reflection-probe volumes, navmesh areas, terrain painting,
    scaffolding, markers and patrol routes. Tools that write files or assets,
    bake, switch scenes or only read are refused, and the error lists the
    tools a proposal can run.
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
            f"Ask the user to review it in Eadon > MCP Bridge > Operation Queue: they can "
            f"Execute it there, or Approve it so you can run execute_proposal('{proposal_id}')."
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def execute_proposal(proposal_id: str) -> str:
    """
    Execute a proposal the user has approved in the Operation Queue window.

    A proposal the user has not approved is refused (PROPOSAL_NOT_APPROVED):
    ask them to Approve or Execute it in Eadon > MCP Bridge > Operation Queue.

    The operations run in order as a single undo step — one Ctrl+Z reverses
    the whole proposal. If one fails, everything the proposal did is undone and
    the error (PROPOSAL_FAILED) names the operation and why; submit a corrected
    proposal. A proposal runs only once.

    proposal_id: The ID returned by propose_changes.
    """
    try:
        data = await UnityClient.instance().post("/tools/proposal/execute", {
            "proposal_id": proposal_id,
        })
        executed = data.get("executed_count", 0)
        return (
            f"Executed proposal '{proposal_id}': {executed} operation(s) applied "
            f"as one undo step."
        )
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"


async def discard_proposal(proposal_id: str) -> str:
    """
    Discard a pending or approved proposal without executing it.

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


async def get_pending_proposals(proposal_id: str | None = None) -> str:
    """
    List proposals waiting for a decision, or check what happened to one.

    Without a proposal_id: every open proposal with its status — "pending"
    (waiting for the user's review), "approved" (execute_proposal may run it)
    or "executing".

    With a proposal_id: that proposal in full, whatever its status ("pending",
    "approved", "executing", "executed", "failed", "discarded"): each
    operation's params and, once it has run, its results and any failure. Use
    it to learn whether the user executed or discarded a proposal in the
    Operation Queue window.

    proposal_id: The ID returned by propose_changes (optional).
    """
    try:
        query = f"?proposal_id={urllib.parse.quote(proposal_id)}" if proposal_id else ""
        data = await UnityClient.instance().get(f"/tools/proposal/pending{query}")
        if proposal_id:
            return json.dumps(data, indent=2)
        proposals = data.get("proposals", [])
        if not proposals:
            return "No proposals waiting for a decision."
        return json.dumps(proposals, indent=2)
    except UnityConnectionError as exc:
        return f"Cannot connect to Unity: {exc}"
    except UnityError as exc:
        return f"Unity returned an error: {exc}"
