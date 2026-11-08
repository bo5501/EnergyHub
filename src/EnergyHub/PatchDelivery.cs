using System;
using System.Reflection;
using HarmonyLib;

namespace EnergyHub
{
    /// <summary>
    /// 物流清单面板越界兜底（联机 / 改槽位 mod 的兼容修复）。
    ///
    /// 现象：打开物流清单时报
    ///   System.IndexOutOfRangeException
    ///     at UIPlayerDeliveryPanel.OnDeliveryBoxChange ()
    /// 原因：面板用 <c>deliveryPackage._pos2index</c>（格子序号 → 实际存储索引）来搬数据：
    ///   <code>ref GRID local = ref grids[this._index_box2player[gridIndex]];</code>
    ///   而这张表**只在** <see cref="DeliveryPackage.RecalcIndexMaps"/> 里按
    ///   <c>rowCount × colCount</c> 重建，其余位置一律写 -1。
    ///   联机同步（Nebula / Nebulae）或槽位 mod（如 DeliverySlotsTweaks）直接改
    ///   rowCount/colCount 时若没重算，表里就残留 -1 或旧尺寸的映射，
    ///   拿它去索引 grids 就是 <c>grids[-1]</c> → 越界。
    ///
    /// 做法：在 <c>OnDeliveryPackageSizeChanged</c> 之前把尺寸夹回原版上限并重建映射表。
    ///   幂等、不动玩家物品：映射表完全由 rowCount / colCount / unlocked 决定，
    ///   原版 <see cref="DeliveryPackage.NotifySizeChange"/> 走的也是同一条路。
    /// </summary>
    internal static class Patch_DeliveryPanelGuard
    {
        /// <summary>面板缓存的映射数组引用（private，正常与 deliveryPackage._pos2index 是同一个对象）。</summary>
        private static readonly FieldInfo FIndexBox2Player =
            AccessTools.Field(typeof(UIPlayerDeliveryPanel), "_index_box2player");

        private static bool _logged;

        /// <summary>
        /// 显式挂补丁（不走 PatchAll）：目标方法在不同游戏版本里若改名，
        /// 这里返回后只跳过本补丁，不会连累整个模组的其它 patch。
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(UIPlayerDeliveryPanel), "OnDeliveryPackageSizeChanged");
                if (target == null)
                {
                    Plugin.Log.LogWarning("找不到 UIPlayerDeliveryPanel.OnDeliveryPackageSizeChanged，跳过物流清单兜底");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(Patch_DeliveryPanelGuard), "Prefix"));
                Plugin.Log.LogInfo("已挂上物流清单索引兜底（联机/槽位 mod 改尺寸后不再越界）");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("挂物流清单兜底失败（不影响其它功能）: " + e.Message);
            }
        }

        private static void Prefix(UIPlayerDeliveryPanel __instance)
        {
            try
            {
                Repair(__instance);
            }
            catch (Exception e)
            {
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogWarning("物流清单索引修复失败（不影响其它功能）: " + e.Message);
                }
            }
        }

        private static void Repair(UIPlayerDeliveryPanel panel)
        {
            DeliveryPackage pkg = panel?.deliveryPackage;
            if (pkg == null || pkg.grids == null || pkg._pos2index == null)
                return;

            int row = pkg.rowCount;
            int col = pkg.colCount;

            // 夹到原版上限，同时挡掉负数：负数会让 activeCount 变成负值，
            // RecalcIndexMaps 直接把整张表写成 -1，面板一开就炸。
            if (row > DeliveryPackage.MAX_ROWCOUNT) row = DeliveryPackage.MAX_ROWCOUNT;
            if (col > DeliveryPackage.MAX_COLCOUNT) col = DeliveryPackage.MAX_COLCOUNT;
            if (row < 0) row = 0;
            if (col < 0) col = 0;

            // 极端情况：别的 mod 把 grids 换成更短的数组。映射表的地址公式按 5 列宽算，
            // 行数超过 grids.Length / 5 一样会越界。
            int capacity = pkg.grids.Length;
            if (capacity >= DeliveryPackage.MAX_COLCOUNT)
            {
                int maxRow = capacity / DeliveryPackage.MAX_COLCOUNT;
                if (row > maxRow) row = maxRow;
            }

            bool repaired = row != pkg.rowCount || col != pkg.colCount;

            // 尺寸没变也要查映射表本身是否失效（-1 / 旧尺寸的残留）
            if (!repaired && row > 0 && col > 0)
            {
                int active = row * col;
                if (active > pkg._pos2index.Length)
                    active = pkg._pos2index.Length;
                for (int i = 0; i < active; i++)
                {
                    int expect = ((i / col) + 1) * DeliveryPackage.MAX_COLCOUNT - (col - i % col);
                    if (pkg._pos2index[i] != expect)
                    {
                        repaired = true;
                        break;
                    }
                }
            }

            pkg.rowCount = row;
            pkg.colCount = col;

            // 重建 rowCount × colCount 个格子的映射，其余位置写 -1（幂等）
            pkg.RecalcIndexMaps();

            // 面板若持有旧数组（被别的 mod 换过 _pos2index 才可能），这里对齐引用
            if (FIndexBox2Player != null)
            {
                object cached = FIndexBox2Player.GetValue(panel);
                if (!ReferenceEquals(cached, pkg._pos2index))
                    FIndexBox2Player.SetValue(panel, pkg._pos2index);
            }

            if (repaired && !_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo("物流清单索引已重建（" + row + "×" + col +
                                   "）：这是联机 / 槽位 mod 改尺寸后未重算映射的兼容修复");
            }
        }
    }
}
