"""Validation for the ``RF_<Category>_<Name>_<State>`` naming rule.

The build refuses to export a misnamed asset rather than letting a typo reach
Unity, where the mismatch only shows up as a broken prefab reference much later.
"""

import re

CATEGORIES = ("Vehicle", "Structure", "Prop")
STATES = ("Intact", "Damaged", "Destroyed")

_PATTERN = re.compile(r"^RF_(?P<category>[A-Za-z]+)_(?P<name>[A-Za-z0-9]+)(?:_(?P<state>[A-Za-z]+))?$")


def validate(asset_name):
    """Check an asset name against the spec's naming rule.

    Args:
        asset_name: Full asset name, e.g. ``"RF_Structure_FlagTower_Damaged"``.

    Returns:
        An empty string when the name is valid, otherwise the reason it is not.
    """
    match = _PATTERN.match(asset_name)
    if match is None:
        return f"'{asset_name}' does not match RF_<Category>_<Name>[_<State>]"

    category = match.group("category")
    if category not in CATEGORIES:
        return f"'{asset_name}' has category '{category}'; expected one of {', '.join(CATEGORIES)}"

    state = match.group("state")
    if state is not None and state not in STATES:
        return f"'{asset_name}' has state '{state}'; expected one of {', '.join(STATES)}"

    return ""
