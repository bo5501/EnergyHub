using UnityEngine;
using UnityEngine.EventSystems;

namespace EnergyHub
{
    /// <summary>
    /// 窗口拖动：挂在一个覆盖标题栏的透明 Image 上，拖它就能移动整个窗口。
    /// 原版 DSP 的窗口也是靠标题栏拖动，这里做成同样手感（并且点标题会置顶）。
    /// </summary>
    public class WindowDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        /// <summary>要移动的窗口根节点（pivot 在右上角）。</summary>
        public RectTransform target;

        private Canvas _canvas;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (target == null)
                return;
            target.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null)
                return;

            // eventData.delta 是屏幕像素，Canvas 有缩放时要除回去
            float s = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            Vector2 p = target.anchoredPosition + eventData.delta / s;
            Clamp(ref p);
            target.anchoredPosition = p;
        }

        /// <summary>别让窗口被拖到屏幕外找不回来：左上角至少留 150×45 可见。</summary>
        private void Clamp(ref Vector2 p)
        {
            var parent = target.parent as RectTransform;
            if (parent == null)
                return;
            float pw = parent.rect.width;
            float ph = parent.rect.height;
            if (pw < 1f || ph < 1f)
                return;

            float keepX = 150f;
            float keepY = 45f;

            // 锚点在父的右上角，窗口向右下展开 → x、y 通常都是负值
            float minX = -pw + keepX;
            float maxX = -20f;
            float minY = -ph + keepY;
            float maxY = -2f;

            if (maxX < minX) maxX = minX;
            if (maxY < minY) maxY = minY;

            p.x = Mathf.Clamp(p.x, minX, maxX);
            p.y = Mathf.Clamp(p.y, minY, maxY);
        }
    }
}
