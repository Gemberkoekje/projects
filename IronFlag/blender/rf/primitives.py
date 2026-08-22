"""Primitive builders - the only shapes IronFlag assets are allowed to use.

The asset spec restricts construction to box, cylinder, wedge/prism, cone and pyramid, so
this module is deliberately small. Every builder:

* generates its geometry directly in world space and leaves the object transform
  at identity, which is what "apply all transforms before export" amounts to;
* shades flat and writes one palette color into the ``Col`` color attribute;
* returns the object, so parts can be collected into a list and joined.

Positions are in meters (1 Blender unit = 1 meter) and rotations are in degrees,
because that is how the dimensions in the asset spec are written.

Facing convention: build vehicles pointing along **-Y**. The glTF exporter maps
Blender -Y to Unity +Z, so a jeep built nose-first toward -Y drives forward in
Unity without anyone re-orienting anything.
"""

import math

import bpy
from mathutils import Euler, Matrix, Vector

from . import material as rf_material
from . import palette as rf_palette
from . import scene as rf_scene

_AXIS_ROTATION = {
    "Z": (0.0, 0.0, 0.0),
    "X": (0.0, 90.0, 0.0),
    "Y": (90.0, 0.0, 0.0),
}


def box(name, size, at=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), color=None, group=rf_material.BODY):
    """Build an axis-aligned box centered on ``at``.

    Args:
        name: Object and mesh name.
        size: ``(x, y, z)`` extents in meters.
        at: World-space center.
        rot: Euler rotation in degrees, applied about ``at``.
        color: Linear RGBA from :mod:`rf.palette`.
        group: Material group from :mod:`rf.material`; defaults to the vertex-colored
            body. Non-body groups are joined into their own child object.

    Returns:
        The created mesh object.
    """
    half_x, half_y, half_z = (component * 0.5 for component in size)
    verts = [
        (-half_x, -half_y, -half_z), (half_x, -half_y, -half_z),
        (half_x, half_y, -half_z), (-half_x, half_y, -half_z),
        (-half_x, -half_y, half_z), (half_x, -half_y, half_z),
        (half_x, half_y, half_z), (-half_x, half_y, half_z),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    return _emit(name, verts, faces, at, rot, color, group)


def wedge(name, size, at=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), color=None, group=rf_material.BODY):
    """Build a triangular prism that slopes down toward +Y.

    The cross-section is full height at the -Y face and zero height at +Y, which
    is the shape used for hoods, windscreens, ramps and roof pitches.

    Args:
        name: Object and mesh name.
        size: ``(x, y, z)`` extents of the bounding box, in meters.
        at: World-space center of that bounding box.
        rot: Euler rotation in degrees, applied about ``at``.
        color: Linear RGBA from :mod:`rf.palette`.
        group: Material group from :mod:`rf.material`; defaults to the vertex-colored
            body. Non-body groups are joined into their own child object.

    Returns:
        The created mesh object.
    """
    half_x, half_y, half_z = (component * 0.5 for component in size)
    verts = [
        (-half_x, -half_y, -half_z), (half_x, -half_y, -half_z),
        (half_x, half_y, -half_z), (-half_x, half_y, -half_z),
        (-half_x, -half_y, half_z), (half_x, -half_y, half_z),
    ]
    faces = [
        (0, 3, 2, 1), (0, 1, 5, 4), (1, 2, 5), (2, 3, 4, 5), (3, 0, 4),
    ]
    return _emit(name, verts, faces, at, rot, color, group)


def pyramid(name, size, at=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), color=None, group=rf_material.BODY,
            apex_size=(0.0, 0.0)):
    """Build a rectangular pyramid or truncated pyramid rising along +Z.

    Args:
        name: Object and mesh name.
        size: ``(x, y, z)`` extents; ``x`` and ``y`` are the base footprint.
        at: World-space center of the bounding box.
        rot: Euler rotation in degrees, applied about ``at``.
        color: Linear RGBA from :mod:`rf.palette`.
        group: Material group from :mod:`rf.material`; defaults to the vertex-colored
            body. Non-body groups are joined into their own child object.
        apex_size: ``(x, y)`` extents of the top face. ``(0, 0)`` gives a point.

    Returns:
        The created mesh object.
    """
    half_x, half_y, half_z = (component * 0.5 for component in size)
    top_x, top_y = (component * 0.5 for component in apex_size)

    if top_x <= 0.0 and top_y <= 0.0:
        verts = [
            (-half_x, -half_y, -half_z), (half_x, -half_y, -half_z),
            (half_x, half_y, -half_z), (-half_x, half_y, -half_z),
            (0.0, 0.0, half_z),
        ]
        faces = [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
    else:
        verts = [
            (-half_x, -half_y, -half_z), (half_x, -half_y, -half_z),
            (half_x, half_y, -half_z), (-half_x, half_y, -half_z),
            (-top_x, -top_y, half_z), (top_x, -top_y, half_z),
            (top_x, top_y, half_z), (-top_x, top_y, half_z),
        ]
        faces = [
            (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
            (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        ]
    return _emit(name, verts, faces, at, rot, color, group)


def cylinder(name, radius, height, at=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), color=None,
             group=rf_material.BODY, segments=12, axis="Z"):
    """Build a closed cylinder centered on ``at``.

    Args:
        name: Object and mesh name.
        radius: Radius in meters.
        height: Length along the chosen axis, in meters.
        at: World-space center.
        rot: Extra Euler rotation in degrees, applied after ``axis``.
        color: Linear RGBA from :mod:`rf.palette`.
        group: Material group from :mod:`rf.material`; defaults to the vertex-colored
            body. Non-body groups are joined into their own child object.
        segments: Radial segment count. Keep it low - these are faceted on purpose.
        axis: ``"X"``, ``"Y"`` or ``"Z"``; the axis the cylinder runs along.

    Returns:
        The created mesh object.
    """
    return cone(name, radius, radius, height, at=at, rot=rot, color=color, group=group,
                segments=segments, axis=axis)


def cone(name, radius_bottom, radius_top, height, at=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0),
         color=None, group=rf_material.BODY, segments=12, axis="Z"):
    """Build a cone or frustum centered on ``at``.

    Args:
        name: Object and mesh name.
        radius_bottom: Radius at the -axis end, in meters.
        radius_top: Radius at the +axis end; ``0`` gives a true cone.
        height: Length along the chosen axis, in meters.
        at: World-space center.
        rot: Extra Euler rotation in degrees, applied after ``axis``.
        color: Linear RGBA from :mod:`rf.palette`.
        group: Material group from :mod:`rf.material`; defaults to the vertex-colored
            body. Non-body groups are joined into their own child object.
        segments: Radial segment count.
        axis: ``"X"``, ``"Y"`` or ``"Z"``; the axis the shape runs along.

    Returns:
        The created mesh object.
    """
    half_height = height * 0.5
    verts = []
    faces = []

    ring_bottom = []
    ring_top = []
    for index in range(segments):
        angle = 2.0 * math.pi * index / segments
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        ring_bottom.append(len(verts))
        verts.append((radius_bottom * cos_a, radius_bottom * sin_a, -half_height))
    if radius_top > 0.0:
        for index in range(segments):
            angle = 2.0 * math.pi * index / segments
            cos_a, sin_a = math.cos(angle), math.sin(angle)
            ring_top.append(len(verts))
            verts.append((radius_top * cos_a, radius_top * sin_a, half_height))
    else:
        apex = len(verts)
        verts.append((0.0, 0.0, half_height))

    for index in range(segments):
        following = (index + 1) % segments
        if radius_top > 0.0:
            faces.append((ring_bottom[index], ring_bottom[following],
                          ring_top[following], ring_top[index]))
        else:
            faces.append((ring_bottom[index], ring_bottom[following], apex))

    faces.append(tuple(reversed(ring_bottom)))
    if radius_top > 0.0:
        faces.append(tuple(ring_top))

    axis_rotation = _AXIS_ROTATION[axis.upper()]
    combined = tuple(axis_rotation[i] + rot[i] for i in range(3))
    return _emit(name, verts, faces, at, combined, color, group)


def join(name, objs):
    """Merge several primitives into one object.

    Args:
        name: Name for the merged object and its mesh.
        objs: Objects to merge. The first one becomes the merge target.

    Returns:
        The merged object.
    """
    if len(objs) == 1:
        merged = objs[0]
    else:
        rf_scene.select(objs)
        bpy.ops.object.join()
        merged = bpy.context.view_layer.objects.active

    merged.name = name
    merged.data.name = name
    prune_material_slots(merged)
    return merged


def prune_material_slots(obj):
    """Drop every material slot no face actually uses, remapping the rest.

    Parts are grouped before joining, so a joined object is normally single-material
    already. This is the safety net for an asset that joins mixed groups by mistake:
    exporting an unused slot would hand Unity a renderer with a material it never
    draws with, which is exactly the kind of thing that hides an error.

    Args:
        obj: Mesh object to tidy.
    """
    mesh = obj.data
    if len(mesh.materials) < 2:
        return

    used = sorted({polygon.material_index for polygon in mesh.polygons})
    if len(used) == len(mesh.materials):
        return

    kept = [mesh.materials[index] for index in used]
    remapped = {old: new for new, old in enumerate(used)}
    # Snapshot first: clearing the slots makes Blender clamp every material_index,
    # so reading them afterwards gives zeroes rather than the original assignment.
    original = [polygon.material_index for polygon in mesh.polygons]

    mesh.materials.clear()
    for material in kept:
        mesh.materials.append(material)
    for polygon, previous in zip(mesh.polygons, original):
        polygon.material_index = remapped[previous]


def attach_group(parent, group, parts):
    """Join a material group's parts into its child object and parent it.

    Args:
        parent: Object the group hangs off - usually the asset root, but a moving
            part when the group belongs to it (a muzzle on a rotating turret).
        group: Material group from :mod:`rf.material`.
        parts: Objects in that group. An empty list is a no-op.

    Returns:
        The joined child object, or ``None`` when ``parts`` is empty.
    """
    if not parts:
        return None

    child = join(rf_material.object_name(group), parts)
    attach(child, parent)
    return child


def set_pivot(obj, point):
    """Move an object's origin to a world-space point without moving the geometry.

    Use this on parts Unity animates - wheels, turrets, rotors - so the part
    rotates about its axle or ring rather than about the vehicle's origin.

    Args:
        obj: Object whose origin should move.
        point: World-space pivot position.
    """
    offset = Vector(point)
    obj.data.transform(Matrix.Translation(-offset))
    obj.location = offset


def attach(child, parent):
    """Parent one object to another, keeping its current world position.

    Args:
        child: Object to parent.
        parent: New parent object.
    """
    # matrix_world is evaluated lazily, and a parent that has just had its pivot moved
    # still reports the old one until the depsgraph catches up.
    bpy.context.view_layer.update()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def _emit(name, verts, faces, at, rot, color, group):
    """Create the object, bake its transform into the mesh, shade and paint it."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate(verbose=False)

    if color is None:
        # Only the body group's material reads vertex color; the rest get white so an
        # override in Unity shows its own color exactly, unmultiplied.
        color = rf_palette.HULL if group == rf_material.BODY else rf_palette.WHITE

    radians = [math.radians(angle) for angle in rot]
    transform = Matrix.Translation(Vector(at)) @ Euler(radians, "XYZ").to_matrix().to_4x4()
    mesh.transform(transform)
    mesh.shade_flat()

    obj = bpy.data.objects.new(name, mesh)
    rf_scene.link(obj)

    rf_material.assign(obj, group)
    rf_material.paint(obj, color)
    return obj
