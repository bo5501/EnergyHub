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
        public const string Version = "1.20.0";

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
        // 1.20.0：槽位就是 F 键号（F1~F10 对应 1~10），这里要 F10；
        // 页码不写死 —— 「电力」页在 BuildIndex 里是第几页会随版本 / mod 组合变，
        // 运行时拿原版蓄电器当锚点算出来（见 Protos.ResolveBuildIndex）。
        public const int BuildIndex = 607;          // 兜底值（锚点找不到时用）
        public const int BuildIndexSlot = 10;       // F10
        public const int BuildIndexAnchor = 2206;   // 原版「蓄电器」，一定挂在电力页

        /// <summary>运行时实际使用的 BuildIndex（默认等于兜底值）。</summary>
        public static int ResolvedBuildIndex = BuildIndex;

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
        public const string LPoolTechName = "EnergyHub.PoolTech.Name";
        public const string LPoolTechDesc = "EnergyHub.PoolTech.Desc";
        public const string LPoolTechConclusion = "EnergyHub.PoolTech.Conclusion";

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

        // ---------------- 能量池等级（1.21.0：改由科技树研究） ----------------
        //
        // 1.20.x 及以前：等级是**每台建筑自己的**状态，在面板上投料升级。
        // 代价是「拆了重建就变回 1 级」，而且这个状态要跟着存档、跟着联机包到处传，
        // 修了两版仍然会在「拆和建落在同一个扫描周期」这类边角上翻车。
        //
        // 1.21.0 换成另一条路：等级变成**全局研究进度**，挂在科技树「升级」页的一个
        // 可重复研究节点上（和原版「矿物利用」那种一样），只消耗引力矩阵。
        //   · 拆了重建？等级是存档级研究进度，重建出来天生就是当前等级 —— 问题消失；
        //   · 存档 / 联机也不用再同步每台塔的等级和进度池，一份 techStates 就够；
        //   · 面板上那条升级栏改成只读的状态显示。
        //
        // 【1.21.1 关键修正】研究进度里的 curLevel 直接就是**能量池等级**：
        //   初始 curLevel = 1（= 能量池 1 级），研究一次 +1，第 19 次到 20 级。
        // 这样科技树上写的「等级 N」和枢纽面板上的「N 级」永远是同一个数（1.21.0 里
        // 一个是研究次数、一个是池子等级，正好差 1，玩家一眼就看出不对）。
        //
        // 每级引力矩阵需求（从 level 级升到 level+1 级）：
        //   cost(level) = 900·level + 100·level²
        //   1 级 1000 个、2 级 2200、3 级 3600、5 级 7000、10 级 19000、19 级 53200，
        //   全程合计 418000 个（1.21.1 的 20+5l+l² 合计只有 3344，玩家反馈"太便宜"）。
        //
        // TechProto.GetHashNeeded(level) 只支持 a + b·level + c·level² 这种形式，
        // 所以直接令 GetHashNeeded(level) = cost(level) 即：
        //   HashNeeded = 0、LevelCoef1 = 900×3600、LevelCoef2 = 100×3600。
        // （每个引力矩阵 3600 hash，见 TechProto.kPointPerItem；LabComponent 里 1 件材料 = 3600 hash）

        /// <summary>科技节点固定 id。必须 ≥ 2000 —— <c>TechProto.page = ID &gt;= 2000 ? 1 : 0</c>，
        /// ≥ 2000 才会被摆到「升级」页；同时必须 &lt; 12000（TechProto.kMaxProtoId）。
        /// 2900~2999 段在原版、创世之书里都是空的，2999 取最靠后、最不容易被后来者撞上。</summary>
        public const int PoolTechFixedId = 2999;

        /// <summary>运行时实际用的节点 id（2999 被别的 MOD 占了会在这里换成空闲 id）。</summary>
        public static int PoolTechId = PoolTechFixedId;

        /// <summary>总共要研究几次：19 次（1 级起步，每研究一次 +1 级，第 19 次到 20 级）。</summary>
        public const int PoolTechSteps = MaxPoolLevel - 1;

        /// <summary>
        /// 写进 <c>TechProto.MaxLevel</c> / <c>TechState.maxLevel</c> 的值（19）。
        ///
        /// DSP 可重复科技的怪癖（GameHistoryData.AddTechHash）：
        /// 只有当「本次完成时 curLevel 已经等于 maxLevel」才会把 <c>unlocked</c> 置真，
        /// 否则只把 curLevel +1。也就是说 maxLevel = N 的节点要点 **N+1 次**才算研究完，
        /// 最后那次不再涨等级。
        /// 我们让 curLevel 从 1 起步、直接代表能量池等级，于是：
        ///   研究 1 次 → curLevel 2（= 2 级）… 研究 18 次 → curLevel 19（= 19 级）
        ///   第 19 次 → curLevel 已经等于 maxLevel，unlocked = true，等级 = 19 + 1 = 20。
        /// 所以 MaxLevel 填 19，等级 = <c>curLevel + (unlocked ? 1 : 0)</c>，
        /// 中前期（1~19 级）科技树显示的等级与面板完全一致。
        /// </summary>
        public const int PoolTechProtoMaxLevel = PoolTechSteps;      // 19

        /// <summary>研究材料每个值多少 hash（ItemPoints = 1 → 一个引力矩阵 3600 hash）。</summary>
        public const int PoolTechItemPoints = 1;

        /// <summary>
        /// 从 <paramref name="level"/> 级研究到下一级需要多少个引力矩阵（level 1..19）。
        /// cost(level) = 900·level + 100·level²
        /// （1.21.3 起把研究成本拉高约 30 倍：1 级 1000 个、5 级 7000、19 级 53200）。
        /// </summary>
        public static int PoolTechCostOf(int level)
        {
            if (level >= MaxPoolLevel) return 0;
            if (level < 1) level = 1;
            return 900 * level + 100 * level * level;
        }

        /// <summary><paramref name="level"/> 级这一档研究需要的 hash（= 引力矩阵数 × 3600）。</summary>
        public static long PoolTechHashOf(int level)
        {
            return (long)PoolTechCostOf(level) * 3600L;
        }

        /// <summary>
        /// GetHashNeeded(level) 的常数项 / 一次项 / 二次项（单位 hash）。
        /// 与 <see cref="PoolTechCostOf"/> 完全一致：cost(level) = 900·level + 100·level² 个引力矩阵，
        /// 一个矩阵 3600 hash，所以常数项 0、一次项 900×3600、二次项 100×3600。
        /// （HashNeeded 写 0 是安全的：GetHashNeeded 只会被 level ≥ 1 调用。）
        /// </summary>
        public const int PoolTechHashNeeded = 0;
        public const int PoolTechCoef1 = 900 * 3600;
        public const int PoolTechCoef2 = 100 * 3600;

        /// <summary>
        /// 1.21.0 的旧公式（0 基研究次数：500 + 150·l + 10·l²）。只用来在读档时
        /// 认出「这条 TechState 是老版本写的」，从而把进度平移 +1 级、并换成新价格。
        /// </summary>
        public static long LegacyPoolTechHashOf(int curLevel)
        {
            int l = curLevel < 0 ? 0 : curLevel;
            return (500L + 150L * l + 10L * l * l) * 3600L;
        }

        /// <summary>1 级研究到满级累计需要多少引力矩阵（面板 / 文档展示用）。</summary>
        public static int TotalPoolTechCost()
        {
            int sum = 0;
            for (int lv = 1; lv < MaxPoolLevel; lv++) sum += PoolTechCostOf(lv);
            return sum;
        }

        /// <summary>
        /// 研究材料：引力矩阵（原版 6005，默认值；运行时由 <see cref="Items.GravitonMatrix"/>
        /// 按本地化名字校正，避免版本差异导致 id 变动）。
        /// </summary>
        public const int GravitonMatrixDefault = 6005;

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

        // ---------------- 提速耗材（1.10.0 加入，1.20.0 取消） ----------------
        //
        // 原来卡槽里放「三级增产剂 Mk.III」可以把传输速率翻倍（每秒吃 1 个）。
        // 玩家反馈这一格对玩法没有帮助、还占地方，1.20.0 起整个功能去掉：
        // 不再有提速格、不再消耗增产剂、传输速率只由能量池等级决定。
        // 协议 / 存档里的 dBoost / slotBoost 字段保留（恒为 0），老档仍然能读。

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

        /// <summary>
        /// 某一级的「1 个耗材换多少焦耳」（1.21.4 重做）。
        ///
        /// 1.20.1 起用的是「满速传输 1 秒的能量」= <c>PoolOf(level) / 100</c>，
        /// 20 级算下来是 10 TJ/个 —— 只有**满速**跑才是每秒 1 个。
        /// 但实际传能功率由接收端电网缺口决定，通常只有满速的千分之一到百分之一，
        /// 于是 7 级的塔传 50 MW 时要 **6 分钟才扣 1 个信息矩阵**，
        /// 面板上那个数字几分钟都不动，看着就是「传能不扣料」。
        ///
        /// 现在改成温和的等级曲线：1 级 1 GJ/个，每级 +5%，20 级约 2.5 GJ/个。
        /// 50 MW → 20 秒 1 个，100 MW → 10 秒 1 个，肉眼能看见扣料；
        /// 满速高负载时由 <see cref="MaxItemsPerSec"/> 限流，不会几秒烧空卡槽。
        /// </summary>
        public static long EnergyPerItemAt(int level)
        {
            if (level < 1) level = 1;
            if (level > MaxPoolLevel) level = MaxPoolLevel;
            double per = 1_000_000_000.0 * Math.Pow(1.05, level - 1);
            return (long)per;
        }

        /// <summary>
        /// 每条链路每秒最多扣多少个耗材（1.21.4）。
        ///
        /// 能量池满速时（20 级 10 TW）按 <see cref="EnergyPerItemAt"/> 算是每秒上万个，
        /// 卡槽 1 万格眨眼就空 —— 那又会回到「料烧光 → 硬停」。这里硬性限流：
        /// 满速也好、超高负载也好，一条链路每秒最多扣这么多个，
        /// 超出部分算「等级带来的省料」，不再额外扣。
        ///
        /// 正常玩法（单塔几十 GW 以内）根本够不到这个上限，不影响按能量计费。
        /// </summary>
        public const int MaxItemsPerSec = 10;

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
        /// 每种消耗品在卡槽里的存量上限（1.20.1）。
        ///
        /// 以前上限是「格数 × 该物品的原版堆叠」：84 格被 5 种料抢，蓄电器一格只叠 100，
        /// 玩家用传送带源源不断送料时会很快顶满，面板上那几格看起来就「不涨了 / 没数据」。
        /// 现在直接把每格的 stackSize 抬到 1 万（原版 AddItem 认 grid.stackSize），
        /// 一种料一格就能存 1 万个，传送带送多少面板显示多少。
        /// </summary>
        public const int SlotStackMax = 10000;

        /// <summary>
        /// 电力节点连接距离（米）。节点之间取两者 connectDistance 的较大值判定能否连线，
        /// 电力感应塔的 connectDistance 约 9，这里给 12 保证玩家把塔放在旁边就能接上。
        /// </summary>
        public const float PowerConnectDistance = 12f;
    }
}
