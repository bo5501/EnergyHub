using System;
using HarmonyLib;
using UnityEngine;

namespace EnergyHub
{
    /// <summary>
    /// 建造栏兜底（1.21.0）。
    ///
    /// ============ 1.20.0 为什么没修好 ============
    ///
    /// <c>UIBuildMenu.StaticLoad()</c> 只做一件事：遍历 <c>LDB.items.dataArray</c>，
    /// 把每个 <c>item.BuildIndex</c> 拆成「页 = /100」「槽 = %100」，填进
    /// <c>protos[页, 槽]</c>。之后 <c>SetCurrentCategory</c> 就按这张表摆按钮。
    ///
    /// 1.20.0 的做法是在 proto 建表阶段（<see cref="Protos.PreloadAll"/>）改
    /// <c>item.BuildIndex</c>，前提是「StaticLoad 还没跑」。这个前提不成立：
    ///   · 别的 MOD / 提前创建的 UI 都可能先把 StaticLoad 跑掉，<c>staticLoaded</c> 一旦为 true
    ///     就再也不会重来，我们改的 BuildIndex 压根没被读过；
    ///   · 就算读到了，**同一页同一个槽被两个物品同时声明**时，dataArray 里靠后的那个会覆盖前面的
    ///     —— 我们挑「空槽」是在 PreloadAll 那一刻判定的，之后别的 MOD 再塞一个同槽物品，
    ///     我们的按钮就被顶掉了（页签里自然看不到）。
    ///
    /// 所以这里改成「事后落位」：不管 StaticLoad 什么时候跑、跑几次，
    /// 都在它跑完之后**直接把 protos[页, 槽] 写成我们的物品**，并把这张表里
    /// 我们这个物品的其它残留格清掉（否则旧位置会留下一个幽灵按钮）。
    /// 槽位按「当刻的真实占用」重算，优先 F10，被占就找最近的空槽。
    /// </summary>
    [HarmonyPatch(typeof(UIBuildMenu), "StaticLoad")]
    internal static class Patch_BuildMenuStaticLoad
    {
        private static void Prefix()
        {
            try { Protos.ResolveBuildIndex(Protos.FindItem()); }
            catch (Exception e) { Plugin.Log.LogWarning("建造栏：末尾重算 BuildIndex 失败: " + e.Message); }
        }

        private static void Postfix()
        {
            try { Protos.FinalizeBuildSlot(); }
            catch (Exception e) { Plugin.Log.LogWarning("建造栏：落位失败: " + e.Message); }
        }
    }
}
