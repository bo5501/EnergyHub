using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EnergyHub
{
    /// <summary>
    /// 科技树节点：在「信息矩阵」科技后面挂一个分支，解锁星际能量枢纽。
    ///
    /// 为什么是「运行时」创建而不是像 model / item / recipe 那样在 Awake 里 PreAdd：
    ///   · Awake 时 LDB 还没载入，既拿不到原版科技表，也挑不出空闲 id；
    ///   · 硬编一个 id 有撞号风险（撞到原版科技会直接把科技树搞坏）。
    /// 所以等 LDB 就绪（<see cref="Protos.PreloadAll"/>，正好在 LDBTool 建图标图集之前），
    /// 再从原版科技表里挑一个空闲 id（&lt;2000 = 主科技树那一页）、
    /// **浅拷贝母节点**、改几个字段、追加进 dataArray 并重建索引。
    ///
    /// 浅拷贝的好处：HashNeeded / Items / ItemPoints / Level / MaxLevel / IsLabTech /
    /// IconPath 这些字段全都沿用母节点，不用逐项猜原版数值（猜错就是解锁不了或者一解锁就满级）。
    /// </summary>
    internal static class Tech
    {
        public static bool Done { get; private set; }
        /// <summary>节点是否真的建成了。为 false 时上层会退回「配方永远解锁」的旧行为。</summary>
        public static bool Ok { get; private set; }
        public static TechProto Proto { get; private set; }
        public static TechProto Source { get; private set; }

        private static readonly MethodInfo _memberClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Register()
        {
            if (Done)
                return;
            TechProtoSet set = LDB.techs;
            if (set == null || set.dataArray == null || set.dataArray.Length == 0)
                return;                     // LDB 还没好，下次再试

            Done = true;

            TechProto src = FindMatrixTech(set.dataArray);
            if (src == null)
            {
                Plugin.Log.LogWarning("科技：找不到「信息矩阵」科技节点，跳过注册（物品仍然可通过配方解锁）");
                return;
            }

            int id = PickId(set.dataArray);
            if (id <= 0)
            {
                Plugin.Log.LogWarning("科技：主科技树没有空闲 id，跳过注册");
                return;
            }

            TechProto t;
            try
            {
                t = Clone(src);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("科技：克隆母节点失败: " + e.Message);
                return;
            }

            t.ID = id;
            t.Name = C.LTechName;
            t.SID = "";
            t.sid = "";
            t.Desc = C.LTechDesc;
            t.Conclusion = C.LTechConclusion;
            t.Published = true;
            t.IsHiddenTech = false;
            t.IsObsolete = false;

            // 研究材料（1.7.1）：解锁条件 = 信息矩阵。
            // ItemPoints 的语义（看 LabComponent.InternalUpdateResearch + UITechNode 显示）：
            //   每产 1 hash，实验室消耗 ItemPoints/3600 个该矩阵；
            //   UITechNode 显示的「需要 N 个」= hashNeeded × ItemPoints / 3600。
            //   原版科技这个值大多在 2~60（wiki：items_per_set 通常 20 左右）。
            // 取 20 → 每 180 hash 吃 1 个信息矩阵，母节点 hash 越多、要的矩阵越多，体感与原版一致。
            // IsLabTech 必须为 true：GameHistoryData 只给 IsLabTech 的科技设 LabComponent.matrixPoints，
            // 没有它实验室根本不会消耗矩阵。
            t.Items = new[] { Items.InformationMatrix };
            t.ItemPoints = new[] { 20 };
            t.IsLabTech = true;

            // 单次解锁型（不是可升级的重复科技）：Level = MaxLevel = 0，
            // 这样 TechState.curLevel >= maxLevel 直接成立，hash 攒满就解锁。
            t.Level = 0;
            t.MaxLevel = 0;
            t.LevelCoef1 = 0;
            t.LevelCoef2 = 0;

            // 只挂到信息矩阵这一个前置上，形成「一个分支」而不是把整条主干锁死
            t.PreTechs = new[] { src.ID };
            t.PreTechsImplicit = new int[0];
            t.PreTechsMax = false;

            // 解锁我们的配方（配方解锁后物品才算解锁，见 GameHistoryData.ItemUnlocked）
            t.UnlockRecipes = new[] { C.RecipeId };
            t.UnlockFunctions = new int[0];
            t.UnlockValues = new double[0];
            t.AddItems = new int[0];
            t.AddItemCounts = new int[0];
            t.PropertyOverrideItems = new int[0];
            t.PropertyItemCounts = new int[0];

            // 图标直接沿用母节点（IconPath 指向 Resources 里的科技图标，
            // Preload 时会被 Load 出来，随后 IconSet.Create 会把它拷进图集）
            t.IconPath = src.IconPath;
            t.IconTag = "";
            t.Position = FreePos(src, set.dataArray);

            Append(set, t);

            try
            {
                t.Preload();
                t.Preload2();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("科技：Preload 失败（节点已加入，可能显示不完整）: " + e);
            }

            // 母节点的 postTechArray 之前就构建好了，把新分支补进去
            // （科技树用它判断「有没有后续节点」，缺了不影响功能，但补上更整齐）
            try
            {
                var post = new List<TechProto>(src.postTechArray ?? new TechProto[0]);
                post.Add(t);
                src.postTechArray = post.ToArray();
            }
            catch { }

            Proto = t;
            Source = src;
            C.TechId = id;
            Ok = true;

            Plugin.Log.LogInfo("科技节点已注册: id=" + id + "「" + t.name + "」" +
                               " 前置=#" + src.ID + "「" + src.name + "」" +
                               " 坐标=(" + t.Position.x.ToString("0") + "," + t.Position.y.ToString("0") + ")" +
                               " 解锁配方=" + C.RecipeId +
                               " 图标=" + (t.iconSprite != null ? "ok" : "null") +
                               " 研究需求=" + t.GetHashNeeded(0) + " hash" +
                               " + 信息矩阵×" + (t.GetHashNeeded(0) * 20L / 3600L));
        }

        // ---------------- 母节点 ----------------

        /// <summary>
        /// 找「信息矩阵」科技节点 = 解锁「生产信息矩阵」那条配方的科技。
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
                    if (t == null || t.UnlockRecipes == null)
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
                if (t == null || t.ID >= 2000 || t.Items == null)
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

        // ---------------- id / 坐标 ----------------

        /// <summary>
        /// 挑一个空闲科技 id。必须 &lt; 2000：<c>TechProto.page = ID &gt;= 2000 ? 1 : 0</c>，
        /// 2000 以上是「升级」那一页，挂到那里就等于跑到另一个页面去了。
        /// 从 1999 往下找第一个没用过的，结果是确定性的（同一版本游戏每次算出来都一样），
        /// 存档里的科技状态不会因为重启而错位。
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
        /// 在母节点旁边找一个没被占用的格子（科技树的 Position 是整数格，x 一格 114 px）。
        /// 1.7.1 要求节点「放在信息矩阵节点后面」——科技树是从左往右长的，
        /// 所以第一优先是正右侧一格（x+1, y），其次再往外/上下找。
        /// 都被占了就往外扩，最后退回「母节点下面一格」。
        /// </summary>
        private static Vector2 FreePos(TechProto src, TechProto[] all)
        {
            var used = new HashSet<long>();
            for (int i = 0; i < all.Length; i++)
            {
                TechProto t = all[i];
                if (t == null)
                    continue;
                used.Add(Pack(Mathf.RoundToInt(t.Position.x), Mathf.RoundToInt(t.Position.y)));
            }

            int sx = Mathf.RoundToInt(src.Position.x);
            int sy = Mathf.RoundToInt(src.Position.y);

            // 顺序：正右（节点后面）→ 右二 → 右上/右下 → 右三 → 正上/正下 → 右侧更远
            int[] dx = { 1, 2, 1, 1, 3, 0, 0, 4, 2, 2 };
            int[] dy = { 0, 0, -1, 1, 0, -1, 1, 0, -2, 2 };
            for (int i = 0; i < dx.Length; i++)
            {
                int x = sx + dx[i];
                int y = sy + dy[i];
                if (y < 0)
                    continue;
                if (!used.Contains(Pack(x, y)))
                    return new Vector2(x, y);
            }

            for (int d = 1; d <= 6; d++)
            {
                for (int k = -2; k <= 2; k++)
                {
                    int x = sx + k;
                    int y = sy - d;
                    if (y >= 0 && !used.Contains(Pack(x, y)))
                        return new Vector2(x, y);
                }
            }
            return new Vector2(sx, Mathf.Max(0, sy - 1));
        }

        private static long Pack(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        // ---------------- 表操作 ----------------

        private static TechProto Clone(TechProto src)
        {
            if (_memberClone == null)
                throw new InvalidOperationException("拿不到 MemberwiseClone");
            return (TechProto)_memberClone.Invoke(src, null);
        }

        /// <summary>
        /// 追加到 TechProtoSet 并让 <c>LDB.techs.Select(id)</c> 查得到。
        ///
        /// 不调 <c>ProtoSet.OnAfterDeserialize()</c>：那会顺手把每个 tech 的 name 重置成
        /// <c>Name</c>（本地化 key），英文/日文环境下会把整棵科技树的名字打回中文 key。
        /// 这里只反射拿私有的 dataIndices 字典，补一条 id → index 映射，其余一概不动。
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
                Plugin.Log.LogWarning("科技：补索引失败，改用全量重建: " + e.Message);
            }

            // 兜底：字典拿不到时只能全量重建（极少数情况下会重置 name，中文环境无感）
            set.OnAfterDeserialize();
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
