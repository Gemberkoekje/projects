"""RF_Structure_Turret - the automated gun tower, in three destruction states.

Brief: a **tower**, and the one piece of map furniture that is **team-tinted**. Every
other structure in the spec is neutral because both sides want it; a turret is the
opposite - which side it belongs to is the single most important thing a player reads
about it, from across the map, before deciding whether to drive that way.

Silhouette: a hexagonal shaft four metres tall on a low apron, flaring at the top into
a gallery with a squat armoured head on it. It has to read apart from the fuel depot (a
tall cylinder) and the ammo depot (a low crate stack) at a glance, and apart from the
flag tower, which is a broad pyramid at 6.2 m. What distinguishes it from all three is
the **barrel**, which is the only horizontal spike on any structure in the game.

**Four metres, which is exactly twice a wall.** This asset used to be 1.68 m and it was
built that way on purpose - "well under a building's height so it never becomes cover in
its own right". Two things retired that reasoning. The first is that walls arrived at
2.0 m, so the gun *tower* was shorter than the fence beside it and read as a bollard.
The second is that the original reason was never true: a round in this game sweeps a
``CombatPlane`` column from 0.5 m to 30 m regardless of anything's height, so a turret's
base has always been cover and always blocked fire, and making the thing above it taller
costs nothing that was not already being paid. Height here is silhouette, and only
silhouette.

Read the three heights together: a wall stops a jeep and hides one, a gun tower doubles
it and is unmistakably a built defence, and the flag tower is half again as tall as that
and is the thing the whole map is about.

The head is a separate ``Turret`` child pivoted on its ring, exactly as on the tank, so
Unity traverses it in place - see ``IronFlag.Destruction.AutoTurret``. Built facing -Y,
like every other asset, so it arrives in Unity facing +Z. The ``Muzzle`` material group
hangs off the *head* rather than the gallery, so the firing point traverses with the gun
and cannot disagree with where the barrel is pointing.

**Only the intact and damaged states have a ``Turret``.** The destroyed one is a cracked
apron with the head lying beside it, and having no traversing part is how the rubble is
silent by construction rather than by a check: ``AutoTurret`` looks for the node inside
whichever state is showing and finds nothing.

Team trim goes on the **gallery and the apron**, never on the head, for the reason the
tank's does: the head rotates, and a team stripe that swings around with the gun stops
being a readable marker of whose emplacement this is. The gallery ring is the important
one and it is new with the tower - the camera looks down at 58 degrees, and on a
four-metre shaft the apron is the part the tower itself hides. A collar directly under
the head is the one team-coloured surface that cannot be occluded by the structure
carrying it.
"""

from rf import material
from rf import palette
from rf import primitives as p
from rf import scene

ASSET_INTACT = "RF_Structure_Turret_Intact"
ASSET_DAMAGED = "RF_Structure_Turret_Damaged"
ASSET_DESTROYED = "RF_Structure_Turret_Destroyed"

#: Matches IronFlag.Destruction.AutoTurret.TurretNodeName, and the tank's part of the
#: same name. Renaming one without the other leaves an emplacement that never fires.
_TURRET_NAME = "Turret"

#: Overall height, and the number the whole asset is built around: twice ``prop_wall``'s
#: 2.00 m, and comfortably under ``structure_flagtower``'s 6.20 m. See the module
#: docstring for why it is not the 1.68 m this used to be.
_HEIGHT = 4.00

_PAD_RADIUS = 1.30
_PAD_HEIGHT = 0.30

#: Where the shaft stops and the gallery starts. Chosen so the gallery's *top* lands on
#: the underside of the housing rather than near it: the head is a separate object and
#: nothing would complain about a gap, so the first build of this tower left the gun
#: floating 0.16 m above the corbel holding it up.
_SHAFT_TOP = 3.06
_SHAFT_FOOT_RADIUS = 1.05
_SHAFT_HEAD_RADIUS = 0.74

#: The corbel the head stands on. Much wider than the top of the shaft it sits on, which
#: is what turns a post into a tower: a shaft that simply carried a gun would read as a
#: lamp standard from the gameplay camera.
_GALLERY_HEIGHT = 0.32
_GALLERY_RADIUS = 1.12
_GALLERY_TOP = _SHAFT_TOP + _GALLERY_HEIGHT

#: The armoured box the gun is in, and the piece that decides the total height.
_HOUSING_HEIGHT = 0.62
_HEAD_Z = _HEIGHT - (_HOUSING_HEIGHT * 0.5)

#: Where the head turns about. Unity rotates the part around this point, so it has to be
#: the middle of the ring rather than the middle of the geometry - the barrel sticks out
#: one side and would swing the head off its own gallery.
_TURRET_RING = (0.0, 0.0, _GALLERY_TOP)

#: Height of the team bands, and where the two of them sit.
_COLLAR_HEIGHT = 0.12
_APRON_COLLAR_Z = _PAD_HEIGHT + 0.05
_GALLERY_COLLAR_Z = _GALLERY_TOP - (_GALLERY_HEIGHT * 0.5)

#: Ceiling every piece of the destroyed state's geometry stays under, in metres - about
#: knee height, and ``prop_wall``'s number for the same reason. ``Destructible`` switches
#: a destroyed structure's colliders off the moment it is shown, so nothing here has a
#: hitbox however tall it is drawn; this constant is what stops the *picture* disagreeing
#: with that. It matters more for a tower than it did for a wall: four metres of
#: collapsed masonry is the most tempting thing on the map to draw as a heap, and a heap
#: a player reads as cover is cover they get shot through.
_DESTROYED_CEILING = 0.45


def _pad(color):
    """Build the hexagonal apron the tower stands on.

    Args:
        color: Linear RGBA for the concrete.

    Returns:
        The pad object.
    """
    return p.cylinder("Pad", radius=_PAD_RADIUS, height=_PAD_HEIGHT,
                      at=(0.0, 0.0, _PAD_HEIGHT * 0.5), color=color, segments=6)


def _shaft(color, top=_SHAFT_TOP):
    """Build the tapered hexagonal column between the apron and the gallery.

    Args:
        color: Linear RGBA for the concrete.
        top: Height the shaft is standing to, in metres.

    Returns:
        The shaft object.
    """
    height = max(0.05, top - _PAD_HEIGHT)
    # The taper is measured to wherever this one actually stops, so a shortened shaft is
    # a shaft cut off partway up rather than the whole cone squashed into less room.
    reach = (top - _PAD_HEIGHT) / (_SHAFT_TOP - _PAD_HEIGHT)
    return p.cone("Shaft",
                  radius_bottom=_SHAFT_FOOT_RADIUS,
                  radius_top=_SHAFT_FOOT_RADIUS
                  + ((_SHAFT_HEAD_RADIUS - _SHAFT_FOOT_RADIUS) * reach),
                  height=height,
                  at=(0.0, 0.0, _PAD_HEIGHT + (height * 0.5)),
                  color=color, segments=6)


def _gallery(color, radius=_GALLERY_RADIUS):
    """Build the corbel the head stands on.

    Args:
        color: Linear RGBA for the concrete.
        radius: How far it oversails the shaft, in metres.

    Returns:
        The gallery object.
    """
    return p.cylinder("Gallery", radius=radius, height=_GALLERY_HEIGHT,
                      at=(0.0, 0.0, _SHAFT_TOP + (_GALLERY_HEIGHT * 0.5)),
                      color=color, segments=6)


def _head(color, barrel_color, barrel_length=1.70, barrel_at=-0.95):
    """Build the traversing gun head, without its muzzle.

    Args:
        color: Linear RGBA for the armour.
        barrel_color: Linear RGBA for the barrel.
        barrel_length: Length of the barrel in metres.
        barrel_at: Y offset of the barrel's centre; negative points it forward.

    Returns:
        The head's parts, in build order.
    """
    return [
        p.box("Housing", size=(1.05, 1.15, _HOUSING_HEIGHT),
              at=(0.0, 0.10, _HEAD_Z), color=color),
        # Sloped front plate. The wedge falls away toward +Y, so it is turned to face
        # front like the tank's glacis.
        p.wedge("Mantlet", size=(1.05, 0.45, _HOUSING_HEIGHT), at=(0.0, -0.70, _HEAD_Z),
                rot=(0.0, 0.0, 180.0), color=palette.METAL_DARK),
        p.cylinder("Barrel", radius=0.09, height=barrel_length,
                   at=(0.0, barrel_at, _HEAD_Z), color=barrel_color, segments=8, axis="Y"),
        # A counterweight box at the back, so the head is not symmetrical and a player
        # can read which way it is pointing even when the barrel is edge-on.
        p.box("Breech", size=(0.62, 0.40, 0.34), at=(0.0, 0.78, _HEAD_Z + 0.06),
              color=palette.METAL_DARK),
    ]


def _trim(apron_z=_APRON_COLLAR_Z, gallery_z=_GALLERY_COLLAR_Z,
          gallery_radius=_GALLERY_RADIUS + 0.06):
    """Build the team-coloured bands around the apron and under the head.

    Two rings rather than one, and the upper one is the one that matters. The camera
    looks down at 58 degrees; on a four-metre shaft the apron ring is the part the tower
    itself stands in front of, and a collar directly under the head is the only
    team-coloured surface nothing can occlude.

    Args:
        apron_z: Height of the lower band's centre, in metres.
        gallery_z: Height of the upper band's centre, in metres.
        gallery_radius: Radius of the upper band, in metres.

    Returns:
        The trim parts, in build order.
    """
    return [
        p.cylinder("Collar", radius=_PAD_RADIUS * 0.92, height=_COLLAR_HEIGHT,
                   at=(0.0, 0.0, apron_z), group=material.TEAM, segments=6),
        p.cylinder("GalleryBand", radius=gallery_radius, height=_COLLAR_HEIGHT,
                   at=(0.0, 0.0, gallery_z), group=material.TEAM, segments=6),
    ]


def _sandbags(color, sides=("N", "S", "E", "W")):
    """Build the sandbag course round the foot.

    What says "emplacement" rather than "monument" at the size this thing reads at, and
    the reason the tower keeps a wide apron instead of rising straight off the ground.

    Args:
        color: Linear RGBA for the sacking.
        sides: Which of the four courses are still there.

    Returns:
        The sandbag parts, in build order.
    """
    laid = {
        "N": p.box, "S": p.box, "E": p.box, "W": p.box,
    }
    at = {
        "N": ((1.55, 0.34, 0.30), (0.0, 1.02, 0.44)),
        "S": ((1.55, 0.34, 0.30), (0.0, -1.02, 0.44)),
        "E": ((0.34, 1.55, 0.30), (1.02, 0.0, 0.44)),
        "W": ((0.34, 1.55, 0.30), (-1.02, 0.0, 0.44)),
    }
    return [
        laid[side](f"Sandbag{side}", size=at[side][0], at=at[side][1], color=color)
        for side in sides
    ]


def build_intact():
    """Build the working gun tower.

    Returns:
        The joined root object, with the head and team trim parented to it.
    """
    scene.begin(ASSET_INTACT)

    base_parts = [
        _pad(palette.CONCRETE),
        _shaft(palette.CONCRETE),
        _gallery(palette.CONCRETE),
    ] + _sandbags(palette.SAND)
    root = p.join(ASSET_INTACT, base_parts)

    turret = p.join(_TURRET_NAME, _head(palette.HULL, palette.METAL_DARK))
    p.set_pivot(turret, _TURRET_RING)
    p.attach(turret, root)
    p.attach_group(turret, material.MUZZLE, [
        p.cylinder("Tip", radius=0.11, height=0.20, at=(0.0, -1.85, _HEAD_Z),
                   group=material.MUZZLE, segments=8, axis="Y"),
    ])

    p.attach_group(root, material.TEAM, _trim())
    return root


def build_damaged():
    """Build the tower after a hit: gallery holed, shaft scarred, barrel shortened.

    The shaft keeps its full height, deliberately, and that is the one thing this state
    may not change: the head sits on the gallery, and a shortened shaft would drop a
    working gun through its own tower. What a damaged tower has lost is armour and a
    corner of the corbel holding it up.

    Returns:
        The joined root object, with the head and team trim parented to it.
    """
    scene.begin(ASSET_DAMAGED)

    base_parts = [
        _pad(palette.CONCRETE),
        _shaft(palette.CONCRETE),
        # The corbel has taken the hit and lost most of its oversail on one side, which
        # is the readable version of "the next one brings it down".
        _gallery(palette.CHARRED, radius=_GALLERY_RADIUS * 0.82),
        # A bite out of the shaft, high up where the shell went in. All on one side on
        # purpose, so the damage reads as the direction the shot came from rather than
        # as even wear - the same argument the wall and the door both make.
        p.box("Scar", size=(0.62, 0.34, 0.78), at=(-0.62, -0.52, 2.18),
              rot=(0.0, 12.0, 18.0), color=palette.CHARRED),
        # And a lower one, so the tower reads as hit twice rather than dented once.
        p.box("Chip", size=(0.44, 0.30, 0.46), at=(0.66, -0.44, 1.15),
              rot=(0.0, -9.0, 14.0), color=palette.CHARRED),
        # Two courses of sandbags burst and one gone.
        p.box("SandbagBurst", size=(0.70, 0.34, 0.22), at=(-0.55, -1.10, 0.38),
              rot=(0.0, 9.0, -24.0), color=palette.CHARRED),
    ] + _sandbags(palette.SAND, sides=("N", "E"))
    root = p.join(ASSET_DAMAGED, base_parts)

    # The barrel has lost its last half-metre, which is the readable version of "this
    # one is nearly gone" - and it is cosmetic: reach is WeaponTuning's, not the mesh's.
    turret = p.join(_TURRET_NAME, _head(
        palette.CHARRED, palette.CHARRED, barrel_length=1.15, barrel_at=-0.70))
    p.set_pivot(turret, _TURRET_RING)
    p.attach(turret, root)
    p.attach_group(turret, material.MUZZLE, [
        p.cylinder("Tip", radius=0.11, height=0.20, at=(0.0, -1.32, _HEAD_Z),
                   group=material.MUZZLE, segments=8, axis="Y"),
    ])

    p.attach_group(root, material.TEAM, _trim(gallery_radius=_GALLERY_RADIUS * 0.88))
    return root


def build_destroyed():
    """Build the wreck: a cracked apron with four metres of tower down across it.

    There is deliberately no ``Turret`` child here. A destroyed tower has nothing to
    traverse, which is what makes the rubble silent by construction rather than by a
    check somebody could forget to write.

    Nothing here stands higher than ``_DESTROYED_CEILING``. That is a stronger constraint
    for a tower than it was for a wall and it is the right one: the taller the thing that
    fell, the more tempting it is to draw the wreck as a heap - and a heap is exactly
    what a player reads as cover, right up until the round they were sheltering from
    goes through it. The tower does not pile up; it comes down flat and spreads.

    Returns:
        The joined root object, with team trim parented to it.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        _pad(palette.CHARRED),
        # A stump of the shaft, sheared off just above the apron.
        _shaft(palette.CHARRED, top=_PAD_HEIGHT + 0.14),
        # The head, on its side on the apron where it landed. Flat rather than tipped up,
        # because a box on its corner is the single easiest way to break the ceiling.
        #
        # Every loose piece below is centred on its own *measured* half-range rather than
        # on half its thickness, and the two are not the same number once a box is
        # rotated: a four-degree tilt on a 1.70 m course moves its bottom by 0.06 m,
        # which is four times what the box's own thickness suggests. The first build of
        # this state put five of these seven underground by between 6 and 23 millimetres.
        p.box("HousingFallen", size=(1.05, 1.15, 0.32), at=(1.30, -0.82, 0.206),
              rot=(0.0, 5.0, 34.0), color=palette.CHARRED),
        # The barrel, thrown clear across the wreck. This is the one piece that still
        # says "turret" once everything else is a burnt heap, so it survives whole.
        p.cylinder("BarrelFallen", radius=0.09, height=1.60, at=(-0.80, 0.95, 0.100),
                   rot=(0.0, 84.0, 26.0), color=palette.METAL_DARK, segments=8, axis="Y"),
        # Two courses of the shaft, down flat where they toppled. A four-metre tower
        # leaves more masonry than a wall segment does, and it lands further out.
        p.box("CourseFallen", size=(1.70, 0.86, 0.30), at=(-1.62, -1.05, 0.210),
              rot=(0.0, 4.0, -62.0), color=palette.SCORCH),
        p.box("Lintel", size=(1.25, 0.62, 0.26), at=(0.42, 1.62, 0.196),
              rot=(0.0, 6.0, 18.0), color=palette.CHARRED),
        p.box("Rubble1", size=(0.62, 0.50, 0.26), at=(-0.35, -1.55, 0.157),
              rot=(6.0, 0.0, -18.0), color=palette.SCORCH),
        p.box("Rubble2", size=(0.48, 0.55, 0.24), at=(1.66, 0.72, 0.163),
              rot=(9.0, 0.0, 40.0), color=palette.CHARRED),
        p.box("Rubble3", size=(0.40, 0.44, 0.22), at=(-1.78, 0.34, 0.135),
              rot=(0.0, 7.0, 12.0), color=palette.SCORCH),
    ]
    root = p.join(ASSET_DESTROYED, parts)

    # The team band survives, scorched but readable: a wrecked emplacement is still
    # somebody's, and a player driving past one needs to know whose ground they are on.
    # Only the apron's - the gallery it was under is lying across the apron in pieces.
    band = _DESTROYED_CEILING - (_COLLAR_HEIGHT * 0.5) - 0.015
    p.attach_group(root, material.TEAM, [
        p.cylinder("Collar", radius=_PAD_RADIUS * 0.92, height=_COLLAR_HEIGHT,
                   at=(0.0, 0.0, band), group=material.TEAM, segments=6),
    ])
    return root


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
