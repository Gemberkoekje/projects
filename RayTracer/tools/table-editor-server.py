#!/usr/bin/env python3
"""Local dev server for tools/table-editor.html.

Serves the tools/ directory (so the editor + DatBlueprint/blueprint.svg load) AND exposes
the game's live table so the editor can load from / save to it directly:

    GET  /api/table   -> 200 with Pinball.App/table.json, or 404 if it doesn't exist yet
    PUT  /api/table   -> validate + atomically overwrite Pinball.App/table.json (400 on bad JSON)

Only that one fixed file is ever written -- the body is parsed as JSON first, so a malformed
save can't corrupt the table the game loads. Binds to 127.0.0.1 only (never the network).

Run:  python tools/table-editor-server.py          (from the repo root or tools/)
      python tools/table-editor-server.py --port 8792 --table ../Pinball.App/table.json
Then open the URL it prints.
"""
import argparse
import json
import os
import sys
import tempfile
from http.server import HTTPServer, SimpleHTTPRequestHandler

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
# tools/ sits next to Pinball.App/ under the repo root.
DEFAULT_TABLE = os.path.normpath(os.path.join(TOOLS_DIR, "..", "Pinball.App", "table.json"))


class Handler(SimpleHTTPRequestHandler):
    table_path = DEFAULT_TABLE

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=TOOLS_DIR, **kwargs)

    # ---- JSON helpers ----
    def _json(self, code, obj):
        body = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    # ---- routes ----
    def do_GET(self):
        if self.path.split("?", 1)[0] == "/api/table":
            if not os.path.exists(self.table_path):
                return self._json(404, {"error": "table.json not found", "path": self.table_path})
            try:
                with open(self.table_path, "rb") as fh:
                    data = fh.read()
            except OSError as e:
                return self._json(500, {"error": str(e)})
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(data)
            return
        return super().do_GET()

    def do_PUT(self):
        if self.path.split("?", 1)[0] != "/api/table":
            return self._json(404, {"error": "unknown endpoint"})
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length else b""
        # Validate before touching the file the game loads.
        try:
            obj = json.loads(raw.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as e:
            return self._json(400, {"error": "not valid JSON: %s" % e})
        if not isinstance(obj, dict) or not isinstance(obj.get("walls"), list):
            return self._json(400, {"error": "expected a table object with a 'walls' array"})
        # Pretty-print so the on-disk file stays diff-friendly, then write atomically.
        pretty = json.dumps(obj, indent=2).encode("utf-8")
        try:
            os.makedirs(os.path.dirname(self.table_path), exist_ok=True)
            fd, tmp = tempfile.mkstemp(dir=os.path.dirname(self.table_path), suffix=".tmp")
            with os.fdopen(fd, "wb") as fh:
                fh.write(pretty)
            os.replace(tmp, self.table_path)
        except OSError as e:
            return self._json(500, {"error": str(e)})
        return self._json(200, {"ok": True, "bytes": len(pretty), "path": self.table_path})

    # POST behaves like PUT for convenience (older fetch code / sendBeacon).
    do_POST = do_PUT

    def log_message(self, fmt, *args):
        sys.stderr.write("  %s - %s\n" % (self.address_string(), fmt % args))


def main():
    ap = argparse.ArgumentParser(description="Serve the table editor + live table.json read/write.")
    ap.add_argument("--port", type=int, default=8792)
    ap.add_argument("--table", default=DEFAULT_TABLE, help="path to the game's table.json")
    args = ap.parse_args()

    Handler.table_path = os.path.abspath(args.table)
    httpd = HTTPServer(("127.0.0.1", args.port), Handler)
    print("Table editor server")
    print("  editor : http://localhost:%d/table-editor.html" % args.port)
    print("  table  : %s%s" % (Handler.table_path, "" if os.path.exists(Handler.table_path) else "  (missing — will be created on first save)"))
    print("  serving: %s" % TOOLS_DIR)
    print("Ctrl+C to stop.")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nstopped.")


if __name__ == "__main__":
    main()
