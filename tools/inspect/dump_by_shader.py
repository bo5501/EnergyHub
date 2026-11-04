"""按 shader 分类导出材质完整参数（含 shader 属性表）。
用法: python dump_by_shader.py [shader关键字] [材质名关键字]
"""
import UnityPy, sys

P = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(P)

shader_kw = (sys.argv[1] if len(sys.argv) > 1 else "Exchanger")
mat_kw = (sys.argv[2] if len(sys.argv) > 2 else "").lower()

# shader path_id -> 名字
shaders = {}
shader_objs = {}
for obj in env.objects:
    if obj.type.name != "Shader":
        continue
    try:
        s = obj.read()
        shaders[obj.path_id] = s.m_Name
        shader_objs[obj.path_id] = obj
    except Exception:
        pass

print("shaders:", len(shaders))
hits = [n for n in shaders.values() if shader_kw.lower() in (n or "").lower()]
print("命中 shader:", sorted(set(hits)))

def texname(v):
    try:
        if v.m_Texture is None:
            return None
        return v.m_Texture.read().m_Name
    except Exception:
        return "?"

shown = 0
for obj in env.objects:
    if obj.type.name != "Material":
        continue
    try:
        m = obj.read()
    except Exception:
        continue
    sid = m.m_Shader.path_id if getattr(m.m_Shader, "path_id", None) else 0
    sname = shaders.get(sid, "?")
    if shader_kw.lower() not in sname.lower():
        continue
    if mat_kw and mat_kw not in m.m_Name.lower():
        continue
    print("=" * 78)
    print("MAT:", m.m_Name, "| shader:", sname, "| pid", sid)
    sp = m.m_SavedProperties
    for k, v in sp.m_Colors:
        print("   C", k, tuple(round(x, 3) for x in (v.r, v.g, v.b, v.a)))
    for k, v in sp.m_Floats:
        print("   F", k, round(v, 4))
    for k, v in sp.m_TexEnvs:
        print("   T", k, texname(v))
    shown += 1
    if shown >= 12:
        break

# 打印该类 shader 的属性表
for pid, name in shaders.items():
    if shader_kw.lower() not in (name or "").lower():
        continue
    try:
        s = shader_objs[pid].read()
        pf = s.m_ParsedForm
        print("=" * 78)
        print("SHADER PROPS:", name)
        for p in pf.m_PropInfo.m_Props:
            print("   ", p.m_Type, p.m_Name, "|", p.m_Description, "|", getattr(p, "m_Flags", None))
        break
    except Exception as e:
        print("shader parse err:", e)
