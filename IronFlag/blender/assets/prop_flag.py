"""RF_Prop_Flag - the objective, and the only asset that is a rule rather than scenery.

Brief (asset spec): team-color accent on the banner only; neutral staff. It has no
destruction states, because nothing in the game is allowed to shoot the objective -
the same reasoning that leaves the bunker and the flag tower whole.

Three things carry this one mesh, which is why the origin is at the *foot of the
staff* rather than at its middle: it stands on the flag tower's pole, it is planted
on the ground where a jeep was killed, and it rides a mast above whichever jeep is
carrying it. All three want "put the bottom of the staff here".

Built facing the same way vehicles are - the banner streams toward +Y, which the
exporter maps to Unity -Z - so a carried flag trails behind the jeep rather than
across its nose.

The banner is a plain vertical rectangle on purpose. The game's camera looks down at
58 degrees, so a vertical face still shows half its area; a flag folded or angled to
catch a top-down view reads as a windvane from the side, and the side is where the
team color has to be legible.
"""

from rf import material
from rf import palette
from rf import primitives as p
from rf import scene

ASSET_NAME = "RF_Prop_Flag"

#: Metres from the foot of the staff to its tip. Tall enough to clear a jeep's roll
#: cage by a clear margin, so a carrier is unmistakable from across the map.
_STAFF_HEIGHT = 2.40

_STAFF_RADIUS = 0.055

#: Banner extents. Roughly a metre square, which is the smallest patch of team color
#: that still reads at the gameplay camera's thirty-four metres.
_BANNER_SIZE = (0.07, 1.45, 0.90)
_BANNER_CENTRE = (0.0, 0.775, 1.80)


def build():
    """Build the flag and return its root object.

    Returns:
        The joined staff, with the team-colored banner parented to it.
    """
    scene.begin(ASSET_NAME)

    parts = [
        p.cylinder("Staff", radius=_STAFF_RADIUS, height=_STAFF_HEIGHT,
                   at=(0.0, 0.0, _STAFF_HEIGHT * 0.5), color=palette.METAL, segments=8),
        # A spike on top, so the silhouette has a point to it even when the banner is
        # edge-on to the camera.
        p.pyramid("Finial", size=(0.16, 0.16, 0.26), at=(0.0, 0.0, _STAFF_HEIGHT + 0.13),
                  color=palette.METAL),
        # A collar where the staff meets the ground, tower pole or mast. It hides the
        # join in all three, and none of the three can be modelled for.
        p.cylinder("Collar", radius=0.13, height=0.16, at=(0.0, 0.0, 0.08),
                   color=palette.METAL_DARK, segments=8),
    ]
    root = p.join(ASSET_NAME, parts)

    # White vertex color under the accent material, so the team color Unity assigns
    # shows exactly rather than multiplied by anything baked into the mesh.
    banner = [
        p.box("Banner", size=_BANNER_SIZE, at=_BANNER_CENTRE,
              color=palette.WHITE, group=material.TEAM),
    ]
    p.attach_group(root, material.TEAM, banner)

    return root


BUILDERS = {ASSET_NAME: build}
