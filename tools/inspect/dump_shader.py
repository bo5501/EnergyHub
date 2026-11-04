import UnityPy, sys
p = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(p)

targets = ["Material #42", "24 - Default", "03 - Default", "02 - Default"]
seen=set()
for obj in env.objects:
    if obj.type.name != "Material": continue
    try: m=obj.read()
    except: continue
    if m.m_Name not in targets or m.m_Name in seen: continue
    seen.add(m.m_Name)
    print("="*70)
    print("MATERIAL:", m.m_Name)
    try:
        sh = m.m_Shader.read()
        print("  shader:", sh.m_Name, "| parsed:", sh.m_ParsedForm.m_Name if getattr(sh,'m_ParsedForm',None) else None)
    except Exception as e:
        print("  shader err", e)
    print("  saved props:", getattr(m,'m_SavedProperties',None) is not None)
    sp = m.m_SavedProperties
    try:
        for t in sp.m_TexEnvs:
            print("    tex:", t[0])
    except Exception as e: print("   texerr", e)
    try:
        for c in sp.m_Colors:
            print("    color:", c[0], c[1])
    except Exception as e: print("   colerr", e)
    try:
        for f in sp.m_Floats:
            print("    float:", f[0], f[1])
    except Exception: pass
