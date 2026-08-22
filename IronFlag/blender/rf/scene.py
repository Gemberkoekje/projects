"""Scene lifecycle helpers.

Every asset is built in a scene of its own so builds are order-independent and
repeatable: :func:`begin` wipes the file back to empty and creates a collection
named after the asset, which is where the primitives land.
"""

import bpy


def reset():
    """Delete every object, mesh, material and collection in the file.

    Blender starts headless builds with the factory scene (cube, camera, light),
    and each asset must start from nothing rather than inherit the previous one.
    """
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)

    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)

    for material in list(bpy.data.materials):
        bpy.data.materials.remove(material)

    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)


def begin(asset_name):
    """Reset the file and open a collection named after the asset.

    Args:
        asset_name: Full asset name, e.g. ``"RF_Vehicle_Jeep"``. The asset spec
            requires the collection name to match the object name.

    Returns:
        The newly created and now-active :class:`bpy.types.Collection`.
    """
    reset()
    collection = bpy.data.collections.new(asset_name)
    bpy.context.scene.collection.children.link(collection)

    layer_collection = bpy.context.view_layer.layer_collection.children[collection.name]
    bpy.context.view_layer.active_layer_collection = layer_collection
    return collection


def link(obj):
    """Link a newly created object into the active collection.

    Args:
        obj: Object to link.
    """
    bpy.context.collection.objects.link(obj)


def deselect_all():
    """Clear the selection and the active object."""
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    bpy.context.view_layer.objects.active = None


def select(objs, active_index=0):
    """Select exactly the given objects and make one of them active.

    Args:
        objs: Objects to select.
        active_index: Index within ``objs`` of the object to make active, which
            is what operators such as join and export treat as the anchor.
    """
    deselect_all()
    for obj in objs:
        obj.select_set(True)
    if objs:
        bpy.context.view_layer.objects.active = objs[active_index]


def hierarchy(root):
    """Return a root object followed by all of its descendants.

    Args:
        root: Object at the top of the hierarchy.

    Returns:
        A list with ``root`` first, then every child, grandchild and so on.
    """
    collected = [root]
    for child in root.children:
        collected.extend(hierarchy(child))
    return collected
