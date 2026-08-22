"""RF_Prop_Bridge - the water crossing, in two states.

Brief (asset spec): simple beam and plank construction, and **binary** - intact or
destroyed, no partial-damage state, because a bridge is either crossable or it is
not and a half-damaged one would be a lie about whether you can drive over it.

The abutments and piers survive destruction while the deck does not. That is the
readable version of "you can no longer cross here": the crossing point stays
recognisable on the minimap, but the span is visibly gone rather than merely
scorched.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Prop_Bridge_Intact"
ASSET_DESTROYED = "RF_Prop_Bridge_Destroyed"

_PIER_Y = 4.20
_ABUTMENT_Y = 6.90


def _supports():
    """Build the piers and abutments, which survive both states.

    Returns:
        The support objects, in build order.
    """
    parts = []
    for label, y in (("Near", -_PIER_Y), ("Far", _PIER_Y)):
        parts.append(p.box(f"Pier{label}", size=(1.30, 1.30, 1.00), at=(0.0, y, 0.50),
                           color=palette.CONCRETE))
    for label, y in (("Near", -_ABUTMENT_Y), ("Far", _ABUTMENT_Y)):
        parts.append(p.box(f"Abutment{label}", size=(5.20, 1.40, 0.90), at=(0.0, y, 0.45),
                           color=palette.CONCRETE))
    return parts


def build_intact():
    """Build the crossable bridge.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = _supports() + [
        p.box("Deck", size=(5.00, 13.00, 0.40), at=(0.0, 0.0, 1.00), color=palette.WOOD),
        p.box("RailL", size=(0.18, 13.00, 0.55), at=(2.41, 0.0, 1.475), color=palette.WOOD),
        p.box("RailR", size=(0.18, 13.00, 0.55), at=(-2.41, 0.0, 1.475), color=palette.WOOD),
    ]
    return p.join(ASSET_INTACT, parts)


def build_destroyed():
    """Build the dropped span: stubs at each bank, nothing in between.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = _supports() + [
        # Stubs still attached to each bank, sagging toward the gap.
        p.box("StubNear", size=(5.00, 3.20, 0.38), at=(0.0, -4.75, 0.92),
              rot=(-11.0, 0.0, 0.0), color=palette.CHARRED),
        p.box("StubFar", size=(5.00, 3.00, 0.38), at=(0.0, 4.85, 0.94),
              rot=(9.0, 0.0, 0.0), color=palette.CHARRED),
        p.box("Beam1", size=(0.45, 2.60, 0.30), at=(1.35, -1.75, 0.28),
              rot=(0.0, 12.0, 22.0), color=palette.CHARRED),
        p.box("Beam2", size=(0.40, 2.20, 0.28), at=(-1.55, 1.55, 0.26),
              rot=(0.0, -14.0, -34.0), color=palette.SCORCH),
    ]
    return p.join(ASSET_DESTROYED, parts)


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DESTROYED: build_destroyed,
}
