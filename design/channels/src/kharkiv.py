import math, tpl

def f(v): return f'{v:.1f}'

def bez(p0, p1, p2, p3, n=60):
    out = []
    for i in range(n + 1):
        t = i / n; u = 1 - t
        out.append((u*u*u*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
                    u*u*u*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1]))
    return out

def poly(pts, close=True):
    return 'M' + ' L'.join(f'{f(x)} {f(y)}' for x, y in pts) + (' Z' if close else '')

# ── Cornucopia (local coords: mouth up at y≈-170, tip curls at the bottom) ──
def cornucopia():
    # centreline: mouth centre → down → curls right/up at the tip
    c = bez((0, -165), (-10, -20), (10, 130), (85, 205), 80)
    # tip curl: small spiral continuing the centreline
    last = c[-1]; ang = math.atan2(c[-1][1] - c[-2][1], c[-1][0] - c[-2][0])
    curl = []
    r0 = 34
    cx = last[0] + r0 * math.cos(ang - math.pi / 2); cy = last[1] + r0 * math.sin(ang - math.pi / 2)
    a0 = ang + math.pi / 2
    for i in range(1, 40):
        a = a0 - i * 0.11; r = r0 * (1 - i / 60)
        curl.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    c = c + curl
    n = len(c)
    left, right, norms = [], [], []
    L = 0; lens = [0]
    for i in range(1, n): L += math.dist(c[i], c[i-1]); lens.append(L)
    for i in range(n):
        a, b = c[max(i-1, 0)], c[min(i+1, n-1)]
        dx, dy = b[0] - a[0], b[1] - a[1]; d = math.hypot(dx, dy) or 1
        nx, ny = -dy / d, dx / d
        t = lens[i] / L
        w = 98 * (1 - t) ** 1.25 + 5
        left.append((c[i][0] + nx * w, c[i][1] + ny * w)); right.append((c[i][0] - nx * w, c[i][1] - ny * w))
        norms.append((nx, ny, w))
    body = poly(left + right[::-1])
    # bands across the horn (black cuts)
    bands = []
    for t in (0.10, 0.17, 0.36, 0.52, 0.66):
        i = min(range(n), key=lambda k: abs(lens[k] / L - t))
        nx, ny, w = norms[i]
        p, q = (c[i][0] + nx * (w + 4), c[i][1] + ny * (w + 4)), (c[i][0] - nx * (w + 4), c[i][1] - ny * (w + 4))
        bands.append(f'<path d="M{f(p[0])} {f(p[1])} L{f(q[0])} {f(q[1])}" stroke="#000" stroke-width="{10 if t > 0.2 else 12}" stroke-linecap="butt"/>')
    # mouth rim (black ellipse outline cut, inner dark)
    mouth = ('<ellipse cx="0" cy="-165" rx="101" ry="30" fill="#000"/>'
             '<ellipse cx="0" cy="-165" rx="92" ry="24" fill="#fff"/>'
             '<ellipse cx="0" cy="-163" rx="76" ry="16" fill="#000"/>')
    # fruit spilling out: grapes, apple, pear, leaves — white with black outline gaps
    def blob(d, sw=9): return f'<path d="{d}" fill="#fff" stroke="#000" stroke-width="{sw}"/>'
    def circ(x, y, r, sw=8): return f'<circle cx="{x}" cy="{y}" r="{r}" fill="#fff" stroke="#000" stroke-width="{sw}"/>'
    leaf_l = blob('M-30 -192 C-100 -196 -146 -240 -156 -300 C-98 -300 -46 -264 -30 -192 Z')
    leaf_r = blob('M40 -196 C104 -200 146 -240 160 -292 C104 -294 56 -262 40 -196 Z')
    leaf_vein = ('<path d="M-38 -198 C-80 -220 -116 -256 -144 -290" fill="none" stroke="#000" stroke-width="6"/>'
                 '<path d="M48 -202 C88 -222 124 -252 150 -284" fill="none" stroke="#000" stroke-width="6"/>')
    pear = blob('M10 -190 C-25 -190 -38 -230 -22 -255 C-12 -270 -10 -290 5 -300 C20 -290 22 -270 32 -255 C48 -230 45 -190 10 -190 Z')
    stalk = '<path d="M5 -298 C2 -312 6 -322 14 -330" fill="none" stroke="#fff" stroke-width="9" stroke-linecap="round"/>'
    apple = circ(-62, -205, 36) + '<path d="M-62 -238 C-60 -248 -56 -254 -48 -258" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round"/>'
    grapes = ''.join(circ(x, y, 16, 7) for x, y in [(55, -200), (80, -205), (67, -225), (95, -224), (43, -222), (82, -246), (58, -248), (70, -270)])
    fruit = leaf_l + leaf_r + leaf_vein + grapes + pear + stalk + apple
    return (f'<path d="{body}" fill="#fff" stroke="#000" stroke-width="12" paint-order="stroke"/>'
            + ''.join(bands) + mouth + fruit)

# ── Caduceus (local coords: vertical staff, wings on top) ──
def caduceus():
    staff = ('<rect x="-13" y="-235" width="26" height="520" rx="13" fill="#fff" stroke="#000" stroke-width="12" paint-order="stroke"/>'
             '<circle cx="0" cy="-252" r="27" fill="#fff" stroke="#000" stroke-width="10"/>')
    def wing(sx):
        root_top, root_bot = (12, -224), (12, -178)
        pts = bez(root_top, (55, -262), (110, -292), (172, -300), 30)
        # feather tips (outer) and notches (inner), alternating, top → bottom
        path = [(172, -300), (122, -262), (170, -256), (114, -228), (152, -212), (98, -200), (118, -176), root_bot]
        for p, q in zip(path, path[1:]):
            m = ((p[0] + q[0]) / 2 + 10, (p[1] + q[1]) / 2 + 9)
            for i in range(1, 11):
                t = i / 10; u = 1 - t
                pts.append((u*u*p[0] + 2*u*t*m[0] + t*t*q[0], u*u*p[1] + 2*u*t*m[1] + t*t*q[1]))
        pts = [(sx * x, y) for x, y in pts]
        lines = ''.join(f'<path d="M{f(sx*x0)} {f(y0)} L{f(sx*x1)} {f(y1)}" stroke="#000" stroke-width="8" stroke-linecap="round"/>'
                        for (x0, y0), (x1, y1) in [((122, -262), (70, -240)), ((114, -228), (66, -214)), ((98, -200), (60, -194))])
        return f'<path d="{poly(pts)}" fill="#fff" stroke="#000" stroke-width="10" paint-order="stroke"/>' + lines
    def snake(sx):
        # tail at the bottom, three crossings, head at the top facing the staff
        pts = []
        segs = [((0, 215), (sx*62, 200), (sx*72, 150), (0, 120)),
                ((0, 120), (sx*-80, 88), (sx*-80, 32), (0, 5)),
                ((0, 5), (sx*82, -24), (sx*82, -82), (0, -110)),
                ((0, -110), (sx*-70, -135), (sx*-78, -175), (sx*-40, -186))]
        for s in segs: pts += bez(*s, 30)[(1 if pts else 0):]
        d = poly(pts, close=False)
        hx, hy = sx * -30, -186
        head = (f'<ellipse cx="{hx}" cy="{hy}" rx="21" ry="14" transform="rotate({sx*-12} {hx} {hy})" fill="#fff" stroke="#000" stroke-width="9"/>'
                f'<circle cx="{hx + sx*6}" cy="{hy - 3}" r="3.6" fill="#000"/>')
        return (f'<path d="{d}" fill="none" stroke="#000" stroke-width="40" stroke-linecap="round"/>'
                f'<path d="{d}" fill="none" stroke="#fff" stroke-width="22" stroke-linecap="round"/>' + head)
    return staff + wing(1) + wing(-1) + snake(1) + snake(-1)

def emblem(rot=34, scale=0.9, cx=500, cy=532, horn_first=True):
    horn = f'<g transform="translate({cx} {cy}) rotate({-rot}) scale({scale})">{cornucopia()}</g>'
    cad = f'<g transform="translate({cx} {cy}) rotate({rot}) scale({scale})">{caduceus()}</g>'
    inner = (horn + cad) if horn_first else (cad + horn)
    return (f'<mask id="em" maskUnits="userSpaceOnUse" x="0" y="0" width="1000" height="1000">'
            f'<rect width="1000" height="1000" fill="#000"/>{inner}</mask>'
            f'<rect width="1000" height="1000" fill="url(#gold)" mask="url(#em)"/>')
