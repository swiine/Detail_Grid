"""Check that Pavement_Profile_Tool.html draws the same DXF as profile_tool.py.

Runs the page's script in Node on a profiles file, then compares every entity
(geometry, layers, text, hatch pattern lines) with the Python tool's output
and audits the browser DXF with ezdxf.

Usage:
    python scripts/check_html.py [profiles.txt]     (needs node on PATH)
"""

from __future__ import annotations

import io
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import ezdxf

sys.path.insert(0, str(Path(__file__).resolve().parent))
import profile_tool as pt  # noqa: E402

HTML = pt.ROOT / "Pavement_Profile_Tool.html"


def browser_dxf(src: str) -> str:
    html = HTML.read_text()
    script = re.search(r"<script>(.*?)</script>", html, re.S)[1]
    core = script[:script.index("// @@CORE-END")]
    js = core + (
        "\nconst src = require('fs').readFileSync(0, 'utf8');"
        "\nconst {profiles, errors} = parseText(src);"
        "\nif (errors.length) { console.error(JSON.stringify(errors)); process.exit(2); }"
        "\nprocess.stdout.write(toDXF(build(profiles, {columns: 3, schedule: true})));"
    )
    with tempfile.NamedTemporaryFile("w", suffix=".js", delete=False) as f:
        f.write(js)
    res = subprocess.run(["node", f.name], input=src, capture_output=True,
                         text=True, check=False)
    if res.returncode:
        sys.exit(f"node failed:\n{res.stderr}")
    return res.stdout


def signature(doc) -> list:
    r = lambda v: round(float(v), 4)  # noqa: E731
    out = []
    for e in doc.modelspace():
        t, a = e.dxftype(), e.dxf
        if t == "LINE":
            s = (r(a.start.x), r(a.start.y), r(a.end.x), r(a.end.y))
        elif t == "LWPOLYLINE":
            s = (tuple((r(x), r(y)) for x, y in e.get_points("xy")),
                 e.closed, a.get("linetype", "BYLAYER"))
        elif t == "CIRCLE":
            s = (r(a.center.x), r(a.center.y), r(a.radius))
        elif t == "TEXT":
            s = (a.text, r(a.insert.x), r(a.insert.y), r(a.height),
                 r(a.get("rotation", 0)), a.get("halign", 0), a.get("valign", 0),
                 a.style)
        elif t == "HATCH":
            lines = [] if a.solid_fill else [
                (r(l.angle), r(l.base_point.x), r(l.base_point.y), r(l.offset.x),
                 r(l.offset.y), tuple(r(d) for d in l.dash_length_items))
                for l in e.pattern.lines]
            s = (a.pattern_name, a.solid_fill, len(e.paths), lines)
        else:
            s = ()
        out.append((t, a.layer) + tuple(s))
    return out


def main() -> None:
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else pt.ROOT / "profiles" / "pavement_schedule.txt"
    # the browser tool is built with the R2000 skeleton; compare like with like
    ref = pt.build(pt.read_file(path), schedule=True)
    ref_sig = signature(ref)

    text = browser_dxf(path.read_text())
    doc = ezdxf.read(io.StringIO(text))
    auditor = doc.audit()
    if auditor.has_errors:
        sys.exit(f"audit errors: {[str(e) for e in auditor.errors]}")
    sig = signature(doc)

    if sig != ref_sig:
        for i, (a, b) in enumerate(zip(sig, ref_sig)):
            if a != b:
                sys.exit(f"entity {i} differs:\n  html:   {a}\n  python: {b}")
        sys.exit(f"entity count differs: html {len(sig)}, python {len(ref_sig)}")
    print(f"OK: {len(sig)} entities match, audit clean "
          f"({sum(1 for s in sig if s[0] == 'HATCH')} hatches)")


if __name__ == "__main__":
    main()
