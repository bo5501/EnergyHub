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
                //   现在必须研究「星际能量枢纽」科技才解锁，所以填 0，
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

            // 兜底：科技节点确实排不出来（找不到锚点 / 没空闲 id / 异常）时，
            // 把物品改回「永远解锁」，否则配方永远锁着，玩家造不出建筑 ——
            // 那比「解锁少一个科技节点」严重得多。
            if (Tech.LaidOut && !Tech.Ok)
            {
                ItemProto fallback = FindItem();
                if (fallback != null)
                    fallback.UnlockKey = -1;
                Plugin.Log.LogWarning("科技节点不可用：已把星际能量枢纽改回「永远解锁」");
            }

            ItemProto item = FindItem();
            if (item != null)
            {
                ApplyIcon(item);                // 配方 Preload 会继承物品图标
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
            ProtoRegistry.RegisterString(C.LItemName, "Interstellar Energy Hub", "星际能量枢纽");

            // 1.7.0：制作台 / 建造栏里显示的是这段 Description，必须把「这是干什么的」说清楚。
            // 换行在 UIItemTip 里是支持的（原版物品描述也有多行）。
            // 1.7.1：储能并入发送/接收（不再有独立储能模式），文案同步改。
            ProtoRegistry.RegisterString(C.LItemDesc,
                "A megastructure that moves power between planets and doubles as a giant accumulator.\n" +
                "Unlocked by the Interstellar Energy Hub research node right after Information Matrix " +
                "(researched with Information Matrices).\n" +
                "Modes: Idle, Send, Receive — in Send/Receive modes the pool also works as a large " +
                "accumulator, charging on grid surplus and discharging on shortage automatically.\n" +
                "Receivers connect to a sending unit directly; transfers consume Information Matrices " +
                "(Space Warpers + Graviton Lenses across star systems).\n" +
                "One sender can feed several receivers: output is split by what each receiver can " +
                "actually absorb, so a full pool gets nothing and idle share is re-handed.\n" +
                "Drop Proliferator Mk.III into the boost slot to double the interstellar transfer rate.\n" +
                "The internal pool grows from 100 GJ to 1 PJ over 20 levels; the material cost steps " +
                "up every 4 levels (" + C.UpgradeCostOf(1) + " Accumulators + " + C.UpgradeCost2Of(1) +
                " Graviton Matrices at levels 1-4, up to " + C.UpgradeCostOf(17) + " + " +
                C.UpgradeCost2Of(17) + " at the top tier, both required). Items fed by belt are " +
                "accumulated into an upgrade pool, so no single delivery has to cover a whole level. " +
                "Charge / discharge power grows with the level, capped at 50 TW.\n" +
                "Eight belt ports at the base (4 front + 4 back) feed the consumable slots.",
                "可在星球之间直接输送能量的巨型建筑，同时也能当一台超大容量的蓄电器使用。\n" +
                "在科技树「能量枢纽」科技（解锁能量枢纽建筑那条）右下方一格解锁" +
                "「星际能量枢纽」，节点挂在卫星配电系统正下方（研究需消耗信息矩阵）；" +
                "找不到该节点时退回「信息矩阵」后面。解锁后才能制造与建造。\n" +
                "\n" +
                "【三种工作模式】\n" +
                "· 待机：能量池保持当前电量，不参与电网充放，也不参与星际传输；\n" +
                "· 发送：从本地电网抽电存进池子，等接收端「连接」之后把能量发出去；\n" +
                "· 接收：连上一台发送终端后接收能量，并注入本星球电网。\n" +
                "\n" +
                "【内置蓄电器】发送 / 接收模式下，能量池同时是一台超大蓄电器：\n" +
                "电网有余电时自动充电，电网缺电时自动放电，全程自动切换、无需操作。\n" +
                "\n" +
                "【点对点传输】接收端在自己的面板里点「连接」选定一台发送终端，" +
                "只有真正连上的接收端才会收到能量（不会串到同星球的别的枢纽上）。\n" +
                "同一恒星系内每 200 MJ 消耗 1 个「信息矩阵」；跨恒星系还要额外消耗" +
                "「空间翘曲器 + 引力透镜」，都由发送端提供。\n" +
                "\n" +
                "【按需自动分配】一台发送端挂多台接收端时，按每台「现在能吃多少」分配：" +
                "池子已满的端不再占份额，被在途账本或耗材卡住的份额会自动转给还有空间的端。\n" +
                "\n" +
                "【传输提速】卡槽里放「三级增产剂 Mk.III」，星际传输速率 ×2，" +
                "提速期间每秒消耗 1 个，取完自动回到普通速率。\n" +
                "\n" +
                "【能量池升级】卡槽里放入「蓄电器」与「引力矩阵」，共 " + C.MaxPoolLevel + " 级，" +
                "材料按阶梯递增：1~4 级每级 " + C.UpgradeCostOf(1) + " 蓄电器 + " + C.UpgradeCost2Of(1) +
                " 引力矩阵，之后每 4 级抬高一次，最高一档 " + C.UpgradeCostOf(17) + " 蓄电器 + " +
                C.UpgradeCost2Of(17) + " 引力矩阵（两种都要）。" +
                "送进卡槽的材料会自动累积到升级进度池，不必一次凑齐，攒够再点升级。\n" +
                "1 级 100 GJ，容量逐级指数增长，满级 1 PJ；" +
                "充/放功率随容量提升（满池约 20 秒充满），功率硬顶 50 TW。\n" +
                "\n" +
                "【传送带接口】底座前后各有 4 个接口（共 8 个进口），消耗品可以直接用传送带送进来。");
            ProtoRegistry.RegisterString(C.LRecipeName, "Interstellar Energy Hub", "星际能量枢纽");
            ProtoRegistry.RegisterString(C.LRecipeDesc,
                "Assemble an Interstellar Energy Hub from power exchangers, ray receivers, " +
                "accumulators and processors.",
                "用能量枢纽、射线接收站、蓄电器与处理器组件组装一台星际能量枢纽。");

            ProtoRegistry.RegisterString(C.LTechName, "Interstellar Energy Hub", "星际能量枢纽");
            ProtoRegistry.RegisterString(C.LTechDesc,
                "Engineers finally worked out how to fold a power grid through a warper lane. " +
                "Unlocks the Interstellar Energy Hub: a glass prism tower that stores energy like a giant " +
                "accumulator, beams it to another planet, and receives it on the other side.\n" +
                "Researched with Information Matrices.",
                "把整座电网折叠进翘曲通道的第一次成功尝试。\n" +
                "\n" +
                "解锁「星际能量枢纽」的建造与手工组装：\n" +
                "· 池子是一台超大容量蓄电器，发送 / 接收时自动为电网充放电（1 级 100 GJ，满级 1 PJ，功率随等级提升、硬顶 50 TW）；\n" +
                "· 可在恒星系内 / 跨恒星系点对点输送能量，卡槽放三级增产剂可再提速 ×2；\n" +
                "· 接收端接入本地电网，把远方的电直接喂进工厂。\n" +
                "\n" +
                "研究消耗：信息矩阵。\n" +
                "建造材料：能量枢纽 ×5、射线接收站 ×5、蓄电器 ×100、处理器组件 ×5。");
            ProtoRegistry.RegisterString(C.LTechConclusion,
                "Unlocks the Interstellar Energy Hub.",
                "解锁「星际能量枢纽」的建造与手工组装。");
        }
    }
}
