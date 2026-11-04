import UnityPy, sys, json
p = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(p)
target = sys.argv[1] if len(sys.argv) > 1 else "energy-exchanger"

def read_obj(obj):
    try: return obj.read()
    except Exception as e: return None

# 找目标 GO
go = None
for obj in env.objects:
    if obj.type.name != "GameObject": continue
    d = read_obj(obj)
    if d is not None and d.m_Name == target:
        go = (obj, d); break
if go is None:
    print("not found:", target); sys.exit(1)

obj, d = go
print("GAME OBJECT:", d.m_Name, "path_id", obj.path_id)
print("components:", len(d.m_Component))
for c in d.m_Component:
    try:
        comp = c.component.read()
        pb = c.component
    except Exception as e:
        print("  ?", e); continue
    print(f"  [{pb.type.name}] path={pb.path_id} name={getattr(comp,'m_Name','')}")
    if pb.type.name == "Transform":
        print("     pos", comp.m_LocalPosition, "children", len(comp.m_Children))
        for ch in comp.m_Children[:20]:
            try:
                co = ch.read(); cgo = co.m_GameObject.read()
                print("       child:", cgo.m_Name, co.m_LocalPosition, co.m_LocalScale)
            except Exception: pass
    if pb.type.name == "MonoBehaviour":
        print("     script:", comp.m_Script.read().m_Name if hasattr(comp,'m_Script') else '?')
