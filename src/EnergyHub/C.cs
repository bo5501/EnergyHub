using System;
namespace EnergyHub
{
    /// <summary>
    /// 全局常量。
    ///
    /// id 约束（0.10.35 实测，反编译 + 游戏资源数据核对）：
    ///  - ItemProto.ID 必须 &lt; 12000（ItemProto.kMaxProtoId；IconSet.itemIconIndex / signalIconIndex /
    ///    itemProtoById / FactoryProductionStat 等数组都是 12000）。原版物品 175 个，id 1000~6006。
    ///    且要避开这些区间特判：2000~2020（传送带/分拣器）、2199~2299（带类）、3000~3999（炮塔过滤）、
    ///    5100~5199（战机）、6000~6100（增产剂属性物品）。
    ///  - RecipeProto.ID 必须 &lt; 500（RecipeProto.kMaxProtoId）。原版配方 id 1~162。
    ///  - ModelProto.ID 必须 &lt; 模型表长度 + 64（ModelProtoSet.modelArray 按 id 索引；modelIndices 再限 2048）。
    ///    原版模型 664 个、id 1~665（仅缺 178），所以 666 是第一个安全空位。
    ///  - EntityData.protoId 是 short，另需 &lt; 32768（已满足）。
    /// </summary>
    internal static class C
    {
        public const string Guid = "com.energyhub.dsp";
        public const string Name = "星际能量枢纽塔";
        public const string Version = "1.19.3";

        // proto id
        public const int ModelId = 666;    // 原版模型最大 id = 665
        public const int ItemId = 8000;    // 原版物品最大 id = 6006，且不在任何特判区间内
        public const int RecipeId = 300;   // 原版配方 1~162

        /// <summary>
        /// 科技树节点 id。**固定值**，见 Tech.cs：
        /// 节点必须在 LDB 建表之前交给 LDBTool（<c>LDBTool.PreAddProto</c>），
        /// 那一刻 LDB 还没载入、挑不了空闲 id，所以只能用常量。
        ///
        /// 1999 在所有已知环境里都是空的：
        ///   · 原版科技最大 1512 一带，1900 段是黑雾/升级类科技；
        ///   · 创世之书 3.2.4 内嵌 techs.json 最大 id = 1963（已抽取核对）。
        /// 必须 &lt; 2000 —— <c>TechProto.page = ID &gt;= 2000 ? 1 : 0</c>，
        /// 2000 以上会被摆到「升级」那一页去。
        /// </summary>
        public const int TechFixedId = 1999;

        /// <summary>运行时实际用的节点 id（正常情况下等于 <see cref="TechFixedId"/>）。</summary>
        public static int TechId = TechFixedId;

        /// <summary>科技节点图标的兜底路径（原版「星际电力运输」= 能量枢纽那条科技）。</summary>
        public const string TechIconFallback = "Icons/Tech/1512";

        // ---------------- 科技节点摆放栅格（1.15.1） ----------------
        //
        // 参考 UITechTree.GetSqueezedPos：屏幕坐标 = (Position.x × 114, Position.y × 48)，
        // 而科技树里相邻两列的节点 Position 相差 4（114×4 = 456 px），
        // 相邻两行的节点 Position 也相差 4（48×4 = 192 px，正好容得下 ~130 px 高的节点面板）。
        // 所以「往右一格 / 往下一行」在数据上是 ±4，不是 ±1。
        public const int TechColStep = 4;
        public const int TechRowStep = 4;

        // 建造栏：BuildIndex = 分类*100 + 槽位（UIBuildMenu：分类 ≤15、槽位 ≤12）
        // 分类 6 = 物流，原版「能量枢纽」占 606，我们放到 607
        public const int BuildIndex = 607;

        // 物品面板格位（ItemProto.GridIndex，原版 2xxx 段）。
        // 注意：建造栏实际读的是 BuildIndex，GridIndex 只影响部分面板排序，这里取一个空位。
        public const int GridIndex = 2114;

        // 本地化 key
        public const string LItemName = "EnergyHub.Item.Name";
        public const string LItemDesc = "EnergyHub.Item.Desc";
        public const string LRecipeName = "EnergyHub.Recipe.Name";
        public const string LRecipeDesc = "EnergyHub.Recipe.Desc";
        public const string LTechName = "EnergyHub.Tech.Name";
        public const string LTechDesc = "EnergyHub.Tech.Desc";
        public const string LTechConclusion = "EnergyHub.Tech.Conclusion";

        // 建筑尺寸（米）
        /// <summary>总高：1.6.0 起塔顶是三颗公转能量球（球心 29.80 m、半径 0.90 m → 球顶 30.70 m），
        /// 外加半径 5.35 m 的同心光环，碰撞盒仍按 32 m 取。</summary>
        public const float Height = 32.0f;
        public const float BaseRadius = 4.30f; // 底座半径

        // 能量参数（1.10.0 规格：功率硬顶 50 TW、20 级、容量指数曲线 100 GJ → 1 PJ）
        // 换算关系：50 TW × 20 s = 1 PJ —— 满级池子用满级功率正好 20 秒充满，
        // 所以「满级容量」和「功率封顶」是同一个数决定的，改一个另一个要跟着改。
        // ⚠ long 上限 9.22e18 J，容量离溢出还有 4 个数量级的余量。
        /// <summary>
        /// 能量池 1 级容量（J）：100 GJ —— 正好等于一级升级材料（1000 台蓄电器）的储能。
        /// </summary>
        public const long PoolBase = 100_000_000_000L;

        /// <summary>能量池满级容量（J）：1 PJ（= 50 TW × 20 s）。</summary>
        public const long PoolMaxCap = 1_000_000_000_000_000L;

        /// <summary>能量池等级上限（1.10.0：50 → 20 级，压缩曲线、每级收益更大）。</summary>
        public const int MaxPoolLevel = 20;

        /// <summary>升 1 级（1 → 2）的蓄电器基数；其余等级见 <see cref="UpgradeCostOf"/>。</summary>
        public const int UpgradeCost = 1000;

        /// <summary>
        /// 升级耗材之一：蓄电器（原版 kAccumulator = 2206）。
        /// 注意 2207 是「满电蓄电器」，升级只认空的 2206。
        /// </summary>
        public const int UpgradeItem = 2206;

        /// <summary>
        /// 升一级还要消耗多少个「引力矩阵」（1.12.0 起第二种材料，两种必须同时凑齐）。
        /// 这是 1 → 2 级的基数，其余等级见 <see cref="UpgradeCost2Of"/>。
        /// </summary>
        public const int UpgradeCost2 = 2000;

        /// <summary>
        /// 升级耗材之二：引力矩阵（原版 6005，默认值；运行时由 <see cref="Items.GravitonMatrix"/>
        /// 按本地化名字校正，避免版本差异导致 id 变动）。
        /// </summary>
        public const int UpgradeItem2 = 6005;

        // ---------------- 阶梯式升级材料（1.13.0） ----------------
        //
        // 20 级分 5 档，每 4 级抬一档（1~4 / 5~8 / 9~12 / 13~16 / 17~19），
        // 档内材料固定、跨档抬升，越往后越贵，避免「后期升级比前期还便宜」。
        //
        // 数值对齐：容量曲线每级 ×1.62（4 级约 ×6.9），材料每档只抬 1.35~1.55 倍，
        // 相对收益仍然明显递减，但绝对量已经到了需要分次投料的程度
        // （蓄电器堆叠较小，3500 个无法一次塞进 84 格卡槽 + 背包）。
        // 所以升级材料改成「投入式进度池」：卡槽/传送带送来的材料会被自动吸收累积，
        // 攒够当前等级的需求再点升级，玩家不必一次凑齐。
        private static readonly int[] UpTierAcc = { 1000, 1400, 1900, 2600, 3500 };
        private static readonly int[] UpTierMat = { 2000, 3200, 5000, 7800, 12000 };

        /// <summary>等级 L 属于第几档（0 起）：1~4 级 → 0，5~8 级 → 1，以此类推。</summary>
        public static int UpgradeTierOf(int level)
        {
            if (level < 1) level = 1;
            int t = (level - 1) / 4;
            if (t < 0) t = 0;
            if (t >= UpTierAcc.Length) t = UpTierAcc.Length - 1;
            return t;
        }

        /// <summary>从 level 升到 level+1 需要的蓄电器（满级返回 0）。</summary>
        public static int UpgradeCostOf(int level)
        {
            if (level >= MaxPoolLevel) return 0;
            if (level < 1) level = 1;
            return UpTierAcc[UpgradeTierOf(level)];
        }

        /// <summary>从 level 升到 level+1 需要的引力矩阵（满级返回 0）。</summary>
        public static int UpgradeCost2Of(int level)
        {
            if (level >= MaxPoolLevel) return 0;
            if (level < 1) level = 1;
            return UpTierMat[UpgradeTierOf(level)];
        }

        /// <summary>1 级升到 20 级累计需要的蓄电器（面板 / 文档展示用）。</summary>
        public static int TotalUpgradeCost()
        {
            int sum = 0;
            for (int l = 1; l < MaxPoolLevel; l++) sum += UpgradeCostOf(l);
            return sum;
        }

        /// <summary>1 级升到 20 级累计需要的引力矩阵。</summary>
        public static int TotalUpgradeCost2()
        {
            int sum = 0;
            for (int l = 1; l < MaxPoolLevel; l++) sum += UpgradeCost2Of(l);
            return sum;
        }

        /// <summary>
        /// 建筑功率硬顶：50 TW（5×10^13 W）。实际功率 = min(50 TW, 容量÷充满秒数)，
        /// 随等级增长，满级（20 级）恰好触及 50 TW。
        /// 1 tick = 1/60 s，50 TW ≈ 833 GJ/tick。
        /// </summary>
        public const long MaxPowerWatt = 50_000_000_000_000L;
        public const long MaxPowerPerTick = MaxPowerWatt / 60L;

        /// <summary>满池充/放电时间（秒）：任何等级都一样，功率随容量同步缩放。</summary>
        public const double PoolFullChargeSec = 20.0;

        /// <summary>满池跨星球传输时间（秒）：比电网充放慢 5 倍，鼓励多台并联。</summary>
        public const double PoolFullTransferSec = 100.0;

        // ---------------- 提速耗材（1.10.0） ----------------

        /// <summary>
        /// 提速耗材：三级增产剂 Mk.III（原版物品 1141/1142/1143 三档，1143 是最高档）。
        /// 卡槽里有它时，星际传输速率放大 <see cref="BoostRate"/> 倍，并按秒消耗。
        /// </summary>
        public const int BoostItem = 1143;

        /// <summary>有提速耗材时的传输倍率。</summary>
        public const float BoostRate = 2.0f;

        /// <summary>提速期间每秒消耗的提速剂个数（只在真的有能量外送时扣）。</summary>
        public const float BoostPerSec = 1.0f;

        // ---------------- 按需自动分配（1.11.0） ----------------

        /// <summary>
        /// 接收端上报的「需求」按自身放电能力的多少秒折算（J = 满功率放电 × 秒数）。
        ///
        /// 直接用「池子空余」当需求会让大池子独占全部能量：一台 1 PJ 的空池
        /// 永远是 100 GJ 池子的 1000 倍需求，可它其实只是池子大、并不缺电。
        /// 按「接下来几秒能吸收多少」折算，规模相近的接收端才拿到相近的份额，
        /// 真有缺口的端（池子在掉）会因为空余持续刷新而稳住自己的份额。
        /// </summary>
        public const double DemandSeconds = 5.0;

        /// <summary>
        /// 接收端需求上报的有效期（秒）。超过这个时间没刷新（对端星球没加载、
        /// 或那台机器没在被模拟），发送端就退回用「在途账本空余」估算需求。
        /// </summary>
        public const float DemandTimeout = 3.0f;


        /// <summary>1 级容量（保留旧名，prefab 的默认 maxExcEnergy 仍用它）。</summary>
        public const long PoolCapacity = PoolBase;

        /// <summary>
        /// 某一级的能量池容量（J）：指数曲线 PoolBase × (PoolMaxCap/PoolBase)^((L-1)/19)。
        /// L1 = 100 GJ，L5 ≈ 670 GJ，L10 ≈ 7.4 TJ，L15 ≈ 82 TJ，L20 = 1 PJ（每级约 ×1.61）。
        /// </summary>
        public static long PoolOf(int level)
        {
            if (level < 1) level = 1;
            if (level > MaxPoolLevel) level = MaxPoolLevel;
            if (level == MaxPoolLevel)
                return PoolMaxCap;
            double t = (level - 1) / (double)(MaxPoolLevel - 1);
            return (long)(PoolBase * Math.Pow(PoolMaxCap / (double)PoolBase, t));
        }

        /// <summary>
        /// 某一级的「待接收」账本上限（J）= 容量的 4 倍，硬顶 4 EJ（防 long 溢出）。
        /// 接收端星球没加载 / 池子满了的时候，在途能量最多攒这么多，
        /// 超出的部分发送端就不再投递（不空烧电和翘曲器）。
        /// </summary>
        public static long PendingCapOf(int level)
        {
            const long hard = 4_000_000_000_000_000_000L;
            long cap = PoolOf(level);
            if (cap >= hard / 4)
                return hard;
            return Math.Min(cap * 4, hard);
        }

        /// <summary>
        /// 某一级的放电功率上限（J/tick）= min(50 TW, 容量÷20秒)。
        /// 保证任何等级满池充/放都约 20 秒，L20 恰好触及 50 TW。
        /// </summary>
        public static long ExchangePerTickOf(int level)
        {
            long byTime = (long)(PoolOf(level) / (PoolFullChargeSec * 60.0));
            return Math.Min(byTime, MaxPowerPerTick);
        }

        /// <summary>
        /// 某一级的跨星球传输上限（J/tick）= min(50 TW, 容量÷100秒)。
        /// 比电网功率慢 5 倍，鼓励多台枢纽并联。
        /// </summary>
        public static long TransferPerTickOf(int level)
        {
            long byTime = (long)(PoolOf(level) / (PoolFullTransferSec * 60.0));
            return Math.Min(byTime, MaxPowerPerTick);
        }

        /// <summary>
        /// 传能消耗：每 <see cref="EnergyPerItem"/> 焦耳消耗 1 个物品。
        ///   · 同一恒星系内（跨星球）→ 信息矩阵 ×1
        ///   · 跨恒星系          → 空间翘曲器 ×1 + 黑雾引力透镜 ×1
        /// 200 MJ 与原来的 1 翘曲器/200 MJ 一致，按 24 MW 的传输速率约 8 秒 1 个。
        /// </summary>
        public const long EnergyPerItem = 200_000_000L;

        /// <summary>旧名保留：等价于 <see cref="EnergyPerItem"/>。</summary>
        public const long WarperPerEnergy = EnergyPerItem;

        /// <summary>
        /// 卡槽里完全没有这条链路要用的消耗品时，还允许送出多少（相对本帧额度的倍率）。
        ///
        /// **默认 0 = 硬停**：消耗品是传能的前提条件，没料就一点能量都不送。
        /// 早期版本在这里放了 0.5 的「免费额度」，本意是「别让玩家以为建筑坏了」，
        /// 结果就成了「没料也能白传一半」—— 条件形同虚设，玩家一个矩阵都没放，
        /// 塔照样以 50% 速率为电网供电。现在默认关掉，面板会明确写缺的是哪种料。
        ///
        /// 这是**整帧一份**的额度（见 <c>HubManager.DeliverToReceivers</c>），
        /// 不是每台接收端一份、更不是每轮分配重新打一次折。
        /// 想要回旧行为（半速白传）把 BepInEx 配置里的 `NoItemRate` 改成 0.5 即可。
        /// </summary>
        public static float NoItemRate = 0f;

        /// <summary>
        /// 特效强度的爬升 / 衰减速度（每秒，0~1）。
        ///
        /// 参考原版 <c>PowerExchangerComponent.StateUpdate</c>：它的 state 每 tick 走 0.00557
        /// 逼近 targetState（约 3 秒走完 0~1），特效用的是同一条「斜坡」而不是硬开关——
        /// 硬开关在电网抖动时会闪，斜坡看起来像「充能 / 泄能」。
        /// 熄灭比点亮快一倍，玩家看到的是「没电了，一下就停了」。
        /// </summary>
        public const float FxRisePerSec = 0.85f;
        public const float FxFallPerSec = 1.80f;

        /// <summary>
        /// 「有能量在动」的保持时间（秒）。
        ///
        /// 电网负载每帧都在变，发送端的 currEnergyPerTick 会时有时无（工厂瞬间吃满发电、
        /// 电池切换、玩家铺了一条带……）。直接拿它做布尔判据，特效就会跟着电网「呼吸」
        /// 一闪一闪。命中一次就锁这么久，期间即使这一帧没抽到电也保持点亮。
        /// </summary>
        public const float FxHoldSec = 1.5f;

        /// <summary>
        /// 建筑「 GPU 实例动画数据」里 power 的固定值（0~1）。
        ///
        /// DSP 用 <c>DrawMeshInstancedIndirect</c> 画建筑，每个实例带 20 字节的
        /// <c>AnimData</c>（time / prepare_length / working_length / state / power），
        /// 每帧由 <c>PowerSystem.GameTick</c> 写入、再上传到 GPU。
        /// 交换站的 power = currPoolEnergy / maxPoolEnergy，而我们的池子每 tick 都被抽干，
        /// 于是 power 在 0 和某个极小值之间来回跳 —— 原版 shader 里「按功率调制」的那一路
        /// 会跟着一起跳，看起来就是整栋建筑一闪一闪，改材质参数根本压不住（实例数据在材质之后生效）。
        ///
        /// 所以 1.4.0 直接在 <c>PowerSystem.GameTick</c> 的 Postfix 里把这台机器的 AnimData 钉死：
        /// state=0、time=0、power=这个常量。1 = 「满功率」，自发光按最亮的那一档恒定输出。
        /// </summary>
        public const float PinAnimPower = 1f;

        // ---------------- 底部传送带接口（1.4.0） ----------------

        /// <summary>接口套筒从底座外壁再向外凸出多少米（视觉上的「接口仓」）。</summary>
        public const float PortOut = 0.55f;

        /// <summary>接口套筒的外半宽。4 个接口间距 2 m，所以半宽必须 ≤ 1 m，否则相邻会穿插。</summary>
        public const float PortHalfW = 0.97f;

        /// <summary>接口套筒内孔半宽（传送带 2 m 宽，两侧边缘被套筒压住，读作「插进接口」）。</summary>
        public const float PortInnerHalfW = 0.83f;

        /// <summary>接口套筒高度：地面 0 → 0.94 m，内孔顶 0.80 m（传送带带面 0.2 m 在孔内）。</summary>
        public const float PortHeight = 0.94f;
        public const float PortInnerTop = 0.80f;

        /// <summary>
        /// 消耗品卡槽的格子数：6 列 × 14 行 = 84 格。
        ///
        /// 三种传能消耗品（信息矩阵 / 空间翘曲器 / 透镜）各自至少要占满一格，
        /// 原来 1 格时放完一种、另一种就再也进不来（「放不进 / 传送带不停」的根因之一）。
        ///
        /// 1.7.0 扩到 30 格是为了放得下 2000 个引力矩阵（矩阵一格叠 100）。
        /// 1.12.0 起升级要「1000 蓄电器 + 2000 引力矩阵」两种材料：
        /// 蓄电器的堆叠上限远小于矩阵，1000 个就可能占掉 50 格，
        /// 加上 2000 矩阵的 20 格，30 格根本不够 —— 扩到 84 格留出余量，
        /// 让信息矩阵 / 翘曲器 / 透镜 / 增产剂还能同时待在槽里。
        /// （升级扣料也支持直接从背包扣，卡槽只是「优先扣」的来源，见 HubManager.Upgrade）
        /// </summary>
        public const int SlotCols = 6;
        public const int SlotRows = 14;
        public const int SlotCount = SlotCols * SlotRows;

        /// <summary>
        /// 电力节点连接距离（米）。节点之间取两者 connectDistance 的较大值判定能否连线，
        /// 电力感应塔的 connectDistance 约 9，这里给 12 保证玩家把塔放在旁边就能接上。
        /// </summary>
        public const float PowerConnectDistance = 12f;
    }
}
