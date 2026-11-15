using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace EnergyHub
{
    /// <summary>
    /// 能量池等级的「科技化」（1.21.0）。
    ///
    /// ============ 为什么改 ============
    ///
    /// 1.20.x 及以前，等级是**每台建筑自己的**状态：面板上投料升级，状态跟着存档、
    /// 跟着联机包同步。代价是「拆了重建就变回 1 级」，而且这个状态要跨存档 / 跨端搬运，
    /// 修了两版（1.20.0 首版、1.20.1 第二版）仍然会在「拆和建落在同一个扫描周期」
    /// 这类时序边角上翻车。
    ///
    /// 1.21.0 换一条路：等级变成**全局研究进度** —— 科技树「升级」页里一个可重复研究的节点，
    /// 和原版「矿物利用 / 运输船引擎」那种多级科技一模一样，只消耗引力矩阵。
    ///   · 拆了重建？等级是存档级研究进度，重建出来天生就是当前等级，问题从根上消失；
    ///   · 存档 / 联机也不用再搬每台塔的等级与进度池，一份 techStates 就够。
    ///
    /// ============ 实现要点 ============
    ///
    ///   · 节点 id 必须 ≥ 2000（<c>TechProto.page = ID ≥ 2000 ? 1 : 0</c>），才会落在「升级」页；
    ///     和 <see cref="Tech"/> 一样走 LDBTool 的 PreAdd 队列（在 Awake 提交骨架），
    ///     位置 / 前置 / 材料等建表之后再在 <see cref="Layout"/> 里写。
    ///   · 可重复研究：<c>Level = 0, MaxLevel = 19</c> → 研究 19 次到 20 级。
    ///     每次完成时 AddTechHash 会把 curLevel +1、hashNeeded 重算一次
    ///     （<c>GetHashNeeded(level) = HashNeeded + level·LevelCoef1 + level²·LevelCoef2</c>），
    ///     所以材料递增只要把三条参数按 500 + 150·l + 10·l² 折算成 hash 就行。
    ///   · 等级 = 1 + curLevel（curLevel 0 = 初始 1 级）。
    ///   · 老存档没有我们这个 id 的 TechState，<see cref="EnsureState"/> 会补一条，
    ///     并按「存档里已有塔的最高等级」把一开始的研究进度补上，避免升级过的存档被降回 1 级。
    /// </summary>
    internal static class PoolTech
    {
        /// <summary>节点是否已经就位（坐标、前置、材料都写好了）。</summary>
        public static bool Ok { get; private set; }

        /// <summary>是否已经跑过 <see cref="Layout"/>。</summary>
        public static bool LaidOut { get; private set; }

        public static TechProto Proto { get; private set; }

        private static TechProto _ours;          // 交给 LDBTool 的骨架
        private static int _rescues;

        /// <summary>当前研究出的能量池等级（1~20）。</summary>
        private static int _level = 1;
        private static float _timer;

        /// <summary>上一次看到的 curLevel / 是否正在被研究（用于「升一级后自动重新排队」）。</summary>
        private static int _lastCurLevel = -1;
        private static bool _wasResearching;

        /// <summary>
        /// 当前等级 = curLevel + (unlocked ? 1 : 0)。读不到科技状态（还没开局 / 表没建好）时退回 1。
        /// 每 0.5 秒刷新一次缓存，别在每帧的 tick 里反复查字典。
        /// </summary>
        public static int Level
        {
            get { return _level; }
        }

        /// <summary>已研究次数（0 ~ <see cref="C.PoolTechSteps"/>）。</summary>
        public static int ResearchCount
        {
            get { return _level - 1; }
        }

        // ---------------- 1) Awake：提交给 LDBTool ----------------

        public static void PreAdd()
        {
            if (_ours != null)
                return;

            var t = new TechProto
            {
                ID = C.PoolTechFixedId,
                Name = C.LPoolTechName,
                SID = "",
                sid = "",
                Desc = C.LPoolTechDesc,
                Conclusion = C.LPoolTechConclusion,
                Published = true,
                IsHiddenTech = false,
                IsObsolete = false,

                // 这些数组 Preload / Preload2 会直接取 .Length，留 null 就是 NRE
                PreItem = new int[0],
                PreTechs = new int[0],
                PreTechsImplicit = new int[0],
                PreTechsMax = false,
                PropertyOverrideItems = new int[0],
                PropertyItemCounts = new int[0],
                UnlockRecipes = new int[0],
                UnlockFunctions = new int[0],
                UnlockValues = new double[0],
                AddItems = new int[0],
                AddItemCounts = new int[0],

                // 研究材料：引力矩阵。ItemPoints = 1 → 一个矩阵 3600 hash。
                Items = new[] { Items.GravitonMatrix },
                ItemPoints = new[] { C.PoolTechItemPoints },
                IsLabTech = true,

                // 可重复研究：Level = 0（没有「等级后缀」），MaxLevel = 19，
                // curLevel 从 1 起步（= 能量池等级），研究一次 +1。
                Level = 0,
                MaxLevel = C.PoolTechProtoMaxLevel,
                HashNeeded = C.PoolTechHashNeeded,
                LevelCoef1 = C.PoolTechCoef1,
                LevelCoef2 = C.PoolTechCoef2,

                IconPath = C.TechIconFallback,
                IconTag = "",
                Position = Vector2.zero,
            };

            LDBTool.PreAddProto(t);
            _ours = t;

            Plugin.Log.LogInfo("能量池科技：已提交节点骨架 id=" + C.PoolTechFixedId +
                               "（升级页，共研究 " + C.PoolTechSteps + " 次，" +
                               "材料 = 引力矩阵 " + C.PoolTechCostOf(1) + " → " +
                               C.PoolTechCostOf(C.MaxPoolLevel - 1) +
                               "，合计 " + C.TotalPoolTechCost() + "）");
        }

        // ---------------- 2) PostAdd：定位置 / 定前置 / 定图标 ----------------

        public static void Layout()
        {
            if (LaidOut)
                return;

            TechProtoSet set = LDB.techs;
            if (set == null || set.dataArray == null || set.dataArray.Length == 0)
                return;                       // LDB 还没好，下次再说

            TechProto t = FindInArray(set.dataArray, C.PoolTechFixedId);
            bool mine = t != null && (ReferenceEquals(t, _ours) ||
                                      string.Equals(t.Name, C.LPoolTechName, StringComparison.Ordinal));
            if (mine && !ReferenceEquals(t, _ours))
            {
                Plugin.Log.LogInfo("能量池科技：LDBTool 建表时换了对象实例，按本地化 key 认回 id=" + t.ID);
                _ours = t;
            }
            if (t != null && !mine)
            {
                Plugin.Log.LogWarning("能量池科技：id " + C.PoolTechFixedId + " 已被「" + t.Name + "」占用，改用空闲 id");
                t = null;
            }

            if (t == null)
            {
                int id = PickId(set.dataArray);
                if (id <= 0)
                {
                    Plugin.Log.LogWarning("能量池科技：升级页没有空闲 id，等级固定为 1 级");
                    LaidOut = true;
                    return;
                }
                t = _ours ?? new TechProto();
                t.ID = id;
                Append(set, t);
                Plugin.Log.LogInfo("能量池科技：节点不在 LDBTool 的建表结果里，已运行时追加 id=" + id);
            }

            // ---- 字段 ----
            t.Items = new[] { Items.GravitonMatrix };
            t.ItemPoints = new[] { C.PoolTechItemPoints };
            t.IsLabTech = true;
            t.Level = 0;
            t.MaxLevel = C.PoolTechProtoMaxLevel;
            t.HashNeeded = C.PoolTechHashNeeded;
            t.LevelCoef1 = C.PoolTechCoef1;
            t.LevelCoef2 = C.PoolTechCoef2;
            t.UnlockRecipes = new int[0];
            t.UnlockFunctions = new int[0];
            t.UnlockValues = new double[0];
            t.AddItems = new int[0];
            t.AddItemCounts = new int[0];
            t.Published = true;
            t.IsHiddenTech = false;
            t.IconTag = "";

            // 前置：1.21.2 起**不再挂**建筑科技那条前置。
            //
            // 建筑科技在主科技页（page 0），本节点在升级页（page 1）。科技树是按页分成两个
            // graphGroup 的，前置连线却用同一个 nodes 字典按 anchoredPosition 画 ——
            // 跨页前置会从升级页的节点拉一条线到主科技页的坐标上，落在屏幕外或别的节点身上。
            // 原版升级页的科技也基本不挂主科技页的前置，所以这里跟原版保持一致：
            // 不挂前置（没解锁建筑时研究它没有任何作用，不会造成实际影响）。
            int preTech = 0;
            t.PreTechs = new int[0];
            t.PreTechsImplicit = new int[0];
            t.PreTechsMax = false;

            t.Position = PickUpgradePos(set.dataArray, t.ID);

            try { t.Preload(); }
            catch (Exception e) { Plugin.Log.LogWarning("能量池科技：Preload 失败: " + e.Message); }

            ApplyIcon(t);

            try { t.Preload2(); }
            catch (Exception e) { Plugin.Log.LogWarning("能量池科技：Preload2 失败: " + e.Message); }

            Proto = t;
            C.PoolTechId = t.ID;
            Ok = true;
            LaidOut = true;

            Plugin.Log.LogInfo("能量池科技节点已就位: id=" + t.ID + "「" + t.name + "」" +
                               " 页=" + t.page + "（1 = 升级页）" +
                               " 坐标=(" + t.Position.x.ToString("0") + "," + t.Position.y.ToString("0") + ")" +
                               " 可研究=" + t.MaxLevel + " 次" +
                               " 材料=" + Items.Name(Items.GravitonMatrix) +
                               "×" + C.PoolTechCostOf(1) + "→" + C.PoolTechCostOf(C.MaxPoolLevel - 1) +
                               "（合计 " + C.TotalPoolTechCost() + "）" +
                               " 前置=" + (preTech > 0 ? "#" + preTech : "无"));
        }

        /// <summary>每个科技节点也都需要图标；直接借引力矩阵的物品图标（和材料一致，一眼能认出来）。</summary>
        private static readonly AccessTools.FieldRef<TechProto, Sprite> _iconRef =
            AccessTools.FieldRefAccess<TechProto, Sprite>("_iconSprite");

        private static void ApplyIcon(TechProto t)
        {
            if (t == null || _iconRef == null)
                return;
            try
            {
                Sprite sp = Items.Icon(Items.GravitonMatrix);
                if (sp == null)
                    sp = Resources.Load<Sprite>(C.TechIconFallback);
                if (sp != null)
                    _iconRef(t) = sp;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("能量池科技：图标设置失败（不影响功能）: " + e.Message);
            }
        }

        // ---------------- 等级读取 ----------------

        /// <summary>每帧调：0.5 秒刷一次等级缓存，顺带补科技状态、续排队。</summary>
        public static void Tick()
        {
            if (!LaidOut && Protos.IsPreloaded)
            {
                try { Layout(); }
                catch (Exception e) { Plugin.Log.LogWarning("能量池科技：布局失败: " + e.Message); }
            }

            EnsureState();

            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = 0.5f;

            _level = ReadLevel();
            AutoRequeue();
        }

        /// <summary>直接从科技状态里读等级（1~20）。读不到就退回 1。</summary>
        public static int ReadLevel()
        {
            int lv = 1;
            try
            {
                if (GameMain.history != null && GameMain.history.techStates != null && C.PoolTechId > 0)
                {
                    TechState st;
                    if (GameMain.history.techStates.TryGetValue(C.PoolTechId, out st))
                    {
                        // 1.21.1：curLevel 直接就是能量池等级（初始 1），
                        // 最后那次完成只把 unlocked 置真、不再涨 curLevel，所以补一个 +1。
                        int cur = Mathf.Clamp(st.curLevel, 0, C.PoolTechProtoMaxLevel);
                        lv = cur + (st.unlocked ? 1 : 0);
                    }
                }
            }
            catch { }
            if (lv < 1) lv = 1;
            if (lv > C.MaxPoolLevel) lv = C.MaxPoolLevel;
            return lv;
        }

        /// <summary>
        /// 可重复科技每完成一级都会被 <c>GameHistoryData.AddTechHash</c> 从队列里踢出去
        /// （DequeueTech），于是「研究完一级就停住，要手动再排 19 次」—— 1.21.0 里
        /// 玩家看到的就是这个，很容易以为研究坏了。
        /// 这里在「刚刚升了一级、而且它当时正在被研究」时把它放回队尾：
        /// 玩家主动取消（把它从队列里删掉）不会被我们强行加回来 —— 只有完成升级这一下才续。
        /// </summary>
        private static void AutoRequeue()
        {
            try
            {
                GameHistoryData h = GameMain.history;
                if (h == null || Proto == null || h.techStates == null)
                    return;
                TechState st;
                if (!h.techStates.TryGetValue(Proto.ID, out st))
                    return;

                bool researching = h.currentTech == Proto.ID;
                bool leveled = _lastCurLevel >= 0 && st.curLevel > _lastCurLevel;

                if (leveled && _wasResearching && !st.unlocked && st.curLevel < st.maxLevel)
                {
                    h.EnqueueTech(Proto.ID);
                    Plugin.Log.LogInfo("能量池科技：第 " + st.curLevel + " 级完成，已自动重新排队（下一级 " +
                                       C.PoolTechCostOf(st.curLevel) + " 引力矩阵）");
                }

                _lastCurLevel = st.curLevel;
                _wasResearching = researching || h.currentTech == Proto.ID;
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Plugin.Log.LogWarning("能量池科技：续排队失败: " + e.Message);
            }
        }

        /// <summary>
        /// 老存档 / 新开局都要有我们这个 id 的 TechState。
        ///
        /// 缺了会拿到默认结构体（hashNeeded = 6000000、maxLevel = 0），那这个节点永远是
        /// 「要交 600 万 hash」的死节点。补建时按「存档里已有塔的最高等级」把进度补上 ——
        /// 1.20.x 存档里的 15 级塔，升级到 1.21 之后仍然是 15 级，不会被打回 1 级。
        ///
        /// 1.21.1 还多两件事：
        ///   · 游戏自己建的 TechState（curLevel = Level = 0、hashNeeded = GetHashNeeded(0)）
        ///     要平移到 curLevel = 1，否则第一次研究只涨到 1 级，看不出效果；
        ///   · 1.21.0 写下的进度（curLevel 是「研究次数」，0 起、旧价格）要平移 +1 级并换新价格。
        /// </summary>
        public static void EnsureState()
        {
            if (Proto == null || GameMain.data == null || GameMain.history == null)
                return;
            try
            {
                var states = GameMain.history.techStates;
                if (states == null)
                    return;

                int upoint = C.PoolTechItemPoints;

                if (!states.ContainsKey(Proto.ID))
                {
                    // TakeMigratedLevel 返回「研究次数」（旧等级 - 1），1.21.1 的 curLevel = 等级 = 次数 + 1
                    int seed = Mathf.Clamp(HubManager.TakeMigratedLevel(), 0, C.PoolTechSteps);
                    // 沙盒存档直接拉满，省得测试满级性能还要点 19 次
                    if (HubManager.IsSandbox)
                        seed = C.PoolTechSteps;
                    bool maxed = seed >= C.PoolTechSteps;
                    int cur = Mathf.Clamp(seed + 1, 1, C.PoolTechProtoMaxLevel);
                    long need = Proto.GetHashNeeded(cur);
                    states[Proto.ID] = new TechState(maxed, cur, C.PoolTechProtoMaxLevel,
                                                    0L, need, upoint);
                    if (seed > 0)
                        Plugin.Log.LogInfo("能量池科技：已为存档补建研究进度 #" + Proto.ID +
                                           "（" + cur + " 级，迁移自旧存档里的塔）");
                    else
                        Plugin.Log.LogInfo("能量池科技：已补建科技状态 #" + Proto.ID +
                                           "（1 级，下一级需要 hash=" + need + "）");
                    _level = Mathf.Clamp(maxed ? C.MaxPoolLevel : cur, 1, C.MaxPoolLevel);
                    _lastCurLevel = cur;
                    return;
                }

                TechState st2 = states[Proto.ID];
                if (st2.unlocked)
                {
                    // 已经研究完了。1.21.0 的满级状态是 curLevel = 18 + unlocked（= 20 级），
                    // 新口径要求 curLevel = 19，补齐一下，否则读出来会变成 19 级。
                    if (st2.curLevel < C.PoolTechProtoMaxLevel)
                    {
                        states[Proto.ID] = new TechState(true, C.PoolTechProtoMaxLevel,
                                                         C.PoolTechProtoMaxLevel, 0L,
                                                         Proto.GetHashNeeded(C.PoolTechProtoMaxLevel), upoint);
                        Plugin.Log.LogInfo("能量池科技：满级状态校正为 curLevel=" + C.PoolTechProtoMaxLevel);
                    }
                    return;
                }

                int cur2 = st2.curLevel;
                // curLevel = 0 → 游戏按 TechProto.Level 建的默认状态（还没研究过）
                // hashNeeded 对得上 1.21.0 的旧公式 → 那条进度是「研究次数」口径，要 +1 级
                // （新旧两条公式在 curLevel ≥ 0 上恒不相等，不会误判）
                bool legacy = cur2 <= 0 || st2.hashNeeded == C.LegacyPoolTechHashOf(cur2);
                if (legacy)
                {
                    cur2 = Mathf.Clamp(cur2 + 1, 1, C.PoolTechProtoMaxLevel);
                    Plugin.Log.LogInfo("能量池科技：把旧进度平移到「等级 = curLevel」口径（curLevel → " +
                                       cur2 + "）");
                }

                long expect = Proto.GetHashNeeded(cur2);
                if (st2.maxLevel != C.PoolTechProtoMaxLevel || st2.hashNeeded != expect || st2.curLevel != cur2)
                {
                    // 换价格后旧的 hashUploaded 可能已经超过新的 hashNeeded，
                    // 那样 AddTechHash 会因为「已经交够了」直接返回、卡住不动，这里归零重来。
                    long uploaded = st2.hashUploaded;
                    if (uploaded < 0 || uploaded >= expect) uploaded = 0L;
                    states[Proto.ID] = new TechState(false, cur2, C.PoolTechProtoMaxLevel,
                                                     uploaded, expect, upoint);
                    _lastCurLevel = cur2;
                    Plugin.Log.LogInfo("能量池科技：修正科技状态 #" + Proto.ID +
                                       "（level " + st2.curLevel + "→" + cur2 +
                                       "，maxLevel " + st2.maxLevel + "→" + C.PoolTechProtoMaxLevel +
                                       "，hashNeeded " + st2.hashNeeded + "→" + expect +
                                       " = " + C.PoolTechCostOf(cur2) + " 引力矩阵）");
                }
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Plugin.Log.LogWarning("能量池科技：补建状态失败: " + e.Message);
            }
        }

        // ---------------- 表操作 ----------------

        private static TechProto FindInArray(TechProto[] arr, int id)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null && arr[i].ID == id)
                    return arr[i];
            }
            return null;
        }

        /// <summary>
        /// 挑一个空闲 id：优先 2999，被占就按 <see cref="C.PoolTechFixedId"/> 往下（再往上）找，
        /// 保证落在 2000~9999（升级页 + 安全区），并且同一个 mod 组合下每次都挑到同一个 id
        /// （科技状态是按 id 存进存档的，id 一变研究进度就丢了）。
        /// </summary>
        private static int PickId(TechProto[] techs)
        {
            var used = new HashSet<int>();
            for (int i = 0; i < techs.Length; i++)
            {
                if (techs[i] != null && techs[i].ID > 0)
                    used.Add(techs[i].ID);
            }
            for (int id = C.PoolTechFixedId; id < 10000; id++)
            {
                if (!used.Contains(id))
                    return id;
            }
            for (int id = 2000; id < C.PoolTechFixedId; id++)
            {
                if (!used.Contains(id))
                    return id;
            }
            return 0;
        }

        /// <summary>
        /// 在「升级」页给节点找位置（1.21.2 重做，按原版真实网格）。
        ///
        /// ============ 之前的错 ============
        ///
        /// <c>TechProto.Position</c> 是**网格坐标**，原版 <c>UITechTree.GetSqueezedPos</c> 里
        /// 换算成像素就是 <c>new Vector2(coord.x * 114f, coord.y * 48f)</c> ——
        /// 1 个 x 单位只有 114 px 宽、1 个 y 单位只有 48 px 高，所以原版升级页节点的
        /// 列间距一般是 3~4 个单位、行间距是 1~2 个单位。
        ///
        /// 1.21.0 从「最右一列再往右」扫，直接把节点丢到屏幕外；
        /// 1.21.1 改成从质心按 <see cref="C.TechColStep"/>(=4) 一环一环往外找 ——
        /// 步长 4 个单位 = 456 px，在紧凑的升级页网格里等于「隔开好几列再放一个」，
        /// 所以节点孤零零挂在旁边、周围空一大片，看着就不像原版科技树的一部分。
        ///
        /// ============ 现在的做法 ============
        ///
        ///   · 先读本页所有原版节点的真实坐标；
        ///   · 用它们**反推**本页的列步长 / 行步长（同行相邻 x 的最小差、同列相邻 y 的最小差），
        ///     这样我们用的间距和原版节点之间的一模一样，而不是猜一个 4；
        ///   · 从本页左上角开始按「行 → 列」顺序找第一个空位（行优先，使节点尽量出现在
        ///     一打开就能看到的位置，而不是页尾）。
        ///
        /// 结果：节点就像原版科技树里正常的一个节点，跟邻居同列、同间距。
        /// </summary>
        private static Vector2 PickUpgradePos(TechProto[] all, int selfId)
        {
            var xs = new List<int>();
            var ys = new List<int>();
            for (int i = 0; i < all.Length; i++)
            {
                TechProto t = all[i];
                if (t == null || t.page != 1)
                    continue;
                if (selfId > 0 && t.ID == selfId)
                    continue;
                xs.Add(Mathf.RoundToInt(t.Position.x));
                ys.Add(Mathf.RoundToInt(t.Position.y));
            }

            // 本页一个节点都没有（极少见）：摆在原点右边一格
            if (xs.Count == 0)
                return new Vector2(1f, 0f);

            // ---- 反推原版网格步长 ----
            // 同一行（y 相同）里最小的 x 差 = 列间距；同一列里最小的 y 差 = 行间距。
            // 原版节点之间绝不重叠，所以这个最小差天然是「安全间距」。
            int dx = 0, dy = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                for (int j = i + 1; j < xs.Count; j++)
                {
                    if (ys[i] == ys[j])
                    {
                        int d = Mathf.Abs(xs[i] - xs[j]);
                        if (d > 0 && (dx == 0 || d < dx)) dx = d;
                    }
                    if (xs[i] == xs[j])
                    {
                        int d = Mathf.Abs(ys[i] - ys[j]);
                        if (d > 0 && (dy == 0 || d < dy)) dy = d;
                    }
                }
            }
            // 取不到（每行每列都只有一个节点）时按原版常见的紧凑间距兜底。
            // 下限的意义：原版节点面板约 300×90 px，而 1 个坐标单位只有 114×48 px，
            // 所以列距至少 3（342 px）、行距至少 2（96 px）才不会互相压住。
            if (dx < 3) dx = 3;
            if (dy < 2) dy = 2;

            int minX = xs[0], minY = ys[0];
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] < minX) minX = xs[i];
                if (ys[i] < minY) minY = ys[i];
            }

            // ---- 行优先找空位 ----
            // 最多往下 16 行、每行 14 列；正常情况第一轮就能命中。
            for (int row = 0; row <= 16; row++)
            {
                int y = minY + row * dy;
                for (int col = 0; col < 14; col++)
                {
                    int x = minX + col * dx;
                    if (!Near(xs, ys, x, y, dx, dy))
                    {
                        Plugin.Log.LogInfo("能量池科技：升级页共 " + xs.Count + " 个原版节点，" +
                                           "反推网格 = 列距 " + dx + " / 行距 " + dy +
                                           "（像素 " + (dx * 114) + "×" + (dy * 48) + "），" +
                                           "落位 = (" + x + "," + y + ")");
                        return new Vector2(x, y);
                    }
                }
            }

            // 全部占满（不可能）：摆在最右一列的下一格
            int fx = minX;
            for (int i = 0; i < xs.Count; i++) if (xs[i] > fx) fx = xs[i];
            return new Vector2(fx + dx, minY);
        }

        /// <summary>
        /// 候选格和已有节点是否贴太近。间距用「反推出来的原版步长」：
        /// 差 >= 一个步长就算不同格（原版节点之间就是这个距离，不会重叠）。
        /// </summary>
        private static bool Near(List<int> xs, List<int> ys, int x, int y, int dx, int dy)
        {
            for (int i = 0; i < xs.Count; i++)
            {
                if (Mathf.Abs(xs[i] - x) < dx && Mathf.Abs(ys[i] - y) < dy)
                    return true;
            }
            return false;
        }

        private static long Pack(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        private static void Append(TechProtoSet set, TechProto t)
        {
            int n = set.dataArray.Length;
            Array.Resize(ref set.dataArray, n + 1);
            set.dataArray[n] = t;
            try
            {
                Type bt = typeof(TechProtoSet).BaseType;
                FieldInfo f = bt != null
                    ? bt.GetField("dataIndices", BindingFlags.Instance | BindingFlags.NonPublic)
                    : null;
                var dict = f != null ? f.GetValue(set) as Dictionary<int, int> : null;
                if (dict != null)
                    dict[t.ID] = n;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("能量池科技：补索引失败: " + e.Message);
            }
        }
    }
}
