"""
Run the repository's consistency checks as part of the normal test run, so a
mismatch fails pytest instead of waiting for someone to run the scripts:

  - scripts/check_contract.py: every Python call and every catalog tool
    (built-in and extension) uses a route the bridge has, with argument
    names the C# request types read
  - scripts/export_tool_catalog.py --check: the built-in tool catalog in
    the Unity package matches the Python docstrings and schemas
"""

import sys
from pathlib import Path

import pytest

SCRIPTS = Path(__file__).resolve().parent.parent / "scripts"
sys.path.insert(0, str(SCRIPTS))

import check_contract  # noqa: E402
import export_tool_catalog  # noqa: E402


def test_python_tools_catalog_and_bridge_agree(monkeypatch, capsys):
    monkeypatch.setattr(sys, "argv", ["check_contract.py"])
    result = check_contract.main()
    report = capsys.readouterr().out
    assert result == 0, report


def test_builtin_tool_catalog_is_up_to_date():
    assert export_tool_catalog.is_up_to_date(), (
        "unity-package/Editor/Catalog/builtin-tools.json is out of date: "
        "run scripts/export_tool_catalog.py from server/"
    )
