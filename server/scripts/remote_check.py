"""
Check a Unity MCP server running in network mode (streamable-http) on another
machine, from the machine that will connect to it.

Usage, on a machine with this repository (from server/):
    uv run python scripts/remote_check.py <server-ip> <server-token> [port]

On a machine without it, copy this one file; uv fetches Python and mcp by itself:
    uv run --with "mcp>=2.3,<3" remote_check.py <server-ip> <server-token> [port]

The server side, on the Unity machine (see Documentation~/REMOTE_SETUP.md):
    UNITY_MCP_SERVER_TOKEN=<token> uv run unity-mcp --transport streamable-http --host 0.0.0.0

It checks that:
  1. a request with a wrong token is refused (HTTP 401)
  2. an MCP session with the right token lists the tools
  3. ping_unity reaches Unity on that machine
"""

import asyncio
import sys
import urllib.error
import urllib.request

from mcp import Client
from mcp.client.streamable_http import create_mcp_http_client, streamable_http_client


def status_with_token(url: str, token: str) -> int:
    """HTTP status of a bare POST carrying this token (401 means the token was refused)."""
    request = urllib.request.Request(url, data=b"{}", method="POST", headers={
        "Authorization": f"Bearer {token}", "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream"})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status
    except urllib.error.HTTPError as exc:
        return exc.code


async def session_check(url: str, token: str) -> None:
    http = create_mcp_http_client(headers={"Authorization": f"Bearer {token}"})
    async with Client(streamable_http_client(url, http_client=http)) as client:
        tools = (await client.list_tools()).tools
        print(f"2. MCP session OK: {len(tools)} tools listed")
        result = await client.call_tool("ping_unity", {})
        print(f"3. ping_unity: {result.content[0].text}")


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    host, token = sys.argv[1], sys.argv[2]
    port = sys.argv[3] if len(sys.argv) > 3 else "8000"
    url = f"http://{host}:{port}/mcp"
    print(f"Server: {url}")

    try:
        refused = status_with_token(url, "wrong-token") == 401
        accepted = status_with_token(url, token) != 401
    except (OSError, urllib.error.URLError) as exc:
        print(f"Cannot reach {url}: {exc}\nCheck the IP, that the server is running with --host 0.0.0.0, "
              "and the Mac's firewall.")
        return 1
    print(f"1. Wrong token refused: {'yes' if refused else 'NO - check UNITY_MCP_SERVER_TOKEN is set'}")
    if not accepted:
        print(f"   Your token was refused too. It has {len(token)} characters; "
              "'openssl rand -hex 32' makes 64. Copy it again, whole.")
        return 1

    asyncio.run(session_check(url, token))
    return 0


if __name__ == "__main__":
    sys.exit(main())
