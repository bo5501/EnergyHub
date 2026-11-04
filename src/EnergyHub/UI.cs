using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnergyHub
{
    /// <summary>UGUI 小工具：全部用代码建，避免依赖 AssetBundle 里的 UI prefab。</summary>
    internal static class UI
    {
        private static Font _font;
        private static Sprite _white;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                try
                {
                    if (UIRoot.instance != null)
                    {
                        var t = UIRoot.instance.GetComponentInChildren<Text>(true);
                        if (t != null && t.font != null) _font = t.font;
                    }
                }
                catch { }
                if (_font == null)
                {
                    foreach (string name in new[] { "Microsoft YaHei", "SimHei", "微软雅黑", "Noto Sans CJK SC" })
                    {
                        try
                        {
                            var f = Font.CreateDynamicFontFromOSFont(name, 16);
                            if (f != null) { _font = f; break; }
                        }
                        catch { }
                    }
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null) Plugin.Log.LogWarning("拿不到任何字体，UI 文字可能不显示");
                return _font;
            }
        }

        /// <summary>1x1 白色贴图，用于纯色矩形。</summary>
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Clamp;
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                return _white;
            }
        }

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        public static void Rect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            return Panel(parent, name, x, y, w, h, color, null, Image.Type.Simple);
        }

        /// <summary>带精灵的面板（用原版 UI 的九宫格贴图，观感与原版窗口一致）。</summary>
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h,
            Color color, Sprite sprite, Image.Type type)
        {
            var rt = Node(parent, name);
            Rect(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : White;
            img.color = color;
            img.type = sprite != null ? type : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, float x, float y, float w, float h,
            int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rt = Node(parent, name);
            Rect(rt, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Btn(Transform parent, string name, string text, float x, float y, float w, float h,
            Color bg, Color fg, Action onClick, int size = 15)
        {
            return Btn(parent, name, text, x, y, w, h, bg, fg, onClick, size, null);
        }

        public static Button Btn(Transform parent, string name, string text, float x, float y, float w, float h,
            Color bg, Color fg, Action onClick, int size, Sprite sprite)
        {
            var rt = Node(parent, name);
            Rect(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : White;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.color = bg;
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var label = Label(rt, "label", text, 0f, 0f, w, h, size, fg, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        public static Image Icon(Transform parent, string name, float x, float y, float size, Sprite sprite)
        {
            var rt = Node(parent, name);
            Rect(rt, x, y, size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);
            img.raycastTarget = false;
            return img;
        }

        public static string FmtEnergy(long joule)
        {
            double j = joule;
            if (Math.Abs(j) >= 1e18) return (j / 1e18).ToString("F2") + " EJ";
            if (Math.Abs(j) >= 1e15) return (j / 1e15).ToString("F2") + " PJ";
            if (Math.Abs(j) >= 1e12) return (j / 1e12).ToString("F2") + " TJ";
            if (Math.Abs(j) >= 1e9) return (j / 1e9).ToString("F2") + " GJ";
            if (Math.Abs(j) >= 1e6) return (j / 1e6).ToString("F2") + " MJ";
            if (Math.Abs(j) >= 1e3) return (j / 1e3).ToString("F1") + " kJ";
            return j.ToString("F0") + " J";
        }

        public static string FmtPower(double wattPerSec)
        {
            double w = wattPerSec;
            if (Math.Abs(w) >= 1e15) return (w / 1e15).ToString("F2") + " PW";
            if (Math.Abs(w) >= 1e12) return (w / 1e12).ToString("F2") + " TW";
            if (Math.Abs(w) >= 1e9) return (w / 1e9).ToString("F2") + " GW";
            if (Math.Abs(w) >= 1e6) return (w / 1e6).ToString("F2") + " MW";
            if (Math.Abs(w) >= 1e3) return (w / 1e3).ToString("F1") + " kW";
            return w.ToString("F0") + " W";
        }
    }
}
