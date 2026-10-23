"""
Project file tools for the Unity MCP Bridge.

Gives the agent read/write access to files within the Unity project's Assets
folder. Writing a file does NOT import it: call refresh_assets() afterwards to
make Unity import it (and compile C# scripts). This keeps a domain reload from
cutting the conversation off in the middle of a sequence of writes.

  write_project_file  — create or overwrite any file under Assets/
  read_project_file   — read an existing file's text content
  list_project_files  — list files in a directory under Assets/

All paths are relative to Assets/. A leading "Assets/" is accepted and stripped.
Paths containing ".." are rejected to prevent writes outside the project.
"""

from __future__ import annotations

import json
from typing import Optional

from unity_mcp.unity_client import UnityClient, UnityConnectionError, UnityError


async def write_project_file(
    path:                str,
    content:             str,
    overwrite:           bool = True,
    create_directories:  bool = True,
) -> str:
    """
    Write a file to the Unity project's Assets folder. The file is not imported.

    The most common use is creating new C# scripts, but it works for any text
    file: ScriptableObject data, shader graphs exported as JSON, config files,
    README documents, etc.

    Unity does not import the file and does not compile scripts until
    refresh_assets() is called (or the Unity Editor regains focus with Auto
    Refresh on). Write every file first, then call refresh_assets() once.

    Parameters
    ----------
    path               : path relative to Assets/, e.g. "Scripts/Player/PlayerController.cs"
                         A leading "Assets/" is accepted and stripped automatically.
    content            : full text content to write (UTF-8)
    overwrite          : replace the file if it already exists (default True).
                         Set False to protect existing files.
    create_directories : create intermediate directories if they don't exist (default True).
                         Set False to require the directory to exist first.

    Returns
    -------
    { asset_path, created: bool, overwrote: bool, bytes_written: int }

    Workflow — create a new MonoBehaviour
    -------------------------------------
    write_project_file(
        path="Scripts/AI/GuardPatrol.cs",
        content='''using UnityEngine;

public class GuardPatrol : MonoBehaviour
{
    public Transform[] waypoints;

    private void Start()
    {
    }
}'''
    )
    # Then, once all files are written: refresh_assets()

    Notes
    -----
    - Write all files first, send the final response, then call
      refresh_assets() to trigger compilation without killing the conversation.
    - Immediately after writing a script, wait for Unity to finish compiling
      before trying to add the new component to a scene object via add_component_to_object.
    - To update an existing script, call read_project_file first to see the
      current content, then write_project_file with the modified version.
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/project/write_file", {
            "path":               path,
            "content":            content,
            "overwrite":          overwrite,
            "create_directories": create_directories,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def read_project_file(path: str) -> str:
    """
    Read the text content of a file from the Unity project's Assets folder.

    Use this to inspect an existing script before modifying it, to check whether
    a file already exists before writing, or to read config and data files.

    Parameters
    ----------
    path : path relative to Assets/, e.g. "Scripts/Player/PlayerController.cs"
           A leading "Assets/" is accepted and stripped automatically.

    Returns
    -------
    { asset_path, exists: bool, content?: str, size_bytes?: int }

    If the file does not exist, returns { exists: false } without an error —
    use this to safely check for a file before writing it.
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/project/read_file", {"path": path})
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def list_project_files(
    path:      str  = "",
    pattern:   str  = "*",
    recursive: bool = False,
) -> str:
    """
    List files in a directory under the Unity project's Assets folder.

    .meta files are excluded from results automatically.

    Parameters
    ----------
    path      : directory path relative to Assets/ (empty = Assets root)
    pattern   : file glob pattern (default "*"). Examples: "*.cs", "*.prefab", "*.asset"
    recursive : include files in subdirectories (default False)

    Returns
    -------
    { directory, files: [str], count: int }

    All returned paths are in "Assets/..." format.

    Examples
    --------
    list_project_files("Scripts", "*.cs", recursive=True)
    → lists all C# scripts under Assets/Scripts/

    list_project_files("Prefabs/Props", "*.prefab")
    → lists all prefabs directly in Assets/Prefabs/Props/
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/project/list_files", {
            "path":      path,
            "pattern":   pattern,
            "recursive": recursive,
        })
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})


async def refresh_assets() -> str:
    """
    Refresh the Unity AssetDatabase and trigger recompilation of any changed scripts.
    Call it last: the recompile reloads Unity's scripts and can end the conversation.

    Call this AFTER writing all files and AFTER composing the final response text —
    not in the middle of a tool-call sequence. This avoids a domain reload killing
    the in-flight conversation.

    Correct workflow for creating a C# script:
        1. write_project_file(path="Scripts/Foo.cs", content="...")
        2. Compose and send your final summary response to the user
        3. [separately, if asked] call refresh_assets()

    Or, if the user needs the script compiled as part of the same turn:
        1. write_project_file(...)
        2. refresh_assets()        ← this ends the agent turn (domain reload follows)
        3. (tell the user Unity is compiling)

    Returns
    -------
    { refreshed: true, message: str }
    """
    client = UnityClient.instance()
    try:
        result = await client.post("/tools/project/refresh_assets", {})
        return json.dumps(result)
    except (UnityConnectionError, UnityError) as e:
        return json.dumps({"error": str(e)})
