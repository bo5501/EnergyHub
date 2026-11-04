using System;
using System.Reflection;
using BepInEx.Logging;

namespace EnergyHub
{
    /// <summary>
    /// 运行环境自检（1.14.0）。
    ///
    /// 目的不是「支持所有 MOD」，而是让出问题时一眼能看出是环境问题还是本 MOD 的问题：
    ///   · 装了创世之书（ProjectGenesis）时，它会大量改写配方体系与科技树布局，
    ///     我们依赖的原版物品有可能被替换/隐藏；
    ///   · 装了 Nebula 时，结算只由主机执行，客户端面板是只读的。
    ///
    /// 检查结果只打日志（不弹窗、不改行为），LDB 就绪后跑一次。
    /// </summary>
    public static class Compat
    {
        private static bool _done;
        private static int _genesis = -1;   // -1 未知 / 0 没装 / 1 装了

        /// <summary>
        /// 是否装了创世之书（ProjectGenesis）。反射扫程序集，结果缓存。
        ///
        /// 科技节点挂哪里要用到它（创世把科技树布局整个重排过），
        /// 而 Tech.Register 可能早于 <see cref="Check"/> 执行，所以这里独立判定、不依赖 Check。
        /// </summary>
        public static bool HasGenesis
        {
            get
            {
                if (_genesis < 0)
                    _genesis = HasAssembly("ProjectGenesis") ? 1 : 0;
                return _genesis == 1;
            }
        }

        public static void Check()
        {
            if (_done)
                return;

            try
            {
                if (LDB.items == null || LDB.items.dataArray == null || LDB.items.dataArray.Length == 0)
                    return;
            }
            catch
            {
                return;
            }

            _done = true;

            try
            {
                bool genesis = HasGenesis;
                Plugin.Log.LogInfo("=== 兼容自检 ===");
                Plugin.Log.LogInfo("  创世之书（ProjectGenesis）: " + (genesis ? "已检测到，配方/科技按降级模式处理" : "未安装"));
                Plugin.Log.LogInfo("  Nebula 联机框架: " + (MP.HasNebula ? "已安装" : "未安装") +
                                   (MP.Active ? (MP.IsClient ? "（当前是客户端，只读显示）" : "（当前是主机，负责结算）") : ""));

                CheckItem("建筑物品", C.ItemId);
                CheckItem("配方原料-蓄电器", 2206);
                CheckItem("升级材料-蓄电器", C.UpgradeItem);
                CheckItem("升级材料-引力矩阵", Items.GravitonMatrix);
                CheckItem("传能耗材-信息矩阵", Items.InformationMatrix);
                CheckItem("提速耗材-增产剂Mk.III", C.BoostItem);

                // 1.16.0：科技节点改成「Awake 交给 LDBTool → 建表后定位置」两步，
                // 自检通常跑在两步之间，此时还没就位是正常的，别吓人。
                if (!Tech.LaidOut)
                    Plugin.Log.LogInfo("  科技节点: 已提交注册，等 LDB 建表后再定位置（属正常流程）");
                else if (!TechExists())
                    Plugin.Log.LogWarning("  科技节点注册失败（科技树里会找不到本 MOD），请把上面的日志发给作者");
                else if (!string.IsNullOrEmpty(Tech.AnchorNote))
                    Plugin.Log.LogInfo("  科技节点锚点: " + Tech.AnchorNote +
                                       (genesis ? "（创世之书模式）" : ""));

                Plugin.Log.LogInfo("================");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("兼容自检失败: " + e.Message);
            }
        }

        private static void CheckItem(string label, int id)
        {
            try
            {
                ItemProto p = id > 0 ? LDB.items.Select(id) : null;
                if (p == null)
                {
                    Plugin.Log.LogWarning("  " + label + "（id=" + id + "）在当前环境里不存在" +
                                          "——相关功能会不可用，通常是配方体系被其它大型 MOD 改写过");
                    return;
                }

                Plugin.Log.LogInfo("  " + label + ": " + p.name + " (id=" + id + ")");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("  " + label + " 校验异常: " + e.Message);
            }
        }

        private static bool TechExists()
        {
            try
            {
                return C.TechId > 0 && LDB.techs != null && LDB.techs.Select(C.TechId) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasAssembly(string name)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                if (asms[i] != null && asms[i].GetName().Name == name)
                    return true;
            }
            return false;
        }
    }
}
