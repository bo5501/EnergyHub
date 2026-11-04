using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using CommonAPI;
using CommonAPI.Systems;
using crecheng.DSPModSave;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace EnergyHub
{
    [BepInPlugin(C.Guid, C.Name, C.Version)]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency(LDBToolPlugin.MODGUID)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
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

            // 先装 patch：ModelProto.Preload 会重建 prefabDesc，必须在它跑之前挂上
            try
            {
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
                Patch_ExcBeltUpdate.Apply(_harmony);

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

            // 联机（1.14.0）：软兼容，Nebula 没装就什么都不做。
            // 必须在 Harmony 之后调用——子模块里没有任何 patch，只是注册网络包。
            try { MP.Init(); }
            catch (Exception e) { Log.LogWarning("联机兼容层初始化失败: " + e.Message); }
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

            // 1.14.0：LDB 就绪后跑一次环境自检（创世之书 / Nebula / 关键物品是否都在）
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

            // 联机状态采样（0.5 秒一次）：决定本帧是「主机结算」还是「客户端只读」
            try { MP.Poll(Time.deltaTime); }
            catch (Exception e) { Log.LogWarning("联机状态采样失败: " + e.Message); }

            try
            {
                HubManager.Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                if (Time.frameCount % 600 == 0)
                    Log.LogError("tick 异常: " + e);
            }

            // 主机：把本帧结算结果广播给客户端（内部节流 0.5 秒）
            try { MP.Broadcast(Time.deltaTime); }
            catch (Exception e) { Log.LogWarning("联机广播失败: " + e.Message); }

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
