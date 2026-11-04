"""把 models/parts/*.obj 合并成 mod 用的单 Mesh 多 submesh 二进制。

输出格式 (little endian):
  char[4]  "BHUB"
  int32    submeshCount
  per submesh:
    int32  matId         0=hull 1=dark 2=emissive 3=glass 4=accent
    int32  vcount
    int32  icount        (索引数, = 三角形数*3)
    f32[vcount*3] pos
    f32[vcount*3] nrm
    int32[icount] idx     (相对该 submesh 的局部索引)
"""
import glob
import os
import struct

PARTS_DIR = os.path.join("..", "parts")
OUT = os.path.join("..", "..", "EnergyHubMod", "src", "EnergyHub", "Assets", "hub_mesh.bin")

MAT_ID = {"hull": 0, "dark": 1, "emissive": 2, "glass": 3, "accent": 4}


def read_obj(path):
    """返回 (verts[(x,y,z)], normals[(x,y,z)], tris[(i,j,k)])，索引为 0 基。"""
    verts, norms, faces = [], [], []
    cur_normal = (0.0, 0.0, 0.0)
    for line in open(path, "r", encoding="utf-8"):
        if line.startswith("v "):
            p = line.split()
            verts.append((float(p[1]), float(p[2]), float(p[3])))
        elif line.startswith("vn "):
            p = line.split()
            cur_normal = (float(p[1]), float(p[2]), float(p[3]))
        elif line.startswith("f "):
            toks = line.split()[1:]
            idx = []
            for t in toks:
                sp = t.split("/")
                vi = int(sp[0]) - 1
                if vi < 0:
                    vi = len(verts) + vi
                idx.append(vi)
            for k in range(1, len(idx) - 1):
                faces.append((idx[0], idx[k], idx[k + 1]))
    # 该文件通常只有一个 vn，广播到所有顶点
    if len(norms) == 0:
        norms = [cur_normal] * len(verts)
    return verts, norms, faces


def main():
    groups = {}  # matId -> (pos[], nrm[], idx[])
    for f in sorted(glob.glob(os.path.join(PARTS_DIR, "*-*.obj"))):
        base = os.path.basename(f)[:-4]
        if "-" not in base:
            continue
        mat = base.rsplit("-", 1)[1]
        if mat not in MAT_ID:
            continue
        mid = MAT_ID[mat]
        verts, norms, faces = read_obj(f)
        if not faces:
            continue
        pos_l, nrm_l, idx_l = groups.setdefault(mid, ([], [], []))
        off = len(pos_l)
        pos_l.extend(verts)
        nrm_l.extend(norms)
        for a, b, c in faces:
            idx_l.extend((off + a, off + b, off + c))

    subs = sorted(groups.items())
    total_v = sum(len(p) for _, (p, _, _) in subs)
    total_t = sum(len(i) // 3 for _, (_, _, i) in subs)
    print(f"submeshes={len(subs)} verts={total_v} tris={total_t}")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as fp:
        fp.write(b"BHUB")
        fp.write(struct.pack("<i", len(subs)))
        for mid, (pos, nrm, idx) in subs:
            fp.write(struct.pack("<iii", mid, len(pos), len(idx)))
            for x, y, z in pos:
                fp.write(struct.pack("<fff", x, y, z))
            for x, y, z in nrm:
                fp.write(struct.pack("<fff", x, y, z))
            for i in idx:
                fp.write(struct.pack("<i", i))
            print(f"  mat={mid} v={len(pos)} i={len(idx)} tris={len(idx)//3}")

    print("written:", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
