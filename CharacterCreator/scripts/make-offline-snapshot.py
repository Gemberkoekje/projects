"""Synthesizes the offline VOID snapshot that the first start publishes (DESIGN.md §4.2).

Written in VOID's GET /skills shape and marked "synthesized": true, so the published ruleset is
flagged provisional until an SL or EC imports from a real VOID. Built only from public sources:

- the content (skills.json): VOID names, and the cost the 2026 book prints (bookCost);
- VOID's public rulebook draft (config/SkillRequirements/vaardigheden-2026-v07.json): the
  prerequisites and which skills are extra, resolved the way VOID's loader does (named sets, a
  book skill VOID splits in two counts as both, two book entries for one VOID skill add up);
- Thorfgin's data: how often a skill may be bought (maxcount);
- the content's properties: energy and imbue caps, under the type names VOID is guessed to use.

Usage:
    python -I scripts/make-offline-snapshot.py <content dir> <vaardigheden-2026-v07.json> <thorfgin src/json>
"""

import json
import re
import sys
import unicodedata
from pathlib import Path

ENERGY = {
    "elemental_mana": "Elemental",
    "spiritual_mana": "Spiritual",
    "willpower": "Willpower",
    "inspiration": "Inspiration",
    "elemental_ritual_mana": "Elemental ritual",
    "spiritual_ritual_mana": "Spiritual ritual",
}
IMBUE = {"glyph_imbue_cap": "Elemental", "rune_imbue_cap": "Spiritual"}


def norm(text):
    text = unicodedata.normalize("NFKD", text or "").encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def main(content_dir, book_path, thorfgin_dir):
    content_dir, thorfgin_dir = Path(content_dir), Path(thorfgin_dir)
    content = json.loads((content_dir / "skills.json").read_text(encoding="utf-8"))
    book = json.loads(Path(book_path).read_text(encoding="utf-8"))
    thorfgin = json.loads((thorfgin_dir / "nl" / "vaardigheden.json").read_text(encoding="utf-8"))

    skills = content["skills"]
    ids = {skill["void"].lower(): number for number, skill in enumerate(skills, start=1)}

    # Book names (Dutch, English, aliases) to the VOID names they stand for.
    names = {}
    for entry in book["skills"]:
        for name in [entry["name"], entry.get("english", "")] + book.get("aliases", {}).get(entry["name"], []):
            if name:
                names.setdefault(norm(name), []).extend(v for v in entry.get("void", []) if v not in names.get(norm(name), []))
    for skill in skills:
        names.setdefault(norm(skill["void"]), [skill["void"]])

    sets = book.get("sets", {})
    unresolved = set()

    def expand(item, seen=()):
        if item.startswith("@"):
            if item in seen:
                return []
            return [v for member in sets.get(item[1:], []) for v in expand(member, seen + (item,))]
        found = names.get(norm(item))
        if not found:
            unresolved.add(item)
        return found or []

    # Prerequisites per VOID name; two book entries for one VOID skill add their groups together.
    requirements = {}
    extra = set()
    for entry in book["skills"]:
        for void_name in entry.get("void", []):
            if entry.get("extra"):
                extra.add(void_name.lower())
            for group in entry.get("requires", []):
                kind = group.get("kind", "any")
                members = [] if kind == "extra" else sorted({ids[v.lower()] for item in group.get("skills", []) for v in expand(item) if v.lower() in ids})
                requirements.setdefault(void_name.lower(), []).append(
                    {"kind": kind, "amount": group.get("amount", 1), "skills": None if kind == "extra" else members})

    # How often a skill may be bought, from Thorfgin's maxcount, matched like the content import does.
    times = {}
    for item in (s for group in ("BasisVaardigheden", "ExtraVaardigheden") for s in thorfgin[group]):
        base = re.sub(r"\s*\((EL|SP)\)$", "", item["skill"])
        base = re.sub(r"^(Levens|Doods)\s+", "", base)
        for void_name in names.get(norm(item["skill"]), []) or names.get(norm(base), []):
            times[void_name.lower()] = max(times.get(void_name.lower(), 1), min(int(item.get("maxcount", 1)), 99))

    listing = []
    for skill in skills:
        key = skill["void"].lower()
        times_max = times.get(key, 1)
        props = {p["name"]: p["value"] for p in skill["properties"]}
        energy = next(((ENERGY[name], value) for name, value in props.items() if name in ENERGY), (None, None))
        imbue = next(((IMBUE[name], value) for name, value in props.items() if name in IMBUE), (None, None))
        listing.append({
            "class": "Skill",
            "url": f"/skills/{ids[key]}",
            "id": ids[key],
            "name": skill["void"],
            "cost": max(skill["bookCost"], 0),
            "base_max": 0 if key in extra else times_max,
            "times_max": times_max,
            "mana_amount": energy[1],
            "manatype": {"name": energy[0]} if energy[0] else None,
            "imbue_cap": imbue[1],
            "imbue_type": {"name": imbue[0]} if imbue[0] else None,
            "loresheet": bool(skill["loresheet"]["url"]),
            "blanks": False,
            "deprecated": False,
            "requirements": requirements.get(key, []),
        })

    snapshot = {
        "class": "List",
        "url": "/skills",
        "source": f"offline snapshot ({content['ruleset']}), synthesized from the public rulebook draft, the 2026 book's costs and Thorfgin's data",
        "synthesized": True,
        "taken_at": "2026-11-07T00:00:00Z",
        "list": listing,
    }
    (content_dir / "void-snapshot.json").write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{len(listing)} skills, {sum(len(r) for r in requirements.values())} prerequisite groups, "
          f"{len(extra)} extra; unresolved names: {sorted(unresolved) or 'none'}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
