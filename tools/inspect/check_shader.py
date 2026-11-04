import UnityPy
p = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(p)
try:
    m = None
    for obj in env.objects:
        if obj.type.name != "Material": continue
        mm = obj.read()
        if mm.m_Name == "Material #42":
            m = mm; break
    sh = m.m_Shader.read()
    print("shader name:", sh.m_Name)
    pf = sh.m_ParsedForm
    print("parsed name:", pf.m_Name)
    props = pf.m_PropInfo.m_Props
    names = [pp.m_Name for pp in props]
    print("prop count:", len(names))
    print("has _InstBuffer:", "_InstBuffer" in names)
    print("names sample:", names[:60])
    print("\n--- constant buffers ---")
    try:
        for cb in pf.m_ConstantBuffers:
            print(" CB:", cb.m_Name, [v.m_Name for v in cb.m_VectorParams][:20])
    except Exception as e: print(" cb err", e)
except Exception as e:
    import traceback; traceback.print_exc()
