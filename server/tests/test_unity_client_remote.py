"""
Additional tests for UnityClient connection configuration.
Append these to tests/test_unity_client.py, or run as a standalone file.
"""

import pytest
from unity_mcp.unity_client import UnityClient


# ---------------------------------------------------------------------------
# New env var configuration tests
# ---------------------------------------------------------------------------

def test_unity_mcp_url_sets_base_url(monkeypatch):
    """UNITY_MCP_URL sets the full base URL and overrides host/port vars."""
    monkeypatch.setenv("UNITY_MCP_URL", "http://192.168.1.50:11000")
    monkeypatch.delenv("UNITY_MCP_HOST", raising=False)
    monkeypatch.delenv("UNITY_MCP_PORT", raising=False)
    client = UnityClient()
    assert client.base_url == "http://192.168.1.50:11000"


def test_unity_mcp_url_strips_trailing_slash(monkeypatch):
    monkeypatch.setenv("UNITY_MCP_URL", "http://192.168.1.50:11000/")
    client = UnityClient()
    assert not client.base_url.endswith("/")


def test_unity_mcp_url_takes_priority_over_host_and_port(monkeypatch):
    """UNITY_MCP_URL wins even when UNITY_MCP_HOST and UNITY_MCP_PORT are also set."""
    monkeypatch.setenv("UNITY_MCP_URL",  "http://machine-b:11000")
    monkeypatch.setenv("UNITY_MCP_HOST", "machine-c")
    monkeypatch.setenv("UNITY_MCP_PORT", "12345")
    client = UnityClient()
    assert client.base_url == "http://machine-b:11000"


def test_unity_mcp_host_sets_host(monkeypatch):
    """UNITY_MCP_HOST changes only the hostname, port stays at default 11000."""
    monkeypatch.delenv("UNITY_MCP_URL", raising=False)
    monkeypatch.setenv("UNITY_MCP_HOST", "192.168.1.99")
    monkeypatch.delenv("UNITY_MCP_PORT", raising=False)
    client = UnityClient()
    assert client.base_url == "http://192.168.1.99:11000"


def test_unity_mcp_host_and_port_combine(monkeypatch):
    """UNITY_MCP_HOST and UNITY_MCP_PORT can be set independently."""
    monkeypatch.delenv("UNITY_MCP_URL", raising=False)
    monkeypatch.setenv("UNITY_MCP_HOST", "build-machine")
    monkeypatch.setenv("UNITY_MCP_PORT", "12000")
    client = UnityClient()
    assert client.base_url == "http://build-machine:12000"


def test_explicit_base_url_beats_all_env_vars(monkeypatch):
    """Constructor base_url argument always wins over any environment variable."""
    monkeypatch.setenv("UNITY_MCP_URL",  "http://env-machine:11000")
    monkeypatch.setenv("UNITY_MCP_HOST", "env-host")
    monkeypatch.setenv("UNITY_MCP_PORT", "99999")
    client = UnityClient(base_url="http://explicit-host:22222")
    assert client.base_url == "http://explicit-host:22222"


def test_default_is_localhost_when_no_env_vars(monkeypatch):
    """Without any env vars the client targets localhost:11000."""
    monkeypatch.delenv("UNITY_MCP_URL",  raising=False)
    monkeypatch.delenv("UNITY_MCP_HOST", raising=False)
    monkeypatch.delenv("UNITY_MCP_PORT", raising=False)
    client = UnityClient()
    assert client.base_url == "http://localhost:11000"


def test_connection_hint_is_remote_aware():
    """_connection_hint produces a different message for remote vs local URLs."""
    local_client  = UnityClient(base_url="http://localhost:11000")
    remote_client = UnityClient(base_url="http://192.168.1.50:11000")
    local_hint  = local_client._connection_hint()
    remote_hint = remote_client._connection_hint()
    # Local hint should mention the Unity Editor console
    assert "console" in local_hint.lower() or "plugin" in local_hint.lower()
    # Remote hint should not, but should mention the target machine
    assert "target machine" in remote_hint.lower() or "firewall" in remote_hint.lower()
    assert local_hint != remote_hint
