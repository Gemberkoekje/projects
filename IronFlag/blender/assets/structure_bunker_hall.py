"""RF_Structure_BunkerHall - the underground half of a team's base.

The blockhouse above ground is where a vehicle *comes out*; this is where the
other three are waiting. Two decks of two bays around a central lift shaft, cut
away on the field-facing side so a camera in front of the bunker looks straight
into it - which is the whole trick, and it is a modelling convention rather than a
shader: **the front wall is simply not built**, so this asset only ever reads from
one side.

Nothing here is reachable. It hangs under the blockhouse, it is given no colliders
in Unity, and no round in this game resolves below ``y = 0``. It is scenery for one
camera.

Two numbers here are not free:

* The shaft is centred on ``LiftPlatform`` - 5.2 m out of the bunker door - because
  the lift car rises through it and stops flush with that collar. Move one and the
  car surfaces through solid ground.
* The whole hall hangs **below the sea slab**. A level's sea is a box as wide as
  the map, top at the water level and ``SeaThickness`` deep, and it is drawn under
  the island as well as around it, so geometry reaching up into it gets a sheet of
  water drawn across the middle of the picture. See ``_ROOF_TOP``.

Four child objects ``Bay0``..``Bay3`` carry the deck plates a vehicle parks on,
with their origins on the plate the way ``LiftPlatform`` and ``Helipad`` do, so
where a vehicle waits stays an art decision. Each carries a ``Lamp`` child, which
is the only light an underground room gets - Unity gives those the emissive bay
material and hangs a point light off each one.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_NAME = "RF_Structure_BunkerHall"

#: Name prefix Unity looks the bay decks up by, plus the roster index. It must match
#: ``TeamBunker.BayNodePrefix``; renaming one leaves that vehicle parked at the
#: bunker's origin instead of in a bay.
BAY_PREFIX = "Bay"

#: Name of the cap along the top of the cutaway face. Unity frames the select
#: camera so the top of the picture lands on it, which is what keeps the sky - and
#: the underside of the sea slab - out of a shot of an underground room. It must
#: match ``TeamBunker.SkylineNodeName``.
SKYLINE_NAME = "Skyline"

#: Name prefix of the lamp inside a bay, plus the same roster index. Unity gives it
#: the emissive bay material and hangs a point light off it. Numbered rather than
#: four objects called "Lamp" because Blender renames a duplicate name to "Lamp.001"
#: and Unity would then be looking one up that no longer exists.
LAMP_PREFIX = "Lamp"

# -- The shaft, which is pinned to the bunker's lift platform -------------------
_SHAFT_Y = -5.20        # LiftPlatform's Y in structure_bunker.py
_SHAFT_HALF_X = 2.00    # 4.0 m across, so a 3.6 m car has 200 mm either side
_SHAFT_HALF_Y = 1.70    # shallower: the blockhouse's front wall is 1.8 m beyond it
_RAIL_X = 1.80          # the guide rails, wrapped by the lift car's shoes
_RAIL_Y = 1.45

# -- Vertical stack ------------------------------------------------------------
# The roof is at -4.20 rather than just under the ground because the shipped maps
# put the sea slab's underside at -3.70 (water level -0.70, slab 3.0 m thick). Half
# a metre of margin is the whole reason this base is not one storey shallower.
_ROOF_TOP = -4.20
_ROOF_SLAB = 0.80
_CLEAR = 4.20           # head height in a bay: the helicopter is 2.8 m over its skids
_DECK_SLAB = 0.50
_BASE_SLAB = 0.60
_SURFACE = -0.60        # top of the chimney: the blockhouse collar is the rest of it

_UPPER_CEILING = _ROOF_TOP - _ROOF_SLAB                 # -5.00
_UPPER_FLOOR = _UPPER_CEILING - _CLEAR                  # -8.40
_LOWER_CEILING = _UPPER_FLOOR - _DECK_SLAB              # -8.90
_LOWER_FLOOR = _LOWER_CEILING - _CLEAR                  # -12.30
_HALL_BOTTOM = _LOWER_FLOOR - _BASE_SLAB                # -12.90

# -- Horizontal stack ----------------------------------------------------------
_BAY_INNER = 2.60       # the pier between the shaft and a bay ends here
_BAY_OUTER = 9.60
_BAY_CENTRE = (_BAY_INNER + _BAY_OUTER) * 0.5           # 6.40
_BAY_WIDTH = _BAY_OUTER - _BAY_INNER                    # 7.00, and the tank is 6.0 long
_HALL_HALF_X = _BAY_OUTER + 0.60

# -- Depth: the cutaway plane, and how far back a bay goes ---------------------
_FACE_Y = _SHAFT_Y - _SHAFT_HALF_Y                      # -6.90, the cutaway plane
_BACK_Y = -2.40
_BACK_WALL = 0.60
_BAY_DEPTH = _BACK_Y - _FACE_Y                          # 4.50
_HALL_DEPTH = _BAY_DEPTH + _BACK_WALL
_DEPTH_CENTRE = (_FACE_Y + _BACK_Y) * 0.5               # -4.65
_SHELL_CENTRE = _DEPTH_CENTRE + (_BACK_WALL * 0.5)

# -- The facade, which is what stops the frame being full of sky ---------------
# Deliberately far wider and taller than the hall. The select camera frames the
# hall itself; everything past its edges has to be *something*, and rock is the
# only honest answer in a world that models no earth.
_FACE_HALF_X = 30.0
_FACE_BOTTOM = -24.0
_FACE_THICK = 0.60

_DECK_PLATE = 0.12      # the plate a vehicle actually stands on

#: Where each roster slot waits, as (x centre, deck height). Two columns read
#: bottom-up - jeep and tank on the left of the picture, ASV and helicopter on the
#: right - so the two that weigh the most or leave from the roof are the two
#: upstairs, which is how the game this is a homage to arranged the same four.
#:
#: **Blender -X is the left of the picture.** Two flips get you there and they do
#: not cancel: glTFast negates X on import, so Blender +X is Unity -X; and the
#: select camera looks back along the bunker's own heading, so its right-hand
#: vector is the bunker's -X. Get it backwards and the roster reads right to left
#: against a console strip that reads left to right.
_BAY_PLACES = (
    (-_BAY_CENTRE, _LOWER_FLOOR),   # 0: jeep
    (-_BAY_CENTRE, _UPPER_FLOOR),   # 1: tank
    (_BAY_CENTRE, _LOWER_FLOOR),    # 2: ASV
    (_BAY_CENTRE, _UPPER_FLOOR),    # 3: helicopter
)

#: Every bay's clear height and width, published for the Unity side to frame with.
BAY_SIZE = (_BAY_WIDTH, _BAY_DEPTH, _CLEAR)


def _face(name, low, high, x_min, x_max, structural=True):
    """Build one box of the cutaway face, spanning a rectangle of that plane.

    Args:
        name: Object name.
        low: Bottom of the rectangle, in metres.
        high: Top of the rectangle, in metres.
        x_min: Left edge, in metres.
        x_max: Right edge, in metres.
        structural: Whether this piece frames an opening (concrete) or is the rock
            the base is cut into (dark). The distinction is the whole reason the
            picture reads as a base underground rather than as a warehouse.

    Returns:
        The created box.
    """
    return p.box(
        name,
        size=(x_max - x_min, _FACE_THICK, high - low),
        at=((x_min + x_max) * 0.5, _FACE_Y + (_FACE_THICK * 0.5), (low + high) * 0.5),
        color=palette.CONCRETE if structural else palette.HULL_DARK)


def _bay(slot, x_centre, floor_top):
    """Build one bay's deck plate, with its lamp parented to it.

    Args:
        slot: Roster index, which becomes the object name.
        x_centre: Middle of the bay, in metres.
        floor_top: Height of the deck slab this plate sits on.

    Returns:
        The deck plate, origin on the surface a vehicle stands on - exactly as
        ``LiftPlatform`` and ``Helipad`` do it, because Unity parks vehicles there.
    """
    deck = p.join(f"{BAY_PREFIX}{slot}", [
        p.box(
            f"{BAY_PREFIX}{slot}", size=(_BAY_WIDTH - 0.40, _BAY_DEPTH - 0.40, _DECK_PLATE),
            at=(x_centre, _DEPTH_CENTRE, floor_top + (_DECK_PLATE * 0.5)),
            color=palette.METAL_DARK),
    ])
    p.set_pivot(deck, (x_centre, _DEPTH_CENTRE, floor_top + _DECK_PLATE))

    lamp = p.join(f"{LAMP_PREFIX}{slot}", [
        p.box(
            f"{LAMP_PREFIX}{slot}", size=(_BAY_WIDTH - 3.40, 0.36, 0.16),
            at=(x_centre, _DEPTH_CENTRE, floor_top + _CLEAR - 0.08),
            color=palette.LIGHT),
    ])
    p.attach(lamp, deck)
    return deck


def build():
    """Build the underground hall and return its root object.

    Returns:
        The joined hall, with the four bay decks parented to it.
    """
    scene.begin(ASSET_NAME)

    parts = [
        # The cutaway face, with five voids in it: four bay mouths, and the shaft
        # running from the bottom deck clear out of the top of the frame.
        _face("FaceLeft", _FACE_BOTTOM, _ROOF_TOP, -_FACE_HALF_X, -_BAY_OUTER,
              structural=False),
        _face("FaceRight", _FACE_BOTTOM, _ROOF_TOP, _BAY_OUTER, _FACE_HALF_X,
              structural=False),
        _face("FaceSill", _FACE_BOTTOM, _LOWER_FLOOR, -_BAY_OUTER, _BAY_OUTER,
              structural=False),

        # The shell behind it.
        p.box("BackWall",
              size=(_HALL_HALF_X * 2.0, _BACK_WALL, _ROOF_TOP - _HALL_BOTTOM),
              at=(0.0, _BACK_Y + (_BACK_WALL * 0.5), (_ROOF_TOP + _HALL_BOTTOM) * 0.5),
              color=palette.HULL_DARK),
        p.box("BaseSlab",
              size=(_HALL_HALF_X * 2.0, _HALL_DEPTH, _BASE_SLAB),
              at=(0.0, _SHELL_CENTRE, _LOWER_FLOOR - (_BASE_SLAB * 0.5)),
              color=palette.CONCRETE),
        p.box("ChimneyBack",
              size=(_BAY_INNER * 2.0, _BACK_WALL, _SURFACE - _ROOF_TOP),
              at=(0.0, _SHAFT_Y + _SHAFT_HALF_Y + (_BACK_WALL * 0.5),
                  (_ROOF_TOP + _SURFACE) * 0.5),
              color=palette.HULL_DARK),
    ]

    for side, sign in (("L", -1.0), ("R", 1.0)):
        inner = min(sign * _BAY_INNER, sign * _BAY_OUTER)
        outer = max(sign * _BAY_INNER, sign * _BAY_OUTER)
        parts.append(_face(f"FaceLintel{side}", _UPPER_CEILING, _ROOF_TOP, inner, outer))
        parts.append(_face(f"FaceBand{side}", _LOWER_CEILING, _UPPER_FLOOR, inner, outer))

        parts.append(p.box(
            f"SideWall{side}", size=(0.60, _HALL_DEPTH, _ROOF_TOP - _HALL_BOTTOM),
            at=(sign * (_HALL_HALF_X - 0.30), _SHELL_CENTRE, (_ROOF_TOP + _HALL_BOTTOM) * 0.5),
            color=palette.HULL_DARK))

        # The piers either side of the shaft, and the chimney they become above the
        # roof. Darker than the walls, so the shaft reads as a slot rather than as
        # a gap between two rooms.
        parts.append(p.box(
            f"Pier{side}",
            size=(_BAY_INNER - _SHAFT_HALF_X, _HALL_DEPTH, _ROOF_TOP - _HALL_BOTTOM),
            at=(sign * ((_SHAFT_HALF_X + _BAY_INNER) * 0.5), _SHELL_CENTRE,
                (_ROOF_TOP + _HALL_BOTTOM) * 0.5),
            color=palette.HULL_DARK))
        parts.append(p.box(
            f"Chimney{side}",
            size=(_BAY_INNER - _SHAFT_HALF_X, _SHAFT_HALF_Y * 2.0, _SURFACE - _ROOF_TOP),
            at=(sign * ((_SHAFT_HALF_X + _BAY_INNER) * 0.5), _SHAFT_Y,
                (_ROOF_TOP + _SURFACE) * 0.5),
            color=palette.HULL_DARK))

        # The two things in the shaft that say which way the car moves.
        parts.append(p.box(
            f"Rail{side}", size=(0.16, 0.16, _SURFACE - _LOWER_FLOOR),
            at=(sign * _RAIL_X, _SHAFT_Y + _RAIL_Y, (_SURFACE + _LOWER_FLOOR) * 0.5),
            color=palette.METAL_DARK))

    # Roof and middle deck, both in three pieces so the shaft passes through them.
    for name, floor_top, slab in (("Roof", _ROOF_TOP, _ROOF_SLAB),
                                  ("Deck", _UPPER_FLOOR, _DECK_SLAB)):
        for side, sign in (("L", -1.0), ("R", 1.0)):
            parts.append(p.box(
                f"{name}{side}",
                size=(_HALL_HALF_X - _SHAFT_HALF_X, _HALL_DEPTH, slab),
                at=(sign * ((_HALL_HALF_X + _SHAFT_HALF_X) * 0.5), _SHELL_CENTRE,
                    floor_top - (slab * 0.5)),
                color=palette.CONCRETE))

        behind = _SHAFT_Y + _SHAFT_HALF_Y
        parts.append(p.box(
            f"{name}Back",
            size=(_SHAFT_HALF_X * 2.0, (_BACK_Y + _BACK_WALL) - behind, slab),
            at=(0.0, (behind + _BACK_Y + _BACK_WALL) * 0.5, floor_top - (slab * 0.5)),
            color=palette.CONCRETE))

    # Hazard paint down both edges of the shaft on both decks: the one place in this
    # hall where somebody would be standing next to a drop.
    for floor_top in (_UPPER_FLOOR, _LOWER_FLOOR):
        for sign in (-1.0, 1.0):
            parts.append(p.box(
                "Hazard", size=(0.30, _SHAFT_HALF_Y * 2.0, 0.06),
                at=(sign * (_SHAFT_HALF_X - 0.15), _SHAFT_Y, floor_top + 0.03),
                color=palette.WARNING))

    root = p.join(ASSET_NAME, parts)

    skyline = p.join(SKYLINE_NAME, [
        p.box(
            SKYLINE_NAME, size=(_FACE_HALF_X * 2.0, _FACE_THICK, 0.40),
            at=(0.0, _FACE_Y + (_FACE_THICK * 0.5), _ROOF_TOP - 0.20),
            color=palette.CONCRETE),
    ])
    p.set_pivot(skyline, (0.0, _FACE_Y + (_FACE_THICK * 0.5), _ROOF_TOP))
    p.attach(skyline, root)

    for slot, (x_centre, floor_top) in enumerate(_BAY_PLACES):
        p.attach(_bay(slot, x_centre, floor_top), root)

    return root


BUILDERS = {ASSET_NAME: build}
