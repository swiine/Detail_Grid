"""Parametric piano-key layout generator, exported as DXF for CAD tools.

Real pianos are not built to one universal geometry - manufacturers vary
octave width, black-key stagger, and key length. Rather than hard-code one
"the" standard, this generator takes those dimensions as parameters so the
output can be tuned to match whatever reference (manufacturer spec, PTG
standard, a narrow/DS keyboard, etc.) the user needs.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import ezdxf

WHITE_NOTES = ["C", "D", "E", "F", "G", "A", "B"]
BLACK_NOTES = ["C#", "D#", "F#", "G#", "A#"]

# Default black-key center position for each black note, expressed as a
# multiple of the white-key width measured from the left edge of the
# octave. By default each black key sits exactly on the boundary between
# the two white keys it separates; `black_key_offsets` can shift a key
# left/right (in white-key widths) from that boundary to reproduce a
# specific manufacturer's stagger.
BLACK_KEY_BOUNDARY = {
    "C#": 1.0,
    "D#": 2.0,
    "F#": 4.0,
    "G#": 5.0,
    "A#": 6.0,
}


@dataclass
class PianoKeyStandard:
    octave_width_mm: float = 165.1
    white_key_length_mm: float = 150.0
    black_key_length_ratio: float = 0.6
    black_key_width_ratio: float = 0.55
    num_octaves: int = 1
    black_key_offsets: dict[str, float] = field(
        default_factory=lambda: {note: 0.0 for note in BLACK_NOTES}
    )

    @property
    def white_key_width_mm(self) -> float:
        return self.octave_width_mm / len(WHITE_NOTES)

    @property
    def black_key_width_mm(self) -> float:
        return self.white_key_width_mm * self.black_key_width_ratio

    @property
    def black_key_length_mm(self) -> float:
        return self.white_key_length_mm * self.black_key_length_ratio


def build_keyboard(standard: PianoKeyStandard) -> ezdxf.document.Drawing:
    doc = ezdxf.new("R2010")
    msp = doc.modelspace()

    doc.layers.add("WHITE_KEYS", color=7)
    doc.layers.add("BLACK_KEYS", color=250)

    ww = standard.white_key_width_mm
    wl = standard.white_key_length_mm
    bw = standard.black_key_width_mm
    bl = standard.black_key_length_mm
    black_y0 = wl - bl

    for octave in range(standard.num_octaves):
        base_x = octave * standard.octave_width_mm

        for i, note in enumerate(WHITE_NOTES):
            x0 = base_x + i * ww
            _rect(msp, x0, 0, x0 + ww, wl, layer="WHITE_KEYS")

        for note in BLACK_NOTES:
            offset = standard.black_key_offsets.get(note, 0.0)
            center_x = base_x + (BLACK_KEY_BOUNDARY[note] + offset) * ww
            x0 = center_x - bw / 2
            hatch = msp.add_hatch(color=250, dxfattribs={"layer": "BLACK_KEYS"})
            path = hatch.paths.add_polyline_path(
                [(x0, black_y0), (x0 + bw, black_y0), (x0 + bw, wl), (x0, wl)],
                is_closed=True,
            )
            _rect(msp, x0, black_y0, x0 + bw, wl, layer="BLACK_KEYS")

    return doc


def _rect(msp, x0, y0, x1, y1, layer):
    msp.add_lwpolyline(
        [(x0, y0), (x1, y0), (x1, y1), (x0, y1)],
        close=True,
        dxfattribs={"layer": layer},
    )
