"""Builds the Character Creator's content from Thorfgin's builder data and VOID's rulebook draft.

DESIGN.md §4.1. One content entry per VOID skill (what the picker offers), keyed by a slug of the
VOID name, named after the 2026 book, with the text, spells, recipes, properties and loresheet of
Thorfgin's matching skills (ruleset 2023-10b, text fixes up to 2026-04). Only skills in VOID's
public rulebook draft go in, so no SL-only skill can reach this public repository.

Writes skills.json and presets.json (embedded in CharacterCreator.infrastructure) and
IMPORT-REPORT.md, the list SL and EC review (§3.7, steps 5 and 6).

Usage:
    python -I scripts/import-thorfgin-content.py <thorfgin src/json> <vaardigheden-2026-v07.json> <out dir>
"""

import json
import re
import sys
import unicodedata
from pathlib import Path

RULESET = "2026-v07"
BOOK = "Vaardigheden, Vortex Adventures 2026 V07"

# Thorfgin's own map (SharedActions.js getPdfURL); it matches the rules page on 2026-11-07.
PDF_FOLDERS = {
    "2026/03": ["Vaardigheden.pdf", "Spreuken_en_Technieken.pdf", "Crafting_loresheets.pdf"],
    "2024/06": [
        "Armourpoint_kostuum_eisen.pdf", "Samenvatting_regelsysteem.pdf", "Imbue_loresheet.pdf",
        "Kennis_van_kruiden.pdf", "Genezende_Dranken.pdf", "Kruiden_Elixers.pdf", "Magische_Elixers.pdf",
        "Hallucinerende_Elixers.pdf", "Giffen.pdf",
    ],
    "2022/04": ["priest_runes.ttf", "mage_glyphs.ttf"],
    "2022/03": ["Priest-Runes.pdf", "Mage-Glyphs.pdf"],
}
PDF_URL = {name: f"https://the-vortex.nl/wp-content/uploads/{folder}/{name}" for folder, names in PDF_FOLDERS.items() for name in names}

VARIANTS = {
    "elemental": {"nl": "elementair", "en": "elemental"},
    "spiritual": {"nl": "spiritueel", "en": "spiritual"},
}


def norm(text):
    text = unicodedata.normalize("NFKD", text or "").encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def slug(text):
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode().lower()
    text = text.replace("'", "")
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-")


def clean(text):
    """Thorfgin's JSON carries escaped newlines as the two characters backslash and n."""
    return (text or "").replace("\\n", "\n").strip()


def variant_of(void_name):
    lowered = void_name.lower()
    for variant in VARIANTS:
        if variant in lowered:
            return variant
    return None


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def main(thorfgin_dir, book_path, out_dir):
    thorfgin_dir, out_dir = Path(thorfgin_dir), Path(out_dir)
    book = load(book_path)
    skills_nl = load(thorfgin_dir / "nl" / "vaardigheden.json")
    skills_en = load(thorfgin_dir / "en" / "vaardigheden.json")
    t_nl = [s for group in ("BasisVaardigheden", "ExtraVaardigheden") for s in skills_nl[group]]
    t_en = {s["id"]: s for group in ("BasisVaardigheden", "ExtraVaardigheden") for s in skills_en[group]}

    report = {"no_void": [], "no_text": [], "unmatched": [], "ambiguous": [], "merged": [], "presets": []}

    # 1. One entry per VOID skill, in book order, named after the 2026 book.
    lookup = {}
    content = {}
    for entry in book["skills"]:
        lookup.setdefault(norm(entry["name"]), entry)
        if entry.get("english"):
            lookup.setdefault(norm(entry["english"]), entry)
        void_names = entry.get("void") or []
        if not void_names:
            report["no_void"].append(entry["name"])
            continue
        for void_name in void_names:
            split = len(void_names) > 1
            variant = variant_of(void_name) if split else None
            suffix_nl = f" ({VARIANTS[variant]['nl']})" if variant else (f" ({void_name})" if split else "")
            suffix_en = f" ({VARIANTS[variant]['en']})" if variant else (f" ({void_name})" if split else "")
            name = {"nl": entry["name"] + suffix_nl, "en": (entry.get("english") or entry["name"]) + suffix_en}
            if void_name in content:
                existing = content[void_name]
                existing["book"].append(entry["name"])
                if not split:
                    existing["name"] = name
                continue
            content[void_name] = {
                "key": slug(void_name),
                "void": void_name,
                "book": [entry["name"]],
                "name": name,
                "category": {"nl": "", "en": ""},
                "page": 0,
                "pageFrom": "",
                "description": {"nl": "", "en": ""},
                "properties": [],
                "spells": [],
                "recipes": [],
                "loresheet": {"url": "", "page": 0, "extra": ""},
                "sources": [],
            }

    # 2. Thorfgin's skills onto those entries.
    def find_entry(nl_name, en_name):
        tries = []
        for name in (nl_name, en_name):
            tries.append((name, None))
            match = re.match(r"^(.*?)\s*\((EL|SP)\)$", name or "")
            if match:
                variant = "elemental" if match.group(2) == "EL" else "spiritual"
                base = match.group(1)
                tries.append((base, variant))
                tries.append((re.sub(r"^(Levens|Doods|Life|Death)\s+", "", base), variant))
            tries.append((re.sub(r"\s*\([^)]*\)$", "", name or ""), None))
        for name, variant in tries:
            entry = lookup.get(norm(name))
            if entry:
                return entry, variant
        return None, None

    sources_by_id = {}
    for skill in t_nl:
        en = t_en.get(skill["id"], {})
        entry, variant = find_entry(skill["skill"], en.get("skill", ""))
        if not entry:
            report["unmatched"].append(f"{skill['id']} {skill['skill']} ({en.get('skill', '')})")
            continue
        void_names = entry.get("void") or []
        if len(void_names) == 1:
            void_name = void_names[0]
        else:
            picks = [name for name in void_names if variant and variant in name.lower()]
            if len(picks) != 1:
                report["ambiguous"].append(f"{skill['id']} {skill['skill']} -> {entry['name']} {void_names}")
                continue
            void_name = picks[0]
        target = content[void_name]
        target["sources"].append(skill["id"])
        sources_by_id[skill["id"]] = target

        for lang, source in (("nl", skill), ("en", en)):
            text = clean(source.get("description"))
            if text and text not in target["description"][lang]:
                target["description"][lang] = f"{target['description'][lang]}\n\n{text}".strip()
            if not target["category"][lang] and source.get("category"):
                target["category"][lang] = re.sub(r"^Extra\s+", "", source["category"]).strip()
        if not target["page"]:
            target["page"] = skill.get("page") or 0
            target["pageFrom"] = "2023-10b"
        if not target["properties"]:
            target["properties"] = [{"name": p["name"], "value": p["value"]} for p in skill.get("Eigenschappen", [])]
        lore = skill.get("loresheet") or {}
        if lore.get("pdf") and not target["loresheet"]["url"]:
            target["loresheet"] = {
                "url": PDF_URL.get(lore["pdf"], ""),
                "page": lore.get("page") or 1,
                "extra": PDF_URL.get(lore.get("special", ""), ""),
            }

    for target in content.values():
        if len(target["sources"]) > 1:
            report["merged"].append(f"{target['key']}: Thorfgin skills {target['sources']} merged; check the text")
        if not target["sources"]:
            report["no_text"].append(f"{target['key']} ({' / '.join(target['book'])})")

    # 3. Spells, by Thorfgin's skill ids; NL and EN files run in parallel.
    def flatten(groups):
        """Thorfgin's English file wraps some categories' skills in an extra list; unwrap them."""
        for group in groups:
            if isinstance(group, list):
                yield from flatten(group)
            else:
                yield group

    def ids_of(group):
        return tuple(sorted(group["id"] if isinstance(group["id"], list) else [group["id"]]))

    spells_nl = load(thorfgin_dir / "nl" / "spreuken.json")["Categories"]
    spells_en = load(thorfgin_dir / "en" / "spreuken.json")["Categories"]
    english_groups = {ids_of(group): group for cat in spells_en for group in flatten(cat["Skills"])}
    for cat_nl in spells_nl:
        for group_nl in flatten(cat_nl["Skills"]):
            group_en = english_groups.get(ids_of(group_nl), {"Spells": []})
            english = {spell["id"]: spell for spell in group_en["Spells"]}
            spells = []
            for spell in group_nl["Spells"]:
                en = english.get(spell["id"], {})
                spells.append({
                    "id": spell["id"],
                    "name": {"nl": spell["spell"], "en": en.get("spell", spell["spell"])},
                    "energyType": {"nl": spell.get("mana_type", ""), "en": en.get("mana_type", "")},
                    "cost": str(spell.get("mana_cost", "")),
                    "incantation": {"nl": clean(spell.get("incantation")), "en": clean(en.get("incantation"))},
                    "description": {"nl": clean(spell.get("description")), "en": clean(en.get("description"))},
                    "effect": {"nl": spell.get("spell_effect", ""), "en": en.get("spell_effect", "")},
                    "duration": {"nl": spell.get("spell_duration", ""), "en": en.get("spell_duration", "")},
                    "page": spell.get("page") or 0,
                })
            for skill_id in group_nl["id"]:
                target = sources_by_id.get(skill_id)
                if target:
                    known = {spell["id"] for spell in target["spells"]}
                    target["spells"].extend(spell for spell in spells if spell["id"] not in known)

    # 4. Recipes, the same way; a category's common recipes go to the top level.
    recipes_nl = load(thorfgin_dir / "nl" / "recepten.json")["Categories"]
    recipes_en = load(thorfgin_dir / "en" / "recepten.json")["Categories"]

    def recipe(item, en):
        return {
            "id": item["id"],
            "name": {"nl": item["recipe"], "en": en.get("recipe", item["recipe"])},
            "effect": {"nl": clean(item.get("effect")), "en": clean(en.get("effect"))},
            "inspiration": str(item.get("inspiration", "")),
            "components": {"nl": clean(item.get("components")), "en": clean(en.get("components"))},
        }

    common = []
    english_recipe_groups = {ids_of(group): group for cat in recipes_en for group in flatten(cat["Skills"])}
    for cat_nl, cat_en in zip(recipes_nl, recipes_en):
        common_en = {item["id"]: item for item in cat_en.get("Common", [])}
        if cat_nl.get("Common"):
            common.append({
                "category": {"nl": cat_nl["categorie"], "en": cat_en["categorie"]},
                "recipes": [recipe(item, common_en.get(item["id"], {})) for item in cat_nl["Common"]],
            })
        for group_nl in flatten(cat_nl["Skills"]):
            group_en = english_recipe_groups.get(ids_of(group_nl), {"Recipes": []})
            english = {item["id"]: item for item in group_en["Recipes"]}
            for skill_id in ids_of(group_nl):
                target = sources_by_id.get(skill_id)
                if target:
                    target["recipes"].extend(recipe(item, english.get(item["id"], {})) for item in group_nl["Recipes"])

    # 5. Presets, by Thorfgin's skill ids; NL and EN run in parallel.
    presets_nl = load(thorfgin_dir / "nl" / "presets.json")["Presets"]
    presets_en = load(thorfgin_dir / "en" / "presets.json")["Presets"]
    presets = []
    for preset_nl, preset_en in zip(presets_nl, presets_en):
        skills, missing = [], []
        for item in preset_nl["Skills"]:
            target = sources_by_id.get(item["id"])
            if not target:
                missing.append(item["skill"])
                continue
            existing = next((s for s in skills if s["key"] == target["key"]), None)
            if existing:
                existing["times"] += item.get("count", 1)
            else:
                skills.append({"key": target["key"], "times": item.get("count", 1)})
        if missing:
            report["presets"].append(f"{preset_nl['name']}: left out {missing}")
        presets.append({
            "key": slug(preset_nl["name"]),
            "name": {"nl": preset_nl["name"], "en": preset_en["name"]},
            "information": {"nl": clean(preset_nl.get("information")), "en": clean(preset_en.get("information"))},
            "xp": preset_nl.get("max_xp", 15),
            "newCharacter": bool(preset_nl.get("is_checked")),
            "skills": skills,
        })

    # 6. Write.
    for target in content.values():
        del target["sources"]
    out_dir.mkdir(parents=True, exist_ok=True)
    header = {
        "ruleset": RULESET,
        "book": BOOK,
        "source": "Generated by scripts/import-thorfgin-content.py from Thorfgin's charactercreator (ruleset 2023-10b, text fixes up to 2026-04) and VOID's config/SkillRequirements/vaardigheden-2026-v07.json. Pages are the 2023-10b book's until checked against the current PDF (pageFrom).",
    }
    skills_doc = dict(header, skills=list(content.values()), commonRecipes=common)
    (out_dir / "skills.json").write_text(json.dumps(skills_doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (out_dir / "presets.json").write_text(json.dumps(dict(header, presets=presets), ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    lines = [
        f"# Content import report — {RULESET}",
        "",
        "Generated by `scripts/import-thorfgin-content.py`. For SL and EC: what the import could not do by",
        "itself (DESIGN.md §3.7, steps 5 and 6). Thorfgin's text is the 2023-10b book with fixes up to",
        "2026-04; the 2026 book is on https://the-vortex.nl/het-spel/regels/.",
        "",
        f"- {len(content)} skills offered (one per VOID skill in the 2026 book), "
        f"{sum(1 for t in content.values() if t['description']['nl'])} with Thorfgin's text.",
        f"- {sum(len(t['spells']) for t in content.values())} spells and techniques, "
        f"{sum(len(t['recipes']) for t in content.values())} recipes, {len(presets)} presets.",
        "",
    ]
    sections = [
        ("no_text", "Skills without text yet (new in the 2026 book, or not in Thorfgin's data)"),
        ("merged", "Skills that took the text of several of Thorfgin's skills"),
        ("unmatched", "Thorfgin's skills not in the 2026 book (left out)"),
        ("ambiguous", "Thorfgin's skills that match a split book skill but not one VOID skill (left out)"),
        ("no_void", "2026 book skills VOID has no skill for (not offered)"),
        ("presets", "Presets that lost skills"),
    ]
    for key, title in sections:
        lines.append(f"## {title}")
        lines.append("")
        lines.extend(f"- {item}" for item in report[key]) if report[key] else lines.append("None.")
        lines.append("")
    lines.append("## Pages")
    lines.append("")
    lines.append("Every page number is the 2023-10b book's (`pageFrom`), until checked against the current PDF.")
    (out_dir / "IMPORT-REPORT.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    print(f"{len(content)} skills, {len(presets)} presets; " + ", ".join(f"{k}: {len(v)}" for k, v in report.items()))


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
