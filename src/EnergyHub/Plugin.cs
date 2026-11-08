using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using CommonAPI;
using CommonAPI.Systems;
using crecheng.DSPModSave;
using HarmonyLib;
using NebulaAPI;
using UnityEngine;
using xiaoye97;

namespace EnergyHub
{
    [BepInPlugin(C.Guid, C.Name, C.Version)]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency(LDBToolPlugin.MODGUID)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    // 联机：Nebula 官方 API（不是联机本体）。这个包明确写着「Does NOT require Nebula」，
    // 纯单机玩家装着也没有副作用；Thunderstore 安装时会作为依赖自动带上。
    [BepInDependency(NebulaModAPI.API_GUID)]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry))]
    [ModSaveSettings(LoadOrder = LoadOrder.Preload)]
    public class Plugin : BaseUnityPlugin, IModCanSave
    {
        public static ManualLogSource Log;
        public static Plugin Instance;

        private static Harmony _harmony;
        private static bool _iconApplied;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo(C.Name + " v" + C.Version + " 启动中…");

            // 缺料倍率：默认 0 = 卡槽里没有该链路要用的消耗品就完全不传（硬条件）。
            // 想恢复「半速白传」的老手感，把 BepInEx/config 下本模组 cfg 里的 NoItemRate 改成 0.5 即可，
            // 不用重新编译。
            try
            {
                var noItem = Config.Bind("传输", "NoItemRate", C.NoItemRate,
                    "卡槽里没有该链路要用的消耗品时，还允许送出多少（0 = 完全停机，默认；0.5 = 半速白传）。");
                C.NoItemRate = Mathf.Clamp01(noItem.Value);
                Log.LogInfo("缺料传输倍率 NoItemRate = " + C.NoItemRate +
                            (C.NoItemRate <= 0f ? "（硬停：没料不传能）" : "（缺料仍按此倍率白传）"));
            }
            catch (Exception e)
            {
                Log.LogWarning("读取配置失败: " + e.Message);
            }

            // 联机（1.19.5）：注册枢纽状态包、挂「进星球拉快照」的回调。
            // 单机时这些回调不会触发，行为与以前完全一致。
            try { MP.Init(); }
            catch (Exception e) { Log.LogWarning("联机兼容层初始化失败（按单机运行）: " + e.Message); }

            // 先装 patch：ModelProto.Preload 会重建 prefabDesc，必须在它跑之前挂上
            try            {
                _harmony = new Harmony(C.Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                int n = 0;
                foreach (var _ in _harmony.GetPatchedMethods()) n++;
                Log.LogInfo("Harmony patch 完成: " + n + " 个目标");
            }
            catch (Exception e)
            {
                Log.LogError("Harmony patch 失败: " + e);
            }

            // 单独装：结构体方法补丁失败时不要连累其它 patch
            if (_harmony != null)
            {
                Patch_ExcBeltUpdate.Apply(_harmony);

                // 兼容修复：联机（Nebula/Nebulae）或槽位 mod 改了物流清单尺寸却没重算
                // 索引表时，打开面板会 IndexOutOfRange。这里在面板刷新前把表重建好。
                Patch_DeliveryPanelGuard.Apply(_harmony);
            }

            try
            {
                Protos.RegisterAll();
                // 1.16.0：科技节点也必须走 LDBTool 的 PreAdd 队列（和创世之书一个套路）。
                // 在 LDB 建表之后再手工追加会被 LDBTool 建表时冲掉 —— 1.15.x 在科技树里
                // 看不到节点的真因就是这个，日志里「已注册 (41,5)」那行出现在 "Pre Loading..." 之前。
                Tech.PreAdd();

                // LDBTool 会在 Post Loading 阶段开头回调这个 Action（此时 Pre 批次的 proto 已加入 LDB），
                // 正好是补 Preload() + 给科技节点定位置的时机
                LDBTool.PostAddDataAction += Protos.PreloadAll;
            }
            catch (Exception e)
            {
                Log.LogError("proto 注册失败: " + e);
            }
        }

        private void Update()
        {
            // 万一检视流程中途抛异常导致 entityPool 的 id 屏蔽没还原，这里强制补回来
            Patch_Inspectee.Recover();

            // LDB 就绪后补一次图标（正常情况下 IconSet.Create 之前已经挂好了，这里只是兜底）
            if (!_iconApplied && LDB.items != null && LDB.items.dataArray != null && LDB.items.dataArray.Length > 0)
            {
                _iconApplied = true;
                Protos.EnsureIcon();
            }

            // 按名字解析消耗品 id（信息矩阵 / 黑雾引力透镜）；内部自带重试与幂等
            try { Items.Resolve(); }
            catch (Exception e) { Log.LogWarning("解析消耗品失败: " + e.Message); }

            // 1.14.0：LDB 就绪后跑一次环境自检（创世之书 / 关键物品是否都在）
            try { Compat.Check(); }
            catch (Exception e) { Log.LogWarning("兼容自检失败: " + e.Message); }

            // 1.16.0：科技节点在 Awake 就交给 LDBTool 建表，位置/前置在 PostAdd 里定好
            // （Tech.Layout，由 Protos.PreloadAll 调用）。这里只做「等表就绪 + 事后校验」。
            try { Tech.Tick(); }
            catch (Exception e) { Log.LogWarning("科技节点维护失败: " + e.Message); }

            // 1.7.0：老存档里没有我们那个科技节点的 TechState，缺了就要交 600 万 hash。
            // 命中 ContainsKey 就直接返回，每帧调也没开销。
            try { Tech.EnsureState(); }
            catch (Exception e) { Log.LogWarning("补建科技状态失败: " + e.Message); }

            // 兜底：等存档数据就绪后再试一次（正常情况下 LDBTool 的 PostAdd 回调里已经跑完了）。
            // 加 GameMain.data 判断是为了避免在加载动画阶段提前触发 LDB 的 Resources.Load。
            if (!Protos.IsPreloaded && GameMain.data != null)
                Protos.PreloadAll();
            try
            {
                HubManager.Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Log.LogError("tick 异常: " + e);
            }

            // 通电特效的旋转/呼吸
            try
            {
                HubEffect.Animate(Time.deltaTime);
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Log.LogError("特效动画异常: " + e);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // ---------------- DSPModSave ----------------

        public void Export(BinaryWriter w)
        {
            HubManager.Export(w);
        }

        public void Import(BinaryReader r)
        {
            HubManager.Import(r);
        }

        public void IntoOtherSave()
        {
            // 新存档：清空运行时状态
            HubManager.Clear();
        }
    }
}
