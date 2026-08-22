"""Fixed color palette for every IronFlag asset.

The palette is defined once here and reused everywhere, per the "Color Palette
(fixed - reuse everywhere)" rule in ``return-fire-homage-asset-spec.md``. Colors
are authored as sRGB hex (what you would pick in a color picker) and exposed as
linear RGBA tuples, because Blender color attributes and glTF ``COLOR_0`` are
both linear.

Team colors are *not* in this palette on purpose. Team identity is carried by a
second material slot (see :mod:`rf.material`) that Unity overrides per team, so
one mesh serves both sides.
"""


def srgb_to_linear(hex_color, alpha=1.0):
    """Convert an sRGB hex string to a linear RGBA tuple.

    Args:
        hex_color: Color as ``"#rrggbb"`` or ``"rrggbb"``.
        alpha: Straight alpha, unchanged by the transfer function.

    Returns:
        A 4-tuple of floats in linear space, ready for a Blender color attribute.
    """
    text = hex_color.lstrip("#")
    channels = [int(text[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(_to_linear(c) for c in channels) + (alpha,)


def _to_linear(channel):
    """Apply the sRGB electro-optical transfer function to one channel."""
    if channel <= 0.04045:
        return channel / 12.92
    return ((channel + 0.055) / 1.055) ** 2.4


# -- Vehicle / structure body -------------------------------------------------
HULL = srgb_to_linear("#7A7F73")        # neutral olive-grey; the default body color
HULL_DARK = srgb_to_linear("#4E5249")   # shadow panels, under-body, recesses
METAL = srgb_to_linear("#9AA0A6")       # exposed metal: barrels, frames, rails
METAL_DARK = srgb_to_linear("#5B6066")  # gun metal, tracks, hinges
RUBBER = srgb_to_linear("#232629")      # tyres, treads, blade roots
GLASS = srgb_to_linear("#2B3A45")       # canopies, vision slits, screens

# -- Map furniture (neutral, never team-tinted) -------------------------------
CONCRETE = srgb_to_linear("#9C9689")    # bunkers, tower walls, bridge piers
SAND = srgb_to_linear("#C2B191")        # ground-adjacent stone, gravel, planks
WOOD = srgb_to_linear("#7C5A38")        # bridge decking, crates, tree trunks
FOLIAGE = srgb_to_linear("#4C6B3C")     # tree canopies, bushes

# -- Readability accents ------------------------------------------------------
LIGHT = srgb_to_linear("#E8E4DA")       # off-white: markings, flag cloth, lamps
# Accent faces get pure white so the team material Unity assigns to the accent
# slot shows its own color exactly, unmultiplied by anything baked into the mesh.
WHITE = (1.0, 1.0, 1.0, 1.0)
WARNING = srgb_to_linear("#C8912E")     # hazard stripes on depots and lifts

# -- Damage tint (identical across every _Damaged / _Destroyed asset) ---------
CHARRED = srgb_to_linear("#2E2B29")     # burnt structure; the damage-state base
SCORCH = srgb_to_linear("#7A3B22")      # rust/ember edging on destroyed states
