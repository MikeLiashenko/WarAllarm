# WarAlarm medallions for 3D printing

The three channel icons as relief medallions, Ø80 mm, 3.8 mm thick: **WarAlarm** (tryzub), **Київ**
(Archangel Michael), **Харків** (cornucopia and caduceus). Made in Blender from the same vector
sources as the icons (`design/channels/src`, tryzub from `app/lib/widgets/brand_icon.dart`).

| Part (`stl/<icon>/<icon>_<part>.stl`) | Height, mm | Filament |
|---|---|---|
| `blue` (top half) | 0 – 2.4 | blue |
| `yellow` (bottom half) | 0 – 2.4 | yellow |
| `field` (shield) | 2.4 – 3.0 | black |
| `rim` (shield rim) | 2.4 – 3.6 | white |
| `gold` (emblem) | 3.0 – 3.8 | gold / yellow |
| `silver` (emblem, Kyiv and Kharkiv) | 3.0 – 3.8 | white / silver |

The black line work of the emblems is the gaps between the gold and silver parts, where the black
field shows through.

## Printing

- **Multi-colour (Bambu AMS):** select all the parts of one icon (`<icon>_blue.stl` … `<icon>_silver.stl`)
  and drag them into Bambu Studio together. Answer **Yes** to "load as a single object with
  multiple parts", then give each part its filament. The parts share the same origin, so they line up by themselves.
- **One colour:** `<icon>_all.stl`. The relief alone carries the picture: shield +0.6, rim +1.2, emblem +1.4 mm.
- Print flat, face up, 0.4 mm nozzle, 0.12–0.16 mm layers for the relief. The thinnest lines in
  the Kyiv and Kharkiv emblems are about 0.45 mm at Ø80. For finer detail, scale to 100–120 % in the slicer.

## Rebuild

```bash
cd design/print3d/src
python masks.py                     # Chrome renders flat-colour masks, numpy traces them -> build/*.json
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P blender_build.py   # STL + renders + .blend
```

Photos: `renders/`. Blender scene: `waralarm_medallions.blend`.
