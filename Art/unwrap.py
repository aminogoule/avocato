import bpy, bmesh, json, sys, math, os
OUT = sys.argv[-1]
me = bpy.data.objects['avo_hero']
bpy.context.view_layer.objects.active = me
me.select_set(True)

ok = False
try:
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    r = bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.015,
                                 area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    ok = 'FINISHED' in r
    print("UNWRAP: smart_project ->", r)
except Exception as e:
    print("UNWRAP: smart_project не сработал:", e)
    try: bpy.ops.object.mode_set(mode='OBJECT')
    except Exception: pass

m = me.data
uvl = m.uv_layers.active
uvs = [tuple(l.uv) for l in uvl.data]
us=[u[0] for u in uvs]; vs=[u[1] for u in uvs]
print("UNWRAP: слой %r  U[%.3f,%.3f] V[%.3f,%.3f]" % (uvl.name, min(us),max(us),min(vs),max(vs)))
grid=set()
for u in uvs: grid.add((min(63,max(0,int(u[0]*64))), min(63,max(0,int(u[1]*64)))))
print("UNWRAP: занято ячеек 64x64: %d (%.1f%%)" % (len(grid), 100*len(grid)/4096))

# части меша, чтобы отличить косточку
bm = bmesh.new(); bm.from_mesh(m); bm.verts.ensure_lookup_table()
part = [-1]*len(m.vertices); seen=set(); pid=0; sizes=[]
for v in bm.verts:
    if v.index in seen: continue
    st=[v]; n=0; seen.add(v.index)
    while st:
        c=st.pop(); part[c.index]=pid; n+=1
        for e in c.link_edges:
            o=e.other_vert(c)
            if o.index not in seen: seen.add(o.index); st.append(o)
    sizes.append((pid,n)); pid+=1
bm.free()
big = max(sizes, key=lambda t: t[1])[0]
print("UNWRAP: частей", sizes, "| корпус =", big)

m.calc_loop_triangles()
tris=[]
for t in m.loop_triangles:
    tri=[]
    for li in t.loops:
        vi = m.loops[li].vertex_index
        co = m.vertices[vi].co
        uv = uvl.data[li].uv
        tri.append([round(co.x,5), round(co.y,5), round(co.z,5), round(uv[0],6), round(uv[1],6)])
    tris.append({"v": tri, "pit": int(part[m.loops[t.loops[0]].vertex_index] != big)})
json.dump({"tris": tris}, open(OUT, "w"))
print("UNWRAP: выгружено треугольников", len(tris), "->", OUT)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(OUT), "mh_uv.blend"))
