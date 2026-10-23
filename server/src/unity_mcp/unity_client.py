"""
Async HTTP client for communicating with the Unity Editor plugin.

All MCP tools go through this module. It owns the request/response envelope
(success / data / error) and translates failures into typed Python exceptions
so that tool functions only need to handle two error cases:

  - UnityConnectionError  — Unity is not running or the plugin is not active
  - UnityError            — Unity is running but returned success:false

Connection configuration (in priority order)
---------------------------------------------
1. base_url argument passed to the constructor — used in tests.
2. UNITY_MCP_URL environment variable — full URL, e.g. http://192.168.1.50:11000
   Use this when the MCP server is NOT running on the same machine as Unity.
3. UNITY_MCP_HOST + UNITY_MCP_PORT environment variables — convenient
   alternative to UNITY_MCP_URL when you only need to change the host.
4. Default: http://localhost:11000 — correct when the MCP server runs on
   the same machine as the Unity Editor (the standard configuration).

Bridge token
------------
Unity refuses every request that does not carry its bridge token as
"Authorization: Bearer <token>". The token is read on every request, so a
regenerated token is picked up without restarting:
1. UNITY_MCP_TOKEN environment variable — needed when Unity runs on another
   machine (copy it there with Window > MCP Bridge > Copy Bridge Token).
2. ~/.unity-mcp/bridge-token — the file Unity writes on this machine.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Any

import httpx


# ---------------------------------------------------------------------------
# Exceptions
# ---------------------------------------------------------------------------

class UnityConnectionError(Exception):
    """Raised when the Unity Editor plugin cannot be reached over HTTP."""


class UnityError(Exception):
    """Raised when Unity returns a well-formed success:false response."""

    def __init__(self, code: str, message: str) -> None:
        self.code = code
        self.message = message
        super().__init__(f"[{code}] {message}")


# ---------------------------------------------------------------------------
# Bridge token
# ---------------------------------------------------------------------------

TOKEN_ENV_VAR = "UNITY_MCP_TOKEN"


def default_token_path() -> Path:
    """The file Unity writes the bridge token to on this machine."""
    return Path.home() / ".unity-mcp" / "bridge-token"


# ---------------------------------------------------------------------------
# Client
# ---------------------------------------------------------------------------

class UnityClient:
    """
    Thin async HTTP client that wraps the Unity plugin's REST interface.

    Usage
    -----
    # From a tool function — reuses the shared singleton:
    data = await UnityClient.instance().get("/status")

    # In tests — create a fresh client pointing at a mock server:
    client = UnityClient(base_url="http://localhost:11000")

    # Connecting to Unity on a different machine (set env var before starting):
    # UNITY_MCP_URL=http://192.168.1.50:11000 unity-mcp --transport stdio
    """

    _instance: UnityClient | None = None

    def __init__(
        self,
        base_url: str | None = None,
        token: str | None = None,
        token_path: Path | None = None,
    ) -> None:
        # token / token_path are for tests; normally the token comes from
        # UNITY_MCP_TOKEN or the token file (see _token()).
        self._fixed_token = token
        self._token_path = token_path

        if base_url:
            # Explicit argument — used in tests and direct construction.
            self.base_url = base_url.rstrip("/")
        elif url_env := os.environ.get("UNITY_MCP_URL"):
            # Full URL override — takes priority over component vars.
            # Useful when connecting to a Unity instance on a different machine.
            self.base_url = url_env.rstrip("/")
        else:
            # Component-based construction.
            # UNITY_MCP_HOST changes the target machine; UNITY_MCP_PORT changes the port.
            host = os.environ.get("UNITY_MCP_HOST", "localhost")
            port = os.environ.get("UNITY_MCP_PORT", "11000")
            self.base_url = f"http://{host}:{port}"

    # ------------------------------------------------------------------
    # Singleton
    # ------------------------------------------------------------------

    @classmethod
    def instance(cls) -> UnityClient:
        """Return the shared singleton, creating it on first call."""
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    # ------------------------------------------------------------------
    # HTTP methods
    # ------------------------------------------------------------------

    async def get(self, path: str) -> dict[str, Any]:
        """GET the given path and return the unwrapped data payload."""
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(f"{self.base_url}{path}", headers=self._auth_headers())
        except httpx.ConnectError as exc:
            raise UnityConnectionError(self._connection_hint()) from exc
        return self._unwrap(response)

    async def post(self, path: str, body: Any) -> dict[str, Any]:
        """
        POST a JSON body to the given path and return the unwrapped data payload.

        `body` may be a Pydantic model (serialised via model_dump) or a plain dict.
        """
        payload = body.model_dump() if hasattr(body, "model_dump") else body
        try:
            async with httpx.AsyncClient(timeout=30.0) as client:
                response = await client.post(
                    f"{self.base_url}{path}",
                    json=payload,
                    headers={"Content-Type": "application/json", **self._auth_headers()},
                )
        except httpx.ConnectError as exc:
            raise UnityConnectionError(self._connection_hint()) from exc
        return self._unwrap(response)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _token(self) -> str | None:
        """The bridge token: UNITY_MCP_TOKEN, else the token file, else None."""
        if self._fixed_token:
            return self._fixed_token
        env = os.environ.get(TOKEN_ENV_VAR, "").strip()
        if env:
            return env
        try:
            text = (self._token_path or default_token_path()).read_text(encoding="utf-8").strip()
        except OSError:
            return None
        return text or None

    def _auth_headers(self) -> dict[str, str]:
        token = self._token()
        return {"Authorization": f"Bearer {token}"} if token else {}

    def _unwrap(self, response: httpx.Response) -> dict[str, Any]:
        """
        Parse the Unity response envelope.
        Returns the `data` dict on success; raises UnityError on failure.
        """
        try:
            data = response.json()
        except ValueError:
            raise UnityError(
                f"HTTP_{response.status_code}",
                f"Unity answered with a non-JSON response: {response.text[:200]}",
            ) from None
        if not data.get("success", False):
            err = data.get("error") or {}
            code = err.get("code", "UNKNOWN")
            message = err.get("message", "Unknown error from Unity")
            if code == "UNAUTHORIZED":
                message += " " + self._token_hint()
            raise UnityError(code, message)
        # `data` key is optional on success (e.g. a void operation)
        return data.get("data") or {}

    def _token_hint(self) -> str:
        if os.environ.get(TOKEN_ENV_VAR, "").strip():
            return (
                f"This MCP server sends the token from {TOKEN_ENV_VAR}; "
                "check it matches the Unity machine's token."
            )
        path = self._token_path or default_token_path()
        if self._token():
            return (
                f"This MCP server sent the token from {path}. "
                f"If Unity runs on another machine, set {TOKEN_ENV_VAR} to that machine's token."
            )
        return (
            f"This MCP server found no token in {TOKEN_ENV_VAR} or {path}. "
            f"If Unity runs on another machine, set {TOKEN_ENV_VAR} to its token."
        )

    def _connection_hint(self) -> str:
        is_local = (
            self.base_url.startswith("http://localhost")
            or self.base_url.startswith("http://127.0.0.1")
        )
        if is_local:
            return (
                f"Cannot reach Unity Editor at {self.base_url}. "
                "Make sure Unity is open and the MCP Bridge plugin is active "
                "(check the Editor console for '[MCP Bridge] Listening on port …')."
            )
        return (
            f"Cannot reach Unity Editor at {self.base_url}. "
            "Make sure Unity is running on the target machine with the MCP Bridge "
            "active, and that no firewall is blocking the bridge port."
        )
