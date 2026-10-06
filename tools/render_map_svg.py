"""Renders docs/map-preview.svg from src/DerZombies.Core/MapData.cs (no dependencies)."""
import re, pathlib

root = pathlib.Path(__file__).resolve().parent.parent
src = (root / "src/DerZombies.Core/MapData.cs").read_text()
layout = re.findall(r'^\s*"([#A-Za-z0-9]{20,})",\s*$', src, re.M)
names = dict(re.findall(r"\['(\w)'\] = \"([^\"]+)\"", src))
costs = dict(re.findall(r"\['(\w)'\] = (\d+),", src))
features = re.findall(r"new FeatureDef\(FeatureKind\.(\w+), (\d+), (\d+)(?:, (?:nameof\(\w+\.(\w+)\)|\"([^\"]*)\"))?\)", src)

C = 12
rows, cols = len(layout), len(layout[0])
W, H = cols * C, rows * C
out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H + 30}" font-family="monospace">',
       f'<rect width="{W}" height="{H + 30}" fill="#111"/>']
area_cells = {}
for r, line in enumerate(layout):
    for c, ch in enumerate(line):
        x, y = c * C, r * C
        if ch == "#":
            out.append(f'<rect x="{x}" y="{y}" width="{C}" height="{C}" fill="#3a3a3a"/>')
        elif ch.isupper():
            out.append(f'<rect x="{x}" y="{y}" width="{C}" height="{C}" fill="#20262c"/>')
            area_cells.setdefault(ch, []).append((c, r))
        else:
            out.append(f'<rect x="{x}" y="{y}" width="{C}" height="{C}" fill="#8a5a2b"/>')
for ch, cells in area_cells.items():
    cx = (min(c for c, _ in cells) + max(c for c, _ in cells) + 1) / 2 * C
    cy = (min(r for _, r in cells) + max(r for _, r in cells) + 1) / 2 * C
    out.append(f'<text x="{cx}" y="{cy}" fill="#9aa" font-size="11" text-anchor="middle">{names.get(ch, ch).upper()}</text>')
for ch, cost in costs.items():
    cells = [(c, r) for r, line in enumerate(layout) for c, x in enumerate(line) if x == ch]
    if cells:
        cx = sum(c for c, _ in cells) / len(cells) * C + C / 2
        cy = sum(r for _, r in cells) / len(cells) * C + C / 2 + 3
        out.append(f'<text x="{cx}" y="{cy}" fill="#ffd27a" font-size="7" text-anchor="middle">{cost}</text>')
style = {"PlayerStart": ("#ffff00", "START"), "Perk": ("#ff4d4d", None), "WallBuy": ("#e0c060", None),
         "BoxLocation": ("#4d79ff", "box"), "PowerSwitch": ("#ff0000", "POWER"), "LandingPad": ("#00d0ff", "pad"),
         "PackAPunch": ("#cc44ff", "PaP"), "Gondola": ("#00ffff", "gondola"), "Dragon": ("#ff9900", "dragon"),
         "BowPedestal": ("#ff9900", "bow"), "BowAltar": ("#cc44ff", None)}
for kind, c, r, k1, k2 in features:
    color, label = style.get(kind, ("#fff", kind))
    label = label or (k1 or k2) + (" altar" if kind == "BowAltar" else "")
    x, y = int(c) * C + C / 2, int(r) * C + C / 2
    out.append(f'<circle cx="{x}" cy="{y}" r="3.5" fill="{color}"/>')
    out.append(f'<text x="{x}" y="{y - 5}" fill="{color}" font-size="6.5" text-anchor="middle">{label}</text>')
out.append(f'<text x="6" y="{H + 20}" fill="#ccc" font-size="11">DER EISENDRACHE - Civil 3D Zombies map preview (brown = buyable doors, cost shown)</text>')
out.append("</svg>")
(root / "docs/map-preview.svg").write_text("\n".join(out))
print("wrote docs/map-preview.svg")
