using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnergyHub
{
    /// <summary>
    /// 消耗品 id 解析。
    ///
    /// 「黑雾引力透镜」是原版 Dark Fog 版本追加的物品，id = <see cref="DarkFogLens"/>（1211），
    /// 紧跟在引力透镜 1209 / 空间翘曲器 1210 后面。它**确实存在**，
    /// 中文名在 Locale/2052/prototype.txt 里，物品本体在 resources.assets 的 ItemProtoSet 中。
    ///
    /// 所以这里以「已知 id」为骨架，不再把物品识别完全押在本地化名字上：
    ///   1. 先按 id 探测 1211(黑雾引力透镜) / 1209(引力透镜)，存在就进候选（1211 永远排第一）；
    ///   2. 再追加名字里带「透镜」/ "Lens" 的物品（兼容其它透镜 mod）；
    ///   3. 本地化名字还没加载好时，第 1 步依然有效，不会再出现「背包里有却提示找不到物品」。
    /// </summary>
    internal static class Items
    {
        /// <summary>信息矩阵（同星系传能的消耗品）。</summary>
        public static int InformationMatrix = 6004;

        /// <summary>
        /// 引力矩阵（能量池升级材料之一，默认 6005）。
        /// 和 <see cref="InformationMatrix"/> 一样按本地化名字校正：
        /// 版本差异 / 汉化包可能让 id 偏移，光靠硬编码会点「升级」时报「缺少引力矩阵」。
        /// </summary>
        public static int GravitonMatrix = C.UpgradeItem2;

        /// <summary>空间翘曲器（跨星系传能的必需品）。</summary>
        public static int Warper = 1210;

        /// <summary>黑雾引力透镜：跨星系发送端的透镜消耗品（原版 id = 1211）。</summary>
        public const int DarkFogLens = 1211;

        /// <summary>普通引力透镜（射线接收站用的那种，id = 1209），作为兼容候选。</summary>
        public const int GravityLens = 1209;

        /// <summary>「透镜」这一格接受的所有物品 id，按优先级排序（1211 黑雾透镜在最前）。</summary>
        public static readonly List<int> Lenses = new List<int>();

        private static readonly List<int> Fallback = new List<int>(2) { DarkFogLens, GravityLens };

        /// <summary>
        /// 透镜格实际要遍历的 id 列表。
        /// Lenses 还没解析出来时退回 {1211, 1209}，保证任何时刻都能放 / 取 / 计数。
        /// </summary>
        public static List<int> Candidates
        {
            get { return Lenses.Count > 0 ? Lenses : Fallback; }
        }

        /// <summary>透镜格展示 / 消费时用的主物品（永远是黑雾引力透镜优先）。</summary>
        public static int PrimaryLens
        {
            get { return Lenses.Count > 0 ? Lenses[0] : DarkFogLens; }
        }

        public static bool IsLens(int itemId)
        {
            if (itemId <= 0)
                return false;
            if (itemId == DarkFogLens || itemId == GravityLens)
                return true;
            return Lenses.Contains(itemId);
        }

        /// <summary>透镜格的显示名。</summary>
        public static string LensLabel
        {
            get
            {
                if (Lenses.Count == 0)
                    return Name(DarkFogLens);
                // 主物品就是黑雾引力透镜时不再堆「+N」，免得玩家以为还要别的透镜
                if (Lenses[0] == DarkFogLens || Lenses.Count == 1)
                    return Name(Lenses[0]);
                return Name(Lenses[0]) + " +" + (Lenses.Count - 1);
            }
        }

        public static string Summary { get; private set; } = "未解析";

        private static bool _done;
        private static int _tries;
        private static int _lastTryFrame = -1000;

        /// <summary>每帧都可以调：内部有短路，未解析成功时每 20 帧重试一次（本地化名字加载有先后）。</summary>
        public static void Resolve()
        {
            if (_done)
                return;
            if (Time.frameCount - _lastTryFrame < 20)
                return;
            _lastTryFrame = Time.frameCount;

            ItemProto[] arr;
            try
            {
                if (LDB.items == null || LDB.items.dataArray == null)
                    return;
                arr = LDB.items.dataArray;
                if (arr.Length == 0)
                    return;
            }
            catch
            {
                return;
            }

            int matrix = -1;
            int graviton = -1;
            var nameHits = new List<int>(4);

            for (int i = 0; i < arr.Length; i++)
            {
                ItemProto p = arr[i];
                if (p == null || p.ID <= 0)
                    continue;

                string n;
                try { n = p.name; }
                catch { continue; }
                if (string.IsNullOrEmpty(n))
                    continue;
                n = n.Trim();
                if (n.Length == 0)
                    continue;

                if (matrix < 0 && (Hit(n, "信息矩阵") || Hit(n, "Information Matrix")))
                {
                    matrix = p.ID;
                    continue;
                }
                if (graviton < 0 && (Hit(n, "引力矩阵") || Hit(n, "Graviton Matrix")))
                {
                    graviton = p.ID;
                    continue;
                }
                if (Hit(n, "透镜") || Hit(n, "Lens"))
                {
                    if (!nameHits.Contains(p.ID))
                        nameHits.Add(p.ID);
                }
            }

            // 1) 已知 id 骨架：不依赖名字，存在即入列（1211 永远第一）
            var ids = new List<int>(8);
            bool hasDarkFog = Exists(DarkFogLens);
            bool hasGravity = Exists(GravityLens);
            if (hasDarkFog) ids.Add(DarkFogLens);
            if (hasGravity) ids.Add(GravityLens);

            // 2) 名字命中的其它透镜（去重，按 id 升序）
            nameHits.Sort();
            for (int i = 0; i < nameHits.Count; i++)
            {
                if (!ids.Contains(nameHits[i]))
                    ids.Add(nameHits[i]);
            }

            // 3) 什么都没解析到就再等（名字 / LDB 还没就绪），最多 600 次后按内置兜底
            if (ids.Count == 0 && matrix < 0)
            {
                if (++_tries < 600)
                    return;
            }

            if (matrix > 0)
                InformationMatrix = matrix;
            if (graviton > 0)
                GravitonMatrix = graviton;

            Lenses.Clear();
            if (ids.Count > 0)
                Lenses.AddRange(ids);

            _done = true;

            var sb = new System.Text.StringBuilder();
            sb.Append("信息矩阵=").Append(InformationMatrix).Append('(').Append(Name(InformationMatrix)).Append(')');
            sb.Append(" 引力矩阵=").Append(GravitonMatrix).Append('(').Append(Name(GravitonMatrix)).Append(')');
            sb.Append(" 空间翘曲器=").Append(Warper).Append('(').Append(Name(Warper)).Append(')');
            sb.Append(" 透镜组=").Append(Lenses.Count);
            for (int i = 0; i < Lenses.Count; i++)
                sb.Append(" #").Append(Lenses[i]).Append('(').Append(Name(Lenses[i])).Append(')');
            if (Lenses.Count == 0)
                sb.Append(" 兜底=").Append(DarkFogLens).Append('(').Append(Name(DarkFogLens)).Append(')');
            Summary = sb.ToString();
            Plugin.Log.LogInfo("[物品] " + Summary);
        }

        private static bool Exists(int itemId)
        {
            try
            {
                ItemProto p = LDB.items.Select(itemId);
                return p != null && p.ID == itemId;
            }
            catch { }
            return false;
        }

        /// <summary>宽容匹配：名字里包含关键字就算（原版名字偶尔带前后缀/空格/换行）。</summary>
        private static bool Hit(string name, string key)
        {
            return name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string Name(int itemId)
        {
            try
            {
                ItemProto p = LDB.items.Select(itemId);
                if (p != null && !string.IsNullOrEmpty(p.name))
                    return p.name;
            }
            catch { }
            return "?";
        }

        public static Sprite Icon(int itemId)
        {
            try
            {
                ItemProto p = LDB.items.Select(itemId);
                if (p != null) return p.iconSprite;
            }
            catch { }
            return null;
        }
    }
}
