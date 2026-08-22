"""Material groups - the small fixed set of materials every IronFlag asset uses.

The asset spec forbids texture maps and UV unwrapping, so nearly all color lives in
mesh data: the ``body`` group is one vertex-colored material, which is how a whole
vehicle can be a single mesh and still carry a dozen palette colors.

A separate material is only worth adding when Unity needs to *change* something at
runtime that a baked vertex color cannot express:

``body`` (``RF_Flat``)
    Vertex-colored. Everything that is just a color.
``team`` (``RF_TeamAccent``)
    Flag decals and trim. Unity assigns a per-team material, which is what makes one
    mesh serve every team.
``lightFront`` / ``lightRear`` (``RF_LightFront`` / ``RF_LightRear``)
    Head and tail lights. These need emission, and emission is a material property -
    a vertex color cannot glow. Keeping them separate also lets Unity switch them on
    with the engine, flash brake lights, or kill them when a vehicle is wrecked.
``muzzle`` (``RF_Muzzle``)
    Barrel and launcher mouths. A distinct material so weapon VFX can be anchored to
    a known renderer, and so the business end stays readably dark against the hull.

Each non-body group is joined into its own child object named in :data:`GROUPS`,
rather than becoming an extra material slot on the hull mesh. A renderer whose
submeshes mix glTFast's Shader Graph material with a Unity-side material does not
reliably draw the second one; one object, one material is unambiguous.
"""

import bpy

COLOR_ATTRIBUTE_NAME = "Col"

BODY = "body"
TEAM = "team"
LIGHT_FRONT = "lightFront"
LIGHT_REAR = "lightRear"
MUZZLE = "muzzle"

#: Group name -> (material name, child object name). The body group has no child
#: object: it is joined into the asset root itself.
GROUPS = {
    BODY: ("RF_Flat", ""),
    TEAM: ("RF_TeamAccent", "TeamTrim"),
    LIGHT_FRONT: ("RF_LightFront", "LightsFront"),
    LIGHT_REAR: ("RF_LightRear", "LightsRear"),
    MUZZLE: ("RF_Muzzle", "Muzzle"),
}

#: Base colors baked into the non-vertex-colored materials. Team accent is a neutral
#: placeholder because Unity always overrides it; the others are real colors, so an
#: exported .glb looks right in any viewer even before Unity adds emission.
_GROUP_COLOR = {
    TEAM: (0.80, 0.80, 0.80, 1.0),
    LIGHT_FRONT: (0.95, 0.92, 0.82, 1.0),
    LIGHT_REAR: (0.62, 0.09, 0.07, 1.0),
    MUZZLE: (0.10, 0.10, 0.11, 1.0),
}


def material_name(group):
    """Return the material name for a group.

    Args:
        group: One of the group constants in this module.

    Returns:
        The material name, e.g. ``"RF_TeamAccent"``.
    """
    return GROUPS[group][0]


def object_name(group):
    """Return the child object name a group is joined into.

    Args:
        group: One of the group constants in this module.

    Returns:
        The object name, or an empty string for :data:`BODY`.
    """
    return GROUPS[group][1]


def flat_material():
    """Return the shared vertex-color material, creating it on first use.

    Returns:
        The ``RF_Flat`` :class:`bpy.types.Material`.
    """
    name = material_name(BODY)
    existing = bpy.data.materials.get(name)
    if existing is not None:
        return existing

    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    principled = tree.nodes["Principled BSDF"]
    principled.inputs["Roughness"].default_value = 0.65
    principled.inputs["Metallic"].default_value = 0.0

    color_node = tree.nodes.new("ShaderNodeVertexColor")
    color_node.layer_name = COLOR_ATTRIBUTE_NAME
    color_node.location = (-320, 180)
    tree.links.new(color_node.outputs["Color"], principled.inputs["Base Color"])
    return material


def plain_material(group):
    """Return a solid-color material for a non-body group, creating it on first use.

    Args:
        group: One of the group constants other than :data:`BODY`.

    Returns:
        The :class:`bpy.types.Material` for that group.
    """
    name = material_name(group)
    existing = bpy.data.materials.get(name)
    if existing is not None:
        return existing

    material = bpy.data.materials.new(name)
    material.use_nodes = True
    principled = material.node_tree.nodes["Principled BSDF"]
    principled.inputs["Base Color"].default_value = _GROUP_COLOR[group]
    principled.inputs["Roughness"].default_value = 0.55
    principled.inputs["Metallic"].default_value = 0.0
    return material


def material_for(group):
    """Return the material a group uses.

    Args:
        group: One of the group constants in this module.

    Returns:
        The :class:`bpy.types.Material`.
    """
    return flat_material() if group == BODY else plain_material(group)


def assign(obj, group):
    """Give an object exactly one material slot, holding its group's material.

    Args:
        obj: Mesh object to configure.
        group: One of the group constants in this module.
    """
    mesh = obj.data
    mesh.materials.clear()
    mesh.materials.append(material_for(group))
    for polygon in mesh.polygons:
        polygon.material_index = 0


def paint(obj, color):
    """Write one solid color into every corner of a mesh's color attribute.

    Only the body group's material reads this, but it is written for every object so
    the attribute is always present and consistent.

    Args:
        obj: Mesh object to paint.
        color: Linear RGBA tuple, normally a constant from :mod:`rf.palette`.
    """
    mesh = obj.data
    attribute = mesh.color_attributes.get(COLOR_ATTRIBUTE_NAME)
    if attribute is None:
        attribute = mesh.color_attributes.new(
            name=COLOR_ATTRIBUTE_NAME, type="FLOAT_COLOR", domain="CORNER")
    mesh.color_attributes.active_color = attribute
    mesh.color_attributes.render_color_index = mesh.color_attributes.find(COLOR_ATTRIBUTE_NAME)

    for element in attribute.data:
        element.color = color
