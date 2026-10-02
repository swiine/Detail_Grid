"""Generate pavement profile section details as a DXF for CAD.

Drawn 1:1 in millimetres (model space). Annotation is sized for plotting
at 1:10 (2.5 mm text on paper = 25 mm in model space).

Usage:
    python scripts/pavement_profiles.py            # writes cad/Pavement_Profiles.dxf
    python scripts/pavement_profiles.py --preview  # also writes PNG + PDF previews
"""

from __future__ import annotations

import argparse
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

PAVER_NOTE = "60mm THICK PAVER, REFER LANDSCAPE SPECIFICATION"
MORTAR_NOTE = "30mm THICK MORTAR BED"
SCREED_NOTE = "VARIABLE SCREED, REFER DRAWING CV-TTW-1030"
EX_SLAB_NOTE = "EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)"
DGB_NOTE = "150mm THICK DGB20 ROAD BASE"
SUBGRADE_NOTE = "COMPACTED SUB-GRADE TO 98% MDD STANDARD"
NEW_SLAB_NOTE = "NEW SLAB TO STRUCTURAL ENGINEER'S SPECIFICATION"
LANDSCAPE_NOTE = "LANDSCAPING, REFER LANDSCAPE SPECIFICATION"


@dataclass
class Material:
    layer: str
    pattern: str
    pattern_scale: float
    pattern_angle: float = 0.0
    color: int = 8


PAVER = Material("PAVE-PAVER", "ANSI31", 20, 0, 1)
MORTAR = Material("PAVE-MORTAR", "AR-SAND", 1.0, 0, 8)
SCREED = Material("PAVE-SCREED", "AR-SAND", 2.0, 0, 9)
CONCRETE = Material("PAVE-CONC", "AR-CONC", 1.0, 0, 8)
ROADBASE = Material("PAVE-ROADBASE", "GRAVEL", 8, 0, 8)
SUBGRADE = Material("PAVE-SUBGRADE", "EARTH", 12, 45, 8)
SOIL = Material("PAVE-SOIL", "EARTH", 12, 45, 52)


@dataclass
class Course:
    material: Material
    thickness: float          # drawn thickness (mm)
    note: str
    dim: str | None = None    # dimension text; None = no dimension
    open_bottom: bool = False  # e.g. sub-grade: no bottom edge
    joints: float = 0.0       # spacing of vertical joints (pavers)


@dataclass
class Profile:
    tag: str
    title: str
    courses: list[Course]
    void_below: bool = False
    extra_notes: list[str] = field(default_factory=list)


def paver_courses() -> list[Course]:
    return [
        Course(PAVER, 60, PAVER_NOTE, "60", joints=200),
        Course(MORTAR, 30, MORTAR_NOTE, "30"),
    ]


def on_grade() -> list[Course]:
    return paver_courses() + [
        Course(ROADBASE, 150, DGB_NOTE, "150"),
        Course(ROADBASE, 150, DGB_NOTE, "150"),
        Course(SUBGRADE, 200, SUBGRADE_NOTE, open_bottom=True),
    ]


def on_structure() -> list[Course]:
    return paver_courses() + [
        Course(SCREED, 50, SCREED_NOTE, "VAR."),
        Course(CONCRETE, 200, EX_SLAB_NOTE),
    ]


PROFILES = [
    Profile("P1-A", "PAVER TYPE 1 - ON STRUCTURE", on_structure()),
    Profile("P1-B", "PAVER TYPE 1 - ON GRADE", on_grade()),
    Profile("P1-C", "PAVER TYPE 1 - OVER VOID",
            paver_courses() + [Course(CONCRETE, 200, NEW_SLAB_NOTE)],
            void_below=True),
    Profile("P2-A", "PAVER TYPE 2 - ON STRUCTURE", on_structure()),
    Profile("P2-B", "PAVER TYPE 2 - ON GRADE", on_grade()),
    Profile("L1", "LANDSCAPING",
            [Course(SOIL, 400, LANDSCAPE_NOTE, open_bottom=True)]),
]


# --------------------------------------------------------------------------
# Drawing helpers
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
    for m in (PAVER, MORTAR, SCREED, CONCRETE, ROADBASE, SUBGRADE, SOIL):
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
    h.set_pattern_fill(m.pattern, scale=m.pattern_scale,
                       angle=m.pattern_angle, color=256)
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
    text(msp, label, x + s * 1.5, y + s * 1.4 + 10, align=TextEntityAlignment.BOTTOM_LEFT)


def leader(msp, start, elbow_y, text_x, note):
    """Dot at the course, leader to an elbow, then horizontal to the note."""
    lay = {"layer": "PAVE-ANNO"}
    sx, sy = start
    msp.add_circle(start, 6, dxfattribs=lay)
    dot = msp.add_hatch(color=256, dxfattribs=lay)
    dot.paths.add_edge_path().add_arc(start, 6, 0, 360)
    knee_x = STRIP_W_OFFSET[0] + STRIP_W + 120
    msp.add_lwpolyline([(sx, sy), (knee_x, elbow_y), (text_x - 15, elbow_y)],
                       dxfattribs=lay)
    text(msp, note, text_x, elbow_y)


STRIP_W_OFFSET = [0.0]  # x origin of current strip (used by leader)


def draw_profile(msp, p: Profile, ox: float, oy: float) -> None:
    """Draw one profile with its finished surface at (ox, oy)."""
    STRIP_W_OFFSET[0] = ox
    x0, x1 = ox, ox + STRIP_W
    y = oy
    lay = {"layer": "PAVE-OUTLINE"}
    bands = []
    for c in p.courses:
        yb = y - c.thickness
        pts = [(x0, y), (x1, y), (x1, yb), (x0, yb)]
        hatch(msp, c.material, pts)
        msp.add_line((x0, y), (x1, y), dxfattribs=lay)
        if not c.open_bottom:
            msp.add_line((x0, yb), (x1, yb), dxfattribs=lay)
        if c.joints:
            x = x0 + c.joints / 2
            while x < x1:
                msp.add_line((x, y), (x, yb), dxfattribs=lay)
                x += c.joints
        bands.append((c, y, yb))
        y = yb

    bottom = y
    if p.void_below:
        vb = bottom - 250
        msp.add_lwpolyline([(x0, bottom), (x0, vb), (x1, vb), (x1, bottom)],
                           dxfattribs={"layer": "PAVE-VOID",
                                       "linetype": "DASHED"})
        text(msp, "VOID", (x0 + x1) / 2, (bottom + vb) / 2, h=TXT * 1.4,
             layer="PAVE-VOID", align=TextEntityAlignment.MIDDLE_CENTER)
        p_notes_bottom = vb
    else:
        p_notes_bottom = bottom

    break_line(msp, x0, oy, bottom)
    break_line(msp, x1, oy, bottom)

    # dimensions on the left
    for c, yt, yb in bands:
        if c.dim:
            vdim(msp, x0 - 120, yt, yb, c.dim)

    level_marker(msp, x0 + 150, oy, "FINISHED SURFACE LEVEL")

    # notes on the right, one per course, kept level with the course where
    # possible and pushed down only as needed so leaders never cross
    text_x = x1 + 300
    row = TXT * 2.4
    elbow_y = oy + row
    for i, (c, yt, yb) in enumerate(bands):
        elbow_y = min((yt + yb) / 2, elbow_y - row)
        sx = x0 + STRIP_W * (0.55 + 0.08 * i)
        leader(msp, (sx, (yt + yb) / 2), elbow_y, text_x, c.note)
    for note in p.extra_notes:
        elbow_y -= row
        text(msp, note, text_x, elbow_y)

    # title block: tag box + title + scale
    ty = min(oy - 850, p_notes_bottom - 200)  # titles align across a row
    bs, bw = TXT_TAG * 2.2, TXT_TAG * 3.4
    msp.add_lwpolyline([(x0, ty), (x0 + bw, ty), (x0 + bw, ty - bs),
                        (x0, ty - bs)], close=True,
                       dxfattribs={"layer": "PAVE-TITLE"})
    text(msp, p.tag, x0 + bw / 2, ty - bs / 2, h=TXT_TAG, layer="PAVE-TITLE",
         align=TextEntityAlignment.MIDDLE_CENTER)
    text(msp, p.title, x0 + bw + 60, ty - bs * 0.35, h=TXT_TITLE,
         layer="PAVE-TITLE")
    msp.add_line((x0 + bw + 60, ty - bs * 0.62), (x1 + 1500, ty - bs * 0.62),
                 dxfattribs={"layer": "PAVE-TITLE"})
    text(msp, f"SCALE 1:{SCALE}", x0 + bw + 60, ty - bs * 0.82,
         h=TXT * 1.2, layer="PAVE-TITLE")


def draw_schedule(msp, ox: float, oy: float) -> None:
    """Pavement schedule / legend matching the source key."""
    rows = {
        "P1-A": ["60mm Thickness Paver, refer landscape specification, on",
                 "30mm Thickness Mortar, on",
                 "Variable Screed, refer Drawing CV-TTW-1030, on",
                 "Existing concrete slab (levels TBC on site)."],
        "P1-B": ["60mm Thickness Paver, refer landscape specification, on",
                 "30mm Thickness Mortar, on",
                 "150mm Thickness DGB20 road base, on",
                 "150mm Thickness DGB20 road base, on",
                 "Compacted sub-grade to 98% MDD standard"],
        "P1-C": ["60mm Thickness Paver, refer landscape specification, on",
                 "30mm Thickness Mortar, on",
                 "new slab to structural engineers specification."],
    }
    rows["P2-A"] = rows["P1-A"]
    rows["P2-B"] = rows["P1-B"]
    rows["L1"] = ["Refer to landscape specification"]
    titles = {p.tag: p.title for p in PROFILES}

    text(msp, "PAVEMENT SCHEDULE", ox, oy, h=TXT_TITLE, layer="PAVE-TITLE",
         align=TextEntityAlignment.BOTTOM_LEFT)
    y = oy - 150
    bs, bw = TXT_TAG * 2.2, TXT_TAG * 3.4
    for tag, lines in rows.items():
        msp.add_lwpolyline([(ox, y), (ox + bw, y), (ox + bw, y - bs),
                            (ox, y - bs)], close=True,
                           dxfattribs={"layer": "PAVE-TITLE"})
        text(msp, tag, ox + bw / 2, y - bs / 2, h=TXT_TAG, layer="PAVE-TITLE",
             align=TextEntityAlignment.MIDDLE_CENTER)
        tx = ox + bw + 60
        text(msp, titles[tag], tx, y - TXT * 1.4, h=TXT * 1.6,
             layer="PAVE-TITLE")
        ly = y - TXT * 1.4 - TXT * 2.2
        for line in lines:
            text(msp, line, tx, ly, layer="PAVE-TITLE")
            ly -= TXT * 1.6
        y = min(y - bs, ly) - 120


def build() -> ezdxf.document.Drawing:
    doc = ezdxf.new("R2013", setup=True)
    setup(doc)
    msp = doc.modelspace()
    for i, p in enumerate(PROFILES):
        col, rowi = i % 3, i // 3
        draw_profile(msp, p, 400 + col * CELL_W, -400 - rowi * CELL_H)
    draw_schedule(msp, 400 + 3 * CELL_W, -300)
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
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--preview", action="store_true",
                    help="also write PNG/PDF previews")
    args = ap.parse_args()
    OUT_DIR.mkdir(exist_ok=True)
    doc = build()
    out = OUT_DIR / "Pavement_Profiles.dxf"
    doc.saveas(out)
    print(f"wrote {out}")
    if args.preview:
        preview(doc, OUT_DIR / "Pavement_Profiles_preview")
        print("wrote previews")


if __name__ == "__main__":
    main()
