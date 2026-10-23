"""
Pydantic models for Unity MCP Bridge request and response payloads.

Each phase adds new models here as new tools are implemented.
Keeping them in one module makes it easy to share types between tools
and to generate documentation from schemas.
"""

from __future__ import annotations
from pydantic import BaseModel


class StatusData(BaseModel):
    """Response payload for GET /status."""
    unity_version: str
    scene_name: str
    scene_path: str
    is_playing: bool
