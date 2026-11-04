using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EnergyHub
{
    public enum HubMode
    {
        Idle = 0,
        Send = 1,
        Receive = 2,
        /// <summary>
        /// 【已废弃】储能（1.7.0 曾作为独立模式，1.7.1 并入发送/接收——
        /// 这两种模式下池子本来就自动充放，不需要单独的按钮）。
        /// 枚举值保留只为老存档兼容：Import 时 3 会转成 Idle。
        /// </summary>
        Store = 3,
    }

    /// <summary>单台枢纽的可持久化配置。</summary>
    public class HubInfo
    {
        public int entityId;
        public int planetId;
        public int mode = (int)HubMode.Idle;

        /// <summary>
        /// 对接的对端终端（只由「接收端」保存——连接是接收端主动发起的）。
        /// -1 / 0 表示未连接。发送端不需要存对端，它每帧从别人指向自己的连接反推。
        /// </summary>
        public int peerPlanetId = -1;
        public int peerEntityId;

        /// <summary>
        /// 记录这台枢纽「第一次被看到时」的位置。
        /// DSP 的 entityId 会被回收复用：拆掉一台枢纽、原地（或附近）再建一台，
        /// 新建筑很可能拿到同一个 id。没有这个位置校验的话，新建筑会直接继承
        /// 旧建筑的「接收模式 + 对接关系」，看起来就像是「我没点连接，它自己连上了」。
        /// </summary>
        public Vector3 pos;
        public bool hasPos;

        // 统计数据（持久化，跨存档累计）
        public long totalSent;
        public long totalReceived;

        // 运行时
        public long lastTickEnergy;
        public float ratePerSecond;
        /// <summary>物品扣费的小数累计（不足 1 个的部分留到下次）。</summary>
        public double costAcc;
        /// <summary>诊断日志节流计时（每 2 秒打一条收发状态）。</summary>
        public float diagTimer;

        // 对接状态（运行时，每帧由管理器重算）
        /// <summary>发送端：有多少台接收端连到了我（=0 时不工作）。</summary>
        public int receiverCount;
        /// <summary>接收端：0=未连接，1=已连接（且链路有效）。</summary>
        public int senderCount;
        /// <summary>链路是否已生效（发送端 receiverCount&gt;0；接收端 senderCount&gt;0）。</summary>
        public bool linked;
        /// <summary>接收端：对端终端是否还存在且仍处于「发送」模式。</summary>
        public bool peerValid;
        /// <summary>
        /// 接收端：它所在星球的工厂还没加载（没被模拟）。
        /// 这时链路仍然成立，能量先记进 _pending，等那颗星球加载后再落地。
        /// </summary>
        public bool peerUnloaded;
        /// <summary>
        /// 接收端：对端还在，但当前不是「发送能量」状态（链路暂停，切回发送会自动恢复）。
        /// 只用来避免每帧重复打日志。
        /// </summary>
        public bool peerStale;
        /// <summary>
        /// 这条对接关系是读档恢复回来的（不是本次游戏里点出来的）。
        /// 面板会标「存档恢复」，免得玩家误以为「我没点，它自己连上了」。
        /// </summary>
        public bool restored;
        /// <summary>上一帧记录下来的链路状态（只用于打日志，别拿它做逻辑）。</summary>
        public bool loggedLink;
        /// <summary>
        /// 发送端：卡槽里没有可用消耗品，正在按降速模式传能（面板显示提示用）。
        /// 以前缺料是「硬停」，玩家看到的是完全不动、以为功能坏了。
        /// </summary>
        public bool itemStarved;

        // ---- 特效强度（1.3.8）----
        /// <summary>
        /// 特效强度 0~1：0 = 完全不显示（连待机的光环都不转），1 = 满功率。
        ///
        /// 为什么不用「有没有电」这个布尔值：电网每帧都在抖（负载变化、电池满放），
        /// 布尔值会让特效疯狂开关。原版交换机的 state 也是每 tick 以 0.00557 逼近 target，
        /// 靠斜坡消化抖动，这里照抄这个做法。
        /// </summary>
        public float fxLevel;
        /// <summary>本帧期望的特效强度目标（0 或 1）。</summary>
        public float fxTarget = 1f;
        /// <summary>目标为 0 时的原因（只用于打日志 / 面板提示）。</summary>
        public string fxReason = "";
        /// <summary>上一次打日志时的开关状态。</summary>
        public bool fxLoggedOn;
        /// <summary>是否已经打过第一条日志（避免开场对一个本来就是 0 的实例误报「已停止」）。</summary>
        public bool fxLoggedOnce;
        /// <summary>
        /// 「有能量在动」的保持计时（秒）。见 C.FxHoldSec：
        /// 电网负载每帧都在变，不加这个会让特效跟着电网一起一闪一闪。
        /// </summary>
        public float gridHold;

        // ---- 能量池等级（1.7.0，1.10.0 改为 1~20 级）----
        /// <summary>能量池等级 1~20：容量见 <see cref="C.PoolOf"/>（100 GJ → 1 PJ 指数曲线）。</summary>
        public int level = 1;

        /// <summary>
        /// 蓄电器方向的当前决策：1 = 充电，-1 = 放电，0 = 停。
        /// 1.7.1 起发送/接收模式共用（池子在这两种模式下自动充当蓄电器）。
        /// </summary>
        public int storageDir;
        /// <summary>蓄电器切换方向的计时（消抖用，见 HubManager.BatteryDir）。</summary>
        public float storageHold;
        /// <summary>面板显示用：上一次充/放电功率（J/tick，带符号）。</summary>
        public long lastFlow;

        // ---- 提速（1.10.0）----
        /// <summary>卡槽里有三级增产剂、本帧传输确实在提速（面板显示用）。</summary>
        public bool boostActive;
        /// <summary>提速耗材的扣料累计（秒），不足 1 个的部分留到下次。</summary>
        public float boostAcc;

        // ---- 按需自动分配（1.11.0）----
        /// <summary>
        /// 接收端：本帧向发送端上报的「需求能量」（J）。
        /// = min(池子空余, 满功率放电 × <see cref="C.DemandSeconds"/> 秒) + 本地电网缺口。
        /// 发送端据此按比例分配，而不是对 N 台接收端无脑均分。
        /// </summary>
        public long demand;
        /// <summary>接收端：需求是否在有效期内（远程星球未加载 / 机器没被模拟时会上一次的值过期）。</summary>
        public bool demandFresh;
        /// <summary>接收端：上一次上报需求的时间戳（<c>Time.time</c>），发送端据此判断新鲜度。</summary>
        public float demandStamp;

        /// <summary>
        /// 发送端：本帧实际分给这台接收端的能量（J），面板显示用。
        /// 每帧被发送端清空重算，不属于存档数据。
        /// </summary>
        public long granted;
        /// <summary>发送端：本帧分给它的能量占本次总额的比例（0~1），面板显示用。</summary>
        public float grantedPct;


        /// <summary>
        /// 已经投进升级池的材料（1.13.0）。
        ///
        /// 为什么要有它：阶梯材料后期一档要 2600~3500 个蓄电器，而蓄电器堆叠小，
        /// 卡槽 84 格 + 背包也放不下，玩家不可能「一次凑齐」。所以改成投入式：
        /// 卡槽里（含传送带送进来的）升级材料会自动被吸进这里累积，攒够当前等级的需求再点升级。
        /// 满级时若还有余料，会自动退回卡槽，不会凭空消失。
        /// </summary>
        public long upAcc;
        /// <summary>已投入升级池的引力矩阵（1.13.0）。</summary>
        public long upMat;

        /// <summary>卡槽吸收节流计时（运行时，不入存档）：每 0.25 秒扫一次卡槽就够。</summary>
        public float absorbTimer;

        /// <summary>这台机器的能量池容量（J）。</summary>
        public long Capacity
        {
            get { return C.PoolOf(level); }
        }

        /// <summary>升级到下一级要多少蓄电器（满级返回 0）。</summary>
        public int UpgradeNeed
        {
            get { return C.UpgradeCostOf(level); }
        }

        /// <summary>升级到下一级要多少引力矩阵（满级返回 0）。</summary>
        public int UpgradeNeed2
        {
            get { return C.UpgradeCost2Of(level); }
        }
    }

    /// <summary>对接面板里的一行：一个「终端」。</summary>
    public class PeerRow
    {
        public int planetId;
        public int entityId;
        public string planetName;
        /// <summary>与当前星球相同（能读到实时状态）。</summary>
        public bool samePlanet;
        /// <summary>是否已通电（只有同星球才判得出来，远程一律 false）。</summary>
        public bool online;
        /// <summary>是否已经和本机连上。</summary>
        public bool connected;
        /// <summary>本端是否可点「连接」（接收端才行）。</summary>
        public bool selectable;
        /// <summary>对端累计收发能量（用于面板显示）。</summary>
        public long peerTotal;
        /// <summary>对端当前池电量（仅同星球实时值，远程为 -1）。</summary>
        public long peerPool = -1;
        /// <summary>已经有几台接收端连着它（发送端行的显示）。</summary>
        public int peerLinks;
        /// <summary>与本机不在同一个恒星系（跨星系传能）。</summary>
        public bool crossStar;

        /// <summary>自动分配：这台接收端当前上报的需求（J），发送端行为 0 时表示不适用。</summary>
        public long demand;
        /// <summary>自动分配：本帧实际分给它的能量（J）。</summary>
        public long granted;
        /// <summary>自动分配：需求是否有效（过期时面板显示「需求未知」）。</summary>
        public bool demandValid;
    }

    /// <summary>
    /// 星际能量枢纽的全局管理器。
    /// 能量池直接复用游戏原生的 PowerExchangerComponent：
    ///   state =  1 表示从本地电网充电（发送端抽电）
    ///   state = -1 表示向本地电网放电（接收端注入）
    /// 跨星球能量用全局账本 Pending 记账，只有当前星球的工厂参与 tick，
    /// 目标星球未加载时能量留在账本里，等那边加载后再落地。
    /// </summary>
    public static class HubManager
    {
        private static readonly Dictionary<int, Dictionary<int, HubInfo>> _byPlanet =
            new Dictionary<int, Dictionary<int, HubInfo>>();

        /// <summary>
        /// 接收端 key（<see cref="Key"/>(星球, 实体)）→ 这台接收端专属的「在途」能量（J）。
        ///
        /// 1.3.7 从「按星球记账」改成「按接收端记账」：
        /// 以前一颗星球只有一个桶，同星球上**没有连接**的接收端也会从这个桶里吸能量，
        /// 玩家看到的就是「我没点连接，它自己在收能量、池子在涨」。
        /// 同星球两台接收端还会重复吸同一桶（凭空多出一份能量）。
        /// </summary>
        private static readonly Dictionary<long, long> _pending = new Dictionary<long, long>();

        private static PlanetFactory _lastFactory;
        private static int _lastPlanetId = -1;
        private static float _scanTimer;
        private static float _acc;

        public static event Action OnHubsChanged;

        // ---------------- 访问器 ----------------

        public static Dictionary<long, long> Pending => _pending;

        public static IEnumerable<int> KnownPlanets => _byPlanet.Keys;

        public static HubInfo Get(int planetId, int entityId)
        {
            Dictionary<int, HubInfo> d;
            if (!_byPlanet.TryGetValue(planetId, out d))
                return null;
            HubInfo info;
            return d.TryGetValue(entityId, out info) ? info : null;
        }

        public static Dictionary<int, HubInfo> GetPlanet(int planetId)
        {
            Dictionary<int, HubInfo> d;
            return _byPlanet.TryGetValue(planetId, out d) ? d : null;
        }

        /// <summary>某台接收端当前的在途能量。</summary>
        public static long GetPending(int planetId, int entityId)
        {
            long v;
            return _pending.TryGetValue(Key(planetId, entityId), out v) ? v : 0;
        }

        /// <summary>整颗星球上所有接收端的在途能量合计（只用于面板汇总显示）。</summary>
        public static long GetPlanetPending(int planetId)
        {
            long sum = 0;
            foreach (var kv in _pending)
            {
                if ((int)(kv.Key >> 32) == planetId)
                    sum += kv.Value;
            }
            return sum;
        }

        // ---------------- 状态维护 ----------------

        /// <summary>扫描当前工厂，把新出现的枢纽实体补进表里。</summary>
        public static void Scan(PlanetFactory factory, int planetId, bool force = false)
        {
            if (factory == null)
                return;
            if (!force && factory == _lastFactory && planetId == _lastPlanetId)
                return;
            _lastFactory = factory;
            _lastPlanetId = planetId;

            Dictionary<int, HubInfo> d;
            if (!_byPlanet.TryGetValue(planetId, out d))
            {
                d = new Dictionary<int, HubInfo>();
                _byPlanet[planetId] = d;
            }

            EntityData[] pool = factory.entityPool;
            int cursor = factory.entityCursor;
            // 工厂刚创建 / 还在铺实体时 cursor 可能是 0~1。这时候做「已拆除」判定会把
            // 这颗星球上所有枢纽一次性判死，连带 DropPeersOf 把对着它的对接关系全清掉
            // （远程星球刚加载时最容易踩）。等实体表铺好再扫。
            if (pool == null || cursor <= 1)
                return;
            for (int i = 1; i < cursor && i < pool.Length; i++)
            {
                EntityData ed = pool[i];
                if (ed.id != i || ed.protoId != (short)C.ItemId)
                    continue;

                HubInfo info;
                if (!d.TryGetValue(i, out info))
                {
                    d[i] = new HubInfo { entityId = i, planetId = planetId, pos = ed.pos, hasPos = true };
                    continue;
                }

                // 实体 id 被回收复用：位置对不上 → 这已经不是原来那台枢纽了。
                // 把工作模式和对接关系统统清掉，否则新建筑会「继承」旧连接，
                // 玩家看到的就是「我什么都没点，它自己就连上发送端了」。
                if (!info.hasPos)
                {
                    info.pos = ed.pos;
                    info.hasPos = true;
                }
                else if ((info.pos - ed.pos).sqrMagnitude > 1f)
                {
                    Plugin.Log.LogInfo("枢纽 #" + i + "（星球 " + planetId +
                                       "）位置变了，原记录来自另一台已拆除的建筑（实体 id 被复用）：" +
                                       "已重置为待机、并清掉旧的对接关系");
                    info.mode = (int)HubMode.Idle;
                    // 发送端的「谁连着我」是每帧从接收端反推的，这里清掉对端即可，不用额外通知
                    info.peerPlanetId = -1;
                    info.peerEntityId = 0;
                    info.linked = false;
                    info.peerValid = false;
                    info.peerUnloaded = false;
                    info.pos = ed.pos;
                    _pending.Remove(Key(planetId, i));
                }
            }

            // 清掉已被拆除的
            List<int> dead = null;
            foreach (var kv in d)
            {
                int id = kv.Key;
                if (id >= pool.Length || pool[id].id != id || pool[id].protoId != (short)C.ItemId)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(id);
                }
            }
            if (dead != null)
            {
                foreach (int id in dead)
                {
                    d.Remove(id);
                    DropPeersOf(planetId, id);
                    // 拆掉的接收端不能把在途能量留在账本里：以后再建一台拿到同一个 id 的建筑，
                    // 一接通就会凭空多出一大笔能量。
                    _pending.Remove(Key(planetId, id));
                }
            }
            if (OnHubsChanged != null) OnHubsChanged();
        }

        // ---------------- 联机同步（1.14.0）----------------
        //
        // 口径：主机（host）独占结算，客户端只显示。
        // 原因：Nebula 的客户端也会本地跑自己星球的 factory，如果两边都执行
        // 充放电 / 扣料 / 升级，物品与能量会各算一遍，很快对不上（还会触发 desync 警告）。
        //
        // 同步内容只走一个包（HubStatePacket.Data，二进制），每 0.5 秒一次：
        //   每台枢纽 34 字节，100 台也才 3.4 KB，占用可以忽略。
        // 包里带的是「结果」而不是「操作」：
        //   模式 / 等级 / 池储量 / 升级进度 / 对接关系 / 累计收发 / 特效开关。
        // 客户端拿到后直接覆盖本地 HubInfo，再用它刷面板与特效。

        /// <summary>主机：把所有枢纽状态写进一条二进制流（Count + N 条记录）。</summary>
        public static void CollectStates(BinaryWriter w)
        {
            List<HubInfo> list = new List<HubInfo>(32);
            foreach (var pkv in _byPlanet)
            {
                if (pkv.Value == null)
                    continue;
                foreach (var kv in pkv.Value)
                {
                    if (kv.Value != null)
                        list.Add(kv.Value);
                }
            }

            w.Write(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                HubInfo info = list[i];
                w.Write(info.planetId);
                w.Write(info.entityId);
                w.Write((byte)info.mode);
                w.Write((byte)Mathf.Clamp(info.level, 1, C.MaxPoolLevel));

                long pool = 0;
                PowerExchangerComponent exc;
                if (TryGetExc(info.planetId, info.entityId, out exc))
                    pool = exc.currPoolEnergy;

                w.Write(pool);
                w.Write(info.Capacity);
                w.Write(info.upAcc);
                w.Write(info.upMat);
                w.Write(info.peerPlanetId);
                w.Write(info.peerEntityId);
                w.Write(info.totalSent);
                w.Write(info.totalReceived);

                byte flags = 0;
                if (info.linked) flags |= 1;
                if (info.boostActive) flags |= 2;
                if (info.itemStarved) flags |= 4;
                if (info.fxTarget > 0.5f) flags |= 8;
                w.Write(flags);
            }
        }

        /// <summary>客户端：把主机广播的状态覆盖到本地 HubInfo / exchange 组件上。</summary>
        public static void ApplyStates(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 4096)
                return;

            for (int i = 0; i < n; i++)
            {
                int pid = r.ReadInt32();
                int eid = r.ReadInt32();
                int mode = r.ReadByte();
                int level = r.ReadByte();
                long pool = r.ReadInt64();
                long cap = r.ReadInt64();
                long upAcc = r.ReadInt64();
                long upMat = r.ReadInt64();
                int peerPlanet = r.ReadInt32();
                int peerEntity = r.ReadInt32();
                long sent = r.ReadInt64();
                long recv = r.ReadInt64();
                int flags = r.ReadByte();

                HubInfo info = Get(pid, eid);
                if (info == null)
                    continue;   // 客户端还没发现这台（通常是远端星球，本来也不显示）

                if (mode >= 0 && mode <= 3)
                    info.mode = mode;
                if (level >= 1 && level <= C.MaxPoolLevel)
                    info.level = level;

                info.upAcc = upAcc;
                info.upMat = upMat;
                info.peerPlanetId = peerPlanet;
                info.peerEntityId = peerEntity;
                info.totalSent = sent;
                info.totalReceived = recv;

                info.linked = (flags & 1) != 0;
                info.boostActive = (flags & 2) != 0;
                info.itemStarved = (flags & 4) != 0;
                info.fxTarget = (flags & 8) != 0 ? 1f : 0f;

                // 发送端 / 接收端的对接计数由主机口径反推，别再让本地 RebuildLinks 覆盖
                if ((HubMode)info.mode == HubMode.Send)
                    info.receiverCount = info.linked ? 1 : 0;
                else if ((HubMode)info.mode == HubMode.Receive)
                    info.senderCount = info.linked ? 1 : 0;

                // 池子储量：直接写进本地的 exchange 组件，面板读的就是它
                PowerExchangerComponent exc;
                if (TryGetExc(pid, eid, out exc))
                {
                    long clamp = Math.Min(pool, Math.Max(cap, 0));
                    exc.currPoolEnergy = Math.Max(0, clamp);
                }
            }
        }

        /// <summary>按 (星球, 实体) 找到对应的 exchange 组件（工厂没加载时返回 false）。</summary>
        private static bool TryGetExc(int planetId, int entityId, out PowerExchangerComponent exc)
        {
            exc = default(PowerExchangerComponent);
            PlanetFactory factory = FactoryOf(planetId);
            if (factory == null || factory.entityPool == null || factory.powerSystem == null)
                return false;
            if (entityId <= 0 || entityId >= factory.entityPool.Length)
                return false;

            EntityData ed = factory.entityPool[entityId];
            if (ed.id != entityId || ed.powerExcId <= 0 || ed.powerExcId >= factory.powerSystem.excPool.Length)
                return false;

            exc = factory.powerSystem.excPool[ed.powerExcId];
            return true;
        }

        /// <summary>
        /// 客户端专用 tick：只把等级同步到本地组件（容量 / 功率用于面板显示），
        /// 不做任何充放电、扣料、账本与升级。链路关系由主机的广播包维护。
        /// </summary>
        private static void ClientVisualTick()
        {
            foreach (var pkv in _byPlanet)
            {
                PlanetFactory factory = FactoryOf(pkv.Key);
                if (factory == null || factory.entityPool == null || factory.powerSystem == null)
                    continue;

                foreach (var kv in pkv.Value)
                {
                    HubInfo info = kv.Value;
                    if (info == null)
                        continue;
                    PowerExchangerComponent exc;
                    if (!TryGetExc(pkv.Key, kv.Key, out exc))
                        continue;
                    ApplyLevel(info, ref exc);
                }
            }
        }

        // ---------------- 主循环 ----------------

        public static void Tick(float dt)
        {
            if (GameMain.data == null)
                return;
            PlanetData planet = GameMain.localPlanet;
            if (planet == null || planet.factory == null)
                return;

            PlanetFactory localFactory = planet.factory;
            int localPid = planet.id;

            _scanTimer += dt;
            bool fullScan = _scanTimer >= 0.5f;
            if (fullScan)
            {
                _scanTimer = 0f;
                Scan(localFactory, localPid, true);
                // 远程星球的建筑在别处被拆掉后，也要能从对接表里消失；
                // 轮询式扫描（每 0.5 秒一台）避免每帧遍历所有工厂的实体表。
                ScanOneRemote(localPid);
            }
            else
            {
                Scan(localFactory, localPid, false);
            }

            // 先按实体表核对特效：建筑被拆的这一帧就销毁，不再等 20 帧超时（否则会看到
            // 光圈残留约 1 秒）。必须放在 hubs 为空提前返回之前，拆掉最后一台也要清。
            try { HubEffect.Prune(localFactory, localPid); }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Plugin.Log.LogWarning("特效清理异常: " + e.Message);
            }

            if (_byPlanet.Count == 0)
            {
                _sendTargets.Clear();
                return;
            }

            // 联机（1.14.0）：客户端只做显示，结算全部交给主机。
            // 放在 RebuildLinks 之前——链路关系由主机广播包维护，本地重算会把它覆盖成「未连接」。
            if (!MP.IsHost)
            {
                ClientVisualTick();
                return;
            }

            // 即使本星球电力系统暂时为空也要重算链路，否则所有对接会在这一帧被清成「未连接」
            RebuildLinks(localPid, localFactory, localFactory.powerSystem);

            Transform localRoot = planet.gameObject != null ? planet.gameObject.transform : null;

            // ⚠ 关键修复（1.3.3）：遍历**所有**有枢纽的星球，而不是只处理 localPlanet。
            //   跨星系传输时玩家通常站在「接收端」那颗星球上，发送端在另一颗星球——
            //   以前只跑 localPlanet，发送端永远不 tick，跨星球传输根本不可能发生
            //   （日志里只有 [接收] 没有 [发送]，就是这个原因）。
            foreach (var pkv in _byPlanet)
            {
                int pid = pkv.Key;
                Dictionary<int, HubInfo> hubs = pkv.Value;
                if (hubs == null || hubs.Count == 0)
                    continue;

                PlanetFactory factory = FactoryOf(pid);
                if (factory == null || factory.powerSystem == null || factory.entityPool == null)
                    continue;   // 这颗星球的工厂还没创建（没去过 / 没加载）
                PowerSystem ps = factory.powerSystem;

                bool isLocal = pid == localPid;
                Transform planetRoot = isLocal ? localRoot : null;

            foreach (var kv in hubs)
            {
                HubInfo info = kv.Value;
                int eid = kv.Key;
                if (eid >= factory.entityPool.Length)
                    continue;
                EntityData ed = factory.entityPool[eid];
                if (ed.id != eid || ed.powerExcId <= 0 || ed.powerExcId >= ps.excPool.Length)
                    continue;

                PowerExchangerComponent exc = ps.excPool[ed.powerExcId];

                // 等级 → 池容量 / 充放功率。每帧对齐（读档时原版会把它重置回 prefab 的默认值），
                // 代价只是两次比较，可以放心每帧做。
                ApplyLevel(info, ref exc);

                // 本 tick 允许搬运的能量：随等级容量增长，封顶 50 TW（见 C.TransferPerTickOf）
                long tickBudget = (long)(C.TransferPerTickOf(info.level) *
                                         Mathf.Clamp(dt * 60f, 0.2f, 3f));

                // 特效同步放到 switch 之后（要先算出这一帧的 fxTarget 才知道该不该亮）。

                // 老存档的建筑是按旧 prefab 建的（格子少），这里扩到 C.SlotCount 格，
                // 否则信息矩阵/翘曲器/透镜/蓄电器几种物品抢格子，放完一种另一种就再也进不来。
                EnsureSlotSize(GetStorage(factory, ed.storageId));

                // 传送带送来的消耗品直接进卡槽（原版交换机的皮带逻辑被我们接管了，
                // 它只会认 emptyId/fullId=0 的「任意物品」，会把东西吞进 emptyCount 里丢掉）
                PullItemsFromBelts(factory, ed.storageId, eid, exc);

                // 卡槽里的升级材料（含刚送进来的）吸进升级进度池累积。
                // 阶梯材料后期一档要 2600~3500 个蓄电器，不可能一次凑齐，只能靠这条路分次投料。
                // 每 tick 扫 84 格卡槽太浪费，节流到 0.25 秒一次；手动「投入」按钮不走这条路
                // （InvestUpgrade / Upgrade 内部会立刻吸收，判定口径不会滞后）。
                info.absorbTimer -= dt;
                if (info.absorbTimer <= 0f)
                {
                    info.absorbTimer = 0.25f;
                    AbsorbUpgradeItems(info, factory, ed.storageId);
                }

                long moved = 0;
                info.boostActive = false;   // 只有发送分支在真的提速时会重新置位

                // ---- 特效强度目标（1.3.8）----
                // 判据照抄原版 PowerSystem 给交换机充电的那段：
                //   · 只有 excPool[i].state == 1 时原版才调 InputUpdate，把**真正充进池子**的量
                //     写进 currEnergyPerTick；发电余量不够时它按 num39 比例缩水，一点电都没有时是 0。
                //   · 所以 currEnergyPerTick > 0 就等于「这台机器这一帧真的从电网拿到电了」，
                //     比看电网总量更准（电网有电但全被工厂吃掉时，这里就是 0）。
                bool gridFeeding = exc.currEnergyPerTick > 0 || exc.currPoolEnergy > 0;
                bool netLive = exc.networkId > 0 && NetHasPower(ps, exc.networkId);
                // 抖动抑制：见 C.FxHoldSec。命中「有能量在动」时由各分支把 gridHold 重新拉满。
                info.gridHold = Mathf.Max(0f, info.gridHold - dt);
                float fxTarget = 0f;
                string fxReason = "未接入电网";

                // 1.7.1：储能不再是独立模式。只有待机才清蓄电器状态；
                // 发送 / 接收模式共用 BatteryDir 的决策（storageDir/storageHold）。
                if ((HubMode)info.mode == HubMode.Idle)
                {
                    info.storageDir = 0;
                    info.storageHold = 0f;
                }

                switch ((HubMode)info.mode)
                {
                    case HubMode.Send:
                    {
                        // ---- 直连对接 ----
                        // 发送端自己不知道谁是客户端，只认「有没有接收端连到我」。
                        // 一台都没连上时不再完全待机：蓄电器功能照常（见 BatteryDir），
                        // 只是外送部分停掉。
                        bool wasLinkedS = info.loggedLink;
                        info.loggedLink = info.linked;
                        if (wasLinkedS != info.linked)
                            Plugin.Log.LogInfo("枢纽 #" + eid + (info.linked
                                ? " 对接成功：接收端 ×" + info.receiverCount + "，开始发送"
                                : " 对接断开：没有接收端连接，停止发送（蓄电器照常工作）"));

                        // 1.7.1 蓄电器并入发送模式：
                        //   · 电网缺电且池里有电 → 反向放电支援本地电网；
                        //   · 其余情况保持「从电网充电」（vanilla InputUpdate 只抽真正的富余，
                        //     不会抢工厂的用电），池子 = 一台挂在电网上的蓄电器。
                        // 1.8.2：充电功率 = 电网空闲量（发电容量 − 用电需求），不做等级限制，
                        // 空闲再多也照单全收（见 ApplyChargeCap）。
                        int dirS = BatteryDir(info, exc, ps, dt);
                        exc.state = dirS < 0 ? -1f : 1f;
                        exc.targetState = exc.state;
                        ApplyChargeCap(info, ref exc, dirS < 0 ? 0 : 1, ps);

                        // 发送端只有在「真的有能量在动」时才演特效：
                        // 充进池子（currEnergyPerTick>0）、放电入网（dirS<0）或外送成功都算。
                        if (gridFeeding || dirS != 0)
                            info.gridHold = C.FxHoldSec;
                        fxTarget = gridFeeding || dirS != 0 || info.gridHold > 0f ? 1f : 0f;
                        fxReason = exc.networkId <= 0 ? "没有接入电网"
                                 : !info.linked ? "没有接收端连接（蓄电器照常工作）"
                                 : "本星球电网没有空闲能量";

                        long avail = exc.currPoolEnergy;
                        info.itemStarved = false;

                        // 1.10.0 提速：卡槽里有三级增产剂 Mk.III 时，本帧传输额度 ×2，
                        // 每提速 1 秒扣 1 个（只在本帧真的送了能量时才扣）。
                        int boostHave = CountItem(factory, ed.storageId, C.BoostItem);
                        bool boost = boostHave > 0;
                        long sendBudget = boost ? (long)(tickBudget * C.BoostRate) : tickBudget;

                        if (info.linked && avail > 0)
                        {
                            long want = Math.Min(avail, sendBudget);
                            long delivered = DeliverToReceivers(factory, ed.storageId, info, want);
                            if (delivered > 0)
                            {
                                exc.currPoolEnergy -= delivered;
                                info.totalSent += delivered;
                                moved = delivered;

                                if (boost)
                                    ConsumeBoost(factory, ed.storageId, info, boostHave, dt);
                            }
                        }
                        info.boostActive = boost && moved > 0;
                        if (!boost)
                            info.boostAcc = 0f;

                        // 诊断：每 2 秒一条，定位能量断在哪一环（池子没充上 / 没投出去 / 没消耗品）
                        info.diagTimer -= dt;
                        if (info.diagTimer <= 0f)
                        {
                            info.diagTimer = 2f;
                            long tgPending = -1;
                            string tgInfo = "无";
                            List<long> tg;
                            if (_sendTargets.TryGetValue(Key(info.planetId, info.entityId), out tg) && tg != null && tg.Count > 0)
                            {
                                long k0 = tg[0];
                                int rp0 = (int)(k0 >> 32);
                                tgPending = GetPending(rp0, (int)(uint)(k0 & 0xFFFFFFFFL));
                                tgInfo = "";
                                for (int i = 0; i < tg.Count; i++)
                                {
                                    int rp = (int)(tg[i] >> 32);
                                    int re = (int)(uint)(tg[i] & 0xFFFFFFFFL);
                                    PlanetFactory rf = FactoryOf(rp);
                                    if (i > 0) tgInfo += ",";
                                    tgInfo += "#" + re + "@" + rp + (rf == null ? "(未加载)" : "(已加载)");
                                }
                            }
                            Plugin.Log.LogInfo("[发送] #" + eid +
                                               " 星球=" + pid +
                                               " 池=" + exc.currPoolEnergy +
                                               " 单tick上限=" + tickBudget +
                                               " 实投=" + moved +
                                               " 矩阵=" + CountItem(factory, ed.storageId, Items.InformationMatrix) +
                                               " 提速剂=" + CountItem(factory, ed.storageId, C.BoostItem) +
                                               (info.boostActive ? "（提速中 ×" + C.BoostRate + "）" : "") +
                                               " 缺料=" + (info.itemStarved ? "是" : "否") +
                                               " 蓄电器=" + (info.storageDir > 0 ? "充" : info.storageDir < 0 ? "放" : "停") +
                                               " 电网空闲=" + GridIdleOf(ps, exc) + "J/tick（" +
                                               GridIdleOf(ps, exc) * 60L + "W）" +
                                               " 接收端=" + tgInfo +
                                               " 对端在途=" + tgPending);
                        }
                        break;
                    }

                    case HubMode.Receive:
                    {
                        // 接收端是「客户端」：连接关系由它自己发起（见 Connect），
                        // 这里只负责把账本里属于**这台**接收端的能量灌进池子，由 exchanger 放电入网。
                        info.senderCount = info.linked ? 1 : 0;
                        info.receiverCount = 0;

                        // 1.7.1 蓄电器并入接收模式：
                        //   · 电网有余电且池子没满 → 顺便充一点（把富余存起来）；
                        //   · 其余时间保持「放电入网」。
                        // 判据 energyCapacity − energyRequired 不含交换站自身的放电上限
                        // （见 BatteryDir 注释），自己放电不会把需求抬高，不会抖。
                        int dirR = BatteryDir(info, exc, ps, dt);
                        exc.state = dirR > 0 ? 1f : -1f;
                        exc.targetState = exc.state;
                        ApplyChargeCap(info, ref exc, dirR > 0 ? 1 : 0, ps);

                        // ---- 1.11.0 按需自动分配：把「我现在能吃多少」上报给发送端 ----
                        // 口径：池子空余按「满功率放电 × C.DemandSeconds 秒」折算（避免大池子独占份额），
                        // 再加本地电网当前缺口这一秒要烧掉的量（缺口会立刻把刚到的能量消耗掉）。
                        // 两者相加仍不超过池子空余——装不下的需求对发送端没有意义。
                        {
                            long roomNow = info.Capacity - exc.currPoolEnergy;
                            if (roomNow < 0) roomNow = 0;
                            long perSec = C.ExchangePerTickOf(info.level) * 60L;
                            long burst = (long)Math.Min((double)roomNow, perSec * C.DemandSeconds);
                            long gapSec = 0;
                            if (exc.networkId > 0 && exc.networkId < ps.netPool.Length)
                            {
                                PowerNetwork netR = ps.netPool[exc.networkId];
                                long gap = netR.energyRequired - netR.energyCapacity;
                                if (gap > 0) gapSec = gap * 60L;
                            }
                            long rest = roomNow - burst;
                            info.demand = burst + (rest > 0 ? Math.Min(rest, gapSec) : 0);
                            info.demandFresh = true;
                            info.demandStamp = Time.time;
                        }

                        // ⚠ 1.3.7 关键修复：只有链路真的成立才允许从在途里取能量。
                        // 以前这里只看「桶里有没有」，不看 linked —— 于是没点过连接的接收端
                        // 也会把在途吸进自己的池子：面板明明写着「未连接」，电量却在涨、已收也在涨，
                        // 看起来就是「我没点连接，它自己连上了」。
                        long pendingHere = GetPending(pid, eid);
                        long pendingBefore = pendingHere;   // 扣减前留一份，用来判「还有没有能量在流」
                        if (info.linked && pendingHere > 0)
                        {
                            long room = info.Capacity - exc.currPoolEnergy;
                            // 接收侧的取料上限也放大到提速后的档位：发送端开了加速器时
                            // 在途会按 ×2 的速度堆起来，接收端如果还按 1 倍速取，账本就会淤积。
                            long takeLimit = (long)(tickBudget * C.BoostRate);
                            long take = Math.Min(Math.Min(pendingHere, room), takeLimit);
                            if (take > 0)
                            {
                                exc.currPoolEnergy += take;
                                AddPending(Key(pid, eid), -take);
                                pendingHere -= take;
                                info.totalReceived += take;
                                moved = take;
                            }
                        }

                        // 接收端的特效跟着「有没有能量真的在流」走：
                        // 链路没建、或者对端断供（没收到、没在途、池子也空）时，
                        // 蓄电器在充放（dirR != 0）也照样亮。
                        // 注意 pendingHere 是「取完之后剩下的」——正常传输时它每帧都被取空，
                        // 所以必须再加上 pendingBefore 和本帧实收的 moved，否则会误判成没在收。
                        bool receiving = moved > 0 || pendingBefore > 0 || exc.currPoolEnergy > 0;
                        if (receiving || dirR != 0)
                            info.gridHold = C.FxHoldSec;
                        fxTarget = (info.linked && (receiving || info.gridHold > 0f)) || dirR != 0 ? 1f : 0f;
                        fxReason = exc.networkId <= 0 ? "没有接入电网"
                                 : !info.linked ? "未连接发送端（蓄电器照常工作）"
                                 : "没有在途能量";

                        // 诊断：每 2 秒一条（在途能量 / 池子 / 链路有效性）
                        info.diagTimer -= dt;
                        if (info.diagTimer <= 0f)
                        {
                            info.diagTimer = 2f;
                            Plugin.Log.LogInfo("[接收] #" + eid +
                                               " 池=" + exc.currPoolEnergy + "/" + info.Capacity +
                                               " 在途=" + pendingHere +
                                               " 链路=" + (info.linked ? "有效"
                                                        : info.peerValid ? "对端已选但本机未通电" : "未连接") +
                                               " 已收=" + info.totalReceived);
                        }
                        break;
                    }

                    // 1.7.1：Store 不再是可选模式（蓄电器并入发送/接收）。
                    // 老存档在 Import 时已把 3 转成 Idle；万一仍有残留值，
                    // 与 default 一样按待机处理。
                    case HubMode.Store:
                    default:
                        exc.state = 0f;
                        exc.targetState = 0f;
                        info.linked = false;
                        info.receiverCount = 0;
                        info.senderCount = 0;
                        info.storageDir = 0;
                        info.storageHold = 0f;

                        // 待机：没有电源输入就彻底静下来。
                        // 「有输入」= 真的接了电网（networkId>0）并且那个电网确实有电
                        // （发电能力 / 电池存量 / 正在供出的电 三者有一个 >0 才算）。
                        // 只是接了电缆、但电网里一台发电机都没有的情况，按「没电」处理。
                        // 这里不用 gridHold：断电是明确的状态变化，玩家希望它当场停下；
                        // 抖动抑制只给「逐帧争抢发电余量」的发送端用。
                        info.gridHold = 0f;
                        fxTarget = netLive ? 1f : 0f;
                        fxReason = exc.networkId > 0 ? "电网里没有电" : "没有接入电网";
                        break;
                }

                ps.excPool[ed.powerExcId] = exc;

                // ---- 特效强度：参考原版 StateUpdate 的斜坡，不硬开关 ----
                info.fxTarget = fxTarget;
                if (fxTarget > 0.5f)
                    info.fxReason = "";
                else
                    info.fxReason = fxReason;
                float fxStep = (fxTarget > info.fxLevel ? C.FxRisePerSec : C.FxFallPerSec) * dt;
                info.fxLevel = Mathf.MoveTowards(info.fxLevel, fxTarget, fxStep);

                if (planetRoot != null)
                {
                    try
                    {
                        // alive = 这台机器还值得留一个特效根节点。
                        // 待机 + 完全没电 + 已经淡到 0 时直接销毁，避免场上一堆空节点。
                        bool alive = exc.networkId > 0 || info.mode != (int)HubMode.Idle || info.fxLevel > 0.01f;
                        HubEffect.Sync(pid, planetRoot, eid, ed.pos, ed.rot,
                            (HubMode)info.mode, alive, info.ratePerSecond, info.fxLevel);
                    }
                    catch (Exception e)
                    {
                        if (Time.frameCount % 600 == 0)
                            Plugin.Log.LogWarning("特效同步异常: " + e.Message);
                    }
                }

                // 只在「亮/灭」翻转时打一条，方便排查「为什么这台机器没特效」
                bool fxOn = info.fxTarget > 0.5f;
                if (!info.fxLoggedOnce || fxOn != info.fxLoggedOn)
                {
                    info.fxLoggedOnce = true;
                    info.fxLoggedOn = fxOn;
                    string modeName = info.mode == (int)HubMode.Send ? "发送"
                                    : info.mode == (int)HubMode.Receive ? "接收" : "待机";
                    Plugin.Log.LogInfo("枢纽 #" + eid + "（" + modeName + "）特效" +
                                       (fxOn ? "启动" : "停止：" + info.fxReason));
                }

                info.lastTickEnergy = moved;
                float targetRate = moved * 60f;
                info.ratePerSecond = Mathf.Lerp(info.ratePerSecond, targetRate, 0.12f);
            }

            }   // foreach (有枢纽的星球)
        }

        // ---------------- 星球 / 工厂查找 ----------------

        /// <summary>
        /// 某个电网「有没有电」。
        ///
        /// 只看 networkId &gt; 0 是不够的：拉了几根电缆但电网里一台发电机都没有时它也算「接入了电网」。
        /// 判据照原版 PowerSystem 结算出来的三个量：
        ///   · energyCapacity —— 这一帧可持续供出的总功率（发电机出力，不含电池）
        ///   · energyStored   —— 电网里电池的总存量
        ///   · energyServed   —— 这一帧真的供出去的电
        /// 三者全为 0 就是「电网空转」，此时待机特效应该停。
        /// </summary>
        private static bool NetHasPower(PowerSystem ps, int networkId)
        {
            if (ps == null || ps.netPool == null)
                return false;
            if (networkId <= 0 || networkId >= ps.netPool.Length)
                return false;
            PowerNetwork n = ps.netPool[networkId];
            if (n == null)
                return false;
            return n.energyCapacity > 0 || n.energyStored > 0 || n.energyServed > 0;
        }

        // ---------------- 能量池等级（1.7.0） ----------------

        /// <summary>
        /// 把等级换算出来的「池容量 / 充放功率」写回 exchanger。
        ///
        /// 为什么要每帧对齐：<c>PowerExchangerComponent.Import</c>（读档）会无条件把
        /// maxPoolEnergy / energyPerTick 重置成 prefab 里的默认值（PowerSystem.cs:329~330），
        /// 我们改过的值会被覆盖掉。每帧比较一次、不一致才写，开销就是两个 long 比较。
        /// 容量调小的话（理论上不会，升级只增不减）顺手把池电量夹回去，避免超容量。
        /// </summary>
        public static void ApplyLevel(HubInfo info, ref PowerExchangerComponent exc)
        {
            if (info == null)
                return;
            if (info.level < 1) info.level = 1;
            if (info.level > C.MaxPoolLevel) info.level = C.MaxPoolLevel;

            long cap = C.PoolOf(info.level);
            if (exc.maxPoolEnergy != cap)
                exc.maxPoolEnergy = cap;
            if (exc.currPoolEnergy > cap)
                exc.currPoolEnergy = cap;

            long rate = C.ExchangePerTickOf(info.level);
            if (exc.energyPerTick != rate)
                exc.energyPerTick = rate;
        }

        /// <summary>
        /// 升一级：材料按阶梯需求扣（见 <see cref="C.UpgradeCostOf"/> / <see cref="C.UpgradeCost2Of"/>），
        /// 等级 +1 并立刻应用新容量。
        ///
        /// 1.13.0 起材料分两个池子：
        ///   1) 升级进度池（<see cref="HubInfo.upAcc"/> / <see cref="HubInfo.upMat"/>）——
        ///      卡槽里、以及传送带送进卡槽的升级材料，每 tick 自动吸进这里累积；
        ///   2) 玩家背包——升级时直接扣，不需要搬进卡槽。
        /// 这样后期一档 3500 个蓄电器也能分很多次送料，不必一次凑齐（蓄电器堆叠小，根本凑不齐）。
        /// 返回 true = 升级成功。
        /// </summary>
        public static bool Upgrade(HubInfo info, PlanetFactory factory, int storageId, out string msg)
        {
            msg = "";
            if (info == null)
                return false;
            if (info.level >= C.MaxPoolLevel)
            {
                msg = "已经是满级（" + C.MaxPoolLevel + " 级）";
                return false;
            }

            StorageComponent stg = GetStorage(factory, storageId);
            if (stg == null)
            {
                msg = "读不到卡槽";
                return false;
            }

            int accId = C.UpgradeItem;
            int matId = Items.GravitonMatrix;
            int needAcc = info.UpgradeNeed;
            int needMat = info.UpgradeNeed2;

            // 卡槽里（含刚由传送带送进来的）材料先并入进度池，保证判定与扣除口径一致
            AbsorbUpgradeItems(info, factory, storageId);

            long haveAcc = info.upAcc + CountBag(accId);
            long haveMat = info.upMat + CountBag(matId);

            if (haveAcc < needAcc)
            {
                msg = "缺少" + Items.Name(accId) + "：" + haveAcc + " / " + needAcc +
                      "（已投 " + info.upAcc + " + 背包 " + CountBag(accId) + "）";
                return false;
            }
            if (haveMat < needMat)
            {
                msg = "缺少" + Items.Name(matId) + "：" + haveMat + " / " + needMat +
                      "（已投 " + info.upMat + " + 背包 " + CountBag(matId) + "）";
                return false;
            }

            ConsumeUpgradePool(info, factory, accId, needAcc);
            ConsumeUpgradePool(info, factory, matId, needMat);

            int newLevel = info.level + 1;
            info.level = newLevel;
            info.storageDir = 0;
            info.storageHold = 0f;

            Plugin.Log.LogInfo("枢纽 #" + info.entityId + "（星球 " + info.planetId + "）能量池升级到 " +
                               info.level + " 级：容量 " + info.Capacity + " J，" +
                               "功率 " + C.ExchangePerTickOf(info.level) + " J/tick，" +
                               "消耗 " + Items.Name(accId) + " ×" + needAcc +
                               " + " + Items.Name(matId) + " ×" + needMat +
                               "（下一级需 " + C.UpgradeCostOf(info.level) + " + " +
                               C.UpgradeCost2Of(info.level) + "）");

            msg = "升级成功：能量池 " + info.level + " 级，" + UI.FmtEnergy(info.Capacity);
            return true;
        }

        /// <summary>
        /// 把卡槽里的升级材料吸进进度池（每 tick 调一次，传送带送料也能自动累积）。
        /// 只吸到「当前等级需求」为止，多出来的留在卡槽里，不会凭空吞掉玩家的料。
        /// </summary>
        public static void AbsorbUpgradeItems(HubInfo info, PlanetFactory factory, int storageId)
        {
            if (info == null || factory == null || storageId <= 0)
                return;
            if (info.level >= C.MaxPoolLevel)
                return;

            AbsorbUpgradeOne(info, factory, storageId, C.UpgradeItem, info.UpgradeNeed, true);
            AbsorbUpgradeOne(info, factory, storageId, Items.GravitonMatrix, info.UpgradeNeed2, false);
        }

        private static void AbsorbUpgradeOne(HubInfo info, PlanetFactory factory, int storageId,
                                             int itemId, int need, bool isAcc)
        {
            if (itemId <= 0 || need <= 0)
                return;
            long pool = isAcc ? info.upAcc : info.upMat;
            long room = need - pool;
            if (room <= 0)
                return;

            int inSlot = CountItem(factory, storageId, itemId);
            if (inSlot <= 0)
                return;

            int take = (int)Math.Min((long)inSlot, room);
            if (take <= 0)
                return;

            ConsumeItem(factory, storageId, itemId, take);
            if (isAcc) info.upAcc += take;
            else info.upMat += take;
        }

        /// <summary>升级扣料：先扣进度池，不足的部分直接从背包扣。</summary>
        private static void ConsumeUpgradePool(HubInfo info, PlanetFactory factory, int itemId, int need)
        {
            if (need <= 0 || itemId <= 0)
                return;

            bool isAcc = itemId == C.UpgradeItem;
            long pool = isAcc ? info.upAcc : info.upMat;
            int fromPool = (int)Math.Min(pool, (long)need);
            if (fromPool > 0)
            {
                if (isAcc) info.upAcc -= fromPool;
                else info.upMat -= fromPool;
            }

            int rest = need - fromPool;
            if (rest <= 0)
                return;
            TakeBag(itemId, rest);
        }

        /// <summary>
        /// 从玩家背包「投入」升级材料（面板上升级格的「放入」按钮）。
        ///
        /// 比塞进卡槽再等自动吸收更直接：不受卡槽格数限制，投入量还受当前等级需求封顶，
        /// 多出来的留在背包里（不会把玩家的料一次吸干）。
        /// 返回实际投入的数量。
        /// </summary>
        public static int InvestUpgrade(HubInfo info, PlanetFactory factory, int storageId, int itemId, int n)
        {
            if (info == null || itemId <= 0 || n <= 0)
                return 0;
            if (info.level >= C.MaxPoolLevel)
                return 0;

            // 先把卡槽里还没吸的余料并进来，保证「已投」数是准的
            AbsorbUpgradeItems(info, factory, storageId);

            bool isAcc = itemId == C.UpgradeItem;
            int need = isAcc ? info.UpgradeNeed : info.UpgradeNeed2;
            long pool = isAcc ? info.upAcc : info.upMat;
            long room = need - pool;
            if (room <= 0)
                return 0;

            int want = (int)Math.Min((long)n, room);
            int moved = TakeBag(itemId, want);
            if (moved > 0)
            {
                if (isAcc) info.upAcc += moved;
                else info.upMat += moved;
            }
            return moved;
        }

        /// <summary>
        /// 把进度池里的材料退回玩家背包（面板上升级格的「取回」按钮，避免投进去了拿不出来）。
        ///
        /// 优先退背包而不是卡槽：卡槽里的升级材料会在下一 tick 被 <see cref="AbsorbUpgradeItems"/>
        /// 再吸回进度池，退进卡槽等于白点。背包满了才退回卡槽。
        /// 返回实际退回的数量。
        /// </summary>
        public static int RefundUpgrade(HubInfo info, PlanetFactory factory, int storageId, int itemId)
        {
            if (info == null || factory == null || itemId <= 0)
                return 0;

            bool isAcc = itemId == C.UpgradeItem;
            long pool = isAcc ? info.upAcc : info.upMat;
            if (pool <= 0)
                return 0;

            int want = (int)Math.Min(pool, int.MaxValue);
            int moved = AddToBag(itemId, want);
            if (moved < want)
            {
                int rest = want - moved;
                moved += AddItem(factory, storageId, itemId, rest);
            }
            if (moved > 0)
            {
                if (isAcc) info.upAcc -= moved;
                else info.upMat -= moved;
            }
            return moved;
        }

        /// <summary>往玩家背包加物品（读不到背包时返回 0）。</summary>
        private static int AddToBag(int itemId, int n)
        {
            if (n <= 0 || itemId <= 0)
                return 0;
            try
            {
                Player p = GameMain.mainPlayer;
                if (p == null || p.package == null)
                    return 0;
                int remain;
                return p.package.AddItem(itemId, n, 0, out remain);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>从玩家背包扣物品（读不到背包时静默失败）。</summary>
        private static int TakeBag(int itemId, int n)
        {
            if (n <= 0 || itemId <= 0)
                return 0;
            try
            {
                Player p = GameMain.mainPlayer;
                if (p == null || p.package == null)
                    return 0;
                int inc;
                return p.package.TakeItem(itemId, n, out inc);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>玩家背包里有多少个某物品（读不到背包时返回 0）。</summary>
        public static int CountBag(int itemId)
        {
            try
            {
                Player p = GameMain.mainPlayer;
                if (p == null || p.package == null || itemId <= 0)
                    return 0;
                return p.package.GetItemCount(itemId);
            }
            catch
            {
                return 0;
            }
        }


        /// <summary>按星球 id 取工厂（没去过 / 还没建的星球返回 null）。</summary>
        private static PlanetFactory FactoryOf(int planetId)
        {
            try
            {
                if (GameMain.data == null || GameMain.data.galaxy == null)
                    return null;
                PlanetData p = GameMain.data.galaxy.PlanetById(planetId);
                return p != null ? p.factory : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 轮询扫描一台「非当前星球」的枢纽（每 0.5 s 一台）。
        /// 目的：别的星球上的枢纽被拆掉后，_byPlanet 里的记录要能及时清掉，
        /// 否则发送端的 _sendTargets 会一直往一个已经不存在的接收端投递能量。
        /// </summary>
        private static void ScanOneRemote(int localPid)
        {
            List<int> keys = null;
            foreach (var pkv in _byPlanet)
            {
                if (pkv.Key == localPid)
                    continue;
                if (keys == null) keys = new List<int>();
                keys.Add(pkv.Key);
            }
            if (keys == null || keys.Count == 0)
                return;
            if (_remoteScanCursor >= keys.Count)
                _remoteScanCursor = 0;
            int pid = keys[_remoteScanCursor];
            _remoteScanCursor++;
            PlanetFactory f = FactoryOf(pid);
            if (f != null)
                Scan(f, pid, false);
        }

        private static int _remoteScanCursor;

        // ---------------- 直连对接 ----------------

        /// <summary>
        /// 每台发送端的「对端接收端清单」（key = <see cref="Key"/>(接收端星球, 接收端实体)）。
        ///
        /// 1.3.7 之前这里存的是**星球 id**，于是「点对点」实际是「点对星球」：
        /// 同一颗星球上任何一台接收端都能分到能量，哪怕它根本没连过。
        /// 现在精确到实体，才是真正的点对点。
        /// </summary>
        private static readonly Dictionary<long, List<long>> _sendTargets = new Dictionary<long, List<long>>();

        private static long Key(int planetId, int entityId)
        {
            return ((long)planetId << 32) | (uint)entityId;
        }

        /// <summary>诊断用：查某台终端当前的工作模式（-1 = 不存在）。</summary>
        public static int PeekMode(int planetId, int entityId)
        {
            HubInfo h = Get(planetId, entityId);
            return h != null ? h.mode : -1;
        }

        /// <summary>接收端点「连接」：把自己挂到某台发送端上。</summary>
        public static bool Connect(HubInfo me, int sendPlanetId, int sendEntityId)
        {
            if (me == null || me.mode != (int)HubMode.Receive)
                return false;
            HubInfo s = Get(sendPlanetId, sendEntityId);
            if (s == null || s.mode != (int)HubMode.Send)
                return false;
            // 同星球的终端也允许连：同一电网里传能没有净收益，但玩家常用两台设备在
            // 同一颗星球上验证功能，之前一刀切禁掉会让「对接」整个不可用。
            // 这里只留一条日志，不阻断。
            if (sendPlanetId == me.planetId)
                Plugin.Log.LogInfo("注意：枢纽 #" + me.entityId + " 连接的是同星球的发送终端 #" +
                                   sendEntityId + "（同一电网，仅适合测试，实际传能收益为 0）");
            me.peerPlanetId = sendPlanetId;
            me.peerEntityId = sendEntityId;
            me.peerStale = false;
            me.restored = false;
            me.peerValid = true;
            Plugin.Log.LogInfo("枢纽 #" + me.entityId + "（星球 " + me.planetId + "）已连接发送终端 " +
                               sendEntityId + "@星球 " + sendPlanetId);
            return true;
        }

        /// <summary>断开当前连接。</summary>
        public static bool Disconnect(HubInfo me)
        {
            if (me == null || me.peerPlanetId < 0)
                return false;
            long key = Key(me.planetId, me.entityId);
            long leftover;
            if (_pending.TryGetValue(key, out leftover) && leftover > 0)
            {
                _pending.Remove(key);
                Plugin.Log.LogInfo("枢纽 #" + me.entityId + " 断开，清掉本机在途 " + leftover + " J");
            }
            Plugin.Log.LogInfo("枢纽 #" + me.entityId + " 断开与终端 " + me.peerEntityId +
                               "@星球 " + me.peerPlanetId + " 的连接");
            me.peerPlanetId = -1;
            me.peerEntityId = 0;
            me.restored = false;
            me.linked = false;
            me.peerValid = false;
            me.senderCount = 0;
            return true;
        }

        /// <summary>清掉所有指向某台发送端的连接（那台机器被拆掉时调用）。</summary>
        public static void DropPeersOf(int sendPlanetId, int sendEntityId)
        {
            foreach (var pkv in _byPlanet)
            {
                foreach (var kv in pkv.Value)
                {
                    HubInfo h = kv.Value;
                    if (h.peerPlanetId == sendPlanetId && h.peerEntityId == sendEntityId)
                    {
                        h.peerPlanetId = -1;
                        h.peerEntityId = 0;
                        h.linked = false;
                        h.peerValid = false;
                        // 对端没了，这台接收端的在途也一并清掉，别让存量躺在账本里
                        _pending.Remove(Key(h.planetId, h.entityId));
                    }
                }
            }
        }

        /// <summary>
        /// 填充对接面板的行。
        ///   接收端 → 列出银河里所有「发送」终端（点一下就连上）
        ///   发送端 → 列出已经连到自己的接收端（只读，连接由对方发起）
        /// </summary>
        public static void FillPeers(HubInfo me, List<PeerRow> rows)
        {
            rows.Clear();
            if (me == null)
                return;

            PlanetData lp = GameMain.localPlanet;
            int curPid = lp != null ? lp.id : -1;
            PlanetFactory f = lp != null ? lp.factory : null;
            PowerSystem ps = f != null ? f.powerSystem : null;

            foreach (var pkv in _byPlanet)
            {
                foreach (var kv in pkv.Value)
                {
                    HubInfo other = kv.Value;
                    if (other.entityId == me.entityId && other.planetId == me.planetId)
                        continue;

                    bool asSender = me.mode == (int)HubMode.Receive && other.mode == (int)HubMode.Send;
                    bool asReceiver = me.mode == (int)HubMode.Send && other.mode == (int)HubMode.Receive &&
                                      other.peerPlanetId == me.planetId && other.peerEntityId == me.entityId;
                    if (!asSender && !asReceiver)
                        continue;

                    var row = new PeerRow
                    {
                        planetId = other.planetId,
                        entityId = other.entityId,
                        planetName = PlanetName(other.planetId),
                        samePlanet = other.planetId == curPid,
                        // 同星球也允许连接（同一电网没有净收益，但玩家需要它来验证功能）。
                        // 真正的限制放在「消耗品」上：同星球按同星系结算信息矩阵。
                        selectable = asSender,
                        connected = me.peerPlanetId == other.planetId && me.peerEntityId == other.entityId,
                        peerLinks = other.receiverCount,
                        crossStar = IsCrossStar(me.planetId, other.planetId),
                    };
                    row.online = row.samePlanet && IsPowered(f, ps, other.entityId);
                    if (row.samePlanet && ps != null && f != null && f.entityPool != null &&
                        other.entityId > 0 && other.entityId < f.entityPool.Length)
                    {
                        EntityData od = f.entityPool[other.entityId];
                        if (od.id == other.entityId && od.powerExcId > 0 && od.powerExcId < ps.excPool.Length)
                            row.peerPool = ps.excPool[od.powerExcId].currPoolEnergy;
                    }
                    row.peerTotal = asSender ? other.totalSent : other.totalReceived;

                    // 自动分配：我是发送端时，行上显示这台接收端的需求 / 本帧实得。
                    if (asReceiver)
                    {
                        row.demandValid = other.demandFresh &&
                                          (Time.time - other.demandStamp) <= C.DemandTimeout;
                        row.demand = other.demand;
                        row.granted = other.granted;
                    }
                    rows.Add(row);
                }
            }

            // 稳定排序：同星球的排前面，再按星球 id / 实体 id，避免每次刷新跳位
            rows.Sort((a, b) =>
            {
                if (a.samePlanet != b.samePlanet) return a.samePlanet ? -1 : 1;
                if (a.planetId != b.planetId) return a.planetId.CompareTo(b.planetId);
                return a.entityId.CompareTo(b.entityId);
            });
        }

        private static string PlanetName(int planetId)
        {
            try
            {
                if (GameMain.data != null && GameMain.data.galaxy != null)
                {
                    PlanetData p = GameMain.data.galaxy.PlanetById(planetId);
                    if (p != null && !string.IsNullOrEmpty(p.displayName))
                        return p.displayName;
                }
            }
            catch { }
            return "星球 #" + planetId;
        }

        /// <summary>
        /// 把一份能量按「连到我这台发送端的接收端」平均分摊，投递给各自的接收端。
        /// 每条链路单独结算消耗品：
        ///   · 同一恒星系 → 需要「信息矩阵」
        ///   · 跨恒星系   → 需要「空间翘曲器」+「黑雾引力透镜」
        /// 返回真正投递出去的能量；发送端只按这个数扣电。
        /// </summary>
        /// <summary>
        /// 蓄电器方向决策（1.7.1）：发送 / 接收模式下能量池自动充当蓄电器。
        ///
        /// 判据直接读 PowerSystem 结算出来的两个量（看 PowerSystem.cs 里
        /// energyCapacity / energyRequired 的赋值）：
        ///   energyCapacity = 发电总容量 − 交换站放电上限   ← 不包含我们自己的放电
        ///   energyRequired = 用电需求
        /// 所以 capacity − required 就是「本 tick 真正的余量」：
        ///   余量为正 → 电网有余电，充；余量为负 → 电网缺电，放。
        /// 自己放电不会把 required 抬高，不会出现「一放电就以为不缺电」的抖动。
        ///
        /// 死区 1%：电网在 0 附近抖的时候别来回切；
        /// 消抖：0 → 动要 0.25 s，充 ↔ 放互切要 0.75 s（原版交换机 state 自带
        /// 0.00557/tick 斜坡，叠加后是「平滑过渡」的观感）。
        ///
        /// 结果缓存在 info.storageDir（+1 充 / −1 放 / 0 停），调用方按自己的模式取舍：
        /// 发送端「放电优先」（缺电时支援电网），接收端「充电优先」（富余时存起来）。
        /// </summary>
        private static int BatteryDir(HubInfo info, PowerExchangerComponent exc, PowerSystem ps, float dt)
        {
            PowerNetwork net = null;
            if (exc.networkId > 0 && exc.networkId < ps.netPool.Length)
                net = ps.netPool[exc.networkId];

            long surplus = 0;
            bool hasNet = false;
            if (net != null)
            {
                surplus = net.energyCapacity - net.energyRequired;
                hasNet = net.energyCapacity > 0 || net.energyRequired > 0 || net.energyStored > 0;
            }

            long dead = 1;
            if (net != null)
            {
                long scale = Math.Max(net.energyCapacity, net.energyRequired);
                dead = Math.Max(1L, scale / 100L);
            }

            long cap = info.Capacity;
            int want;
            if (!hasNet || surplus == 0)
                want = 0;
            else if (exc.currPoolEnergy >= cap)
                want = surplus < -dead ? -1 : 0;     // 满了只能放
            else if (exc.currPoolEnergy <= 0)
                want = surplus > dead ? 1 : 0;       // 空了只能充
            else
                want = surplus > dead ? 1 : (surplus < -dead ? -1 : 0);

            if (want == info.storageDir)
            {
                info.storageHold = 0f;
            }
            else if (want == 0)
            {
                info.storageDir = 0;
                info.storageHold = 0f;
            }
            else
            {
                info.storageHold += dt;
                float need = info.storageDir == 0 ? 0.25f : 0.75f;
                if (info.storageHold >= need)
                {
                    info.storageDir = want;
                    info.storageHold = 0f;
                }
            }
            return info.storageDir;
        }

        /// <summary>
        /// 充电功率跟随电网空闲量（1.9.1：上限随等级容量增长，硬顶 1 PW）。
        /// 空闲量 = 当前发电容量 − 当前用电需求（energyCapacity 不含交换站自身放电上限，
        /// 所以 capacity − required 是真实余量）。
        /// ApplyLevel 每帧先把 energyPerTick 重置回本级功率，这里再按方向覆写：
        ///   · 充电（dir>0）：energyPerTick = min(电网空闲量, 本级功率)；
        ///   · 放电（dir=0）：保持本级功率（原版 OutputUpdate 按未满足用电量封顶）。
        /// </summary>
        private static void ApplyChargeCap(HubInfo info, ref PowerExchangerComponent exc, int dir, PowerSystem ps)
        {
            long want = C.ExchangePerTickOf(info.level);
            if (dir > 0 && exc.networkId > 0 && exc.networkId < ps.netPool.Length)
            {
                PowerNetwork net = ps.netPool[exc.networkId];
                long idle = net.energyCapacity - net.energyRequired;
                want = idle <= 0 ? 0 : Math.Min(idle, want);
            }
            if (exc.energyPerTick != want)
                exc.energyPerTick = want;
        }

        /// <summary>电网空闲量（发电容量 − 用电需求），J/tick。没接电网返回 0。</summary>
        private static long GridIdleOf(PowerSystem ps, PowerExchangerComponent exc)
        {
            if (exc.networkId <= 0 || exc.networkId >= ps.netPool.Length)
                return 0;
            PowerNetwork net = ps.netPool[exc.networkId];
            long idle = net.energyCapacity - net.energyRequired;
            return idle > 0 ? idle : 0;
        }

        /// <summary>
        /// 一台接收端在本帧分配里的中间状态。
        /// 用 struct + 复用 List，避免 60 fps × N 台每帧 new 出垃圾。
        /// </summary>
        private struct SendTarget
        {
            public long key;
            public int planet;
            public int entity;
            public bool cross;
            /// <summary>需求权重（J）：越大说明它近期越能吸收，分配时按权重切分。</summary>
            public long weight;
            /// <summary>在途账本还能装多少（J）。</summary>
            public long room;
            /// <summary>本帧已分到的量（J）。</summary>
            public long grant;
        }

        private static readonly List<SendTarget> _sendBuf = new List<SendTarget>();

        /// <summary>
        /// 把本帧的 <paramref name="amount"/> 按需分配给所有连到本机的接收端。
        ///
        /// 1.11.0 起不再「N 台平分」：
        ///   · 每台接收端在自己的 tick 里上报需求权重（池子空余 ∩ 5 秒放电能力，+ 电网缺口），
        ///     池子已经满了的端权重为 0，直接退出分配 —— 能量不会再被倒进一个装不下的账本；
        ///   · 在途账本 / 消耗品卡住的目标本轮少拿，剩余额度进入下一轮分给还有空间的端；
        ///   · 所有端都吃不下时返回 0，能量留在发送端池子里，等接收端消耗出空位再来拿。
        /// </summary>
        private static long DeliverToReceivers(PlanetFactory factory, int storageId, HubInfo info, long amount)
        {
            List<long> targets;
            if (amount <= 0 || !_sendTargets.TryGetValue(Key(info.planetId, info.entityId), out targets) ||
                targets == null || targets.Count == 0)
                return 0;

            int myStar = StarOf(info.planetId);

            // 两条链路类型各有各的消耗品，预算分开算、分开扣：
            // 同星系吃信息矩阵，跨星系吃空间翘曲器 + 透镜。
            long budgetSame = ItemBudget(factory, storageId, false);
            long budgetCross = ItemBudget(factory, storageId, true);

            _sendBuf.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                long recvKey = targets[i];
                int recvPlanet = (int)(recvKey >> 32);
                int recvEntity = (int)(uint)(recvKey & 0xFFFFFFFFL);

                // 投递前再核一次「这台接收端现在确实还认这条链路」。
                // 链表每帧重建，但这里多挡一道，能量绝不会落进没连接的终端账本。
                HubInfo ro = Get(recvPlanet, recvEntity);
                if (ro == null || ro.mode != (int)HubMode.Receive ||
                    ro.peerPlanetId != info.planetId || ro.peerEntityId != info.entityId)
                    continue;

                ro.granted = 0;
                ro.grantedPct = 0f;

                long room = C.PendingCapOf(ro.level) - GetPending(recvPlanet, recvEntity);
                if (room <= 0)
                    continue;

                bool fresh = ro.demandFresh && (Time.time - ro.demandStamp) <= C.DemandTimeout;
                long weight;
                if (fresh)
                {
                    weight = ro.demand;
                }
                else
                {
                    // 对端没上报（星球未加载 / 那台机器没在被模拟）：按它「满功率放电 5 秒」的
                    // 口径估一个权重，和在线上报的接收端同一个量纲，不会因为账本大就独占份额。
                    weight = C.ExchangePerTickOf(ro.level) * 60L * (long)C.DemandSeconds;
                    if (weight < C.EnergyPerItem) weight = C.EnergyPerItem;
                }
                if (weight > room) weight = room;
                if (weight <= 0) continue;

                SendTarget t = new SendTarget();
                t.key = recvKey;
                t.planet = recvPlanet;
                t.entity = recvEntity;
                t.cross = myStar < 0 || StarOf(recvPlanet) != myStar;
                t.weight = weight;
                t.room = room;
                _sendBuf.Add(t);
            }

            int n = _sendBuf.Count;
            if (n == 0)
                return 0;

            long left = amount;
            long delivered = 0;

            // 多轮分配：每轮按剩余权重比例切分，被账本 / 消耗品卡住的目标本轮少拿，
            // 剩下的额度进入下一轮再分。6 轮足够收敛（每轮至少淘汰一个受限目标）。
            for (int round = 0; round < 6 && left > 0; round++)
            {
                long totalWeight = 0;
                for (int i = 0; i < n; i++)
                {
                    SendTarget t = _sendBuf[i];
                    if (t.room > 0) totalWeight += t.weight;
                }
                if (totalWeight <= 0)
                    break;

                long roundLeft = left;
                long roundSent = 0;

                for (int i = 0; i < n && roundLeft > 0; i++)
                {
                    SendTarget t = _sendBuf[i];
                    if (t.room <= 0) continue;

                    long share = (long)((double)roundLeft * t.weight / totalWeight);
                    if (share <= 0) share = 1;            // 极小权重也分 1 J，避免长期饿死
                    if (share > t.room) share = t.room;
                    if (share > roundLeft) share = roundLeft;

                    long budget = t.cross ? budgetCross : budgetSame;
                    bool hasItems = budget > 0;

                    // 缺消耗品不再「硬停」：有料 = 全速并扣料；没料 = C.NoItemRate 倍速、不扣料。
                    long allow = hasItems ? Math.Min(share, budget) : (long)(share * C.NoItemRate);
                    if (allow <= 0)
                        continue;
                    if (!hasItems)
                        info.itemStarved = true;

                    long add = Math.Min(allow, t.room);
                    if (add <= 0)
                        continue;

                    if (hasItems)
                    {
                        if (t.cross) budgetCross -= add;
                        else budgetSame -= add;
                    }

                    AddPending(t.key, add);
                    if (hasItems)
                        ChargeItems(factory, storageId, info, t.cross, add);

                    t.grant += add;
                    t.room -= add;
                    _sendBuf[i] = t;

                    delivered += add;
                    roundSent += add;
                    roundLeft -= add;
                }

                if (roundSent <= 0)
                    break;                                 // 本轮一点没送出去：剩下的都卡死了，别空转
                left -= roundSent;
            }

            // 把结果写回接收端的 HubInfo，面板据此显示「需求 / 本帧实得」。
            for (int i = 0; i < n; i++)
            {
                SendTarget t = _sendBuf[i];
                HubInfo ro = Get(t.planet, t.entity);
                if (ro == null) continue;
                ro.granted = t.grant;
                ro.grantedPct = delivered > 0 ? (float)((double)t.grant / delivered) : 0f;
            }

            return delivered;
        }


        /// <summary>星球 → 恒星系下标（-1 表示查不到）。</summary>
        private static readonly Dictionary<int, int> _starOf = new Dictionary<int, int>();

        public static int StarOf(int planetId)
        {
            int v;
            if (_starOf.TryGetValue(planetId, out v))
                return v;
            v = -1;
            try
            {
                if (GameMain.data != null && GameMain.data.galaxy != null)
                {
                    PlanetData p = GameMain.data.galaxy.PlanetById(planetId);
                    if (p != null && p.star != null)
                        v = p.star.index;
                }
            }
            catch { }
            // 查不到就别缓存（可能星球数据还没加载完），下次再查
            if (v >= 0)
                _starOf[planetId] = v;
            return v;
        }

        /// <summary>两台枢纽是否跨恒星系。</summary>
        public static bool IsCrossStar(int planetA, int planetB)
        {
            int a = StarOf(planetA), b = StarOf(planetB);
            return a < 0 || b < 0 || a != b;
        }

        /// <summary>
        /// 当前链路需要哪几类消耗品：
        ///   sameStar  = 存在同恒星系链路 → 用「信息矩阵」
        ///   crossStar = 存在跨恒星系链路 → 用「空间翘曲器 + 透镜」
        /// 没有任何有效链路（待机 / 没人连我 / 我还没连上）时两者都是 false，
        /// 面板会把对应卡槽置灰禁用，避免玩家放进去了却不生效、还以为被吞了。
        ///
        /// 发送端可能同时挂着同星系和跨星系的接收端（混合），这时两类都返回 true，
        /// 和 <see cref="ChargeItems"/> 里逐台按 crossStar 结算的口径一致。
        /// </summary>
        public static void LinkUsage(HubInfo info, out bool sameStar, out bool crossStar, out bool hasLink)
        {
            sameStar = false;
            crossStar = false;
            hasLink = false;
            if (info == null)
                return;

            if (info.mode == (int)HubMode.Receive)
            {
                if (info.peerPlanetId < 0 || info.peerEntityId <= 0)
                    return;
                if (info.peerPlanetId == info.planetId)
                {
                    // 同星球链路：同一电网，按同星系结算（也要吃信息矩阵）
                    sameStar = true;
                    hasLink = true;
                    return;
                }
                if (IsCrossStar(info.planetId, info.peerPlanetId)) crossStar = true;
                else sameStar = true;
                hasLink = true;
                return;
            }

            if (info.mode != (int)HubMode.Send)
                return;

            List<long> targets;
            if (!_sendTargets.TryGetValue(Key(info.planetId, info.entityId), out targets) || targets == null)
                return;
            for (int i = 0; i < targets.Count; i++)
            {
                int pid = (int)(targets[i] >> 32);
                hasLink = true;
                if (pid == info.planetId) sameStar = true;   // 同星球链路按同星系结算
                else if (IsCrossStar(info.planetId, pid)) crossStar = true;
                else sameStar = true;
            }
        }

        /// <summary>兼容旧调用：只要链路类型。</summary>
        public static void LinkUsage(HubInfo info, out bool sameStar, out bool crossStar)
        {
            bool hasLink;
            LinkUsage(info, out sameStar, out crossStar, out hasLink);
        }

        private static bool IsPowered(PlanetFactory factory, PowerSystem ps, int entityId)
        {
            if (factory == null || ps == null || factory.entityPool == null)
                return false;
            if (entityId <= 0 || entityId >= factory.entityPool.Length)
                return false;
            EntityData ed = factory.entityPool[entityId];
            if (ed.id != entityId || ed.powerExcId <= 0 || ed.powerExcId >= ps.excPool.Length)
                return false;
            return ps.excPool[ed.powerExcId].networkId > 0;
        }

        /// <summary>
        /// 每帧重算对接表：
        ///   · 接收端存着「我连的是哪台发送端」，逐条校验对端还存在且仍是发送模式；
        ///   · 对端被拆 / 改成别的模式 → 自动断开并往日志里留一条；
        ///   · 当前星球的接收端还要求自己已接入电网，否则链路不生效（不拉能量）。
        /// </summary>
        private static void RebuildLinks(int curPlanetId, PlanetFactory factory, PowerSystem ps)
        {
            foreach (var pkv in _byPlanet)
            {
                foreach (var kv in pkv.Value)
                {
                    HubInfo h = kv.Value;
                    h.receiverCount = 0;
                    h.senderCount = 0;
                    h.linked = false;
                    h.peerValid = false;
                    h.peerUnloaded = false;
                }
            }
            _sendTargets.Clear();

            foreach (var pkv in _byPlanet)
            {
                int pid = pkv.Key;
                foreach (var kv in pkv.Value)
                {
                    HubInfo recv = kv.Value;
                    if (recv.mode != (int)HubMode.Receive || recv.peerPlanetId < 0 || recv.peerEntityId <= 0)
                        continue;

                    HubInfo send = Get(recv.peerPlanetId, recv.peerEntityId);
                    if (send == null || send.mode != (int)HubMode.Send)
                    {
                        // 对端被拆 / 被改成别的模式：链路失效，但**不抹掉玩家选的对端**。
                        // 真正被拆掉的那种由 Scan → DropPeersOf 负责清；
                        // 这里保留 peer，对方切回「发送能量」就自动恢复，玩家不用再点一次。
                        if (!recv.peerStale)
                        {
                            recv.peerStale = true;
                            Plugin.Log.LogInfo("枢纽 #" + recv.entityId + "（星球 " + recv.planetId +
                                               "）的发送终端 " + recv.peerEntityId + "@" + recv.peerPlanetId +
                                               " 当前不在「发送能量」状态，链路暂停（切回发送即恢复）");
                        }
                        continue;
                    }
                    recv.peerStale = false;

                    recv.peerValid = true;

                    // 接收端必须在**它自己那颗星球**上真的通了电，链路才生效。
                    // 以前只校验当前星球（curPlanetId），接收端在别的星球时完全不校验，
                    // 会一直往一个断电 / 没加载的接收端投递能量。
                    PlanetFactory rf = pid == curPlanetId ? factory : FactoryOf(pid);
                    PowerSystem rps = pid == curPlanetId ? ps : (rf != null ? rf.powerSystem : null);

                    // 1.3.6：星球还没加载（rf == null）时**不再断开链路**。
                    // 以前这里直接 continue → 发送端显示「没有接收端连接」、池子满着却一动不动，
                    // 跨星球链路在「接收端那颗星球没被模拟」时就是这样静默失效的。
                    // 现在改成：链路照常成立，能量记进 _pending 等那颗星球加载后再落地
                    // （_pending 有 PendingCap 上限，不会无限攒）。
                    if (rf == null || rps == null)
                    {
                        recv.peerUnloaded = true;
                    }
                    else
                    {
                        recv.peerUnloaded = false;
                        if (!IsPowered(rf, rps, recv.entityId))
                            continue;                   // 连上了，但本机没通电：链路暂不生效
                    }

                    recv.linked = true;
                    recv.senderCount = 1;
                    send.receiverCount++;
                    send.linked = true;

                    long sk = Key(send.planetId, send.entityId);
                    List<long> list;
                    if (!_sendTargets.TryGetValue(sk, out list))
                    {
                        list = new List<long>(1);
                        _sendTargets[sk] = list;
                    }
                    list.Add(Key(pid, recv.entityId));
                }
            }
        }

        /// <summary>
        /// 从接在枢纽上的「输入」传送带里取消耗品放进卡槽（信息矩阵 / 空间翘曲器 / 透镜 / 蓄电器 / 提速剂）。
        ///
        /// 一共 8 个接口：
        ///   · 槽位 0~3 —— 原版 <c>PowerExchangerComponent.belt0~3</c> 持有的四个带，
        ///     建造时由 PlanetFactory 从实体连接点 0~3 绑定；
        ///   · 槽位 4~7 —— 1.10.0 在模型背面新加的四个进口，原版组件**不认识**它们，
        ///     所以这里直接读实体连接点（<c>ReadObjectConn</c> 的 slot 4~7）拿到带 id。
        ///     实体连接池每个实体有 16 格，4~11 是空闲段（12~15 被配送器 / 多层建筑占用），安全。
        ///
        /// 关键点：先「看」带尾是什么（<see cref="CargoTraffic.GetItemIdAtRear"/>），
        /// 不是消耗品就完全不碰；卡槽放不下也完全不碰。绝不再「先取出来再放回去」——
        /// 那样塞不回去就会把玩家的产线物品吞掉，而且满仓时会一直吞。
        /// </summary>
        private static void PullItemsFromBelts(PlanetFactory factory, int storageId, int entityId, PowerExchangerComponent exc)
        {
            CargoTraffic ct = factory != null ? factory.cargoTraffic : null;
            if (ct == null)
                return;
            StorageComponent stg = GetStorage(factory, storageId);
            if (stg == null)
                return;

            bool changed = false;
            for (int slot = 0; slot < 8; slot++)
            {
                int beltId = 0;
                bool isOutput = false;
                if (slot < 4)
                {
                    switch (slot)
                    {
                        case 0: beltId = exc.belt0; isOutput = exc.isOutput0; break;
                        case 1: beltId = exc.belt1; isOutput = exc.isOutput1; break;
                        case 2: beltId = exc.belt2; isOutput = exc.isOutput2; break;
                        default: beltId = exc.belt3; isOutput = exc.isOutput3; break;
                    }
                }
                else
                {
                    // 背面四个进口：连接点 4~7，只认「往建筑里送」的方向。
                    try
                    {
                        bool outFlag;
                        int otherObjId, otherSlot;
                        factory.ReadObjectConn(entityId, slot, out outFlag, out otherObjId, out otherSlot);
                        if (otherObjId > 0 && otherObjId < factory.entityPool.Length &&
                            !outFlag && factory.entityPool[otherObjId].beltId > 0)
                        {
                            beltId = factory.entityPool[otherObjId].beltId;
                            isOutput = false;
                        }
                    }
                    catch
                    {
                        beltId = 0;
                    }
                }
                if (beltId <= 0 || isOutput)
                    continue;
                if (beltId >= ct.beltPool.Length || ct.beltPool[beltId].segPathId <= 0)
                    continue;

                // 每条带每帧最多取 2 个，避免一口气抽空
                for (int k = 0; k < 2; k++)
                {
                    int itemId;
                    try
                    {
                        itemId = ct.GetItemIdAtRear(beltId);
                    }
                    catch
                    {
                        break;
                    }
                    if (itemId <= 0)
                        break;
                    if (!IsConsumable(itemId))
                        break;                      // 带尾不是我们的消耗品：整条带不碰
                    if (RoomFor(stg, itemId) <= 0)
                        break;                      // 卡槽放不下：连取都不取

                    byte stack, inc;
                    int got = ct.TryPickItemAtRear(beltId, itemId, null, out stack, out inc);
                    if (got != itemId)
                        break;

                    int remainInc;
                    int added = stg.AddItem(itemId, stack, inc, out remainInc);
                    if (added > 0)
                        changed = true;
                    if (added < stack)
                    {
                        // 极小概率：读数量和真正塞进去之间被别的逻辑占了格子。
                        // 塞回带子；塞不回去只可能是带子也满了，这时才真的会丢，打日志方便排查。
                        if (!ct.TryInsertItemAtHead(beltId, itemId, (byte)(stack - added), inc))
                            Plugin.Log.LogWarning("卡槽已满，传送带上的 " + Items.Name(itemId) +
                                                  " 无法退回（丢弃 " + (stack - added) + " 个）");
                        break;
                    }
                }
            }

            if (changed)
                stg.NotifyStorageChange();
        }

        /// <summary>给 UI 用：卡槽还能放多少个该物品（0 = 已满）。</summary>
        public static int SlotRoom(PlanetFactory factory, int storageId, int itemId)
        {
            return RoomFor(GetStorage(factory, storageId), itemId);
        }

        /// <summary>卡槽还能再放多少个该物品（0 = 放不下了）。</summary>
        private static int RoomFor(StorageComponent stg, int itemId)        {
            if (stg == null || stg.grids == null || itemId <= 0 || itemId >= 12000)
                return 0;
            int stack = StorageComponent.itemStackCount != null &&
                        itemId < StorageComponent.itemStackCount.Length
                ? StorageComponent.itemStackCount[itemId]
                : 0;
            if (stack <= 0)
                stack = 100;

            int room = 0;
            int n = Math.Min(stg.size, stg.grids.Length);
            for (int i = 0; i < n; i++)
            {
                if (stg.grids[i].itemId == itemId)
                {
                    int cap = stg.grids[i].stackSize > 0 ? stg.grids[i].stackSize : stack;
                    if (cap > stg.grids[i].count)
                        room += cap - stg.grids[i].count;
                }
                else if (stg.grids[i].itemId == 0 &&
                         (stg.grids[i].filter == 0 || stg.grids[i].filter == itemId))
                {
                    room += stack;
                }
            }
            return room;
        }

        /// <summary>
        /// 卡槽格子数兜底：老存档里的建筑是按旧 prefab 建的（1 格），
        /// 这里把它扩到 <see cref="C.SlotCount"/> 格，否则三种消耗品抢一格根本放不下。
        /// SetSize 会保留已有物品。
        /// </summary>
        private static void EnsureSlotSize(StorageComponent stg)
        {
            if (stg == null || stg.size >= C.SlotCount)
                return;
            try
            {
                stg.SetSize(C.SlotCount);
            }
            catch (Exception e)
            {
                if (!_slotSizeWarned)
                {
                    _slotSizeWarned = true;
                    Plugin.Log.LogWarning("卡槽扩容失败（仍可用，容量偏小）: " + e.Message);
                }
            }
        }

        private static bool _slotSizeWarned;

        /// <summary>是不是本建筑认的消耗品。</summary>
        /// <summary>
        /// 提速扣料（1.10.0）：按秒累计，够 1 个就扣。
        /// 只在「本帧真的往外送了能量、且确实在提速」时调用，空转不扣。
        /// 卡槽里不够时把累计清零（避免以后再攒到 1 个就瞬间扣掉一批）。
        /// </summary>
        private static void ConsumeBoost(PlanetFactory factory, int storageId, HubInfo info, int have, float dt)
        {
            info.boostAcc += dt * C.BoostPerSec;
            int n = (int)info.boostAcc;
            if (n <= 0)
                return;
            n = Math.Min(n, have);
            if (n > 0)
                ConsumeItem(factory, storageId, C.BoostItem, n);
            info.boostAcc -= n;
            if (info.boostAcc > 0.999f)
                info.boostAcc = 0.999f;
        }

        private static bool IsConsumable(int itemId)
        {
            return itemId == ItemProto.kWarperId ||
                   itemId == Items.InformationMatrix ||
                   itemId == C.UpgradeItem ||      // 1.7.0：能量池升级耗材（空蓄电器）
                   itemId == Items.GravitonMatrix || // 1.12.0：升级耗材之二（引力矩阵）
                   itemId == C.BoostItem ||        // 1.10.0：传输提速耗材（三级增产剂 Mk.III）
                   Items.IsLens(itemId);
        }

        /// <summary>给某台接收端的在途账本加减能量（key = <see cref="Key"/>(星球, 实体)）。</summary>
        private static void AddPending(long key, long delta)
        {
            long v;
            _pending.TryGetValue(key, out v);
            v += delta;
            if (v < 0) v = 0;
            _pending[key] = v;
        }

        public static int CountItem(PlanetFactory factory, int storageId, int itemId)
        {
            if (storageId <= 0 || factory?.factoryStorage == null || itemId <= 0)
                return 0;
            StorageComponent stg = factory.factoryStorage.storagePool[storageId];
            if (stg == null || stg.grids == null)
                return 0;
            int n = 0;
            for (int i = 0; i < stg.grids.Length; i++)
            {
                if (stg.grids[i].itemId == itemId)
                    n += stg.grids[i].count;
            }
            return n;
        }

        /// <summary>透镜格当前数量（黑雾透镜 1211 / 普通透镜 1209 / 其它透镜 mod 一起算）。</summary>
        public static int CountLens(PlanetFactory factory, int storageId)
        {
            int n = 0;
            System.Collections.Generic.List<int> ids = Items.Candidates;
            for (int i = 0; i < ids.Count; i++)
                n += CountItem(factory, storageId, ids[i]);
            return n;
        }

        /// <summary>从背包往透镜格里放，返回实际放入数量（多个候选时依次填）。</summary>
        public static int AddLens(PlanetFactory factory, int storageId, int want)
        {
            if (want <= 0)
                return 0;
            int total = 0;
            System.Collections.Generic.List<int> ids = Items.Candidates;
            for (int i = 0; i < ids.Count && total < want; i++)
                total += AddItem(factory, storageId, ids[i], want - total);
            return total;
        }

        /// <summary>把透镜格里的所有透镜取回背包。</summary>
        public static int TakeLens(PlanetFactory factory, int storageId)
        {
            int total = 0;
            System.Collections.Generic.List<int> ids = Items.Candidates;
            for (int i = 0; i < ids.Count; i++)
                total += TakeItem(factory, storageId, ids[i]);
            return total;
        }

        /// <summary>扣掉 count 个透镜（优先扣 1211 黑雾透镜，其次 1209）。</summary>
        private static void ConsumeLens(PlanetFactory factory, int storageId, int count)
        {
            if (count <= 0)
                return;
            int left = count;
            System.Collections.Generic.List<int> ids = Items.Candidates;
            for (int i = 0; i < ids.Count && left > 0; i++)
            {
                int have = CountItem(factory, storageId, ids[i]);
                int take = Math.Min(have, left);
                if (take <= 0)
                    continue;
                ConsumeItem(factory, storageId, ids[i], take);
                left -= take;
            }
        }

        /// <summary>诊断用：玩家背包里有多少个该物品（放不进时打日志用）。</summary>
        public static int BagCount(int itemId)
        {
            if (itemId <= 0)
                return 0;
            try
            {
                Player player = GameMain.mainPlayer;
                if (player != null && player.package != null)
                    return player.package.GetItemCount(itemId);
            }
            catch { }
            return 0;
        }

        /// <summary>诊断用：背包 + 卡槽里各候选透镜的数量，放不进时打日志。</summary>
        public static string LensDebug(PlanetFactory factory, int storageId)
        {
            System.Collections.Generic.List<int> ids = Items.Candidates;
            var sb = new System.Text.StringBuilder();
            Player player = GameMain.mainPlayer;
            for (int i = 0; i < ids.Count; i++)
            {
                int bag = 0;
                try { if (player != null && player.package != null) bag = player.package.GetItemCount(ids[i]); }
                catch { }
                int slot = CountItem(factory, storageId, ids[i]);
                if (i > 0) sb.Append(" | ");
                sb.Append(Items.Name(ids[i])).Append('#').Append(ids[i])
                  .Append(" 背包=").Append(bag).Append(" 卡槽=").Append(slot)
                  .Append(" 余量=").Append(SlotRoom(factory, storageId, ids[i]));
            }
            return sb.ToString();
        }

        /// <summary>按现有消耗品折算这条链路最多能送多少能量。</summary>
        private static long ItemBudget(PlanetFactory factory, int storageId, bool crossStar)
        {
            if (crossStar)
            {
                int warper = CountItem(factory, storageId, ItemProto.kWarperId);
                int lens = CountLens(factory, storageId);
                int n = Math.Min(warper, lens);
                return n > 0 ? n * C.EnergyPerItem : 0;
            }
            int matrix = CountItem(factory, storageId, Items.InformationMatrix);
            return matrix > 0 ? matrix * C.EnergyPerItem : 0;
        }

        /// <summary>
        /// 扣物品。按累计小数结算，避免「每次投递不足 200 MJ 就免费」。
        /// </summary>
        private static void ChargeItems(PlanetFactory factory, int storageId, HubInfo info, bool crossStar, long energy)
        {
            if (energy <= 0)
                return;
            info.costAcc += (double)energy / C.EnergyPerItem;
            int n = (int)Math.Floor(info.costAcc);
            if (n <= 0)
                return;

            if (crossStar)
            {
                int warper = CountItem(factory, storageId, ItemProto.kWarperId);
                int lens = CountLens(factory, storageId);
                n = Math.Min(n, Math.Min(warper, lens));
                if (n > 0)
                {
                    ConsumeItem(factory, storageId, ItemProto.kWarperId, n);
                    ConsumeLens(factory, storageId, n);
                    info.costAcc -= n;
                }
                else
                {
                    info.costAcc = 0;
                }
            }
            else
            {
                int matrix = CountItem(factory, storageId, Items.InformationMatrix);
                n = Math.Min(n, matrix);
                if (n > 0)
                {
                    ConsumeItem(factory, storageId, Items.InformationMatrix, n);
                    info.costAcc -= n;
                }
                else
                {
                    info.costAcc = 0;
                }
            }
            if (info.costAcc > 0.999)
                info.costAcc = 0.999;
        }

        public static StorageComponent GetStorage(PlanetFactory factory, int storageId)
        {
            if (storageId <= 0 || factory?.factoryStorage == null)
                return null;
            StorageComponent[] pool = factory.factoryStorage.storagePool;
            if (pool == null || storageId >= pool.Length)
                return null;
            return pool[storageId];
        }

        /// <summary>从玩家背包搬最多 want 个物品进枢纽的卡槽，返回实际放入数量。</summary>
        public static int AddItem(PlanetFactory factory, int storageId, int itemId, int want)
        {
            if (want <= 0 || itemId <= 0)
                return 0;
            StorageComponent stg = GetStorage(factory, storageId);
            Player player = GameMain.mainPlayer;
            if (stg == null || player == null || player.package == null)
                return 0;

            int inc;
            int got = player.package.TakeItem(itemId, want, out inc);
            if (got <= 0)
                return 0;

            int remainInc;
            int added = stg.AddItem(itemId, got, inc, out remainInc);
            if (added < got)
            {
                // 放不下，把剩下的退回背包
                int backInc;
                player.package.AddItem(itemId, got - added, inc - remainInc, out backInc);
            }
            stg.NotifyStorageChange();
            return added;
        }

        /// <summary>把卡槽里某种物品全部取回背包，返回取出数量。</summary>
        public static int TakeItem(PlanetFactory factory, int storageId, int itemId)
        {
            StorageComponent stg = GetStorage(factory, storageId);
            Player player = GameMain.mainPlayer;
            if (stg == null || player == null || player.package == null || itemId <= 0)
                return 0;

            int total = 0;
            int remainInc;
            while (true)
            {
                int inc;
                int got = stg.TakeItem(itemId, 1, out inc);
                if (got <= 0)
                    break;
                int added = player.package.AddItem(itemId, got, inc, out remainInc);
                total += added;
                if (added < got)
                    break;
            }
            if (total > 0)
                stg.NotifyStorageChange();
            return total;
        }

        private static void ConsumeItem(PlanetFactory factory, int storageId, int itemId, long count)
        {
            if (count <= 0 || storageId <= 0 || factory?.factoryStorage == null || itemId <= 0)
                return;
            StorageComponent stg = factory.factoryStorage.storagePool[storageId];
            if (stg == null)
                return;
            int got = stg.TakeItem(itemId, (int)Math.Min(count, int.MaxValue), out _);
            if (got <= 0)
                return;
            // 1.3.9：发送端每秒扣 60 次料，每次 NotifyStorageChange 都会把 UI 的
            // onStorageChange 事件全跑一遍（打开面板时尤其明显）。
            // 1.4.0：面板没开时没必要 4 Hz 地通知（那会让卡槽格子在渲染线程上反复刷新），
            // 改成 1.5 秒一次；面板开着才用 0.25 秒，保证玩家看到的数量及时。
            stg.changed = true;
            float notifyGap = HubWindow.IsOpen ? 0.25f : 1.5f;
            if (Time.time - _stgNotifyTime >= notifyGap)
            {
                _stgNotifyTime = Time.time;
                stg.NotifyStorageChange();
            }
        }

        private static float _stgNotifyTime;

        /// <summary>
        /// 把本 mod 建筑的 GPU 实例动画数据钉死（1.4.0，修「一放料模型就一闪一闪」）。
        ///
        /// 由 <c>PowerSystem.GameTick</c> 的 Postfix 每帧调用一次，时机正好在原版写完之后、
        /// GPU 上传之前，所以覆盖一定生效：
        ///   · state / time / prepare_length → 0（原版会按池子充放电状态来回改 time，
        ///     顶点动画与「按状态调制」的分支都读它）；
        ///   · power → <see cref="C.PinAnimPower"/>（原版写的是池电量比例，我们的池子每 tick
        ///     被抽干，它就在 0 和极小值之间跳，自发光跟着跳 —— 这就是闪烁的直接来源）。
        /// 只改渲染数据，不影响发电/耗电/传输逻辑（那些读的是 excPool，不是 animPool）。
        /// </summary>
        public static void PinAnimPool(PowerSystem ps)
        {
            PlanetFactory f = ps != null ? ps.factory : null;
            if (f == null || f.entityPool == null || f.entityAnimPool == null || f.planet == null)
                return;
            // 只在玩家当前所在星球上做：远程工厂的电力 tick 跑在后台线程，
            // 那里去读 _byPlanet 会和主线程的 Tick 抢字典（而且远程也不渲染，改了没意义）。
            if (GameMain.localPlanet == null || f.planet != GameMain.localPlanet)
                return;

            Dictionary<int, HubInfo> hubs;
            if (!_byPlanet.TryGetValue(f.planet.id, out hubs) || hubs == null || hubs.Count == 0)
                return;

            AnimData[] pool = f.entityAnimPool;
            EntityData[] ents = f.entityPool;
            foreach (var kv in hubs)
            {
                int eid = kv.Key;
                if (eid <= 0 || eid >= pool.Length || eid >= ents.Length)
                    continue;
                if (ents[eid].id != eid || ents[eid].powerExcId <= 0)
                    continue;

                AnimData a = pool[eid];
                a.state = 0U;
                a.time = 0f;
                a.prepare_length = 0f;
                a.power = C.PinAnimPower;
                pool[eid] = a;
            }
        }

        // ---------------- 存档 ----------------

        public static void Export(BinaryWriter w)
        {
            w.Write(5); // 版本 5：+ 升级进度池（1.13.0 阶梯材料）；版本 4：+ 能量池等级（1.7.0）

            w.Write(_byPlanet.Count);
            foreach (var pkv in _byPlanet)
            {
                w.Write(pkv.Key);
                w.Write(pkv.Value.Count);
                foreach (var kv in pkv.Value)
                {
                    HubInfo i = kv.Value;
                    w.Write(i.entityId);
                    w.Write(i.mode);
                    w.Write(i.peerPlanetId);
                    w.Write(i.peerEntityId);
                    w.Write(i.totalSent);
                    w.Write(i.totalReceived);
                    // 位置：读档后用来核对「这个实体 id 还是不是我那台机器」。
                    // entityId 会被游戏回收复用，只靠 id 会把对接关系错挂到新建筑上。
                    w.Write(i.hasPos);
                    w.Write(i.pos.x);
                    w.Write(i.pos.y);
                    w.Write(i.pos.z);
                    w.Write(i.level);
                    // 1.13.0：已投入升级池的材料（阶梯需求下要跨多次投料累积）
                    w.Write(i.upAcc);
                    w.Write(i.upMat);
                }
            }

            w.Write(_pending.Count);
            foreach (var kv in _pending)
            {
                w.Write((int)(kv.Key >> 32));
                w.Write((int)(uint)(kv.Key & 0xFFFFFFFFL));
                w.Write(kv.Value);
            }
        }

        public static void Clear()
        {
            _byPlanet.Clear();
            _pending.Clear();
            _sendTargets.Clear();
            _starOf.Clear();
            _lastFactory = null;
            _lastPlanetId = -1;
            try { HubEffect.Clear(); } catch { }
        }

        public static void Import(BinaryReader r)
        {
            _byPlanet.Clear();
            _pending.Clear();
            _sendTargets.Clear();
            _starOf.Clear();
            _lastFactory = null;
            _lastPlanetId = -1;

            try
            {
                int ver = r.ReadInt32();
                int restored = 0;

                int planetCount = r.ReadInt32();
                for (int p = 0; p < planetCount; p++)
                {
                    int planetId = r.ReadInt32();
                    int n = r.ReadInt32();
                    var d = new Dictionary<int, HubInfo>();
                    for (int k = 0; k < n; k++)
                    {
                        var info = new HubInfo { planetId = planetId };
                        info.entityId = r.ReadInt32();
                        info.mode = r.ReadInt32();
                        if (info.mode == (int)HubMode.Store)
                        {
                            // 1.7.1：储能不再是独立模式（并入发送/接收），老档统一回待机，
                            // 玩家重新点一次「发送/接收」即可，蓄电器功能自动带着。
                            info.mode = (int)HubMode.Idle;
                        }
                        if (ver >= 2)
                        {
                            info.peerPlanetId = r.ReadInt32();
                            info.peerEntityId = r.ReadInt32();
                        }
                        else
                        {
                            // v1 存的是「目标星球」，没有对端实体，无法还原成直连，只能让玩家重新连
                            int oldTarget = r.ReadInt32();
                            if (oldTarget >= 0 && info.mode == (int)HubMode.Receive)
                                info.mode = (int)HubMode.Idle;
                        }
                        info.totalSent = r.ReadInt64();
                        info.totalReceived = r.ReadInt64();
                        if (ver >= 3)
                        {
                            info.hasPos = r.ReadBoolean();
                            float px = r.ReadSingle();
                            float py = r.ReadSingle();
                            float pz = r.ReadSingle();
                            info.pos = new Vector3(px, py, pz);
                        }
                        if (ver >= 4)
                            info.level = r.ReadInt32();
                        if (ver >= 5)
                        {
                            info.upAcc = r.ReadInt64();
                            info.upMat = r.ReadInt64();
                        }
                        if (info.level < 1) info.level = 1;
                        if (info.level > C.MaxPoolLevel) info.level = C.MaxPoolLevel;
                        d[info.entityId] = info;
                        if (info.mode == (int)HubMode.Receive && info.peerEntityId > 0)
                        {
                            info.restored = true;   // 面板会标「存档恢复」
                            restored++;
                        }
                    }
                    _byPlanet[planetId] = d;
                }

                int pc = r.ReadInt32();
                for (int k = 0; k < pc; k++)
                {
                    if (ver >= 3)
                    {
                        int pp = r.ReadInt32();
                        int pe = r.ReadInt32();
                        _pending[Key(pp, pe)] = r.ReadInt64();
                    }
                    else
                    {
                        // v2 是「按星球记账」的旧桶，没法拆到具体接收端；
                        // 直接丢掉更安全——否则读档后一笔存量会被同星球任何接收端吸走。
                        r.ReadInt32();
                        r.ReadInt64();
                    }
                }

                if (restored > 0)
                    Plugin.Log.LogInfo("已从存档恢复 " + restored + " 条对接关系（这些是上次保存时的连接，" +
                                       "不是刚点出来的；要断就在面板上点「断开」）");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("读取存档数据失败，已重置: " + e.Message);
                _byPlanet.Clear();
                _pending.Clear();
            }
        }
    }
}
