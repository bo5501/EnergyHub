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

        /// <summary>
        /// 1.19.3 起本模组只支持单机，这个标记不再会产生新记录（保留字段是为了存档兼容）。
        /// 老存档里如果带着 remote=true 的记录，进游戏后被 Scan 扫到就会转正。
        /// </summary>
        public bool remote;

        /// <summary>上次读到的池储量（工厂不在本端时用它兜底，避免重复读组件）。</summary>
        public long cachedPool;

        /// <summary>
        /// 本地操作时间戳（<c>Time.time</c>，联机客户端用）。
        /// 客户端点了「切换模式 / 连接 / 升级」之后的一小段时间里，
        /// 主机广播回来的旧快照不应该把界面上的新状态弹回去，靠它做保护窗口。
        /// </summary>
        public float localOpStamp;

        // 运行时
        public long lastTickEnergy;
        public float ratePerSecond;
        /// <summary>
        /// 物品扣费的小数累计（不足 1 个的部分留到下次）。
        /// 两条链路分开记：同星系扣信息矩阵，跨星系扣翘曲器 + 透镜。
        /// 共用一个累计器时，一台发送端同时挂着两类接收端，
        /// 先跑到的跨星系分支会把「同星系那部分能量」的账也记走，
        /// 结果扣的料和链路类型对不上（信息矩阵永远不扣 / 翘曲器被同星系消耗）。
        /// </summary>
        public double costAccSame;
        public double costAccCross;
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
        /// <summary>上一次打日志时是不是「被消耗品卡住」，用来只在状态变化时提示一次。</summary>
        public bool loggedStarve;
        /// <summary>
        /// 发送端：本帧被消耗品条件卡住了（面板显示提示用）。
        /// 1.19.2 起缺料就是硬停，不是降速 —— 这条链路要用的料一个都没有，就不许送出能量。
        /// </summary>
        public bool itemStarved;

        /// <summary>
        /// 缺料时具体缺什么（"信息矩阵" / "空间翘曲器+透镜"），面板直接展示，
        /// 免得玩家看到一个「缺耗材」却不知道往卡槽里放哪种。
        /// </summary>
        public string starveNeed;

        // ---- 发送端卡槽快照（接收端面板展示用，1.19.3）----
        // 接收端是「客户端」，卡槽在发送端身上，玩家在接收端窗口里看不到料还剩多少，
        // 只能看到「连上了但 0 W」。所以发送端每 0.2 秒把自己的卡槽存量写给所有接收端，
        // 接收端面板据此显示「有货 / 缺货 + 还有多少个」，缺货时直接提示让对方补什么。
        public int peerStockMatrix;
        public int peerStockWarper;
        public int peerStockLens;
        /// <summary>最近一次收到快照的时刻（Time.time）。0 = 从没同步过。</summary>
        public float peerStockStamp;
        /// <summary>发送端自己的快照节流计时（只对发送端有意义）。</summary>
        public float stockTimer;

        // ---- 卡槽存量（联机同步，1.19.6）----
        // 卡槽的内容是 StorageComponent 里的真实物品，Nebula 只同步「玩家用原版仓库界面
        // 拖动物品」那几种调用；本模组自己的搬运走的是没被它挂钩的重载，所以跨端完全看不到。
        // 这里把 6 种可能出现在卡槽里的物品数量写进状态包，对端用「多退少补」的方式
        // 直接改写 StorageComponent.grids（同样绕开 Nebula 的挂钩，不会来回打架）。
        public int slotMatrix;
        public int slotWarper;
        public int slotLens;
        public int slotBoost;
        public int slotAcc;
        public int slotMat;
        /// <summary>收到过至少一次联机卡槽数据；没收到过就别拿 0 去清空本地卡槽。</summary>
        public bool slotSynced;

        /// <summary>
        /// 客户端这一轮攒下的「卡槽变动量」（玩家点放入 / 取出），随状态包发给主机。
        /// 为什么发增量而不是绝对值：客户端的卡槽是主机广播下来的镜像，可能比主机晚 0.5 秒；
        /// 拿绝对值覆盖会把主机这期间消耗掉的料当成新料补回去（等于白送燃料）。
        /// </summary>
        public int dMatrix;
        public int dWarper;
        public int dLens;
        public int dBoost;
        public int dAcc;
        public int dMat;

        /// <summary>增量是否非 0（决定要不要打那 6 个 int）。</summary>
        public bool HasSlotDelta
        {
            get { return dMatrix != 0 || dWarper != 0 || dLens != 0 || dBoost != 0 || dAcc != 0 || dMat != 0; }
        }

        // ---- 特效强度（1.3.8）----
        /// <summary>
        /// 特效强度 0~1：0 = 完全不显示（连待机的光环都不转），1 = 满功率。
        ///
        /// 为什么不用「有没有电」这个布尔值：电网每帧都在抖（负载变化、电池满放），
        /// 布尔值会让特效疯狂开关。原版交换机的 state 也是每 tick 以 0.00557 逼近 target，
        /// 靠斜坡消化抖动，这里照抄这个做法。
        /// </summary>
        public float fxLevel;
        /// <summary>
        /// 主机算好的特效强度，跟着状态包一起发（1.19.6）。
        /// 客户端原本是自己拿 ratePerSecond 再推一遍，算法和主机不一样，于是两边
        /// 「池子同步了、模式同步了，但特效各亮各的」。改成直接回放这个值。
        /// </summary>
        public float netFxLevel;

        /// <summary>
        /// 主机上这台 exchanger 的充放状态（-1 放电 / 0 停 / +1 充电），随状态包发下来。
        ///
        /// 1.19.7：客户端以前一律把 state 钉成 0，只回放池子数值 —— 本地电网因此
        /// **看不到这台机器在放电**。DSP 的 <c>networkServes</c> 只在
        /// 「本帧有发电 || 有电池储能 || 有放电」时才有值，于是客户端整张电网被算成
        /// 「没有电」：靠 networkServes 决定外观的卫星配电站 / 电力塔全都不亮，
        /// 覆盖范围内的耗电建筑也被判成缺电 —— 而主机那边一切正常。
        /// </summary>
        public float netExcState;
        /// <summary>
        /// 主机这一 tick 的实际充放功率（J/tick，带符号：正=充，负=放）。
        /// 客户端拿它的绝对值当本地 <c>energyPerTick</c>，让本地结算按主机同样的
        /// 力度充放 —— 池子本身仍然每帧被主机值覆盖，只是「电网里多了一个同等大小的电源/负载」。
        /// </summary>
        public long netExcPerTick;
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
    /// 星际能量枢纽塔的全局管理器。
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
        /// <summary>退到主菜单后已经清过一次运行时状态（避免每帧重复 Clear）。</summary>
        private static bool _menuCleared;
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

                // 这条记录原本是「远程占位」（从对端上报里学到、本端没加载这颗星球）。
                // 现在本端真的有工厂了，它就是本地枢纽：转正后才会参与结算与全量广播。
                if (info.remote)
                    info.remote = false;

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

        // ---------------- 主循环 ----------------

        public static void Tick(float dt)
        {
            if (GameMain.data == null)
            {
                if (!_menuCleared)
                {
                    _menuCleared = true;
                    Clear();
                }
                return;
            }
            _menuCleared = false;

            // 联机（1.19.5）：主机每 0.5 秒把全部枢纽状态广播给客户端。
            // 单机、客户端调用都是空操作（见 MP.Broadcast）。
            Broadcast(dt);

            PlanetData localPlanet = GameMain.localPlanet;
            if (localPlanet == null || localPlanet.factory == null)
            {
                return;
            }
            PlanetFactory factory = localPlanet.factory;
            int id = localPlanet.id;
            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                Scan(factory, id, force: true);
                ScanOneRemote(id);
            }
            else
            {
                Scan(factory, id);
            }
            try
            {
                HubEffect.Prune(factory, id);
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 0)
                {
                    Plugin.Log.LogWarning(("特效清理异常: " + ex.Message));
                }
            }
            if (_byPlanet.Count == 0)
            {
                _sendTargets.Clear();
                return;
            }
            RebuildLinks(id, factory, factory.powerSystem);
            Transform val = (((object)localPlanet.gameObject != null) ? localPlanet.gameObject.transform : null);
            foreach (KeyValuePair<int, Dictionary<int, HubInfo>> item in _byPlanet)
            {
                int key = item.Key;
                Dictionary<int, HubInfo> value = item.Value;
                if (value == null || value.Count == 0)
                {
                    continue;
                }
                PlanetFactory val2 = FactoryOf(key);
                if (val2 == null || val2.powerSystem == null || val2.entityPool == null)
                {
                    continue;
                }
                PowerSystem powerSystem = val2.powerSystem;
                Transform val3 = ((key == id) ? val : null);
                foreach (KeyValuePair<int, HubInfo> item2 in value)
                {
                    HubInfo value2 = item2.Value;
                    int key2 = item2.Key;
                    if (key2 >= val2.entityPool.Length)
                    {
                        continue;
                    }
                    EntityData val4 = val2.entityPool[key2];
                    if (val4.id != key2 || val4.powerExcId <= 0 || val4.powerExcId >= powerSystem.excPool.Length)
                    {
                        continue;
                    }
                    PowerExchangerComponent exc = powerSystem.excPool[val4.powerExcId];
                    ApplyLevel(value2, ref exc);

                    // 联机（1.19.5）：客户端不结算，只回放主机广播来的状态。
                    // 如果客户端也跑下面那套，同一条链路会被算两次 —— 扣两遍料、送两份能。
                    if (!MP.Authoritative)
                    {
                        float cfx;
                        string cwhy;
                        ReplayRemote(value2, ref exc, powerSystem, out cfx, out cwhy);
                        powerSystem.excPool[val4.powerExcId] = exc;

                        value2.fxTarget = cfx;
                        value2.fxReason = cfx > 0.5f ? "" : cwhy;
                        float cstep = (cfx > value2.fxLevel ? C.FxRisePerSec : C.FxFallPerSec) * dt;
                        value2.fxLevel = Mathf.MoveTowards(value2.fxLevel, cfx, cstep);
                        if ((object)val3 != null)
                        {
                            try
                            {
                                bool aliveC = exc.networkId > 0 || value2.mode != (int)HubMode.Idle || value2.fxLevel > 0.01f;
                                HubEffect.Sync(key, val3, key2, val4.pos, val4.rot,
                                    (HubMode)value2.mode, aliveC, value2.ratePerSecond, value2.fxLevel);
                            }
                            catch (Exception e)
                            {
                                if (Time.frameCount % 600 == 0)
                                    Plugin.Log.LogWarning("特效同步异常: " + e.Message);
                            }
                        }
                        continue;
                    }

                    long num = (long)((float)C.TransferPerTickOf(value2.level) * Mathf.Clamp(dt * 60f, 0.2f, 3f));
                    EnsureSlotSize(GetStorage(val2, val4.storageId));
                    PullItemsFromBelts(val2, val4.storageId, key2, exc, value2);
                    value2.absorbTimer -= dt;
                    if (value2.absorbTimer <= 0f)
                    {
                        value2.absorbTimer = 0.25f;
                        AbsorbUpgradeItems(value2, val2, val4.storageId);
                    }
                    long num2 = 0L;
                    value2.boostActive = false;
                    bool flag = exc.currEnergyPerTick > 0 || exc.currPoolEnergy > 0;
                    bool flag2 = exc.networkId > 0 && NetHasPower(powerSystem, exc.networkId);
                    value2.gridHold = Mathf.Max(0f, value2.gridHold - dt);
                    float num3 = 0f;
                    string text = "未接入电网";
                    if (value2.mode == 0)
                    {
                        value2.storageDir = 0;
                        value2.storageHold = 0f;
                    }
                    switch ((HubMode)value2.mode)
                    {
                    case HubMode.Send:
                    {
                        bool loggedLink = value2.loggedLink;
                        value2.loggedLink = value2.linked;
                        if (loggedLink != value2.linked)
                        {
                            Plugin.Log.LogInfo(("枢纽 #" + key2 + (value2.linked ? (" 对接成功：接收端 ×" + value2.receiverCount + "，开始发送") : " 对接断开：没有接收端连接，停止发送（蓄电器照常工作）")));
                        }
                        int num13 = BatteryDir(value2, exc, powerSystem, dt);
                        exc.state = ((num13 < 0) ? (-1f) : 1f);
                        exc.targetState = exc.state;
                        ApplyChargeCap(value2, ref exc, (num13 >= 0) ? 1 : 0, powerSystem);
                        if (flag || num13 != 0)
                        {
                            value2.gridHold = C.FxHoldSec;
                        }
                        num3 = ((flag || num13 != 0 || value2.gridHold > 0f) ? 1f : 0f);
                        if (exc.networkId <= 0)
                        {
                            text = "没有接入电网";
                        }
                        else
                        {
                            text = ((!value2.linked) ? "没有接收端连接（蓄电器照常工作）" : "本星球电网没有空闲能量");
                        }
                        long currPoolEnergy = exc.currPoolEnergy;
                        value2.itemStarved = false;
                        value2.starveNeed = null;
                        value2.stockTimer -= dt;
                        if (value2.stockTimer <= 0f)
                        {
                            value2.stockTimer = 0.2f;
                            PublishStock(val2, val4.storageId, value2);
                        }
                        int num14 = CountItem(val2, val4.storageId, C.BoostItem);
                        bool flag4 = num14 > 0 && HasSendFuel(val2, val4.storageId);
                        long val9 = (flag4 ? ((long)((float)num * 2f)) : num);
                        if (value2.linked && currPoolEnergy > 0)
                        {
                            long amount = Math.Min(currPoolEnergy, val9);
                            long num15 = DeliverToReceivers(val2, val4.storageId, value2, amount, num);
                            if (num15 > 0)
                            {
                                exc.currPoolEnergy -= num15;
                                value2.totalSent += num15;
                                num2 = num15;
                                if (flag4)
                                {
                                    ConsumeBoost(val2, val4.storageId, value2, num14, dt);
                                }
                            }
                        }
                        if (value2.itemStarved && num2 <= 0)
                        {
                            if (!value2.loggedStarve)
                            {
                                value2.loggedStarve = true;
                                Plugin.Log.LogInfo(("枢纽 #" + key2 + " 停止发送：卡槽里没有" + (value2.starveNeed ?? "传能耗材") + "（同星系每 200 MJ 吃 1 信息矩阵；跨星系吃 1 空间翘曲器 + 1 引力透镜） | 矩阵=" + CountItem(val2, val4.storageId, Items.InformationMatrix) + " 翘曲器=" + CountItem(val2, val4.storageId, ItemProto.kWarperId) + " " + LensDebug(val2, val4.storageId)));
                            }
                        }
                        else
                        {
                            value2.loggedStarve = false;
                        }
                        value2.boostActive = flag4 && num2 > 0;
                        if (!flag4)
                        {
                            value2.boostAcc = 0f;
                        }
                        value2.diagTimer -= dt;
                        if (!(value2.diagTimer <= 0f))
                        {
                            break;
                        }
                        value2.diagTimer = 2f;
                        long num16 = -1L;
                        string text2 = "无";
                        if (_sendTargets.TryGetValue(Key(value2.planetId, value2.entityId), out var value3) && value3 != null && value3.Count > 0)
                        {
                            long num17 = value3[0];
                            num16 = GetPending((int)(num17 >> 32), (int)(num17 & 0xFFFFFFFFu));
                            text2 = "";
                            for (int i = 0; i < value3.Count; i++)
                            {
                                int planetId = (int)(value3[i] >> 32);
                                int num18 = (int)(value3[i] & 0xFFFFFFFFu);
                                PlanetFactory val10 = FactoryOf(planetId);
                                if (i > 0)
                                {
                                    text2 += ",";
                                }
                                text2 = text2 + "#" + num18 + "@" + planetId + ((val10 == null) ? "(未加载)" : "(已加载)");
                            }
                        }
                        Plugin.Log.LogInfo(("[发送] #" + key2 + " 星球=" + key + " 池=" + exc.currPoolEnergy + " 单tick上限=" + num + " 实投=" + num2 + " 矩阵=" + CountItem(val2, val4.storageId, Items.InformationMatrix) + " 翘曲器=" + CountItem(val2, val4.storageId, ItemProto.kWarperId) + " 透镜=" + CountLens(val2, val4.storageId) + " 提速剂=" + CountItem(val2, val4.storageId, C.BoostItem) + (value2.boostActive ? ("（提速中 ×" + 2f + "）") : "") + " 缺料=" + (value2.itemStarved ? "是" : "否") + " 蓄电器=" + ((value2.storageDir > 0) ? "充" : ((value2.storageDir < 0) ? "放" : "停")) + " 电网空闲=" + GridIdleOf(powerSystem, exc) + "J/tick（" + GridIdleOf(powerSystem, exc) * 60 + "W） 接收端=" + text2 + " 对端在途=" + num16));
                        break;
                    }
                    case HubMode.Receive:
                    {
                        value2.senderCount = (value2.linked ? 1 : 0);
                        value2.receiverCount = 0;
                        int num4 = BatteryDir(value2, exc, powerSystem, dt);
                        exc.state = ((num4 > 0) ? 1f : (-1f));
                        exc.targetState = exc.state;
                        ApplyChargeCap(value2, ref exc, (num4 > 0) ? 1 : 0, powerSystem);
                        long num5 = value2.Capacity - exc.currPoolEnergy;
                        if (num5 < 0)
                        {
                            num5 = 0L;
                        }
                        long num6 = C.ExchangePerTickOf(value2.level) * 60;
                        long num7 = (long)Math.Min(num5, (double)num6 * 5.0);
                        long val5 = 0L;
                        if (exc.networkId > 0 && exc.networkId < powerSystem.netPool.Length)
                        {
                            PowerNetwork val6 = powerSystem.netPool[exc.networkId];
                            long num8 = val6.energyRequired - val6.energyCapacity;
                            if (num8 > 0)
                            {
                                val5 = num8 * 60;
                            }
                        }
                        long num9 = num5 - num7;
                        value2.demand = num7 + ((num9 > 0) ? Math.Min(num9, val5) : 0);
                        value2.demandFresh = true;
                        value2.demandStamp = Time.time;
                        long num10 = GetPending(key, key2);
                        long num11 = num10;
                        if (value2.linked && num10 > 0)
                        {
                            long val7 = value2.Capacity - exc.currPoolEnergy;
                            long val8 = (long)((float)num * 2f);
                            long num12 = Math.Min(Math.Min(num10, val7), val8);
                            if (num12 > 0)
                            {
                                exc.currPoolEnergy += num12;
                                AddPending(Key(key, key2), -num12);
                                num10 -= num12;
                                value2.totalReceived += num12;
                                num2 = num12;
                            }
                        }
                        bool flag3 = num2 > 0 || num11 > 0 || exc.currPoolEnergy > 0;
                        if (flag3 || num4 != 0)
                        {
                            value2.gridHold = C.FxHoldSec;
                        }
                        num3 = (((value2.linked && (flag3 || value2.gridHold > 0f)) || num4 != 0) ? 1f : 0f);
                        if (exc.networkId <= 0)
                        {
                            text = "没有接入电网";
                        }
                        else
                        {
                            text = ((!value2.linked) ? "未连接发送端（蓄电器照常工作）" : "没有在途能量");
                        }
                        value2.diagTimer -= dt;
                        if (value2.diagTimer <= 0f)
                        {
                            value2.diagTimer = 2f;
                            Plugin.Log.LogInfo(("[接收] #" + key2 + " 池=" + exc.currPoolEnergy + "/" + value2.Capacity + " 在途=" + num10 + " 链路=" + (value2.linked ? "有效" : (value2.peerValid ? "对端已选但本机未通电" : "未连接")) + " 已收=" + value2.totalReceived));
                        }
                        break;
                    }
                    default:
                        exc.state = 0f;
                        exc.targetState = 0f;
                        value2.linked = false;
                        value2.receiverCount = 0;
                        value2.senderCount = 0;
                        value2.storageDir = 0;
                        value2.storageHold = 0f;
                        value2.gridHold = 0f;
                        num3 = (flag2 ? 1f : 0f);
                        text = ((exc.networkId > 0) ? "电网里没有电" : "没有接入电网");
                        break;
                    }
                    powerSystem.excPool[val4.powerExcId] = exc;
                    value2.fxTarget = num3;
                    if (num3 > 0.5f)
                    {
                        value2.fxReason = "";
                    }
                    else
                    {
                        value2.fxReason = text;
                    }
                    float num19 = ((num3 > value2.fxLevel) ? C.FxRisePerSec : C.FxFallPerSec) * dt;
                    value2.fxLevel = Mathf.MoveTowards(value2.fxLevel, num3, num19);
                    if ((object)val3 != null)
                    {
                        try
                        {
                            bool powered = exc.networkId > 0 || value2.mode != 0 || value2.fxLevel > 0.01f;
                            HubEffect.Sync(key, val3, key2, val4.pos, val4.rot, (HubMode)value2.mode, powered, value2.ratePerSecond, value2.fxLevel);
                        }
                        catch (Exception ex2)
                        {
                            if (Time.frameCount % 600 == 0)
                            {
                                Plugin.Log.LogWarning(("特效同步异常: " + ex2.Message));
                            }
                        }
                    }
                    bool flag5 = value2.fxTarget > 0.5f;
                    if (!value2.fxLoggedOnce || flag5 != value2.fxLoggedOn)
                    {
                        value2.fxLoggedOnce = true;
                        value2.fxLoggedOn = flag5;
                        string text3;
                        if (value2.mode == 1)
                        {
                            text3 = "发送";
                        }
                        else
                        {
                            text3 = ((value2.mode == 2) ? "接收" : "待机");
                        }
                        Plugin.Log.LogInfo(("枢纽 #" + key2 + "（" + text3 + "）特效" + (flag5 ? "启动" : ("停止：" + value2.fxReason))));
                    }
                    value2.lastTickEnergy = num2;
                    value2.cachedPool = exc.currPoolEnergy;   // 联机广播用：主机是唯一真实来源
                    value2.netFxLevel = value2.fxLevel;       // 特效强度一起发，客户端直接回放
                    // 1.19.7：把「这台机器在电网里是电源还是负载、以多大功率」也发下去，
                    // 客户端据此参与本地电网结算（见 ApplyNetGridState）。
                    value2.netExcState = exc.state;
                    value2.netExcPerTick = exc.currEnergyPerTick;
                    float num20 = (float)num2 * 60f;
                    value2.ratePerSecond = Mathf.Lerp(value2.ratePerSecond, num20, 0.12f);
                }
            }
        }

        // ---------------- 联机同步（1.19.5，Nebula 官方 API） ----------------

        /// <summary>主机全量广播的节流计时。</summary>
        private static float _bcastTimer;

        /// <summary>
        /// 状态包的流协议版本。只要 <see cref="WriteHub"/> / <see cref="ReadHubFrom"/> 的
        /// 字段顺序变了就必须 +1：联机两端版本不一致时直接丢弃对方的包，
        /// 而不是拿旧格式去读新字段（那会读出错位的垃圾值，池子/等级瞬间变成天文数字）。
        /// v2 = 1.19.7，新增 netExcState / netExcPerTick。
        /// </summary>
        internal const int ProtocolVersion = 2;

        /// <summary>
        /// 进星球时主机可能还没加载这颗星球的工厂，快照先存着，等 <c>OnPlanetLoadFinished</c> 再落地。
        /// </summary>
        private static readonly Dictionary<int, byte[]> _pendingByPlanet = new Dictionary<int, byte[]>();

        /// <summary>把一台枢纽的可同步字段按固定顺序写进流里。读写顺序必须与 <see cref="ReadHubFrom"/> 一致。</summary>
        internal static void WriteHub(BinaryWriter w, HubInfo info)
        {
            w.Write(info.planetId);
            w.Write(info.entityId);
            w.Write(info.mode);
            w.Write(info.peerPlanetId);
            w.Write(info.peerEntityId);
            w.Write(info.level);
            w.Write(info.totalSent);
            w.Write(info.totalReceived);
            w.Write(info.upAcc);
            w.Write(info.upMat);
            w.Write(info.cachedPool);
            w.Write(info.ratePerSecond);
            w.Write(info.peerStockMatrix);
            w.Write(info.peerStockWarper);
            w.Write(info.peerStockLens);

            byte flags = 0;
            if (info.linked) flags |= 1;
            if (info.itemStarved) flags |= 2;
            if (info.peerValid) flags |= 4;
            if (info.peerUnloaded) flags |= 8;
            if (info.peerStale) flags |= 16;
            w.Write(flags);

            // 缺料种类只有两种，编号传输比字符串省事也省包
            byte need = 0;
            if (info.starveNeed == "信息矩阵") need = 1;
            else if (info.starveNeed == "翘曲器+透镜") need = 2;
            w.Write(need);

            // 1.19.6：卡槽里 6 种可能的物品 + 主机算好的特效强度
            w.Write(info.slotMatrix);
            w.Write(info.slotWarper);
            w.Write(info.slotLens);
            w.Write(info.slotBoost);
            w.Write(info.slotAcc);
            w.Write(info.slotMat);
            w.Write(info.dMatrix);
            w.Write(info.dWarper);
            w.Write(info.dLens);
            w.Write(info.dBoost);
            w.Write(info.dAcc);
            w.Write(info.dMat);
            w.Write(info.netFxLevel);
            // 1.19.7：电网参与量（state + 实际充放功率）。少了这两个，客户端整张电网会被算成没电
            w.Write(info.netExcState);
            w.Write(info.netExcPerTick);
        }

        /// <summary>按 (星球, 实体) 找记录；没有就建一条「远端占位」，等本端真的扫到这台机器再转正。</summary>
        private static HubInfo GetOrCreate(int planetId, int entityId)
        {
            Dictionary<int, HubInfo> d;
            if (!_byPlanet.TryGetValue(planetId, out d))
            {
                d = new Dictionary<int, HubInfo>();
                _byPlanet[planetId] = d;
            }
            HubInfo info;
            if (!d.TryGetValue(entityId, out info))
            {
                info = new HubInfo { entityId = entityId, planetId = planetId, remote = true };
                d[entityId] = info;
            }
            return info;
        }

        /// <summary>
        /// 打包一份快照。<paramref name="planetFilter"/> &lt; 0 表示全部星球。
        /// 返回的字节流可以直接塞进 <see cref="HubStatePacket"/>。
        /// </summary>
        internal static byte[] BuildSnapshot(int planetFilter)
        {
            List<HubInfo> list = new List<HubInfo>();
            foreach (var pkv in _byPlanet)
            {
                if (planetFilter >= 0 && pkv.Key != planetFilter)
                    continue;
                foreach (var kv in pkv.Value)
                    list.Add(kv.Value);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(ProtocolVersion);
                    w.Write(list.Count);
                    for (int i = 0; i < list.Count; i++)
                        WriteHub(w, list[i]);
                }
                return ms.ToArray();
            }
        }

        /// <summary>客户端：把刚刚操作过的那一台上报给主机（单机 / 主机端是空操作）。</summary>
        public static void ReportState(HubInfo info)
        {
            if (info == null || !MP.CanReport)
                return;
            info.localOpStamp = Time.time;

            // 上报前先读一次本端卡槽（绝对值只在主机→客户端方向用，这里顺手刷新一下本端记录）
            CaptureSlots(info);

            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(ProtocolVersion);
                    w.Write(1);
                    WriteHub(w, info);
                }
                data = ms.ToArray();
            }

            // 增量发出去就清零，避免重复应用（丢包就算了，主机那边 0.5 秒后会用绝对值把卡槽抹平）
            info.dMatrix = info.dWarper = info.dLens = info.dBoost = info.dAcc = info.dMat = 0;

            MP.Send(new HubStatePacket(1, data));
        }

        /// <summary>主机：每 0.5 秒把全部枢纽状态广播一次。</summary>
        internal static void Broadcast(float dt)
        {
            if (!MP.Active || !MP.IsHost)
                return;
            _bcastTimer -= dt;
            if (_bcastTimer > 0f)
                return;
            _bcastTimer = MP.BroadcastInterval;

            if (_byPlanet.Count == 0)
                return;

            // 广播前先从本端卡槽读一次真实存量（0.5 秒一次，代价可以忽略）
            foreach (var pkv in _byPlanet)
            {
                foreach (var kv in pkv.Value)
                    CaptureSlots(kv.Value);
            }

            byte[] data = BuildSnapshot(-1);
            if (data == null || data.Length <= 4)
                return;
            MP.Send(new HubStatePacket(0, data));
        }

        /// <summary>处理器收到的数据在这里落地。</summary>
        internal static void OnNetData(byte[] data, bool fromHost, bool snapshot)
        {
            // 主菜单 / 加载中：没有 GameMain，面板也读不到。先存着，进星球后补。
            if (GameMain.data == null)
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader r = new BinaryReader(ms))
                {
                    if (r.ReadInt32() != ProtocolVersion)
                        return;
                    int n = r.ReadInt32();
                    if (n > 0)
                    {
                        int pid = r.ReadInt32();
                        _pendingByPlanet[pid] = data;
                    }
                }
                return;
            }

            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader r = new BinaryReader(ms))
            {
                ApplyStates(r, fromHost, snapshot);
            }
        }

        /// <summary>星球加载完成：把之前暂存的快照补上。</summary>
        internal static void FlushPendingPlanet(int planetId)
        {
            byte[] data;
            if (!_pendingByPlanet.TryGetValue(planetId, out data))
                return;
            _pendingByPlanet.Remove(planetId);
            OnNetData(data, true, true);
        }

        /// <summary>
        /// 应用一批状态。
        /// <para><paramref name="fromHost"/> = true 表示这是主机发下来的（客户端回放）；</para>
        /// <para><paramref name="snapshot"/> = true 表示全量快照，false 表示客户端主动上报的单台操作。</para>
        /// </summary>
        internal static void ApplyStates(BinaryReader r, bool fromHost, bool snapshot)
        {
            int ver = r.ReadInt32();
            if (ver != ProtocolVersion)
            {
                if (Time.frameCount % 600 == 0)
                    Plugin.Log.LogWarning("[联机] 对端状态包协议版本不一致（本端 v" + ProtocolVersion +
                                          "，收到 v" + ver + "），已忽略 —— 两端请用同一个版本的模组");
                return;
            }

            int n = r.ReadInt32();
            if (n < 0 || n > 8192)
                return;

            float now = Time.time;
            for (int i = 0; i < n; i++)
            {
                int pid = r.ReadInt32();
                int eid = r.ReadInt32();
                HubInfo info = GetOrCreate(pid, eid);

                // 客户端刚点过（1.5 秒内）的那台，不接受主机快照里的旧控制字段
                bool protect = fromHost && now - info.localOpStamp < 1.5f;
                if (!ReadHubFrom(r, info, pid, eid, !protect, fromHost))
                    break;

                if (fromHost)
                {
                    // 客户端的池储量 / 充放状态直接写回 exchanger，面板、特效、本地电网都读它。
                    // 1.19.7：以前这里只改了一个栈上的结构体副本（没有写回 excPool），
                    // 真正生效的是每帧的 ReplayRemote；现在统一走 ApplyRemoteGrid。
                    ApplyRemoteGrid(info, pid, eid);

                    // 卡槽：主机说了算，直接把客户端卡槽改成和主机一模一样。
                    // 客户端刚点过（protect）就跳过，免得把刚放进去的料削回去。
                    if (!protect)
                        MirrorSlots(info);
                }
                else
                {
                    // 主机收到客户端上报：只按增量补/扣，不用绝对值覆盖 ——
                    // 客户端的卡槽是 0.5 秒前的镜像，拿它覆盖会把主机已消耗的料补回来。
                    ApplySlotDeltas(info);
                }
            }

            if (OnHubsChanged != null)
                OnHubsChanged();
        }

        /// <summary>
        /// 客户端：把主机广播下来的「池储量 + 充放状态」写回本地 exchanger。
        ///
        /// 池储量以主机为唯一真相；<c>state</c> / <c>energyPerTick</c> 则让**本地电网**
        /// 也把这台机器当成同样大小的电源（放电）或负载（充电）。
        /// 1.19.7 之前客户端恒定 state = 0，本地电网里既没有这台机器的放电、也没有它的负载，
        /// 于是整张电网被算成「没有电」：卫星配电站 / 电力塔这类靠 <c>networkServes</c>
        /// 决定外观的建筑全部变黑、覆盖范围内的耗电建筑也被判成缺电 —— 主机那边却是正常的。
        /// </summary>
        private static void ApplyRemoteGrid(HubInfo info, int planetId, int entityId)
        {
            if (info == null)
                return;
            PlanetFactory f = FactoryOf(planetId);
            if (f == null || f.entityPool == null || f.powerSystem == null)
                return;
            if (entityId <= 0 || entityId >= f.entityPool.Length)
                return;

            EntityData ed = f.entityPool[entityId];
            if (ed.id != entityId || ed.powerExcId <= 0 || ed.powerExcId >= f.powerSystem.excPool.Length)
                return;

            PowerExchangerComponent exc = f.powerSystem.excPool[ed.powerExcId];

            long cap = C.PoolOf(info.level);
            long pool = info.cachedPool;
            if (pool < 0) pool = 0;
            if (pool > cap) pool = cap;
            exc.currPoolEnergy = pool;

            ApplyNetGridState(info, ref exc);

            f.powerSystem.excPool[ed.powerExcId] = exc;

            // 诊断（10 秒一条）：确认客户端这张电网到底有没有被算成「有电」。
            // 卫星配电站 / 电力塔的外观直接吃 networkServes，这里把原版结算出来的几个量打出来，
            // 下次再出现「主机正常、客户端没电」就能一眼看出是哪一侧的问题。
            if (Time.time - _gridDiagTime > 10f)
            {
                _gridDiagTime = Time.time;
                PowerSystem ps = f.powerSystem;
                PowerNetwork net = (exc.networkId > 0 && exc.networkId < ps.netPool.Length)
                    ? ps.netPool[exc.networkId] : null;
                Plugin.Log.LogInfo("[联机] 客户端电网 枢纽#" + entityId + "@" + planetId +
                                   " 网络=#" + exc.networkId +
                                   " state=" + exc.state +
                                   " 功率=" + exc.energyPerTick + "/tick" +
                                   (net != null
                                       ? (" | 发电=" + net.energyCapacity + " 需求=" + net.energyRequired +
                                          " 已供=" + net.energyServed + " 供电率=" + net.consumerRatio.ToString("0.00"))
                                       : " | 未接入电网"));
            }
        }

        private static float _gridDiagTime;

        /// <summary>
        /// 把主机广播的充放状态套到本地 exchanger 上（不动池储量）。
        ///
        /// · state 直接给到 ±1：原版 <c>PowerSystem</c> 里只有 <c>state &gt;= 1</c> /
        ///   <c>&lt;= -1</c> 才把 exchanger 计入电网的负载 / 电源，中间的平滑值等于不参与；
        /// · energyPerTick 用主机这一 tick 的**实际**功率：主机没在动（0）时客户端也保持 0，
        ///   否则客户端会按本级满功率去抽 / 灌本地电网，把整张电网的状态带偏。
        /// </summary>
        private static void ApplyNetGridState(HubInfo info, ref PowerExchangerComponent exc)
        {
            float st = info.netExcState;
            if (st >= 0.5f)
            {
                exc.state = 1f;
                exc.targetState = 1f;
            }
            else if (st <= -0.5f)
            {
                exc.state = -1f;
                exc.targetState = -1f;
            }
            else
            {
                exc.state = 0f;
                exc.targetState = 0f;
            }

            long pt = info.netExcPerTick >= 0 ? info.netExcPerTick : -info.netExcPerTick;
            exc.energyPerTick = pt;
        }

        /// <summary>
        /// <see cref="ApplyStates"/> 用：<paramref name="pid"/>/<paramref name="eid"/> 已经读过，
        /// 这里只读后面的字段，避免重复读。
        /// </summary>
        private static bool ReadHubFrom(BinaryReader r, HubInfo info, int pid, int eid,
                                        bool overwriteControl, bool fromHost)
        {
            int mode = r.ReadInt32();
            int peerPid = r.ReadInt32();
            int peerEid = r.ReadInt32();
            int level = r.ReadInt32();
            long sent = r.ReadInt64();
            long received = r.ReadInt64();
            long upAcc = r.ReadInt64();
            long upMat = r.ReadInt64();
            long pool = r.ReadInt64();
            float rate = r.ReadSingle();
            int stMatrix = r.ReadInt32();
            int stWarper = r.ReadInt32();
            int stLens = r.ReadInt32();
            byte flags = r.ReadByte();
            byte need = r.ReadByte();
            int stMat2 = r.ReadInt32();
            int stWarper2 = r.ReadInt32();
            int stLens2 = r.ReadInt32();
            int stBoost2 = r.ReadInt32();
            int stAcc2 = r.ReadInt32();
            int stGrav2 = r.ReadInt32();
            int dMat2 = r.ReadInt32();
            int dWarper2 = r.ReadInt32();
            int dLens2 = r.ReadInt32();
            int dBoost2 = r.ReadInt32();
            int dAcc2 = r.ReadInt32();
            int dGrav2 = r.ReadInt32();
            float netFx = r.ReadSingle();
            float netExcState = r.ReadSingle();
            long netExcPerTick = r.ReadInt64();

            int prevLevel = info.level;

            // 「经济类」字段（累计收发、池储量、速率、升级进度、特效强度、卡槽绝对值）
            // 只有主机发下来的才可信；客户端上报里的这些是它本地的空壳值。
            if (fromHost)
            {
                info.totalSent = sent;
                info.totalReceived = received;
                info.upAcc = upAcc;
                info.upMat = upMat;
                info.cachedPool = Math.Max(0L, pool);
                info.ratePerSecond = rate;
                info.netFxLevel = Mathf.Clamp01(netFx);
                info.netExcState = netExcState;
                info.netExcPerTick = netExcPerTick;
                info.slotMatrix = Math.Max(0, stMat2);
                info.slotWarper = Math.Max(0, stWarper2);
                info.slotLens = Math.Max(0, stLens2);
                info.slotBoost = Math.Max(0, stBoost2);
                info.slotAcc = Math.Max(0, stAcc2);
                info.slotMat = Math.Max(0, stGrav2);
                info.slotSynced = true;
            }

            info.peerStockMatrix = stMatrix;
            info.peerStockWarper = stWarper;
            info.peerStockLens = stLens;
            info.peerStockStamp = Time.time;
            info.linked = (flags & 1) != 0;
            info.itemStarved = (flags & 2) != 0;
            info.peerValid = (flags & 4) != 0;
            info.peerUnloaded = (flags & 8) != 0;
            info.peerStale = (flags & 16) != 0;
            info.starveNeed = need == 1 ? "信息矩阵" : need == 2 ? "翘曲器+透镜" : null;
            info.level = Math.Max(1, Math.Min(C.MaxPoolLevel, level));

            if (!fromHost)
            {
                // 客户端点了「升级」：它已经从自己那份（主机广播的）进度池里扣过料了，
                // 主机这边按旧等级把同样的料补扣一次，池子才对得上。
                for (int lv = prevLevel; lv < info.level; lv++)
                {
                    info.upAcc = Math.Max(0L, info.upAcc - C.UpgradeCostOf(lv));
                    info.upMat = Math.Max(0L, info.upMat - C.UpgradeCost2Of(lv));
                }

                if (StorageIdOf(FactoryOf(pid), eid) > 0)
                {
                    // 这台机器的工厂在主机这边：按增量补/扣，绝对值不用（客户端的是 0.5 秒前的镜像）
                    info.dMatrix += dMat2;
                    info.dWarper += dWarper2;
                    info.dLens += dLens2;
                    info.dBoost += dBoost2;
                    info.dAcc += dAcc2;
                    info.dMat += dGrav2;
                }
                else
                {
                    // 工厂不在主机这边（玩家跑去主机没加载过的星球）：增量落不下去，
                    // 就采用客户端报上来的绝对值 —— 主机不结算这台机器，只是把它转发给其它客户端看。
                    info.slotMatrix = Math.Max(0, stMat2);
                    info.slotWarper = Math.Max(0, stWarper2);
                    info.slotLens = Math.Max(0, stLens2);
                    info.slotBoost = Math.Max(0, stBoost2);
                    info.slotAcc = Math.Max(0, stAcc2);
                    info.slotMat = Math.Max(0, stGrav2);
                    info.slotSynced = true;
                }
            }

            if (overwriteControl)
            {
                info.mode = mode;
                info.peerPlanetId = peerPid;
                info.peerEntityId = peerEid;
            }
            return true;
        }

        // ---------------- 卡槽内容同步（1.19.6） ----------------

        /// <summary>某台枢纽的卡槽在 <paramref name="f"/> 里的 storageId（0 = 这台机器不在本端）。</summary>
        private static int StorageIdOf(PlanetFactory f, int entityId)
        {
            if (f == null || f.entityPool == null || entityId <= 0 || entityId >= f.entityPool.Length)
                return 0;
            EntityData ed = f.entityPool[entityId];
            return ed.id == entityId ? ed.storageId : 0;
        }

        /// <summary>把本端卡槽的真实存量记进记录里（主机广播前 / 客户端上报前各调一次）。</summary>
        internal static bool CaptureSlots(HubInfo info)
        {
            if (info == null)
                return false;
            PlanetFactory f = FactoryOf(info.planetId);
            int sid = StorageIdOf(f, info.entityId);
            if (sid <= 0)
                return false;

            info.slotMatrix = CountItem(f, sid, Items.InformationMatrix);
            info.slotWarper = CountItem(f, sid, ItemProto.kWarperId);
            info.slotLens = CountLens(f, sid);
            info.slotBoost = CountItem(f, sid, C.BoostItem);
            info.slotAcc = CountItem(f, sid, C.UpgradeItem);
            info.slotMat = CountItem(f, sid, Items.GravitonMatrix);
            info.slotSynced = true;
            return true;
        }

        /// <summary>
        /// 把记录里的卡槽存量写进本地卡槽（多退少补）。
        ///
        /// 为什么不直接调 <c>storage.AddItem(6 参数)</c> / <c>TakeItemFromGrid</c>：
        /// Nebula 的 <c>StorageComponent_Patch</c> 只挂在那两个重载上（它用来同步玩家
        /// 在原版仓库界面里的拖动）。本模组走的是 <c>AddItem(itemId,count,inc,out remain)</c>
        /// 和 <c>TakeItem(itemId,count,out inc)</c>，正好没被挂钩 —— 既不会把
        /// 「镜像」当成玩家操作再往对端发一遍（那样会来回叠加），也不需要额外加锁。
        /// </summary>
        internal static void MirrorSlots(HubInfo info)
        {
            if (info == null || !info.slotSynced)
                return;
            PlanetFactory f = FactoryOf(info.planetId);
            int sid = StorageIdOf(f, info.entityId);
            if (sid <= 0)
                return;
            StorageComponent stg = GetStorage(f, sid);
            if (stg == null)
                return;
            EnsureSlotSize(stg);

            bool dirty = false;
            dirty |= SetExactItem(f, sid, Items.InformationMatrix, info.slotMatrix);
            dirty |= SetExactItem(f, sid, ItemProto.kWarperId, info.slotWarper);
            dirty |= SetExactItem(f, sid, C.BoostItem, info.slotBoost);
            dirty |= SetExactItem(f, sid, C.UpgradeItem, info.slotAcc);
            dirty |= SetExactItem(f, sid, Items.GravitonMatrix, info.slotMat);
            dirty |= SetExactLens(f, sid, info.slotLens);
            if (dirty)
            {
                stg.changed = true;
                stg.NotifyStorageChange();
            }
        }

        /// <summary>让卡槽里某种物品正好是 <paramref name="want"/> 个，返回是否动过。</summary>
        private static bool SetExactItem(PlanetFactory f, int storageId, int itemId, int want)
        {
            if (itemId <= 0)
                return false;
            if (want < 0) want = 0;
            StorageComponent stg = GetStorage(f, storageId);
            if (stg == null)
                return false;

            int have = CountItem(f, storageId, itemId);
            if (have == want)
                return false;
            if (have > want)
            {
                stg.TakeItem(itemId, have - want, out _);
            }
            else
            {
                int inc;
                stg.AddItem(itemId, want - have, 0, out inc);
            }
            return true;
        }

        /// <summary>透镜有两种 id（黑雾引力透镜优先），合计对齐。</summary>
        private static bool SetExactLens(PlanetFactory f, int storageId, int want)
        {
            if (want < 0) want = 0;
            int have = CountLens(f, storageId);
            if (have == want)
                return false;
            if (have > want)
                ConsumeLens(f, storageId, have - want);
            else
                AddLens(f, storageId, want - have);
            return true;
        }

        /// <summary>客户端：记一笔卡槽变动（放入 / 取出），随下一次上报发给主机。</summary>
        public static void NoteSlotDelta(HubInfo info, int itemId, bool isLens, int delta)
        {
            if (info == null || delta == 0 || !MP.CanReport)
                return;
            if (isLens) info.dLens += delta;
            else if (itemId == Items.InformationMatrix) info.dMatrix += delta;
            else if (itemId == ItemProto.kWarperId) info.dWarper += delta;
            else if (itemId == C.BoostItem) info.dBoost += delta;
            else if (itemId == C.UpgradeItem) info.dAcc += delta;
            else if (itemId == Items.GravitonMatrix) info.dMat += delta;
        }

        /// <summary>主机：把客户端上报的卡槽增量落到本地卡槽上。</summary>
        internal static void ApplySlotDeltas(HubInfo info)
        {
            if (info == null || !info.HasSlotDelta)
                return;

            PlanetFactory f = FactoryOf(info.planetId);
            int sid = StorageIdOf(f, info.entityId);
            if (sid <= 0)
            {
                // 这颗星球的工厂不在主机这边（玩家跑去了主机没加载过的星球）：
                // 增量落不下去，直接丢弃 —— 客户端那边的卡槽已经是它自己的样子，
                // 主机不参与这台机器的结算，留着只会变成脏数据。
                info.dMatrix = info.dWarper = info.dLens = info.dBoost = info.dAcc = info.dMat = 0;
                return;
            }

            StorageComponent stg = GetStorage(f, sid);
            if (stg != null)
            {
                EnsureSlotSize(stg);
                bool dirty = false;
                dirty |= DeltaItem(f, sid, Items.InformationMatrix, info.dMatrix);
                dirty |= DeltaItem(f, sid, ItemProto.kWarperId, info.dWarper);
                dirty |= DeltaItem(f, sid, C.BoostItem, info.dBoost);
                dirty |= DeltaItem(f, sid, C.UpgradeItem, info.dAcc);
                dirty |= DeltaItem(f, sid, Items.GravitonMatrix, info.dMat);
                if (info.dLens != 0)
                {
                    if (info.dLens > 0) AddLens(f, sid, info.dLens);
                    else ConsumeLens(f, sid, -info.dLens);
                    dirty = true;
                }
                if (dirty)
                {
                    stg.changed = true;
                    stg.NotifyStorageChange();
                }
            }

            info.dMatrix = info.dWarper = info.dLens = info.dBoost = info.dAcc = info.dMat = 0;
            CaptureSlots(info);   // 立刻刷新绝对值，下一次广播就是新值
        }

        /// <summary>按增量加/扣某种物品（负数扣，扣不到就扣光）。</summary>
        private static bool DeltaItem(PlanetFactory f, int storageId, int itemId, int delta)
        {
            if (itemId <= 0 || delta == 0)
                return false;
            StorageComponent stg = GetStorage(f, storageId);
            if (stg == null)
                return false;
            if (delta > 0)
            {
                int inc;
                stg.AddItem(itemId, delta, 0, out inc);
            }
            else
            {
                int have = CountItem(f, storageId, itemId);
                int take = Math.Min(have, -delta);
                if (take > 0)
                    stg.TakeItem(itemId, take, out _);
            }
            return true;
        }

        /// <summary>
        /// 客户端每帧的「回放」：不参与结算，只把主机广播来的池储量 / 速率搬到面板与特效上。
        /// 关键是 <c>exc.state = 0</c>：原版 PowerSystem 只在 state != 0 时才充放电，
        /// 这样客户端不会偷偷从本地电网抽电，也不会把电送出去。
        /// </summary>
        private static void ReplayRemote(HubInfo info, ref PowerExchangerComponent exc, PowerSystem ps,
                                         out float fxTarget, out string fxReason)
        {
            long cap = C.PoolOf(info.level);
            long pool = info.cachedPool;
            if (pool < 0) pool = 0;
            if (pool > cap) pool = cap;
            exc.currPoolEnergy = pool;

            // 1.19.7：客户端也按主机的 state / 功率参与本地电网结算（否则整张电网被算成没电）。
            // 池子每帧被这里覆盖回主机值，所以本地结算改不动「真实储量」。
            ApplyNetGridState(info, ref exc);

            info.storageDir = 0;

            bool netLive = exc.networkId > 0 && NetHasPower(ps, exc.networkId);

            // 1.19.6：特效强度直接回放主机算好的值（netFxLevel 跟着包一起下来）。
            // 以前是拿 ratePerSecond 再推一遍，两边判据不同 —— 于是「池子同步了、
            // 模式同步了，但特效各亮各的」。
            fxTarget = Mathf.Clamp01(info.netFxLevel);

            if (info.mode == (int)HubMode.Idle)
            {
                if (!netLive) fxTarget = 0f;
                fxReason = fxTarget > 0.5f ? "" : (exc.networkId > 0 ? "电网里没有电" : "没有接入电网");
                return;
            }

            if (fxTarget > 0.01f)
            {
                fxReason = "";
                return;
            }

            fxReason = info.itemStarved
                ? "发送端缺少" + (info.starveNeed ?? "传能耗材") + "（主机那边已停机）"
                : "主机那边没有能量在传输";
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

            // 按「实际扣到的数量」记进度，而不是按「打算扣的数量」：
            // 两者不一致时（同一格被别的逻辑抢先吃掉）进度池会虚高，
            // 升级判定就会放行一次根本不存在的料。
            int got = ConsumeItem(factory, storageId, itemId, take);
            if (got <= 0)
                return;
            if (isAcc) info.upAcc += got;
            else info.upMat += got;
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
            // 换了发送端：上一台的卡槽快照立刻作废，免得面板先显示别人的存货
            me.peerStockStamp = 0f;
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
            me.peerStockStamp = 0f;     // 断了就别再显示上一台的卡槽
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
        ///
        /// 消耗品是**真·硬条件**（1.19.2 修正）：
        ///   · 有料 → 下发量严格封顶在「料能付得起的能量」（数量 × EnergyPerItem），料扣完就停；
        ///   · 这一帧要用的料一个都没有 → 直接不发（<see cref="C.NoItemRate"/> 默认 0）。
        ///     早期版本这里留了 50% 的「免费额度」，等于没料也能白传一半，条件形同虚设。
        ///
        /// <paramref name="amount"/> 是本帧可下发的能量（可能含提速 ×2），
        /// <paramref name="freeBase"/> 是**未提速**的本帧额度 —— 免费额度按它算，
        /// 免得提速剂把「免费部分」也翻倍。
        /// </summary>
        private static long DeliverToReceivers(PlanetFactory factory, int storageId, HubInfo info,
                                               long amount, long freeBase)
        {
            List<long> targets;
            if (amount <= 0 || !_sendTargets.TryGetValue(Key(info.planetId, info.entityId), out targets) ||
                targets == null || targets.Count == 0)
                return 0;

            int myStar = StarOf(info.planetId);

            // 两条链路类型各有各的消耗品，预算分开算、分开扣：
            // 同星系吃信息矩阵，跨星系吃空间翘曲器 + 透镜。
            // 先把「卡槽里到底有没有料」算清楚，别用「剩余预算 > 0」当判据 ——
            // 预算被扣到 0 的那一瞬间会被误判成「没料」，从而掉进降速白送的分支。
            int haveMatrix = CountItem(factory, storageId, Items.InformationMatrix);
            int haveWarper = CountItem(factory, storageId, ItemProto.kWarperId);
            int haveLens = CountLens(factory, storageId);

            long budgetSame = (long)haveMatrix * C.EnergyPerItem;
            long budgetCross = (long)Math.Min(haveWarper, haveLens) * C.EnergyPerItem;

            // 「卡槽里完全没有这种料」才允许按 C.NoItemRate 降速送。
            bool starvedSame = haveMatrix <= 0;
            bool starvedCross = haveWarper <= 0 || haveLens <= 0;

            // 免费额度是**整帧一份**，不是每台接收端一份、更不是每一轮一份。
            // 以前每一轮都对「剩余额度」重新打五折，6 轮下来 0.5+0.25+0.125+… ≈ 98%，
            // 也就是说卡槽空空如也照样能按满速传能、还不扣料 —— 消耗品形同虚设。
            // 基数用「未提速的本帧额度」，否则提速剂会把免费额度也翻倍。
            long freeQuota = (long)((double)freeBase * C.NoItemRate);

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

                    bool starved = t.cross ? starvedCross : starvedSame;
                    long budget = t.cross ? budgetCross : budgetSame;

                    long add;
                    if (starved)
                    {
                        // 卡槽里完全没有这条链路要用的消耗品。
                        // C.NoItemRate 默认 0 → freeQuota = 0 → 一个 J 都不发（硬停）。
                        if (freeQuota <= 0)
                        {
                            info.itemStarved = true;
                            info.starveNeed = t.cross ? "翘曲器+透镜" : "信息矩阵";
                            continue;
                        }
                        long allow = (long)((double)share * C.NoItemRate);
                        if (allow <= 0)
                        {
                            info.itemStarved = true;
                            info.starveNeed = t.cross ? "翘曲器+透镜" : "信息矩阵";
                            continue;
                        }
                        if (allow > freeQuota) allow = freeQuota;
                        add = Math.Min(allow, t.room);
                        if (add <= 0)
                            continue;
                        freeQuota -= add;
                        info.itemStarved = true;
                        info.starveNeed = t.cross ? "翘曲器+透镜" : "信息矩阵";
                    }
                    else
                    {
                        // 有料：严格按料付能量。料扣完 = 本帧这条链路停发（硬停），
                        // 等下一帧补料再继续 —— 这才是「按条件限制」。
                        long allow = Math.Min(share, budget);
                        if (allow <= 0)
                        {
                            info.itemStarved = true;      // 料不够，本帧被卡住
                            info.starveNeed = t.cross ? "翘曲器+透镜" : "信息矩阵";
                            continue;
                        }
                        add = Math.Min(allow, t.room);
                        if (add <= 0)
                            continue;

                        if (t.cross) budgetCross -= add;
                        else budgetSame -= add;
                        ChargeItems(factory, storageId, info, t.cross, add);
                    }

                    AddPending(t.key, add);

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

        /// <summary>
        /// 把本机卡槽里的传能耗材数量同步给所有连到本机的接收端（1.19.3）。
        ///
        /// 接收端窗口里看不到发送端的卡槽，玩家只看到「连上了，但 0 W」，
        /// 完全不知道是对方没料还是自己没通电。这里由发送端每 0.2 秒写一份快照过去，
        /// 接收端面板就能显示「发送端：信息矩阵 × 12」或者「缺信息矩阵」。
        ///
        /// 只在一个进程内的 <see cref="HubInfo"/> 之间传递：同一份存档模拟里（单机，含跨星球）直接生效。
        /// 跨玩家（Nebula/Nebulae）本版不做网络同步，对方进程里看不到这份快照。
        /// </summary>
        private static void PublishStock(PlanetFactory factory, int storageId, HubInfo send)
        {
            List<long> targets;
            if (factory == null || !_sendTargets.TryGetValue(Key(send.planetId, send.entityId), out targets) ||
                targets == null || targets.Count == 0)
                return;

            int matrix = CountItem(factory, storageId, Items.InformationMatrix);
            int warper = CountItem(factory, storageId, ItemProto.kWarperId);
            int lens = CountLens(factory, storageId);
            float now = Time.time;

            for (int i = 0; i < targets.Count; i++)
            {
                HubInfo ro = Get((int)(targets[i] >> 32), (int)(uint)(targets[i] & 0xFFFFFFFFL));
                if (ro == null)
                    continue;
                ro.peerStockMatrix = matrix;
                ro.peerStockWarper = warper;
                ro.peerStockLens = lens;
                ro.peerStockStamp = now;
            }
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
        private static void PullItemsFromBelts(PlanetFactory factory, int storageId, int entityId,
                                               PowerExchangerComponent exc, HubInfo info)
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
                    if (!BeltMayStock(info, itemId))
                        break;                      // 当前工作模式用不上：不前送（料留在带上，不会被吞）
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
        /// 扣不到（卡槽刚好被搬空）就保留累计，下次凑够再扣，不清零白送。
        /// </summary>
        private static void ConsumeBoost(PlanetFactory factory, int storageId, HubInfo info, int have, float dt)
        {
            info.boostAcc += dt * C.BoostPerSec;
            int n = (int)info.boostAcc;
            if (n <= 0)
                return;
            n = Math.Min(n, have);
            int got = n > 0 ? ConsumeItem(factory, storageId, C.BoostItem, n) : 0;
            info.boostAcc -= got;      // 只销掉真扣到的，扣不到就留着下次再扣
            if (info.boostAcc > 0.999f)
                info.boostAcc = 0.999f;
        }

        /// <summary>
        /// 传送带能不能把这种物品送进卡槽。
        ///
        /// 传能耗材（信息矩阵 / 翘曲器 / 透镜）只有「发送」模式用得上：
        /// 待机和接收端的卡槽手动是放不进去的，皮带也不该偷偷塞进去，否则玩家的料
        /// 会堆在一个根本不消耗的格子里（还会把面板的「缺料」状态搅乱）。
        /// 升级材料（空蓄电器 / 引力矩阵）任何模式都要收 —— 待机时也能先攒着升级。
        /// </summary>
        private static bool BeltMayStock(HubInfo info, int itemId)
        {
            if (itemId == C.UpgradeItem || itemId == Items.GravitonMatrix)
                return true;
            return info != null && info.mode == (int)HubMode.Send;
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

        /// <summary>扣掉 count 个透镜（优先扣 1211 黑雾透镜，其次 1209）。返回实际扣掉的数量。</summary>
        private static int ConsumeLens(PlanetFactory factory, int storageId, int count)
        {
            if (count <= 0)
                return 0;
            int left = count;
            System.Collections.Generic.List<int> ids = Items.Candidates;
            for (int i = 0; i < ids.Count && left > 0; i++)
            {
                int have = CountItem(factory, storageId, ids[i]);
                int take = Math.Min(have, left);
                if (take <= 0)
                    continue;
                int got = ConsumeItem(factory, storageId, ids[i], take);
                left -= got;
            }
            return count - left;
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

        /// <summary>
        /// 卡槽里有没有「任意一条链路付得起的料」。
        /// 只用来判断「能不能提速」—— 没料时连半速额度都不该被提速翻倍，
        /// 否则提速剂又把「白送」放大成满速。
        /// </summary>
        private static bool HasSendFuel(PlanetFactory factory, int storageId)
        {
            if (CountItem(factory, storageId, Items.InformationMatrix) > 0)
                return true;
            return CountItem(factory, storageId, ItemProto.kWarperId) > 0 &&
                   CountLens(factory, storageId) > 0;
        }

        /// <summary>
        /// 扣物品。按累计小数结算，避免「每次投递不足 200 MJ 就免费」。
        /// 两条链路的累计分开记（<see cref="HubInfo.costAccSame"/> / <see cref="HubInfo.costAccCross"/>），
        /// 扣料只销掉**真正扣到**的数量，扣不到就留在累计里下次补 —— 不再清零白送。
        /// </summary>
        private static void ChargeItems(PlanetFactory factory, int storageId, HubInfo info, bool crossStar, long energy)
        {
            if (energy <= 0)
                return;

            double acc = crossStar ? info.costAccCross : info.costAccSame;
            acc += (double)energy / C.EnergyPerItem;
            int n = (int)Math.Floor(acc);
            if (n <= 0)
            {
                if (crossStar) info.costAccCross = acc;
                else info.costAccSame = acc;
                return;
            }

            int charged = 0;
            if (crossStar)
            {
                int warper = CountItem(factory, storageId, ItemProto.kWarperId);
                int lens = CountLens(factory, storageId);
                int can = Math.Min(n, Math.Min(warper, lens));
                if (can > 0)
                {
                    int w = ConsumeItem(factory, storageId, ItemProto.kWarperId, can);
                    int l = ConsumeLens(factory, storageId, can);
                    charged = Math.Min(w, l);     // 两边都真扣到的才算数
                }
            }
            else
            {
                int matrix = CountItem(factory, storageId, Items.InformationMatrix);
                int can = Math.Min(n, matrix);
                if (can > 0)
                    charged = ConsumeItem(factory, storageId, Items.InformationMatrix, can);
            }

            acc -= charged;
            if (acc > 0.999) acc = 0.999;
            if (acc < 0) acc = 0;
            if (crossStar) info.costAccCross = acc;
            else info.costAccSame = acc;
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

        /// <summary>从卡槽扣掉 count 个物品，返回**实际扣掉**的数量（扣不到就是 0）。</summary>
        private static int ConsumeItem(PlanetFactory factory, int storageId, int itemId, long count)
        {
            if (count <= 0 || storageId <= 0 || factory?.factoryStorage == null || itemId <= 0)
                return 0;
            StorageComponent stg = factory.factoryStorage.storagePool[storageId];
            if (stg == null)
                return 0;
            int got = stg.TakeItem(itemId, (int)Math.Min(count, int.MaxValue), out _);
            if (got <= 0)
                return 0;
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
            return got;
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
