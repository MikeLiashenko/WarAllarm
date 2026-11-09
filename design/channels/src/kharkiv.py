"""Герб Харкова для іконки каналу — намальовано вручну за пропорціями офіційного герба.

Рог достатку (хвіст-завиток ліворуч унизу, розтруб із букетом праворуч угорі),
поверх нього кадуцей: навершя й срібні крила ліворуч угорі, древко донизу
праворуч, дві срібні змії з піднятими головами обвивають древко над рогом.

Координати — у системі референсу 960×1185 (так зручно звіряти), FIT переносить
їх у щит іконки. Кожен елемент малюється двічі: у масці (білий силует + чорні
проміжки й деталі) і в кольорі (золото / срібло).
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

def smooth(pts, closed=True, k=1/6):
    """Catmull-Rom through pts as cubic Béziers."""
    n = len(pts); P = lambda i: pts[i % n] if closed else pts[max(0, min(n - 1, i))]
    d = f'M{f(pts[0][0])} {f(pts[0][1])}'
    for i in range(n if closed else n - 1):
        p0, p1, p2, p3 = P(i - 1), P(i), P(i + 1), P(i + 2)
        c1 = (p1[0] + (p2[0] - p0[0]) * k, p1[1] + (p2[1] - p0[1]) * k)
        c2 = (p2[0] - (p3[0] - p1[0]) * k, p2[1] - (p3[1] - p1[1]) * k)
        d += f' C{f(c1[0])} {f(c1[1])} {f(c2[0])} {f(c2[1])} {f(p2[0])} {f(p2[1])}'
    return d + (' Z' if closed else '')

def sample(pts, closed=False, k=1/6, n=12):
    """Points along the Catmull-Rom curve (for tubes and bands)."""
    m = len(pts); P = lambda i: pts[i % m] if closed else pts[max(0, min(m - 1, i))]
    out = [pts[0]]
    for i in range(m if closed else m - 1):
        p0, p1, p2, p3 = P(i - 1), P(i), P(i + 1), P(i + 2)
        c1 = (p1[0] + (p2[0] - p0[0]) * k, p1[1] + (p2[1] - p0[1]) * k)
        c2 = (p2[0] - (p3[0] - p1[0]) * k, p2[1] - (p3[1] - p1[1]) * k)
        out += bez(p1, c1, c2, p2, n)[1:]
    return out

GOLD, SILVER = 'url(#gold)', 'url(#silver)'
GAP = 15          # black gap between parts, in reference units (≈9 in the icon)

class Pen:
    """mode 'mask': white shapes with black gaps; mode 'color': the same shapes in colour."""
    def __init__(self, mode): self.mode, self.out = mode, []
    def shape(self, tag, color=GOLD, gap=GAP):
        if self.mode == 'mask':
            self.out.append(f'{tag} fill="#fff" stroke="#000" stroke-width="{gap}" stroke-linejoin="round" paint-order="stroke"/>')
        else:
            self.out.append(f'{tag} fill="{color}"/>')
    def tube(self, d, w, color=GOLD, gap=GAP / 2, cap='round'):
        if self.mode == 'mask':
            self.out.append(f'<path d="{d}" fill="none" stroke="#000" stroke-width="{w + 2*gap}" stroke-linecap="{cap}" stroke-linejoin="round"/>')
            self.out.append(f'<path d="{d}" fill="none" stroke="#fff" stroke-width="{w}" stroke-linecap="{cap}" stroke-linejoin="round"/>')
        else:
            self.out.append(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{w}" stroke-linecap="{cap}" stroke-linejoin="round"/>')
    def cut(self, d, w=8, cap='round'):
        """black detail line: only exists in the mask."""
        if self.mode == 'mask':
            self.out.append(f'<path d="{d}" fill="none" stroke="#000" stroke-width="{w}" stroke-linecap="{cap}"/>')
    def hole(self, tag):
        if self.mode == 'mask': self.out.append(f'{tag} fill="#000"/>')
    def group(self, transform, fn, *a):
        self.out.append(f'<g transform="{transform}">'); fn(self, *a); self.out.append('</g>')
    def svg(self): return ''.join(self.out)

# ── Ріг ──
HORN = [(585, 330), (552, 392), (527, 462), (508, 525), (482, 572), (432, 590), (380, 594), (330, 602),
        (282, 620), (242, 650), (208, 684), (180, 722), (160, 762), (146, 802), (140, 838), (150, 872),
        (178, 894), (216, 900), (248, 880), (258, 848), (246, 820), (264, 792), (292, 763), (336, 736),
        (400, 716), (470, 703), (560, 683), (640, 649), (700, 603), (742, 541), (760, 472), (766, 420),
        (740, 360), (680, 325), (630, 318)]
RINGS = [((432, 592), (476, 702)), ((384, 595), (420, 711)), ((336, 601), (366, 726)), ((290, 617), (318, 744)),
         ((250, 646), (286, 766)), ((216, 678), (262, 792)), ((188, 714), (246, 818))]
GROOVES = [((566, 380), (560, 452), (522, 520), (490, 580)),
           ((634, 378), (642, 470), (584, 582), (530, 690)),
           ((700, 404), (712, 500), (654, 592), (604, 666)),
           ((746, 456), (754, 532), (714, 592), (674, 628))]

def horn(p):
    p.shape(f'<path d="{smooth(HORN)}"', GOLD, GAP + 4)
    for (ux, uy), (lx, ly) in RINGS:          # rings of the narrow part, bowed toward the tip
        mx, my = (ux + lx) / 2 - 16, (uy + ly) / 2 + 4
        p.cut(f'M{ux} {uy} Q{f(mx)} {f(my)} {lx} {ly}', 9)
    for a, b, c, d in GROOVES:                 # spiral flutes of the wide part
        p.cut(f'M{a[0]} {a[1]} C{b[0]} {b[1]} {c[0]} {c[1]} {d[0]} {d[1]}', 9)
    # rolled tip: a spiral groove inside the curl
    cx, cy = 204, 858; sp = []
    for i in range(70):
        a = 2.4 + i * 0.085; r = 34 - i * 0.42
        sp.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    p.cut(poly(sp, close=False), 8)

# ── Букет: листя навколо й між плодами, плоди зверху ──
BQ = (706, 312)
def leaf(p, base, tip, w, color=SILVER):
    bx, by = base; tx, ty = tip
    dx, dy = tx - bx, ty - by; L = math.hypot(dx, dy) or 1; nx, ny = -dy / L, dx / L
    m1 = (bx + dx * 0.45 + nx * w, by + dy * 0.45 + ny * w)
    m2 = (bx + dx * 0.45 - nx * w, by + dy * 0.45 - ny * w)
    d = f'M{f(bx)} {f(by)} Q{f(m1[0])} {f(m1[1])} {f(tx)} {f(ty)} Q{f(m2[0])} {f(m2[1])} {f(bx)} {f(by)} Z'
    p.shape(f'<path d="{d}"', color, 10)
    p.cut(f'M{f(bx + dx*0.12)} {f(by + dy*0.12)} L{f(tx - dx*0.16)} {f(ty - dy*0.16)}', 5)

def bouquet(p):
    cx, cy = BQ
    # three rings of leaves, outer first, fanned all round the heap
    for n, r0, r1, w, a0 in ((17, 80, 170, 36, 0.0), (17, 60, 146, 33, 0.18), (13, 30, 112, 30, 0.09)):
        for i in range(n):
            a = a0 + 2 * math.pi * i / n
            ca, sa = math.cos(a), math.sin(a)
            j = 1.0 if i % 2 else 0.9
            leaf(p, (cx + ca * r0 * 0.9, cy + sa * r0 * 0.85), (cx + ca * r1 * j, cy + sa * r1 * j * 0.9), w)
    def apple(x, y, r):
        p.shape(f'<circle cx="{x}" cy="{y}" r="{r}"', GOLD, 11)
        p.cut(f'M{f(x - r*0.58)} {f(y - r*0.1)} A{f(r*0.62)} {f(r*0.62)} 0 0 1 {f(x - r*0.12)} {f(y - r*0.6)}', 5)
        p.cut(f'M{f(x + r*0.05)} {f(y - r*0.7)} q3 -9 10 -13', 5)
    def pear(x, y, s_, rot):
        d = 'M0 34 C-24 34 -30 10 -19 -4 C-12 -13 -9 -28 0 -34 C9 -28 12 -13 19 -4 C30 10 24 34 0 34 Z'
        p.shape(f'<path d="{d}" transform="translate({x} {y}) rotate({rot}) scale({s_})"', GOLD, 11 / s_)
        p.cut(f'M0 -34 q2 -9 8 -12', 5) if False else None
    def grapes(x, y, rot, rows=(3, 3, 2, 1), r=10):
        a = math.radians(rot)
        for j, nrow in enumerate(rows):
            for i in range(nrow):
                gx, gy = (i - (nrow - 1) / 2) * r * 1.9, j * r * 1.65
                px, py = x + gx * math.cos(a) - gy * math.sin(a), y + gx * math.sin(a) + gy * math.cos(a)
                p.shape(f'<circle cx="{f(px)}" cy="{f(py)}" r="{r}"', GOLD, 8)
    # back row, middle, front
    apple(640, 228, 28); apple(702, 206, 25); apple(764, 234, 29)
    grapes(596, 262, 25, r=11); pear(816, 292, 1.05, 20)
    apple(668, 284, 34); pear(742, 298, 1.15, -10); grapes(798, 344, -20, r=11)
    apple(624, 342, 27); grapes(694, 334, 0, rows=(3, 2, 1), r=11); apple(756, 364, 26)

# ── Кадуцей ──
STAFF0, STAFF1 = (200, 314), (756, 850)
ANG = math.degrees(math.atan2(STAFF1[1] - STAFF0[1], STAFF1[0] - STAFF0[0]))

def staff(p, s0=0.0, s1=1.0):
    a = (STAFF0[0] + (STAFF1[0] - STAFF0[0]) * s0, STAFF0[1] + (STAFF1[1] - STAFF0[1]) * s0)
    b = (STAFF0[0] + (STAFF1[0] - STAFF0[0]) * s1, STAFF0[1] + (STAFF1[1] - STAFF0[1]) * s1)
    p.tube(f'M{f(a[0])} {f(a[1])} L{f(b[0])} {f(b[1])}', 20, GOLD, GAP / 2, 'round' if s1 == 1.0 and s0 == 0 else 'butt')

def capital(p):
    def local(q):
        q.shape('<path d="M4 -13 L-8 -25 L-44 -25 L-62 -11 L-62 11 L-44 25 L-8 25 L4 13 Z"', GOLD, 12)
        q.cut('M-14 -24 L-14 24', 6); q.cut('M-44 -24 L-50 0 L-44 24', 6)
    p.group(f'translate({STAFF0[0]} {STAFF0[1]}) rotate({f(ANG)})', local)

def feather(p, base, tip, w, sag=0.0):
    bx, by = base; tx, ty = tip
    dx, dy = tx - bx, ty - by; L = math.hypot(dx, dy) or 1; nx, ny = -dy / L, dx / L
    left, right = [], []
    for i in range(25):
        t = i / 24
        h = w * (0.45 + 0.55 * t) if t < 0.8 else w * math.sqrt(max(0.0, 1 - ((t - 0.8) / 0.2) ** 2))
        c = (bx + dx * t + nx * sag * math.sin(math.pi * t), by + dy * t + ny * sag * math.sin(math.pi * t))
        left.append((c[0] + nx * h, c[1] + ny * h)); right.append((c[0] - nx * h, c[1] - ny * h))
    p.shape(f'<path d="{poly(left + right[::-1])}"', SILVER, 11)
    p.cut(f'M{f(bx + dx*0.1)} {f(by + dy*0.1)} L{f(bx + dx*0.78)} {f(by + dy*0.78)}', 4)

def wing(p, root, lead, tip, tips, bases_t, sag, w):
    le = bez(root, lead[0], lead[1], tip, 60)
    # primaries: from the inner arc out to the feather tips, the lowest drawn first
    for k in range(len(tips) - 1, -1, -1):
        t = bases_t[k]; bx, by = le[int(t * 60)]
        # bases sit a little inside the leading edge, toward the root
        rx, ry = root
        base = (bx + (rx - bx) * 0.35, by + (ry - by) * 0.35)
        feather(p, base, tips[k], w, sag)
    # coverts: the band along the leading edge with a scalloped lower edge
    inner = [(x + (root[0] - x) * 0.0, y) for x, y in le]
    cx = sum(x for x, _ in tips) / len(tips); cy = sum(y for _, y in tips) / len(tips)
    lower = []
    for i, (x, y) in enumerate(le[::-1]):
        t = i / 60
        k = 0.30 * math.sin(math.pi * min(1, t * 1.05)) + 0.04
        bump = 1 + 0.25 * abs(math.sin(t * math.pi * 5))
        lower.append((x + (cx - x) * k * bump, y + (cy - y) * k * bump))
    p.shape(f'<path d="{poly(le + lower)}"', SILVER, 11)

def wings(p):
    # upper wing: raised up-right from the staff
    wing(p, (226, 340), ((232, 292), (286, 216)), (328, 166),
         [(330, 170), (348, 208), (350, 248), (338, 284), (314, 314), (280, 338)],
         [0.97, 0.84, 0.7, 0.56, 0.42, 0.28], 6, 18)
    # lower wing: spread down-left
    wing(p, (244, 346), ((196, 316), (118, 380)), (58, 444),
         [(60, 442), (96, 458), (134, 458), (170, 448), (202, 430), (228, 404)],
         [0.97, 0.84, 0.7, 0.56, 0.42, 0.28], -6, 17)

# ── Змії: голова й тіло одним контуром, хвіст звужується ──
SNAKE_A = [(300, 357), (328, 336), (356, 314), (380, 301), (401, 305), (411, 335), (407, 380), (395, 430),
           (380, 475), (366, 532), (378, 574), (425, 590), (482, 588), (532, 602), (564, 634), (580, 684),
           (590, 742), (608, 790), (640, 816)]
SNAKE_B = [(165, 495), (194, 472), (220, 453), (242, 441), (252, 456), (244, 480), (236, 502), (250, 518),
           (286, 514), (330, 494), (376, 474), (420, 464), (462, 478), (482, 520), (486, 566), (494, 612),
           (518, 648), (564, 670), (632, 679), (686, 694), (713, 720), (716, 748)]
SW = 26

def snake_frame(pts):
    c = sample(pts, n=16)
    s = [0.0]
    for i in range(1, len(c)): s.append(s[-1] + math.dist(c[i], c[i - 1]))
    L = s[-1]
    def width(x):
        prof = [(0, 5), (12, 19), (32, 33), (58, 30), (80, 21), (104, SW), (L * 0.72, SW), (L, 4)]
        for (x0, w0), (x1, w1) in zip(prof, prof[1:]):
            if x <= x1: return w0 + (w1 - w0) * (x - x0) / ((x1 - x0) or 1)
        return 4
    fr = []
    for i in range(len(c)):
        a, b = c[max(i - 1, 0)], c[min(i + 1, len(c) - 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]; d = math.hypot(dx, dy) or 1
        fr.append((c[i], (-dy / d, dx / d), width(s[i]), s[i]))
    return fr, L

def bands(p, fr, s0, s1):
    last = -99
    for (x, y), (nx, ny), w, s in fr:
        if s < s0 or s > s1 or s - last < 15: continue
        last = s
        p.cut(f'M{f(x - nx*w*0.44)} {f(y - ny*w*0.44)} L{f(x + nx*w*0.22)} {f(y + ny*w*0.22)}', 7.5, 'butt')

def snake(p, pts):
    fr, L = snake_frame(pts)
    left = [(x + nx * w / 2, y + ny * w / 2) for (x, y), (nx, ny), w, _ in fr]
    right = [(x - nx * w / 2, y - ny * w / 2) for (x, y), (nx, ny), w, _ in fr]
    p.shape(f'<path d="{poly(left + right[::-1])}"', SILVER, GAP)
    bands(p, fr, 104, L - 18)
    # eye (upper side of the head) and mouth
    (ex, ey), (nx, ny), w, _ = min(fr, key=lambda e: abs(e[3] - 30))
    p.hole(f'<circle cx="{f(ex - nx*7)}" cy="{f(ey - ny*7)}" r="5.5"')
    mouth = [(x + nx * 4, y + ny * 4) for (x, y), (nx, ny), w, s in fr if s < 26]
    p.cut(poly(mouth, close=False), 4)
    # neck line: where the head meets the body
    (hx, hy), (nx, ny), w, _ = min(fr, key=lambda e: abs(e[3] - 70))
    p.cut(f'M{f(hx - nx*w*0.5)} {f(hy - ny*w*0.5)} Q{f(hx + (fr[0][0][0]-hx)*0.08)} {f(hy + (fr[0][0][1]-hy)*0.08)} {f(hx + nx*w*0.5)} {f(hy + ny*w*0.5)}', 4)

def snake_patch(p, pts, s0, s1):
    """redraw a stretch of the body over whatever crossed it (no cut across the ends)."""
    fr, L = snake_frame(pts)
    seg = [c for c, _, _, s in fr if s0 <= s <= s1]
    p.tube(poly(seg, close=False), SW, SILVER, GAP / 2, 'butt')
    bands(p, fr, s0 + 4, s1 - 4)

def caduceus(p):
    wings(p)
    staff(p)
    capital(p)
    snake(p, SNAKE_B)
    snake(p, SNAKE_A)
    # weave: the staff over A's neck, B over A at the lower crossing
    staff(p, 0.26, 0.36)
    snake_patch(p, SNAKE_B, 470, 560)

def draw(p):
    horn(p)
    bouquet(p)
    caduceus(p)

# reference box (65..885 × 170..900) → icon shield, centred like the tryzub
FIT = 'translate(514 488) scale(0.64) translate(-475 -535)'

def emblem(two_tone=True, fit=FIT):
    m, col = Pen('mask'), Pen('color')
    m.group(fit, draw); col.group(fit, draw)
    colour = col.svg() if two_tone else ''
    return (f'<mask id="em" maskUnits="userSpaceOnUse" x="-2000" y="-2000" width="5000" height="5000">'
            f'<rect x="-2000" y="-2000" width="5000" height="5000" fill="#000"/>{m.svg()}</mask>'
            f'<g mask="url(#em)"><rect x="-2000" y="-2000" width="5000" height="5000" fill="url(#gold)"/>{colour}</g>')
