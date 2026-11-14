"""Step 1 of the 3D-printable WarAlarm medallions: flat-colour masks -> traced outlines.

    python masks.py            (Chrome renders the SVGs; numpy traces the colour regions)

For each icon (WarAlarm tryzub, Kyiv, Kharkiv) the channel-icon SVG (design/channels/src) is rendered
with one flat colour per print part: white rim of the shield, black field (and the black line work of
the emblem), gold, silver/white. Each colour becomes closed outlines (outer loops CCW, holes CW) in
millimetres on a medallion of DIAM mm, written to build/<icon>.json for blender_build.py.
"""
import json, os, re, subprocess, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
CH = os.path.join(ROOT, 'design', 'channels', 'src')
sys.path.insert(0, CH)
import tpl, kyiv, kharkiv  # noqa: E402

OUT = os.path.join(HERE, 'build'); os.makedirs(OUT, exist_ok=True)
PX = 3000                       # render size: 1000 SVG units -> 3000 px
DIAM = 80.0                     # medallion diameter, mm (the SVG's 1000 units)
CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
PALETTE = {                     # flat render colours -> part
    'rim': (255, 0, 0), 'field': (0, 0, 0), 'gold': (255, 212, 0), 'silver': (255, 255, 255), 'none': (0, 0, 255),
}


def tryzub_emblem():
    """The app icon's tryzub (brand_icon.dart: loops in units of its height, x from the centre line)."""
    src = open(os.path.join(ROOT, 'app', 'lib', 'widgets', 'brand_icon.dart'), encoding='utf-8').read()
    body = src[src.index('_tryzub = <List<double>>['):]
    body = body[:body.index('];')]
    loops = [[float(v) for v in re.findall(r'-?\d+\.\d+', m)] for m in re.findall(r'\[([^\[\]]+)\]', body)]
    h, x0, y0 = 500.0, 500.0, 255.0
    d = ''.join('M' + ' L'.join(f'{x0 + l[i] * h:.2f} {y0 + l[i + 1] * h:.2f}' for i in range(0, len(l), 2)) + ' Z' for l in loops)
    return f'<path d="{d}" fill="url(#gold)" fill-rule="evenodd"/>'


def flat_svg(emblem):
    """Medallion in flat part colours: no flag (built in Blender), no gradients, no shadow."""
    e = emblem.replace('url(#gold)', '#FFD400').replace('url(#silver)', '#FFFFFF')
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1000" width="{PX}" height="{PX}" shape-rendering="crispEdges">
  <defs><clipPath id="shieldClip"><path d="{tpl.SHIELD}"/></clipPath></defs>
  <rect width="1000" height="1000" fill="#0000FF"/>
  <path d="{tpl.SHIELD}" fill="#000"/>
  <g clip-path="url(#shieldClip)">{e}</g>
  <path d="{tpl.SHIELD}" fill="none" stroke="#FF0000" stroke-width="24" stroke-linejoin="round"/>
</svg>'''


def render(svg, png):
    html = os.path.join(OUT, '_r.html')
    open(html, 'w', encoding='utf-8').write(f'<html><body style="margin:0;background:#00f">{svg}</body></html>')
    subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                    f'--window-size={PX},{PX}', f'--screenshot={png}', 'file:///' + html.replace('\\', '/')],
                   check=True, capture_output=True)


def classify(png):
    a = np.asarray(Image.open(png).convert('RGB'), dtype=np.int32)[:PX, :PX]
    names = list(PALETTE); cols = np.array([PALETTE[n] for n in names])
    d = ((a[:, :, None, :] - cols[None, None, :, :]) ** 2).sum(-1)
    return names, d.argmin(-1)


def trace(mask):
    """Closed outlines of a boolean mask along pixel edges (corner coordinates x = column, y = row).
    Every edge keeps the filled pixel on its right; at a saddle corner (two pixels touching diagonally)
    the walk always turns right, so the loops never cross. Blender fills them even-odd."""
    P = np.pad(mask, 1); f = P[1:-1, 1:-1]
    out = {}
    def add(sel, a, b):
        for r, c in zip(*np.nonzero(sel)):
            out.setdefault((c + a[0], r + a[1]), []).append((c + b[0], r + b[1]))
    add(f & ~P[:-2, 1:-1], (0, 0), (1, 0))      # top
    add(f & ~P[2:, 1:-1], (1, 1), (0, 1))       # bottom
    add(f & ~P[1:-1, :-2], (0, 1), (0, 0))      # left
    add(f & ~P[1:-1, 2:], (1, 0), (1, 1))       # right
    saddle = {k for k, v in out.items() if len(v) > 1}
    loops = []
    while out:
        start = next((k for k in out if k not in saddle), next(iter(out))); p = start; prev = None; loop = []; nud = []
        while True:
            loop.append(p); nud.append(p in saddle)
            vs = out[p]
            if len(vs) == 1 or prev is None: q = vs.pop()
            else:
                dx, dy = p[0] - prev[0], p[1] - prev[1]; want = (p[0] - dy, p[1] + dx)   # right turn (y down)
                q = want if want in vs else vs[0]; vs.remove(q)
            if not vs: del out[p]
            prev, p = p, q
            if p == start and p not in out: break
            if p == start and len(loop) > 2: break
        if len(loop) >= 4:
            L = np.array(loop, dtype=np.float64)
            # a saddle corner is shared by two loops (or twice by one): pull each pass a quarter pixel into its own
            # pixel, so no two outlines touch (Blender's fill breaks on touching outlines)
            for k in np.nonzero(nud)[0]:
                d1 = L[k] - L[k - 1]; d2 = L[(k + 1) % len(L)] - L[k]
                L[k] = L[k] + 0.25 * (-d1 + d2)
            loops.append(L)
    return loops


def rdp(pts, tol):
    """Douglas-Peucker, iterative (loops have tens of thousands of pixel corners)."""
    n = len(pts)
    if n < 3: return pts
    keep = np.zeros(n, bool); keep[0] = keep[-1] = True; st = [(0, n - 1)]
    while st:
        i0, i1 = st.pop()
        if i1 - i0 < 2: continue
        a, ab = pts[i0], pts[i1] - pts[i0]; L = np.hypot(*ab); seg = pts[i0 + 1:i1]
        d = np.abs(ab[0] * (seg[:, 1] - a[1]) - ab[1] * (seg[:, 0] - a[0])) / L if L > 0 else np.hypot(*(seg - a).T)
        k = int(d.argmax())
        if d[k] > tol: m = i0 + 1 + k; keep[m] = True; st += [(i0, m), (m, i1)]
    return pts[keep]


def simplify(loop, tol=0.6):
    # split a closed loop at its two farthest points, simplify both halves
    i = 0; j = int(np.hypot(*(loop - loop[0]).T).argmax())
    a = rdp(loop[i:j + 1], tol); b = rdp(np.vstack([loop[j:], loop[:1]]), tol)
    return np.vstack([a[:-1], b[:-1]])


def area(p): return 0.5 * float(np.sum(p[:, 0] * np.roll(p[:, 1], -1) - np.roll(p[:, 0], -1) * p[:, 1]))


def main():
    icons = {'waralarm': tryzub_emblem(), 'kyiv': kyiv.emblem(), 'kharkiv': kharkiv.emblem()}
    k = DIAM / PX
    for name, em in icons.items():
        png = os.path.join(OUT, f'{name}_flat.png')
        render(flat_svg(em), png)
        names, lab = classify(png)
        parts = {}
        for i, part in enumerate(names):
            if part == 'none': continue
            loops = []
            for lp in trace(lab == i):
                s = simplify(lp)
                if len(s) < 3: continue
                mm = np.column_stack([(s[:, 0] - PX / 2) * k, (PX / 2 - s[:, 1]) * k])   # y up, centre 0
                if abs(area(mm)) < 0.02: continue                                         # specks < 0.02 mm²
                loops.append(np.round(mm, 3).tolist())
            parts[part] = loops
            print(name, part, len(loops), 'loops', sum(len(l) for l in loops), 'points')
        json.dump({'diam': DIAM, 'parts': parts}, open(os.path.join(OUT, f'{name}.json'), 'w'))


if __name__ == '__main__':
    main()
