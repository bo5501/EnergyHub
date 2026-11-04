using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnergyHub
{
    /// <summary>
    /// 运行时构造建筑 prefab 与 PrefabDesc。
    /// 不走 AssetBundle：BatchRenderer 用 DrawMeshInstancedIndirect(mesh,...) 渲染，
    /// lodVertas 只是可选的顶点动画缓冲，置 null 不影响出图。
    /// </summary>
    internal static class HubPrefab
    {
        private static GameObject _prefab;
        private static PrefabDesc _desc;

        /// <summary>兜底路径：万一某处仍按 PrefabPath 去 Resources.Load，能拿到一个真实 prefab。</summary>
        public const string FallbackPrefabPath = "Entities/Prefabs/energy-exchanger";

        /// <summary>
        /// 传送带接口数量（1.10.0：4 → 8）。
        /// 前面 4 个（-Z 侧）对应原版 <c>PowerExchangerComponent.belt0~3</c>；
        /// 背面 4 个（+Z 侧）是新增的进口，原版组件不认，由 <see cref="HubManager"/> 直接读
        /// 实体连接点 4~7 取料。接口索引 = portPoses 下标 = 建造时写入的连接点索引，
        /// 实体连接池每个实体 16 格，4~11 是空闲段，加到 8 个安全。
        /// </summary>
        public const int PortCount = 8;

        /// <summary>每侧的接口数（8 个 = 前 4 + 后 4）。</summary>
        public const int PortsPerSide = PortCount / 2;

        public static PrefabDesc GetDesc()
        {
            if (_desc != null)
            {
                // 首次构建时原版建筑 prefab 可能还没 Preload（拿不到 instancing 材质），
                // 这里补一次：拿到真材质就替换掉占位材质。
                if (!HubMesh.HasTemplate)
                    TryRetryMaterials();
                return _desc;
            }

            Mesh mesh = HubMesh.Get();
            Material[] mats = HubMesh.BuildMaterials();

            var root = new GameObject("energy-hub");
            root.SetActive(false);      // 纯模板对象，不进场景

            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            BuildColliders(root);

            _prefab = root;
            _desc = new PrefabDesc(C.ModelId, root);

            // ---- 渲染数据（ReadPrefab 读不到 LODModelDesc，这里手工补齐）----
            // 注意：lodMeshes / lodMaterials / lodVertas / lodSubmeshIgnores 按 lodCount 循环访问，
            // 长度 >= lodCount 即可（这里 1）。
            _desc.lodCount = 1;
            _desc.lodMeshes = new Mesh[1];
            _desc.lodMeshes[0] = mesh;
            _desc.lodMaterials = new Material[1][];
            _desc.lodMaterials[0] = mats;
            _desc.lodBlueprintMaterials = new Material[1][];
            _desc.lodBlueprintMaterials[0] = mats;
            _desc.lodSubmeshIgnores = new bool[1][];
            _desc.lodSubmeshIgnores[0] = new bool[mats.Length];
            _desc.lodVertas = new VertaBuffer[1];
            // ⚠ lodDistances 必须是 4 个：ObjectRenderer.Render / DynamicRenderer / LabRenderer /
            //   InserterRenderer 全都**无条件**读 [0][1][2][3]（不看 lodCount），
            //   长度不足就是 IndexOutOfRangeException @ ObjectRenderer.Render。
            // 距离要对齐原版：原版单层 LOD 建筑的 LODModelDesc 全都是 [5000]（从
            // resources.assets 里 837 个 LODModelDesc 统计出来），超过这个距离才会被
            // GPU 的 LODCulling kernel 剔除。之前设 220~360，视角一拉远（>360m）
            // 建筑就整个消失，而原版建筑还能看到。
            _desc.lodDistances = new float[4] { 5000f, 5000f, 5000f, 5000f };
            _desc.cullingHeight = C.Height + 2f;
            _desc.startInstCapacity = 16;
            _desc.batchCapacity = 2048;
            _desc.castShadow = 99;
            _desc.recvShadow = 99;

            if (_desc.mesh == null)
                _desc.mesh = mesh;
            if (_desc.materials == null || _desc.materials.Length == 0)
                _desc.materials = mats;

            // ---- 电网 ----
            // isPowerExchanger：拿到 PowerExchangerComponent（能量池 + 与本地电网充放电）
            // isPowerNode：  必须有！PlanetFactory 只有这一个分支会 powerSystem.NewNodeComponent，
            //                没有 node 就没有 networkId，电力感应塔永远连不上，能量池也收不到电。
            _desc.isPowerExchanger = true;
            _desc.exchangeEnergyPerTick = C.MaxPowerPerTick;
            _desc.maxExcEnergy = C.PoolCapacity;
            _desc.emptyId = 0;
            _desc.fullId = 0;

            _desc.isPowerNode = true;
            _desc.powerConnectDistance = C.PowerConnectDistance;
            _desc.powerCoverRadius = 0f;                  // 不做无线供电，只作为节点接入电网
            _desc.powerPoint = new Vector3(0f, 2f, 0f);

            // ---- 卡槽：放信息矩阵 / 空间翘曲器 / 透镜 ----
            _desc.isStorage = true;
            _desc.storageCol = C.SlotCols;
            _desc.storageRow = C.SlotRows;

            // ---- 建造占地 ----
            _desc.roughRadius = C.BaseRadius;
            _desc.roughHeight = C.Height;
            _desc.roughWidth = C.BaseRadius * 2f;
            _desc.dragBuild = false;
            _desc.blueprintBoxSize = new Vector2(C.BaseRadius * 2f, C.BaseRadius * 2f);
            _desc.buildingGizmoCenter = new Vector3(0f, C.Height * 0.5f, 0f);
            _desc.buildingGizmoExt = new Vector3(C.BaseRadius, C.Height * 0.5f, C.BaseRadius);
            _desc.barWidth = C.BaseRadius * 2f;
            _desc.barHeight = C.Height;

            // ---- 传送带接口 ----
            _desc.portPoses = BuildPortPoses(PortCount);

            // ---- 兜底：把所有为 null 的数组字段填成空数组 ----
            // 游戏里到处直接取字段 .Length（例如 BuildTool_Path.UpdateRaycast 里的
            // prefabDesc.multiLevelAlternativeIds.Length），null 就是必崩的 NRE。
            FillNullArrays(_desc);

            Plugin.Log.LogInfo("建筑 prefab 构建完成: submesh=" + HubMesh.SubmaterialCount +
                               " 接口=" + _desc.portPoses.Length +
                               " 电力节点=" + _desc.isPowerNode +
                               " 池=" + C.PoolCapacity);
            return _desc;
        }

        /// <summary>
        /// 八个传送带接口：前后各 4 个，都排在离底座边缘 2 m 间距的位置上，居中对齐。
        /// 索引顺序 = 前侧（-Z）0~3，后侧（+Z）4~7，与实体连接点编号一致。
        ///
        /// 1.2.7 重要教训：forward 必须指向建筑外侧！BuildTool_Path 里传送带起点
        /// = 接口位置 + forward × 1.1（终点接口的方向也是基于朝外 forward 推导），
        /// forward 朝内时这个起点落在建筑碰撞体内部，传送带根本连不上（1.2.6 的回归）。
        /// 「进口」由玩家铺带方式决定：带头（箭头）接到接口 = 进料（isOutput=false），
        /// 接口箭头朝外只是原版建筑的通用画法，不影响进料。
        ///
        /// 1.3.9：接口高度 0.4 → 0.2。原版 BuildTool_Path 判定地面时用的是
        /// 「地表高度 + 0.2」这个基准（planet.realRadius + 0.2f），也就是传送带带面
        /// 正好在网格底上方 0.2 m。接口给 0.4 时带子会悬空在半腰上，接起来别扭；
        /// 对齐到 0.2 之后传送带就是贴着网格底部接进来的。
        ///
        /// 1.4.0：模型底部加了凸出的接口套筒（HubMesh.AppendPortSockets，向外 0.55 m），
        /// 接口点跟着从「底座表面」挪到「套筒口外 2 cm」。这样建造时 BuildingGizmo 的接口箭头
        /// 不会被套筒遮住，带子也是从套筒口接出去，不会插进筒壁里。
        /// </summary>
        private static Pose[] BuildPortPoses(int count)
        {
            var poses = new Pose[count];
            const float spacing = 2f;                      // 传送带宽度，保证四条带并排不重叠
            int perSide = count / 2;
            float x0 = -spacing * (perSide - 1) * 0.5f;    // 居中：-3, -1, +1, +3
            float zEdge = C.BaseRadius + C.PortOut + 0.02f;

            int k = 0;
            for (int side = 0; side < 2 && k < count; side++)
            {
                float z = side == 0 ? -zEdge : zEdge;
                // 前侧朝 -Z、后侧朝 +Z，都是「朝建筑外」
                Quaternion rot = Quaternion.LookRotation(
                    side == 0 ? Vector3.back : Vector3.forward, Vector3.up);
                for (int i = 0; i < perSide && k < count; i++, k++)
                {
                    var pos = new Vector3(x0 + spacing * i, PortY, z);
                    poses[k] = new Pose(pos, rot);
                }
            }
            return poses;
        }

        /// <summary>
        /// 传送带接口的离地高度（米）。0.2 = 原版传送带带面高度（BuildTool_Path 里
        /// 「地表 + 0.2f」那个基准），传带从这里接进来正好贴地，不会悬空。
        /// </summary>
        public const float PortY = 0.2f;

        /// <summary>PrefabDesc 里所有数组字段若为 null 就填空数组，防止游戏侧 .Length 直接 NRE。</summary>
        private static void FillNullArrays(PrefabDesc desc)
        {
            try
            {
                FieldInfo[] fields = typeof(PrefabDesc).GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    Type t = f.FieldType;
                    if (!t.IsArray)
                        continue;
                    if (f.GetValue(desc) != null)
                        continue;
                    Type elem = t.GetElementType();
                    f.SetValue(desc, Array.CreateInstance(elem, 0));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("填充 PrefabDesc 空数组失败: " + e.Message);
            }
        }

        private static void TryRetryMaterials()
        {
            try
            {
                Material[] mats = HubMesh.BuildMaterials();
                if (!HubMesh.HasTemplate || mats == null || mats.Length == 0)
                    return;
                if (_desc.lodMaterials != null && _desc.lodMaterials.Length > 0)
                    _desc.lodMaterials[0] = mats;
                if (_desc.lodBlueprintMaterials != null && _desc.lodBlueprintMaterials.Length > 0)
                    _desc.lodBlueprintMaterials[0] = mats;
                _desc.materials = mats;
                if (_prefab != null)
                {
                    var mr = _prefab.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterials = mats;
                }
                Plugin.Log.LogInfo("已补上原版建筑材质（instancing 可用）");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("补材质失败: " + e.Message);
            }
        }

        private static void BuildColliders(GameObject root)
        {
            // 建造占地框：isTrigger + 同一 prefab => ColliderData.usage = Build
            var buildGo = new GameObject("collider-build");
            buildGo.transform.SetParent(root.transform, false);
            var bc = buildGo.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0f, C.Height * 0.5f, 0f);
            bc.size = new Vector3(C.BaseRadius * 2f, C.Height, C.BaseRadius * 2f);

            // 物理/拾取：非 Trigger => usage = Physics
            var physGo = new GameObject("collider-physics");
            physGo.transform.SetParent(root.transform, false);
            var pc = physGo.AddComponent<BoxCollider>();
            pc.isTrigger = false;
            pc.center = new Vector3(0f, C.Height * 0.5f, 0f);
            pc.size = new Vector3(C.BaseRadius * 1.9f, C.Height, C.BaseRadius * 1.9f);
        }
    }
}
