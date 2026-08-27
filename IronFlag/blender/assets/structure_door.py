"""RF_Structure_Door - a team-owned gate that sinks into the ground, in three states.

Brief: **a wall segment that opens.** Nearly every dimension in this file is copied
from ``prop_wall.py`` rather than chosen, because a gate is only worth having if it
can be dropped into a wall run and come out looking like part of it: the same
five-metre length, the same half-piers at the ends, the same footing height, the
same coping line. What differs is what stands between the two piers - a steel leaf
instead of a concrete panel - and that leaf is a separate ``Leaf`` child object,
because Unity slides it straight down into the floor whenever a vehicle of the
owning side comes near. See ``IronFlag.Destruction.AutoDoor``.

It is a ``Structure`` rather than a ``Prop`` even though it tiles with one, because
that split is about what a thing *does*: the trees, buildings and walls are
repeated neutral cover, and the depots, tower, turret and this are purpose-built
installations that belong to somebody and act. A gate that opens for one side and
not the other is the second of those, not the first.

**The bay is the door.** A wall's piers stand 4.00 m apart, so the opening here is
4.00 m and the gate is exactly one bay of the wall it sits in. That leaves 0.40 m
of daylight either side of the tank, the widest vehicle in the game at 3.19 m: a
gate you have to line up for is still a gate, and it is the only number in this
file that is a judgement rather than a copy.

**Nothing crosses the threshold.** The footing runs under the two piers and stops -
where a wall has a continuous 0.30 m plinth, a door has a gap. That is what an open
gate has to be: not a low step a vehicle bumps over, but bare ground. It reads
right from the top-down camera too, where a run of walls is a solid line and a gate
is the break in it.

**Team trim goes in two places, for two different jobs.** The collars around the
piers say whose gate this is and never move. The cap along the top of the leaf is
the *state*: from two hundred metres up, a closed gate is a coloured bar in a grey
wall and an open one is a gap, so what a player reads is the colour being there or
not being there rather than an animation they have to catch.

Built running along X and blocking across Y, exactly as the wall is, so the -Y
facing convention every other asset follows means a door placed at yaw 0 in Unity
runs east-west and stops north-south traffic - and a door and a wall placed at the
same yaw are the same wall.
"""

from rf import material
from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Structure_Door_Intact"
ASSET_DAMAGED = "RF_Structure_Door_Damaged"
ASSET_DESTROYED = "RF_Structure_Door_Destroyed"

#: Matches IronFlag.Destruction.AutoDoor.LeafNodeName. Renaming one without the other
#: leaves a gate that never opens - and, unlike a turret that never fires, one nobody
#: notices is broken until they drive into their own door.
_LEAF_NAME = "Leaf"

#: Length of one segment, and the whole reason a door tiles with a wall: this is
#: prop_wall's _LENGTH, which is the coarsest step LevelEditorSession.GridSteps offers.
_LENGTH = 5.00

#: Height of the finished gate, cap included. prop_wall's _HEIGHT.
_HEIGHT = 2.00

#: The courses, from prop_wall. The footing and the coping are shared with the wall so
#: a run of the two has one plinth line and one cap line rather than two that nearly
#: agree. The leaf is thinner than a wall's 0.60 panel because it is steel in a frame
#: rather than poured concrete - which is also the honest reason a gate is worth fewer
#: hit points than the wall it sits in.
_FOOTING_THICK = 0.90
_FOOTING_HEIGHT = 0.30
_PIER_THICK = 0.80
_COPING_THICK = 0.76
_COPING_HEIGHT = 0.15
_LEAF_THICK = 0.50

#: Piers rise to just under the cap, which then sits on them. prop_wall's numbers.
_PIER_WIDTH = 0.50
_PIER_HEIGHT = _HEIGHT - _COPING_HEIGHT
_PIER_X = (_LENGTH - _PIER_WIDTH) * 0.5

#: Clear width of the opening: the gap between the two piers' inner faces. Four metres,
#: which is the same bay a wall leaves between its own piers - see the module docstring.
_OPENING = _LENGTH - (_PIER_WIDTH * 2.0)

#: Where the cap of anything two metres tall sits. Both the piers and the leaf carry one.
_CAP_Z = _HEIGHT - (_COPING_HEIGHT * 0.5)

#: Ceiling every piece of the destroyed state's geometry stays under, in metres - about
#: knee height, and prop_wall's number for the same reason. ``Destructible`` switches a
#: destroyed structure's colliders off the moment it is shown, so nothing here has a
#: hitbox however tall it is drawn; this constant is what stops the *picture* disagreeing
#: with that. Checked against the built mesh rather than by eye, because a rotated box's
#: true top is not its unrotated height - see WALLS_NOTES.md.
_DESTROYED_CEILING = 0.45

#: Height of the team band round a pier, and where its centre sits.
#:
#: The wrecked state has to bring the band down under the ceiling, so that number is
#: derived from the ceiling rather than written next to it: a band that kept its
#: standing height would be the one piece of a flattened gate poking above knee height,
#: and it would be the *coloured* piece, which is the worst one to get wrong. The 0.015
#: is what keeps its underside inside the pad it settles onto instead of coplanar with
#: the pad's top face, which would flicker.
_COLLAR_HEIGHT = 0.14
_COLLAR_Z = _FOOTING_HEIGHT + 0.12
_DESTROYED_COLLAR_Z = _DESTROYED_CEILING - (_COLLAR_HEIGHT * 0.5) - 0.015


def _pad(name, x, color):
    """Build the plinth under one pier.

    Two of these rather than one continuous footing, which is the whole difference
    between a door's ground and a wall's: the threshold between them is bare, so an
    open gate is ground rather than a 0.30 m step a vehicle has to climb.

    Args:
        name: Object name.
        x: Offset along the run, in metres.
        color: Linear RGBA for the concrete.

    Returns:
        The pad object.
    """
    return p.box(name, size=(_PIER_WIDTH, _FOOTING_THICK, _FOOTING_HEIGHT),
                 at=(x, 0.0, _FOOTING_HEIGHT * 0.5), color=color)


def _pier(name, x, color, height=_PIER_HEIGHT):
    """Build one of the half-piers a door shares with a wall.

    Half a pier at either end, so a door butted against a wall makes one full pier at
    the join exactly as two walls do. This is the single dimension that must not drift
    from ``prop_wall``.

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


def _pier_cap(name, x, color):
    """Build the coping course on top of one pier.

    The same width and height as a wall's coping, so the cap line runs unbroken across
    a join between a door and its neighbour.

    Args:
        name: Object name.
        x: Offset along the run, in metres.
        color: Linear RGBA for the stone.

    Returns:
        The cap object.
    """
    return p.box(name, size=(_PIER_WIDTH, _COPING_THICK, _COPING_HEIGHT),
                 at=(x, 0.0, _CAP_Z), color=color)


def _collars(z=_COLLAR_Z):
    """Build the team-coloured bands around both piers.

    On the piers rather than on the leaf, for the reason the turret's ring is on its
    base rather than its head: the leaf moves, and an open gate whose only marking has
    just gone underground is a gate nobody can tell the owner of.

    Proud across the *thickness* and inset along the *run*. A band that overhung the
    segment's ends would push geometry 0.04 m into whatever is butted against it, and
    a door's whole reason for being five metres long is that something usually is.

    Args:
        z: Height of the bands' centres, in metres.

    Returns:
        The trim parts, in build order.
    """
    size = (_PIER_WIDTH - 0.04, _PIER_THICK + 0.08, _COLLAR_HEIGHT)
    return [
        p.box("CollarWest", size=size, at=(-_PIER_X, 0.0, z), group=material.TEAM),
        p.box("CollarEast", size=size, at=(_PIER_X, 0.0, z), group=material.TEAM),
    ]


def _leaf(color, extra=()):
    """Build the sliding leaf, with its team cap parented to it.

    The leaf's origin is put on the ground at the middle of the opening, so Unity's
    closed position is a plain zero on ``localPosition`` and the open one is a plain
    negative Y. Anything parented here travels with it - which is why the cap is
    attached to the leaf and the collars are not.

    Args:
        color: Linear RGBA for the steel.
        extra: Further parts joined into the leaf body, such as damage.

    Returns:
        The leaf object, with its ``TeamTrim`` child already attached.
    """
    body = p.box("LeafPanel", size=(_OPENING, _LEAF_THICK, _PIER_HEIGHT),
                 at=(0.0, 0.0, _PIER_HEIGHT * 0.5), color=color)

    leaf = p.join(_LEAF_NAME, [body] + list(extra))
    p.set_pivot(leaf, (0.0, 0.0, 0.0))

    # Proud of the panel by the same margin a wall's coping is proud of its own, so a
    # closed gate's cap and its neighbours' copings read as one rail.
    p.attach_group(leaf, material.TEAM, [
        p.box("Cap", size=(_OPENING, _COPING_THICK, _COPING_HEIGHT),
              at=(0.0, 0.0, _CAP_Z), group=material.TEAM),
    ])
    return leaf


def build_intact():
    """Build the working gate.

    Returns:
        The joined root object, with the leaf and the team collars parented to it.
    """
    scene.begin(ASSET_INTACT)

    frame = [
        _pad("PadWest", -_PIER_X, palette.CONCRETE),
        _pad("PadEast", _PIER_X, palette.CONCRETE),
        _pier("PierWest", -_PIER_X, palette.CONCRETE),
        _pier("PierEast", _PIER_X, palette.CONCRETE),
        _pier_cap("CapWest", -_PIER_X, palette.SAND),
        _pier_cap("CapEast", _PIER_X, palette.SAND),
    ]
    root = p.join(ASSET_INTACT, frame)

    p.attach(_leaf(palette.METAL_DARK), root)
    p.attach_group(root, material.TEAM, _collars())
    return root


def build_damaged():
    """Build the gate after a hit: the leaf buckled, one pier cracked, still working.

    The leaf keeps its full height and its full width, deliberately. ``prop_wall``'s
    damaged state makes the same promise for the same reason - a damaged barrier stops
    exactly what an intact one stops, and being *through* it is what the destroyed
    state is for. A gate with a hole in it at half hit points would make ``DamagedAt``
    the breach point and leave the destroyed state meaning nothing.

    It still opens, too, and that is the point of the state: what a damaged gate has
    lost is armour, not its mechanism. Losing the mechanism is what being destroyed is.

    Returns:
        The joined root object, with the leaf and the team collars parented to it.
    """
    scene.begin(ASSET_DAMAGED)

    frame = [
        _pad("PadWest", -_PIER_X, palette.CONCRETE),
        _pad("PadEast", _PIER_X, palette.CONCRETE),
        _pier("PierWest", -_PIER_X, palette.CONCRETE),
        # The struck pier is cracked down to the height of the leaf's shoulder, which
        # is what stops the damage looking like a clean saw cut. All on one side on
        # purpose, so it reads as the direction the shot came from rather than as even
        # wear - the same argument the wall and the turret both make.
        _pier("PierEast", _PIER_X, palette.CHARRED, height=1.30),
        _pier_cap("CapWest", -_PIER_X, palette.SAND),
        # The piece of coping that came off the struck pier, at its foot. Centred on
        # its own measured half-range rather than half its thickness - see the note in
        # build_destroyed about what a tilt does to a long box's true bottom.
        p.box("CapFallen", size=(0.62, 0.34, 0.16), at=(1.62, -0.74, 0.123),
              rot=(0.0, 8.0, -14.0), color=palette.CHARRED),
    ]
    root = p.join(ASSET_DAMAGED, frame)

    # A dent driven into the leaf from the struck side, and the runner it rides in torn
    # open beside it. Between them they say the *mechanism* is what is failing, which is
    # the honest reason a gate is softer than the wall it sits in. Both are joined into
    # the leaf rather than left on the frame, so they travel down with it.
    dent = p.box("Dent", size=(1.10, _LEAF_THICK + 0.08, 0.70),
                 at=(1.05, 0.0, 1.15), rot=(0.0, 9.0, 0.0), color=palette.CHARRED)
    runner = p.box("Runner", size=(0.34, _LEAF_THICK + 0.10, 0.46),
                   at=(1.72, 0.0, 0.34), rot=(0.0, -6.0, 0.0), color=palette.SCORCH)

    p.attach(_leaf(palette.CHARRED, extra=(dent, runner)), root)
    p.attach_group(root, material.TEAM, _collars())
    return root


def build_destroyed():
    """Build the breach: the leaf blown off its runners and flat across the threshold.

    There is deliberately no ``Leaf`` child here. A destroyed gate has nothing to
    drive, which is what makes the rubble inert by construction rather than by a check
    somebody could forget to write - ``AutoDoor`` looks for the node inside whichever
    state is showing and finds nothing. It is the same trick ``structure_turret``
    plays with its missing ``Turret``.

    Nothing here stands higher than ``_DESTROYED_CEILING``. The rubble's colliders are
    switched off the moment it is shown, so a heap a player could read as still
    blocking would be a gate they drive straight through - and a gate is the one thing
    on the map somebody is already *expecting* to drive through, which makes a picture
    that lies about it worse here than anywhere else.

    Returns:
        The joined root object, with the team collars parented to it.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        # The pads survive every time. They are what says a gate stood here rather than
        # that the run was always this long, and they keep a wrecked line reading as a
        # line across the ground from the top-down camera.
        _pad("PadWest", -_PIER_X, palette.CHARRED),
        _pad("PadEast", _PIER_X, palette.CHARRED),
        # One stump at each end, so a broken gate between two standing walls does not
        # leave its neighbours' piers hanging in mid-air. Uneven heights, so the break
        # reads as a break rather than as a second matched pair of posts.
        _pier("PierWest", -_PIER_X, palette.CHARRED, height=0.42),
        _pier("PierEast", _PIER_X, palette.CHARRED, height=0.28),
        # The leaf, down across the threshold where it fell. Centred on its own measured
        # half-range rather than half its thickness, so a long tilted box's true low
        # corner rests on the ground instead of hanging under it. A three-degree tilt on
        # a 3.60 m box moves its bottom by 0.094 m, which is four times the number the
        # box's own thickness would have suggested - and the first build of this state
        # did put it 0.029 m underground.
        p.box("LeafFallen", size=(3.60, 1.05, 0.24), at=(-0.15, 0.30, 0.215),
              rot=(0.0, 3.0, -4.0), color=palette.SCORCH),
        # The top rail, sheared off and lying clear of it.
        p.box("RailFallen", size=(2.05, 0.30, 0.18), at=(0.55, -0.95, 0.180),
              rot=(0.0, 5.0, 7.0), color=palette.CHARRED),
        p.box("Rubble1", size=(0.50, 0.42, 0.22), at=(1.70, 0.62, 0.125),
              rot=(4.0, 0.0, 16.0), color=palette.SCORCH),
        p.box("Rubble2", size=(0.42, 0.36, 0.20), at=(-1.78, -0.70, 0.122),
              rot=(0.0, 6.0, -17.0), color=palette.CHARRED),
    ]
    root = p.join(ASSET_DESTROYED, parts)

    # The collars survive, scorched but readable: a wrecked gate is still somebody's,
    # and a player driving through one needs to know whose ground they are on. Settled
    # into the pads, because at their standing height they would be the one piece of a
    # flattened gate above the ceiling - and the coloured one.
    p.attach_group(root, material.TEAM, _collars(_DESTROYED_COLLAR_Z))
    return root


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
