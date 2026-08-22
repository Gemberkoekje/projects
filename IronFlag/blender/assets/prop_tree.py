"""RF_Prop_Tree - vegetation cover, in three states.

Brief (asset spec): one tree/vegetation prop, reused across the map for terrain
variety and line-of-sight breaks. Neutral map furniture, never team-tinted.

Kept to three primitives because this is the prop that will be placed in the largest
numbers, and a stacked-cone canopy reads as vegetation from the gameplay camera
without costing anything. The destroyed state keeps the stump on the same footprint,
so a cleared firing line still shows where the cover used to be.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Prop_Tree_Intact"
ASSET_DAMAGED = "RF_Prop_Tree_Damaged"
ASSET_DESTROYED = "RF_Prop_Tree_Destroyed"


def build_intact():
    """Build the healthy tree.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = [
        p.cylinder("Trunk", radius=0.24, height=2.40, at=(0.0, 0.0, 1.20),
                   color=palette.WOOD, segments=8),
        p.cone("CanopyLower", radius_bottom=1.70, radius_top=0.90, height=1.40,
               at=(0.0, 0.0, 2.90), color=palette.FOLIAGE, segments=8),
        p.cone("CanopyUpper", radius_bottom=0.95, radius_top=0.0, height=1.30,
               at=(0.0, 0.0, 4.05), color=palette.FOLIAGE, segments=8),
    ]
    return p.join(ASSET_INTACT, parts)


def build_damaged():
    """Build the tree with its crown blown off and the trunk scorched.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DAMAGED)

    parts = [
        p.cylinder("Trunk", radius=0.24, height=2.40, at=(0.0, 0.0, 1.20),
                   color=palette.WOOD, segments=8),
        p.cone("CanopyLower", radius_bottom=1.35, radius_top=0.80, height=1.00,
               at=(0.05, -0.05, 2.70), color=palette.FOLIAGE, segments=8),
        p.cone("CanopyBurnt", radius_bottom=0.80, radius_top=0.30, height=0.60,
               at=(0.12, -0.10, 3.45), color=palette.CHARRED, segments=8),
    ]
    return p.join(ASSET_DAMAGED, parts)


def build_destroyed():
    """Build the felled tree: a stump plus the trunk down beside it.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        p.cylinder("Stump", radius=0.26, height=0.70, at=(0.0, 0.0, 0.35),
                   color=palette.CHARRED, segments=8),
        p.cylinder("FallenTrunk", radius=0.22, height=2.60, at=(0.85, 0.95, 0.24),
                   rot=(76.0, 0.0, 34.0), color=palette.CHARRED, segments=8),
        p.box("Debris", size=(0.90, 0.75, 0.30), at=(-0.95, -0.65, 0.28),
              rot=(0.0, 10.0, -26.0), color=palette.SCORCH),
    ]
    return p.join(ASSET_DESTROYED, parts)


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
