using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnergyHub
{
    /// <summary>
    /// 建筑「通电特效」：在枢纽上方挂一组全息能量环 + 能量光柱（参考全息信标的观感）。
    ///
    /// 为什么不用建筑自身的 mesh / 材质做动画：
    ///   建筑是 GPU Instancing 批量渲染的（BatchRenderer + _InstBuffer），
    ///   材质是全场共享的，改材质等于改所有枢纽，也没法按实体切换状态。
    /// 所以特效做成独立的 GameObject（挂在星球物体下面，跟着星球自转），
    /// 由 mod 自己按帧驱动旋转/呼吸，通电时才创建、断电立刻销毁。
    ///
    /// 观感：
    ///   待机 + 有电  → 地面一圈很淡的紫色光晕 + 球体微光 + 一小截预热激光 + 几道弱电弧
    ///   发送能量     → 两圈反向旋转的紫色能量环 + 球体全亮 + 十几道电弧 + 竖直激光柱
    ///   接收能量     → 两圈反向旋转的橙色能量环 + 球体全亮 + 电弧 + 从天上收拢的橙色宽光锥
    ///
    /// 1.3.6：塔顶又换回「球」了——不再是 1.3.3 那颗黑球，也不是 1.3.4 的激光炮口，
    /// 而是参考图那种「球面射出电弧的能量核」：模型层是球 + 赤道发光环（HubMesh.AppendOrbHead），
    /// 这里补球心加色光晕、球外一圈同轴光环（参考图的多重蓝环）、以及放射状电弧。
    /// </summary>
    internal static class HubEffect
    {
        private class Inst
        {
            public GameObject root;
            public Transform ringA;
            public Transform ringB;
            public Transform glow;
            /// <summary>球心光晕（billboard 加色面片）：让球体看起来是「发光的核」而不是黑球。</summary>
            public Transform nozzle;
            public float nozzleScale = 3.6f;
            /// <summary>激光束（竖直向上，加色混合的锥筒）。</summary>
            public Transform laser;
            /// <summary>激光束的横向基准缩放（发送 1.0 / 接收 2.4 / 待机很短很细）。</summary>
            public float laserScale = 1f;
            /// <summary>激光束的纵向基准缩放（待机时压成一小截「预热光」）。</summary>
            public float laserLen = 1f;
            /// <summary>球体外的同轴光环（参考图那种多重蓝环），随球共面、只做轻微进动。</summary>
            public Transform[] worm;
            public float[] wormTilt;
            public float[] wormSpin;
            public float[] wormScale;
            /// <summary>1.6.0：绕中心核公转的三颗双色能量球。</summary>
            public Transform[] orbs;
            /// <summary>公转角（度），每帧按强度累加。</summary>
            public float orbAngle;
            /// <summary>球面射出的电弧：折线网格，每隔几十毫秒重新生成。</summary>
            public Transform bolt;
            public Mesh boltMesh;
            public float boltTimer;
            public int mode;
            public int lastFrame;
            public float phase;
            public float spinA;
            public float spinB;
            public float spinW;

            /// <summary>
            /// 特效强度 0~1（1.3.8）。由「这台机器到底有没有能量在动」决定，
            /// 见 HubManager.Tick 里的 fxTarget。0 时整组 renderer 关掉、连动画都不跑，
            /// 所以场上一排没电的枢纽是零开销的。
            /// </summary>
            public float level = 1f;
            /// <summary>缓存的子 renderer，用于一次性开关（GetComponentsInChildren 别每帧调）。</summary>
            public Renderer[] rends;
            public bool visible = true;
        }

        private static readonly Dictionary<int, Inst> _insts = new Dictionary<int, Inst>();
        private static int _planetId = -1;

        private static Mesh _ringMesh;
        private static Mesh _discMesh;
        /// <summary>激光外层（向上张开的锥筒），顶点色沿高度渐隐。</summary>
        private static Mesh _laserMesh;
        /// <summary>激光内核（细亮柱）。</summary>
        private static Mesh _coreMesh;
        private static Mesh _quadMesh;

        /// <summary>同轴光环三层（内 / 中 / 外）+ 最外层一道细环 → 参考图的「多重蓝环」。</summary>
        private static Mesh _wormMain;
        private static Mesh _wormMid;
        private static Mesh _wormOuter;
        private static Mesh _wormFar;
        /// <summary>1.6.0：夹在宽环之间的两道细环，把 4 圈加密成 6 圈。</summary>
        private static Mesh _wormThinA;
        private static Mesh _wormThinB;

        /// <summary>1.6.0：三颗公转能量球（顶点色阴阳双色，加色混合）。</summary>
        private static Mesh _orbMesh;
        private static Material _orbMat;

        private static Material _sendMat;
        private static Material _recvMat;
        private static Material _idleMat;
        /// <summary>发射口光斑材质（径向渐变贴图）。</summary>
        private static Material _spotSend;
        private static Material _spotRecv;
        private static Material _spotIdle;
        /// <summary>激光内核材质（近白，加色混合）。</summary>
        private static Material _coreMatSend;
        private static Material _coreMatRecv;
        /// <summary>同轴光环材质：亮青蓝主环 + 淡蓝紫细环；待机用 *_Dim（同色、亮度 55%）。</summary>
        private static Material _wormCore;
        private static Material _wormEdge;
        private static Material _wormCoreDim;
        private static Material _wormEdgeDim;
        /// <summary>电弧材质（紫白，加色混合）。</summary>
        private static Material _boltMat;
        private static Material _boltMatDim;
        private static Texture2D _white;
        private static Texture2D _glowTex;

        private static bool _logged;

        private static readonly Color SendTint = new Color(0.62f, 0.42f, 1.00f);
        private static readonly Color RecvTint = new Color(1.00f, 0.62f, 0.28f);
        // 1.3.2：待机色再调亮——加色混合下 (0.58,0.48,1.00) 的低端脉动看着还是黑球
        private static readonly Color IdleTint = new Color(0.72f, 0.62f, 1.00f);

        // 激光束染色：外层跟着模式色走，内核实心偏白（加色混合下才够「刺眼」）
        private static readonly Color LaserSendTint = new Color(0.72f, 0.52f, 1.00f);
        private static readonly Color LaserRecvTint = new Color(1.00f, 0.66f, 0.30f);
        private static readonly Color CoreSendTint = new Color(1.00f, 0.92f, 1.00f);
        private static readonly Color CoreRecvTint = new Color(1.00f, 0.94f, 0.78f);

        /// <summary>激光长度（米）。球心 29.55 m，打出去的是塔高的 3 倍，形成一道通天光柱。</summary>
        private const float LaserLen = 90f;

        /// <summary>塔顶中心核高度（米）——与 HubMesh.BuildPrism 的 Ball(29.80, r=0.62) 对齐。</summary>
        private const float OrbY = 29.80f;
        /// <summary>单颗能量球半径（米）。</summary>
        private const float OrbR = 0.90f;
        /// <summary>三颗球的公转半径（米）。球内缘 1.10 &gt; 中心核 0.62，球外缘 2.90 &lt; 最内环 3.15。</summary>
        private const float OrbRingR = 2.00f;
        /// <summary>能量球数量。</summary>
        private const int OrbCount = 3;
        /// <summary>公转角速度（度/秒，乘特效强度）。</summary>
        private const float OrbSpinDeg = 26f;
        /// <summary>每颗球同时挂几道电弧（总计 OrbCount × OrbBoltPer）。</summary>
        private const int BoltPerOrb = 4;
        private const int BoltCount = OrbCount * BoltPerOrb;

        // 能量球的双色（参考图：一半金色、一半翠绿，中间一道 S 形分界）
        private static readonly Color OrbGold = new Color(1.00f, 0.80f, 0.44f);
        private static readonly Color OrbJade = new Color(0.36f, 0.95f, 0.78f);

        // 光环配色（参考图：亮青蓝主环 + 更淡的蓝紫细环，全部同色系）
        private static readonly Color WormCoreTint = new Color(0.32f, 0.78f, 1.00f);
        private static readonly Color WormEdgeTint = new Color(0.58f, 0.66f, 1.00f);
        // 电弧：紫白，靠顶点色往末端衰减
        private static readonly Color BoltTint = new Color(0.86f, 0.74f, 1.00f);

        public static int Count => _insts.Count;

        /// <summary>
        /// 立即清理「已经不该存在」的特效。
        ///
        /// 这就是拆建筑后残留光圈一秒的原因：以前只靠 <see cref="Animate"/> 里
        /// 「20 帧没被 Sync 更新就删」的超时兜底，60fps 是 0.33s，帧率掉到 20fps 就正好 1 秒。
        /// 现在每帧拿实体表核对一次，建筑一被拆（entityPool[id].id 归零）当帧就销毁。
        /// </summary>
        public static void Prune(PlanetFactory factory, int planetId)
        {
            // 换星球：旧星球的特效整体丢掉，不要留到新星球上
            if (planetId != _planetId)
            {
                Clear();
                _planetId = planetId;
                return;
            }
            if (_insts.Count == 0)
                return;

            List<int> dead = null;
            EntityData[] pool = factory != null ? factory.entityPool : null;
            foreach (var kv in _insts)
            {
                int id = kv.Key;
                bool alive = pool != null && id > 0 && id < pool.Length &&
                             pool[id].id == id && pool[id].protoId == (short)C.ItemId;
                if (alive)
                    continue;
                if (dead == null) dead = new List<int>();
                dead.Add(id);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    Plugin.Log.LogInfo("枢纽 #" + dead[i] + " 已拆除，特效立即销毁");
                    Remove(dead[i]);
                }
            }
        }

        /// <summary>把某个枢纽的特效同步到当前状态（每帧调用，未出现的实体超时自动销毁）。</summary>
        /// <param name="level">
        /// 特效强度 0~1。0 = 这台机器此刻没有任何能量在动（待机没接电 / 发送端电网没空闲能量 /
        /// 接收端没在收），整组特效隐藏且不跑动画；中间值用于「充能 / 泄能」的过渡。
        /// </param>
        public static void Sync(int planetId, Transform planetRoot, int entityId,
            Vector3 pos, Quaternion rot, HubMode mode, bool powered, float rate, float level)
        {
            if (planetRoot == null)
                return;
            if (planetId != _planetId)
            {
                Clear();
                _planetId = planetId;
            }

            Inst it;
            _insts.TryGetValue(entityId, out it);

            // 断电：直接拆掉
            if (!powered)
            {
                if (it != null) Remove(entityId);
                return;
            }

            if (it == null || it.root == null || it.mode != (int)mode)
            {
                if (it != null) Remove(entityId);
                it = Create(planetRoot, mode);
                if (it == null) return;
                _insts[entityId] = it;
            }

            it.lastFrame = Time.frameCount;
            float s = Mathf.Max(0.2f, rate / 4_000_000f);
            it.spinA = Mathf.Lerp(14f, 52f, Mathf.Clamp01(s));
            it.spinB = -Mathf.Lerp(10f, 40f, Mathf.Clamp01(s));
            it.spinW = Mathf.Lerp(26f, 96f, Mathf.Clamp01(s));

            it.level = Mathf.Clamp01(level);
            // 低于 1.5% 就没有视觉意义了，整组关掉（含动画），别让没电的枢纽在那空转
            SetVisible(it, it.level > 0.015f);

            Transform tr = it.root.transform;
            // 用 local 坐标：实体坐标本来就是星球本地坐标（PlanetModelingManager 也是
            // 把地表网格 SetParent(planet.gameObject.transform) 后直接用本地坐标）。
            tr.localPosition = pos;
            tr.localRotation = rot;
        }

        /// <summary>一次性开/关整组特效的 renderer（缓存数组，别每帧 GetComponentsInChildren）。</summary>
        private static void SetVisible(Inst it, bool on)
        {
            if (it == null || it.root == null || it.visible == on)
                return;
            it.visible = on;
            if (it.rends == null)
                it.rends = it.root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < it.rends.Length; i++)
            {
                if (it.rends[i] != null)
                    it.rends[i].enabled = on;
            }
        }

        /// <summary>每帧动画（旋转 / 呼吸 / 清理失效实体）。</summary>
        public static void Animate(float dt)
        {
            float t = Time.time;
            float pS = 0.80f + 0.20f * Mathf.Sin(t * 2.6f);
            float pR = 0.82f + 0.18f * Mathf.Sin(t * 1.9f + 1.1f);
            float pI = 0.88f + 0.12f * Mathf.Sin(t * 1.2f);
            if (_sendMat != null) SetTint(_sendMat, SendTint * pS);
            if (_recvMat != null) SetTint(_recvMat, RecvTint * pR);
            if (_idleMat != null) SetTint(_idleMat, IdleTint * pI);
            // 发射口光斑：跟环同节奏，但快一点、幅度小一点，像「在充能」
            if (_spotSend != null) SetTint(_spotSend, SendTint * (0.92f + 0.08f * Mathf.Sin(t * 3.3f)));
            if (_spotRecv != null) SetTint(_spotRecv, RecvTint * (0.92f + 0.08f * Mathf.Sin(t * 2.9f + 1.0f)));
            if (_spotIdle != null) SetTint(_spotIdle, IdleTint * (0.94f + 0.06f * Mathf.Sin(t * 1.2f)));
            // 内核实心不脉动：激光要一直「刺眼」，忽明忽暗会显得是灯不是光
            if (_coreMatSend != null) SetTint(_coreMatSend, CoreSendTint);
            if (_coreMatRecv != null) SetTint(_coreMatRecv, CoreRecvTint);
            // 虫洞：只做很轻的整体亮度起伏，主要动感交给「进动」
            if (_wormCore != null)
                SetTint(_wormCore, WormCoreTint * (0.90f + 0.10f * Mathf.Sin(t * 3.9f)));
            if (_wormEdge != null)
                SetTint(_wormEdge, WormEdgeTint * (0.90f + 0.10f * Mathf.Sin(t * 3.1f + 1.7f)));
            // 能量球只做整体亮度呼吸：双色由顶点色决定，别把色相调歪
            if (_orbMat != null)
                SetTint(_orbMat, Color.white * (0.94f + 0.06f * Mathf.Sin(t * 2.2f)));

            if (_insts.Count == 0)
                return;

            int frame = Time.frameCount;
            List<int> dead = null;
            foreach (var kv in _insts)
            {
                Inst it = kv.Value;
                if (it == null || it.root == null || frame - it.lastFrame > 20)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                    continue;
                }
                // 强度 0：整组已经隐藏，连动画都不跑（一排没电的枢纽就是零开销）
                float L = it.level;
                if (L <= 0.015f)
                    continue;

                if (it.ringA != null) it.ringA.Rotate(0f, it.spinA * L * dt, 0f, Space.Self);
                if (it.ringB != null) it.ringB.Rotate(0f, it.spinB * L * dt, 0f, Space.Self);

                // 发射口光斑：始终正面朝向摄像机（billboard），轻微呼吸
                if (it.nozzle != null)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                        it.nozzle.rotation = cam.transform.rotation;
                    // 强度低时光晕收缩：看起来像「核在冷下去」，而不是突然消失
                    float k = it.nozzleScale * (0.22f + 0.78f * L) * (1f + 0.07f * Mathf.Sin(t * 4.1f + it.phase));
                    it.nozzle.localScale = new Vector3(k, k, k);
                }

                // 激光束：高频微抖 + 横向呼吸，看起来是「正在发射」而不是一根静止柱子
                if (it.laser != null)
                {
                    float j = 1f + 0.055f * Mathf.Sin(t * 12.7f + it.phase)
                                 + 0.022f * Mathf.Sin(t * 29.3f + it.phase * 2.1f);
                    float w = it.laserScale * (0.28f + 0.72f * L) * j;
                    // 长度直接乘强度：没电时激光会「缩回」球里，而不是凭空消失
                    it.laser.localScale = new Vector3(w, it.laserLen * L, w);
                    it.laser.Rotate(0f, 40f * L * dt, 0f, Space.Self);
                }

                // 1.6.0：三颗能量球绕中心核公转 + 各自绕自身轴自转（双色在球面上流转）。
                // 公转半径随强度收缩：没电时三颗球收拢到中心核附近，不会「悬在空里转」。
                if (it.orbs != null)
                {
                    it.orbAngle += dt * OrbSpinDeg * L;
                    float rr = OrbRingR * (0.45f + 0.55f * L);
                    for (int i = 0; i < it.orbs.Length; i++)
                    {
                        Transform o = it.orbs[i];
                        if (o == null)
                            continue;
                        float a = (it.orbAngle + i * (360f / it.orbs.Length)) * Mathf.Deg2Rad + it.phase;
                        float yy = OrbY + 0.10f * Mathf.Sin(t * 1.6f + i * 2.1f);
                        o.localPosition = new Vector3(Mathf.Cos(a) * rr, yy, Mathf.Sin(a) * rr);
                        o.Rotate(0f, (34f + i * 21f) * L * dt, 0f, Space.Self);
                    }
                }

                // 球外同轴光环：参考图里这些环基本是共面的，所以倾角只给 2~4°，
                // 进动也很慢（0.13~0.21 rad/s）——动感来自「整圈轻微的椭圆摆动」，
                // 而不是上一版那种 30° 大倾角互相穿插（那看着像乱线团，不像能量环）。
                if (it.worm != null)
                {
                    float breath = 1f + 0.045f * Mathf.Sin(t * 1.9f + it.phase);
                    for (int i = 0; i < it.worm.Length; i++)
                    {
                        Transform w = it.worm[i];
                        if (w == null)
                            continue;
                        // 进动速度也乘强度：没电时环会停下来并缩到球面附近
                        float a = t * it.wormSpin[i] * L + it.phase * 2f + i * 1.3f;
                        float tilt = it.wormTilt[i];
                        w.localRotation = Quaternion.Euler(
                            Mathf.Sin(a) * tilt,
                            a * 6f,
                            Mathf.Cos(a) * tilt);
                        float s = it.wormScale[i] * breath * (0.32f + 0.68f * L);
                        w.localScale = new Vector3(s, 1f, s);
                    }
                }

                // 球面电弧：每 80~150 ms 换一组折线（和参考图里那种「持续放电」一致）。
                // 强度不够时整组关掉——电弧是最费的一块，没电还在这里打雷最没道理。
                if (it.bolt != null && it.boltMesh != null)
                {
                    bool arcOn = L > 0.35f;
                    if (it.bolt.gameObject.activeSelf != arcOn)
                        it.bolt.gameObject.SetActive(arcOn);
                    if (arcOn)
                    {
                        it.boltTimer -= dt;
                        if (it.boltTimer <= 0f)
                        {
                            it.boltTimer = 0.08f + UnityEngine.Random.value * 0.07f;
                            RebuildBolt(it.boltMesh, UnityEngine.Random.Range(1, 999999), OrbCenters(it));
                        }
                    }
                }

                if (it.glow != null)
                {
                    float p = 1f + 0.025f * Mathf.Sin(t * 2.2f + it.phase);
                    float g = p * (0.30f + 0.70f * L);
                    it.glow.localScale = new Vector3(g, 1f, g);
                }
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) Remove(dead[i]);
            }
        }

        public static void Clear()
        {
            var keys = new List<int>(_insts.Keys);
            for (int i = 0; i < keys.Count; i++) Remove(keys[i]);
            _insts.Clear();
        }

        private static void Remove(int entityId)
        {
            Inst it;
            if (!_insts.TryGetValue(entityId, out it))
                return;
            _insts.Remove(entityId);
            if (it != null && it.root != null)
                UnityEngine.Object.Destroy(it.root);
        }

        private static void SetTint(Material m, Color c)
        {
            if (m.HasProperty("_TintColor"))
                m.SetColor("_TintColor", c);
        }

        // ---------------- 构建 ----------------

        private static Inst Create(Transform parent, HubMode mode)
        {
            EnsureAssets();
            if (_ringMesh == null || _sendMat == null)
                return null;

            // 1.7.1：储能不再是独立模式（并入发送/接收）。特效配色按实际模式走：
            // 只有待机用「待机」那套构图；发送 / 接收在充放电时照常亮自己的配色。
            bool idleLike = mode == HubMode.Idle;

            Material mat = mode == HubMode.Send ? _sendMat : mode == HubMode.Receive ? _recvMat : _idleMat;
            var go = new GameObject("energy-hub-fx-" + mode);
            go.transform.SetParent(parent, false);

            var it = new Inst { root = go, mode = (int)mode, phase = UnityEngine.Random.value * 6.283f };

            // 地面光晕：三种状态都有，待机时最淡
            it.glow = AddPart(go.transform, "glow", _discMesh, mat, new Vector3(0f, 0.12f, 0f), 1f);

            // 塔顶能量球：球心一圈加色光晕（billboard）。
            // 模型层的球是实体材质，不叠这层光晕在暗处就会读成「黑球」（1.3.3 踩过这个坑），
            // 叠上去之后球心是发光的核，和参考图一致。
            Material spotMat = mode == HubMode.Send ? _spotSend
                             : mode == HubMode.Receive ? _spotRecv : _spotIdle;
            // 1.6.0：球心光晕缩小到三球的内切范围，否则整个塔顶被一块白斑糊住
            it.nozzleScale = idleLike ? 1.9f : 2.5f;
            it.nozzle = AddPart(go.transform, "orb-glow", _quadMesh, spotMat,
                new Vector3(0f, OrbY, 0f), it.nozzleScale);
            if (it.nozzle != null)
                it.nozzle.localScale = new Vector3(it.nozzleScale, it.nozzleScale, it.nozzleScale);

            // 待机 → 一小截「预热」光；发送 → 细长直射；接收 → 宽锥从天上收拢
            it.laserScale = mode == HubMode.Send ? 1f : mode == HubMode.Receive ? 2.4f : 0.55f;
            it.laserLen = idleLike ? 0.05f : 1f;
            it.laser = AddPart(go.transform, "laser", _laserMesh, mat,
                new Vector3(0f, OrbY, 0f), it.laserScale);
            if (it.laser != null)
                it.laser.localScale = new Vector3(it.laserScale, it.laserLen, it.laserScale);

            if (mode == HubMode.Send)
            {
                // 内核实心细柱：只有发送端有（接收端是「收光」，不需要一根亮柱）
                Transform core = AddPart(go.transform, "laser-core", _coreMesh, _coreMatSend,
                    new Vector3(0f, OrbY, 0f), 1f);
                if (core != null)
                    core.localScale = Vector3.one;
            }

            // 球面电弧：挂在建筑原点，顶点是建筑本地坐标（1.6.0 起电弧从三颗球的表面伸出）
            it.orbAngle = it.phase * 57.29578f;
            CreateOrbs(go.transform, it);
            it.boltMesh = new Mesh { name = "hub-fx-bolt" };
            it.bolt = AddPart(go.transform, "bolt", it.boltMesh,
                idleLike ? _boltMatDim : _boltMat, Vector3.zero, 1f);
            if (it.bolt != null)
            {
                it.bolt.localScale = Vector3.one;
                RebuildBolt(it.boltMesh, UnityEngine.Random.Range(1, 999999), OrbCenters(it));
            }

            // 球外同轴光环：6 圈（宽环 + 细环交替），半径 3.15~5.35 m，
            // 全部和球心基本共面，只给 2~4° 的倾角做轻微摆动。
            bool dim = idleLike;
            Material wCore = dim ? _wormCoreDim : _wormCore;
            Material wEdge = dim ? _wormEdgeDim : _wormEdge;
            Mesh[] wmesh = { _wormMain, _wormMid, _wormThinA, _wormOuter, _wormThinB, _wormFar };
            float[] wtilt = { 2.5f, -3.2f, 3.6f, 3.8f, -2.2f, -2.6f };
            float[] wspin = { 0.16f, -0.21f, 0.24f, 0.13f, -0.26f, -0.18f };
            float[] wsc = { 1f, 1f, 1f, 1f, 1f, 1f };
            float[] wy = { 0f, 0.06f, -0.04f, -0.05f, 0.09f, 0.10f };
            it.worm = new Transform[wmesh.Length];
            it.wormTilt = new float[wmesh.Length];
            it.wormSpin = new float[wmesh.Length];
            it.wormScale = new float[wmesh.Length];
            for (int i = 0; i < wmesh.Length; i++)
            {
                Material mm = (i == 1 || i == 4 || i == 5) ? wEdge : wCore;
                it.worm[i] = AddPart(go.transform, "ring" + i, wmesh[i], mm,
                    new Vector3(0f, OrbY + wy[i], 0f), wsc[i]);
                it.wormTilt[i] = wtilt[i];
                it.wormSpin[i] = wspin[i];
                it.wormScale[i] = wsc[i];
            }

            if (idleLike)
                return Bind(go, it);

            // 两圈能量环（半径按塔架尺寸取，绕在桅杆外侧）
            it.ringA = AddPart(go.transform, "ringA", _ringMesh, mat, new Vector3(0f, 10.6f, 0f), 1f);
            it.ringB = AddPart(go.transform, "ringB", _ringMesh, mat, new Vector3(0f, 18.2f, 0f), 0.76f);

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo("通电特效已启用: 环/激光/发射口光斑/" +
                                   (mat != null && mat.shader != null ? mat.shader.name : "null") +
                                   " 尺寸=环10.6m/18.2m 发射口28.7m 激光" + LaserLen + "m");
            }
            return Bind(go, it);
        }

        /// <summary>1.6.0：三颗绕中心核分布的能量球（公转位置由 Animate 每帧更新）。</summary>
        private static void CreateOrbs(Transform parent, Inst it)
        {
            it.orbs = new Transform[OrbCount];
            for (int i = 0; i < OrbCount; i++)
            {
                float a = Mathf.PI * 2f * i / OrbCount;
                var p = new Vector3(Mathf.Cos(a) * OrbRingR, OrbY, Mathf.Sin(a) * OrbRingR);
                Transform o = AddPart(parent, "orb" + i, _orbMesh, _orbMat, p, 1f);
                if (o != null)
                {
                    o.localScale = Vector3.one * OrbR;
                    // 初始自转角错开，三颗球的双色朝向不完全一致
                    o.localRotation = Quaternion.Euler(0f, 47f * i, 0f);
                }
                it.orbs[i] = o;
            }
        }

        /// <summary>三颗球的当前本地坐标（给电弧网格用）。</summary>
        private static Vector3[] OrbCenters(Inst it)
        {
            var c = new Vector3[OrbCount];
            for (int i = 0; i < OrbCount; i++)
            {
                Transform o = it.orbs != null && i < it.orbs.Length ? it.orbs[i] : null;
                c[i] = o != null ? o.localPosition : new Vector3(0f, OrbY, 0f);
            }
            return c;
        }

        /// <summary>
        /// 能量球网格：单位球 + 顶点色。
        ///
        /// 顶点色不是均匀的，而是「金 / 翠阴阳双色」：
        ///   分界 = sin(经度 + 0.7·sin(1.6·纬度)) → 一条 S 形分界（参考图那种太极式分割）；
        ///   亮度 = 0.46 + 0.54·(1-|纬度|)^0.75 → 赤道最亮、两极压暗，
        ///   这样加色混合下侧看仍有明暗过渡，能读成一颗球而不是一块亮斑。
        /// </summary>
        private static Mesh BuildOrbMesh(int rings, int seg)
        {
            var verts = new List<Vector3>((rings + 1) * (seg + 1));
            var cols = new List<Color>((rings + 1) * (seg + 1));
            var tris = new List<int>(rings * seg * 6);
            for (int i = 0; i <= rings; i++)
            {
                float ph = Mathf.PI * i / rings;
                float y = Mathf.Cos(ph);
                float rr = Mathf.Sin(ph);
                for (int j = 0; j <= seg; j++)
                {
                    float t = Mathf.PI * 2f * j / seg;
                    verts.Add(new Vector3(rr * Mathf.Cos(t), y, rr * Mathf.Sin(t)));
                    float s = Mathf.Sin(t + 0.70f * Mathf.Sin(y * 1.6f));
                    float k = 0.5f + 0.5f * (float)Math.Tanh(s * 2.4f);
                    Color c = Color.Lerp(OrbJade, OrbGold, k);
                    float sh = 0.46f + 0.54f * Mathf.Pow(1f - Mathf.Abs(y), 0.75f);
                    cols.Add(new Color(c.r * sh, c.g * sh, c.b * sh, 1f));
                }
            }
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j;
                    int b = a + seg + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }
            var m = new Mesh { name = "hub-fx-orb" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>把整组 renderer 缓存进 Inst（开关特效时一次性切，不用每帧 GetComponentsInChildren）。</summary>
        private static Inst Bind(GameObject go, Inst it)
        {
            it.rends = go.GetComponentsInChildren<MeshRenderer>(true);
            it.level = 1f;
            it.visible = true;
            return it;
        }

        private static Transform AddPart(Transform parent, string name, Mesh mesh, Material mat,
            Vector3 localPos, float scale)
        {
            var go = new GameObject(name);
            var tr = go.transform;
            tr.SetParent(parent, false);
            tr.localPosition = localPos;
            tr.localRotation = Quaternion.identity;
            tr.localScale = new Vector3(scale, 1f, scale);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return tr;
        }

        private static void EnsureAssets()
        {
            if (_ringMesh != null)
                return;

            _ringMesh = BuildRing(2.05f, 2.75f, 64);
            _discMesh = BuildDisc(5.1f, 64);
            // 激光束：外层宽锥（向上张开、快速渐隐）+ 内核细柱（几乎不收细，保持「刺眼」）
            _laserMesh = BuildBeam(0.26f, 1.15f, LaserLen, 20, 14, 1.70f);
            _coreMesh = BuildBeam(0.085f, 0.055f, LaserLen, 10, 6, 0.35f);
            _quadMesh = BuildQuad();
            // 球外同轴光环 6 圈（宽环 / 细环交替，中间最亮、内外两侧渐隐）
            _wormMain = BuildRing(3.06f, 3.22f, 96);      // r 3.15 宽环
            _wormMid = BuildRing(3.70f, 3.86f, 96);       // r 3.78 宽环
            _wormThinA = BuildRing(4.10f, 4.15f, 96);     // r 4.12 细环
            _wormOuter = BuildRing(4.48f, 4.68f, 96);     // r 4.58 宽环
            _wormThinB = BuildRing(4.92f, 4.97f, 96);     // r 4.95 细环
            _wormFar = BuildRing(5.22f, 5.48f, 96);       // r 5.35 宽环
            // 三颗公转能量球
            _orbMesh = BuildOrbMesh(22, 34);

            Shader sh = null;
            string[] names =
            {
                "Legacy Shaders/Particles/Additive",
                "Legacy Shaders/Particles/Alpha Blended",
                "Sprites/Default",
            };
            string picked = null;
            for (int i = 0; i < names.Length && sh == null; i++)
            {
                sh = Shader.Find(names[i]);
                if (sh != null) picked = names[i];
            }
            if (sh == null)
            {
                Plugin.Log.LogWarning("找不到可用的特效 shader，通电特效将不显示");
                return;
            }

            _white = SolidWhite();
            _glowTex = BuildGlowTexture(128);
            _sendMat = NewMat(sh, SendTint);
            _recvMat = NewMat(sh, RecvTint);
            _idleMat = NewMat(sh, IdleTint);
            _spotSend = NewMat(sh, SendTint, _glowTex);
            _spotRecv = NewMat(sh, RecvTint, _glowTex);
            _spotIdle = NewMat(sh, IdleTint, _glowTex);
            _coreMatSend = NewMat(sh, CoreSendTint);
            _coreMatRecv = NewMat(sh, CoreRecvTint);
            _wormCore = NewMat(sh, WormCoreTint);
            _wormEdge = NewMat(sh, WormEdgeTint);
            // 待机版：同色、亮度 55%，避免一排待机枢纽顶上都挂着刺眼光环
            _wormCoreDim = NewMat(sh, WormCoreTint * 0.55f);
            _wormEdgeDim = NewMat(sh, WormEdgeTint * 0.55f);
            _boltMat = NewMat(sh, BoltTint);
            _boltMatDim = NewMat(sh, BoltTint * 0.5f);
            // 能量球：顶点色已经是金/翠双色，_TintColor 只做整体亮度呼吸
            _orbMat = NewMat(sh, Color.white);
            Plugin.Log.LogInfo("特效 shader=" + picked + "（塔顶三球：" + OrbCount + " 颗 r" + OrbR +
                               " 公转 R" + OrbRingR + " + 球面电弧 " + BoltCount +
                               " 道 + 6 圈同轴光环 + " + LaserLen + "m 激光束）");
        }

        private static Material NewMat(Shader sh, Color tint)
        {
            return NewMat(sh, tint, null);
        }

        private static Material NewMat(Shader sh, Color tint, Texture2D tex)
        {
            var m = new Material(sh) { name = "energy-hub-fx" };
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
            if (m.HasProperty("_MainTex")) m.mainTexture = tex != null ? (Texture)tex : _white;
            m.renderQueue = 3100;
            return m;
        }

        private static Texture2D SolidWhite()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            t.name = "hub-fx-white";
            return t;
        }

        // ---------------- 顶端激光（1.3.4） ----------------

        /// <summary>面向摄像机的正方形（单位半径，外面用 localScale 拉到球半径）。</summary>
        private static Mesh BuildQuad()
        {
            var m = new Mesh { name = "hub-fx-quad" };
            m.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// 程序化生成「点光源」贴图：中心实心亮、向外快速衰减到 0。
        /// 用作发射口光斑（billboard 面片），比纯色方块自然得多。
        /// </summary>
        private static Texture2D BuildGlowTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.name = "hub-fx-glow";
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n * 2f - 1f;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    // 柔和外晕（1-r 的平方）× 中心灼热点
                    float halo = Mathf.Clamp01(1f - r);
                    halo = halo * halo * 0.80f;
                    float core = Mathf.Clamp01(1f - r / 0.30f);
                    core = core * core;
                    float k = Mathf.Clamp01(halo + core);
                    px[y * n + x] = new Color(1f, 1f, 1f, k);
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }


        // ---------------- 球面电弧（1.3.6） ----------------

        /// <summary>
        /// 重建一组「球面射出的电弧」。
        ///
        /// 每道电弧 = 从球面（0.92R）朝随机方向伸出 3~8 m 的折线，
        /// 每条线段做成两张互相垂直的细带（省掉朝向摄像机的计算，任何角度都不会退化成一条线）。
        /// 顶点色从根部近白到末端转暗，配合加色混合就是参考图那种「持续放电」的观感。
        /// </summary>
        private static void RebuildBolt(Mesh m, int seed, Vector3[] centers)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>(BoltCount * 64);
            var cols = new List<Color>(BoltCount * 64);
            var tris = new List<int>(BoltCount * 48);

            for (int b = 0; b < BoltCount; b++)
            {
                // 1.6.0：电弧改成从三颗公转球的球面伸出（按 b 轮换到不同球），
                // 所以起点要叠加球心坐标（建筑本地坐标，bolt 节点本身在原点）。
                Vector3 basePos = centers != null && centers.Length > 0
                    ? centers[b % centers.Length] : Vector3.zero;
                double th = rnd.NextDouble() * Math.PI * 2.0;
                double ph = (rnd.NextDouble() - 0.5) * 1.4;      // 上下散布 ±0.7 rad
                float ch = (float)Math.Cos(ph);
                var dir = new Vector3(
                    (float)Math.Cos(th) * ch,
                    (float)Math.Sin(ph),
                    (float)Math.Sin(th) * ch).normalized;

                Vector3 perp = Vector3.Cross(dir, Vector3.up);
                if (perp.sqrMagnitude < 0.01f)
                    perp = Vector3.Cross(dir, Vector3.right);
                perp.Normalize();
                Vector3 perp2 = Vector3.Cross(dir, perp).normalized;

                int segs = 6 + rnd.Next(4);
                float len = 1.5f + (float)rnd.NextDouble() * 2.2f;
                float r0 = OrbR * 0.92f;
                Vector3 prev = basePos + dir * r0;

                for (int i = 1; i <= segs; i++)
                {
                    float f = (float)i / segs;
                    float f0 = (float)(i - 1) / segs;
                    Vector3 cur = basePos + dir * (r0 + len * f);
                    float amp = 0.30f * (1f - f) + 0.05f;
                    cur += perp * ((float)rnd.NextDouble() * 2f - 1f) * amp;
                    cur += perp2 * ((float)rnd.NextDouble() * 2f - 1f) * amp;

                    float w0 = Mathf.Lerp(0.060f, 0.012f, f0);
                    float w1 = Mathf.Lerp(0.060f, 0.012f, f);
                    Color c0 = BoltFade(f0);
                    Color c1 = BoltFade(f);

                    BoltQuad(verts, cols, tris, prev + perp * w0, prev - perp * w0,
                        cur - perp * w1, cur + perp * w1, c0, c1);
                    BoltQuad(verts, cols, tris, prev + perp2 * w0, prev - perp2 * w0,
                        cur - perp2 * w1, cur + perp2 * w1, c0, c1);

                    prev = cur;
                }
            }

            m.Clear();
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
        }

        /// <summary>电弧颜色：根部近白，末端偏紫并淡出。</summary>
        private static Color BoltFade(float f)
        {
            float k = 1f - f * 0.72f;
            return new Color(k, k * (0.90f - 0.18f * f), 1f, Mathf.Clamp01(1.05f - f * 0.85f));
        }

        private static void BoltQuad(List<Vector3> v, List<Color> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color ca, Color cb)
        {
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            c.Add(ca); c.Add(ca); c.Add(cb); c.Add(cb);
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        /// <summary>水平环形带（XZ 平面），颜色沿径向做软边。</summary>
        private static Mesh BuildRing(float inner, float outer, int seg)
        {
            float mid = (inner + outer) * 0.5f;
            var verts = new List<Vector3>((seg + 1) * 3);
            var cols = new List<Color>((seg + 1) * 3);
            var tris = new List<int>(seg * 12);
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(c * inner, 0f, s * inner));
                cols.Add(new Color(0.05f, 0.05f, 0.05f, 1f));
                verts.Add(new Vector3(c * mid, 0f, s * mid));
                cols.Add(Color.white);
                verts.Add(new Vector3(c * outer, 0f, s * outer));
                cols.Add(new Color(0.05f, 0.05f, 0.05f, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 3;
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 4);
                tris.Add(b + 1); tris.Add(b + 4); tris.Add(b + 2);
                tris.Add(b + 2); tris.Add(b + 4); tris.Add(b + 5);
            }
            var m = new Mesh { name = "hub-fx-ring" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>地面光晕盘（中心亮、边缘消失）。</summary>
        private static Mesh BuildDisc(float radius, int seg)
        {
            var verts = new List<Vector3>(seg + 2);
            var cols = new List<Color>(seg + 2);
            var tris = new List<int>(seg * 3);
            verts.Add(Vector3.zero);
            cols.Add(new Color(0.7f, 0.7f, 0.7f, 1f));
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                cols.Add(new Color(0.02f, 0.02f, 0.02f, 1f));
            }
            for (int i = 1; i <= seg; i++)
            {
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
            }
            var m = new Mesh { name = "hub-fx-disc" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>球（虫洞中心核），赤道最亮。</summary>
        private static Mesh BuildSphere(float radius, int rings, int seg)
        {
            var verts = new List<Vector3>((rings + 1) * (seg + 1));
            var cols = new List<Color>((rings + 1) * (seg + 1));
            var tris = new List<int>(rings * seg * 6);
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float y = Mathf.Cos(phi) * radius;
                float rr = Mathf.Sin(phi) * radius;
                float c = 0.35f + 0.65f * Mathf.Sin(phi);
                for (int i = 0; i <= seg; i++)
                {
                    float th = Mathf.PI * 2f * i / seg;
                    verts.Add(new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr));
                    cols.Add(new Color(c, c, c, 1f));
                }
            }
            int stride = seg + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int i = 0; i < seg; i++)
                {
                    int b = r * stride + i;
                    tris.Add(b); tris.Add(b + stride); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + stride); tris.Add(b + stride + 1);
                }
            }
            var m = new Mesh { name = "hub-fx-core" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// 竖直激光束（锥筒）：口部半径 r0 → 远端半径 r1，顶点色按 (1-f)^power 渐隐。
        /// power 小 → 内部亮柱（几乎不衰减）；power 大 → 外层柔和辉光（很快淡掉）。
        /// </summary>
        private static Mesh BuildBeam(float r0, float r1, float height, int seg, int rings, float power)
        {
            var verts = new List<Vector3>((rings + 1) * (seg + 1));
            var cols = new List<Color>((rings + 1) * (seg + 1));
            var uvs = new List<Vector2>((rings + 1) * (seg + 1));
            var tris = new List<int>(rings * seg * 6);
            for (int r = 0; r <= rings; r++)
            {
                float f = (float)r / rings;
                float y = height * f;
                float rad = Mathf.Lerp(r0, r1, f);
                float c = 0.62f * Mathf.Pow(1f - f, power);
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * 2f * i / seg;
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
                    cols.Add(new Color(c, c, c, 1f));
                    uvs.Add(new Vector2(0.5f, 0.5f));
                }
            }
            int stride = seg + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int i = 0; i < seg; i++)
                {
                    int b = r * stride + i;
                    tris.Add(b); tris.Add(b + stride); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + stride); tris.Add(b + stride + 1);
                }
            }
            var m = new Mesh { name = "hub-fx-beam" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
