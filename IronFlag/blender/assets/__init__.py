"""Per-asset build scripts, one module per asset family.

Each module exposes a ``BUILDERS`` dict mapping full asset name to a zero-argument
function that builds the asset in a fresh scene and returns its root object::

    BUILDERS = {
        "RF_Structure_FlagTower_Intact": build_intact,
        "RF_Structure_FlagTower_Damaged": build_damaged,
        "RF_Structure_FlagTower_Destroyed": build_destroyed,
    }

``build.py`` discovers every module in this package automatically, so a new asset
is a new file here and nothing else. Start from ``vehicle_jeep.py``.
"""
