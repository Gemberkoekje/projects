"""RF_Vehicle_Tank - the general-purpose damage dealer.

Silhouette brief (asset spec): 5.5m x 3.2m x 2.4m, the **widest hull** of the four,
with a rotating turret. Width is the whole point of this one - it is what separates
it from the ASV at a glance, so the tracks are deliberately the outermost thing on
the vehicle and nothing else is allowed past them.

Turret is a separate ``Turret`` child with its origin on the turret ring, so Unity
rotates it in place. Built facing -Y; origin on the ground at the hull center.
"""

from rf import palette
from rf import primitives as p
from rf import scene
from rf import material

ASSET_NAME = "RF_Vehicle_Tank"

_TRACK_X = 1.32
_TURRET_RING = (0.0, 0.55, 1.64)


def build():
    """Build the tank and return its root object.

    Returns:
        The joined hull object, with the turret and team trim parented to it.
    """
    scene.begin(ASSET_NAME)

    hull_parts = [
        # Tracks. The widest part of any vehicle in the roster.
        p.box("TrackL", size=(0.55, 5.20, 0.80), at=(_TRACK_X, 0.0, 0.40),
              color=palette.RUBBER),
        p.box("TrackR", size=(0.55, 5.20, 0.80), at=(-_TRACK_X, 0.0, 0.40),
              color=palette.RUBBER),
        # Hull tub between the tracks.
        p.box("Hull", size=(2.30, 4.40, 0.55), at=(0.0, 0.05, 0.95), color=palette.HULL),
        # Sloped glacis. The wedge falls away toward +Y, so it is turned to face front.
        p.wedge("Glacis", size=(2.30, 0.90, 0.55), at=(0.0, -2.45, 0.95),
                rot=(0.0, 0.0, 180.0), color=palette.HULL),
        # Engine deck the turret sits on.
        p.box("Deck", size=(2.10, 3.20, 0.42), at=(0.0, 0.45, 1.43), color=palette.HULL),
    ]
    root = p.join(ASSET_NAME, hull_parts)

    turret_parts = [
        p.box("TurretBody", size=(1.70, 2.10, 0.60), at=(0.0, 0.55, 1.94), color=palette.HULL),
        p.box("Mantlet", size=(0.62, 0.45, 0.50), at=(0.0, -0.62, 1.90),
              color=palette.METAL_DARK),
        p.cylinder("Barrel", radius=0.12, height=2.80, at=(0.0, -0.85, 1.90),
                   color=palette.METAL_DARK, segments=8, axis="Y"),
        p.cylinder("Cupola", radius=0.32, height=0.24, at=(0.35, 1.05, 2.28),
                   color=palette.METAL, segments=8),
    ]
    turret = p.join("Turret", turret_parts)
    p.set_pivot(turret, _TURRET_RING)
    p.attach(turret, root)
    p.attach_group(turret, material.MUZZLE, [
        p.cylinder("Tip", radius=0.14, height=0.22, at=(0.0, -2.15, 1.90),
                   group=material.MUZZLE, segments=8, axis="Y"),
    ])

    # Team trim sits on upward-facing surfaces, because the game camera is top-down.
    # Nothing goes on the turret: it rotates independently of the hull.
    trim_parts = [
        p.box("DeckStripeL", size=(0.35, 2.80, 0.06), at=(0.75, 0.45, 1.67), group=material.TEAM),
        p.box("DeckStripeR", size=(0.35, 2.80, 0.06), at=(-0.75, 0.45, 1.67), group=material.TEAM),
        p.box("TrackStripeL", size=(0.50, 3.00, 0.06), at=(_TRACK_X, 0.0, 0.83),
              group=material.TEAM),
        p.box("TrackStripeR", size=(0.50, 3.00, 0.06), at=(-_TRACK_X, 0.0, 0.83),
              group=material.TEAM),
        p.box("PlateRear", size=(1.10, 0.08, 0.35), at=(0.0, 2.29, 1.05), group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, trim_parts)

    p.attach_group(root, material.LIGHT_FRONT, [
        p.box("HeadL", size=(0.22, 0.06, 0.16), at=(0.75, -2.92, 1.05),
              group=material.LIGHT_FRONT),
        p.box("HeadR", size=(0.22, 0.06, 0.16), at=(-0.75, -2.92, 1.05),
              group=material.LIGHT_FRONT),
    ])
    p.attach_group(root, material.LIGHT_REAR, [
        p.box("TailL", size=(0.20, 0.06, 0.14), at=(0.75, 2.28, 1.05),
              group=material.LIGHT_REAR),
        p.box("TailR", size=(0.20, 0.06, 0.14), at=(-0.75, 2.28, 1.05),
              group=material.LIGHT_REAR),
    ])

    return root


BUILDERS = {ASSET_NAME: build}
