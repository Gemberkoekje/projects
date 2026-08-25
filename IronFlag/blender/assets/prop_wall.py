"""RF_Prop_Wall - a short concrete barrier segment, in three destruction states.

Brief: the cheapest way to make a piece of ground defended. One segment is five
metres of capped concrete panel; a fortified corner is four of them in a row with a
turret behind. It is the only asset in the game designed to be placed *touching
copies of itself*, and every dimension here follows from that.

**Five metres, because that is the editor's coarsest grid.** ``LevelEditorSession``
offers 0.5, 1, 2 and 5 m snapping, so a five-metre segment tiles seamlessly at both
1 m and 5 m: click, click, click along the grid and the run comes out continuous
rather than a line of boxes with gaps somebody has to nudge shut.

**The piers stand at the joins, not in the middle.** Each segment carries a
half-pier at either end, so two neighbours make one full pier where they meet and a
lone segment gets end posts. That is what turns repetition into construction - a
wall with a pier every five metres reads as something that was built, where five
identical panels in a row read as tiling.

**Two metres tall: over a jeep, under a tank.** A jeep (1.6 m) is hidden behind one
and a tank (2.4 m) is not, which is the whole reason a wall is worth placing and the
reason it is not worth placing everywhere.

Colors are the neutral map-furniture set - a wall belongs to whoever is standing
behind it, so there is no team trim and ``StructureTuning.BelongsToASide`` says so.
The cap is ``SAND``, the same color as a building's roof, because the top-down
camera sees the top face of everything and a pale top is how the map says "somebody
built this" as against the grey of a road.

Built running along X and blocking across Y, so the -Y facing convention every other
asset follows means a wall placed at yaw 0 in Unity runs east-west and stops
north-south traffic.
"""

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Prop_Wall_Intact"
ASSET_DAMAGED = "RF_Prop_Wall_Damaged"
ASSET_DESTROYED = "RF_Prop_Wall_Destroyed"

#: Length of one segment. The number the whole asset is built around - see the module
#: docstring: it is the coarsest step LevelEditorSession.GridSteps offers, so a row of
#: these butts up exactly when placed on the grid.
_LENGTH = 5.00

#: Height of the finished wall, coping included.
_HEIGHT = 2.00

#: The three courses, thickest at the bottom. A wall that steps inward as it rises is
#: how one is actually built, and from a camera looking down at it the three widths
#: read as concentric outlines rather than as one flat slab.
_FOOTING_THICK = 0.90
_PIER_THICK = 0.80
_COPING_THICK = 0.76
_PANEL_THICK = 0.60

_FOOTING_HEIGHT = 0.30
_COPING_HEIGHT = 0.15
_PANEL_HEIGHT = _HEIGHT - _FOOTING_HEIGHT - _COPING_HEIGHT
_PANEL_Z = _FOOTING_HEIGHT + (_PANEL_HEIGHT * 0.5)
_COPING_Z = _HEIGHT - (_COPING_HEIGHT * 0.5)

#: Piers rise to just under the coping, which then sits on them.
_PIER_WIDTH = 0.50
_PIER_HEIGHT = _HEIGHT - _COPING_HEIGHT
_PIER_X = (_LENGTH - _PIER_WIDTH) * 0.5

#: Where the damaged state's break starts, measured along X. Everything from here to
#: the +X end has lost its top; everything before it is untouched. All on one side on
#: purpose, so the damage reads as the direction the shot came from rather than as
#: even wear - the same argument the turret's damaged state makes.
_BREAK_X = 0.40

#: What is left standing along the broken stretch, ground to top. Above a metre, which
#: is well past anything a ground vehicle can drive up: a damaged wall is still a wall.
#: Being *through* it is what the destroyed state is for.
_BROKEN_TOP = 1.15

#: Ceiling every piece of the destroyed state's geometry stays under, in metres - about
#: knee height. ``Destructible`` switches a destroyed structure's colliders off the
#: moment it is shown, so nothing here has a hitbox regardless of how tall it is drawn -
#: this constant is what stops the *picture* disagreeing with that and reading as a wall
#: a player could still be blocked by. Checked empirically rather than by eye: a rotated
#: box's true top is not its unrotated height, so every part below is measured against
#: this number with the actual build, not estimated from its size tuple.
_DESTROYED_CEILING = 0.45


def _footing(color):
    """Build the wide plinth the wall stands on.

    Args:
        color: Linear RGBA for the concrete.

    Returns:
        The footing object.
    """
    return p.box("Footing", size=(_LENGTH, _FOOTING_THICK, _FOOTING_HEIGHT),
                 at=(0.0, 0.0, _FOOTING_HEIGHT * 0.5), color=color)


def _pier(name, x, color, height=_PIER_HEIGHT):
    """Build one of the half-piers that stand at a segment's ends.

    Args:
        name: Object name.
        x: Offset along the run, in metres.
        color: Linear RGBA for the concrete.
        height: How much of it is left standing, in metres.

    Returns:
        The pier object.
    """
    return p.box(name, size=(_PIER_WIDTH, _PIER_THICK, height),
                 at=(x, 0.0, height * 0.5), color=color)


def build_intact():
    """Build the standing barrier.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = [
        _footing(palette.CONCRETE),
        p.box("Panel", size=(_LENGTH, _PANEL_THICK, _PANEL_HEIGHT),
              at=(0.0, 0.0, _PANEL_Z), color=palette.CONCRETE),
        _pier("PierWest", -_PIER_X, palette.CONCRETE),
        _pier("PierEast", _PIER_X, palette.CONCRETE),
        # Exactly the segment's length, so two neighbours' caps butt instead of
        # overlapping. An overhang here would put two coplanar faces in the same
        # place at every join, which is a flickering seam every five metres.
        p.box("Coping", size=(_LENGTH, _COPING_THICK, _COPING_HEIGHT),
              at=(0.0, 0.0, _COPING_Z), color=palette.SAND),
    ]
    return p.join(ASSET_INTACT, parts)


def build_damaged():
    """Build the barrier with its top course shot off one end.

    The lower course runs unbroken from end to end and both piers still stand, so a
    damaged wall stops exactly what an intact one stops. What it has lost is its cap
    and its height, which is the readable version of "one more like that" - and the
    hole is the destroyed state's business.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DAMAGED)

    whole = (_LENGTH * 0.5) + _BREAK_X
    broken = (_LENGTH * 0.5) - _BREAK_X

    parts = [
        _footing(palette.CONCRETE),
        # The untouched stretch, full height, from the -X end up to the break.
        p.box("Panel", size=(whole, _PANEL_THICK, _PANEL_HEIGHT),
              at=(-broken * 0.5, 0.0, _PANEL_Z), color=palette.CONCRETE),
        # And the stretch past it, sheared off at chest height.
        p.box("PanelBroken", size=(broken, _PANEL_THICK, _BROKEN_TOP - _FOOTING_HEIGHT),
              at=(whole * 0.5, 0.0, (_BROKEN_TOP + _FOOTING_HEIGHT) * 0.5),
              color=palette.CONCRETE),
        _pier("PierWest", -_PIER_X, palette.CONCRETE),
        # The pier on the struck side is cracked down to the height of the course
        # beside it, which is what stops the break looking like a clean saw cut.
        _pier("PierEast", _PIER_X, palette.CHARRED, height=_BROKEN_TOP - 0.10),
        p.box("Coping", size=(whole, _COPING_THICK, _COPING_HEIGHT),
              at=(-broken * 0.5, 0.0, _COPING_Z), color=palette.SAND),
        # A bite out of the shear face, so the break has a direction.
        p.box("Scar", size=(0.85, _PANEL_THICK + 0.06, 0.60),
              at=(_BREAK_X + 0.30, 0.0, _BROKEN_TOP - 0.18),
              rot=(0.0, 11.0, 0.0), color=palette.CHARRED),
        # The piece that came off, at the foot on the near side.
        p.box("Slab", size=(1.30, 0.42, 0.28), at=(1.35, -0.78, 0.16),
              rot=(0.0, 7.0, -13.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_DAMAGED, parts)


def build_destroyed():
    """Build the breach: a scorched footing with the panel down beside it.

    Nothing here stands higher than ``_DESTROYED_CEILING``, deliberately. The
    rubble's colliders are switched off the moment it is shown - see
    ``IronFlag.Destruction.Destructible`` - so a heap a player could read as still
    blocking would be a wall they drive straight through, which is worse than no
    wall at all.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        # The footing survives every time. It is what says a wall stood here rather
        # than a lorry having tipped its load, and it keeps a wrecked run reading as
        # a line across the ground from the top-down camera.
        _footing(palette.CHARRED),
        # One stump at each join, so a broken segment between two standing ones does
        # not leave its neighbours' piers hanging in mid-air. Uneven heights, so the
        # break reads as a break rather than a second matched pair of posts.
        _pier("PierWest", -_PIER_X, palette.CHARRED, height=0.40),
        _pier("PierEast", _PIER_X, palette.CHARRED, height=0.26),
        # A stub of the panel still on its footing at the west end, no taller than the
        # footing plus the ceiling allows.
        p.box("Stub", size=(1.10, _PANEL_THICK, _DESTROYED_CEILING - _FOOTING_HEIGHT),
              at=(-1.85, 0.0, (_DESTROYED_CEILING + _FOOTING_HEIGHT) * 0.5),
              color=palette.CHARRED),
        # The panel itself, toppled clear of the line it stood on. Centred on its own
        # rotated half-height rather than a round number, so a long, tilted box's true
        # low corner rests on the ground instead of hanging under it or floating over
        # it - the naive box-half-thickness a flat box would use is the wrong number
        # once it is tilted about a long axis.
        p.box("SlabFallen", size=(2.20, 0.52, 0.22), at=(0.35, 0.92, 0.19),
              rot=(0.0, 4.0, -5.0), color=palette.SCORCH),
        p.box("SlabLeaning", size=(1.45, 0.48, 0.20), at=(-0.70, -0.84, 0.18),
              rot=(0.0, 6.0, 5.0), color=palette.CHARRED),
        p.box("Rubble1", size=(0.56, 0.44, 0.22), at=(1.55, -0.58, 0.13),
              rot=(5.0, 0.0, 15.0), color=palette.SCORCH),
        p.box("Rubble2", size=(0.44, 0.38, 0.20), at=(-2.10, 0.66, 0.13),
              rot=(0.0, 7.0, -18.0), color=palette.CHARRED),
    ]
    return p.join(ASSET_DESTROYED, parts)


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
