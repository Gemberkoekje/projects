"""glTF export - one ``.glb`` per asset, ready for Unity's glTFast importer.

Export settings are fixed here rather than passed in, so every asset lands in
Unity with the same axis conversion, the same vertex-color handling and the same
lack of cameras, lights and animation tracks.
"""

import os

import bpy

from . import scene as rf_scene


def export_glb(root, output_directory, asset_name):
    """Write one asset, including its parented moving parts, to a ``.glb`` file.

    Args:
        root: Root object of the asset. Children are exported with it.
        output_directory: Folder to write into; created when missing.
        asset_name: File name without extension, matching the object name.

    Returns:
        The absolute path of the written file.
    """
    os.makedirs(output_directory, exist_ok=True)
    filepath = os.path.join(output_directory, asset_name + ".glb")

    rf_scene.select(rf_scene.hierarchy(root))

    bpy.ops.export_scene.gltf(
        filepath=filepath,
        export_format="GLB",
        use_selection=True,
        # Blender Z-up to glTF/Unity Y-up. Models are authored facing -Y so they
        # arrive facing Unity's +Z; never re-orient by hand to compensate.
        export_yup=True,
        # Bake modifiers into the exported mesh. Object transforms are already
        # identity because the primitives build in world space.
        export_apply=True,
        export_materials="EXPORT",
        # Only export COLOR_0 where a material actually reads it, which is the
        # RF_Flat slot. RF_TeamAccent stays a plain material Unity can swap.
        export_vertex_color="MATERIAL",
        export_all_vertex_colors=False,
        export_normals=True,
        export_tangents=False,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
        export_skins=False,
        export_morph=False,
        export_extras=False,
    )
    return filepath
