using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnergyHub
{
    /// <summary>
    /// 从原版「能量枢纽」窗口（UIPowerExchangerWindow）就地抓取视觉样式：
    /// 背景九宫格贴图、标题字体/字号/颜色、模式按钮贴图、进度条贴图、强调色。
    ///
    /// 这样我们的窗口用的是原版那一套 UI 资源，观感和原版能量枢纽窗口一致，
    /// 又不用去反编译/还原它的 prefab 层级（层级里绑着大量 _OnOpen 逻辑，不能直接复用）。
    /// 抓不到时退回自带配色，不会崩。
    /// </summary>
    internal static class Skin
    {
        public static Sprite PanelSprite;
        // 没有原版底图时用深色面板，避免整窗变成白块
        public static Color PanelColor = new Color(0.055f, 0.082f, 0.110f, 0.96f);
        public static Image.Type PanelType = Image.Type.Sliced;
        /// <summary>
        /// 原版背景图的 <c>pixelsPerUnitMultiplier</c>（1.21.2）。
        /// 九宫格拉伸时它决定边框的像素厚度 —— Unity 默认 1，原版 prefab 上通常不是 1，
        /// 我们自己 new 一个 Image 不复制这个值，边框就会被拉粗/拉细，一眼就不像原版。
        /// </summary>
        public static float PanelPPU = 1f;

        /// <summary>原版标题栏底图 / 底色 / 高度（1.21.2，抓不到就不画）。</summary>
        public static Sprite TitleBarSprite;
        public static Color TitleBarColor = new Color(0f, 0f, 0f, 0f);
        public static float TitleBarHeight;
        /// <summary>原版窗口内容区相对窗口边缘的内边距（左上右下），抓不到用 0。</summary>
        public static Vector4 PanelPadding;

        // 1.21.1：这些「同色系」的底色以前是写死的青绿色，和原版窗口不是一套颜色，
        // 摆在一起一眼就能看出是两个 mod。现在全部由原版面板色派生（见 DeriveTints），
        // 只保留这里的默认值应付「抓不到原版窗口」的情况。
        /// <summary>子区块底（进度条底槽）。</summary>
        public static Color PanelBar = new Color(0.105f, 0.156f, 0.209f, 1f);
        /// <summary>分隔线。</summary>
        public static Color LineColor = new Color(0.196f, 0.275f, 0.353f, 0.9f);
        /// <summary>列表行的常规底色。</summary>
        public static Color RowColor = new Color(0.085f, 0.125f, 0.169f, 0.55f);
        /// <summary>列表行的选中 / 已连接底色。</summary>
        public static Color RowOnColor = new Color(0.145f, 0.305f, 0.380f, 0.85f);

        public static Sprite ButtonSprite;
        public static Sprite BarSprite;
        public static Sprite SlotSprite;
        /// <summary>原版窗口右上角关闭按钮的底图（抓不到就退回普通按钮图）。</summary>
        public static Sprite CloseSprite;
        /// <summary>关闭按钮底图原本的染色（六边形贴图本身近白，靠这个颜色压深，必须一起抓）。</summary>
        public static Color CloseTint = Color.white;
        /// <summary>
        /// 原版关闭按钮的整件克隆（未激活的模板）。单抓底图/图标永远拼不出原版观感——
        /// UIButton.Transition 的悬停变色、按压缩放、× 图标都在子层级里，直接实例化最稳。
        /// </summary>
        public static GameObject CloseTemplate;
        /// <summary>关闭按钮上的 × 图标（子级 Image；抓不到就用文字 × 叠加）。</summary>
        public static Sprite CloseIconSprite;

        public static Font TitleFont;
        public static int TitleSize = 20;
        public static Color TitleColor = new Color(0.86f, 0.92f, 0.97f);

        public static Color TextColor = new Color(0.86f, 0.92f, 0.97f);
        public static Color DimColor = new Color(0.56f, 0.66f, 0.74f);
        public static Color AccentColor = new Color(0.34f, 0.86f, 1.00f);
        public static Color OkColor = new Color(0.45f, 0.95f, 0.62f);
        public static Color WarnColor = new Color(1.00f, 0.67f, 0.30f);

        public static Color BtnColor = new Color(0.13f, 0.20f, 0.26f, 1f);
        public static Color BtnOnColor = new Color(0.16f, 0.44f, 0.58f, 1f);
        /// <summary>原版按钮未选中态的灰（mode2TapColor），抓不到时用深灰。</summary>
        public static Color BtnOffColor = new Color(0.45f, 0.45f, 0.45f, 0.39f);

        // ---- 原版窗口的三种模式指示色（1.21.2）----
        // 原版能量枢纽有三种工作模式，每种有一条固定颜色的状态条（tap / marquee）。
        // 我们面板上的「关机 / 仅蓄电 / 发送 / 接收」以前用的是写死的紫（0.72,0.55,1.00）
        // 和橙（1.00,0.65,0.28）—— 这两个颜色原版 UI 里根本不存在，摆在一起就是两个世界。
        // 现在直接复用原版的三档模式色，第四条（关机）用原版的 marqueeOff 灰。
        public static Color Mode1Color = new Color(0.34f, 0.86f, 1.00f);
        public static Color Mode2Color = new Color(0.72f, 0.55f, 1.00f);
        public static Color Mode3Color = new Color(1.00f, 0.65f, 0.28f);
        public static Color ModeOffColor = new Color(0.45f, 0.45f, 0.45f, 0.39f);

        private static int _tries;

        /// <summary>原版样式是否真的抓到了（没抓到下次开窗再试）。</summary>
        public static bool Ready
        {
            get { return PanelSprite != null; }
        }

        public static string Summary { get; private set; }

        public static void Load()
        {
            if (Ready)
                return;                       // 已经套上原版样式
            if (_tries > 24)
                return;                       // 试过多次都拿不到，别一直刷日志
            _tries++;
            try
            {
                UIRoot root = UIRoot.instance;
                UIGame game = root != null ? root.uiGame : null;
                UIPowerExchangerWindow win = game != null ? game.exchangerWindow : null;
                if (win == null)
                {
                    Summary = "原版能量枢纽窗口尚未创建，先用内置配色（下次开窗再抓）";
                    Plugin.Log.LogInfo("[UI] " + Summary);
                    return;
                }

                // ---- 背景：按「名字像背景」+「深色」+「面积大」打分 ----
                //
                // 1.21.2 重做。以前是「面积最大的深色 Image」，结果经常抓到窗口内部的
                // 某个内容底块（原版能量枢纽窗口里也有一堆大 Image），拿它当整窗背景，
                // 颜色、纹理、圆角全都对不上原版外壳。
                // 另外以前把 alpha 强行抬到 0.92，把原版的半透明玻璃质感改成了实心板。
                Image best = null;
                float bestScore = 0f;
                Image fallback = null;
                float fallbackArea = 0f;
                Image anyImg = null;
                float anyArea = 0f;
                Image[] images = win.GetComponentsInChildren<Image>(true);
                var dump = new System.Text.StringBuilder();
                int dumped = 0;
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null || img.sprite == null)
                        continue;
                    Rect r = img.rectTransform.rect;
                    float area = Mathf.Abs(r.width * r.height);
                    // 全屏遮罩（面积过大）不要；太小的装饰也不要
                    if (area > 900f * 700f || area < 4000f)
                        continue;

                    if (dumped < 24)
                    {
                        dump.Append("\n    ").Append(img.gameObject.name)
                            .Append("  ").Append(r.width.ToString("0")).Append("x").Append(r.height.ToString("0"))
                            .Append("  color=").Append(img.color)
                            .Append("  sprite=").Append(img.sprite.name)
                            .Append("  type=").Append(img.type);
                        dumped++;
                    }

                    if (area > anyArea)
                    {
                        anyArea = area;
                        anyImg = img;
                    }
                    if (area < 30000f)
                        continue;
                    if (area > fallbackArea)
                    {
                        fallbackArea = area;
                        fallback = img;
                    }

                    string n = img.gameObject.name.ToLowerInvariant();
                    bool byName = n.Contains("bg") || n.Contains("back") || n.Contains("panel") ||
                                  n.Contains("window") || n.Contains("frame");
                    Color c0 = img.color;
                    float lum = 0.299f * c0.r + 0.587f * c0.g + 0.114f * c0.b;
                    bool dark = lum <= 0.62f && c0.a >= 0.30f;
                    if (!dark && !byName)
                        continue;

                    // 名字像背景的权重最高，其次看面积
                    float score = area * (byName ? 4f : 1f) * (dark ? 1f : 0.35f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = img;
                    }
                }
                if (best == null)
                    best = fallback != null ? fallback : anyImg;
                if (best != null)
                {
                    PanelSprite = best.sprite;
                    Color c = best.color;
                    // 1.21.2：**保留原版透明度**，只设一个下限免得完全看不见。
                    // 以前 Mathf.Max(c.a, 0.92f) 把半透明玻璃改成了实心板，
                    // 和原版窗口并排就是两种质感。
                    PanelColor = new Color(c.r, c.g, c.b, Mathf.Clamp(c.a <= 0.02f ? 0.9f : c.a, 0.45f, 1f));
                    PanelType = best.type == Image.Type.Simple ? Image.Type.Sliced : best.type;
                    PanelPPU = best.pixelsPerUnitMultiplier > 0.01f ? best.pixelsPerUnitMultiplier : 1f;
                }

                // ---- 标题栏：titleText 的父级，取它身上的底图 ----
                // 原版窗口顶部有一条独立的标题底条（比窗口底略亮），我们没有画，
                // 两个窗口摆在一起时最明显的差别就是「原版有头、我们的没有」。
                try
                {
                    if (win.titleText != null)
                    {
                        Transform tp = win.titleText.transform.parent;
                        if (tp != null)
                        {
                            var rt = tp as RectTransform;
                            TitleBarHeight = rt != null ? Mathf.Abs(rt.rect.height) : 0f;
                            Image ti = tp.GetComponent<Image>();
                            if (ti != null && ti.sprite != null)
                            {
                                TitleBarSprite = ti.sprite;
                                TitleBarColor = ti.color;
                            }
                            else if (TitleBarHeight <= 0f)
                            {
                                TitleBarHeight = 0f;
                            }
                        }
                    }
                }
                catch { }

                // ---- 内容内边距：背景相对窗口根的位置 ----
                try
                {
                    if (best != null && win.rectTransform != null)
                    {
                        Rect winRect = win.rectTransform.rect;
                        Rect bgRect = best.rectTransform.rect;
                        float l = best.rectTransform.offsetMin.x;
                        float b = best.rectTransform.offsetMin.y;
                        float r2 = winRect.width - bgRect.width - l;
                        float t2 = winRect.height - bgRect.height - b;
                        PanelPadding = new Vector4(Mathf.Max(0f, l), Mathf.Max(0f, t2),
                                                   Mathf.Max(0f, r2), Mathf.Max(0f, b));
                    }
                }
                catch { }

                // ---- 标题 ----
                if (win.titleText != null)
                {
                    if (win.titleText.font != null) TitleFont = win.titleText.font;
                    if (win.titleText.fontSize > 0) TitleSize = win.titleText.fontSize;
                    TitleColor = win.titleText.color;
                }

                // ---- 模式按钮 ----
                Sprite bs = SpriteOf(win.exchangerMode1UIButton);
                if (bs == null) bs = SpriteOf(win.emptyUIButton);
                if (bs == null) bs = SpriteOf(win.fullUIButton);
                if (bs != null) ButtonSprite = bs;

                // ---- 关闭按钮：子级里名字含 close 的 UIButton，取底图 + × 图标 ----
                // 1.2.8 修正：1.2.7 只取了底图（hex-btn-r-bg-30 六边形），× 图标在子级
                // Image 上没抓到，按钮就成了"空六边形"。这里把子级图标也抓出来。
                try
                {
                    foreach (var b in win.GetComponentsInChildren<UIButton>(true))
                    {
                        string bn = b.gameObject.name.ToLowerInvariant();
                        if (!bn.Contains("close") && !bn.Contains("shut"))
                            continue;
                        Image bgImg = b.GetComponent<Image>();
                        if (bgImg == null) bgImg = b.GetComponentInChildren<Image>(true);
                        if (bgImg == null || bgImg.sprite == null)
                            continue;
                        CloseSprite = bgImg.sprite;
                        CloseTint = bgImg.color;                  // 白贴图靠染色显色，丢了它就是"空白按钮"
                        foreach (Image im in b.GetComponentsInChildren<Image>(true))
                        {
                            if (im != bgImg && im.sprite != null)
                            {
                                CloseIconSprite = im.sprite;
                                break;
                            }
                        }
                        // 整件克隆一份做模板：运行时直接实例化，外观与原版分毫不差
                        CloseTemplate = UnityEngine.Object.Instantiate(b.gameObject);
                        CloseTemplate.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(CloseTemplate);
                        break;
                    }
                }
                catch { }

                // ---- 进度条 / 槽位 ----
                if (win.progressImages != null && win.progressImages.Length > 0 && win.progressImages[0] != null)
                    BarSprite = win.progressImages[0].sprite;
                if (win.emptyIcon != null && win.emptyIcon.sprite != null)
                    SlotSprite = win.emptyIcon.sprite;

                // ---- 强调色：直接取原版三种模式的选中色 ----
                Color tap1 = win.mode1TapColor;
                if (tap1.a < 0.01f) tap1.a = 1f;
                Color tap2 = win.mode2TapColor;
                if (tap2.a < 0.01f) tap2.a = 1f;
                BtnOnColor = tap1;
                BtnOffColor = tap2;                           // 原版未选中按钮的灰
                if (win.powerNormalColor.a > 0.01f) OkColor = win.powerNormalColor;
                if (win.powerLowColor.a > 0.01f) WarnColor = win.powerLowColor;

                // 原版三档模式色（窗口里那三条随模式变色的状态条）
                Color m1 = Solid(win.mode1TapColor, new Color(0.34f, 0.86f, 1.00f));
                Color m2 = Solid(win.mode2TapColor, m1);
                Color m3 = Solid(win.mode3TapColor, m2);
                Mode1Color = m1;
                Mode2Color = m2;
                Mode3Color = m3;
                Color off = Solid(win.marqueeOffColor, new Color(0.45f, 0.45f, 0.45f));
                ModeOffColor = off;

                // 强调色：原版窗口里最"正"的那个电量色（powerNormalColor），
                // 而不是模式色 —— 模式色是给状态条用的，拿来当进度条颜色会过艳。
                if (win.powerNormalColor.a > 0.01f)
                    AccentColor = new Color(win.powerNormalColor.r, win.powerNormalColor.g,
                                            win.powerNormalColor.b, 1f);

                DeriveTints();

                Summary = "已套用原版能量枢纽样式: 背景=" + (PanelSprite != null ? PanelSprite.name : "无") +
                          " 底色=" + PanelColor +
                          " 九宫格系数=" + PanelPPU +
                          " 标题栏=" + (TitleBarSprite != null ? TitleBarSprite.name + "/" + TitleBarColor : "无") +
                          " 高=" + TitleBarHeight.ToString("0") +
                          " 边距=" + PanelPadding +
                          " 按钮=" + (ButtonSprite != null ? ButtonSprite.name : "无") +
                          " 关闭=" + (CloseSprite != null ? CloseSprite.name : "无") +
                          " 模板=" + (CloseTemplate != null ? CloseTemplate.name : "无") +
                          "×" + (CloseIconSprite != null ? "有" : "无") + " 染色=" + CloseTint +
                          " 标题字号=" + TitleSize +
                          " 选中色=" + tap1 + "/" + tap2;
                Plugin.Log.LogInfo("[UI] " + Summary);
                // 原版窗口的层级摘要：万一观感还是对不上，凭这段日志就能定位抓到的是哪一层
                Plugin.Log.LogInfo("[UI] 原版窗口 Image 清单（名字 尺寸 颜色 贴图 类型）:" + dump);
            }
            catch (Exception e)
            {
                Summary = "抓取原版样式失败: " + e.Message;
                Plugin.Log.LogWarning("[UI] " + Summary);
            }
        }

        /// <summary>
        /// 由原版面板色派生出一整套同色系的底色（1.21.1）。
        /// 以前这些是写死的青绿（0.10/0.16/0.21 那套），和原版窗口的深蓝灰不是一家人，
        /// 两个窗口并排一眼就能看出「这是第三方 mod 画的」。
        /// </summary>
        private static void DeriveTints()
        {
            Color p = PanelColor;
            PanelBar = Mix(p, Color.white, 0.10f, 1f);      // 子区块底：比窗口底亮一档
            LineColor = Mix(p, Color.white, 0.26f, 0.90f);  // 分隔线：再亮一点、留一点透明
            RowColor = Mix(p, Color.black, 0.25f, 0.55f);   // 列表行：比窗口底暗一档、半透明
            RowOnColor = Mix(AccentColor, p, 0.35f, 0.80f); // 已连接的行：掺一点强调色
            DimColor = Mix(TextColor, p, 0.45f, 1f);        // 次要文字：主文字往底色调一点
            BtnColor = Mix(p, Color.white, 0.06f, 1f);      // 按钮底：比窗口底略亮
        }

        private static Color Mix(Color a, Color b, float t, float alpha)
        {
            t = Mathf.Clamp01(t);
            return new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), alpha);
        }

        private static float Lum(Color c)
        {
            return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        }

        /// <summary>原版颜色全是「白贴图 + 染色」，未初始化时 alpha 是 0 —— 这时用兜底色。</summary>
        private static Color Solid(Color c, Color fallback)
        {
            if (c.a < 0.02f)
                return fallback;
            return new Color(c.r, c.g, c.b, 1f);
        }

        private static Sprite SpriteOf(UIButton btn)
        {
            if (btn == null)
                return null;
            Image img = btn.GetComponent<Image>();
            if (img == null) img = btn.GetComponentInChildren<Image>(true);
            return img != null ? img.sprite : null;
        }
    }
}
