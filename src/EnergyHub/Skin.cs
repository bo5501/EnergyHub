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

        private static bool _loaded;

        public static string Summary { get; private set; }

        public static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            try
            {
                UIRoot root = UIRoot.instance;
                UIGame game = root != null ? root.uiGame : null;
                UIPowerExchangerWindow win = game != null ? game.exchangerWindow : null;
                if (win == null)
                {
                    Summary = "原版能量枢纽窗口尚未创建，使用内置配色";
                    Plugin.Log.LogInfo("[UI] " + Summary);
                    return;
                }

                // ---- 背景：窗口层级里面积最大的那个 Image（排除全屏遮罩）----
                Image best = null;
                float bestArea = 0f;
                Image[] images = win.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null || img.sprite == null)
                        continue;
                    Rect r = img.rectTransform.rect;
                    float area = Mathf.Abs(r.width * r.height);
                    if (area < 30000f || area > 900f * 700f)
                        continue;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = img;
                    }
                }
                if (best != null)
                {
                    PanelSprite = best.sprite;
                    Color c = best.color;
                    // 原版底图如果带透明度，保证窗口不至于看不出边界
                    PanelColor = new Color(c.r, c.g, c.b, Mathf.Max(c.a, 0.92f));
                    PanelType = best.type == Image.Type.Simple ? Image.Type.Sliced : best.type;
                }

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

                Summary = "已套用原版能量枢纽样式: 背景=" + (PanelSprite != null ? PanelSprite.name : "无") +
                          " 按钮=" + (ButtonSprite != null ? ButtonSprite.name : "无") +
                          " 关闭=" + (CloseSprite != null ? CloseSprite.name : "无") +
                          " 模板=" + (CloseTemplate != null ? CloseTemplate.name : "无") +
                          "×" + (CloseIconSprite != null ? "有" : "无") + " 染色=" + CloseTint +
                          " 标题字号=" + TitleSize +
                          " 选中色=" + tap1 + "/" + tap2;
                Plugin.Log.LogInfo("[UI] " + Summary);
            }
            catch (Exception e)
            {
                Summary = "抓取原版样式失败: " + e.Message;
                Plugin.Log.LogWarning("[UI] " + Summary);
            }
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
