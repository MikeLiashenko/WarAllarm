"""Герб Києва для іконки каналу — намальовано вручну за пропорціями офіційного герба.

Архістратиг Михаїл на повний зріст: німб, великі крила від плечей до ніг,
плащ, туніка з розеткою на грудях і поясом, у правій руці (ліворуч для глядача)
опущений полум'яний меч вістрям угору-ліворуч, у лівій — овальний щит із хрестом.

Координати — у системі референсу 960×1281; FIT переносить їх у щит іконки.
Палітра: золото (німб, волосся, плащ, меч, щит, пояс, розетка, чоботи) і срібло
(крила, обличчя й руки, туніка, поле щита).
"""
import math
from kharkiv import Pen, GOLD, SILVER, bez, poly, smooth, f

GAP = 16          # ≈9 in the icon
AXIS = 970        # x' = AXIS - x mirrors the left wing into the right one

def mirror(pts): return [(AXIS - x, y) for x, y in pts]

# ── Крила ──
WING_L = [(368, 300), (376, 232), (366, 172), (336, 130), (290, 112), (246, 126), (216, 170), (203, 240),
          (199, 330), (198, 450), (199, 580), (198, 710), (196, 830), (195, 930), (196, 986)]
WING_L_IN = [(326, 760), (338, 640), (346, 520), (356, 410)]
FEATHERS_L = [[(226, 318), (222, 640), (216, 968)], [(254, 330), (250, 640), (244, 948)],
              [(282, 330), (278, 620), (272, 920)], [(310, 318), (306, 580), (300, 880)],
              [(336, 300), (334, 500), (328, 760)]]

def wing(p, side):
    m = (lambda pts: pts) if side < 0 else mirror
    # scalloped lower edge from the tip up to where the cloak covers the wing
    tip, end = (196, 986), (314, 832)
    sc = []
    n = 4
    for i in range(n):
        a = (tip[0] + (end[0] - tip[0]) * i / n, tip[1] + (end[1] - tip[1]) * i / n)
        b = (tip[0] + (end[0] - tip[0]) * (i + 1) / n, tip[1] + (end[1] - tip[1]) * (i + 1) / n)
        for j in range(1, 11):
            t = j / 10; bump = math.sin(math.pi * t) * 16
            sc.append((a[0] + (b[0] - a[0]) * t + bump * 0.55, a[1] + (b[1] - a[1]) * t + bump * 0.85))
    outline = WING_L + sc + WING_L_IN
    p.shape(f'<path d="{smooth(m(outline))}"', SILVER, GAP)
    # coverts: an arc of small feathers under the top of the wing
    arc = [(352, 236), (344, 186), (318, 158), (284, 150), (254, 162), (236, 196), (228, 250)]
    p.cut(smooth(m(arc), closed=False), 8)
    for i in range(len(arc) - 1):                      # little scallops below the arc
        (x0, y0), (x1, y1) = arc[i], arc[i + 1]
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        q = m([(x0, y0), (mx + (370 - mx) * 0.0, my + 26), (x1, y1)])
        p.cut(f'M{f(q[0][0])} {f(q[0][1])} Q{f(q[1][0])} {f(q[1][1])} {f(q[2][0])} {f(q[2][1])}', 6)
    arc1 = [(356, 262), (340, 240), (312, 230), (282, 228), (254, 234), (232, 248), (216, 268)]
    for i in range(len(arc1) - 1):
        (x0, y0), (x1, y1) = arc1[i], arc1[i + 1]
        q = m([(x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2 + 24), (x1, y1)])
        p.cut(f'M{f(q[0][0])} {f(q[0][1])} Q{f(q[1][0])} {f(q[1][1])} {f(q[2][0])} {f(q[2][1])}', 6)
    arc2 = [(358, 300), (338, 316), (310, 326), (282, 330), (254, 330), (228, 322), (210, 310)]
    p.cut(smooth(m(arc2), closed=False), 8)
    for i in range(len(arc2) - 1):
        (x0, y0), (x1, y1) = arc2[i], arc2[i + 1]
        q = m([(x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2 + 26), (x1, y1)])
        p.cut(f'M{f(q[0][0])} {f(q[0][1])} Q{f(q[1][0])} {f(q[1][1])} {f(q[2][0])} {f(q[2][1])}', 6)
    # long feathers: shafts between the columns, and rounded feather tips staggered down each column
    for fl in FEATHERS_L:
        p.cut(smooth(m(fl), closed=False), 6)
    for ci, xc in enumerate((212, 239, 267, 295, 322)):
        y = 410 + (ci % 2) * 60
        while y < 910 - ci * 26:
            xm = xc - (y - 400) * 0.012
            q = m([(xm - 11, y - 16), (xm, y + 14), (xm + 11, y - 16)])
            p.cut(f'M{f(q[0][0])} {f(q[0][1])} Q{f(q[1][0])} {f(q[1][1])} {f(q[2][0])} {f(q[2][1])}', 6)
            y += 120

# ── Плащ, тіло, голова ──
CLOAK = [(398, 320), (350, 346), (322, 420), (308, 560), (302, 700), (318, 830), (354, 944), (430, 1004),
         (530, 1010), (640, 966), (712, 900), (706, 760), (690, 620), (662, 420), (622, 338), (566, 318),
         (482, 310)]
TUNIC = [(400, 322), (440, 312), (482, 322), (525, 312), (566, 322), (582, 380), (574, 500), (590, 560),
         (596, 700), (590, 762), (578, 802), (480, 806), (350, 800), (344, 746), (352, 640), (364, 560),
         (386, 524), (378, 470), (370, 420), (378, 360)]

def body(p):
    p.shape(f'<path d="{smooth(CLOAK)}"', GOLD, GAP)
    p.cut('M352 360 C336 520 330 700 352 900', 7)          # a fold of the cloak
    p.cut('M672 470 C690 620 700 760 690 880', 7)
    # legs and boots
    for leg, garter, boot in (
            ([(382, 796), (444, 796), (448, 862), (440, 936), (437, 1044), (382, 1044), (378, 936), (372, 862)],
             (376, 930, 68, 50),
             [(382, 1040), (438, 1040), (442, 1084), (420, 1110), (390, 1124), (334, 1126), (340, 1104), (372, 1080)]),
            ([(486, 796), (552, 796), (557, 860), (552, 930), (550, 1022), (498, 1022), (494, 930), (488, 860)],
             (490, 910, 66, 50),
             [(500, 1018), (552, 1018), (557, 1060), (590, 1088), (608, 1114), (560, 1116), (530, 1102), (505, 1074)])):
        p.shape(f'<path d="{smooth(leg)}"', SILVER, GAP)
        x, y, w, h = garter
        p.shape(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10"', GOLD, 12)
        p.shape(f'<path d="{smooth(boot, k=1/8)}"', GOLD, GAP)
    p.cut('M398 852 Q412 870 428 852', 6); p.cut('M504 852 Q520 870 536 852', 6)     # knees
    # tunic
    p.shape(f'<path d="{smooth(TUNIC, k=1/7)}"', SILVER, GAP)
    p.cut('M442 316 Q482 346 524 316', 7)                                            # neckline
    # belt
    p.shape('<path d="M382 498 Q480 512 578 498 L580 524 Q480 540 384 524 Z"', GOLD, 12)
    # pleats and hem
    for x0, x1 in ((396, 380), (436, 428), (476, 476), (516, 524), (556, 572)):
        p.cut(f'M{x0} 548 L{x1} 790', 7)
    p.cut('M348 748 Q470 762 592 748', 7); p.cut('M350 772 Q470 786 590 772', 6)
    # rosette on the chest
    p.shape('<circle cx="476" cy="420" r="52"', GOLD, 12)
    for i in range(10):
        a = 2 * math.pi * i / 10
        cx, cy = 476 + math.cos(a) * 33, 420 + math.sin(a) * 33
        p.hole(f'<ellipse cx="{f(cx)}" cy="{f(cy)}" rx="12" ry="7" transform="rotate({f(math.degrees(a))} {f(cx)} {f(cy)})"')
    p.hole('<circle cx="476" cy="420" r="15"')
    p.shape('<circle cx="476" cy="420" r="9"', GOLD, 0.1)

def head(p):
    p.shape('<circle cx="482" cy="190" r="94"', GOLD, GAP)          # halo
    p.cut('<circle cx="482" cy="190" r="80"/>' and 'M402 190 A80 80 0 1 1 562 190 A80 80 0 1 1 402 190', 6)
    p.shape('<rect x="461" y="262" width="42" height="62" rx="14"', SILVER, GAP)     # neck
    p.shape('<ellipse cx="482" cy="206" rx="56" ry="62"', GOLD, GAP)                 # hair
    p.shape('<path d="M482 176 C508 176 520 196 520 222 C520 252 504 280 482 280 C460 280 444 252 444 222 C444 196 456 176 482 176 Z"', SILVER, 12)
    for x in (464, 500):
        p.hole(f'<ellipse cx="{x}" cy="224" rx="6" ry="4"')
    p.cut('M452 211 Q462 205 473 212', 5); p.cut('M491 212 Q502 205 512 211', 5)
    p.cut('M482 226 L478 247 L487 249', 4.5); p.cut('M473 263 Q482 267 491 263', 5)

# ── Рука з мечем ──
def sword_arm(p):
    # sleeve tabs at the shoulder
    for i, (x, y, a) in enumerate(((330, 372, 28), (352, 384, 16), (376, 390, 4), (400, 392, -8))):
        p.shape(f'<rect x="{x - 12}" y="{y - 6}" width="24" height="54" rx="11" transform="rotate({a} {x} {y})"', SILVER, 11)
    # arm: shoulder → elbow → wrist
    p.tube('M352 420 Q330 462 300 486 Q272 508 246 524', 44, SILVER, GAP / 2)
    # flaming blade, from the guard up-left to the point
    B, T = (196, 488), (74, 248)
    dx, dy = T[0] - B[0], T[1] - B[1]; L = math.hypot(dx, dy); ux, uy = dx / L, dy / L; nx, ny = -uy, ux
    left, right = [], []
    for i in range(61):
        t = i / 60
        h = 20 * (1 - t) ** 0.8 + 2
        wl = 7 * math.sin(t * math.pi * 7) * (1 - t * 0.6)
        wr = 7 * math.sin(t * math.pi * 7 + 1.6) * (1 - t * 0.6)
        cx, cy = B[0] + dx * t, B[1] + dy * t
        left.append((cx + nx * (h + wl), cy + ny * (h + wl))); right.append((cx - nx * (h + wr), cy - ny * (h + wr)))
    p.shape(f'<path d="{poly(left + [(T[0] + ux * 6, T[1] + uy * 6)] + right[::-1])}"', GOLD, GAP)
    p.cut(f'M{f(B[0] + dx*0.06)} {f(B[1] + dy*0.06)} L{f(B[0] + dx*0.72)} {f(B[1] + dy*0.72)}', 5)
    # cross-guard and fist
    ang = math.degrees(math.atan2(dy, dx))
    p.shape(f'<rect x="-11" y="-40" width="22" height="80" rx="7" transform="translate({B[0] + 8} {B[1] + 16}) rotate({f(ang)})"', GOLD, 12)
    p.shape('<path d="M206 512 C222 498 252 504 262 522 C270 538 260 560 240 566 C220 572 200 560 198 542 C196 530 198 520 206 512 Z"', SILVER, 12)
    p.cut('M214 530 Q230 526 246 534', 5); p.cut('M212 546 Q228 542 244 550', 5)

# ── Щит ──
def shield(p):
    p.shape('<ellipse cx="596" cy="466" rx="86" ry="158"', GOLD, GAP)
    p.hole('<ellipse cx="596" cy="466" rx="66" ry="136"')
    p.shape('<ellipse cx="596" cy="466" rx="58" ry="128"', SILVER, 0.1)
    p.shape('<path d="M585 336 L607 336 L607 452 L654 450 L655 472 L607 474 L607 598 L585 598 L585 474 L538 476 L537 454 L585 452 Z"', GOLD, 10)

def draw(p):
    wing(p, -1); wing(p, 1)
    p.shape('<circle cx="482" cy="190" r="94"', GOLD, GAP)
    body(p)
    head(p)
    p.shape('<ellipse cx="420" cy="320" rx="17" ry="12"', GOLD, 10)                 # clasp
    sword_arm(p)
    shield(p)

# reference box (74..790 × 96..1126) → icon shield
FIT = 'translate(500 512) scale(0.57) translate(-432 -612)'

def emblem(two_tone=True, fit=FIT):
    m, col = Pen('mask'), Pen('color')
    m.group(fit, draw); col.group(fit, draw)
    colour = col.svg() if two_tone else ''
    return (f'<mask id="em" maskUnits="userSpaceOnUse" x="-2000" y="-2000" width="5000" height="5000">'
            f'<rect x="-2000" y="-2000" width="5000" height="5000" fill="#000"/>{m.svg()}</mask>'
            f'<g mask="url(#em)"><rect x="-2000" y="-2000" width="5000" height="5000" fill="url(#gold)"/>{colour}</g>')
