"""RF_Prop_BuildingA / RF_Prop_BuildingB - reusable cover, three states each.

Brief (asset spec): one or two simple building shapes, reused across the map for
terrain variety and destruction spectacle. Two are built here because a single
repeated shape reads as tiling rather than as a place.

A is low and wide with a flat roof; B is tall and narrow with a gable. Neither is
allowed to be pyramidal - the flag tower owns that silhouette, and the whole decoy
mechanic depends on a pyramid meaning "tower" and nothing else.

Neutral colors only: these are map furniture, contested by both sides.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_A_INTACT = "RF_Prop_BuildingA_Intact"
ASSET_A_DAMAGED = "RF_Prop_BuildingA_Damaged"
ASSET_A_DESTROYED = "RF_Prop_BuildingA_Destroyed"

ASSET_B_INTACT = "RF_Prop_BuildingB_Intact"
ASSET_B_DAMAGED = "RF_Prop_BuildingB_Damaged"
ASSET_B_DESTROYED = "RF_Prop_BuildingB_Destroyed"


def build_a_intact():
    """Build the low wide warehouse, undamaged.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_A_INTACT)

    parts = [
        p.box("Walls", size=(6.00, 4.60, 3.00), at=(0.0, 0.0, 1.50), color=palette.CONCRETE),
        p.box("Roof", size=(6.40, 5.00, 0.35), at=(0.0, 0.0, 3.175), color=palette.SAND),
        p.box("Door", size=(1.40, 0.16, 2.00), at=(0.0, -2.34, 1.00),
              color=palette.HULL_DARK),
        p.box("WindowL", size=(1.00, 0.14, 0.80), at=(1.95, -2.33, 2.05), color=palette.GLASS),
        p.box("WindowR", size=(1.00, 0.14, 0.80), at=(-1.95, -2.33, 2.05), color=palette.GLASS),
    ]
    return p.join(ASSET_A_INTACT, parts)


def build_a_damaged():
    """Build the warehouse with its roof caved in on one side.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_A_DAMAGED)

    parts = [
        p.box("Walls", size=(6.00, 4.60, 3.00), at=(0.0, 0.0, 1.50), color=palette.CONCRETE),
        # Half the roof still spans; the other half has dropped inside.
        p.box("RoofIntact", size=(3.40, 5.00, 0.35), at=(-1.50, 0.0, 3.175),
              color=palette.SAND),
        p.box("RoofFallen", size=(2.60, 3.80, 0.30), at=(1.55, 0.25, 2.55),
              rot=(7.0, -16.0, 9.0), color=palette.CHARRED),
        p.box("Breach", size=(1.30, 0.20, 1.70), at=(1.90, -2.32, 0.85),
              color=palette.CHARRED),
        p.box("Door", size=(1.40, 0.16, 2.00), at=(0.0, -2.34, 1.00),
              color=palette.HULL_DARK),
        p.box("Rubble", size=(1.10, 0.90, 0.45), at=(2.35, -3.00, 0.42),
              rot=(0.0, 14.0, 26.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_A_DAMAGED, parts)


def build_a_destroyed():
    """Build the flattened warehouse: stub walls and a rubble field.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_A_DESTROYED)

    parts = [
        p.box("StubWalls", size=(6.00, 4.60, 0.75), at=(0.0, 0.0, 0.375),
              color=palette.CHARRED),
        p.box("Slab", size=(3.20, 2.40, 0.35), at=(-0.85, 0.45, 0.85),
              rot=(6.0, -13.0, 8.0), color=palette.CHARRED),
        p.box("Rubble1", size=(1.60, 1.30, 0.60), at=(1.65, -0.75, 0.68),
              rot=(0.0, 11.0, 24.0), color=palette.SCORCH),
        p.box("Rubble2", size=(1.20, 1.50, 0.55), at=(-2.05, -1.35, 0.62),
              rot=(9.0, 0.0, -28.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_A_DESTROYED, parts)


def build_b_intact():
    """Build the tall narrow house, undamaged.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_B_INTACT)

    parts = [
        p.box("Walls", size=(4.00, 4.00, 4.20), at=(0.0, 0.0, 2.10), color=palette.SAND),
        # Gable, not a pyramid: two wedges meeting at a ridge along X.
        p.wedge("RoofFront", size=(4.50, 2.25, 1.30), at=(0.0, -1.125, 4.85),
                rot=(0.0, 0.0, 180.0), color=palette.CONCRETE),
        p.wedge("RoofRear", size=(4.50, 2.25, 1.30), at=(0.0, 1.125, 4.85),
                color=palette.CONCRETE),
        p.box("Door", size=(1.20, 0.16, 2.10), at=(0.0, -2.04, 1.05),
              color=palette.HULL_DARK),
        p.box("Window", size=(0.90, 0.14, 0.90), at=(0.0, -2.03, 3.10), color=palette.GLASS),
    ]
    return p.join(ASSET_B_INTACT, parts)


def build_b_damaged():
    """Build the house with the gable half torn away.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_B_DAMAGED)

    parts = [
        p.box("Walls", size=(4.00, 4.00, 4.20), at=(0.0, 0.0, 2.10), color=palette.SAND),
        p.wedge("RoofRear", size=(4.50, 2.25, 1.30), at=(0.0, 1.125, 4.85),
                color=palette.CONCRETE),
        p.box("RoofStub", size=(4.20, 1.10, 0.35), at=(0.10, -1.35, 4.35),
              rot=(-14.0, 0.0, 6.0), color=palette.CHARRED),
        p.box("Breach", size=(1.50, 0.22, 1.60), at=(-1.05, -2.02, 2.60),
              color=palette.CHARRED),
        p.box("Door", size=(1.20, 0.16, 2.10), at=(0.0, -2.04, 1.05),
              color=palette.HULL_DARK),
        p.box("Rubble", size=(1.20, 1.00, 0.50), at=(-1.85, -2.65, 0.46),
              rot=(0.0, 15.0, -22.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_B_DAMAGED, parts)


def build_b_destroyed():
    """Build the collapsed house: a broken wall stub and scattered masonry.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_B_DESTROYED)

    parts = [
        p.box("StubWalls", size=(4.00, 4.00, 0.90), at=(0.0, 0.0, 0.45),
              color=palette.CHARRED),
        # One corner still standing, which is what keeps it reading as the tall building.
        p.box("Corner", size=(1.40, 1.30, 2.30), at=(-1.20, 1.25, 1.15),
              color=palette.CHARRED),
        p.box("Rubble1", size=(1.70, 1.40, 0.60), at=(1.05, -0.85, 0.72),
              rot=(0.0, 12.0, 30.0), color=palette.SCORCH),
        p.box("Rubble2", size=(1.30, 1.20, 0.55), at=(0.45, 1.55, 0.68),
              rot=(11.0, 0.0, -18.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_B_DESTROYED, parts)


BUILDERS = {
    ASSET_A_INTACT: build_a_intact,
    ASSET_A_DAMAGED: build_a_damaged,
    ASSET_A_DESTROYED: build_a_destroyed,
    ASSET_B_INTACT: build_b_intact,
    ASSET_B_DAMAGED: build_b_damaged,
    ASSET_B_DESTROYED: build_b_destroyed,
}
