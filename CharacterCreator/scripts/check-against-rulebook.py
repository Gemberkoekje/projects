"""Checks the content against the current rulebook PDFs and fixes what it can.

DESIGN.md §3.7 (steps 2 and 5) and phase 2. Reads the skills book (Vaardigheden.pdf) and the spells
book (Spreuken_en_Technieken.pdf) from https://the-vortex.nl/het-spel/regels/, then:

- sets each skill's page to where its heading is in the current skills book ("Naam (2 punten)"),
  and records the cost the book prints as bookCost, so the VOID import can compare it with VOID;
- sets each spell's page to where it is in the current spells book;
- appends what it could not find to IMPORT-REPORT.md.

Usage:
    python -I scripts/check-against-rulebook.py <content dir> <Vaardigheden.pdf> <Spreuken_en_Technieken.pdf>
"""

import json
import re
import sys
import unicodedata
from pathlib import Path

import pypdf


def norm(text):
    text = unicodedata.normalize("NFKD", text or "").encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def pages_of(pdf_path):
    reader = pypdf.PdfReader(pdf_path)
    pages = [norm(page.extract_text() or "") for page in reader.pages]
    version = re.search(r"vortex adventures (\d{4}) (v\d+)", " ".join(pages[:3]))
    return pages, (f"{version.group(1)}-{version.group(2)}" if version else "")


def main(content_dir, skills_pdf, spells_pdf):
    content_dir = Path(content_dir)
    skills_path = content_dir / "skills.json"
    doc = json.loads(skills_path.read_text(encoding="utf-8"))

    skill_pages, skills_version = pages_of(skills_pdf)
    spell_pages, spells_version = pages_of(spells_pdf)
    if skills_version != doc["ruleset"] or spells_version != doc["ruleset"]:
        sys.exit(f"The PDFs are {skills_version} and {spells_version}, the content is {doc['ruleset']}: import the new book first.")

    missing_skills, missing_spells = [], []
    for skill in doc["skills"]:
        found = None
        for name in skill["book"]:
            # "Naam (2 punten)" is in the heading and in the contents at the front, nowhere else (a
            # prerequisite names a skill without its points), so the last page with it is the heading's.
            pattern = re.compile(r"\b" + re.escape(norm(name)) + r" (\d+) punt")
            for number, text in enumerate(skill_pages, start=1):
                match = pattern.search(text)
                if match:
                    found = (number, int(match.group(1)))
            if found:
                break
        if found:
            skill["page"], skill["bookCost"] = found
            skill["pageFrom"] = doc["ruleset"]
        else:
            skill["bookCost"] = -1
            missing_skills.append(f"{skill['key']} ({' / '.join(skill['book'])}): page left at {skill['page']} ({skill['pageFrom']})")

        for spell in skill["spells"]:
            name = norm(spell["name"]["nl"])
            cost = norm(spell["cost"])
            hits = [number for number, text in enumerate(spell_pages, start=1) if number > 3 and f" {name} {cost} " in f" {text} "]
            hits = hits or [number for number, text in enumerate(spell_pages, start=1) if number > 3 and f" {name} " in f" {text} "]
            if hits:
                spell["page"] = hits[0]
            else:
                missing_spells.append(f"{spell['name']['nl']} ({skill['key']}): page left at {spell['page']}")

    doc["source"] = re.sub(r" Pages are .*$", "", doc["source"]) + f" Pages and bookCost checked against the {doc['ruleset']} PDFs by scripts/check-against-rulebook.py; see IMPORT-REPORT.md for what was not found."
    skills_path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    report_path = content_dir / "IMPORT-REPORT.md"
    report = report_path.read_text(encoding="utf-8")
    report = report[: report.index("## Pages")] if "## Pages" in report else report
    unique_spells = sorted(set(missing_spells))
    lines = [
        "## Pages",
        "",
        f"Checked against the current PDFs ({doc['ruleset']}) by `scripts/check-against-rulebook.py`: "
        f"{len(doc['skills']) - len(missing_skills)} of {len(doc['skills'])} skills found in the skills book, "
        f"with the cost it prints (`bookCost`).",
        "",
        "### Skills not found in the skills book (page kept from 2023-10b)",
        "",
    ]
    lines.extend(f"- {item}" for item in missing_skills) if missing_skills else lines.append("None.")
    lines += ["", "### Spells and techniques not found in the spells book (page kept from 2023-10b)", ""]
    lines.extend(f"- {item}" for item in unique_spells) if unique_spells else lines.append("None.")
    report_path.write_text(report + "\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    print(f"{doc['ruleset']}: skills found {len(doc['skills']) - len(missing_skills)}/{len(doc['skills'])}, spells not found {len(unique_spells)}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
