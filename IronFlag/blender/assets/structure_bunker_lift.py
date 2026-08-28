"""RF_Structure_BunkerLift - the car that carries a vehicle up out of the base.

Its own asset rather than part of ``RF_Structure_BunkerHall`` for one reason: it
moves and the hall does not. Unity drives it up and down the shaft, and at its top
stop its deck is flush with the ``LiftPlatform`` collar on the blockhouse above.

The **origin is the middle of the deck's top face**, so parking the car at a point
puts the surface a vehicle stands on exactly there - the same convention the
bunker's two deploy markers and the hall's four bay plates already use. Nothing on
this asset stands proud of that surface except the four guide shoes, which are
shorter than the ground clearance of everything that rides it.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_NAME = "RF_Structure_BunkerLift"

_DECK_X = 3.60          # the shaft is 4.0 m across, so this clears it by 200 mm
_DECK_Y = 3.00          # and 3.4 m deep, because the blockhouse wall is close
_DECK_THICK = 0.20
_RAIL_X = 1.80          # the hall's guide rails, which the shoes wrap
_RAIL_Y = 1.45
_SHOE = 0.34
_POST = 0.28


def build():
    """Build the lift car and return its root object.

    Returns:
        The joined car, origin on the middle of its deck.
    """
    scene.begin(ASSET_NAME)

    parts = [
        p.box("Deck", size=(_DECK_X, _DECK_Y, _DECK_THICK), at=(0.0, 0.0, -_DECK_THICK * 0.5),
              color=palette.METAL_DARK),
        p.box("Frame", size=(_DECK_X - 0.50, _DECK_Y - 0.50, 0.34),
              at=(0.0, 0.0, -_DECK_THICK - 0.17), color=palette.HULL_DARK),
        p.cylinder("Ram", radius=0.22, height=0.90, at=(0.0, 0.0, -0.85),
                   color=palette.METAL, segments=10),
    ]

    # Hazard paint on the two edges a vehicle drives over, sunk flush into the deck
    # so nothing rides up on it.
    for sign in (-1.0, 1.0):
        parts.append(p.box(
            "Hazard", size=(_DECK_X, 0.34, 0.06), at=(0.0, sign * (_DECK_Y - 0.34) * 0.5, -0.03),
            color=palette.WARNING))

    for x_sign in (-1.0, 1.0):
        parts.append(p.box(
            "Shoe", size=(_SHOE, _SHOE, 0.44), at=(x_sign * _RAIL_X, _RAIL_Y, -0.42),
            color=palette.METAL))

        for y_sign in (-1.0, 1.0):
            parts.append(p.box(
                "Post", size=(0.16, 0.16, _POST),
                at=(x_sign * (_DECK_X - 0.16) * 0.5, y_sign * (_DECK_Y - 0.16) * 0.5,
                    _POST * 0.5),
                color=palette.METAL_DARK))

    return p.join(ASSET_NAME, parts)


BUILDERS = {ASSET_NAME: build}
