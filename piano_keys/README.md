# piano_keys

Parametric piano-key layout generator that exports DXF for use in AutoCAD
(or any other CAD tool that reads DXF).

Real keyboards aren't built to one universal geometry - octave width, key
length, and black-key stagger vary by manufacturer and by keyboard type
(full-size, narrow/DS, custom). This tool takes those as parameters instead
of hard-coding one standard, so the output can be tuned to match whatever
reference dimensions you're working from.

## Install

```
pip install -r requirements.txt
```

## Usage

```
python -m piano_keys.cli \
  --octaves 2 \
  --octave-width 165.1 \
  --white-length 150 \
  --black-length-ratio 0.6 \
  --black-width-ratio 0.55 \
  --offset "C#=-0.08" \
  --offset "D#=0.08" \
  --output keyboard.dxf
```

- `--octaves`: number of full octaves (C through B) to generate.
- `--octave-width`: width of one octave (7 white keys) in mm.
- `--white-length` / `--black-length-ratio`: white key length in mm, and
  black key length as a fraction of it.
- `--black-width-ratio`: black key width as a fraction of white key width.
- `--offset NOTE=VALUE`: shift a black key (`C#`, `D#`, `F#`, `G#`, `A#`)
  left/right from its default position (centered on the boundary between
  the two white keys it separates), in units of white-key widths. Repeat
  for multiple keys. Use this to reproduce a specific manufacturer's
  stagger.

Output is a DXF (`R2010` format) with two layers: `WHITE_KEYS` and
`BLACK_KEYS` (black keys are also filled with a solid hatch on that layer).

## As a library

```python
from piano_keys import PianoKeyStandard, build_keyboard

standard = PianoKeyStandard(octave_width_mm=165.1, num_octaves=1)
standard.black_key_offsets["C#"] = -0.08
doc = build_keyboard(standard)
doc.saveas("keyboard.dxf")
```

## Limitations

- Generates whole octaves starting on C; partial/offset keyboards (e.g.
  starting on A0 like a real 88-key piano) aren't supported yet.
