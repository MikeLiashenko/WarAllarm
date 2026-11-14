"""Step 2: the WarAlarm medallions in Blender -> STL parts for printing + photo renders.

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P blender_build.py   (after masks.py)

Each medallion (Ø80 mm, 1 Blender unit = 1 mm):
    flag     0.0-2.4 mm   two half discs: blue (top) and yellow (bottom)
    field    2.4-3.0      the black shield field: the whole inside of the rim, one plain plate
    rim      2.4-3.6      the white rim of the shield
    gold     3.0-3.8      gold parts of the emblem, standing on the field
    silver   3.0-3.8      white/silver parts; the emblem's black line work is the gaps between them
The parts never overlap, so a multi-colour printer (Bambu AMS) gets one STL per filament colour;
<icon>_all.stl is everything as one body for a single-colour print (the relief carries the picture).
"""
import json, math, os
import bpy, bmesh

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.dirname(HERE)
BUILD = os.path.join(HERE, 'build')
ICONS = [('waralarm', 'WarAlarm'), ('kyiv', 'WarAlarm Київ'), ('kharkiv', 'WarAlarm Харків')]
Z = {'flag': (0.0, 2.4), 'field': (2.4, 3.0), 'rim': (2.4, 3.6), 'gold': (3.0, 3.8), 'silver': (3.0, 3.8)}
COLORS = {  # base colour, metallic, roughness
    'blue': ((0.0, 0.10, 0.45), 0.0, 0.45), 'yellow': ((1.0, 0.62, 0.0), 0.0, 0.45), 'field': ((0.012, 0.012, 0.015), 0.0, 0.35),
    'rim': ((0.9, 0.9, 0.9), 0.0, 0.4), 'gold': ((1.0, 0.70, 0.12), 0.45, 0.3), 'silver': ((0.92, 0.93, 0.95), 0.1, 0.3),
}
SPACING = 92.0

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 0.001; scene.unit_settings.length_unit = 'MILLIMETERS'


def material(name):
    m = bpy.data.materials.get(name)
    if m: return m
    c, met, rough = COLORS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*c, 1); b.inputs['Metallic'].default_value = met; b.inputs['Roughness'].default_value = rough
    return m


def inside(pt, lp):
    x, y = pt; ins = False; n = len(lp)
    for i in range(n):
        x1, y1 = lp[i - 1]; x2, y2 = lp[i]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: ins = not ins
    return ins


def area(lp): return 0.5 * sum(lp[i - 1][0] * lp[i][1] - lp[i][0] * lp[i - 1][1] for i in range(len(lp)))


def groups(loops):
    """Even-odd nesting -> [(outer, [holes])]: each piece is filled on its own (one big fill of hundreds of
    outlines is where Blender's fill gives up)."""
    ar = [abs(area(l)) for l in loops]
    bb = [(min(p[0] for p in l), min(p[1] for p in l), max(p[0] for p in l), max(p[1] for p in l)) for l in loops]
    par = []
    for i, l in enumerate(loops):
        pt = l[0]; cont = []
        for j, m in enumerate(loops):
            if i != j and ar[j] > ar[i] and bb[j][0] <= pt[0] <= bb[j][2] and bb[j][1] <= pt[1] <= bb[j][3] and inside(pt, m): cont.append(j)
        par.append(cont)
    depth = [len(c) for c in par]
    out = {i: [] for i in range(len(loops)) if depth[i] % 2 == 0}
    for i in range(len(loops)):
        if depth[i] % 2 == 1:
            p = min(par[i], key=lambda j: ar[j])          # the smallest container = the direct parent
            if p in out: out[p].append(i)
    return [(loops[o], [loops[h] for h in hs]) for o, hs in out.items()]


def fill_mesh(loops, z0, z1):
    cu = bpy.data.curves.new('tmp', 'CURVE'); cu.dimensions = '2D'; cu.fill_mode = 'BOTH'
    cu.extrude = (z1 - z0) / 2; cu.resolution_u = 1
    for lp in loops:
        sp = cu.splines.new('POLY'); sp.points.add(len(lp) - 1)
        for p, (x, y) in zip(sp.points, lp): p.co = (x, y, 0, 1)
        sp.use_cyclic_u = True
    ob = bpy.data.objects.new('tmp', cu); bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob); bpy.data.curves.remove(cu)
    return me


def top_area(me):
    return sum(p.area for p in me.polygons if p.normal.z > 0.9)


def solid(name, loops, z0, z1, mat, coll, dx):
    """Filled outlines (even-odd) extruded from z0 to z1 -> one mesh object; checks the filled area."""
    bm = bmesh.new(); want = got = 0.0
    for outer, holes in groups(loops):
        me = fill_mesh([outer] + holes, z0, z1)
        a = abs(area(outer)) - sum(abs(area(h)) for h in holes); t = top_area(me)
        want += a; got += t
        if a > 0.05 and abs(t - a) > 0.05 * a + 0.05: print(f'  ! {name}: piece filled {t:.2f} of {a:.2f} mm2')
        bm.from_mesh(me); bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    print(f'{name}: {got:.1f} of {want:.1f} mm2 filled')
    mo = bpy.data.objects.new(name, me); coll.objects.link(mo); mo.location = (dx, 0, (z0 + z1) / 2)
    me.materials.append(material(mat))
    return mo


def half_disc(r, top, n=180):
    a0 = 0 if top else math.pi
    return [[r * math.cos(a0 + math.pi * i / n), r * math.sin(a0 + math.pi * i / n)] for i in range(n + 1)]


objs = {}
for i, (key, title) in enumerate(ICONS):
    d = json.load(open(os.path.join(BUILD, f'{key}.json')))
    r = d['diam'] / 2; dx = (i - 1) * SPACING
    coll = bpy.data.collections.new(title); scene.collection.children.link(coll)
    parts = [solid(f'{key}_blue', [half_disc(r, True)], *Z['flag'], 'blue', coll, dx),
             solid(f'{key}_yellow', [half_disc(r, False)], *Z['flag'], 'yellow', coll, dx)]
    # the field is one plain plate: the inside of the rim (the rim's inner outline = its smaller loop)
    rim = d['parts']['rim']; inner = min(rim, key=lambda l: abs(area(l)))
    parts.append(solid(f'{key}_field', [inner], *Z['field'], 'field', coll, dx))
    for part in ('rim', 'gold', 'silver'):
        if d['parts'].get(part): parts.append(solid(f'{key}_{part}', d['parts'][part], *Z[part], part, coll, dx))
    objs[key] = parts

# ---- STL: one file per colour (multi-colour print) + everything in one (single colour)
for key, parts in objs.items():
    sd = os.path.join(OUT, 'stl', key); os.makedirs(sd, exist_ok=True)
    for ob in parts:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        loc = ob.location.copy(); ob.location.x = 0                      # each file centred on the origin
        bpy.ops.wm.stl_export(filepath=os.path.join(sd, f'{ob.name}.stl'), export_selected_objects=True, apply_modifiers=True)
        ob.location = loc
    dx = (list(objs).index(key) - 1) * SPACING
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True); ob.location.x -= dx
    bpy.ops.wm.stl_export(filepath=os.path.join(sd, f'{key}_all.stl'), export_selected_objects=True, apply_modifiers=True)
    for ob in parts: ob.location.x += dx

# ---- photo set: table, soft light, camera
bpy.ops.mesh.primitive_plane_add(size=2000, location=(0, 0, -0.01)); table = bpy.context.object; table.name = 'table'
tm = bpy.data.materials.new('table'); tm.use_nodes = True
tb = tm.node_tree.nodes['Principled BSDF']; tb.inputs['Base Color'].default_value = (0.055, 0.06, 0.07, 1); tb.inputs['Roughness'].default_value = 0.8
table.data.materials.append(tm)
world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.03, 0.035, 0.045, 1); world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
for name, loc, energy, size in (('key', (-140, -160, 260), 7e5, 160), ('fill', (220, -60, 160), 1.6e5, 200), ('rimlight', (0, 220, 120), 2.5e5, 120)):
    ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.size = size
    lo = bpy.data.objects.new(name, ld); scene.collection.objects.link(lo); lo.location = loc
    lo.rotation_euler = (0, 0, 0); c = lo.constraints.new('TRACK_TO'); c.target = table; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
cam_d = bpy.data.cameras.new('cam'); cam_d.lens = 85; cam_d.clip_start = 1; cam_d.clip_end = 10000
cam = bpy.data.objects.new('cam', cam_d); scene.collection.objects.link(cam); scene.camera = cam
aim = bpy.data.objects.new('aim', None); scene.collection.objects.link(aim)
tc = cam.constraints.new('TRACK_TO'); tc.target = aim; tc.track_axis = 'TRACK_NEGATIVE_Z'; tc.up_axis = 'UP_Y'

scene.render.engine = 'CYCLES'
scene.cycles.samples = 160; scene.cycles.use_denoising = True
try:
    pr = bpy.context.preferences.addons['cycles'].preferences
    for t in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
        try:
            pr.compute_device_type = t; pr.get_devices()
            if any(dv.type == t for dv in pr.devices):
                for dv in pr.devices: dv.use = dv.type == t
                scene.cycles.device = 'GPU'; print('GPU', t); break
        except TypeError: pass
except Exception as e: print('CPU render', e)
scene.view_settings.view_transform = 'AgX'; scene.view_settings.look = 'AgX - Medium High Contrast'

R = os.path.join(OUT, 'renders'); os.makedirs(R, exist_ok=True)


def shot(fn, pos, target, w, h, lens=85):
    cam.location = pos; aim.location = target; cam_d.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.filepath = os.path.join(R, fn); bpy.ops.render.render(write_still=True)
    print('render', fn)


shot('all_three.png', (0, -255, 225), (0, -6, -2), 1920, 1080, 50)
shot('all_three_top.png', (0, -1, 520), (0, 0, 0), 1920, 900, 85)
for i, (key, _) in enumerate(ICONS):
    x = (i - 1) * SPACING
    shot(f'{key}.png', (x - 55, -150, 130), (x, 0, 2), 1400, 1400, 85)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'waralarm_medallions.blend'))
print('done')
