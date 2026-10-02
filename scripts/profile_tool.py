"""Pavement profile tool: type a build-up, get a hatched CAD section (DXF).

Each course is one line/argument: "<thickness> <material>[: note]"

    60 paver
    30 mortar
    200 concrete: NEW SLAB TO STRUCTURAL ENGINEER'S SPECIFICATION
    var screed            -> drawn at the material's default, dimensioned VAR.
    subgrade              -> no thickness: drawn open-bottomed, no dimension
    void                  -> (last line only) dashed void under the build-up

Usage:
    # one profile straight from the command line
    python scripts/profile_tool.py -t P3-A -n "PAVER TYPE 3 - ON GRADE" \\
        "80 paver" "30 mortar" "200 concrete" "subgrade"

    # several profiles from a text file (see profiles/pavement_schedule.txt)
    python scripts/profile_tool.py -f profiles/pavement_schedule.txt

    # no arguments: asks for the tag, title and courses one at a time
    python scripts/profile_tool.py

    python scripts/profile_tool.py --list      # show available materials

Drawn 1:1 in millimetres; annotation sized for plotting at 1:SCALE.
Change the look of any material in MATERIALS below.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

import ezdxf
from ezdxf.enums import TextEntityAlignment

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "cad"

SCALE = 10                     # intended plot scale 1:SCALE
TXT = 2.5 * SCALE              # annotation text height
TXT_TITLE = 5.0 * SCALE        # detail title text height
TXT_TAG = 7.0 * SCALE          # tag (P1-A etc.) text height
STRIP_W = 1000.0               # width of section strip
CELL_W, CELL_H = 3600.0, 1900.0
COLUMNS = 3                    # profiles per row on a multi-profile sheet
VOID_DEPTH = 250.0


# --------------------------------------------------------------------------
# THE LOOK - edit here. One entry per material keyword.
#   pattern/scale/angle : hatch (any AutoCAD acad.pat name)
#   color               : layer colour (ACI)
#   note                : default note, {t} = thickness in mm
#   default             : drawn thickness when none / "var" is given
#   joints              : spacing of vertical joint lines (pavers), 0 = none
#   open_bottom         : no bottom edge (sub-grade, soil)
#   aliases             : other words you can type for the same material
# --------------------------------------------------------------------------

@dataclass
class Material:
    layer: str
    pattern: str
    scale: float
    angle: float = 0.0
    color: int = 8
    note: str = ""
    default: float = 100.0
    joints: float = 0.0
    open_bottom: bool = False
    aliases: tuple[str, ...] = ()


MATERIALS: dict[str, Material] = {
    "paver": Material("PAVE-PAVER", "ANSI31", 20, 0, 1,
                      "{t}mm THICK PAVER, REFER LANDSCAPE SPECIFICATION",
                      60, joints=200, aliases=("pavers", "paving", "pavement")),
    "mortar": Material("PAVE-MORTAR", "AR-SAND", 1.0, 0, 8,
                       "{t}mm THICK MORTAR BED", 30, aliases=("bedding",)),
    "screed": Material("PAVE-SCREED", "AR-SAND", 2.0, 0, 9,
                       "{t}mm THICK SCREED", 50),
    "sand": Material("PAVE-SAND", "AR-SAND", 1.5, 0, 42,
                     "{t}mm THICK BEDDING SAND", 30),
    "concrete": Material("PAVE-CONC", "AR-CONC", 1.0, 0, 8,
                         "{t}mm THICK CONCRETE", 200,
                         aliases=("conc", "slab", "concrete slab")),
    "asphalt": Material("PAVE-ASPHALT", "AR-SAND", 0.6, 0, 250,
                        "{t}mm THICK ASPHALT", 40,
                        aliases=("ac", "bitumen", "hotmix")),
    "dgb20": Material("PAVE-ROADBASE", "GRAVEL", 8, 0, 8,
                      "{t}mm THICK DGB20 ROAD BASE", 150,
                      aliases=("dgb", "roadbase", "road base", "basecourse")),
    "subbase": Material("PAVE-SUBBASE", "GRAVEL", 14, 0, 33,
                        "{t}mm THICK SUB-BASE", 150, aliases=("sub-base",)),
    "gravel": Material("PAVE-GRAVEL", "GRAVEL", 5, 0, 33,
                       "{t}mm THICK GRAVEL", 100, aliases=("crushed rock",)),
    "subgrade": Material("PAVE-SUBGRADE", "EARTH", 12, 45, 8,
                         "COMPACTED SUB-GRADE TO 98% MDD STANDARD", 200,
                         open_bottom=True, aliases=("sub-grade", "ground")),
    "soil": Material("PAVE-SOIL", "EARTH", 12, 45, 52,
                     "{t}mm THICK SOIL", 400, open_bottom=True,
                     aliases=("topsoil", "landscaping", "planting")),
}

_LOOKUP = {k: k for k in MATERIALS}
for _k, _m in MATERIALS.items():
    for _a in _m.aliases:
        _LOOKUP[_a] = _k


# --------------------------------------------------------------------------
# Input parsing
# --------------------------------------------------------------------------

@dataclass
class Course:
    material: Material
    thickness: float          # drawn thickness (mm)
    note: str
    dim: str | None = None    # dimension text; None = no dimension


@dataclass
class Profile:
    tag: str
    title: str
    courses: list[Course] = field(default_factory=list)
    void_below: bool = False


_COURSE_RE = re.compile(
    r"^\s*(?:(?P<t>\d+(?:\.\d+)?|var(?:iable|ies)?)\s*(?:mm)?\s+)?"
    r"(?P<mat>[^:]+?)\s*(?::\s*(?P<note>.+))?\s*$", re.I)


def parse_course(line: str) -> Course | None:
    """Parse "200 concrete[: note]". Returns None for a "void" line."""
    m = _COURSE_RE.match(line)
    if not m:
        raise ValueError(f"can't read course: {line!r}")
    word = m["mat"].strip().lower()
    if word == "void":
        return None
    key = _LOOKUP.get(word)
    if key is None:
        raise ValueError(f"unknown material {word!r} in {line!r}. "
                         f"Known: {', '.join(sorted(_LOOKUP))}")
    mat = MATERIALS[key]
    t = m["t"]
    if t is None:
        thick, dim = mat.default, None
    elif t.lower().startswith("var"):
        thick, dim = mat.default, "VAR."
    else:
        thick = float(t)
        dim = f"{thick:g}"
    if m["note"]:
        note = m["note"]
    elif dim == "VAR.":
        note = mat.note.replace("{t}mm THICK", "VARIABLE")
    else:
        note = mat.note.format(t=f"{thick:g}")
    note = re.sub(r"(\d)MM\b", r"\1mm", note.strip().upper())
    return Course(mat, thick, note, dim)


def make_profile(tag: str, title: str, lines: list[str]) -> Profile:
    p = Profile(tag.upper(), title.upper())
    for i, line in enumerate(lines):
        c = parse_course(line)
        if c is None:
            if i != len(lines) - 1:
                raise ValueError(f"{tag}: 'void' must be the last line")
            p.void_below = True
        else:
            p.courses.append(c)
    if not p.courses:
        raise ValueError(f"{tag}: no courses given")
    return p


def read_file(path: Path) -> list[Profile]:
    """Blocks of "[TAG] TITLE" followed by course lines. '#' = comment."""
    profiles, head, lines = [], None, []
    for raw in path.read_text().splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        h = re.match(r"^\[(?P<tag>[^\]]+)\]\s*(?P<title>.*)$", line)
        if h:
            if head:
                profiles.append(make_profile(*head, lines))
            head, lines = (h["tag"], h["title"]), []
        elif head is None:
            raise ValueError(f"{path}: course before any [TAG] line: {line!r}")
        else:
            lines.append(line)
    if head:
        profiles.append(make_profile(*head, lines))
    return profiles


def ask() -> list[Profile]:
    print("Pavement profile tool. Materials: " + ", ".join(MATERIALS))
    tag = input("Tag (e.g. P3-A): ").strip() or "P1"
    title = input("Title (e.g. PAVER TYPE 3 - ON GRADE): ").strip() or tag
    print("Courses top to bottom, e.g. '200 concrete'. Blank line to finish.")
    lines = []
    while (line := input(f"  course {len(lines) + 1}: ").strip()):
        try:
            parse_course(line)
        except ValueError as e:
            print(f"  ! {e}")
            continue
        lines.append(line)
    return [make_profile(tag, title, lines)]


# --------------------------------------------------------------------------
# Drawing
# --------------------------------------------------------------------------

def setup(doc: ezdxf.document.Drawing) -> None:
    doc.units = ezdxf.units.MM
    doc.header["$INSUNITS"] = 4
    doc.header["$MEASUREMENT"] = 1
    doc.header["$LTSCALE"] = SCALE
    doc.styles.add("PAVE-TEXT", font="arial.ttf")
    for name, color in [
        ("PAVE-OUTLINE", 7), ("PAVE-ANNO", 2), ("PAVE-DIM", 3),
        ("PAVE-TITLE", 1), ("PAVE-BREAK", 7), ("PAVE-VOID", 8),
    ]:
        doc.layers.add(name, color=color)
    for m in MATERIALS.values():
        if m.layer not in doc.layers:
            doc.layers.add(m.layer, color=m.color)
    if "DASHED" not in doc.linetypes:
        doc.linetypes.add("DASHED", pattern=[0.75, 0.5, -0.25],
                          description="Dashed __ __ __")


def text(msp, s, x, y, h=TXT, layer="PAVE-ANNO",
         align=TextEntityAlignment.MIDDLE_LEFT):
    t = msp.add_text(s, height=h, dxfattribs={"layer": layer,
                                              "style": "PAVE-TEXT"})
    t.set_placement((x, y), align=align)
    return t


def hatch(msp, m: Material, pts):
    h = msp.add_hatch(color=256, dxfattribs={"layer": m.layer})
    h.set_pattern_fill(m.pattern, scale=m.scale, angle=m.angle, color=256)
    h.paths.add_polyline_path(pts, is_closed=True)


def break_line(msp, x, y_top, y_bot):
    """Vertical break line with a zig-zag at mid height."""
    ym = (y_top + y_bot) / 2
    a, z = 20.0, 15.0
    pts = [(x, y_top + 30), (x, ym + z * 2), (x - a, ym + z),
           (x + a, ym - z), (x, ym - z * 2), (x, y_bot - 30)]
    msp.add_lwpolyline(pts, dxfattribs={"layer": "PAVE-BREAK"})


def vdim(msp, x, y1, y2, label):
    """Simple vertical dimension with architectural ticks."""
    lay = {"layer": "PAVE-DIM"}
    msp.add_line((x, y1), (x, y2), dxfattribs=lay)
    for y in (y1, y2):
        msp.add_line((x - 40, y), (x + 15, y), dxfattribs=lay)
        msp.add_line((x - 12, y - 12), (x + 12, y + 12), dxfattribs=lay)
    if abs(y1 - y2) < TXT * 2.5:
        # too tight for rotated text: write it horizontally beside the dim
        text(msp, label, x - 50, (y1 + y2) / 2, layer="PAVE-DIM",
             align=TextEntityAlignment.MIDDLE_RIGHT)
        return
    t = msp.add_text(label, height=TXT, rotation=90,
                     dxfattribs={"layer": "PAVE-DIM", "style": "PAVE-TEXT"})
    t.set_placement((x - 20, (y1 + y2) / 2),
                    align=TextEntityAlignment.BOTTOM_CENTER)


def level_marker(msp, x, y, label):
    lay = {"layer": "PAVE-ANNO"}
    s = 30
    msp.add_lwpolyline([(x, y), (x - s, y + s * 1.4), (x + s, y + s * 1.4)],
                       close=True, dxfattribs=lay)
    msp.add_line((x - s * 2, y + s * 1.4), (x + 400, y + s * 1.4),
                 dxfattribs=lay)
    text(msp, label, x + s * 1.5, y + s * 1.4 + 10,
         align=TextEntityAlignment.BOTTOM_LEFT)


def leader(msp, start, knee_x, elbow_y, text_x, note):
    """Dot at the course, leader to an elbow, then horizontal to the note."""
    lay = {"layer": "PAVE-ANNO"}
    msp.add_circle(start, 6, dxfattribs=lay)
    dot = msp.add_hatch(color=256, dxfattribs=lay)
    dot.paths.add_edge_path().add_arc(start, 6, 0, 360)
    msp.add_lwpolyline([start, (knee_x, elbow_y), (text_x - 15, elbow_y)],
                       dxfattribs=lay)
    text(msp, note, text_x, elbow_y)


def tag_box(msp, tag, x, y):
    bs, bw = TXT_TAG * 2.2, TXT_TAG * 3.4
    msp.add_lwpolyline([(x, y), (x + bw, y), (x + bw, y - bs), (x, y - bs)],
                       close=True, dxfattribs={"layer": "PAVE-TITLE"})
    text(msp, tag, x + bw / 2, y - bs / 2, h=TXT_TAG, layer="PAVE-TITLE",
         align=TextEntityAlignment.MIDDLE_CENTER)
    return bw, bs


def draw_profile(msp, p: Profile, ox: float, oy: float) -> None:
    """Draw one profile with its finished surface at (ox, oy)."""
    x0, x1 = ox, ox + STRIP_W
    lay = {"layer": "PAVE-OUTLINE"}
    y, bands = oy, []
    for c in p.courses:
        yb = y - c.thickness
        hatch(msp, c.material, [(x0, y), (x1, y), (x1, yb), (x0, yb)])
        msp.add_line((x0, y), (x1, y), dxfattribs=lay)
        if not c.material.open_bottom:
            msp.add_line((x0, yb), (x1, yb), dxfattribs=lay)
        if c.material.joints:
            x = x0 + c.material.joints / 2
            while x < x1:
                msp.add_line((x, y), (x, yb), dxfattribs=lay)
                x += c.material.joints
        bands.append((c, y, yb))
        y = yb

    bottom = lowest = y
    if p.void_below:
        lowest = bottom - VOID_DEPTH
        msp.add_lwpolyline([(x0, bottom), (x0, lowest), (x1, lowest),
                            (x1, bottom)],
                           dxfattribs={"layer": "PAVE-VOID",
                                       "linetype": "DASHED"})
        text(msp, "VOID", (x0 + x1) / 2, (bottom + lowest) / 2, h=TXT * 1.4,
             layer="PAVE-VOID", align=TextEntityAlignment.MIDDLE_CENTER)

    break_line(msp, x0, oy, bottom)
    break_line(msp, x1, oy, bottom)

    for c, yt, yb in bands:
        if c.dim:
            vdim(msp, x0 - 120, yt, yb, c.dim)

    level_marker(msp, x0 + 150, oy, "FINISHED SURFACE LEVEL")

    # notes on the right, one per course, kept level with the course where
    # possible and pushed down only as needed so leaders never cross
    text_x, row = x1 + 300, TXT * 2.4
    elbow_y = oy + row
    for i, (c, yt, yb) in enumerate(bands):
        elbow_y = min((yt + yb) / 2, elbow_y - row)
        sx = x0 + STRIP_W * min(0.55 + 0.08 * i, 0.9)
        leader(msp, (sx, (yt + yb) / 2), x1 + 120, elbow_y, text_x, c.note)

    # title: tag box + title + scale, aligned across a row of profiles
    ty = min(oy - 850, lowest - 200, elbow_y - 200)
    bw, bs = tag_box(msp, p.tag, x0, ty)
    text(msp, p.title, x0 + bw + 60, ty - bs * 0.35, h=TXT_TITLE,
         layer="PAVE-TITLE")
    msp.add_line((x0 + bw + 60, ty - bs * 0.62), (x1 + 1500, ty - bs * 0.62),
                 dxfattribs={"layer": "PAVE-TITLE"})
    text(msp, f"SCALE 1:{SCALE}", x0 + bw + 60, ty - bs * 0.82,
         h=TXT * 1.2, layer="PAVE-TITLE")


def draw_schedule(msp, profiles: list[Profile], ox: float, oy: float) -> None:
    """Pavement schedule listing each profile's build-up, top down."""
    text(msp, "PAVEMENT SCHEDULE", ox, oy, h=TXT_TITLE, layer="PAVE-TITLE",
         align=TextEntityAlignment.BOTTOM_LEFT)
    y = oy - 150
    for p in profiles:
        bw, bs = tag_box(msp, p.tag, ox, y)
        tx = ox + bw + 60
        text(msp, p.title, tx, y - TXT * 1.4, h=TXT * 1.6, layer="PAVE-TITLE")
        ly = y - TXT * 1.4 - TXT * 2.2
        for i, c in enumerate(p.courses):
            last = i == len(p.courses) - 1
            line = c.note + ("." if last else ", ON")
            text(msp, line, tx, ly, layer="PAVE-TITLE")
            ly -= TXT * 1.6
        y = min(y - bs, ly) - 120


def build(profiles: list[Profile], schedule: bool) -> ezdxf.document.Drawing:
    doc = ezdxf.new("R2013", setup=True)
    setup(doc)
    msp = doc.modelspace()
    for i, p in enumerate(profiles):
        col, row = i % COLUMNS, i // COLUMNS
        draw_profile(msp, p, 400 + col * CELL_W, -400 - row * CELL_H)
    if schedule:
        cols = min(len(profiles), COLUMNS)
        draw_schedule(msp, profiles, 400 + cols * CELL_W, -300)
    return doc


def preview(doc, stem: Path) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from ezdxf.addons.drawing import Frontend, RenderContext
    from ezdxf.addons.drawing.config import (BackgroundPolicy, ColorPolicy,
                                             Configuration)
    from ezdxf.addons.drawing.matplotlib import MatplotlibBackend

    fig = plt.figure(figsize=(24, 14))
    ax = fig.add_axes([0, 0, 1, 1])
    cfg = Configuration(background_policy=BackgroundPolicy.WHITE,
                        color_policy=ColorPolicy.BLACK)
    Frontend(RenderContext(doc), MatplotlibBackend(ax), config=cfg) \
        .draw_layout(doc.modelspace())
    for ext in (".png", ".pdf"):
        fig.savefig(stem.with_suffix(ext), dpi=150)
    plt.close(fig)


def main() -> None:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("courses", nargs="*", help='e.g. "200 concrete"')
    ap.add_argument("-t", "--tag", default="P1", help="detail tag, e.g. P3-A")
    ap.add_argument("-n", "--title", help="detail title")
    ap.add_argument("-f", "--file", type=Path, help="profiles text file")
    ap.add_argument("-o", "--out", type=Path, help="output .dxf path")
    ap.add_argument("--schedule", action="store_true",
                    help="add a pavement schedule (always on with -f)")
    ap.add_argument("--preview", action="store_true",
                    help="also write PNG/PDF previews")
    ap.add_argument("--list", action="store_true", help="list materials")
    args = ap.parse_args()

    if args.list:
        for k, m in MATERIALS.items():
            alias = f"  (also: {', '.join(m.aliases)})" if m.aliases else ""
            print(f"{k:10} {m.pattern:8} default {m.default:g}mm{alias}")
        return

    try:
        if args.file:
            profiles = read_file(args.file)
            default_out = OUT_DIR / f"{args.file.stem}.dxf"
        elif args.courses:
            profiles = [make_profile(args.tag, args.title or args.tag,
                                     args.courses)]
            default_out = OUT_DIR / f"{profiles[0].tag}.dxf"
        else:
            profiles = ask()
            default_out = OUT_DIR / f"{profiles[0].tag}.dxf"
    except ValueError as e:
        sys.exit(f"error: {e}")

    out = args.out or default_out
    out.parent.mkdir(parents=True, exist_ok=True)
    doc = build(profiles, schedule=args.schedule or bool(args.file))
    doc.saveas(out)
    print(f"wrote {out}")
    if args.preview:
        preview(doc, out.with_name(out.stem + "_preview"))
        print("wrote previews")


if __name__ == "__main__":
    main()
