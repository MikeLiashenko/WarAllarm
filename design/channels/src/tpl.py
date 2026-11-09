SHIELD = ("M500 130 C585 212 685 243 772 250 Q800 253 800 282 L800 470 "
          "C800 680 670 800 500 890 C330 800 200 680 200 470 L200 282 "
          "Q200 253 228 250 C315 243 415 212 500 130 Z")

def icon(emblem, rounded=True, field=("#1A1F29", "#05070B")):
    """emblem: SVG fragment in 0..1000 coordinates, painted with url(#gold)."""
    rx = 230 if rounded else 0
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1000" width="1000" height="1000">
  <defs>
    <linearGradient id="blue" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2A7BE0"/><stop offset="1" stop-color="#0B4DA2"/></linearGradient>
    <linearGradient id="yellow" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFD94A"/><stop offset="1" stop-color="#F6B600"/></linearGradient>
    <linearGradient id="field" gradientUnits="userSpaceOnUse" x1="0" y1="130" x2="0" y2="890"><stop offset="0" stop-color="{field[0]}"/><stop offset="1" stop-color="{field[1]}"/></linearGradient>
    <linearGradient id="gold" gradientUnits="userSpaceOnUse" x1="0" y1="240" x2="0" y2="770"><stop offset="0" stop-color="#FFE566"/><stop offset="1" stop-color="#F0AE00"/></linearGradient>
    <clipPath id="tile"><rect width="1000" height="1000" rx="{rx}"/></clipPath>
    <clipPath id="shieldClip"><path d="{SHIELD}"/></clipPath>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="14" stdDeviation="20" flood-color="#000" flood-opacity="0.5"/></filter>
  </defs>
  <g clip-path="url(#tile)">
    <rect width="1000" height="500" fill="url(#blue)"/>
    <rect y="500" width="1000" height="500" fill="url(#yellow)"/>
  </g>
  <path d="{SHIELD}" fill="url(#field)" filter="url(#shadow)"/>
  <g clip-path="url(#shieldClip)">{emblem}</g>
  <path d="{SHIELD}" fill="none" stroke="#fff" stroke-width="24" stroke-linejoin="round"/>
</svg>'''
