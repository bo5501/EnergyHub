import UnityPy, sys, json, re
p = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
env = UnityPy.load(p)
kw = re.compile(r'(energy-?exchanger|ray-receiver|orbitalcollector|orbital-collector|assemble|storage|station)', re.I)
hits = []
names = []
for obj in env.objects:
    if obj.type.name == "GameObject":
        try:
            d = obj.read()
            n = d.m_Name
        except Exception:
            continue
        names.append(n)
        if kw.search(n):
            hits.append((obj.path_id, n))
print("total GO:", len(names))
print("hits:", len(hits))
for pid, n in hits[:80]:
    print(f"  {pid} {n}")
open(r"D:/戴森球计划反编译文件/EnergyHubMod/tools/inspect/go_names.txt", "w", encoding="utf-8").write("\n".join(sorted(set(names))))
