"""RF_Vehicle_Helicopter - the only airborne silhouette in the roster.

Silhouette brief (asset spec): slim fuselage, and the only vehicle that reads as
airborne from the gameplay camera. Slimness is what separates it from the three
ground vehicles from above, so the fuselage stays the narrowest hull in the set and
the tail boom keeps it long and thin rather than blocky.

The rotor spans 4.4m, considerably wider than the 2.0m fuselage. An earlier pass
held the rotor down to the fuselage width to protect the tank's "widest hull" read,
and it looked stubby for no benefit: the spec's contract is about *hulls*, and a
rotor disc is not one. The wide disc is also the clearest possible "this one flies"
cue from directly above, which is the angle the game is played at.

Main and tail rotors are separate children with their origins on their own axes:
``MainRotor`` spins about Z, ``TailRotor`` about X. Modelled sitting on its skids so
the origin is the pad contact point. Built facing -Y.
"""

from rf import palette
from rf import primitives as p
from rf import scene
from rf import material

ASSET_NAME = "RF_Vehicle_Helicopter"

_MAST_TOP = (0.0, -0.50, 2.55)
_TAIL_ROTOR_HUB = (0.14, 2.45, 1.95)
_ROTOR_SPAN = 4.40


def build():
    """Build the helicopter and return its root object.

    Returns:
        The joined airframe, with both rotors and the team trim parented to it.
    """
    scene.begin(ASSET_NAME)

    airframe_parts = [
        p.box("SkidL", size=(0.10, 2.40, 0.10), at=(0.62, 0.05, 0.05),
              color=palette.METAL_DARK),
        p.box("SkidR", size=(0.10, 2.40, 0.10), at=(-0.62, 0.05, 0.05),
              color=palette.METAL_DARK),
        p.box("StrutFront", size=(1.34, 0.12, 0.50), at=(0.0, -0.60, 0.35),
              color=palette.METAL_DARK),
        p.box("StrutRear", size=(1.34, 0.12, 0.50), at=(0.0, 0.75, 0.35),
              color=palette.METAL_DARK),
        p.box("Fuselage", size=(1.00, 2.40, 0.95), at=(0.0, -0.50, 1.10), color=palette.HULL),
        p.box("Canopy", size=(0.90, 0.80, 0.62), at=(0.0, -1.45, 1.28), color=palette.GLASS),
        # Nose tapers toward the front, so the wedge is turned around.
        p.wedge("Nose", size=(0.95, 0.90, 0.55), at=(0.0, -2.05, 0.90),
                rot=(0.0, 0.0, 180.0), color=palette.HULL),
        # Engine housing and exhaust: the detail that stops the tail end reading as a
        # bare stick, and gives the boom something to grow out of.
        p.box("Engine", size=(0.70, 0.80, 0.35), at=(0.0, 0.45, 1.72),
              color=palette.METAL_DARK),
        p.cylinder("Exhaust", radius=0.13, height=0.40, at=(0.28, 0.95, 1.60),
                   color=palette.METAL_DARK, segments=8, axis="Y"),
        p.box("TailBoom", size=(0.30, 1.90, 0.28), at=(0.0, 1.60, 1.35), color=palette.HULL),
        p.box("Stabiliser", size=(1.20, 0.40, 0.08), at=(0.0, 2.30, 1.50), color=palette.HULL),
        p.box("TailPylon", size=(0.10, 0.50, 0.85), at=(0.0, 2.50, 1.80), color=palette.HULL),
        p.cylinder("Mast", radius=0.09, height=1.00, at=(0.0, -0.50, 2.05),
                   color=palette.METAL_DARK, segments=8),
        # Chin gun housing, slung under the nose where it clears the skids.
        p.box("GunMount", size=(0.34, 0.40, 0.26), at=(0.0, -2.00, 0.58),
              color=palette.METAL_DARK),
    ]
    root = p.join(ASSET_NAME, airframe_parts)

    # The gun barrels, the renderer M3's weapons fire from. They stay inside the
    # nose's own 5.25m airframe length rather than extending it.
    p.attach_group(root, material.MUZZLE, [
        p.cylinder("BarrelL", radius=0.05, height=0.30, at=(0.08, -2.30, 0.58),
                   group=material.MUZZLE, segments=6, axis="Y"),
        p.cylinder("BarrelR", radius=0.05, height=0.30, at=(-0.08, -2.30, 0.58),
                   group=material.MUZZLE, segments=6, axis="Y"),
    ])

    main_rotor_parts = [
        p.cylinder("Hub", radius=0.18, height=0.16, at=(0.0, -0.50, 2.62),
                   color=palette.METAL_DARK, segments=8),
        p.box("BladeA", size=(0.22, _ROTOR_SPAN, 0.05), at=(0.0, -0.50, 2.72),
              color=palette.METAL_DARK),
        p.box("BladeB", size=(_ROTOR_SPAN, 0.22, 0.05), at=(0.0, -0.50, 2.72),
              color=palette.METAL_DARK),
    ]
    main_rotor = p.join("MainRotor", main_rotor_parts)
    p.set_pivot(main_rotor, _MAST_TOP)
    p.attach(main_rotor, root)

    tail_rotor_parts = [
        p.cylinder("Hub", radius=0.10, height=0.12, at=_TAIL_ROTOR_HUB,
                   color=palette.METAL_DARK, segments=6, axis="X"),
        p.box("BladeA", size=(0.06, 1.00, 0.14), at=_TAIL_ROTOR_HUB,
              color=palette.METAL_DARK),
        p.box("BladeB", size=(0.06, 0.14, 1.00), at=_TAIL_ROTOR_HUB,
              color=palette.METAL_DARK),
    ]
    tail_rotor = p.join("TailRotor", tail_rotor_parts)
    p.set_pivot(tail_rotor, _TAIL_ROTOR_HUB)
    p.attach(tail_rotor, root)

    trim_parts = [
        p.box("SpineStripe", size=(0.60, 1.60, 0.06), at=(0.0, -0.60, 1.60), group=material.TEAM),
        p.box("BoomStripe", size=(0.20, 1.40, 0.06), at=(0.0, 1.60, 1.52), group=material.TEAM),
        p.box("FinPlate", size=(0.12, 0.35, 0.50), at=(0.0, 2.50, 1.85), group=material.TEAM),
        p.box("FlashL", size=(0.06, 1.30, 0.28), at=(0.52, -0.50, 1.15), group=material.TEAM),
        p.box("FlashR", size=(0.06, 1.30, 0.28), at=(-0.52, -0.50, 1.15), group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, trim_parts)

    p.attach_group(root, material.LIGHT_FRONT, [
        p.box("Landing", size=(0.20, 0.06, 0.14), at=(0.0, -2.48, 0.80),
              group=material.LIGHT_FRONT),
    ])
    p.attach_group(root, material.LIGHT_REAR, [
        p.box("Beacon", size=(0.10, 0.06, 0.12), at=(0.0, 2.74, 2.15),
              group=material.LIGHT_REAR),
    ])

    return root


BUILDERS = {ASSET_NAME: build}
