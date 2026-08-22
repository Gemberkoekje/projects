"""RF_Structure_DepotFuel - the refuelling point, in three destruction states.

Brief (asset spec): small, neutral (contestable by both sides, so never team-tinted),
and **visually distinct from the ammo depot at a glance**. That distinction is the
whole job of this asset, so it is built as a tall vertical cylinder while the ammo
depot is a low horizontal box - the two read apart by silhouette alone, at gameplay
camera distance, with no color cue needed.

The concrete pad survives every state, which is what makes the wreck still read as
"this was the fuel depot" rather than as anonymous rubble.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Structure_DepotFuel_Intact"
ASSET_DAMAGED = "RF_Structure_DepotFuel_Damaged"
ASSET_DESTROYED = "RF_Structure_DepotFuel_Destroyed"

_PAD_SIZE = (3.40, 3.40, 0.28)
_TANK_RADIUS = 1.20


def _pad(color):
    """Build the concrete pad common to all three states.

    Args:
        color: Linear RGBA for the pad.

    Returns:
        The pad object.
    """
    return p.box("Pad", size=_PAD_SIZE, at=(0.0, 0.0, 0.14), color=color)


def build_intact():
    """Build the undamaged fuel tank.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = [
        _pad(palette.CONCRETE),
        p.cylinder("Tank", radius=_TANK_RADIUS, height=2.20, at=(0.0, 0.0, 1.38),
                   color=palette.METAL, segments=12),
        p.cone("Dome", radius_bottom=_TANK_RADIUS, radius_top=0.42, height=0.50,
               at=(0.0, 0.0, 2.73), color=palette.METAL, segments=12),
        # Hazard band, the only saturated color on an otherwise neutral prop.
        p.cylinder("Band", radius=_TANK_RADIUS + 0.04, height=0.32, at=(0.0, 0.0, 1.90),
                   color=palette.WARNING, segments=12),
        p.cylinder("Pipe", radius=0.13, height=1.30, at=(1.05, -1.20, 0.90),
                   color=palette.METAL_DARK, segments=8, axis="Y"),
    ]
    return p.join(ASSET_INTACT, parts)


def build_damaged():
    """Build the tank after a hit: dome torn off, shell scorched, still standing.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DAMAGED)

    parts = [
        _pad(palette.CONCRETE),
        p.cylinder("Tank", radius=_TANK_RADIUS, height=1.90, at=(0.0, 0.0, 1.23),
                   color=palette.METAL, segments=12),
        # What is left of the dome, folded over and burnt.
        p.box("DomeWreck", size=(1.60, 1.30, 0.40), at=(0.22, -0.18, 2.32),
              rot=(6.0, -14.0, 22.0), color=palette.CHARRED),
        p.cylinder("Band", radius=_TANK_RADIUS + 0.04, height=0.32, at=(0.0, 0.0, 1.70),
                   color=palette.CHARRED, segments=12),
        p.box("Debris1", size=(0.80, 0.60, 0.35), at=(1.55, 0.90, 0.45),
              rot=(0.0, 16.0, 28.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_DAMAGED, parts)


def build_destroyed():
    """Build the collapsed tank: a split, flattened shell on a burnt pad.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        _pad(palette.CHARRED),
        # The shell, split open and lying over.
        p.cylinder("Shell", radius=_TANK_RADIUS, height=0.85, at=(-0.15, 0.20, 0.62),
                   rot=(74.0, 0.0, 12.0), color=palette.CHARRED, segments=12),
        p.box("Debris1", size=(1.20, 0.90, 0.40), at=(1.45, -0.85, 0.48),
              rot=(0.0, 18.0, 34.0), color=palette.SCORCH),
        p.box("Debris2", size=(0.90, 1.10, 0.35), at=(-1.50, -1.05, 0.45),
              rot=(12.0, 0.0, -26.0), color=palette.CHARRED),
        p.box("Debris3", size=(0.70, 0.65, 0.55), at=(0.55, 1.55, 0.55),
              rot=(0.0, -10.0, 46.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_DESTROYED, parts)


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
