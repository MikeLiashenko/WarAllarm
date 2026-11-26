#!/usr/bin/env python3
"""OSM (osmium export geojsonseq) -> shelters.json for the app and the bot.

Output: {"v": "YYYY-MM-DD", "n": N, "p": [[lat, lon, kind, label], ...]}
kind: s — shelter (укриття / сховище), m — metro entrance, p — underground parking.
Same filter as the live Overpass query: bus-stop canopies, gazebos, picnic shelters and
old military pillboxes are not shelters from air strikes.
"""
import json, sys, datetime, collections

NOT_SHELTER = {"public_transport", "picnic_shelter", "weather_shelter", "lean_to", "basic_hut",
               "field_shelter", "rock_shelter", "sun_shelter", "gazebo", "changing_rooms"}
NOT_BUNKER = {"pillbox", "gun_emplacement", "munitions", "technical", "hardened_aircraft_shelter",
              "missile_silo", "observation"}


def coords(g):
    t, c = g["type"], g["coordinates"]
    if t == "Point":
        return [c]
    if t == "LineString":
        return c
    if t == "Polygon":
        return c[0]
    if t == "MultiPolygon":
        return c[0][0]
    return []


def kind(p, stats):
    a, st = p.get("amenity"), p.get("shelter_type", "")
    if p.get("railway") == "subway_entrance":
        return "m"
    if a == "parking":
        return "p" if p.get("parking") == "underground" else None
    if p.get("emergency") == "shelter" or st == "bomb_shelter":
        return "s"
    if a == "shelter":
        stats["shelter_type:" + (st or "-")] += 1
        if st in NOT_SHELTER or p.get("public_transport") or p.get("bus") == "yes" or p.get("highway") == "bus_stop":
            return None
        return "s"
    if p.get("building") == "bunker" or p.get("bunker_type"):
        bt = p.get("bunker_type", "")
        stats["bunker_type:" + (bt or "-")] += 1
        if bt in NOT_BUNKER or p.get("military") or p.get("historic") or p.get("ruins") == "yes" or p.get("abandoned") == "yes":
            return None
        return "s"
    return None


def label(p, k):
    """Ready-to-show label (the app and the bot only display it)."""
    n = (p.get("name") or "").strip()
    if k == "m":
        return f"Метро «{n}» (вхід)" if n else f"Вхід у метро №{p['ref']}" if p.get("ref") else "Вхід у метро"
    if k == "p":
        return f"Підземний паркінг «{n}»" if n else "Підземний паркінг"
    if n:
        return n
    addr = ", ".join(x for x in (p.get("addr:street"), p.get("addr:housenumber")) if x)
    return f"Укриття · {addr}" if addr else "Укриття"


def main(src, dst):
    out, stats, seen = [], collections.Counter(), set()
    for line in open(src, encoding="utf-8"):
        line = line.strip().lstrip("\x1e")
        if not line:
            continue
        f = json.loads(line)
        p = f.get("properties") or {}
        k = kind(p, stats)
        if not k:
            continue
        cs = coords(f.get("geometry") or {"type": "", "coordinates": []})
        if not cs:
            continue
        lon = sum(c[0] for c in cs) / len(cs)
        lat = sum(c[1] for c in cs) / len(cs)
        key = (round(lat, 4), round(lon, 4), k)
        if key in seen:
            continue
        seen.add(key)
        stats["kind:" + k] += 1
        out.append([round(lat, 5), round(lon, 5), k, label(p, k)])
    json.dump({"v": datetime.date.today().isoformat(), "n": len(out), "p": out}, open(dst, "w", encoding="utf-8"),
              ensure_ascii=False, separators=(",", ":"))
    for s, n in stats.most_common(40):
        print(f"{n:7d}  {s}")
    print("total", len(out))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
