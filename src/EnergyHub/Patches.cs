using System;
using HarmonyLib;
using xiaoye97;

namespace EnergyHub
{
    /// <summary>
    /// LDBTool 只在 VFPreload.InvokeOnLoadWorkEnded 里把 proto 追加进 dataArray，
    /// 不会调用 Preload()。所以在它跑完之后（同一个方法的 Prefix 阶段末端）补一次。
    /// </summary>
    [HarmonyPatch(typeof(VFPreload), "InvokeOnLoadWorkEnded")]
    [HarmonyAfter(LDBToolPlugin.MODGUID)]
    internal static class Patch_PostProtoPreload
    {
        private static void Prefix()
        {
            try
            {
                Protos.PreloadAll();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("PreloadAll 异常: " + e);
            }
        }
    }

    /// <summary>保底：读档时 SkillSystem 会遍历所有 model 的 prefabDesc，此时必须已就绪。</summary>
    [HarmonyPatch(typeof(SkillSystem), MethodType.Constructor, typeof(SpaceSector))]
    internal static class Patch_SkillSystemCtor
    {
        private static void Prefix()
        {
            try
            {
                Protos.PreloadAll();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("SkillSystem 兜底预加载异常: " + e.Message);
            }
        }
    }

    /// <summary>
    /// ModelProto.Preload 每次都会用 Resources.Load 重建 prefabDesc（PrefabPath 为空时退化为
    /// PrefabDesc.none）。我们的 prefab 是运行时构造的，必须在这里重新挂回去。
    /// </summary>
    [HarmonyPatch(typeof(ModelProto), "Preload")]
    internal static class Patch_ModelPreload
    {
        private static void Postfix(ModelProto __instance)
        {
            if (__instance == null || __instance.ID != C.ModelId)
                return;
            try
            {
                PrefabDesc desc = HubPrefab.GetDesc();
                __instance.prefabDesc = desc;
                if (desc != null && desc.mesh != null)
                    __instance.meshBounds = desc.mesh.bounds;   // 原版在 Preload 里用 lodMeshes[0] 算，我们后挂要补
                Patch_ModelArray.Ensure();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("挂载 prefabDesc 失败: " + e);
            }
        }
    }

    /// <summary>
    /// ModelProtoSet.modelArray 是按模型 id 直接索引的数组，长度只有 dataArray.Length + 64。
    /// 模组模型是后加的，这里保证我们的 id 落在数组内（必要时扩容）并指向正确的 ModelProto，
    /// 否则 ItemProto.Preload 会读到 null，建筑变成 PrefabDesc.none（不可见）。
    /// </summary>
    [HarmonyPatch(typeof(ModelProtoSet), "OnAfterDeserialize")]
    internal static class Patch_ModelArray
    {
        private static void Postfix()
        {
            Ensure();
        }

        public static void Ensure()
        {
            try
            {
                ModelProtoSet set = LDB.models;
                if (set == null || set.dataArray == null)
                    return;

                ModelProto mine = null;
                ModelProto[] data = set.dataArray;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] != null && data[i].ID == C.ModelId)
                    {
                        mine = data[i];
                        break;
                    }
                }
                if (mine == null)
                    return;

                ModelProto[] arr = set.modelArray;
                if (arr == null || arr.Length <= C.ModelId)
                {
                    int n = Math.Max(C.ModelId + 1, (arr != null ? arr.Length : 0) + 64);
                    var grown = new ModelProto[n];
                    if (arr != null)
                        Array.Copy(arr, grown, arr.Length);
                    set.modelArray = grown;
                    arr = grown;
                }

                if (arr[C.ModelId] == null)
                    arr[C.ModelId] = mine;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("修正 modelArray 失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// ItemProto.Preload 读 LDB.models.modelArray[ModelIndex] 取 prefabDesc，
    /// 在它之前确保数组里已经有我们的模型。
    /// </summary>
    [HarmonyPatch(typeof(ItemProto), "Preload")]
    internal static class Patch_ItemPreload
    {
        private static void Prefix(ItemProto __instance)
        {
            if (__instance == null || __instance.ID != C.ItemId)
                return;
            Patch_ModelArray.Ensure();
            Protos.EnsureIcon();

            // 万一模型自己那次 Preload 没跑（顺序不定），prefabDesc 会是 null，
            // ItemProto.Preload 里 this.prefabDesc.isCollectStation 会直接 NRE。
            try
            {
                ModelProtoSet set = LDB.models;
                if (set != null && set.modelArray != null && C.ModelId < set.modelArray.Length)
                {
                    ModelProto m = set.modelArray[C.ModelId];
                    if (m != null && m.prefabDesc == null)
                        m.prefabDesc = HubPrefab.GetDesc();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("预置 prefabDesc 失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// IconSet.Create 把每个 ItemProto.iconSprite 拷进 2000x2000 图标图集。
    /// 我们的图标是运行时生成的，必须赶在它之前挂上（否则 UI 里图标永远是空的）。
    /// </summary>
    [HarmonyPatch(typeof(IconSet), "Create")]
    internal static class Patch_IconSet
    {
        private static void Prefix()
        {
            try
            {
                Protos.EnsureIcon();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("图标图集前挂图标失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 1.7.0：**不再**无条件解锁配方。
    ///
    /// 以前这里是「recipeId == 300 → 恒为 true」，让玩家开局就能搓；
    /// 现在解锁交给科技树节点（<see cref="Tech"/>），所以本补丁删除，
    /// 否则科技节点会变成一个纯装饰、点了也没意义的按钮。
    /// 如果哪天需要临时放开，把下面的类取消注释、并在 Plugin 里 PatchAll 就能恢复。
    /// </summary>
    // [HarmonyPatch(typeof(GameHistoryData), "RecipeUnlocked")]
    // internal static class Patch_RecipeUnlocked { ... }

    /// <summary>
    /// 点击枢纽实体时接管 UI。
    ///
    /// 枢纽同时挂了 powerExcId（原版能量枢纽窗口）和 storageId（原版仓储窗口），
    /// 而原版 UIPowerExchangerWindow._OnOpen 对着我们这种「运行时自建 prefab」的实体
    /// 会在 IL_0111 抛 NRE，导致整个检视流程中断（我们的窗口也来不及打开）。
    ///
    /// 做法：在原方法执行期间把这两个 id 临时清零，让它根本不去开原版窗口，
    /// 方法返回后立刻还原（PowerSystem / 传送带都只在 tick 时读 entityPool，看不到 0）。
    /// </summary>
    [HarmonyPatch(typeof(UIGame), "OnPlayerInspecteeChange")]
    internal static class Patch_Inspectee
    {
        private static bool _masked;
        private static int _maskId;
        private static int _saveExc;
        private static int _saveStg;

        internal static PlanetFactory LocalFactory
        {
            get
            {
                var player = GameMain.mainPlayer;
                return player != null ? player.factory : null;
            }
        }

        internal static void Recover()
        {
            if (!_masked)
                return;
            _masked = false;
            try
            {
                PlanetFactory f = LocalFactory;
                if (f == null || f.entityPool == null || _maskId >= f.entityPool.Length)
                    return;
                if (f.entityPool[_maskId].id != _maskId)
                    return;
                f.entityPool[_maskId].powerExcId = _saveExc;
                f.entityPool[_maskId].storageId = _saveStg;
                Plugin.Log.LogWarning("inspect 屏蔽未正常还原，已强制恢复 entityId=" + _maskId);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("恢复 entityPool 失败: " + e.Message);
            }
        }

        private static void Prefix(UIGame __instance, EObjectType objType, int objId)
        {
            _masked = false;
            try
            {
                if (objType != EObjectType.None || objId <= 0)
                    return;

                PlanetFactory f = LocalFactory;
                if (f == null || f.entityPool == null || objId >= f.entityPool.Length)
                    return;

                EntityData ed = f.entityPool[objId];
                if (ed.id != objId || ed.protoId != (short)C.ItemId)
                    return;

                _masked = true;
                _maskId = objId;
                _saveExc = ed.powerExcId;
                _saveStg = ed.storageId;
                if (_saveExc != 0) f.entityPool[objId].powerExcId = 0;
                if (_saveStg != 0) f.entityPool[objId].storageId = 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("inspect 前置屏蔽失败: " + e.Message);
                _masked = false;
            }
        }

        private static void Postfix(UIGame __instance, EObjectType objType, int objId)
        {
            try
            {
                if (_masked)
                {
                    _masked = false;
                    PlanetFactory f = LocalFactory;
                    if (f != null && f.entityPool != null && _maskId < f.entityPool.Length &&
                        f.entityPool[_maskId].id == _maskId)
                    {
                        f.entityPool[_maskId].powerExcId = _saveExc;
                        f.entityPool[_maskId].storageId = _saveStg;
                    }
                }

                if (objType != EObjectType.None || objId <= 0)
                {
                    HubWindow.CloseIfOpen();
                    return;
                }

                PlanetFactory factory = LocalFactory;
                if (factory == null || factory.entityPool == null || objId >= factory.entityPool.Length)
                {
                    HubWindow.CloseIfOpen();
                    return;
                }

                EntityData ed = factory.entityPool[objId];
                if (ed.id != objId || ed.protoId != (short)C.ItemId)
                {
                    HubWindow.CloseIfOpen();
                    return;
                }

                __instance.ShutAllFunctionWindow();
                HubWindow.OpenWindow(ed.id);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("inspect hook 异常: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 兜底：万一还有别的路径要打开原版能量枢纽窗口（对着我们的建筑），
    /// 直接在 UIGame.OpenExchangerWindow 这一层拦掉。
    /// 不能只改 window.exchangerId —— _OnOpen 里 excPool[id].id == id 为真时会一路往下跑，
    /// 反而更容易 NRE。
    /// </summary>
    [HarmonyPatch(typeof(UIGame), "OpenExchangerWindow")]
    internal static class Patch_NoVanillaExchangerWindow
    {
        private static bool Prefix(UIGame __instance)
        {
            try
            {
                UIPowerExchangerWindow win = __instance.exchangerWindow;
                if (win == null)
                    return true;

                int excId = win.exchangerId;
                PlanetFactory f = Patch_Inspectee.LocalFactory;
                if (f == null || f.powerSystem == null || excId <= 0 ||
                    excId >= f.powerSystem.excPool.Length)
                    return true;

                int entityId = f.powerSystem.excPool[excId].entityId;
                if (entityId <= 0 || entityId >= f.entityPool.Length)
                    return true;
                if (f.entityPool[entityId].protoId != (short)C.ItemId)
                    return true;

                Plugin.Log.LogInfo("拦截原版能量枢纽窗口（本建筑由专用窗口接管）");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("拦截原版窗口失败: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// 接管枢纽的传送带逻辑。
    ///
    /// PowerExchangerComponent.PickItemFromBelt 用 emptyId 当过滤条件，而 emptyId == 0 在
    /// CargoTraffic.TryPickItemAtRear 里等价于「任意物品」——枢纽会把传送带上的翘曲器
    /// 吞进 emptyCount，再在 InputUpdate 里当成蓄电器消耗掉，物品直接蒸发。
    /// 我们的翘曲器由 HubManager 自己从皮带上取，所以这里把原版逻辑整体跳过。
    /// </summary>
    internal static class Patch_ExcBeltUpdate
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(PowerExchangerComponent), "BeltUpdate");
                if (target == null)
                {
                    Plugin.Log.LogWarning("找不到 PowerExchangerComponent.BeltUpdate，跳过接管");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(Patch_ExcBeltUpdate), "Prefix"));
                Plugin.Log.LogInfo("已接管能量交换机的传送带逻辑");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("接管传送带逻辑失败（翘曲器可能被原版逻辑吞掉）: " + e.Message);
            }
        }

        private static bool Prefix(ref PowerExchangerComponent __instance, PlanetFactory planetFactory)
        {
            try
            {
                if (planetFactory == null || planetFactory.entityPool == null)
                    return true;
                int eid = __instance.entityId;
                if (eid <= 0 || eid >= planetFactory.entityPool.Length)
                    return true;
                if (planetFactory.entityPool[eid].protoId == (short)C.ItemId)
                    return false;
            }
            catch { }
            return true;
        }
    }

    /// <summary>
    /// 1.4.0：把本 mod 建筑的 GPU 实例动画数据（AnimData）钉死。
    ///
    /// DSP 画建筑走 DrawMeshInstancedIndirect，每个实例 20 字节 AnimData 由
    /// PowerSystem.GameTick 每帧写入再上传 GPU。交换站的 power = 池电量 / 池容量，
    /// 我们的池子每 tick 都被抽干，power 就在 0 和极小值之间反复跳；
    /// 原版 shader 里跟功率/状态挂钩的那一路（以及可能的顶点动画）会跟着跳，
    /// 表现就是「卡槽一放料、开始传能，整栋建筑一闪一闪」。
    /// 材质参数压不住它——实例数据是后于材质生效的。所以直接在 GameTick 之后覆盖掉。
    /// </summary>
    [HarmonyPatch(typeof(PowerSystem), nameof(PowerSystem.GameTick))]
    internal static class Patch_PinAnimPool
    {
        private static void Postfix(PowerSystem __instance)
        {
            try
            {
                HubManager.PinAnimPool(__instance);
            }
            catch
            {
                // 渲染数据而已，出问题不能影响电力 tick
            }
        }
    }

    /// <summary>ESC 关闭窗口。</summary>
    [HarmonyPatch(typeof(UIRoot), "_OnUpdate")]
    internal static class Patch_Escape
    {
        private static void Postfix()
        {
            try
            {
                if (!HubWindow.IsOpen) return;
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                    HubWindow.CloseIfOpen();
            }
            catch { }
        }
    }
}
