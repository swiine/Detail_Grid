# Detail_Grid

## Pavement profile tool

`scripts/profile_tool.py` draws pavement build-ups as hatched CAD sections (DXF).
Type the thickness and material for each course, top down, and it draws the
outline, hatch, joints, dimensions, notes with leaders, and the title tag.

```
pip install ezdxf matplotlib

# one profile from the command line -> cad/P3-A.dxf
python scripts/profile_tool.py -t P3-A -n "PAVER TYPE 3 - ON GRADE" \
    "80 paver" "30 mortar" "200 concrete" "150 dgb20" "subgrade"

# a whole schedule from a text file, with a schedule table
python scripts/profile_tool.py -f profiles/pavement_schedule.txt --preview

# no arguments: asks for the tag, title and courses one at a time
python scripts/profile_tool.py

python scripts/profile_tool.py --list     # available materials
```

Course syntax: `<thickness> <material>[: custom note]`

| Example | Result |
|---|---|
| `200 concrete` | 200 mm concrete hatch, dimensioned 200, note "200mm THICK CONCRETE" |
| `200 concrete: New slab to engineer's spec` | same, with your note |
| `var screed` | drawn at the material's default thickness, dimensioned VAR. |
| `subgrade` | no thickness: default depth, no dimension |
| `void` (last line) | dashed void under the build-up |

**Setting the look:** edit the `MATERIALS` table near the top of the script.
Each material has its hatch pattern, scale, angle, layer, colour, default
note, default thickness and the alternative words you can type for it.
`SCALE`, the text heights and the strip width are just above it.

Output is drawn 1:1 in mm with annotation sized for 1:10, on `PAVE-` layers.

## Current drawing

`cad/Pavement_Profiles.dxf` (P1-A to P2-B, L1 plus the schedule) is generated
from `profiles/pavement_schedule.txt`:

```
python scripts/profile_tool.py -f profiles/pavement_schedule.txt -o cad/Pavement_Profiles.dxf --preview
```
