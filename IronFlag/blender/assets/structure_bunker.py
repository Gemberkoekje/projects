"""RF_Structure_Bunker - the home base, one per team.

Brief (asset spec): houses the vehicle-select lift for ground vehicles plus a
separate helipad, ~10m x 10m footprint, team-color trim on entrance and roof
accents, and **no destruction states** - it is the win-condition target, not a
combat prop.

Both spawn points from the design doc's core loop are modelled, because the lift
and the pad are meant to be a visible pacing beat rather than a menu: the lift bay
opens at the front for ground vehicles, and the helipad sits on the roof.

``LiftPlatform`` is a **collar around a hole**, not a slab. It used to be the deck
a vehicle stood on; the deck is now the lift car in ``RF_Structure_BunkerLift``,
which rises through this collar out of ``RF_Structure_BunkerHall`` below and stops
flush with its top. The origin has not moved, so nothing that deploys from it
noticed.

Each of those two is left as its own named child object with its origin on the
surface a vehicle stands on, rather than joined into the hull. Unity deploys from
those origins - see ``TeamBunker`` - for the same reason it fires rounds from the
``Muzzle`` object rather than from a number in a script: moving the lift is then an
art change and nothing else.

Neutral concrete everywhere; team identity comes entirely from the ``TeamTrim``
child, which bands the roof edge and frames the entrance.
"""

from rf import palette
from rf import primitives as p
from rf import scene
from rf import material

ASSET_NAME = "RF_Structure_Bunker"

_ROOF_HALF_X = 4.80
_ROOF_FRONT_Y = -4.00
_ROOF_REAR_Y = 5.20

#: Names Unity looks these two up by. They must match TeamBunker.LiftNodeName and
#: TeamBunker.HelipadNodeName; renaming one without the other leaves the bunker
#: deploying from a fallback guess at where its own door is.
_LIFT_NAME = "LiftPlatform"
_HELIPAD_NAME = "Helipad"

#: The shaft mouth. Shared with structure_bunker_hall.py, which puts the shaft
#: under it, and with the lift car, which stops flush with the collar's top. The
#: hole is 200 mm tighter than the shaft, and shallower across Y than across X
#: because the block's front wall is only 1.8 m beyond the shaft's centre.
_SHAFT_Y = -5.20
_HOLE_HALF_X = 1.90
_HOLE_HALF_Y = 1.60
_COLLAR_WIDTH = 0.60
_COLLAR_TOP = 0.25


def _collar():
    """Build the rim around the shaft mouth.

    Returns:
        Boxes framing the hole, their tops level with ``_COLLAR_TOP``.

    A rim rather than a deck: what a vehicle stands on is the lift car, and a slab
    across the hole would be a lid on the shaft it rises through.
    """
    reach = _HOLE_HALF_X + _COLLAR_WIDTH
    parts = []

    for sign in (-1.0, 1.0):
        parts.append(p.box(
            "CollarSide", size=(_COLLAR_WIDTH, _HOLE_HALF_Y * 2.0, _COLLAR_TOP),
            at=(sign * (_HOLE_HALF_X + (_COLLAR_WIDTH * 0.5)), _SHAFT_Y, _COLLAR_TOP * 0.5),
            color=palette.METAL_DARK))
        parts.append(p.box(
            "CollarEnd", size=(reach * 2.0, _COLLAR_WIDTH, _COLLAR_TOP),
            at=(0.0, _SHAFT_Y + (sign * (_HOLE_HALF_Y + (_COLLAR_WIDTH * 0.5))),
                _COLLAR_TOP * 0.5),
            color=palette.METAL_DARK))
        # Hazard paint sunk flush into the rim, on the two edges a vehicle crosses.
        parts.append(p.box(
            "CollarHazard", size=(reach * 2.0, 0.24, 0.06),
            at=(0.0, _SHAFT_Y + (sign * (_HOLE_HALF_Y + (_COLLAR_WIDTH * 0.5))),
                _COLLAR_TOP - 0.03),
            color=palette.WARNING))

    return parts


def build():
    """Build the bunker and return its root object.

    Returns:
        The joined structure, with the team trim parented to it.
    """
    scene.begin(ASSET_NAME)

    parts = [
        p.box("Block", size=(8.50, 8.00, 3.20), at=(0.0, 0.60, 1.60), color=palette.CONCRETE),
        p.box("Roof", size=(9.60, 9.20, 0.50), at=(0.0, 0.60, 3.45), color=palette.CONCRETE),
        # Lift bay mouth: the dark recess ground vehicles rise out of.
        p.box("LiftBay", size=(3.40, 1.20, 2.30), at=(0.0, -3.45, 1.15),
              color=palette.HULL_DARK),
    ]
    root = p.join(ASSET_NAME, parts)

    # The two deploy points stay separate objects with their origins on the surface a
    # vehicle stands on, because Unity reads those origins as the places vehicles come
    # out of. Joining either into the hull would hide it from the importer.
    lift = p.join(_LIFT_NAME, _collar())
    p.set_pivot(lift, (0.0, _SHAFT_Y, _COLLAR_TOP))
    p.attach(lift, root)

    pad = p.join(_HELIPAD_NAME, [
        p.cylinder("Deck", radius=2.60, height=0.22, at=(2.20, 1.80, 3.81),
                   color=palette.SAND, segments=12),
        p.cylinder("Mark", radius=1.30, height=0.06, at=(2.20, 1.80, 3.95),
                   color=palette.LIGHT, segments=12),
    ])
    p.set_pivot(pad, (2.20, 1.80, 3.98))
    p.attach(pad, root)

    trim_parts = [
        p.box("BandFront", size=(9.70, 0.30, 0.24), at=(0.0, _ROOF_FRONT_Y, 3.58),
              group=material.TEAM),
        p.box("BandRear", size=(9.70, 0.30, 0.24), at=(0.0, _ROOF_REAR_Y, 3.58),
              group=material.TEAM),
        p.box("BandL", size=(0.30, 9.30, 0.24), at=(_ROOF_HALF_X, 0.60, 3.58), group=material.TEAM),
        p.box("BandR", size=(0.30, 9.30, 0.24), at=(-_ROOF_HALF_X, 0.60, 3.58),
              group=material.TEAM),
        p.box("Lintel", size=(3.60, 0.30, 0.35), at=(0.0, -3.45, 2.45), group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, trim_parts)

    return root


BUILDERS = {ASSET_NAME: build}
