import math, tpl
from kharkiv import bez, poly, f

W = '#fff'; K = '#000'

def qcurve(a, m, b, n=10):
    return [((1-t)**2*a[0] + 2*(1-t)*t*m[0] + t*t*b[0], (1-t)**2*a[1] + 2*(1-t)*t*m[1] + t*t*b[1]) for t in (i/n for i in range(1, n+1))]

def wing(sx):
    root = (-22, -112)
    top = bez(root, (-45, -190), (-90, -262), (-138, -292), 30)
    tips = [(-138, -292), (-170, -246), (-188, -188), (-192, -126), (-182, -62), (-160, -4), (-126, 44), (-88, 70)]
    pts = list(top)
    for a, b in zip(tips, tips[1:]):
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        # bulge outward from the root → scalloped feather tips
        dx, dy = mx - root[0], my - root[1]; d = math.hypot(dx, dy)
        m = (mx + dx / d * 16, my + dy / d * 16)
        pts += qcurve(a, m, b)
    pts += bez((-88, 70), (-60, 20), (-40, -40), (-28, -90), 20)[1:]
    pts = [(sx * x, y) for x, y in pts]
    lines = ''
    for a, b in zip(tips[1:-1], tips[2:]):
        pass
    # feather separations: from inside the wing to each notch between tips
    for t in tips[1:-1]:
        ix, iy = root[0] + (t[0] - root[0]) * 0.42, root[1] + (t[1] - root[1]) * 0.42
        lines += f'<path d="M{f(sx*ix)} {f(iy)} L{f(sx*t[0]*0.97)} {f(t[1]*0.97 + (3 if t[1] > 0 else 0))}" stroke="{K}" stroke-width="7" stroke-linecap="round"/>'
    # covert band: an arc separating the small feathers from the long ones
    cov = bez((-34, -150), (-80, -150), (-120, -110), (-128, -40), 20)
    lines += f'<path d="{poly([(sx*x, y) for x, y in cov], close=False)}" fill="none" stroke="{K}" stroke-width="7"/>'
    return f'<path d="{poly(pts)}" fill="{W}" stroke="{K}" stroke-width="10" paint-order="stroke"/>' + lines

def flame_blade(x, y0, y1, w=13):
    # wavy (flaming) blade from y0 (guard) up to the point y1
    n = 7; L = y0 - y1
    left, right = [], []
    for i in range(n * 6 + 1):
        t = i / (n * 6); y = y0 - L * t
        taper = w * (1 - t ** 3 * 0.9)
        wave = math.sin(t * n * math.pi) * 6 * (1 - t)
        left.append((x - taper + wave, y)); right.append((x + taper + wave, y))
    pts = left + [(x, y1 - 8)] + right[::-1]
    return f'<path d="{poly(pts)}" fill="{W}" stroke="{K}" stroke-width="9" paint-order="stroke"/>'

def archangel():
    out = wing(1) + wing(-1)
    # halo + head
    out += f'<circle cx="0" cy="-172" r="52" fill="{W}" stroke="{K}" stroke-width="10"/>'
    out += f'<circle cx="0" cy="-172" r="41" fill="{K}"/>'
    out += f'<circle cx="0" cy="-170" r="29" fill="{W}"/>'
    # body: tunic with flared cloak
    body = ('M-50 -132 C-30 -140 30 -140 50 -132 C62 -110 50 -70 38 -42 '
            'C55 20 80 110 94 172 C60 184 -60 184 -94 172 C-80 110 -55 20 -38 -42 '
            'C-50 -70 -62 -110 -50 -132 Z')
    out += f'<path d="{body}" fill="{W}" stroke="{K}" stroke-width="12" paint-order="stroke"/>'
    # belt + folds + collar
    out += f'<path d="M-40 -46 Q0 -36 40 -46" fill="none" stroke="{K}" stroke-width="9"/>'
    for d in ('M-14 -26 C-22 40 -30 110 -36 176', 'M14 -26 C22 40 30 110 36 176', 'M-30 -20 C-46 50 -62 120 -70 174', 'M30 -20 C46 50 62 120 70 174'):
        out += f'<path d="{d}" fill="none" stroke="{K}" stroke-width="6"/>'
    out += f'<path d="M-26 -134 Q0 -112 26 -134" fill="none" stroke="{K}" stroke-width="7"/>'
    # boots
    for s in (1, -1):
        out += (f'<path d="M{s*14} 176 L{s*40} 176 L{s*44} 222 Q{s*64} 226 {s*72} 240 L{s*14} 240 Z" '
                f'fill="{W}" stroke="{K}" stroke-width="9" paint-order="stroke"/>')
    # sword arm (viewer's left): raised, fist on the grip, straight sword up
    out += f'<path d="M-44 -122 Q-100 -112 -108 -158" fill="none" stroke="{K}" stroke-width="38" stroke-linecap="round"/>'
    out += f'<path d="M-44 -122 Q-100 -112 -108 -158" fill="none" stroke="{W}" stroke-width="23" stroke-linecap="round"/>'
    bx, g = -108, -206
    blade = f'M{bx-12} {g} L{bx-12} -306 L{bx} -338 L{bx+12} -306 L{bx+12} {g} Z'
    out += f'<path d="{blade}" fill="{W}" stroke="{K}" stroke-width="9" paint-order="stroke"/>'
    out += f'<path d="M{bx} {g-8} L{bx} -310" stroke="{K}" stroke-width="5"/>'
    out += f'<rect x="{bx-40}" y="{g-2}" width="80" height="16" rx="8" fill="{W}" stroke="{K}" stroke-width="9" paint-order="stroke"/>'
    out += f'<rect x="{bx-17}" y="-188" width="34" height="34" rx="11" fill="{W}" stroke="{K}" stroke-width="9" paint-order="stroke"/>'
    # shield (viewer's right) with a cross
    sh = 'M86 -118 C126 -110 146 -80 146 -40 C146 10 116 50 86 70 C56 50 26 10 26 -40 C26 -80 46 -110 86 -118 Z'
    out += f'<path d="{sh}" fill="{W}" stroke="{K}" stroke-width="12" paint-order="stroke"/>'
    out += f'<path d="{sh}" fill="none" stroke="{K}" stroke-width="6" transform="translate(86 -24) scale(0.78) translate(-86 24)"/>'
    out += f'<path d="M86 -86 L86 34 M56 -40 L116 -40" stroke="{K}" stroke-width="12" stroke-linecap="square"/>'
    return out

def emblem(scale=0.95, cx=500, cy=548):
    inner = f'<g transform="translate({cx} {cy}) scale({scale})">{archangel()}</g>'
    return (f'<mask id="em" maskUnits="userSpaceOnUse" x="0" y="0" width="1000" height="1000">'
            f'<rect width="1000" height="1000" fill="#000"/>{inner}</mask>'
            f'<rect width="1000" height="1000" fill="url(#gold)" mask="url(#em)"/>')
