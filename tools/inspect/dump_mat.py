import UnityPy, sys
p = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(p)

want = sys.argv[1] if len(sys.argv)>1 else "energy-exchanger"

# 1) 找目标 GO 的所有子 GO + MeshRenderer 的材质
roots=[]
for obj in env.objects:
    if obj.type.name != "GameObject": continue
    try: d=obj.read()
    except: continue
    if d.m_Name == want: roots.append(obj)
print("root count:", len(roots))

def walk(tr, depth=0, out=None):
    if out is None: out=[]
    try:
        go = tr.m_GameObject.read()
    except Exception:
        return out
    mats=[]
    for c in go.m_Component:
        if c.component.type.name == "MeshRenderer":
            try:
                mr = c.component.read()
                for m in mr.m_Materials:
                    mm = m.read()
                    mats.append(mm.m_Name)
            except Exception as e: mats.append("?")
    out.append((depth, go.m_Name, mats))
    for ch in tr.m_Children:
        try: walk(ch.read(), depth+1, out)
        except Exception: pass
    return out

for r in roots[:1]:
    for c in r.read().m_Component:
        if c.component.type.name == "Transform":
            for depth,name,mats in walk(c.component.read()):
                print(" "*depth, name, "->", mats)

# 2) LODModelDesc 所在 GO
print("\n=== LODModelDesc owners ===")
cnt=0
for obj in env.objects:
    if obj.type.name != "MonoBehaviour": continue
    try: d=obj.read()
    except: continue
    sc = getattr(d,'m_Script',None)
    if sc is None: continue
    try: sn = sc.read().m_Name
    except: continue
    if sn == "LODModelDesc":
        try: go = d.m_GameObject.read(); gn = go.m_Name
        except: gn="?"
        if want in gn:
            print("  ", gn, "lodDistances", getattr(d,'lodDistances',None), "vertaPaths", getattr(d,'lodVertaPaths',None))
            cnt+=1
            if cnt>10: break
