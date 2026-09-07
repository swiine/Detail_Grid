"""Command-line entry point for generating a piano-key DXF layout.

Example:
    python -m piano_keys.cli --octaves 2 --octave-width 165.1 \\
        --white-length 150 --offset C#=-0.08 --offset D#=0.08 \\
        --output keyboard.dxf
"""

from __future__ import annotations

import argparse

from .generator import BLACK_NOTES, PianoKeyStandard, build_keyboard


def _parse_offset(raw: str) -> tuple[str, float]:
    try:
        note, value = raw.split("=", 1)
        return note.strip(), float(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            f"expected NOTE=VALUE (e.g. 'C#=-0.1'), got {raw!r}"
        ) from exc


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--octaves", type=int, default=1, help="number of octaves")
    parser.add_argument(
        "--octave-width", type=float, default=165.1, help="octave width in mm"
    )
    parser.add_argument(
        "--white-length", type=float, default=150.0, help="white key length in mm"
    )
    parser.add_argument(
        "--black-length-ratio",
        type=float,
        default=0.6,
        help="black key length as a fraction of white key length",
    )
    parser.add_argument(
        "--black-width-ratio",
        type=float,
        default=0.55,
        help="black key width as a fraction of white key width",
    )
    parser.add_argument(
        "--offset",
        action="append",
        default=[],
        type=_parse_offset,
        metavar="NOTE=VALUE",
        help=(
            "shift a black key from its default boundary position, in "
            f"white-key widths. NOTE is one of {BLACK_NOTES}. Repeatable."
        ),
    )
    parser.add_argument(
        "--output", default="piano_keys.dxf", help="output DXF file path"
    )
    args = parser.parse_args(argv)

    standard = PianoKeyStandard(
        octave_width_mm=args.octave_width,
        white_key_length_mm=args.white_length,
        black_key_length_ratio=args.black_length_ratio,
        black_key_width_ratio=args.black_width_ratio,
        num_octaves=args.octaves,
    )
    for note, value in args.offset:
        if note not in standard.black_key_offsets:
            parser.error(f"unknown black key {note!r}, expected one of {BLACK_NOTES}")
        standard.black_key_offsets[note] = value

    doc = build_keyboard(standard)
    doc.saveas(args.output)
    print(f"wrote {args.output}")


if __name__ == "__main__":
    main()
