"""Герб Харкова: золотий ріг достатку (хвіст-спіраль ліворуч унизу, розтруб із
плодами праворуч угорі) і кадуцей поверх нього (кулька й срібні крила ліворуч
угорі, древко донизу праворуч, срібні змії).

Кожен елемент малюється двічі: у масці (білий силует + чорний проміжок між
деталями) і в кольорі (золото / срібло); маска вирізає проміжки з кольорового шару.
"""
import math

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

GOLD, SILVER = 'url(#gold)', 'url(#silver)'

class Pen:
    """mode 'mask': white shapes with black gaps; mode 'color': the same shapes in colour."""
    def __init__(self, mode): self.mode, self.out = mode, []
    def shape(self, tag, color=GOLD, gap=10):
        if self.mode == 'mask':
            self.out.append(f'{tag} fill="#fff" stroke="#000" stroke-width="{gap}" paint-order="stroke"/>')
        else:
            self.out.append(f'{tag} fill="{color}"/>')
    def tube(self, d, w, color=GOLD, gap=8, cap='round'):
        if self.mode == 'mask':
            self.out.append(f'<path d="{d}" fill="none" stroke="#000" stroke-width="{w + 2*gap}" stroke-linecap="{cap}"/>')
            self.out.append(f'<path d="{d}" fill="none" stroke="#fff" stroke-width="{w}" stroke-linecap="{cap}"/>')
        else:
            self.out.append(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{w}" stroke-linecap="{cap}"/>')
    def cut(self, tag):
        """black detail line / hole: only exists in the mask."""
        if self.mode == 'mask': self.out.append(tag)
    def group(self, transform, fn):
        self.out.append(f'<g transform="{transform}">'); fn(self); self.out.append('</g>')
    def svg(self): return ''.join(self.out)

# ── Ріг достатку (абсолютні координати іконки 0..1000) ──
def horn_geometry():
    mouth = (650, 398)
    c = bez(mouth, (602, 498), (505, 598), (372, 626), 90)
    nm = len(c)
    # tip: turns down, then right, then up — a small spiral under the end of the horn
    x, y = c[-1]; th = math.atan2(c[-1][1] - c[-2][1], c[-1][0] - c[-2][0])
    step, turn = 2.5, 5.3
    total, k0, k1 = 0.0, 1 / 46, 1 / 17
    while total < turn:
        k = k0 + (k1 - k0) * total / turn
        th -= k * step; total += k * step
        x += step * math.cos(th); y += step * math.sin(th)
        c.append((x, y))
    n = len(c)
    lens = [0.0]
    for i in range(1, n): lens.append(lens[-1] + math.dist(c[i], c[i-1]))
    Lm, L = lens[nm - 1], lens[-1]
    left, right, frame = [], [], []
    for i in range(n):
        a, b = c[max(i-1, 0)], c[min(i+1, n-1)]
        dx, dy = b[0] - a[0], b[1] - a[1]; d = math.hypot(dx, dy) or 1
        nx, ny = -dy / d, dx / d
        if i < nm:
            t = lens[i] / Lm; w = 21 + 77 * (1 - t) ** 1.25
        else:
            t = (lens[i] - Lm) / (L - Lm); w = 21 - 16 * t
        left.append((c[i][0] + nx * w, c[i][1] + ny * w)); right.append((c[i][0] - nx * w, c[i][1] - ny * w))
        frame.append((lens[i] / Lm, c[i], (nx, ny), w))
    return mouth, c, left, right, frame

def horn(p):
    mouth, c, left, right, frame = horn_geometry()
    p.shape(f'<path d="{poly(left + right[::-1])}"', GOLD, 12)
    # twisted ribs: slanted cuts across the horn
    def at(t): return min(frame, key=lambda e: abs(e[0] - t))
    for t in (0.08, 0.19, 0.30, 0.41, 0.52, 0.63, 0.74, 0.85):
        t0, c0, n0, w0 = at(t); t1, c1, n1, w1 = at(t + 0.05)
        a = (c0[0] + n0[0] * (w0 + 6), c0[1] + n0[1] * (w0 + 6))
        b = (c1[0] - n1[0] * (w1 + 6), c1[1] - n1[1] * (w1 + 6))
        m = ((a[0] + b[0]) / 2 + (c1[0] - c0[0]) * 0.6, (a[1] + b[1]) / 2 + (c1[1] - c0[1]) * 0.6)
        p.cut(f'<path d="M{f(a[0])} {f(a[1])} Q{f(m[0])} {f(m[1])} {f(b[0])} {f(b[1])}" fill="none" stroke="#000" stroke-width="{9 if t < 0.45 else 7}"/>')
    # mouth rim: an ellipse across the open end
    ax, ay = c[0][0] - c[6][0], c[0][1] - c[6][1]
    ang = math.degrees(math.atan2(ay, ax)) + 90
    mx, my = mouth[0] + ax * 0.4, mouth[1] + ay * 0.4
    p.shape(f'<ellipse cx="{f(mx)}" cy="{f(my)}" rx="104" ry="34" transform="rotate({f(ang)} {f(mx)} {f(my)})"', GOLD, 10)
    p.cut(f'<ellipse cx="{f(mx)}" cy="{f(my)}" rx="84" ry="20" transform="rotate({f(ang)} {f(mx)} {f(my)})" fill="#000"/>')
    return (mx, my), (ax, ay)

def fruit(p, mouth, axis):
    mx, my = mouth; d = math.hypot(*axis); ux, uy = axis[0] / d, axis[1] / d   # out of the mouth (up-right)
    vx, vy = -uy, ux                                                          # across the mouth
    P = lambda a, b: (mx + ux * a + vx * b, my + uy * a + vy * b)
    # leaves behind the heap, fanned out of the mouth
    for a, b, la, lb, wd in ((40, -78, 108, -150, 0.30), (80, -44, 168, -92, 0.28), (96, 6, 182, 4, 0.26),
                             (80, 52, 150, 98, 0.28), (34, 84, 72, 138, 0.30)):
        s_, e = P(a, b), P(la, lb)
        mid = ((s_[0] + e[0]) / 2, (s_[1] + e[1]) / 2); nx, ny = -(e[1] - s_[1]) * wd, (e[0] - s_[0]) * wd
        p.shape(f'<path d="M{f(s_[0])} {f(s_[1])} Q{f(mid[0] + nx)} {f(mid[1] + ny)} {f(e[0])} {f(e[1])} '
                f'Q{f(mid[0] - nx)} {f(mid[1] - ny)} {f(s_[0])} {f(s_[1])} Z"', GOLD, 9)
        p.cut(f'<path d="M{f(s_[0])} {f(s_[1])} L{f(e[0] - (e[0]-s_[0])*0.18)} {f(e[1] - (e[1]-s_[1])*0.18)}" stroke="#000" stroke-width="5"/>')
    # grapes hanging over the rim on the right
    for a, b in ((14, 96), (-4, 104), (-2, 124), (-20, 114), (-22, 134), (-38, 124), (-42, 142), (-58, 132)):
        x, y = P(a, b); p.shape(f'<circle cx="{f(x)}" cy="{f(y)}" r="14"', GOLD, 7)
    # round fruit heaped in the mouth: back row, then front row
    for a, b, r in ((92, -44, 30), (112, 4, 32), (90, 52, 28),
                    (44, -66, 30), (52, -12, 36), (46, 48, 32), (14, 16, 26)):
        x, y = P(a, b); p.shape(f'<circle cx="{f(x)}" cy="{f(y)}" r="{r}"', GOLD, 9)
        p.cut(f'<path d="M{f(x - r*0.45)} {f(y - r*0.2)} A{f(r*0.55)} {f(r*0.55)} 0 0 1 {f(x - r*0.05)} {f(y - r*0.6)}" '
              f'fill="none" stroke="#000" stroke-width="4" stroke-linecap="round"/>')

# ── Кадуцей (локальні координати: древко вертикально, крила вгорі) ──
def caduceus(p):
    p.shape('<rect x="-15" y="-236" width="30" height="420" rx="15"', GOLD, 12)
    def wing(sx):
        root_top, root_bot = (12, -226), (12, -176)
        pts = bez(root_top, (55, -266), (115, -300), (182, -306), 30)
        path = [(182, -306), (128, -266), (178, -258), (120, -230), (160, -212), (102, -200), (124, -174), root_bot]
        for a, b in zip(path, path[1:]):
            m = ((a[0] + b[0]) / 2 + 10, (a[1] + b[1]) / 2 + 9)
            for i in range(1, 11):
                t = i / 10; u = 1 - t
                pts.append((u*u*a[0] + 2*u*t*m[0] + t*t*b[0], u*u*a[1] + 2*u*t*m[1] + t*t*b[1]))
        p.shape(f'<path d="{poly([(sx * x, y) for x, y in pts])}"', SILVER, 10)
        for (x0, y0), (x1, y1) in (((128, -266), (74, -244)), ((120, -230), (70, -216)), ((102, -200), (62, -194))):
            p.cut(f'<path d="M{f(sx*x0)} {f(y0)} L{f(sx*x1)} {f(y1)}" stroke="#000" stroke-width="8" stroke-linecap="round"/>')
    wing(1); wing(-1)
    p.shape('<circle cx="0" cy="-254" r="30"', GOLD, 10)
    def snake(sx):
        pts = []
        segs = [((0, 140), (sx*58, 126), (sx*68, 84), (0, 58)),
                ((0, 58), (sx*-80, 26), (sx*-80, -32), (0, -60)),
                ((0, -60), (sx*78, -88), (sx*80, -128), (0, -146)),
                ((0, -146), (sx*-64, -160), (sx*-74, -178), (sx*-40, -188))]
        for s_ in segs: pts += bez(*s_, 30)[(1 if pts else 0):]
        p.tube(poly(pts, close=False), 26, SILVER, 9)
        hx, hy = sx * -30, -190
        p.shape(f'<ellipse cx="{hx}" cy="{hy}" rx="25" ry="16" transform="rotate({sx*-12} {hx} {hy})"', SILVER, 9)
        p.cut(f'<circle cx="{hx + sx*8}" cy="{hy - 3}" r="4.5" fill="#000"/>')
    snake(1); snake(-1)

def draw(p):
    mouth, axis = horn(p)
    fruit(p, mouth, axis)
    # caduceus: knob up-left, staff running down-right across the horn
    p.group('translate(494 430) rotate(-73) scale(0.72)', caduceus)

# the whole sign: centred in the shield like the tryzub (its middle at y≈505)
FIT = 'translate(500 512) scale(0.93) translate(-502 -462)'

def emblem(two_tone=True):
    m, col = Pen('mask'), Pen('color')
    m.group(FIT, draw); col.group(FIT, draw)
    colour = col.svg() if two_tone else ''
    return (f'<mask id="em" maskUnits="userSpaceOnUse" x="0" y="0" width="1000" height="1000">'
            f'<rect width="1000" height="1000" fill="#000"/>{m.svg()}</mask>'
            f'<g mask="url(#em)"><rect width="1000" height="1000" fill="url(#gold)"/>{colour}</g>')
