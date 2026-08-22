"""RF_Structure_FlagTower - the reference for destruction states.

Where ``vehicle_jeep.py`` shows a vehicle with moving parts, this shows the other
half of the pipeline: one family, three meshes, built from shared helpers so the
damaged version is unmistakably the same building as the intact one. Unity swaps
between the three per the state-swap destruction system in the design doc.

Silhouette brief (asset spec): pyramidal, so it reads instantly on the minimap and
at gameplay distance; ~6m tall; neutral color only, no team tint - the real tower
and the decoy must be indistinguishable, which is the whole point of the decoy.

The flag itself is deliberately *not* part of this mesh. It is a gameplay object
the jeep picks up, and a decoy with no flag on it would be readable from across
the map.

Each state carries a ``FlagMount`` object whose origin is the point a flag stands
on, kept out of the join for the same reason the bunker keeps ``LiftPlatform``
separate: Unity reads that origin rather than guessing a height, so moving where
the flag sits is an art change and nothing else.

**The flag stands inside the tower, on the plinth, in every state.** It does not
move as the tower comes apart - what changes is how much of the tower is in the
way. Intact, it is sealed in and shows nothing. Damaged, the top is gone and you
look down into it. Destroyed, there is barely a stump left and it stands in the
open.

The damaged state has had its **top taken off** rather than windows cut in it,
because the camera looks *down* at 58 degrees: a hole in the roof shows the whole
flag and a window in a wall shows a slice of it. A solid lower course survives to
the height of a vehicle - so the tower is a box to look into rather than a doorway
to drive into - and the four corners stand above it.

Those corners are laid as **courses of the same stone as the wall**: inner faces
all on one line, because the inside of a tower like this is a straight vertical
shaft, and only the outer face stepping inward once a course to follow the taper.
That is how a tapered masonry building is actually laid, and it is what stops a
corner reading as a rectangle tacked onto a slope - a tilted box slopes on the
inside too. One corner is a course shorter than the rest and loose stones sit at
the shear, because four stumps of matching height read as a design and a building
does not come apart evenly.
"""

import math

from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Structure_FlagTower_Intact"
ASSET_DAMAGED = "RF_Structure_FlagTower_Damaged"
ASSET_DESTROYED = "RF_Structure_FlagTower_Destroyed"

#: Matches IronFlag.Objective.FlagTower.MountNodeName. Renaming one without the
#: other leaves a flag standing at the foot of the tower instead of on it.
_MOUNT_NAME = "FlagMount"

_PLINTH_SIZE = (4.4, 4.4, 0.5)

#: Half the banner's length, from ``prop_flag.py``: the flag hangs to one side of
#: its staff, as a flag does, so its staff is not its centre.
_BANNER_REACH = 0.775

#: Where a flag stands: on the plinth, in every state, and set to one side by
#: :data:`_BANNER_REACH` so that the *banner* is centred on the tower rather than
#: the staff. Centring the staff instead puts a 1.45 m banner into a tower whose
#: inside is 1.3 m across at that height - it pokes out of one wall and only a
#: slice of it shows through the breach on the opposite side.
_MOUNT_AT = (_BANNER_REACH, 0.0, 0.5)

#: Which way a flag faces on this tower. Turned across the tower so the banner
#: presents its broad face through the breach rather than its edge: a flag seen
#: edge-on through a window is a green line a couple of pixels wide from the
#: gameplay camera, which is no answer at all to "which pyramid is the real one".
_MOUNT_YAW = 90.0

#: Height the surviving wall of a damaged tower reaches, in metres. The flag's
#: banner sits between 1.85 m and 2.75 m once it is standing on the plinth, so the
#: wall has to stop below that - and above a tank, or the tower would be a doorway
#: to drive into rather than a box to look into.
_WALL_TOP = 1.75

#: Height the four surviving corners reach. Above the flag's finial at 3.16 m, so
#: the flag reads as being *inside* a broken tower rather than growing out of it.
_CORNER_TOP = 3.30

#: Half-width of the shaft inside the tower. The interior is a straight vertical
#: well - only the *outside* of a tower like this slopes - so every course of the
#: surviving corners has its inner faces on this line however far in the outer
#: face has stepped. Wide enough that the flag's 1.45 m banner stands in it clear
#: of the masonry.
_SHAFT = 0.80

#: How many courses of stonework each surviving corner is built from. The outer
#: face steps in once per course to follow the tower's slope, which is how a
#: tapered masonry building is actually laid - and is what stops a corner reading
#: as a rectangle tacked onto a slope.
_COURSES = 3

_BODY_SIZE = (4.0, 4.0, 4.0)
_BODY_APEX = (1.6, 1.6)
_BODY_CENTRE_Z = 2.5


def _plinth(color):
    """Build the stone base, which survives every state.

    Args:
        color: Linear RGBA for the plinth.

    Returns:
        The plinth object.
    """
    return p.box("Plinth", size=_PLINTH_SIZE, at=(0.0, 0.0, 0.25), color=color)


def _body(color, height_scale=1.0):
    """Build the main truncated pyramid.

    Args:
        color: Linear RGBA for the body.
        height_scale: Fraction of full height, for the collapsed states.

    Returns:
        The body object.
    """
    height = _BODY_SIZE[2] * height_scale
    # Taper toward the same apex proportion regardless of how much is left standing.
    apex = tuple(_BODY_SIZE[i] - (_BODY_SIZE[i] - _BODY_APEX[i]) * height_scale for i in (0, 1))
    return p.pyramid("Body", size=(_BODY_SIZE[0], _BODY_SIZE[1], height),
                     at=(0.0, 0.0, _PLINTH_SIZE[2] + height * 0.5),
                     apex_size=apex, color=color)


def _mount(root, at=None, yaw=None):
    """Add the socket a flag stands in, with its origin at the flag's foot.

    The socket carries the flag's *rotation* as well as its position, so which way
    a flag faces on a tower is an art decision like everything else about where it
    stands. Unity reads both off this object.

    Args:
        root: The joined tower this belongs to.
        at: World-space point a flag's staff stands on. Defaults to
            :data:`_MOUNT_AT`.
        yaw: Degrees about Z the flag is turned. Defaults to :data:`_MOUNT_YAW`.

    Returns:
        The mount object, parented to ``root``.
    """
    at = _MOUNT_AT if at is None else at
    yaw = _MOUNT_YAW if yaw is None else yaw

    collar = p.join(_MOUNT_NAME, [
        p.cylinder("Collar", radius=0.14, height=0.16,
                   at=(at[0], at[1], at[2] - 0.08),
                   color=palette.METAL_DARK, segments=8),
    ])
    p.set_pivot(collar, at)
    # Safe to turn after baking because the socket is a near-round collar: the
    # geometry is unchanged by it and only the exported node rotation matters.
    collar.rotation_euler = (0.0, 0.0, math.radians(yaw))
    p.attach(collar, root)
    return collar


def _half_width(z):
    """Return the body's half-width at a height, following the intact taper.

    Args:
        z: Height in metres.

    Returns:
        Half the body's footprint there, so a course can be cut out of the taper
        and still line up with the courses above and below it.
    """
    base = _BODY_SIZE[0] * 0.5
    apex = _BODY_APEX[0] * 0.5
    climbed = (z - _PLINTH_SIZE[2]) / _BODY_SIZE[2]
    return base - ((base - apex) * climbed)


#: The four corners, as (label, x sign, y sign, courses left standing). One corner
#: is a course shorter than the rest, because four stumps of matching height read
#: as a design and a building does not come apart evenly.
_CORNERS = (
    ("NE", 1.0, 1.0, 3),
    ("NW", -1.0, 1.0, 3),
    ("SE", 1.0, -1.0, 2),
    ("SW", -1.0, -1.0, 3),
)


def _course(index):
    """Return the height band and outer reach of one course of stonework.

    Args:
        index: Course number, counting up from the surviving wall.

    Returns:
        ``(bottom, top, outer)`` in metres - ``outer`` being the half-width the
        tower's sloped face has reached by the middle of that course.
    """
    height = (_CORNER_TOP - _WALL_TOP) / _COURSES
    bottom = _WALL_TOP + (index * height)
    top = bottom + height
    return (bottom, top, _half_width((bottom + top) * 0.5))


def _open_top(wall_color):
    """Build the body with its top gone and its four corners still standing.

    Args:
        wall_color: Linear RGBA for the surviving stone.

    Returns:
        The body parts, in build order.
    """
    lower = _WALL_TOP - _PLINTH_SIZE[2]
    rim = _half_width(_WALL_TOP)

    parts = [
        p.pyramid("CourseLower", size=(_BODY_SIZE[0], _BODY_SIZE[1], lower),
                  at=(0.0, 0.0, _PLINTH_SIZE[2] + lower * 0.5),
                  apex_size=(rim * 2.0, rim * 2.0),
                  color=wall_color),
    ]

    # The four corners, each laid as courses of stone: inner faces on the shaft and
    # straight all the way up, outer faces stepping in once a course to follow the
    # slope of the tower they were part of. Same stone as the wall below them, so a
    # breached tower is the whole tower with its middle taken out rather than a
    # different building sitting on a plinth.
    for label, sign_x, sign_y, standing in _CORNERS:
        for index in range(standing):
            bottom, top, outer = _course(index)
            thickness = outer - _SHAFT
            middle_of = (outer + _SHAFT) * 0.5

            parts.append(p.box(
                f"Corner{label}{index + 1}",
                size=(thickness, thickness, top - bottom),
                at=(sign_x * middle_of, sign_y * middle_of, (bottom + top) * 0.5),
                color=wall_color))

    parts.extend(_spalling(wall_color))
    return parts


def _spalling(wall_color):
    """Build the individual stones left sticking out of the broken courses.

    The courses give the corners their stepped profile; these give the *edges* of
    the break something other than a straight line. Each one is a single stone that
    stayed put when the wall beside it came away - two jutting off the side of a
    corner, one still hanging under the shear where the course above it went.

    Args:
        wall_color: Linear RGBA for the stone.

    Returns:
        The loose stones, in build order.
    """
    # (corner, course it belongs to, how far along the face, height offset, size)
    stones = (
        ("NE", 0, 0.55, 0.10, (0.30, 0.26, 0.22)),
        ("NW", 1, -0.48, -0.05, (0.26, 0.28, 0.20)),
        ("SW", 2, 0.44, 0.06, (0.24, 0.26, 0.24)),
        # Under the shear on the corner that lost its top course, still hanging.
        ("SE", 2, 0.18, -0.30, (0.28, 0.28, 0.26)),
    )

    signs = {label: (sign_x, sign_y) for label, sign_x, sign_y, _ in _CORNERS}
    parts = []
    for index, (corner, course, along, lift, size) in enumerate(stones):
        sign_x, sign_y = signs[corner]
        bottom, top, outer = _course(course)
        face = outer - (size[0] * 0.5)

        parts.append(p.box(
            f"Stone{index + 1}",
            size=size,
            # Along one face of the corner, past where the wall used to carry on.
            at=(sign_x * face,
                sign_y * (_SHAFT - (size[1] * 0.5)) + (sign_y * along),
                ((bottom + top) * 0.5) + lift),
            color=wall_color))

    return parts


def _rubble(index, size, at, rot, color):
    """Build one piece of collapsed masonry.

    Args:
        index: Distinguishes the object names within a state.
        size: ``(x, y, z)`` extents in meters.
        at: World-space center.
        rot: Euler rotation in degrees.
        color: Linear RGBA, normally CHARRED or SCORCH.

    Returns:
        The rubble object.
    """
    return p.box(f"Rubble{index}", size=size, at=at, rot=rot, color=color)


def build_intact():
    """Build the undamaged tower.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_INTACT)

    parts = [
        _plinth(palette.CONCRETE),
        _body(palette.CONCRETE),
        # Cap course, in the second neutral tone so the top edge reads at distance.
        p.pyramid("Cap", size=(1.6, 1.6, 0.7), at=(0.0, 0.0, 4.85),
                  apex_size=(0.9, 0.9), color=palette.SAND),
        # Recessed entrance on the -Y face.
        p.box("Doorway", size=(1.1, 0.35, 1.4), at=(0.0, -1.72, 1.20), color=palette.HULL_DARK),
        # Flag pole. The flag is a gameplay object, not geometry.
        p.cylinder("Pole", radius=0.05, height=1.0, at=(0.0, 0.0, 5.70),
                   color=palette.METAL, segments=8),
    ]
    root = p.join(ASSET_INTACT, parts)
    # Sealed inside, on the plinth. Nothing is ever shown here - an intact tower
    # hides what it holds - and the flag being *within* the walls rather than on
    # top of them is what makes that true rather than merely arranged: there is no
    # angle it could be glimpsed from even if something turned it on.
    _mount(root)
    return root


def build_damaged():
    """Build the tower with its top blown off and its four corners standing.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DAMAGED)

    parts = [
        _plinth(palette.CONCRETE),
    ] + _open_top(palette.CONCRETE) + [
        # The cap course and its pole, down on the apron where they landed. What
        # used to be the top of the tower is on the floor next to it, which is the
        # readable version of "the top came off".
        p.box("CapFallen", size=(1.5, 1.4, 0.5), at=(-2.45, 1.60, 0.30),
              rot=(6.0, -14.0, 28.0), color=palette.CHARRED),
        p.cylinder("Pole", radius=0.05, height=1.0, at=(-2.60, 0.35, 0.25),
                   rot=(0.0, 84.0, 32.0), color=palette.METAL_DARK, segments=8),
        _rubble(1, (1.1, 0.7, 0.45), (2.05, 0.85, 0.55), (0.0, 14.0, 25.0), palette.CHARRED),
    ]
    root = p.join(ASSET_DAMAGED, parts)
    # The same spot as every other state. The flag has not moved - the wall in
    # front of it has gone, and it is now framed by the breach.
    _mount(root)
    return root


def build_destroyed():
    """Build the collapsed tower: a burnt stump on the original footprint.

    Returns:
        The joined root object.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        _plinth(palette.CHARRED),
        _body(palette.CHARRED, height_scale=0.28),
        _rubble(1, (1.5, 1.0, 0.55), (1.75, 1.05, 0.60), (0.0, 18.0, 30.0), palette.CHARRED),
        _rubble(2, (1.2, 1.3, 0.5), (-1.85, -0.95, 0.55), (12.0, 0.0, -22.0), palette.SCORCH),
        _rubble(3, (0.9, 0.8, 0.7), (0.35, -2.05, 0.65), (0.0, -10.0, 48.0), palette.CHARRED),
        # The pole, down and bent across the rubble.
        p.cylinder("Pole", radius=0.05, height=1.6, at=(-0.55, 1.75, 0.85),
                   rot=(74.0, 0.0, 18.0), color=palette.METAL_DARK, segments=8),
    ]
    root = p.join(ASSET_DESTROYED, parts)
    # Still on the plinth, which is now most of what is left. The stump comes up to
    # 1.62 m and the banner starts at 1.85 m, so the flag stands clear of the
    # wreckage with its staff disappearing into it.
    _mount(root)
    return root


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
