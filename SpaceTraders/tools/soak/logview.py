"""Prints the bot's JSON log readably: time, level, message. Masks anything shaped like a JWT.

Usage: python logview.py [--out soak-output] [--from N] [--to N] [--grep REGEX] [--level Warning,Error] [--props] [--tail N]
"""
import argparse
import json
import pathlib
import re

JWT = re.compile(r"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+|eyJ[A-Za-z0-9_\-]{10,}")
SKIP_PROPS = {"@t", "@m", "@i", "@l", "@x", "@r", "Application"}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--from", dest="start", type=int, default=1)
    parser.add_argument("--to", dest="end", type=int, default=0)
    parser.add_argument("--grep", default="")
    parser.add_argument("--level", default="")
    parser.add_argument("--props", action="store_true")
    parser.add_argument("--tail", type=int, default=0)
    parser.add_argument("--exceptions", action="store_true")
    parser.add_argument("--out", default="soak-output", help="folder with the bot's log")
    args = parser.parse_args()
    out = pathlib.Path(args.out).resolve()

    levels = {lvl.strip() for lvl in args.level.split(",") if lvl.strip()}
    pattern = re.compile(args.grep) if args.grep else None
    out = []
    with open(out / "logs" / "app.log", "rb") as f:
        for number, raw in enumerate(f, start=1):
            if number < args.start or (args.end and number > args.end):
                continue
            line = JWT.sub("<jwt>", raw.decode("utf-8", "replace").replace("\x1a", "?").strip())
            if not line:
                continue
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                out.append(f"{number:6d} NONJSON {line[:300]}")
                continue
            level = event.get("@l", "Information")
            if levels and level not in levels:
                continue
            message = event.get("@m", "")
            text = f"{number:6d} {event.get('@t', '')[11:23]} {level[:4]:4} {message}"
            if args.props:
                props = {k: v for k, v in event.items() if k not in SKIP_PROPS}
                text += f"  {json.dumps(props)[:400]}"
            if args.exceptions and "@x" in event:
                text += "\n        " + event["@x"][:1500].replace("\n", "\n        ")
            if pattern and not pattern.search(text):
                continue
            out.append(text)
    for text in out[-args.tail:] if args.tail else out:
        print(text)


if __name__ == "__main__":
    main()
