"""Headless build entry point for every IronFlag art asset.

Run it through Blender, not a bare Python interpreter::

    blender --background --factory-startup --python blender/build.py -- --list
    blender --background --factory-startup --python blender/build.py
    blender --background --factory-startup --python blender/build.py -- --asset Jeep

With no ``--asset`` filter it rebuilds everything into
``unity/Assets/RF/Art/Models``. ``build.ps1`` wraps this for day-to-day use, and
the Unity editor menu ``Tools > IronFlag > Rebuild All Art from Blender`` calls
exactly the same command.

Each asset is built in a scene of its own, name-checked against the spec's
naming rule, and exported as a single ``.glb``.
"""

import argparse
import importlib
import os
import pkgutil
import sys
import traceback

SCRIPT_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.dirname(SCRIPT_DIRECTORY)
DEFAULT_OUTPUT = os.path.join(REPOSITORY_ROOT, "unity", "Assets", "RF", "Art", "Models")

if SCRIPT_DIRECTORY not in sys.path:
    sys.path.insert(0, SCRIPT_DIRECTORY)

import assets as asset_package  # noqa: E402  (needs the sys.path entry above)
from rf import export as rf_export  # noqa: E402
from rf import naming as rf_naming  # noqa: E402


def discover_builders():
    """Import every module in ``assets`` and collect their ``BUILDERS`` entries.

    Returns:
        A dict mapping asset name to builder function, sorted by name.

    Raises:
        ValueError: If two modules claim the same asset name.
    """
    builders = {}
    owners = {}

    for module_info in pkgutil.iter_modules(asset_package.__path__):
        module = importlib.import_module(f"{asset_package.__name__}.{module_info.name}")
        for asset_name, builder in getattr(module, "BUILDERS", {}).items():
            if asset_name in builders:
                raise ValueError(
                    f"'{asset_name}' is defined by both {owners[asset_name]} and {module_info.name}")
            builders[asset_name] = builder
            owners[asset_name] = module_info.name

    return dict(sorted(builders.items()))


def parse_arguments(argv):
    """Parse the arguments Blender passes through after ``--``.

    Args:
        argv: Full ``sys.argv``; everything before ``--`` belongs to Blender.

    Returns:
        The parsed :class:`argparse.Namespace`.
    """
    forwarded = argv[argv.index("--") + 1:] if "--" in argv else []

    parser = argparse.ArgumentParser(prog="build.py", description="Build IronFlag art assets.")
    parser.add_argument("--out", default=DEFAULT_OUTPUT,
                        help="output folder for the .glb files (default: unity/Assets/RF/Art/Models)")
    parser.add_argument("--asset", action="append", default=[],
                        help="case-insensitive substring filter; repeatable")
    parser.add_argument("--list", action="store_true", help="list known assets and exit")
    return parser.parse_args(forwarded)


def selected_assets(builders, filters):
    """Apply the ``--asset`` substring filters.

    Args:
        builders: All discovered builders.
        filters: Substrings; an empty list selects everything.

    Returns:
        A dict of the builders whose name matches at least one filter.
    """
    if not filters:
        return builders

    lowered = [text.lower() for text in filters]
    return {
        name: builder
        for name, builder in builders.items()
        if any(text in name.lower() for text in lowered)
    }


def main():
    """Build the requested assets and report per-asset results."""
    arguments = parse_arguments(sys.argv)
    builders = discover_builders()

    if arguments.list:
        for asset_name in builders:
            print(asset_name)
        return 0

    targets = selected_assets(builders, arguments.asset)
    if not targets:
        print(f"No assets matched {arguments.asset}. Known assets: {', '.join(builders)}",
              file=sys.stderr)
        return 1

    output_directory = os.path.abspath(arguments.out)
    print(f"Building {len(targets)} asset(s) into {output_directory}")

    failures = []
    for asset_name, builder in targets.items():
        problem = rf_naming.validate(asset_name)
        if problem:
            print(f"FAIL {asset_name}: {problem}", file=sys.stderr)
            failures.append(asset_name)
            continue

        try:
            root = builder()
            if root.name != asset_name:
                raise ValueError(
                    f"builder returned root object '{root.name}', expected '{asset_name}'")
            filepath = rf_export.export_glb(root, output_directory, asset_name)
            triangles = sum(len(polygon.vertices) - 2
                            for obj in _mesh_objects(root)
                            for polygon in obj.data.polygons)
            print(f"  OK  {asset_name}  ({triangles} tris)  -> {filepath}")
        except Exception:  # noqa: BLE001 - one bad asset must not stop the batch
            print(f"FAIL {asset_name}:\n{traceback.format_exc()}", file=sys.stderr)
            failures.append(asset_name)

    if failures:
        print(f"{len(failures)} asset(s) failed: {', '.join(failures)}", file=sys.stderr)
        return 1

    print("All assets built.")
    return 0


def _mesh_objects(root):
    """Yield the root and every descendant that carries mesh data."""
    if root.type == "MESH":
        yield root
    for child in root.children:
        yield from _mesh_objects(child)


if __name__ == "__main__":
    sys.exit(main())
