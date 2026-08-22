"""RF_Structure_DepotAmmo - the rearming point, in three destruction states.

Brief (asset spec): small, neutral (contestable, never team-tinted), and **visually
distinct from the fuel depot at a glance**. Where the fuel depot is a tall vertical
cylinder, this is a low horizontal stack of crates: the pair reads apart by
silhouette alone at gameplay camera distance, without relying on color.

The concrete pad survives every state so the wreck still reads as "this was the ammo
depot", and the destroyed state is splintered planks rather than a crumpled shell -
the two depots stay distinguishable even after they are gone.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Structure_DepotAmmo_Intact"
ASSET_DAMAGED = "RF_Structure_DepotAmmo_Damaged"
ASSET_DESTROYED = "RF_Structure_DepotAmmo_Destroyed"

_PAD_SIZE = (3.40, 3.40, 0.28)


def _pad(color):
    """Build the concrete pad common to all three states.

    Args:
        color: Linear RGBA for the pad.

    Returns:
        The pad object.
    """
    return p.box("Pad", size=_PAD_SIZE, at=(0.0, 0.0, 0.14), color=color)


def build_intact():
    """Build the undamaged crate stack.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = [
        _pad(palette.CONCRETE),
        p.box("Crate", size=(2.60, 2.20, 1.55), at=(0.0, 0.0, 1.055), color=palette.WOOD),
        p.box("Lid", size=(2.72, 2.32, 0.18), at=(0.0, 0.0, 1.92), color=palette.METAL_DARK),
        p.box("CrateSmall", size=(1.40, 1.20, 0.85), at=(0.45, 0.35, 2.435),
              color=palette.WOOD),
        # Hazard stripe, matching the fuel depot's band so the pair reads as a set.
        p.box("Stripe", size=(2.62, 0.12, 0.40), at=(0.0, -1.11, 1.05),
              color=palette.WARNING),
    ]
    return p.join(ASSET_INTACT, parts)


def build_damaged():
    """Build the stack after a hit: lid blown off, top crate burst open.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DAMAGED)

    parts = [
        _pad(palette.CONCRETE),
        p.box("Crate", size=(2.60, 2.20, 1.55), at=(0.0, 0.0, 1.055), color=palette.WOOD),
        p.box("Lid", size=(2.72, 2.32, 0.18), at=(0.30, -0.22, 1.94),
              rot=(5.0, -9.0, 14.0), color=palette.CHARRED),
        p.box("CrateBurst", size=(1.20, 1.00, 0.55), at=(-0.55, 0.45, 2.30),
              rot=(0.0, 17.0, -24.0), color=palette.CHARRED),
        p.box("Spill", size=(0.75, 0.60, 0.35), at=(1.55, -1.15, 0.46),
              rot=(0.0, 12.0, 32.0), color=palette.CHARRED),
        p.box("Stripe", size=(2.62, 0.12, 0.40), at=(0.0, -1.11, 1.05),
              color=palette.CHARRED),
    ]
    return p.join(ASSET_DAMAGED, parts)


def build_destroyed():
    """Build the collapsed stack: splintered planks across a burnt pad.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        _pad(palette.CHARRED),
        p.box("Plank1", size=(2.40, 0.55, 0.22), at=(-0.20, -0.35, 0.42),
              rot=(0.0, 6.0, 18.0), color=palette.CHARRED),
        p.box("Plank2", size=(2.10, 0.50, 0.20), at=(0.35, 0.55, 0.44),
              rot=(0.0, -9.0, -32.0), color=palette.SCORCH),
        p.box("Plank3", size=(1.60, 0.45, 0.20), at=(0.95, -0.95, 0.40),
              rot=(14.0, 0.0, 58.0), color=palette.CHARRED),
        p.box("Heap", size=(1.30, 1.10, 0.50), at=(-0.75, 0.85, 0.52),
              rot=(0.0, 8.0, -12.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_DESTROYED, parts)


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
