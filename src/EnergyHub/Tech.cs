using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using xiaoye97;

namespace EnergyHub
{
    /// <summary>
    /// 科技树节点：挂在「能量枢纽」科技（解锁原版能量枢纽建筑那条）的右下方一格，
    /// 形成一条分支出「星际能量枢纽塔」；找不到那条科技时退回「信息矩阵」后面。
    ///
    /// ============ 1.16.0：为什么整段重写 ============
    ///
    /// 1.15.x 是「运行时手工追加」：在 <see cref="Protos.PreloadAll"/>（或者更早的 Update 里）
    /// 反射把 TechProto 塞进 <c>LDB.techs.dataArray</c>。日志实测这条路是错的：
    ///
    ///     [星际能量枢纽塔] 科技节点已注册: id=1999 ... 锚点坐标=(37,9) 坐标=(41,5)
    ///     [LDBTool] Pre Loading...
    ///     [LDBTool] Add 1947 ... to TechProtoSet.      ← 创世之书这时候才整批建表
    ///
    /// 我们的节点建在 LDBTool 建表**之前**，等 LDBTool 按自己的注册队列重建 TechProtoSet 时，
    /// 我们亲手塞进去的那条就不在表里了（锚点坐标还是原版的 (37,9) 也说明创世还没灌表）。
    /// 节点不在 LDB.techs 里 → UITechTree 建节点时遍历不到 → 科技树里永远看不到。
    ///
    /// 创世之书（ProjectGenesis）的做法是标准答案，见它 src/Utils/JsonDataUtils.cs：
    ///   <c>if (LDB.techs.Exist(id)) protoJson.ToProto(...); else LDBTool.PreAddProto(protoJson.ToProto());</c>
    /// 也就是**只往 LDBTool 的 PreAdd 队列里放**，由 LDBTool 在建表那一批统一加进 dataArray；
    /// 它自己的 PostAddDataAction 里再统一 Preload/Preload2 + OnAfterDeserialize。
    ///
    /// 所以这里改成同一套路，分两步：
    ///   1. <see cref="PreAdd"/>（Awake）：构造一个 id 固定为 1999 的 TechProto，交给 LDBTool；
    ///   2. <see cref="Layout"/>（LDBTool 的 PostAddDataAction，此时原版 + 创世的科技都已在表里）：
    ///      这时才反查锚点、算坐标、写前置，并让母节点的 postTechArray 含本节点（画连线）。
    ///
    /// 节点 id 必须固定而不能运行时挑：PreAdd 发生在 Awake，那时 LDB 还没载入，挑不了空闲 id。
    /// 1999 在所有已知环境里都是空的（原版科技最大 1512 段；创世 3.2.4 内嵌 techs.json 最大 1963），
    /// 且 &lt; 2000 才落在主科技树那一页（<c>TechProto.page = ID &gt;= 2000 ? 1 : 0</c>）。
    /// 万一真被别的 MOD 占了，<see cref="Layout"/> 会检测到并退回「运行时挑空闲 id 追加」。
    /// </summary>
    internal static class Tech
    {
        /// <summary>节点是否已经就位（坐标、前置、图标都写好了）。</summary>
        public static bool Ok { get; private set; }

        /// <summary>是否已经跑过 <see cref="Layout"/>（不在表里就别指望科技树里有它）。</summary>
        public static bool LaidOut { get; private set; }

        public static TechProto Proto { get; private set; }
        public static TechProto Source { get; private set; }

        /// <summary>本节点最终挂在哪个前置上（写日志用）。</summary>
        public static string AnchorNote = "";

        private static TechProto _ours;          // 交给 LDBTool 的那个对象
        private static int _verifyFrame;
        private static int _rescues;

        private static readonly MethodInfo _memberClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        // ---------------- 1) Awake：提交给 LDBTool ----------------

        /// <summary>
        /// 在 Awake 里调用（必须早于 LDB 建表）。只放一个「骨架」进 LDBTool 的 PreAdd 队列，
        /// 位置 / 前置 / 图标这些要等科技表建好才知道的东西，留给 <see cref="Layout"/>。
        /// </summary>
        public static void PreAdd()
        {
            if (_ours != null)
                return;

            var t = new TechProto
            {
                ID = C.TechFixedId,
                Name = C.LTechName,
                SID = "",
                sid = "",
                Desc = C.LTechDesc,
                Conclusion = C.LTechConclusion,
                Published = true,
                IsHiddenTech = false,
                IsObsolete = false,

                // 这几个数组 TechProto.Preload / Preload2 / UITechNode 会直接取 .Length，
                // 留 null 就是 NRE，全部给空数组。
                PreItem = new int[0],
                PreTechs = new int[0],
                PreTechsImplicit = new int[0],
                PreTechsMax = false,
                PropertyOverrideItems = new int[0],
                PropertyItemCounts = new int[0],
                UnlockRecipes = new[] { C.RecipeId },
                UnlockFunctions = new int[0],
                UnlockValues = new double[0],
                AddItems = new int[0],
                AddItemCounts = new int[0],

                // 研究材料：信息矩阵（ItemPoints 20 → 每 180 hash 吃 1 个，
                // UITechNode 显示的「需要 N 个」= hashNeeded × 20 / 3600）。
                Items = new[] { Items.InformationMatrix },
                ItemPoints = new[] { 20 },
                IsLabTech = true,

                // 单次解锁型：Level = MaxLevel = 0，hash 攒满即解锁。
                Level = 0,
                MaxLevel = 0,
                LevelCoef1 = 0,
                LevelCoef2 = 0,

                // 先给个原版「星际电力运输」的量级，Layout 里再按真实锚点覆盖。
                HashNeeded = 216000L,

                // 图标先用原版能量枢纽那条科技的（Icons/Tech/1512），Layout 里换成锚点的。
                IconPath = C.TechIconFallback,
                IconTag = "",
                Position = Vector2.zero,
            };

            LDBTool.PreAddProto(t);
            _ours = t;

            Plugin.Log.LogInfo("科技：已提交节点骨架 id=" + C.TechFixedId +
                               "（走 LDBTool PreAdd 队列，与创世之书同一套路），坐标与前置等建表后再定");
        }

        // ---------------- 2) PostAdd：定位置 / 定前置 / 画连线 ----------------

        /// <summary>
        /// 在 LDBTool 的 PostAddDataAction（<see cref="Protos.PreloadAll"/> 里）调用。
        /// 此时原版科技、创世之书的 143 个科技、以及我们自己的节点都已经在
        /// <c>LDB.techs.dataArray</c> 里了，读到的坐标才是最终值。
        /// </summary>
        public static void Layout()
        {
            if (LaidOut)
                return;

            TechProtoSet set = LDB.techs;
            if (set == null || set.dataArray == null || set.dataArray.Length == 0)
                return;                       // LDB 还没好，下次再说

            // ---- 先把「表里那一条」找出来 ----
            TechProto t = FindInArray(set.dataArray, C.TechFixedId);
            // 正常情况下 LDBTool 加进去的就是我们提交的那个对象；万一它内部另建了实例，
            // 用本地化 key 再认一次（Name 是我们自己的 key，不会和别的 MOD 撞）。
            bool mine = t != null && (ReferenceEquals(t, _ours) ||
                                      string.Equals(t.Name, C.LTechName, StringComparison.Ordinal));
            if (mine && !ReferenceEquals(t, _ours))
            {
                Plugin.Log.LogInfo("科技：LDBTool 建表时换了对象实例，按本地化 key 认回本节点 id=" + t.ID);
                _ours = t;
            }

            if (t != null && !mine)
            {
                // id 被别的 MOD 占了（理论上没有，原版/创世都不用到 1999）。
                // 让位：换一个空闲 id，运行时追加（这条老路依然保留，作为最后手段）。
                Plugin.Log.LogWarning("科技：id " + C.TechFixedId + " 已被「" + t.Name +
                                      "」占用，改用运行时分配的 id");
                t = null;
            }

            if (t == null)
            {
                int id = PickId(set.dataArray);
                if (id <= 0)
                {
                    Plugin.Log.LogWarning("科技：主科技树没有空闲 id，跳过注册");
                    AlwaysUnlocked("主科技树没有空闲 id");
                    LaidOut = true;
                    return;
                }
                t = _ours ?? CloneOrNew();
                t.ID = id;
                Append(set, t);
                Plugin.Log.LogInfo("科技：节点不在 LDBTool 的建表结果里，已运行时追加 id=" + id);
            }

            // ---- 锚点：解锁能量枢纽建筑的那条科技 ----
            TechProto src = FindEnergyExchangerTech(set.dataArray);
            if (src != null && src.ID != t.ID)
            {
                AnchorNote = "能量枢纽科技";
            }
            else
            {
                src = FindMatrixTech(set.dataArray);
                AnchorNote = "信息矩阵（未找到能量枢纽科技，兜底）";
                if (src != null)
                    Plugin.Log.LogWarning("科技：没找到解锁能量枢纽的科技，退回「信息矩阵」锚点");
            }

            if (src == null)
            {
                Plugin.Log.LogWarning("科技：找不到任何可用的锚点科技，跳过注册");
                AlwaysUnlocked("找不到可用锚点科技");
                LaidOut = true;
                return;
            }

            // ---- 写字段 ----
            t.Position = PosAfter(src, set.dataArray, t.ID);
            t.PreTechs = new[] { src.ID };
            t.PreTechsImplicit = new int[0];
            t.PreTechsMax = false;
            t.HashNeeded = src.GetHashNeeded(0);
            t.IconPath = string.IsNullOrEmpty(src.IconPath) ? C.TechIconFallback : src.IconPath;
            t.IconTag = "";
            t.Published = true;
            t.IsHiddenTech = false;

            try { t.Preload(); }
            catch (Exception e) { Plugin.Log.LogWarning("科技：Preload 失败: " + e.Message); }

            // Preload2 会算 preTechArray / postTechArray / unlockRecipeArray……
            // 关键：母节点的 postTechArray 也得重算一次，科技树才会从它画一条线到我们这里
            // （箭头是「父节点」按自己的 postTechArray 生成的，见 UITechNode.SetTechProto）。
            try { t.Preload2(); }
            catch (Exception e) { Plugin.Log.LogWarning("科技：本节点 Preload2 失败: " + e.Message); }

            try { src.Preload2(); }
            catch (Exception e) { Plugin.Log.LogWarning("科技：母节点 Preload2 失败: " + e.Message); }

            Proto = t;
            Source = src;
            C.TechId = t.ID;
            Ok = true;
            LaidOut = true;
            _verifyFrame = Time.frameCount;

            Plugin.Log.LogInfo("科技节点已就位: id=" + t.ID + "「" + t.name + "」" +
                               " 前置=#" + src.ID + "「" + src.name + "」（" + AnchorNote + "）" +
                               " 坐标=(" + t.Position.x.ToString("0") + "," + t.Position.y.ToString("0") + ")" +
                               " 锚点坐标=(" + src.Position.x.ToString("0") + "," + src.Position.y.ToString("0") + ")" +
                               " 科技表=" + set.dataArray.Length + " 条" +
                               " 连线=" + (src.postTechArray != null && ContainsProto(src.postTechArray, t)
                                   ? "ok" : "无") +
                               " 研究需求=" + t.GetHashNeeded(0) + " hash" +
                               " + 信息矩阵×" + (t.GetHashNeeded(0) * 20L / 3600L));
        }

        /// <summary>每帧调：没就位就等，就位后每 5 秒确认节点还在表里、坐标没被别的 MOD 改掉。</summary>
        public static void Tick()
        {
            if (!LaidOut)
            {
                // 只在 LDBTool 建表（PreloadAll）跑过之后才补：那之前我们的 proto 还在
                // LDBTool 的注册队列里没进 dataArray，这时去找会误判成「丢了」而重复追加。
                if (Protos.IsPreloaded)
                    Layout();
                return;
            }

            if (Time.frameCount - _verifyFrame < 300)
                return;
            _verifyFrame = Time.frameCount;

            try
            {
                if (LDB.techs == null || LDB.techs.dataArray == null)
                    return;

                TechProto cur = FindInArray(LDB.techs.dataArray, C.TechId);
                if (cur == null || !ReferenceEquals(cur, Proto))
                {
                    // 被别的 MOD 整表重建冲掉了 → 重新走一遍（最多 3 次，避免死循环刷日志）
                    if (_rescues >= 3)
                        return;
                    _rescues++;
                    Plugin.Log.LogWarning("科技：节点已不在科技表里（多半是别的 MOD 重建了 techs 表），重建第 " +
                                          _rescues + " 次");
                    LaidOut = false;
                    Ok = false;
                    Proto = null;
                    Source = null;
                    Layout();
                    return;
                }

                // 坐标被别的 MOD 改掉（创世的 techs.json 不认我们的 id，一般不会，但便宜）
                TechProto src = FindEnergyExchangerTech(LDB.techs.dataArray);
                if (src != null && src.ID != Proto.ID)
                {
                    Vector2 want = PosAfter(src, LDB.techs.dataArray, Proto.ID);
                    if ((Proto.Position - want).sqrMagnitude > 0.01f)
                    {
                        Plugin.Log.LogWarning("科技：坐标被改动 (" + Proto.Position.x.ToString("0") + "," +
                                              Proto.Position.y.ToString("0") + ") → (" +
                                              want.x.ToString("0") + "," + want.y.ToString("0") + ")");
                        Proto.Position = want;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("科技：校验失败: " + e.Message);
            }
        }

        // ---------------- 母节点 ----------------

        /// <summary>
        /// 找「能量枢纽」科技 = 解锁能量枢纽建筑（Energy Exchanger）那条配方的科技。
        ///
        /// 不能按名字找：这条科技在原版叫「星际电力运输」、在创世之书里叫「T星际电力运输」，
        /// 名字里根本没有「能量枢纽」三个字；而科技名本身又是本地化文本，换语言就变。
        /// 所以走「物品 → PrefabDesc.isPowerExchanger → 配方 → 科技的 UnlockRecipes」这条链，
        /// 全是数据标志位，跟语言、和 MOD 的重命名都无关。
        /// 兜底才按名字猜（能量枢纽 / 星际电力运输 / Energy Exchanger / Interstellar Power）。
        /// </summary>
        private static TechProto FindEnergyExchangerTech(TechProto[] techs)
        {
            // 第一步：先把「能量枢纽」这个物品找出来，三条路子取并集（哪个能用用哪个）
            //   · 原版固定 id 2209（创世之书保留原版物品 id，实测 3.2.3/3.2.4 都是 2209）
            //   · PrefabDesc.isPowerExchanger 标志位（LDB 已 Preload 时最准，与语言无关）
            //   · 物品名含「能量枢纽 / Energy Exchanger」（最后兜底，怕将来 id 和标志都变）
            var energyItems = new HashSet<int> { 2209 };
            try
            {
                ItemProto[] items = LDB.items != null ? LDB.items.dataArray : null;
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        ItemProto it = items[i];
                        if (it == null || it.ID == C.ItemId)   // 排除本 MOD 自己的「星际能量枢纽塔」
                            continue;
                        if (it.prefabDesc != null && it.prefabDesc.isPowerExchanger)
                            energyItems.Add(it.ID);
                        if (Contains(it.Name, "能量枢纽") || Contains(it.Name, "Energy Exchanger") ||
                            Contains(it.name, "能量枢纽") || Contains(it.name, "Energy Exchanger"))
                            energyItems.Add(it.ID);
                    }
                }
            }
            catch { }

            // 第二步：物品 → 产出它的配方
            var recipes = new HashSet<int>();
            try
            {
                RecipeProto[] rs = LDB.recipes != null ? LDB.recipes.dataArray : null;
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        RecipeProto r = rs[i];
                        if (r == null || r.Results == null)
                            continue;
                        for (int k = 0; k < r.Results.Length; k++)
                        {
                            if (energyItems.Contains(r.Results[k]))
                            {
                                recipes.Add(r.ID);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            if (recipes.Count > 0)
            {
                TechProto best = null;
                for (int i = 0; i < techs.Length; i++)
                {
                    TechProto t = techs[i];
                    if (t == null || t.ID <= 0 || t.ID >= 2000 || t.UnlockRecipes == null)
                        continue;
                    if (t.ID == C.TechFixedId)     // 别把自己当锚点
                        continue;
                    for (int k = 0; k < t.UnlockRecipes.Length; k++)
                    {
                        if (!recipes.Contains(t.UnlockRecipes[k]))
                            continue;
                        // 万一有多条科技都解锁这条配方，取位置更靠前的那个（y 更大的在上方、
                        // 同高则取更靠左的），这样挂出来的分支落在树的开头而不是末端
                        if (best == null || t.Position.y > best.Position.y ||
                            (t.Position.y == best.Position.y && t.Position.x < best.Position.x))
                            best = t;
                        break;
                    }
                }
                if (best != null)
                    return best;
            }

            // 兜底：按名字/本地化 key 猜
            string[] keys = { "能量枢纽", "星际电力运输", "Energy Exchanger", "Interstellar Power" };
            for (int i = 0; i < techs.Length; i++)
            {
                TechProto t = techs[i];
                if (t == null || t.ID <= 0 || t.ID >= 2000 || t.ID == C.TechFixedId)
                    continue;
                if (Contains(t.Name, "EnergyHub."))   // 本 MOD 自己的节点，别认成锚点
                    continue;
                for (int k = 0; k < keys.Length; k++)
                {
                    if (Contains(t.Name, keys[k]) || Contains(t.name, keys[k]))
                        return t;
                }
            }
            return null;
        }

        /// <summary>
        /// 找「信息矩阵」科技节点 = 解锁「生产信息矩阵」那条配方的科技（兜底锚点）。
        ///
        /// 不按名字找：科技名是本地化文本（"信息矩阵" / "Information Matrix"），
        /// 而且中英之外还有别的语言。按「配方 → 科技」反查最稳：
        ///   1. 在配方表里找产物是 6004（信息矩阵）的那条；
        ///   2. 在科技表里找 UnlockRecipes 含这条配方的科技。
        /// 兜底（万一配方结构变了）：找 Items 里需要 6004 的科技，取坐标最靠右的那个。
        /// </summary>
        private static TechProto FindMatrixTech(TechProto[] techs)
        {
            int recipe = 0;
            try
            {
                RecipeProto[] rs = LDB.recipes != null ? LDB.recipes.dataArray : null;
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        if (rs[i] == null || rs[i].Results == null)
                            continue;
                        for (int k = 0; k < rs[i].Results.Length; k++)
                        {
                            if (rs[i].Results[k] == Items.InformationMatrix)
                            {
                                recipe = rs[i].ID;
                                break;
                            }
                        }
                        if (recipe > 0)
                            break;
                    }
                }
            }
            catch { }

            if (recipe > 0)
            {
                for (int i = 0; i < techs.Length; i++)
                {
                    TechProto t = techs[i];
                    if (t == null || t.UnlockRecipes == null || t.ID == C.TechFixedId)
                        continue;
                    for (int k = 0; k < t.UnlockRecipes.Length; k++)
                    {
                        if (t.UnlockRecipes[k] == recipe)
                            return t;
                    }
                }
            }

            // 兜底：需要信息矩阵当研究材料的科技里，取最靠右的（主干末端）
            TechProto best = null;
            for (int i = 0; i < techs.Length; i++)
            {
                TechProto t = techs[i];
                if (t == null || t.ID >= 2000 || t.ID == C.TechFixedId || t.Items == null)
                    continue;
                bool usesMatrix = false;
                for (int k = 0; k < t.Items.Length; k++)
                {
                    if (t.Items[k] == Items.InformationMatrix)
                    {
                        usesMatrix = true;
                        break;
                    }
                }
                if (!usesMatrix)
                    continue;
                if (best == null || t.Position.x > best.Position.x)
                    best = t;
            }
            return best;
        }

        private static bool Contains(string s, string key)
        {
            return !string.IsNullOrEmpty(s) &&
                   s.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------------- id / 坐标 ----------------

        /// <summary>
        /// 挑一个空闲科技 id（只在「固定 id 被别的 MOD 占了」时用）。
        /// 必须 &lt; 2000：<c>TechProto.page = ID &gt;= 2000 ? 1 : 0</c>，
        /// 2000 以上是「升级」那一页，挂到那里就等于跑到另一个页面去了。
        /// </summary>
        private static int PickId(TechProto[] techs)
        {
            var used = new bool[2000];
            for (int i = 0; i < techs.Length; i++)
            {
                TechProto t = techs[i];
                if (t != null && t.ID > 0 && t.ID < 2000)
                    used[t.ID] = true;
            }
            for (int id = 1999; id >= 100; id--)
            {
                if (!used[id])
                    return id;
            }
            return 0;
        }

        /// <summary>
        /// 在锚点科技（能量枢纽）后面挑一格摆本节点。
        ///
        /// 用户指定的位置：挂在「能量枢纽」科技那一支的右下方，也就是卫星配电系统
        /// 正下方那格空位。栅格换算见 <see cref="C.TechColStep"/>：
        ///   创世之书里能量枢纽科技在 (21,-3)，目标格 (25,-7) = 右一格(+4) + 下一行(-4)。
        ///
        /// 顺序：右下（目标位）→ 同一列继续往下 → 正右 → 正下 → 右二 →
        /// 逐格扫描兜底。每格都按「已占用集合」判定，
        /// 所以换了创世版本、或别的 MOD 把这几格占了，也不会叠在一起。
        /// </summary>
        private static Vector2 PosAfter(TechProto src, TechProto[] all, int selfId)
        {
            var used = new HashSet<long>();
            for (int i = 0; i < all.Length; i++)
            {
                TechProto t = all[i];
                if (t == null)
                    continue;
                // 自己已经在表里了，不能把自己当成「被占用」，否则目标格会被自己挡住。
                if (selfId > 0 && t.ID == selfId)
                    continue;
                used.Add(Pack(Mathf.RoundToInt(t.Position.x), Mathf.RoundToInt(t.Position.y)));
            }

            int sx = Mathf.RoundToInt(src.Position.x);
            int sy = Mathf.RoundToInt(src.Position.y);
            int c = C.TechColStep;      // 4 = 一列
            int r = C.TechRowStep;      // 4 = 一行

            // 原版科技树只往上长，但创世之书大量使用负坐标（它的 y 一直到 -43），
            // 所以这里不限制 y 的正负，只留一个保险上限。
            int[] dx = { c, c, c, 0, c, 2 * c, -c, c, c, 2 * c, 0, 3 * c, c, c };
            int[] dy = { -r, -2 * r, -3 * r, -r, 0, -r, -r, r, -4 * r, -2 * r, -2 * r, -r, -5 * r, -6 * r };
            for (int i = 0; i < dx.Length; i++)
            {
                int x = sx + dx[i];
                int y = sy + dy[i];
                if (Mathf.Abs(y) > 400)
                    continue;
                if (!used.Contains(Pack(x, y)))
                    return new Vector2(x, y);
            }

            // 兜底：大型 MOD 会把科技树铺得很密，按整格往下、往右扫，保证一定有位置
            for (int col = 1; col <= 8; col++)
            {
                for (int row = 1; row <= 10; row++)
                {
                    int x = sx + col * c;
                    int y = sy - row * r;
                    if (Mathf.Abs(y) <= 400 && !used.Contains(Pack(x, y)))
                        return new Vector2(x, y);
                }
            }
            return new Vector2(sx, sy - r);
        }

        private static long Pack(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        // ---------------- 表操作（只给「id 被占用 / 节点被冲掉」的兜底用） ----------------

        private static TechProto FindInArray(TechProto[] arr, int id)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null && arr[i].ID == id)
                    return arr[i];
            }
            return null;
        }

        private static bool ContainsProto(TechProto[] arr, TechProto target)
        {
            for (int i = 0; i < arr.Length; i++)
                if (ReferenceEquals(arr[i], target))
                    return true;
            return false;
        }

        private static TechProto CloneOrNew()
        {
            // 理论上不会走到（_ours 总是存在），保个底而已
            TechProto src = null;
            try
            {
                TechProto[] arr = LDB.techs != null ? LDB.techs.dataArray : null;
                if (arr != null)
                    src = FindEnergyExchangerTech(arr);
            }
            catch { }

            if (src != null && _memberClone != null)
            {
                try { return (TechProto)_memberClone.Invoke(src, null); }
                catch { }
            }
            return _ours ?? new TechProto();
        }

        /// <summary>
        /// 追加到 TechProtoSet 并让 <c>LDB.techs.Select(id)</c> 查得到（仅兜底路径使用）。
        ///
        /// 不调 <c>ProtoSet.OnAfterDeserialize()</c>：那会把每个 tech 的 name 重置成
        /// <c>Name</c>（本地化 key），英文/日文环境下会把整棵科技树的名字打回中文 key。
        /// 这里只反射拿私有的 dataIndices 字典，补一条 id → index 映射。
        /// </summary>
        private static void Append(TechProtoSet set, TechProto t)
        {
            int n = set.dataArray.Length;
            Array.Resize(ref set.dataArray, n + 1);
            set.dataArray[n] = t;

            try
            {
                Type bt = typeof(TechProtoSet).BaseType;      // ProtoSet<TechProto>
                FieldInfo f = bt != null
                    ? bt.GetField("dataIndices", BindingFlags.Instance | BindingFlags.NonPublic)
                    : null;
                var dict = f != null ? f.GetValue(set) as Dictionary<int, int> : null;
                if (dict != null)
                {
                    dict[t.ID] = n;
                    return;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("科技：补索引失败: " + e.Message);
            }
        }

        /// <summary>
        /// 节点确实建不出来时的最后兜底：把物品改成「永远解锁」（UnlockKey = -1），
        /// 否则配方永远锁着、玩家造不出建筑 —— 那比「少一个科技节点」严重得多。
        /// </summary>
        private static void AlwaysUnlocked(string why)
        {
            try
            {
                ItemProto it = LDB.items != null ? LDB.items.Select(C.ItemId) : null;
                if (it == null)
                    return;
                if (it.UnlockKey == -1)
                    return;
                it.UnlockKey = -1;
                it.FindPreTech();
                Plugin.Log.LogWarning("科技：" + why + " —— 已把星际能量枢纽塔改回「永远解锁」");
            }
            catch { }
        }

        /// <summary>
        /// 老存档里没有我们这个科技 id 的 TechState（Import 只读存档里的条目），
        /// 缺了会拿到默认结构体（hashNeeded = 6000000、curLevel = maxLevel = 0），
        /// 于是要提交 600 万 hash 才能解锁。这里按 proto 的真实数值补一条。
        /// 每帧调一次也没关系：ContainsKey 命中就直接返回。
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

                int upoint = 0;
                if (Proto.Items != null && Proto.Items.Length > 0 && Proto.ItemPoints != null &&
                    Proto.ItemPoints.Length > 0 && Proto.Items[0] == 6006)
                    upoint = Proto.ItemPoints[0];

                long expect = Proto.GetHashNeeded(Proto.Level);

                if (!states.ContainsKey(Proto.ID))
                {
                    states[Proto.ID] = new TechState(false, Proto.Level, Proto.MaxLevel, 0L, expect, upoint);
                    Plugin.Log.LogInfo("科技：已为老存档补建科技状态 #" + Proto.ID +
                                       "（需要 hash=" + expect + "）");
                    return;
                }

                // 还有一种情况：游戏在 PrepareTick / VerifyCurrentTech 里取默认 TechState 时
                // 顺手把 (hashNeeded=6000000) 写回了字典，导致这个节点要提交 600 万 hash。
                // 只在「还没解锁 + 一点进度都没有」时纠正，有进度的存档不动，免得吞掉玩家已投入的 hash。
                TechState st = states[Proto.ID];
                if (!st.unlocked && st.hashUploaded == 0L && st.hashNeeded != expect)
                {
                    states[Proto.ID] = new TechState(false, Proto.Level, Proto.MaxLevel, 0L, expect, upoint);
                    Plugin.Log.LogInfo("科技：修正科技状态 #" + Proto.ID +
                                       "（hashNeeded " + st.hashNeeded + " → " + expect + "）");
                }
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Plugin.Log.LogWarning("补建科技状态失败: " + e.Message);
            }
        }
    }
}
