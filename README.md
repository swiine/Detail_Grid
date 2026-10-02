# Detail_Grid

## Pavement profiles

`cad/Pavement_Profiles.dxf` holds the pavement section details P1-A, P1-B, P1-C,
P2-A, P2-B and L1, plus the pavement schedule. Open it in AutoCAD, BricsCAD or
any DXF-capable CAD package.

- Model space is drawn 1:1 in millimetres. Annotation is sized for plotting at 1:10.
- Layers are prefixed `PAVE-` (outline, one per material hatch, annotation, dims, titles).
- `cad/Pavement_Profiles_preview.png` / `.pdf` are quick-look previews.

Regenerate after editing `scripts/pavement_profiles.py`:

```
pip install ezdxf matplotlib
python scripts/pavement_profiles.py --preview
```

Assumed drawn thicknesses (not specified in the schedule): variable screed 50 mm,
slabs 200 mm (existing slab TBC on site; new slab per structural engineer).
