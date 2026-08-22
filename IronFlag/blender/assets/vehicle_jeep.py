"""RF_Vehicle_Jeep - the reference asset for the pipeline.

This is the model to copy when adding anything else: it exercises every part of
the pipeline exactly once - palette colors, a team-accent decal, a joined hull,
separate moving parts with their own pivots, and the ``BUILDERS`` contract the
build script looks for.

Silhouette brief (asset spec): 4.0m x 1.8m x 1.6m, the lowest and most open-frame
of the four vehicles, wheels as separate meshes. It reads against the others by
being the only one with an open roll cage instead of an enclosed body.

Built facing -Y so it arrives in Unity facing +Z. Origin sits on the ground at
the center of the wheelbase.

The spec's ~10-15 primitive ceiling is about whether a *silhouette* reads; the four
flat team-trim plates here add no silhouette at all, so they are counted separately.
Eleven hull pieces and four wheels carry the shape.

The trim is a separate ``TeamTrim`` child rather than extra faces on the hull, so
Unity can recolor it by swapping one renderer's material.
"""

from rf import palette
from rf import primitives as p
from rf import scene
from rf import material

ASSET_NAME = "RF_Vehicle_Jeep"

_WHEEL_RADIUS = 0.38
_WHEEL_WIDTH = 0.28
_WHEEL_X = 0.75
_FRONT_AXLE_Y = -1.25
_REAR_AXLE_Y = 1.30


def build():
    """Build the jeep and return its root object.

    Returns:
        The joined hull object, with the four wheels parented to it.
    """
    scene.begin(ASSET_NAME)

    hull_parts = [
        # Chassis pan - the full length of the vehicle, everything else sits on it.
        p.box("Chassis", size=(1.70, 3.50, 0.30), at=(0.0, 0.0, 0.55), color=palette.HULL),
        # Front bumper, deliberately the widest point so the nose reads first.
        p.box("Bumper", size=(1.78, 0.22, 0.26), at=(0.0, -1.88, 0.55), color=palette.METAL_DARK),
        # Bonnet.
        p.box("Bonnet", size=(1.55, 1.10, 0.34), at=(0.0, -1.15, 0.87), color=palette.HULL),
        # Raked windscreen.
        p.box("Windscreen", size=(1.45, 0.07, 0.58), at=(0.0, -0.56, 1.30),
              rot=(-18.0, 0.0, 0.0), color=palette.GLASS),
        # Roll cage: two posts and a cross bar, the open-frame silhouette cue.
        p.cylinder("RollPostL", radius=0.055, height=0.80, at=(-0.72, 0.62, 1.18),
                   color=palette.METAL, segments=8),
        p.cylinder("RollPostR", radius=0.055, height=0.80, at=(0.72, 0.62, 1.18),
                   color=palette.METAL, segments=8),
        p.cylinder("RollBar", radius=0.055, height=1.50, at=(0.0, 0.62, 1.56),
                   color=palette.METAL, segments=8, axis="X"),
        # Seat backs.
        p.box("SeatBack", size=(1.35, 0.16, 0.42), at=(0.0, 0.28, 0.91), color=palette.HULL_DARK),
        # Rear cargo deck - where the flag rides.
        p.box("CargoDeck", size=(1.60, 0.95, 0.26), at=(0.0, 1.42, 0.83), color=palette.HULL),
        # Cowl-mounted grenade launcher. Fixed forward - the jeep has no turret, which
        # is most of why it has to drive at what it wants to hit.
        p.box("LauncherMount", size=(0.34, 0.36, 0.22), at=(0.0, -1.02, 1.15),
              color=palette.METAL_DARK),
        p.cylinder("LauncherTube", radius=0.09, height=0.70, at=(0.0, -1.45, 1.16),
                   color=palette.METAL_DARK, segments=8, axis="Y"),
    ]

    root = p.join(ASSET_NAME, hull_parts)

    # The mouth of the launcher, the renderer M3's weapons fire from. It is a child of
    # the root rather than of a turret: nothing on the jeep traverses.
    p.attach_group(root, material.MUZZLE, [
        p.cylinder("Tip", radius=0.11, height=0.16, at=(0.0, -1.87, 1.16),
                   group=material.MUZZLE, segments=8, axis="Y"),
    ])

    # Team-colored trim, spread over bonnet, flanks and tail so team identity reads
    # from any angle the split-screen camera can end up at - a bonnet decal alone is
    # hidden behind the windscreen the moment the camera is behind the jeep.
    trim_parts = [
        p.box("DecalBonnet", size=(0.90, 0.70, 0.05), at=(0.0, -1.15, 1.055), group=material.TEAM),
        p.box("TrimL", size=(0.06, 2.00, 0.18), at=(0.87, 0.20, 0.60), group=material.TEAM),
        p.box("TrimR", size=(0.06, 2.00, 0.18), at=(-0.87, 0.20, 0.60), group=material.TEAM),
        p.box("PlateRear", size=(0.70, 0.06, 0.22), at=(0.0, 1.92, 0.85), group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, trim_parts)

    # Lights are their own materials rather than vertex colors so Unity can make them
    # emit, and switch them with the engine.
    p.attach_group(root, material.LIGHT_FRONT, [
        p.box("HeadL", size=(0.22, 0.06, 0.16), at=(0.55, -2.02, 0.62),
              group=material.LIGHT_FRONT),
        p.box("HeadR", size=(0.22, 0.06, 0.16), at=(-0.55, -2.02, 0.62),
              group=material.LIGHT_FRONT),
    ])
    p.attach_group(root, material.LIGHT_REAR, [
        p.box("TailL", size=(0.20, 0.06, 0.14), at=(0.60, 1.92, 0.85),
              group=material.LIGHT_REAR),
        p.box("TailR", size=(0.20, 0.06, 0.14), at=(-0.60, 1.92, 0.85),
              group=material.LIGHT_REAR),
    ])

    # Facing -Y with +Z up makes right = forward x up = -X, so the vehicle's left
    # wheels sit at +X. They land on Unity's left too, because the glTF importer
    # mirrors X when it converts to Unity's left-handed space.
    for label, x, y in (
        ("FL", _WHEEL_X, _FRONT_AXLE_Y),
        ("FR", -_WHEEL_X, _FRONT_AXLE_Y),
        ("RL", _WHEEL_X, _REAR_AXLE_Y),
        ("RR", -_WHEEL_X, _REAR_AXLE_Y),
    ):
        axle = (x, y, _WHEEL_RADIUS)
        wheel = p.cylinder(f"Wheel_{label}", radius=_WHEEL_RADIUS, height=_WHEEL_WIDTH,
                           at=axle, color=palette.RUBBER, segments=12, axis="X")
        # Pivot on the axle so Unity can spin the wheel in place.
        p.set_pivot(wheel, axle)
        p.attach(wheel, root)

    return root


BUILDERS = {ASSET_NAME: build}
