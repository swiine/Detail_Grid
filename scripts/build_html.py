"""Build Pavement_Profile_Tool.html from scripts/html_template.html.

Injects the data the browser tool needs, taken from the Python tool so the
two stay in step:
  - MATERIALS (the look) from profile_tool.py
  - hatch pattern definitions (ezdxf's ISO set)
  - a DXF skeleton (header, layers, text style, linetype) made by ezdxf
  - the example build-up from profiles/pavement_schedule.txt

Usage:
    python scripts/build_html.py
"""

from __future__ import annotations

import io
import json
import sys
from pathlib import Path

import ezdxf
from ezdxf.tools import pattern

sys.path.insert(0, str(Path(__file__).resolve().parent))
import profile_tool as pt  # noqa: E402

ROOT = pt.ROOT
TEMPLATE = ROOT / "scripts" / "html_template.html"
OUT = ROOT / "Pavement_Profile_Tool.html"
EXAMPLE = ROOT / "profiles" / "pavement_schedule.txt"

# patterns shipped in the page: those the materials use, plus common extras
# so a material can be switched to one of these by editing the HTML alone
EXTRA_PATTERNS = [
    "ANSI31", "ANSI32", "ANSI33", "ANSI34", "ANSI35", "ANSI36", "ANSI37",
    "ANSI38", "AR-B816", "AR-B88", "AR-BRSTD", "AR-CONC", "AR-HBONE",
    "AR-RROOF", "AR-SAND", "BRICK", "CLAY", "CROSS", "DASH", "DOTS",
    "EARTH", "GRASS", "GRAVEL", "HEX", "HONEY", "INSUL", "LINE", "NET",
    "SQUARE", "SWAMP",
]


def js_materials() -> str:
    rows = []
    for key, m in pt.MATERIALS.items():
        fields = {
            "layer": m.layer, "pattern": m.pattern, "scale": m.scale,
            "angle": m.angle, "color": m.color, "note": m.note,
            "default": m.default, "joints": m.joints,
            "open_bottom": m.open_bottom, "aliases": list(m.aliases),
        }
        body = ", ".join(f"{k}: {json.dumps(v)}" for k, v in fields.items())
        rows.append(f"  {json.dumps(key)}: {{ {body} }},")
    return "const MATERIALS = {\n" + "\n".join(rows) + "\n};"


def js_patterns() -> str:
    names = sorted({m.pattern for m in pt.MATERIALS.values()} | set(EXTRA_PATTERNS))
    missing = [n for n in names if n not in pattern.ISO_PATTERN]
    if missing:
        sys.exit(f"unknown hatch pattern(s): {', '.join(missing)}")
    defs = {
        n: [[a, list(b), list(o), list(d)] for a, b, o, d in pattern.ISO_PATTERN[n]]
        for n in names
    }
    return "const PATTERNS = " + json.dumps(defs, separators=(",", ":")) + ";"


def js_dxf_skeleton() -> str:
    doc = ezdxf.new("R2000", setup=True)
    pt.setup(doc)
    owner = doc.modelspace().block_record_handle
    buf = io.StringIO()
    doc.write(buf)
    text = buf.getvalue()
    marker = "  0\nSECTION\n  2\nENTITIES\n"
    i = text.index(marker) + len(marker)
    j = text.index("  0\nENDSEC\n", i)
    if text[i:j].strip():
        sys.exit("skeleton modelspace is not empty")
    seed = doc.entitydb.handles.next()  # first free handle
    skel = {"head": text[:i], "tail": text[j:], "owner": owner, "handseed": seed}
    return "const DXF = " + json.dumps(skel) + ";"


def js_example() -> str:
    lines = EXAMPLE.read_text().splitlines()
    while lines and (lines[0].startswith("#") or not lines[0].strip()):
        lines.pop(0)  # drop the file's own usage header
    head = ["# [TAG] TITLE starts a profile, then one course per line, top down.",
            "# e.g.  200 concrete   |   var screed   |   subgrade   |   void", ""]
    return "const EXAMPLE = " + json.dumps("\n".join(head + lines) + "\n") + ";"


def main() -> None:
    data = "\n".join([
        "// ==========================================================================",
        "// THE LOOK: one entry per material keyword. Edit to suit your standards.",
        "//   pattern/scale/angle : hatch (name from PATTERNS below)",
        "//   layer/color         : CAD layer and its colour (ACI)",
        "//   note                : default note, {t} = thickness in mm",
        "//   default             : drawn thickness when none / 'var' is given",
        "//   joints              : spacing of vertical joint lines (pavers), 0 = none",
        "//   open_bottom         : no bottom edge (sub-grade, soil)",
        "//   aliases             : other words you can type for the material",
        "// Generated from scripts/profile_tool.py by scripts/build_html.py.",
        "// ==========================================================================",
        js_materials(),
        "",
        "// Hatch pattern definitions at scale 1 (ISO), as used by ezdxf/AutoCAD.",
        js_patterns(),
        "",
        "// Empty DXF made by ezdxf: header, layers, text style and linetype.",
        js_dxf_skeleton(),
        "",
        js_example(),
    ])
    html = TEMPLATE.read_text()
    if "/*@@DATA@@*/" not in html:
        sys.exit("template is missing the /*@@DATA@@*/ marker")
    OUT.write_text(html.replace("/*@@DATA@@*/", data))
    print(f"wrote {OUT} ({OUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
