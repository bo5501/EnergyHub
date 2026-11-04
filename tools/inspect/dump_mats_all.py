"""导出所有建筑材质的关键参数，用于标定自发光强度。
用法: python dump_mats_all.py [关键字过滤]
"""
import UnityPy, sys, collections

P = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(P)

kw = sys.argv[1] if len(sys.argv) > 1 else ""

def txt(v):
    try:
        return v.read().m_Name
    except Exception:
        return "?"

rows = []
for obj in env.objects:
    if obj.type.name != "Material":
        continue
    try:
        m = obj.read()
    except Exception:
        continue
    name = m.m_Name
    if kw and kw.lower() not in name.lower():
        continue
    try:
        shader = m.m_Shader.read().m_Name
    except Exception:
        shader = "?"
    sp = m.m_SavedProperties
    cols, floats, texs = {}, {}, {}
    try:
        for k, v in sp.m_Colors:
            cols[k] = (round(v.r, 3), round(v.g, 3), round(v.b, 3), round(v.a, 3))
    except Exception:
        pass
    try:
        for k, v in sp.m_Floats:
            floats[k] = round(v, 3)
    except Exception:
        pass
    try:
        for k, v in sp.m_TexEnvs:
            texs[k] = txt(v.m_Texture) if getattr(v, "m_Texture", None) else None
    except Exception:
        pass
    rows.append((name, shader, cols, floats, texs))

print("total materials:", len(rows))
for name, shader, cols, floats, texs in rows:
    em = cols.get("_EmissionColor")
    if em is not None and max(em[:3]) <= 0.01:
        continue  # 只看会发光的
    print("=" * 78)
    print("MAT:", name, "| shader:", shader)
    for k in ("_Color", "_EmissionColor", "_RimColor", "_EmissionMask", "_SpecColor"):
        if k in cols:
            print("   C", k, cols[k])
    for k in ("_AlbedoMultiplier", "_EmissionMultiplier", "_EmissionSwitch", "_EmissionUsePower",
              "_Metallic", "_MetallicMultiplier", "_Glossiness", "_Smoothness", "_SmoothMultiplier",
              "_RimPower", "_RimIntensity", "_EmissionJitter", "_CullMode"):
        if k in floats:
            print("   F", k, floats[k])
    for k, v in texs.items():
        print("   T", k, v)
