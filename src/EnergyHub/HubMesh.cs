using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EnergyHub
{
    /// <summary>
    /// 从嵌入的二进制资源构建建筑 Mesh（单 Mesh 多 submesh）。
    ///
    /// 外观方案（1.0.9 更新配色）：
    ///   材质基座仍是「克隆原版能量枢纽的主装甲基材」——贴图、alpha（光滑度）、
    ///   _MS_Tex、法线全部沿用原版，避免重蹈「自建 1x1 贴图把光滑度顶到 1.0 → 整栋爆白」。
    ///   1.0.9 起只额外改三项，用来复刻「近程电浆塔」（turret-plasma-ground）的配色：
    ///     _Color           反照率染色（暖白 + 淡紫）
    ///     _EmissionTex     纯色贴图（紫 / 橙红 / 黑），自发光只落在该发的 submesh 上
    ///     _EmissionMultiplier / _EmissionSwitch   自发光强度
    ///   每个 submesh 的底色仍由「常量 UV 取样原版 energy-exchanger-a 的平坦色块」决定。
    ///
    ///   之前的做法是「自己生成 1x1 纯色贴图 + 覆盖 _Color/_EmissionMultiplier」，
    ///   结果整栋发白：原版 shader 的反照率 alpha/自发光都寄宿在贴图里，
    ///   1x1 贴图（alpha=1、_EmissionTex 纯白）等于把光滑度顶到最大、把自发光遮罩全开。
    /// </summary>
    internal static class HubMesh
    {
        public const int MatHull = 0;
        public const int MatDark = 1;
        public const int MatEmissive = 2;
        /// <summary>
        /// 原「玻璃筒」槽位。现在的模型里这一层（antenna-glass，r 1.41→0.74 的锥筒）
        /// 正好套在实心塔身（antenna-dark）外面，半径只大 0.1~0.2 m：
        /// 不透明化以后它会把整根塔身遮住，远看就是一坨发光「瓶子」。
        /// 所以构建时直接丢弃这一层，塔身 + 4 根青色能量导轨就露出来了（即「取消玻璃」）。
        /// 想恢复：把 SkipMaterials 清空即可。
        /// </summary>
        public const int MatConduit = 3;
        public const int MatAccent = 4;
        /// <summary>
        /// 玻璃棱镜罩（1.5.0）。走原版 <c>VF Shaders/Forward/PBR Standard Glass</c>：
        /// 自带 Transparent 队列、SrcAlpha/OneMinusSrcAlpha 混合、ZWrite=0，
        /// 还有 <c>_RimColor</c> 菲涅尔边缘光 —— 不用自己拼透明材质。
        /// </summary>
        public const int MatGlass = 5;
        public const int MatCount = 6;

        private static readonly HashSet<int> SkipMaterials = new HashSet<int> { MatConduit };

        /// <summary>
        /// 顶端圆球段的裁切线（米）。原版能量交换站模型顶端是一个「玻璃球 + 经纬骨架」，
        /// 1.3.4 起整段丢弃（y ≥ 该值的三角形全部剔除），改成自建的「激光发射头」。
        /// 原模型 y∈[24.2, 28.08) 处半径只有 0.5~1.6 m，断口会被发射口裙边（r 0.95~1.92）完全盖住。
        /// </summary>
        private const float TrimY = 24.2f;

        private static Mesh _mesh;

        /// <summary>submesh 索引 -> 材质槽位（与 C.MatXxx 对应）。</summary>
        public static int[] SubmeshMaterial = new int[0];

        public static int SubmaterialCount
        {
            get { return SubmeshMaterial.Length; }
        }

        /// <summary>
        /// 每个材质槽在原版贴图上的取样点（常量 UV）。
        /// 坐标是用 tools/inspect/find_uv_swatch.py 在原版 energy-exchanger-a / -e 里
        /// 搜出来的「平坦色块」，并核对过同点的 -s（金属/光滑）与 -n（法线）都是中性值：
        ///   hull(0)      a=0.55 中灰    —— 底座踏板 / 中段环箍（用暖白染色提亮）
        ///   dark(1)      a=0.80 亮白    —— 塔身主装甲（占比最大，用淡紫染色）
        ///   emissive(2)  a=0.22 暗底    —— 导轨 / 圆环 / 球顶，靠自发光上色
        /// 这些点的 -a alpha 都是 0.098，与原版绝大多数像素一致（原版 alpha 中位数就是 0.098），
        /// 光滑度不会像自建 1x1 贴图那样被顶到 1.0。
        /// </summary>
        private static readonly Vector2[] Swatches =
        {
            new Vector2(0.2532f, 0.9480f), // 0 hull     中灰（暖白压条 / 底座踏板）
            new Vector2(0.9270f, 0.9968f), // 1 dark     亮白（淡紫主装甲，塔身占比最大）
            new Vector2(0.7161f, 0.8914f), // 2 emissive 暗底（靠自发光上色）
            new Vector2(0.9036f, 0.8718f), // 3 conduit  暗底 + 微弱青光（当前模型已丢弃该层）
            new Vector2(0.9270f, 0.9968f), // 4 accent（当前模型没用到）
            new Vector2(0.9270f, 0.9968f), // 5 glass（玻璃 shader 没有 _MainTex，UV 其实不参与取样）
        };

        /// <summary>
        /// 每个槽位的反照率染色（乘在原版贴图色块上）。
        /// 取自「近程电浆塔」（turret-plasma-ground，材质 _Color / _TintColor / _EmColor1~4）：
        ///   _TintColor   (1.000, 0.951, 0.909) 暖白
        ///   _EmColor2    (0.682, 0.663, 0.878) 淡紫
        ///   _EmColor1    (1.000, 0.363, 0.297) 橙红
        ///   _EmColor3/4  (0.753, 0.565, 0.992) 紫
        /// 结论：机身是「暖白 + 淡紫」的浅色装甲，紫色自发光做点缀，橙色只用于少量警示灯。
        /// </summary>
        private static readonly Color[] Tints =
        {
            new Color(0.98f, 0.93f, 0.86f), // 0 hull     暖白压条
            new Color(0.82f, 0.79f, 0.93f), // 1 dark     淡紫装甲
            new Color(0.26f, 0.18f, 0.42f), // 2 emissive 深紫底
            new Color(0.55f, 0.30f, 0.22f), // 3 conduit  暗琥珀底
            new Color(1.00f, 0.36f, 0.30f), // 4 accent   橙红
            // 5 glass：淡紫罗兰，alpha 就是玻璃的不透明度（原版 energy-exchanger-glass 是 0.161）
            new Color(0.58f, 0.62f, 0.95f, 0.19f),
        };

        /// <summary>自发光颜色（写进 _EmissionTex 的纯色贴图；白色 _EmissionColor 只是倍率）。</summary>
        private static readonly Color[] Glows =
        {
            Color.black,
            Color.black,
            new Color(0.753f, 0.565f, 0.992f), // 紫（电浆塔 _EmColor3/4）
            new Color(1.000f, 0.363f, 0.297f), // 橙红（_EmColor1）
            new Color(1.000f, 0.363f, 0.297f),
            new Color(0.62f, 0.50f, 1.00f),    // 5 glass：玻璃自身的微光（不能让整片玻璃发亮）
        };

        private static readonly float[] GlowMul = { 0f, 0f, 1.8f, 2.0f, 2.0f, 0.55f };

        private static Vector2 Swatch(int matSlot)
        {
            return Swatches[matSlot >= 0 && matSlot < Swatches.Length ? matSlot : 0];
        }

        /// <summary>一个待提交的几何缓冲（同一材质槽的顶点/法线/三角形）。</summary>
        private class Part
        {
            public readonly List<Vector3> V = new List<Vector3>(256);
            public readonly List<Vector3> N = new List<Vector3>(256);
            public readonly List<int> T = new List<int>(512);
        }

        public static Mesh Get()
        {
            if (_mesh != null)
                return _mesh;
            _mesh = BuildPrism();
            return _mesh;
        }

        /// <summary>
        /// 六棱玻璃棱镜塔。
        ///
        /// 造型（自下而上）：六棱裙座 → 座台 → **主晶**（双锥：1.6~3.4 微收、3.4~6.6 外扩到 3.85、
        /// 6.6~11.0 收到 2.50）→ 金属腰环 → **中晶**（绕 Y 转 15° 错位，11.7~18.6）→ 腰环 →
        /// **冠晶**（19.25~26.6，收到 0.90）→ 托座 → 托碗 → 能量球（球心 29.55 r 2.15）。
        /// 玻璃罩内贯穿一根六棱发光柱（1.6~26.5），透过半透明玻璃能看到，这是「科幻感」的主体。
        ///
        /// 为什么用 6 棱而不是 8 棱：6 棱的棱角更锐，远处也能读出「水晶切割面」；
        /// 而且顶点角取 0/60/…/300 时，-Z（270°）正好落在**平面中心**，
        /// 4 个传送带接口套筒（-Z 侧）贴在平面上，不会骑在棱上。
        ///
        /// 三段之间做 0°/15°/0° 的旋转错位，避免「叠灯笼」的重复感（1.5.0 试过 5 段等长，很难看）。
        /// </summary>
        private static Mesh BuildPrism()
        {
            var mesh = new Mesh { name = "energy-hub-prism" };

            var verts = new List<Vector3>(8192);
            var norms = new List<Vector3>(8192);
            var uvs = new List<Vector2>(8192);
            var tris = new List<int[]>(8);
            var sub = new List<int>(8);

            var hull = new Part();    // 暖白装甲
            var dark = new Part();    // 淡紫结构件 / 棱骨
            var emis = new Part();    // 自发光（光柱 / 球）
            var glass = new Part();   // 玻璃棱镜罩

            // ---- 底座：六棱裙座 + 座台 ----
            PrismAt(hull, 0f, 0.60f, 4.30f, 4.18f, 6, 0f, true, false);
            PrismAt(dark, 0.60f, 1.60f, 4.18f, 3.55f, 6, 0f, false, true);

            // ---- 主晶（0°）----
            PrismAt(glass, 1.60f, 3.40f, 3.30f, 3.05f, 6, 0f, false, false);
            PrismAt(glass, 3.40f, 6.60f, 3.05f, 3.85f, 6, 0f, false, false);
            PrismAt(glass, 6.60f, 11.00f, 3.85f, 2.50f, 6, 0f, false, false);
            // 腰环 1
            PrismAt(hull, 11.00f, 11.70f, 2.50f, 2.82f, 6, 0f, false, true);

            // ---- 中晶（错位 15°）----
            PrismAt(glass, 11.70f, 14.20f, 2.82f, 3.10f, 6, 15f, false, false);
            PrismAt(glass, 14.20f, 18.60f, 3.10f, 1.90f, 6, 15f, false, false);
            // 腰环 2
            PrismAt(hull, 18.60f, 19.25f, 1.90f, 2.18f, 6, 15f, false, true);

            // ---- 冠晶（回到 0°）----
            PrismAt(glass, 19.25f, 21.60f, 2.18f, 2.46f, 6, 0f, false, false);
            PrismAt(glass, 21.60f, 24.60f, 2.46f, 1.60f, 6, 0f, false, false);
            // 颈环 + 冠尖
            PrismAt(hull, 24.60f, 25.30f, 1.60f, 1.30f, 6, 0f, false, true);
            PrismAt(glass, 25.30f, 26.60f, 1.30f, 0.90f, 6, 0f, false, false);

            // ---- 顶部托座 / 托碗 / 中心核 ----
            PrismAt(hull, 26.60f, 27.05f, 0.90f, 1.55f, 6, 0f, false, false);
            PrismAt(dark, 27.05f, 28.35f, 1.55f, 2.05f, 6, 0f, false, false);
            // 1.6.0：原来的单颗大球（r2.15）拆成「中心核 + 三颗公转能量球」。
            // 三颗球是独立 GameObject（要各自转、各自公转），只能放 HubEffect 里；
            // 模型层这里只留一颗 0.62 的小核 + 托碗口沿的细环，断电时塔顶也不会变成一个空洞。
            DiscRing(hull, 28.42f, 1.86f, 2.12f, 0.14f, 24);
            Ball(emis, new Vector3(0f, 29.80f, 0f), 0.86f, 20, 30);
            // 中轴：把中心核和托碗连起来，三颗球绕它公转
            TaperAt(emis, Vector3.zero, 28.15f, 29.20f, 0.24f, 0.18f, 8, false, true);

            // ---- 内部发光柱（透过玻璃能看到）----
            PrismAt(emis, 1.60f, 26.50f, 0.55f, 0.30f, 6, 0f, false, true);

            // ---- 玻璃段棱骨：每段 6 条，贴棱线外侧 1.2% ----
            PrismStruts(dark, 1.60f, 3.40f, 3.30f, 3.05f, 0f);
            PrismStruts(dark, 3.40f, 6.60f, 3.05f, 3.85f, 0f);
            PrismStruts(dark, 6.60f, 11.00f, 3.85f, 2.50f, 0f);
            PrismStruts(dark, 11.70f, 14.20f, 2.82f, 3.10f, 15f);
            PrismStruts(dark, 14.20f, 18.60f, 3.10f, 1.90f, 15f);
            PrismStruts(dark, 19.25f, 21.60f, 2.18f, 2.46f, 0f);
            PrismStruts(dark, 21.60f, 24.60f, 2.46f, 1.60f, 0f);
            PrismStruts(dark, 25.30f, 26.60f, 1.30f, 0.90f, 0f);

            // ---- 双锥最粗处的细箍（比腰环薄，只是给棱镜一道「切面线」）----
            PrismAt(hull, 6.55f, 6.72f, 3.89f, 3.89f, 6, 0f, false, false);
            PrismAt(hull, 14.15f, 14.32f, 3.14f, 3.14f, 6, 15f, false, false);
            PrismAt(hull, 21.55f, 21.72f, 2.50f, 2.50f, 6, 0f, false, false);

            // ---- 底部 4 个传送带接口套筒（-Z 侧，位置与 portPoses 对齐）----
            AppendPortSockets(verts, norms, uvs, tris, sub);

            // 提交顺序有讲究：玻璃最后 → 它的 submesh 排在最后，配合 Transparent 队列
            // 保证「先画内部光柱和骨架，再叠玻璃」。
            Commit(hull, MatHull, verts, norms, uvs, tris, sub);
            Commit(dark, MatDark, verts, norms, uvs, tris, sub);
            Commit(emis, MatEmissive, verts, norms, uvs, tris, sub);
            Commit(glass, MatGlass, verts, norms, uvs, tris, sub);

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++)
                mesh.SetTriangles(tris[i], i, false);
            mesh.RecalculateBounds();
            SubmeshMaterial = sub.ToArray();

            int triCount = 0;
            for (int i = 0; i < tris.Count; i++)
                triCount += tris[i].Length / 3;

            Plugin.Log.LogInfo("棱镜塔已构建: submesh=" + tris.Count +
                               " 顶点=" + verts.Count + " 三角=" + triCount +
                               "（玻璃 " + glass.V.Count + " / 壳体 " + hull.V.Count +
                               " / 结构 " + dark.V.Count + " / 发光 " + emis.V.Count + "）" +
                               " 高=" + C.Height + " 中心核 y=29.80 r=0.86（三颗公转球由特效层提供）");
            return mesh;
        }

        /// <summary>六棱台侧面（rotDeg 让每段可以绕 Y 错位）。</summary>
        private static void PrismAt(Part p, float y0, float y1, float r0, float r1,
            int seg, float rotDeg, bool capBottom, bool capTop)
        {
            float rot = rotDeg * Mathf.Deg2Rad;
            float dr = (r1 - r0) / Mathf.Max(0.0001f, y1 - y0);
            for (int i = 0; i < seg; i++)
            {
                float t0 = rot + Mathf.PI * 2f * i / seg;
                float t1 = rot + Mathf.PI * 2f * (i + 1) / seg;
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0);
                float c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                Vector3 a = new Vector3(c0 * r0, y0, s0 * r0);
                Vector3 b = new Vector3(c0 * r1, y1, s0 * r1);
                Vector3 c = new Vector3(c1 * r1, y1, s1 * r1);
                Vector3 d = new Vector3(c1 * r0, y0, s1 * r0);
                Vector3 n0 = new Vector3(c0, -dr, s0).normalized;
                Vector3 n1 = new Vector3(c1, -dr, s1).normalized;
                AddQuad(p, a, b, c, d, n0, n0, n1, n1, (n0 + n1) * 0.5f);
            }
            if (capBottom) PrismCap(p, y0, r0, seg, rot, -1f);
            if (capTop) PrismCap(p, y1, r1, seg, rot, 1f);
        }

        /// <summary>多边形端盖（扇形三角化）。</summary>
        private static void PrismCap(Part p, float y, float r, int seg, float rot, float sign)
        {
            Vector3 ctr = new Vector3(0f, y, 0f);
            var nrm = new Vector3(0f, sign, 0f);
            for (int i = 0; i < seg; i++)
            {
                float t0 = rot + Mathf.PI * 2f * i / seg;
                float t1 = rot + Mathf.PI * 2f * (i + 1) / seg;
                Vector3 a = new Vector3(Mathf.Cos(t0) * r, y, Mathf.Sin(t0) * r);
                Vector3 b = new Vector3(Mathf.Cos(t1) * r, y, Mathf.Sin(t1) * r);
                AddTri(p, ctr, a, b, nrm, nrm, nrm, nrm);
            }
        }

        /// <summary>沿一段棱线建 6 根细柱（玻璃罩的金属骨架）。</summary>
        private static void PrismStruts(Part p, float y0, float y1, float r0, float r1, float rotDeg)
        {
            float rot = rotDeg * Mathf.Deg2Rad;
            for (int i = 0; i < 6; i++)
            {
                float a = rot + Mathf.PI * 2f * i / 6f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                Strut(p,
                    new Vector3(c * r0 * 1.012f, y0, s * r0 * 1.012f),
                    new Vector3(c * r1 * 1.012f, y1, s * r1 * 1.012f),
                    0.055f, 4);
            }
        }

        /// <summary>两点之间的方柱（无端盖）。</summary>
        private static void Strut(Part p, Vector3 a, Vector3 b, float r, int seg)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-8f)
                return;
            d = d.normalized;
            Vector3 up = Mathf.Abs(d.y) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(d, up).normalized;
            Vector3 v = Vector3.Cross(d, u);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2f * i / seg + Mathf.PI / seg;
                float t1 = Mathf.PI * 2f * (i + 1) / seg + Mathf.PI / seg;
                Vector3 o0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0);
                Vector3 o1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);
                AddQuad(p, a + o0 * r, b + o0 * r, b + o1 * r, a + o1 * r,
                    o0, o0, o1, o1, (o0 + o1) * 0.5f);
            }
        }

        private static void AppendPortSockets(List<Vector3> verts, List<Vector3> norms,
            List<Vector2> uvs, List<int[]> tris, List<int> subMat)
        {
            var body = new Part();   // 套筒主体（淡紫结构色）
            var lip = new Part();    // 口沿（暖白装甲色）
            var emis = new Part();   // 孔口灯带

            const float spacing = 2f;                 // 与 HubPrefab.BuildPortPoses 一致
            const float outW = C.PortHalfW;           // 0.97
            const float inW = C.PortInnerHalfW;       // 0.83
            const float yTop = C.PortHeight;          // 0.94
            const float yInTop = C.PortInnerTop;      // 0.80
            const float yInBot = 0.14f;               // 孔底（正好托住传送带带面 0.2）

            float zInAbs = C.BaseRadius - 0.60f;      // 后端埋进壳体（绝对值）
            float zOutAbs = C.BaseRadius + C.PortOut; // 前端（凸出后的端面，绝对值）

            float sideCx = (outW + inW) * 0.5f;       // 侧壁中心离接口中心的距离
            float sideHx = (outW - inW) * 0.5f;       // 侧壁半厚
            float capCy = (yInTop + yTop) * 0.5f;
            float capHy = (yTop - yInTop) * 0.5f;
            float botCy = yInBot * 0.5f;

            // 1.10.0：前后各 4 个（共 8 个）。side=0 为前侧（-Z），side=1 为后侧（+Z），
            // 几何完全镜像，只有 z 的符号不同。
            int perSide = HubPrefab.PortsPerSide;
            for (int side = 0; side < 2; side++)
            {
                float zs = side == 0 ? -1f : 1f;
                float zIn = zInAbs * zs;
                float zOut = zOutAbs * zs;
                float zc = (zIn + zOut) * 0.5f;
                float zh = Mathf.Abs(zOut - zIn) * 0.5f;

                for (int i = 0; i < perSide; i++)
                {
                    float cx = -spacing * (perSide - 1) * 0.5f + spacing * i;   // -3 / -1 / +1 / +3

                    // ---- 套筒主体：左右两侧壁 + 顶壁 + 底壁 ----
                    Box(body, new Vector3(cx - sideCx, (yInBot + yTop) * 0.5f, zc),
                        new Vector3(sideHx, (yTop - yInBot) * 0.5f, zh));
                    Box(body, new Vector3(cx + sideCx, (yInBot + yTop) * 0.5f, zc),
                        new Vector3(sideHx, (yTop - yInBot) * 0.5f, zh));
                    Box(body, new Vector3(cx, capCy, zc), new Vector3(outW, capHy, zh));
                    Box(body, new Vector3(cx, botCy, zc), new Vector3(outW, yInBot * 0.5f, zh));

                    // ---- 口沿：端面外再贴一圈 6 cm 厚的框（比套筒大 3 cm） ----
                    float lipZ = zOut + zs * 0.03f;
                    const float lipHz = 0.03f;
                    Box(lip, new Vector3(cx - (outW + 0.03f + inW) * 0.5f,
                            (yInBot + yTop) * 0.5f, lipZ),
                        new Vector3((outW + 0.03f - inW) * 0.5f, (yTop - yInBot) * 0.5f, lipHz));
                    Box(lip, new Vector3(cx + (outW + 0.03f + inW) * 0.5f,
                            (yInBot + yTop) * 0.5f, lipZ),
                        new Vector3((outW + 0.03f - inW) * 0.5f, (yTop - yInBot) * 0.5f, lipHz));
                    Box(lip, new Vector3(cx, (yInTop + yTop + 0.03f) * 0.5f - 0.015f, lipZ),
                        new Vector3(outW + 0.03f, (yTop + 0.03f - yInTop) * 0.5f, lipHz));
                    Box(lip, new Vector3(cx, (yInBot - 0.03f) * 0.5f, lipZ),
                        new Vector3(outW + 0.03f, (yInBot + 0.03f) * 0.5f, lipHz));

                    // ---- 孔口灯带：横在内孔顶部，朝外可见 ----
                    Box(emis, new Vector3(cx, yInTop - 0.035f, zOut + zs * 0.02f),
                        new Vector3(inW - 0.06f, 0.022f, 0.012f));
                }
            }

            Commit(body, MatDark, verts, norms, uvs, tris, subMat);
            Commit(lip, MatHull, verts, norms, uvs, tris, subMat);
            Commit(emis, MatEmissive, verts, norms, uvs, tris, subMat);

            Plugin.Log.LogInfo("底部接口套筒已构建: " + (perSide * 2) + " 个（前后各 " + perSide +
                               "），外凸 " + C.PortOut.ToString("0.00") +
                               " m，内孔 " + (inW * 2f).ToString("0.00") + "×" +
                               (yInTop - yInBot).ToString("0.00") + " m" +
                               "（顶点 主体" + body.V.Count + "/口沿" + lip.V.Count +
                               "/灯带" + emis.V.Count + "）");
        }

        /// <summary>轴对齐长方体（6 个面，法线按轴给，绕序由 AddQuad 自动纠正）。</summary>
        private static void Box(Part p, Vector3 c, Vector3 h)
        {
            var nx = new Vector3(-1f, 0f, 0f);
            var px = new Vector3(1f, 0f, 0f);
            var ny = new Vector3(0f, -1f, 0f);
            var py = new Vector3(0f, 1f, 0f);
            var nz = new Vector3(0f, 0f, -1f);
            var pz = new Vector3(0f, 0f, 1f);
            float x0 = c.x - h.x, x1 = c.x + h.x;
            float y0 = c.y - h.y, y1 = c.y + h.y;
            float z0 = c.z - h.z, z1 = c.z + h.z;

            // -X / +X
            AddQuad(p, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1),
                       new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), nx, nx, nx, nx, nx);
            AddQuad(p, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1),
                       new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), px, px, px, px, px);
            // -Y / +Y
            AddQuad(p, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                       new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), ny, ny, ny, ny, ny);
            AddQuad(p, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0),
                       new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), py, py, py, py, py);
            // -Z / +Z
            AddQuad(p, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                       new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), nz, nz, nz, nz, nz);
            AddQuad(p, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
                       new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), pz, pz, pz, pz, pz);
        }

        /// <summary>UV 球（法线朝外）。</summary>
        private static void Ball(Part p, Vector3 c, float r, int rings, int seg)
        {
            for (int i = 0; i < rings; i++)
            {
                float p0 = Mathf.PI * i / rings;
                float p1 = Mathf.PI * (i + 1) / rings;
                float y0 = Mathf.Cos(p0), r0 = Mathf.Sin(p0);
                float y1 = Mathf.Cos(p1), r1 = Mathf.Sin(p1);
                for (int j = 0; j < seg; j++)
                {
                    float t0 = Mathf.PI * 2f * j / seg;
                    float t1 = Mathf.PI * 2f * (j + 1) / seg;
                    float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0);
                    float c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);

                    Vector3 a = new Vector3(c0 * r0, y0, s0 * r0);
                    Vector3 b = new Vector3(c0 * r1, y1, s0 * r1);
                    Vector3 cc = new Vector3(c1 * r1, y1, s1 * r1);
                    Vector3 d = new Vector3(c1 * r0, y0, s1 * r0);
                    // 极点处四边形退化，用三角形也可以，这里靠 want 纠正绕序
                    Vector3 want = (a + b + cc + d) * 0.25f;
                    AddQuad(p, c + a * r, c + b * r, c + cc * r, c + d * r,
                        a, b, cc, d, want);
                }
            }
        }

        /// <summary>水平环盘（上下两面 + 外侧壁），像炮管上的散热片。</summary>
        private static void DiscRing(Part p, float y, float rIn, float rOut, float thick, int seg)
        {
            var up = new Vector3(0f, 1f, 0f);
            var dn = new Vector3(0f, -1f, 0f);
            float yt = y + thick * 0.5f, yb = y - thick * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2f * i / seg;
                float t1 = Mathf.PI * 2f * (i + 1) / seg;
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0);
                float c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                // 上表面
                AddQuad(p,
                    new Vector3(c0 * rIn, yt, s0 * rIn), new Vector3(c0 * rOut, yt, s0 * rOut),
                    new Vector3(c1 * rOut, yt, s1 * rOut), new Vector3(c1 * rIn, yt, s1 * rIn),
                    up, up, up, up, up);
                // 下表面
                AddQuad(p,
                    new Vector3(c0 * rIn, yb, s0 * rIn), new Vector3(c0 * rOut, yb, s0 * rOut),
                    new Vector3(c1 * rOut, yb, s1 * rOut), new Vector3(c1 * rIn, yb, s1 * rIn),
                    dn, dn, dn, dn, dn);
                // 外侧壁
                var n0 = new Vector3(c0, 0f, s0);
                var n1 = new Vector3(c1, 0f, s1);
                AddQuad(p,
                    new Vector3(c0 * rOut, yb, s0 * rOut), new Vector3(c0 * rOut, yt, s0 * rOut),
                    new Vector3(c1 * rOut, yt, s1 * rOut), new Vector3(c1 * rOut, yb, s1 * rOut),
                    n0, n0, n1, n1, (n0 + n1) * 0.5f);
            }
        }

        /// <summary>把一个 Part 追加进全局 mesh 缓冲；同材质槽的三角形合并到同一个 submesh。</summary>
        private static void Commit(Part p, int matSlot, List<Vector3> verts, List<Vector3> norms,
            List<Vector2> uvs, List<int[]> tris, List<int> subMat)
        {
            if (p.V.Count == 0)
                return;
            int baseIndex = verts.Count;
            verts.AddRange(p.V);
            norms.AddRange(p.N);
            Vector2 uv = Swatch(matSlot);
            for (int i = 0; i < p.V.Count; i++) uvs.Add(uv);

            int slot = -1;
            for (int i = 0; i < subMat.Count; i++)
                if (subMat[i] == matSlot) { slot = i; break; }

            int[] dst;
            if (slot >= 0)
            {
                int[] old = tris[slot];
                dst = new int[old.Length + p.T.Count];
                Array.Copy(old, dst, old.Length);
                for (int i = 0; i < p.T.Count; i++) dst[old.Length + i] = p.T[i] + baseIndex;
            }
            else
            {
                slot = tris.Count;
                dst = new int[p.T.Count];
                for (int i = 0; i < p.T.Count; i++) dst[i] = p.T[i] + baseIndex;
                subMat.Add(matSlot);
                tris.Add(null);
            }
            tris[slot] = dst;
        }

        private static void TaperAt(Part p, Vector3 off, float y0, float y1, float r0, float r1,
            int seg, bool capBottom, bool capTop)
        {
            float dr = (r1 - r0) / Mathf.Max(0.0001f, y1 - y0);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2f * i / seg;
                float t1 = Mathf.PI * 2f * (i + 1) / seg;
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0);
                float c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                Vector3 a = off + new Vector3(c0 * r0, y0, s0 * r0);
                Vector3 b = off + new Vector3(c0 * r1, y1, s0 * r1);
                Vector3 c = off + new Vector3(c1 * r1, y1, s1 * r1);
                Vector3 d = off + new Vector3(c1 * r0, y0, s1 * r0);
                Vector3 n0 = new Vector3(c0, -dr, s0).normalized;
                Vector3 n1 = new Vector3(c1, -dr, s1).normalized;
                AddQuad(p, a, b, c, d, n0, n0, n1, n1, (n0 + n1) * 0.5f);
            }
            if (capBottom) Cap(p, off, y0, r0, seg, -1f);
            if (capTop) Cap(p, off, y1, r1, seg, 1f);
        }

        private static void Cap(Part p, Vector3 off, float y, float r, int seg, float sign)
        {
            Vector3 ctr = off + new Vector3(0f, y, 0f);
            var nrm = new Vector3(0f, sign, 0f);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2f * i / seg;
                float t1 = Mathf.PI * 2f * (i + 1) / seg;
                Vector3 a = off + new Vector3(Mathf.Cos(t0) * r, y, Mathf.Sin(t0) * r);
                Vector3 b = off + new Vector3(Mathf.Cos(t1) * r, y, Mathf.Sin(t1) * r);
                AddTri(p, ctr, a, b, nrm, nrm, nrm, nrm);
            }
        }

        /// <summary>加一个四边形；绕序按期望法线自动纠正（Unity 正面 = Cross(b-a, c-a) 指向观察者）。</summary>
        private static void AddQuad(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector3 want)
        {
            int i0 = p.V.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0f)
            {
                p.V.Add(a); p.V.Add(d); p.V.Add(c); p.V.Add(b);
                p.N.Add(na); p.N.Add(nd); p.N.Add(nc); p.N.Add(nb);
            }
            else
            {
                p.V.Add(a); p.V.Add(b); p.V.Add(c); p.V.Add(d);
                p.N.Add(na); p.N.Add(nb); p.N.Add(nc); p.N.Add(nd);
            }
            p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2);
            p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 3);
        }

        private static void AddTri(Part p, Vector3 a, Vector3 b, Vector3 c,
            Vector3 na, Vector3 nb, Vector3 nc, Vector3 want)
        {
            int i0 = p.V.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0f)
            {
                p.V.Add(a); p.V.Add(c); p.V.Add(b);
                p.N.Add(na); p.N.Add(nc); p.N.Add(nb);
            }
            else
            {
                p.V.Add(a); p.V.Add(b); p.V.Add(c);
                p.N.Add(na); p.N.Add(nb); p.N.Add(nc);
            }
            p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2);
        }

        // ---------------- 材质 ----------------

        /// <summary>返回与 submesh 数量等长的材质数组。</summary>
        public static Material[] BuildMaterials()
        {
            Get();
            var src = TemplateMaterials();
            HasTemplate = src.Length > 0;
            if (!HasTemplate)
                Plugin.Log.LogError("找不到可用的原版建筑材质模板，将退化为无实例化材质（建筑可能不显示）");

            Material baseMat = PickBase(src);
            Material glassMat = PickGlass(src);
            if (baseMat != null)
                Plugin.Log.LogInfo("材质模板: " + baseMat.name +
                                   " shader=" + (baseMat.shader != null ? baseMat.shader.name : "null") +
                                   " / 玻璃模板: " + (glassMat != null ? glassMat.name : "未找到，按 shader 兜底"));

            int n = SubmeshMaterial.Length;
            var mats = new Material[n];
            for (int i = 0; i < n; i++)
                mats[i] = CreateFor(SubmeshMaterial[i] == MatGlass && glassMat != null ? glassMat : baseMat,
                                    SubmeshMaterial[i]);

            var sb = new StringBuilder("[配色] 参考近程电浆塔(turret-plasma-ground):");
            for (int i = 0; i < n; i++)
            {
                int slot = SubmeshMaterial[i];
                sb.Append(" #").Append(i).Append("(slot").Append(slot).Append(") 染色=")
                  .Append(Tints[Mathf.Clamp(slot, 0, Tints.Length - 1)]).Append(" 自发光=")
                  .Append(Glows[Mathf.Clamp(slot, 0, Glows.Length - 1)]).Append("×")
                  .Append(GlowMul[Mathf.Clamp(slot, 0, GlowMul.Length - 1)]);
            }
            Plugin.Log.LogInfo(sb.ToString());
            return mats;
        }

        /// <summary>上一次构建材质时是否成功取到原版建筑材质（false 表示要重试）。</summary>
        public static bool HasTemplate { get; private set; }

        /// <summary>
        /// 从游戏现有建筑偷材质。必须用原版建筑材质：DSP 的建筑 shader 会从
        /// GPU 实例缓冲 _InstBuffer 里读每栋建筑的位移/旋转，用自己的 shader 会全部堆在原点。
        /// </summary>
        private static Material[] TemplateMaterials()
        {
            // 1) 优先用原版能量枢纽（同样走 PowerExchanger 通路，材质语义最接近）
            Material[] fromProto = ScanProtoMaterials(true);
            if (fromProto.Length > 0)
                return fromProto;

            // 2) 任意原版建筑
            fromProto = ScanProtoMaterials(false);
            if (fromProto.Length > 0)
                return fromProto;

            // 3) 兜底：直接加载原版枢纽 prefab，读它的 LODModelDesc
            return MaterialsFromPrefab();
        }

        private static Material[] ScanProtoMaterials(bool onlyExchanger)
        {
            if (LDB.items == null || LDB.items.dataArray == null)
                return new Material[0];
            foreach (var p in LDB.items.dataArray)
            {
                if (p == null || p.ID == C.ItemId || p.prefabDesc == null)
                    continue;
                if (onlyExchanger && !p.prefabDesc.isPowerExchanger)
                    continue;
                var lm = p.prefabDesc.lodMaterials;
                if (lm != null && lm.Length > 0 && lm[0] != null && lm[0].Length > 0)
                    return lm[0];
            }
            return new Material[0];
        }

        /// <summary>
        /// 直接 Resources.Load 原版枢纽 prefab 取材质。
        /// 走这条路不依赖 VFPreload 的顺序——我们的 prefab 可能在原版模型 Preload 之前就要建好。
        /// </summary>
        private static Material[] MaterialsFromPrefab()
        {
            try
            {
                var go = Resources.Load<GameObject>(HubPrefab.FallbackPrefabPath);
                if (go == null)
                    return new Material[0];
                var lmd = go.GetComponentInChildren<LODModelDesc>(true);
                if (lmd == null || lmd.lodMeshDatas == null || lmd.lodMeshDatas.Length == 0)
                    return new Material[0];
                Material[] mats = lmd.lodMeshDatas[0].materials;
                if (mats == null || mats.Length == 0)
                    return new Material[0];
                Plugin.Log.LogInfo("材质模板取自 prefab: " + HubPrefab.FallbackPrefabPath + "，共 " + mats.Length + " 个");
                return mats;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("从 prefab 取材质失败: " + e.Message);
                return new Material[0];
            }
        }

        /// <summary>
        /// 在模板材质里挑「主装甲」那一支。
        /// 原版枢纽的 LOD0 材质表里还混着 glass / effect / black / ruins 变体，
        /// 拿错就会得到透明或全功率发光的材质。
        /// </summary>
        /// <summary>
        /// 从原版建筑的材质列表里挑玻璃材质（名字带 glass）。
        /// 目的是拿到 shader = <c>PBR Standard Glass</c> 的那份基材：
        /// 它已经配好了 Transparent 队列、SrcAlpha 混合、ZWrite=0 和 _RimColor 边缘光。
        /// 挑不到就返回 null，CreateFor 里会按 shader 名兜底新建。
        /// </summary>
        private static Material PickGlass(Material[] src)
        {
            if (src == null)
                return null;
            for (int i = 0; i < src.Length; i++)
            {
                Material m = src[i];
                if (m == null)
                    continue;
                string n = (m.name ?? string.Empty).ToLowerInvariant();
                if (n.Contains("glass") && !n.Contains("black"))
                    return m;
            }
            return null;
        }

        private static Material PickBase(Material[] src)
        {
            if (src == null || src.Length == 0)
                return null;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    Material m = src[i];
                    if (m == null)
                        continue;
                    string n = (m.name ?? string.Empty).ToLowerInvariant();
                    bool bad = n.Contains("glass") || n.Contains("effect") || n.Contains("black") ||
                               n.Contains("ruins") || n.Contains("wreck") || n.Contains("mcl");
                    if (pass == 0)
                    {
                        if (!bad && n.Contains("exchanger"))
                            return m;
                    }
                    else if (pass == 1)
                    {
                        if (!bad)
                            return m;
                    }
                    else
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        private static Material CreateFor(Material baseMat, int matSlot)
        {
            int slot = Mathf.Clamp(matSlot, 0, Tints.Length - 1);
            Material m;
            if (baseMat != null)
            {
                m = new Material(baseMat);
            }
            else
            {
                var sh = Shader.Find("VF Shaders/Forward/PBR Standard");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh != null ? sh : Shader.Find("Diffuse"));
            }
            m.name = "energy-hub-mat-" + matSlot;
            m.enableInstancing = true;

            // 1.2.5 关键修复：原版能量交换站用的是专用 shader 变体
            // 「VF Shaders/Forward/PBR Energy Exchanger」，它会从 GPU 实例缓冲里读
            // 本建筑的「工作状态」来调制自发光（原版交换站的能量窗就是这么亮的）。
            // 对我们的建筑后果就是：待机时自发光被压成黑色（顶端圆球发黑）、
            // 工作状态变化时颜色忽明忽暗（外观颜色不稳定）。
            // 换成所有普通原版建筑共用的「VF Shaders/Forward/PBR Standard」，
            // 自发光 = _EmissionTex × _EmissionColor × _EmissionMultiplier 恒定不变。
            Shader cur = m.shader;
            if (slot != MatGlass && cur != null && cur.name.Contains("Energy Exchanger"))
            {
                Shader std = Shader.Find("VF Shaders/Forward/PBR Standard") ??
                             Shader.Find("VF Shaders/Forward/PBR Standard (Instanced)");
                if (std != null)
                {
                    m.shader = std;
                    if (!_shaderSwapped)
                    {
                        _shaderSwapped = true;
                        Plugin.Log.LogInfo("材质 shader 已从 " + cur.name + " 切换为 " + std.name +
                                           "（去掉按工作状态调制的自发光，配色恒定）");
                    }
                }
            }

            // 1.5.0 玻璃槽：必须用 PBR Standard Glass（自带 Transparent 队列 + 边缘光）。
            // 基材没取到原版 glass 材质时，这里兜底把 shader 换过去。
            if (slot == MatGlass && (m.shader == null || !m.shader.name.Contains("Glass")))
            {
                Shader gl = Shader.Find("VF Shaders/Forward/PBR Standard Glass");
                if (gl != null) m.shader = gl;
            }
            if (slot == MatGlass)
            {
                // 边缘光：玻璃棱镜的「棱」就是靠菲涅尔亮边读出来的
                if (m.HasProperty("_RimColor"))
                    m.SetColor("_RimColor", new Color(0.74f, 0.64f, 1.00f, 1f));
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
                if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 2f);   // 背面剔除，别看到内壁
                if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = 3000;
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
            }

            // 唯一的改动：本建筑没有 VertaBuffer（顶点动画缓冲），
            // 关掉顶点动画分支，避免 shader 去读一个没绑定的动画缓冲。
            // 其余贴图（_MainTex/_MS_Tex/_NormalTex）全部保持原版——
            // 反照率的 alpha 通道就是光滑度，动它就会变成镜面白。
            if (m.HasProperty("_ToggleVerta"))
                m.SetFloat("_ToggleVerta", 0f);

            // 1.3.9 关键修复：上面那些只是「已知」的调制开关，原版 energy-exchanger 材质
            // 还有别的自发光调制项（名字不一定叫 _EmissionUsePower）。它们会跟着建筑的
            // 工作状态（animPool.state，卡槽里有料、开始传能后就会 0/100 来回跳）调制自发光，
            // 表现就是「放入消耗品之后建筑模型一闪一闪」。
            // 这里把所有 _Emission* 的浮点属性统一清零，只留下我们自己要设的三个，
            // 自发光就变成纯静态 = 配色恒定。
            ClearEmissionModulators(m, slot);

            // 配色：只改 _Color（反照率染色）与自发光三件套。
            // 自发光贴图换成纯色贴图（不是纯白！），遮罩外的区域不会发光。
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", Tints[slot]);
            float mul = GlowMul[slot];
            if (m.HasProperty("_EmissionSwitch"))
                m.SetFloat("_EmissionSwitch", mul > 0f ? 1f : 0f);
            if (m.HasProperty("_EmissionMultiplier"))
                m.SetFloat("_EmissionMultiplier", mul > 0f ? mul : 0f);
            if (m.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", Color.white);
            // 1.2.7：PBR Standard 这类 Unity 系 shader 需要 _EMISSION 关键字才会渲染自发光
            // （原版交换站专用 shader 不需要）。不打开关键字，自发光完全不生效，
            // 球顶只剩深紫反照率 ≈ 黑色——这就是「待机时顶端圆球发黑」的根因。
            if (m.HasProperty("_EmissionTex"))
            {
                m.SetTexture("_EmissionTex", SolidTex(Glows[slot]));
                if (mul > 0f) m.EnableKeyword("_EMISSION");
                else m.DisableKeyword("_EMISSION");
            }
            // 1.2.5：功率调制与随机抖动全部显式关闭——
            // _EmissionUsePower=1 时自发光跟随建筑工作状态（待机发黑），_EmissionJitter 是随机闪烁。
            // 用户要的是「颜色稳定」，这两项都必须为 0。
            if (m.HasProperty("_EmissionUsePower"))
                m.SetFloat("_EmissionUsePower", 0f);
            if (m.HasProperty("_EmissionJitter"))
                m.SetFloat("_EmissionJitter", 0f);

            if (!_matLogged)
            {
                _matLogged = true;
                DumpMaterial(m);
            }
            return m;
        }

        private static bool _matLogged;
        private static bool _shaderSwapped;

        private static readonly Dictionary<int, Texture2D> _solidTex = new Dictionary<int, Texture2D>();

        /// <summary>
        /// 4x4 纯色贴图（当 _EmissionTex 用）。注意不能用 1x1 + 纯白：
        /// 原版 shader 的自发光是「贴图 × _EmissionColor × _EmissionMultiplier」，
        /// 贴图给纯白、倍率再拉满，整栋建筑就会过曝成白块。
        /// 这里贴图直接带色，倍率只做亮度微调。
        /// </summary>
        private static Texture2D SolidTex(Color c)
        {
            int key = ((int)(Mathf.Clamp01(c.r) * 255f) << 16) |
                      ((int)(Mathf.Clamp01(c.g) * 255f) << 8) |
                       (int)(Mathf.Clamp01(c.b) * 255f);
            Texture2D t;
            if (_solidTex.TryGetValue(key, out t) && t != null)
                return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            var v = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
                (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            for (int i = 0; i < px.Length; i++) px[i] = v;
            t.SetPixels32(px);
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.name = "hub-glow-" + key.ToString("X6");
            _solidTex[key] = t;
            return t;
        }

        /// <summary>
        /// 把材质上所有「自发光调制」浮点属性清零。
        ///
        /// 为什么要这么做：原版 energy-exchanger 用的是它专用的 PBR 变体，材质里带着一整套
        /// 「按建筑工作状态调制自发光」的开关。我们虽然显式关了 _EmissionUsePower / _EmissionJitter，
        /// 但原版还存在别的同类开关（例如按 animPool.state 做的呼吸、按 power 做的明暗），
        /// 一旦卡槽里放进消耗品、建筑开始传能，animPool.state 就在 0/100 之间来回跳，
        /// 自发光跟着跳 —— 视觉上就是「建筑模型一闪一闪」。
        ///
        /// 这里不猜属性名：直接枚举材质全部浮点属性，凡是以 _Emission 开头的
        /// （除了 _EmissionMultiplier / _EmissionSwitch 这两个留给下面显式设置）一律置 0。
        /// 效果：自发光 = 静态贴图 × 固定倍率，与工作状态完全解耦。
        /// </summary>
        /// <summary>
        /// 属性名里出现这些词，就认为它是「跟着建筑工作状态变化的调制开关」。
        /// 全部小写比较（属性名大小写不统一，原版用的就是 _EmissionUsePower 这种驼峰）。
        /// </summary>
        private static readonly string[] ModulatorKeys =
        {
            "use", "jitter", "anim", "state", "speed", "flow",
            "scroll", "pulse", "time", "power", "breath", "blink", "flicker",
        };

        private static void ClearEmissionModulators(Material m, int slot)
        {
            if (m == null || m.shader == null)
                return;
            // 用 Shader.GetProperty* 而不是 Material.GetPropertyNames：
            // DSP 引用的 Unity 程序集里没有 MaterialPropertyType（读取的是 shader 的属性表，
            // 结果等价，但兼容性更好）。
            Shader sh = m.shader;
            int propCount;
            try
            {
                propCount = sh.GetPropertyCount();
            }
            catch
            {
                return;
            }

            StringBuilder killed = null;
            for (int i = 0; i < propCount; i++)
            {
                string n;
                try
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Float)
                        continue;
                    n = sh.GetPropertyName(i);
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(n))
                    continue;
                string lower = n.ToLowerInvariant();
                if (lower.IndexOf("_emission", StringComparison.Ordinal) != 0)
                    continue;
                if (n == "_EmissionMultiplier" || n == "_EmissionSwitch")
                    continue;   // 这两个下面会按配色显式赋值
                // 只清「调制类」开关：名字里带 use / jitter / state / time / power / flow …
                // 的才会跟着工作状态变化。强度类（_EmissionIntensity 之类）必须留着，
                // 清掉会让自发光整体消失（球变黑）。
                bool isModulator = false;
                for (int k = 0; k < ModulatorKeys.Length && !isModulator; k++)
                    isModulator = lower.IndexOf(ModulatorKeys[k], StringComparison.Ordinal) >= 0;
                if (!isModulator)
                    continue;
                float v;
                try { v = m.GetFloat(n); }
                catch { continue; }
                if (Mathf.Approximately(v, 0f))
                    continue;
                m.SetFloat(n, 0f);
                if (killed == null) killed = new StringBuilder();
                if (killed.Length > 0) killed.Append(", ");
                killed.Append(n).Append('=').Append(v.ToString("0.###"));
            }

            if (killed != null)
                Plugin.Log.LogInfo("材质#" + slot + " 已关闭自发光调制开关（会跟着工作状态闪）: " + killed);
        }

        private static void DumpMaterial(Material m)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("材质自检(原版参数): name=").Append(m.name)
                  .Append(" shader=").Append(m.shader != null ? m.shader.name : "null");
                sb.Append(" tex=[").Append(string.Join(",", m.GetTexturePropertyNames())).Append(']');
                string[] cols = { "_Color", "_TintColor", "_EmissionColor", "_EmissionMask", "_SpecularColor" };
                for (int i = 0; i < cols.Length; i++)
                    if (m.HasProperty(cols[i])) sb.Append(' ').Append(cols[i]).Append('=').Append(m.GetColor(cols[i]));
                string[] flts =
                {
                    "_AlbedoMultiplier", "_EmissionMultiplier", "_EmissionSwitch", "_EmissionUsePower",
                    "_EmissionJitter", "_Metallic", "_Glossiness", "_SmoothMultiplier", "_ToggleVerta"
                };
                for (int i = 0; i < flts.Length; i++)
                    if (m.HasProperty(flts[i])) sb.Append(' ').Append(flts[i]).Append('=').Append(m.GetFloat(flts[i]));
                for (int i = 0; i < 5; i++)
                {
                    string p = i == 0 ? "_MainTex" : i == 1 ? "_EmissionTex" : i == 2 ? "_MS_Tex" :
                               i == 3 ? "_NormalTex" : "_EmissionJitterTex";
                    if (!m.HasProperty(p)) continue;
                    var t = m.GetTexture(p) as Texture2D;
                    sb.Append(' ').Append(p).Append('=').Append(t != null ? t.name + "(" + t.width + ")" : "null");
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("材质自检失败: " + e.Message);
            }
        }
    }
}
