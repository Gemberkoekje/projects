"""RF_Structure_Turret - the automated gun emplacement, in three destruction states.

Brief: small, low, and the one piece of map furniture that is **team-tinted**. Every
other structure in the spec is neutral because both sides want it; a turret is the
opposite - which side it belongs to is the single most important thing a player
reads about it, from across the map, before deciding whether to drive that way.

Silhouette: a hexagonal concrete emplacement about 2.6 m across with a squat
armoured head on top, well under a building's height so it never becomes cover in
its own right. It has to read apart from the fuel depot (a tall cylinder) and the
ammo depot (a low crate stack) at a glance: what distinguishes it from both is the
**barrel**, which is the only horizontal spike on any structure in the game.

The head is a separate ``Turret`` child pivoted on its ring, exactly as on the tank,
so Unity traverses it in place - see ``IronFlag.Destruction.AutoTurret``. Built
facing -Y, like every other asset, so it arrives in Unity facing +Z. The ``Muzzle``
material group hangs off the *head* rather than the base, so the firing point
traverses with the gun and cannot disagree with where the barrel is pointing.

**Only the intact and damaged states have a ``Turret``.** The destroyed one is a
cracked ring with the head lying beside it, and having no traversing part is how
the rubble is silent by construction rather than by a check: ``AutoTurret`` looks
for the node inside whichever state is showing and finds nothing.

Team trim goes on the **base**, not the head, for the reason the tank's does: the
head rotates, and a team stripe that swings around with the gun stops being a
readable marker of whose emplacement this is. The base carries a full ring of it,
which is the largest upward-facing surface here and the one the top-down camera
sees most of.
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

#: Where the head turns about, on the top face of the plinth. Unity rotates the part
#: around this point, so it has to be the middle of the ring rather than the middle
#: of the geometry - the barrel sticks out one side and would swing the head off its
#: own base.
_TURRET_RING = (0.0, 0.0, 1.05)

_PAD_RADIUS = 1.30
_PAD_HEIGHT = 0.30
_PLINTH_HEIGHT = 0.75
_HEAD_Z = 1.42


def _pad(color):
    """Build the hexagonal apron the emplacement stands on.

    Args:
        color: Linear RGBA for the concrete.

    Returns:
        The pad object.
    """
    return p.cylinder("Pad", radius=_PAD_RADIUS, height=_PAD_HEIGHT,
                      at=(0.0, 0.0, _PAD_HEIGHT * 0.5), color=color, segments=6)


def _plinth(color, height=_PLINTH_HEIGHT):
    """Build the tapered concrete body between the pad and the gun.

    Args:
        color: Linear RGBA for the concrete.
        height: How much of it is left standing, in metres.

    Returns:
        The plinth object.
    """
    return p.cone("Plinth", radius_bottom=1.05, radius_top=0.86, height=height,
                  at=(0.0, 0.0, _PAD_HEIGHT + (height * 0.5)), color=color, segments=6)


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
        p.box("Housing", size=(1.05, 1.15, 0.52), at=(0.0, 0.10, _HEAD_Z), color=color),
        # Sloped front plate. The wedge falls away toward +Y, so it is turned to face
        # front like the tank's glacis.
        p.wedge("Mantlet", size=(1.05, 0.45, 0.52), at=(0.0, -0.70, _HEAD_Z),
                rot=(0.0, 0.0, 180.0), color=palette.METAL_DARK),
        p.cylinder("Barrel", radius=0.09, height=barrel_length,
                   at=(0.0, barrel_at, _HEAD_Z), color=barrel_color, segments=8, axis="Y"),
        # A counterweight box at the back, so the head is not symmetrical and a player
        # can read which way it is pointing even when the barrel is edge-on.
        p.box("Breech", size=(0.62, 0.40, 0.34), at=(0.0, 0.78, _HEAD_Z + 0.05),
              color=palette.METAL_DARK),
    ]


def _trim_ring(z):
    """Build the team-coloured band around the base.

    Args:
        z: Height of the band's centre, in metres.

    Returns:
        The trim parts, in build order.
    """
    return [
        p.cylinder("Collar", radius=_PAD_RADIUS * 0.92, height=0.10, at=(0.0, 0.0, z),
                   group=material.TEAM, segments=6),
    ]


def build_intact():
    """Build the working emplacement.

    Returns:
        The joined root object, with the head and team trim parented to it.
    """
    scene.begin(ASSET_INTACT)

    base_parts = [
        _pad(palette.CONCRETE),
        _plinth(palette.CONCRETE),
        # Sandbag course around the foot, which is what says "emplacement" rather than
        # "lamp post" at the size this thing has to read at.
        p.box("SandbagN", size=(1.55, 0.34, 0.30), at=(0.0, 1.02, 0.44), color=palette.SAND),
        p.box("SandbagS", size=(1.55, 0.34, 0.30), at=(0.0, -1.02, 0.44), color=palette.SAND),
        p.box("SandbagE", size=(0.34, 1.55, 0.30), at=(1.02, 0.0, 0.44), color=palette.SAND),
        p.box("SandbagW", size=(0.34, 1.55, 0.30), at=(-1.02, 0.0, 0.44), color=palette.SAND),
    ]
    root = p.join(ASSET_INTACT, base_parts)

    turret = p.join(_TURRET_NAME, _head(palette.HULL, palette.METAL_DARK))
    p.set_pivot(turret, _TURRET_RING)
    p.attach(turret, root)
    p.attach_group(turret, material.MUZZLE, [
        p.cylinder("Tip", radius=0.11, height=0.20, at=(0.0, -1.85, _HEAD_Z),
                   group=material.MUZZLE, segments=8, axis="Y"),
    ])

    p.attach_group(root, material.TEAM, _trim_ring(_PAD_HEIGHT + 0.05))
    return root


def build_damaged():
    """Build the emplacement after a hit: armour holed, barrel shortened, still firing.

    Returns:
        The joined root object, with the head and team trim parented to it.
    """
    scene.begin(ASSET_DAMAGED)

    base_parts = [
        _pad(palette.CONCRETE),
        _plinth(palette.CONCRETE),
        # Two courses of sandbags burst and one gone, so the damage reads from the
        # side the shot came from rather than evenly all round.
        p.box("SandbagN", size=(1.55, 0.34, 0.30), at=(0.0, 1.02, 0.44), color=palette.SAND),
        p.box("SandbagE", size=(0.34, 1.55, 0.30), at=(1.02, 0.0, 0.44), color=palette.SAND),
        p.box("SandbagBurst", size=(0.70, 0.34, 0.22), at=(-0.55, -1.10, 0.38),
              rot=(0.0, 9.0, -24.0), color=palette.CHARRED),
        p.box("Scar", size=(0.55, 0.30, 0.55), at=(-0.86, -0.42, 0.80),
              rot=(0.0, 12.0, 18.0), color=palette.CHARRED),
    ]
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

    p.attach_group(root, material.TEAM, _trim_ring(_PAD_HEIGHT + 0.05))
    return root


def build_destroyed():
    """Build the wrecked emplacement: a cracked ring with the head lying beside it.

    There is deliberately no ``Turret`` child here. A destroyed turret has nothing to
    traverse, which is what makes the rubble silent by construction rather than by a
    check somebody could forget to write.

    Returns:
        The joined root object, with team trim parented to it.
    """
    scene.begin(ASSET_DESTROYED)

    parts = [
        _pad(palette.CHARRED),
        # A stump of the plinth, sheared off well below where the ring was.
        _plinth(palette.CHARRED, height=0.32),
        # The head, on the apron where it landed, upside down and a little way off.
        p.box("HousingFallen", size=(1.00, 1.10, 0.50), at=(1.25, -0.85, 0.44),
              rot=(0.0, 22.0, 34.0), color=palette.CHARRED),
        # The barrel, bent across the wreck. This is the one piece that still says
        # "turret" once everything else is a burnt heap.
        p.cylinder("BarrelFallen", radius=0.09, height=1.60, at=(-0.75, 0.95, 0.42),
                   rot=(6.0, 78.0, 26.0), color=palette.METAL_DARK, segments=8, axis="Y"),
        p.box("Rubble1", size=(0.62, 0.50, 0.34), at=(-0.35, -1.05, 0.44),
              rot=(0.0, 14.0, -18.0), color=palette.SCORCH),
        p.box("Rubble2", size=(0.48, 0.55, 0.30), at=(0.55, 0.90, 0.42),
              rot=(10.0, 0.0, 40.0), color=palette.CHARRED),
    ]
    root = p.join(ASSET_DESTROYED, parts)

    # The team band survives, scorched but readable: a wrecked emplacement is still
    # somebody's, and a player driving past one needs to know whose ground they are on.
    p.attach_group(root, material.TEAM, _trim_ring(_PAD_HEIGHT + 0.05))
    return root


BUILDERS = {
    ASSET_INTACT: build_intact,
    ASSET_DAMAGED: build_damaged,
    ASSET_DESTROYED: build_destroyed,
}
