"""RF_Vehicle_ASV - the slow, heavily armored rocket platform.

Silhouette brief (asset spec): the **most armored-looking** of the four, with a
rocket launcher that rotates and elevates.

It is deliberately the most *compact* vehicle after the jeep rather than the
largest. An earlier pass built it taller and longer than the tank, following the
spec's dimension table literally, and in the editor it read as the heavy of the
roster. Bulk is now carried by the sloped armor and six road wheels, not by size,
and the tank keeps both "widest" and "tallest".

The launcher is a **loaded rack, not a turret**. Two protruding tubes on a raised
mount read unmistakably as an anti-air gun - a unit type this game does not have -
so the weapon is now a closed box with a dark open mouth and four missile noses
sitting in it. The thing you see is ordnance waiting to be fired, which is what an
armored support vehicle should look like.

The launcher is the only separate mesh the asset spec asks for on this vehicle, so
the wheels are joined into the hull rather than being animated parts.

Built facing -Y; origin on the ground at the hull center.
"""

from rf import material
from rf import palette
from rf import primitives as p
from rf import scene

ASSET_NAME = "RF_Vehicle_ASV"

_WHEEL_X = 1.24
_WHEEL_RADIUS = 0.40
_LAUNCHER_RING = (0.0, 0.85, 1.79)


def build():
    """Build the ASV and return its root object.

    Returns:
        The joined hull object, with the launcher, lights and team trim parented to it.
    """
    scene.begin(ASSET_NAME)

    hull_parts = [
        p.box("Hull", size=(2.20, 4.10, 0.62), at=(0.0, 0.05, 0.86), color=palette.HULL),
        # Sloped upper armor - the shape that carries "heaviest thing on the field"
        # now that the vehicle itself is not the biggest.
        p.pyramid("Armor", size=(2.20, 3.50, 0.62), at=(0.0, 0.10, 1.48),
                  apex_size=(1.55, 2.70), color=palette.HULL),
        p.wedge("Glacis", size=(2.20, 1.00, 0.62), at=(0.0, -2.15, 0.86),
                rot=(0.0, 0.0, 180.0), color=palette.HULL),
    ]

    for index, y in enumerate((-1.40, 0.0, 1.40)):
        for side, x in (("L", _WHEEL_X), ("R", -_WHEEL_X)):
            hull_parts.append(p.cylinder(
                f"Wheel{index}{side}", radius=_WHEEL_RADIUS, height=0.32,
                at=(x, y, _WHEEL_RADIUS), color=palette.RUBBER, segments=10, axis="X"))

    root = p.join(ASSET_NAME, hull_parts)

    launcher_parts = [
        # Traverse ring the rack turns on.
        p.cylinder("Ring", radius=0.40, height=0.08, at=(0.0, 0.85, 1.83),
                   color=palette.METAL_DARK, segments=10),
        # Closed housing. No barrels, no pedestal.
        p.box("Housing", size=(1.30, 0.90, 0.46), at=(0.0, 0.85, 2.10),
              color=palette.METAL_DARK),
    ]
    # Four missile noses sitting in the rack, protruding just past the mouth.
    for index, (x, z) in enumerate(((0.28, 2.02), (-0.28, 2.02), (0.28, 2.18), (-0.28, 2.18))):
        launcher_parts.append(p.cylinder(
            f"Missile{index}", radius=0.09, height=0.34, at=(x, 0.28, z),
            color=palette.METAL, segments=8, axis="Y"))

    launcher = p.join("Launcher", launcher_parts)
    p.set_pivot(launcher, _LAUNCHER_RING)
    p.attach(launcher, root)

    # The mouth is the muzzle group: a dark recess the missiles sit in, and the
    # renderer weapon VFX can be anchored to. It traverses with the rack.
    p.attach_group(launcher, material.MUZZLE, [
        p.box("Mouth", size=(1.12, 0.12, 0.34), at=(0.0, 0.42, 2.10),
              group=material.MUZZLE),
    ])

    trim_parts = [
        p.box("DeckStripeL", size=(0.28, 2.40, 0.06), at=(0.56, 0.20, 1.82),
              group=material.TEAM),
        p.box("DeckStripeR", size=(0.28, 2.40, 0.06), at=(-0.56, 0.20, 1.82),
              group=material.TEAM),
        p.box("FlankL", size=(0.08, 1.50, 0.28), at=(1.14, 0.20, 1.00), group=material.TEAM),
        p.box("FlankR", size=(0.08, 1.50, 0.28), at=(-1.14, 0.20, 1.00), group=material.TEAM),
        p.box("PlateRear", size=(1.10, 0.08, 0.36), at=(0.0, 2.14, 1.00), group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, trim_parts)

    p.attach_group(root, material.LIGHT_FRONT, [
        p.box("HeadL", size=(0.20, 0.06, 0.14), at=(0.70, -2.67, 1.00),
              group=material.LIGHT_FRONT),
        p.box("HeadR", size=(0.20, 0.06, 0.14), at=(-0.70, -2.67, 1.00),
              group=material.LIGHT_FRONT),
    ])
    p.attach_group(root, material.LIGHT_REAR, [
        p.box("TailL", size=(0.18, 0.06, 0.13), at=(0.75, 2.13, 1.00),
              group=material.LIGHT_REAR),
        p.box("TailR", size=(0.18, 0.06, 0.13), at=(-0.75, 2.13, 1.00),
              group=material.LIGHT_REAR),
    ])

    return root


BUILDERS = {ASSET_NAME: build}
