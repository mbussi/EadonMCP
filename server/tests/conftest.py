"""
Shared pytest fixtures.

The most important one here is reset_client_singleton, which runs automatically
before every test to prevent the shared UnityClient instance from leaking
state between tests.
"""

import pytest
from unity_mcp.unity_client import UnityClient


@pytest.fixture(autouse=True)
def reset_client_singleton():
    """Reset the UnityClient singleton before and after every test."""
    UnityClient._instance = None
    yield
    UnityClient._instance = None
