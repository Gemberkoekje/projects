"""Shared Blender helpers for building IronFlag art assets.

Asset scripts in ``blender/assets/`` import from here rather than calling
``bpy`` directly, so the rules in ``return-fire-homage-asset-spec.md`` - flat
shading, vertex colors, primitives only, applied transforms, one ``.glb`` per
asset - are enforced in one place instead of being re-implemented per model.

Typical asset script::

    from rf import palette, primitives as p, scene

    def build():
        scene.begin("RF_Prop_Crate")
        body = p.box("Body", size=(1.0, 1.0, 1.0), at=(0, 0, 0.5), color=palette.WOOD)
        return p.join("RF_Prop_Crate", [body])

    BUILDERS = {"RF_Prop_Crate": build}
"""

from . import export, material, naming, palette, primitives, scene

__all__ = ["export", "material", "naming", "palette", "primitives", "scene"]
