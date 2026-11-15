using System.Collections.Generic;
using CommonAPI.Systems;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace EnergyHub
{
    /// <summary>注册 ModelProto / ItemProto / RecipeProto 与本地化。</summary>
    internal static class Protos
    {
        public static bool Registered { get; private set; }

        private static Sprite _icon;

        public static void RegisterAll()
        {
            if (Registered)
                return;

            RegisterStrings();

            ModelProto model = new ModelProto
            {
                ID = C.ModelId,
                Name = C.ItemId.ToString(),
                SID = "",
                sid = "",
                OverrideName = "",
                ObjectType = EObjectType.Entity,
                RendererType = 0,
                RotSymmetry = 4,
                HpMax = 80000,
                HpUpgrade = 0,
                HpRecover = 0,
                RuinType = 0,      // 不生成残骸（RuinId 0 会导致取到模型 0）
                RuinId = 0,
                RuinCount = 0,
                RuinLifeTime = 0,
                PrefabPath = "",   // 空 → ModelProto.Preload 不会去 Resources.Load 原版 prefab
            };
            LDBTool.PreAddProto(model);
            ItemProto item = new ItemProto
            {
                ID = C.ItemId,
                Name = C.LItemName,
                SID = "",
                Type = EItemType.Logistics,   // 与原版「能量枢纽」一致
                SubID = 0,
                StackSize = 20,
                Grade = 0,                    // 0 = 无升级链（Grade>0 会去读 Upgrades）
                Upgrades = new int[0],        // 不能留 null：ItemProto.Preload / GetUpgradeID 会取 Length
                IsEntity = true,
                CanBuild = true,
                BuildInGas = false,
                ModelIndex = C.ModelId,
                ModelCount = 1,
                HpMax = 3000,
                IconPath = "",                // 空 → 保留我们运行时塞进去的 _iconSprite
                Description = C.LItemDesc,
                GridIndex = C.GridIndex,
                BuildIndex = C.BuildIndex,    // UIBuildMenu.StaticLoad 用它排建造栏
                BuildMode = 1,                // 单体建造
                // 1.7.0：解锁改由科技树管。
                //   -1 = 永远解锁（以前为了「开局就能搓」）；
                //   现在必须研究「星际能量枢纽塔」科技才解锁，所以填 0，
                //   ItemUnlocked 会顺着 recipes 去查 recipeUnlocked（见 GameHistoryData.ItemUnlocked）。
                UnlockKey = 0,
                PreTechOverride = 0,
                DescFields = new int[0],      // 不能留 null：UIItemTip 会取 Length
            };
            ApplyIcon(item);             // 必须在 LDB 载入前塞好，配方 Preload 会继承物品图标
            LDBTool.PreAddProto(item);

            // 配方：5 能量枢纽 + 5 射线接收站 + 10 蓄电器 + 5 处理器组件（1.7.0）
            //
            // 原料 id 在 Awake 阶段是拿不到的（LDB 还没载入），所以这里先放占位值，
            // 等 PreloadAll（LDB 就绪）时按中英文名字解析出来再填回去，见 ResolveRecipeItems。
            // 占位必须用 1（铁矿）这种一定存在的 id：RecipeProto.Preload 会
            // LDB.items.Select(Items[i]).Productive，取到 null 会直接 NRE。
            RecipeProto recipe = new RecipeProto
            {
                ID = C.RecipeId,
                Name = C.LRecipeName,
                SID = "",
                Type = ERecipeType.Assemble,
                TimeSpend = 300,   // 5 秒
                Items = new[] { ItemProto.kAccumulator, ItemProto.kAccumulator, ItemProto.kAccumulator, ItemProto.kAccumulator },
                ItemCounts = new[] { 5, 5, 100, 5 },
                Results = new[] { C.ItemId },
                ResultCounts = new[] { 1 },
                GridIndex = C.GridIndex,
                Handcraft = true,
                Explicit = true,
                Description = C.LRecipeDesc,
                IconPath = "",     // 空 → 继承产物物品图标
                IconTag = "",
                NonProductive = false,
            };
            LDBTool.PreAddProto(recipe);

            // 只走 PreAdd：LDBTool 的 Pre/Post 是两批独立的注册队列，
            // 两边都加会让同一个 proto 被塞进 dataArray 两次（"Duplicate RecipeId"）。
            // Pre 批次在 VFPreload.InvokeOnLoadWorkEnded 的 Prefix 阶段加入，
            // 正好赶在 LDBTool 自己的 Postfix（Init* + IconSet.Create）之前。

            Registered = true;
            Plugin.Log.LogInfo("proto 注册完成: model=" + C.ModelId + " item=" + C.ItemId +
                               " recipe=" + C.RecipeId + " buildIndex=" + C.BuildIndex);
        }

        // ---------------- 预加载 ----------------

        private static bool _preloaded;

        public static bool IsPreloaded => _preloaded;

        /// <summary>
        /// LDBTool 的 AddProtosToSet 只把 proto 追加进 dataArray，**不会**调用 Preload()。
        /// 不自己调的话：
        ///   - ModelProto.prefabDesc 一直是 null → SkillSystem..ctor 里 prefabDesc.roughRadius 直接 NRE
        ///   - ItemProto.prefabDesc/index/name/recipes 全没初始化
        /// 必须在 LDBTool 的 Postfix（ItemProto.Init* + IconSet.Create）之前跑完。
        /// </summary>
        public static void PreloadAll()
        {
            if (_preloaded)
                return;

            var items = LDB.items;
            var models = LDB.models;
            var recipes = LDB.recipes;
            if (items == null || items.dataArray == null ||
                models == null || models.dataArray == null ||
                recipes == null || recipes.dataArray == null)
                return;

            ModelProto model = FindModel();
            if (model == null)
                return;   // LDBTool 还没把我们的 proto 加进来

            _preloaded = true;
            Plugin.Log.LogInfo("开始预加载 mod proto…");

            Patch_ModelArray.Ensure();          // modelArray[666] = model

            try
            {
                model.Preload();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("ModelProto.Preload 失败: " + e);
            }

            Patch_ModelArray.Ensure();          // Preload 之后 object 可能被重建，再确保一次

            // 1.7.0：配方原料要先解析出来，再预加载物品 —— ItemProto.Preload 会调
            // FindRecipes/ComputeRawMats，顺着 maincraft.Items 去算原料，占位 id 会算错。
            RecipeProto recipe = FindRecipe();
            ResolveRecipeItems(recipe);

            // 科技节点（1.16.0）：proto 骨架在 Awake 就交给 LDBTool 建表了，这里只补
            // 位置 / 前置 / 图标 / postTechArray。必须跑在 item.Preload / recipe.Preload 之前：
            // 这两个 Preload 末尾都会调 FindPreTech()，它是「遍历科技表找
            // UnlockRecipes 含本配方的那一条」。节点还没进表时，物品和配方的 preTech 就是 null，
            // 制造台/科技树上的「由 XX 解锁」会变成空白。
            try
            {
                Tech.Layout();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("科技节点布置失败: " + e);
            }

            // 1.21.0：能量池研究节点（升级页）。必须排在 Tech.Layout 之后：
            // 它要把「解锁本建筑的那条科技」当成前置。
            try
            {
                PoolTech.Layout();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("能量池科技节点布置失败: " + e);
            }

            // 兜底：科技节点确实排不出来（找不到锚点 / 没空闲 id / 异常）时，
            // 把物品改回「永远解锁」，否则配方永远锁着，玩家造不出建筑 ——
            // 那比「解锁少一个科技节点」严重得多。
            if (Tech.LaidOut && !Tech.Ok)
            {
                ItemProto fallback = FindItem();
                if (fallback != null)
                    fallback.UnlockKey = -1;
                Plugin.Log.LogWarning("科技节点不可用：已把星际能量枢纽塔改回「永远解锁」");
            }

            ItemProto item = FindItem();
            if (item != null)
            {
                ApplyIcon(item);                // 配方 Preload 会继承物品图标
                ResolveBuildIndex(item);        // 1.20.0：挂到「电力」页的 F10
                try
                {
                    item.Preload(IndexOf(items.dataArray, item));
                    Plugin.Log.LogInfo("ItemProto 预加载完成: index=" + item.index +
                                       " prefabDesc=" + (item.prefabDesc != null ? "ok" : "null"));
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("ItemProto.Preload 失败: " + e);
                }
            }

            if (recipe != null)
            {
                try
                {
                    recipe.Preload(IndexOf(recipes.dataArray, recipe));
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("RecipeProto.Preload 失败: " + e);
                }
            }

            // VFPreload 里的 InitRecipeItems 也是在我们加配方之前跑的，
            // 不补这一下，组装机（recipeExecuteData 里没有 300）做不出我们的建筑。
            try
            {
                RecipeProto.InitRecipeItems();
                Plugin.Log.LogInfo("已补跑 InitRecipeItems: 配方执行数据=" +
                                   (RecipeProto.recipeExecuteData != null
                                       ? RecipeProto.recipeExecuteData.Count : 0) +
                                   " 含本mod=" +
                                   (RecipeProto.recipeExecuteData != null &&
                                    RecipeProto.recipeExecuteData.ContainsKey(C.RecipeId)));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("补跑 InitRecipeItems 失败: " + e.Message);
            }

            // VFPreload 里的 InitModel* 是在我们加 proto 之前跑的，这里补一遍
            try
            {
                ModelProto.InitMaxModelIndex();
                ModelProto.InitModelIndices();
                ModelProto.InitModelOrders();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("补 InitModel* 失败: " + e.Message);
            }

            // 1.2.8：同款时序问题——VFPreload 的 InitPowerFacilityIndices 构建
            // powerGenId2Index / powerConIndex2Id（统计面板「能量分析」页的聚合索引）。
            // 如果它跑在我们 proto 进 LDB 之前，powerGenId2Index[8000] 就是默认值 0，
            // 我们的发电/耗电全部归到 id 0 → 统计页出现空白条目、数字叠错、归类错误。
            // 这个函数是全量重建、幂等，直接补跑一次。
            try
            {
                ItemProto.InitPowerFacilityIndices();
                Plugin.Log.LogInfo("已补跑 InitPowerFacilityIndices: 发电索引=" +
                                   ItemProto.powerGenIndex2Id.Length + " 耗电索引=" +
                                   ItemProto.powerConIndex2Id.Length +
                                   " 含本mod=" + System.Array.IndexOf(ItemProto.powerGenIndex2Id, C.ItemId));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("补 InitPowerFacilityIndices 失败: " + e.Message);
            }

            Plugin.Log.LogInfo("mod proto 预加载完成");
        }

        // ---------------- 建造栏位置（1.20.0） ----------------

        /// <summary>
        /// 把本建筑挂到建造栏「电力」页的 F10（1.20.0）。
        ///
        /// <c>BuildIndex = 页 × 100 + 槽位</c>，而槽位就是 F 键号（原版分拣器 = 307 → 第 3 页 F7）。
        /// 页码**不能写死**：不同游戏版本、不同 mod 组合下「电力」页的编号会变
        /// （以前这里写死 607，结果建筑落在别的页签里，玩家得逐个页翻）。
        /// 所以拿原版「蓄电器」（<see cref="C.BuildIndexAnchor"/>，一定在电力页）当锚点取页码，
        /// 再在页内找 F10；F10 被占就向两侧找最近的空槽。
        /// </summary>
        internal static void ResolveBuildIndex(ItemProto item)
        {
            if (item == null)
                return;

            ItemProto anchor = null;
            try { anchor = LDB.items != null ? LDB.items.Select(C.BuildIndexAnchor) : null; }
            catch { anchor = null; }

            int page = anchor != null && anchor.BuildIndex > 0 ? anchor.BuildIndex / 100 : 0;
            if (page <= 0)
            {
                item.BuildIndex = C.BuildIndex;
                C.ResolvedBuildIndex = C.BuildIndex;
                Plugin.Log.LogWarning("建造栏：拿不到蓄电器锚点，退回写死的 BuildIndex=" + C.BuildIndex);
                return;
            }

            int chosen = -1;
            // 0, +1, -1, +2, -2 … 先试 F10，再往两边找
            for (int k = 0; k < 12; k++)
            {
                int s = C.BuildIndexSlot + ((k % 2 == 0) ? (k / 2) : (-(k + 1) / 2));
                if (s < 1 || s > 12)
                    continue;
                if (Occupied(page * 100 + s))
                    continue;
                chosen = s;
                break;
            }

            if (chosen < 0)
            {
                item.BuildIndex = C.BuildIndex;
                C.ResolvedBuildIndex = C.BuildIndex;
                Plugin.Log.LogWarning("建造栏：电力页 1~12 槽全被占，退回写死的 BuildIndex=" + C.BuildIndex);
                return;
            }

            item.BuildIndex = page * 100 + chosen;
            C.ResolvedBuildIndex = item.BuildIndex;
            Plugin.Log.LogInfo("建造栏：电力页 = 第 " + page + " 页，槽位 = F" + chosen +
                               "（BuildIndex=" + item.BuildIndex + "，锚点蓄电器=" + anchor.BuildIndex + "）");
        }

        /// <summary>
        /// 建造栏「事后落位」（1.21.0）：直接把 <c>UIBuildMenu.protos[页, 槽]</c> 写成我们的物品。
        ///
        /// 为什么不能只靠改 <c>item.BuildIndex</c>：
        ///   · <c>StaticLoad</c> 只跑一次（staticLoaded），跑在我们前面时改字段根本读不到；
        ///   · 同一页同槽被两个物品声明时，dataArray 里靠后的会覆盖前面的 ——
        ///     我们在建表阶段挑的「空槽」，之后可能被别的 MOD 塞进来的物品顶掉。
        ///
        /// 这里按「当刻的真实占用」重算槽位（优先 F10），清掉本物品在其它格子里的残留，
        /// 再把它写进目标格，并把 <c>BuildIndex</c> 同步回去（保持数据一致）。
        /// 重复调用是安全的：位置没变就直接返回，不刷日志。
        /// </summary>
        /// <summary>
        /// 建造栏位子的「延迟补偿」（1.21.0）。
        ///
        /// <see cref="FinalizeBuildSlot"/> 有两个触发点：<c>StaticLoad</c> 的 Postfix，和这里的两次延时调用。
        /// 三种先后顺序都能覆盖：
        ///   · StaticLoad 跑在补丁之后 → Postfix 落位；
        ///   · StaticLoad 跑在补丁之前（别的 MOD / 提前创建的 UI 先跑掉了）→ 这里的第一次延时调用来补；
        ///   · 落位时锚点物品还没就绪 → 第二次延时再补一次。
        /// 只跑两次，之后不再打扰。
        /// </summary>
        private static float _slotTimer;
        private static int _slotTries;

        internal static void TickBuildSlot()
        {
            if (!_preloaded || _slotTries >= 2)
                return;
            _slotTimer += Time.deltaTime;
            if (_slotTimer < (_slotTries == 0 ? 2f : 6f))
                return;
            _slotTimer = 0f;
            _slotTries++;
            try { FinalizeBuildSlot(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("建造栏：延时落位失败: " + e.Message); }
        }

        internal static void FinalizeBuildSlot()
        {
            ItemProto item = FindItem();
            if (item == null)
                return;
            var set = LDB.items;
            if (set == null || set.dataArray == null)
                return;

            // ---- 页：原版「蓄电器」一定在电力页；拿不到就用能量枢纽（2209）；再不行用当前值 ----
            int page = PageOf(C.BuildIndexAnchor);
            if (page <= 0)
                page = PageOf(2209);
            if (page <= 0)
                page = item.BuildIndex / 100;
            if (page <= 0 || page > 15)
                page = System.Math.Max(1, System.Math.Min(15, C.BuildIndex / 100));

            // ---- 该页的真实占用（排除自己）----
            var used = new bool[13];
            ItemProto[] arr = set.dataArray;
            for (int i = 0; i < arr.Length; i++)
            {
                ItemProto p = arr[i];
                if (p == null || p.ID == C.ItemId || p.BuildIndex <= 0)
                    continue;
                if (p.BuildIndex / 100 != page)
                    continue;
                int sl = p.BuildIndex % 100;
                if (sl >= 0 && sl < used.Length)
                    used[sl] = true;
            }

            // ---- 槽：优先 F10，然后往两边找最近的空槽；实在没空位就退回 F10 ----
            int slot = -1;
            int[] prefer = { 10, 9, 11, 8, 12, 7, 6, 5, 4, 3, 2, 1 };
            for (int i = 0; i < prefer.Length; i++)
            {
                int s2 = prefer[i];
                if (s2 < used.Length && !used[s2])
                {
                    slot = s2;
                    break;
                }
            }
            if (slot < 0)
                slot = C.BuildIndexSlot;

            int buildIndex = page * 100 + slot;
            bool moved = item.BuildIndex != buildIndex;
            item.BuildIndex = buildIndex;
            C.ResolvedBuildIndex = buildIndex;

            // ---- 写表：先清残留，再落位 ----
            ItemProto[,] protos = ProtosTable();
            if (protos == null)
                return;

            for (int p = 0; p < protos.GetLength(0); p++)
            {
                for (int s2 = 0; s2 < protos.GetLength(1); s2++)
                {
                    ItemProto cur = protos[p, s2];
                    if (cur != null && cur.ID == C.ItemId)
                        protos[p, s2] = null;
                }
            }
            if (page < protos.GetLength(0) && slot < protos.GetLength(1))
                protos[page, slot] = item;

            if (!moved && _placed == buildIndex)
                return;
            _placed = buildIndex;

            Plugin.Log.LogInfo("建造栏落位：电力页 = 第 " + page + " 页，槽位 = F" + slot +
                               "（BuildIndex=" + buildIndex + "，" +
                               "该页已用槽=" + UsedSlots(used) + "）" +
                               " 图标=" + (item.iconSprite != null ? "ok" : "缺失") +
                               " 名称=" + (string.IsNullOrEmpty(item.name) ? "空" : item.name));
        }

        /// <summary>上一次落位的 BuildIndex（避免每次 StaticLoad 都刷日志）。</summary>
        private static int _placed = -1;

        /// <summary>物品 BuildIndex 所在页（0 = 不在建造栏里）。</summary>
        private static int PageOf(int itemId)
        {
            try
            {
                ItemProto p = LDB.items != null ? LDB.items.Select(itemId) : null;
                if (p == null || p.BuildIndex <= 0)
                    return 0;
                return p.BuildIndex / 100;
            }
            catch
            {
                return 0;
            }
        }

        private static string UsedSlots(bool[] used)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < used.Length; i++)
            {
                if (!used[i])
                    continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(i);
            }
            return sb.Length > 0 ? sb.ToString() : "无";
        }

        /// <summary><c>UIBuildMenu.protos</c>（private static ItemProto[16, 13]）。</summary>
        private static readonly System.Reflection.FieldInfo _protosField =
            AccessTools.Field(typeof(UIBuildMenu), "protos");

        /// <summary>取建造栏那张静态表（拿不到返回 null）。</summary>
        private static ItemProto[,] ProtosTable()
        {
            try
            {
                return _protosField != null ? _protosField.GetValue(null) as ItemProto[,] : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>这个 BuildIndex 是否已经被别的物品占了。</summary>
        private static bool Occupied(int buildIndex)
        {
            var set = LDB.items;
            if (set == null || set.dataArray == null)
                return false;
            ItemProto[] arr = set.dataArray;
            for (int i = 0; i < arr.Length; i++)
            {
                ItemProto p = arr[i];
                if (p != null && p.ID != C.ItemId && p.BuildIndex == buildIndex)
                    return true;
            }
            return false;
        }

        private static int IndexOf<T>(T[] arr, T target) where T : class
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (ReferenceEquals(arr[i], target))
                    return i;
            }
            return 0;
        }

        // ---------------- 配方原料（1.7.0） ----------------

        /// <summary>
        /// 把配方的四条原料填成「5 能量枢纽 / 5 射线接收站 / 10 蓄电器 / 5 处理器组件」。
        ///
        /// 为什么要按名字找：这几个 id（2208 / 2209 / 1104 …）在 Awake 阶段拿不到，
        /// 硬编又怕记错（原版发电类建筑 id 挨得很密，错一位就是另一种建筑）。
        /// 名字匹配用「先精确后包含」，中英文都试；匹配不到就跳过那一条并打日志，
        /// 而不是塞一个错误的 id 进去 —— 塞错了玩家会看到配方凭空变成别的东西。
        /// </summary>
        private static void ResolveRecipeItems(RecipeProto recipe)
        {
            if (recipe == null)
                return;

            string[][] keys =
            {
                new[] { "能量枢纽", "Energy Exchanger" },
                new[] { "射线接收站", "Ray Receiver" },
                new[] { "蓄电器", "Accumulator" },
                new[] { "处理器", "Processor", "处理器组件" },
            };
            int[] counts = { 5, 5, 100, 5 };

            var ids = new List<int>(4);
            var cnt = new List<int>(4);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Length; i++)
            {
                int id = FindItemByName(keys[i]);
                if (id > 0)
                {
                    ids.Add(id);
                    cnt.Add(counts[i]);
                    if (sb.Length > 0) sb.Append(" + ");
                    sb.Append(counts[i]).Append('×').Append(Items.Name(id)).Append('#').Append(id);
                }
                else
                {
                    Plugin.Log.LogWarning("配方：找不到原料「" + keys[i][0] + "」，已从配方里去掉这一项");
                }
            }

            if (ids.Count == 0)
            {
                Plugin.Log.LogWarning("配方：一种原料都没解析到，保留占位配方（仍可合成，但原料不对）");
                return;
            }

            recipe.Items = ids.ToArray();
            recipe.ItemCounts = cnt.ToArray();
            Plugin.Log.LogInfo("配方原料: " + sb);
        }

        /// <summary>
        /// 按名字找物品。先精确匹配（中英文都算），再退化为「包含」匹配，取名字最短的那个
        /// （避免「蓄电器」匹配到「满蓄电器」这种更长的近义项）。
        /// </summary>
        private static int FindItemByName(string[] keys)
        {
            var set = LDB.items;
            if (set == null || set.dataArray == null)
                return 0;

            int loose = 0;
            int looseLen = int.MaxValue;
            ItemProto[] arr = set.dataArray;
            for (int i = 0; i < arr.Length; i++)
            {
                ItemProto p = arr[i];
                if (p == null || p.ID <= 0 || p.ID == C.ItemId || p.ID >= 12000)
                    continue;

                string a = p.Name;
                string b = null;
                try { b = p.name; } catch { }
                for (int k = 0; k < keys.Length; k++)
                {
                    if (Eq(a, keys[k]) || Eq(b, keys[k]))
                        return p.ID;
                }
                for (int k = 0; k < keys.Length; k++)
                {
                    if (Has(a, keys[k]) || Has(b, keys[k]))
                    {
                        int len = (a != null ? a.Length : 0);
                        if (len < looseLen)
                        {
                            looseLen = len;
                            loose = p.ID;
                        }
                        break;
                    }
                }
            }
            return loose;
        }

        private static bool Eq(string s, string key)
        {
            return !string.IsNullOrEmpty(s) &&
                   string.Equals(s.Trim(), key, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool Has(string s, string key)
        {
            return !string.IsNullOrEmpty(s) &&
                   s.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static ModelProto FindModel()
        {
            return FindIn(LDB.models, p => p.ID == C.ModelId);
        }

        public static RecipeProto FindRecipe()
        {
            return FindIn(LDB.recipes, p => p.ID == C.RecipeId);
        }

        private static T FindIn<T>(ProtoSet<T> set, System.Func<T, bool> match) where T : Proto
        {
            if (set == null || set.dataArray == null)
                return null;
            var data = set.dataArray;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != null && match(data[i]))
                    return data[i];
            }
            return null;
        }

        // ---------------- 图标 ----------------

        private static readonly AccessTools.FieldRef<ItemProto, Sprite> _iconRef =
            AccessTools.FieldRefAccess<ItemProto, Sprite>("_iconSprite");

        /// <summary>
        /// 补挂图标（幂等）。LDB 就绪后调用；IconSet.Create 前调用的话能进入图标图集。
        /// </summary>
        public static void EnsureIcon()
        {
            ApplyIcon(FindItem());
        }

        /// <summary>LDB 就绪后取我们的 ItemProto（字典可能还没重建，兜底线性扫描）。</summary>
        public static ItemProto FindItem()
        {
            var set = LDB.items;
            if (set == null || set.dataArray == null)
                return null;
            ItemProto item = null;
            try
            {
                item = set.Select(C.ItemId);
            }
            catch
            {
                item = null;
            }
            if (item != null)
                return item;
            var data = set.dataArray;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != null && data[i].ID == C.ItemId)
                    return data[i];
            }
            return null;
        }

        private static void ApplyIcon(ItemProto item)
        {
            if (item == null || _iconRef == null)
                return;
            try
            {
                if (_iconRef(item) != null)
                    return;

                const int N = 80;  // IconSet 用 Graphics.CopyTexture 拷 80x80，尺寸/格式必须对得上
                var tex = new Texture2D(N, N, TextureFormat.ARGB32, false);
                tex.name = "energy-hub-icon";
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;

                Color bg0 = new Color(0.10f, 0.16f, 0.22f);
                Color bg1 = new Color(0.20f, 0.32f, 0.42f);
                Color cyan = new Color(0.28f, 0.88f, 1.00f);
                Color hull = new Color(0.82f, 0.88f, 0.94f);
                var px = new Color[N * N];

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N - 0.5f;
                        float v = (y + 0.5f) / N - 0.5f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        Color c = Color.Lerp(bg0, bg1, Mathf.Clamp01(1f - r * 1.6f));

                        // 中央竖塔
                        if (Mathf.Abs(u) < 0.055f && v > -0.34f && v < 0.30f) c = hull;
                        // 底座
                        if (Mathf.Abs(u) < 0.26f && v > -0.44f && v <= -0.34f) c = hull * 0.86f;
                        // 中部光环
                        if (Mathf.Abs(r - 0.26f) < 0.022f && v > -0.14f) c = cyan;
                        // 顶端能量球
                        float od = Mathf.Sqrt(u * u + (v - 0.36f) * (v - 0.36f));
                        if (od < 0.10f) c = Color.Lerp(cyan, Color.white, 1f - od / 0.10f);
                        // 外圈
                        if (r > 0.455f && r < 0.48f) c = new Color(0.30f, 0.55f, 0.70f);

                        c.a = 1f;
                        px[y * N + x] = c;
                    }
                }

                tex.SetPixels(px);
                tex.Apply(false, false);
                Sprite sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect);
                sp.name = "energy-hub-icon";
                _icon = sp;
                _iconRef(item) = sp;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("生成图标失败（不影响功能）: " + e.Message);
            }
        }

        private static void RegisterStrings()
        {
            ProtoRegistry.RegisterString(C.LItemName, "Interstellar Energy Hub Tower", "星际能量枢纽塔");

            // 1.7.0：制作台 / 建造栏里显示的是这段 Description，必须把「这是干什么的」说清楚。
            // 换行在 UIItemTip 里是支持的（原版物品描述也有多行）。
            // 1.7.1：储能并入发送/接收（不再有独立储能模式），文案同步改。
            // 1.21.5：原文案 24 行，物品提示框整屏都是字，玩家反馈「太长」。
            // 改成 9 行、一句一个要点：只留「模式 / 传能 / 分配 / 池子 / 接口」五块，
            // 解锁路径、90% 余量来由、升级花费明细这些都可以在科技树和面板里看到，不再重复。
            ProtoRegistry.RegisterString(C.LItemDesc,
                "A megastructure that moves power between planets, and a giant accumulator.\n" +
                "Level up with the repeatable \"Pool Expansion\" node on the tech tree's Upgrade page " +
                "(Graviton Matrices only). The level is save-wide, so a rebuilt tower keeps it.\n" +
                "Modes: Off / Battery only / Send / Receive. The last three charge from grid surplus " +
                "(at most 90% of it) and discharge into a deficit; Battery only never transfers or " +
                "consumes items.\n" +
                "Send / Receive: a receiver must \"Connect\" to a sender in its panel, otherwise it " +
                "gets nothing.\n" +
                "Same star system: 1 GJ per item (Information Matrix). Cross-system: also 1 Space " +
                "Warper + 1 Graviton Lens. The sender pays.\n" +
                "+5% energy per item per level, at most " + C.MaxItemsPerSec + " items/s per link. " +
                "No items, no transfer; receivers need nothing.\n" +
                "One sender feeding several receivers splits output by demand; full pools get no share.\n" +
                "Pool: 100 GJ at level 1 up to 1 PJ, full charge / discharge in about 20 s, hard cap 50 TW.\n" +
                "Eight belt ports at the base feed the consumable slots, " +
                C.SlotStackMax.ToString("N0") + " per slot.",
                "可在星球之间输送能量的巨型建筑，同时是一台超大蓄电器。\n" +
                "等级在「科技 → 升级」页研究「能量池扩容」提升（只耗引力矩阵）；" +
                "等级是存档进度，拆建不丢。\n" +
                "【模式】关机 / 仅蓄电 / 发送 / 接收。后三种会自动为电网充放（最多吃空闲电的 90%）；" +
                "仅蓄电不传能、不耗料。\n" +
                "【传能】接收端需在面板里「连接」一台发送端，没连上的收不到。\n" +
                "同星系每个耗材换 1 GJ（信息矩阵）；跨星系再加 1 空间翘曲器 + 1 引力透镜，" +
                "全由发送端提供。\n" +
                "每级 +5%，每条链路每秒最多扣 " + C.MaxItemsPerSec + " 个；缺料即停发，" +
                "接收端不放任何物品。\n" +
                "【分配】一台发送端挂多台接收端时按各自缺口分配，池满的不占份额。\n" +
                "【池子】1 级 100 GJ → 满级 1 PJ，满池约 20 秒充放完，功率硬顶 50 TW。\n" +
                "【接口】底座前后 8 个传送带接口，耗材直接送入，每格 " +
                C.SlotStackMax.ToString("N0") + " 个。");
            ProtoRegistry.RegisterString(C.LRecipeName, "Interstellar Energy Hub Tower", "星际能量枢纽塔");
            ProtoRegistry.RegisterString(C.LRecipeDesc,
                "Assemble an Interstellar Energy Hub from power exchangers, ray receivers, " +
                "accumulators and processors.",
                "用能量枢纽、射线接收站、蓄电器与处理器组件组装一台星际能量枢纽塔。");

            ProtoRegistry.RegisterString(C.LTechName, "Interstellar Energy Hub Tower", "星际能量枢纽塔");
            ProtoRegistry.RegisterString(C.LTechDesc,
                "Engineers finally worked out how to fold a power grid through a warper lane. " +
                "Unlocks the Interstellar Energy Hub: a glass prism tower that stores energy like a giant " +
                "accumulator, beams it to another planet, and receives it on the other side.\n" +
                "Researched with Information Matrices.",
                "把整座电网折叠进翘曲通道的第一次成功尝试。\n" +
                "\n" +
                "解锁「星际能量枢纽塔」的建造与手工组装：\n" +
                "· 池子是一台超大容量蓄电器，发送 / 接收时自动为电网充放电（1 级 100 GJ，满级 1 PJ，功率随等级提升、硬顶 50 TW）；\n" +
                "· 可在恒星系内 / 跨恒星系点对点输送能量，速率由能量池等级决定；\n" +
                "· 接收端接入本地电网，把远方的电直接喂进工厂。\n" +
                "\n" +
                "研究消耗：信息矩阵。\n" +
                "建造材料：能量枢纽 ×5、射线接收站 ×5、蓄电器 ×100、处理器组件 ×5。\n" +
                "\n" +
                "能量池扩容在「升级」页的「能量池扩容」节点上研究（可重复 " +
                C.PoolTechSteps + " 次，只消耗引力矩阵）。");
            ProtoRegistry.RegisterString(C.LTechConclusion,
                "Unlocks the Interstellar Energy Hub.",
                "解锁「星际能量枢纽塔」的建造与手工组装。");

            // 能量池扩容（升级页，可重复研究）
            ProtoRegistry.RegisterString(C.LPoolTechName, "Pool Expansion", "能量池扩容");
            ProtoRegistry.RegisterString(C.LPoolTechDesc,
                "Reinforce the shell of the energy pool with graviton lattices: every step raises " +
                "the capacity and the charge / discharge power of every Interstellar Energy Hub in " +
                "this save. Repeatable " + C.PoolTechSteps + " times (up to level " +
                C.MaxPoolLevel + "); each step costs " + C.PoolTechCostOf(1) + " → " +
                C.PoolTechCostOf(C.MaxPoolLevel - 1) + " Graviton Matrices.",
                "用引力晶格加固能量池的外壳：每研究一次，本存档里**所有**星际能量枢纽塔的" +
                "容量与充放功率一起提升。\n" +
                "\n" +
                "· 共可研究 " + C.PoolTechSteps + " 次，能量池最高 " + C.MaxPoolLevel +
                " 级（100 GJ → 1 PJ，功率硬顶 50 TW）；\n" +
                "· 每次消耗 " + C.PoolTechCostOf(1) + " → " + C.PoolTechCostOf(C.MaxPoolLevel - 1) +
                " 个引力矩阵，合计 " + C.TotalPoolTechCost() + " 个；\n" +
                "· 等级是存档级研究进度：拆掉重建的塔自动就是当前等级，不用重新投入。");
            ProtoRegistry.RegisterString(C.LPoolTechConclusion,
                "All hubs gain one more pool level.",
                "本存档所有能量枢纽塔的能量池 +1 级。");
        }
    }
}
