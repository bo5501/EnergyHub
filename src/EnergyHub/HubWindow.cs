using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnergyHub
{
    /// <summary>
    /// 点击枢纽弹出的专用窗口：能量数据 / 发送接收切换 / 终端直连 / 消耗品卡槽。
    ///
    /// 对接是点对点直连：
    ///   · 接收端（客户端）→ 列出银河中所有「发送」终端，点「连接」就连上；
    ///   · 发送端（服务端）→ 只显示谁连到了我，连接由对方发起。
    /// 传能消耗：同一恒星系扣「信息矩阵」；跨恒星系扣「空间翘曲器 + 黑雾引力透镜」。
    /// 玩家打开背包或科技树时本窗口自动关闭（见 <see cref="InventoryOpen"/> /
    /// <see cref="TechTreeOpen"/>）；星图 / 戴森球编辑器等其它界面不会。
    /// </summary>
    public class HubWindow : MonoBehaviour
    {
        private const float W = 640f;
        // 1.12.0：升级要两种材料，卡槽 4 格 + 升级 2 格，窗口 836；
        // 1.20.0：取消「提速」（增产剂 Mk.III）那一格，整块上移 28 → 808
        private const float H = 808f;
        private const int PerPage = 5;
        private const float RowY0 = 310f;
        private const float RowStep = 40f;
        private const float RowH = 36f;

        private static HubWindow _inst;
        /// <summary>刚打开窗口时对「背包已开」判定的宽限帧（背包关合有动画）。</summary>
        private static int _invGraceFrame = -1;

        private RectTransform _root;
        private Text _title;
        private Text _netTag;
        private Text _gridText;
        private Text _poolText;
        private Text _rateText;
        private Text _totalText;
        private Text _pendingText;
        /// <summary>接收端专用：显示「发送端卡槽」有没有传能耗材、还剩多少（1.19.3）。</summary>
        private Text _peerStockText;
        /// <summary>缺料时的那句提示（请对方放什么才能传能）。</summary>
        private Text _peerHintText;
        /// <summary>
        /// 非发送模式（接收 / 仅蓄电 / 关机）用的一行「塔内存料」。
        /// 1.21.2：消耗品卡槽只属于发送端，这三格在其它模式下整块收起，
        /// 但玩家仍要知道传送带/以前放进来的料还在不在，所以用纯文本列一行。
        /// </summary>
        private Text _ownStockText;
        /// <summary>消耗品卡槽区（仅发送能量模式显示）：分隔线 / 区块标题。</summary>
        private Image _slotLine;
        private Text _slotTitle;
        private Text _linkText;
        private Text _hintText;
        private Text _listTitle;
        private Text _pageText;
        private Image _poolFill;
        /// <summary>模式色条（原版 tap：一条随工作模式变色的横条）。</summary>
        private Image _tap;
        /// <summary>
        /// 能量池状态区（1.21.0 起只读）：分隔线 / 标题 / 等级与容量文本。
        /// 升级已经搬到科技树「升级」页（见 PoolTech），面板上不再有按钮和材料格。
        /// </summary>
        private Image _upLine;
        private Text _upTitle;
        private Text _levelText;
        /// <summary>电网信息六项（对齐原版 UIPowerNetworkDesc）。</summary>
        private Text _genText, _reqText, _srvText, _stoText, _batText, _expText;
        /// <summary>充能 / 放能功率（对齐原版 powerTextValue1/2）。</summary>
        private Text _chargeText, _dischargeText;
        private Image _modeIdleLine, _modeSendLine, _modeRecvLine, _modeBatLine;
        private Text _modeIdleText, _modeSendText, _modeRecvText, _modeBatText;
        private Button _modeIdleBtn, _modeSendBtn, _modeRecvBtn, _modeBatBtn;
        private Button _btnPrev, _btnNext;

        private class Row
        {
            public GameObject holder;
            public Image bg;
            public Text name;
            public Text note;
            public Button btn;
            public Text btnLabel;
            public Image btnImg;
        }

        private class Cell
        {
            /// <summary>单品格：具体物品 id。透镜格不用这个字段判断，见 <see cref="isLens"/>。</summary>
            public int itemId;
            /// <summary>true = 「透镜」格，接受任意透镜类物品（黑雾透镜优先）。</summary>
            public bool isLens;
            public Image icon;
            public Text name;
            public Text tag;
            public Text count;
            /// <summary>tag 的原始文案（同星系 / 跨星系），未启用时会被换成「未启用」。</summary>
            public string tagBase;
            public Button btnAdd;
            public Image btnAddImg;
            /// <summary>「取出」按钮：把这一格里的物品退回背包。</summary>
            public Button btnTake;
            public GameObject holder;
            /// <summary>「放入」按钮一次放几个。</summary>
            public int addCount = 20;
            /// <summary>提速格：同星系 / 跨星系链路都用得上，任何链路类型下都可放。</summary>
            public bool always;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Cell> _cells = new List<Cell>();
        private readonly List<PeerRow> _peers = new List<PeerRow>();
        private int _page;

        private int _entityId;
        private int _planetId = -1;
        private float _refreshTimer;

        // 1.21.1：这几块底色不再写死，全部取 Skin 里「由原版能量枢纽窗口派生」的那一套，
        // 保证整个窗口和原版是同一套配色（Skin 抓不到原版时也有同色系兜底值）。
        private static Color CBar { get { return Skin.PanelBar; } }
        private static Color CLine { get { return Skin.LineColor; } }
        private static Color CRow { get { return Skin.RowColor; } }
        private static Color CRowOn { get { return Skin.RowOnColor; } }
        // 1.21.2：模式色不再写死。以前这里是紫 / 橙两个原版 UI 里根本不存在的颜色，
        // 直接换成原版能量枢纽窗口的三档模式指示色（mode1/2/3TapColor）。
        private static Color COrange { get { return Skin.Mode3Color; } }
        private static Color CViolet { get { return Skin.Mode2Color; } }

        // ---------------- 生命周期 ----------------

        public static bool IsOpen => _inst != null && _inst.gameObject.activeSelf;

        public static void OpenWindow(int entityId)
        {
            // 1.21.1：原版样式改成「抓到为止」的懒加载 —— 第一次开窗时原版能量枢纽窗口
            // 可能还没被创建过（Skin 抓不到底图，只能用兜底配色）。这里每次开窗补抓一次，
            // 抓到了就把先前用兜底配色搭出来的窗口重建一遍，保证最终和原版同一套颜色。
            bool wasReady = Skin.Ready;
            Skin.Load();
            if (_inst != null && !wasReady && Skin.Ready)
            {
                UnityEngine.Object.Destroy(_inst.gameObject);
                _inst = null;
            }

            if (_inst == null) Create();
            if (_inst == null) return;

            // 背包是唯一会让本窗口自动关闭的界面，所以点建筑时先把背包收起来，
            // 否则刚打开的窗口会在下一帧被背包判定关掉。
            // 收起后留几帧宽限：背包有开合动画，active 不一定是当帧就变 false。
            try
            {
                UIGame g = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
                if (g != null && g.inventoryWindow != null && g.inventoryWindow.active)
                    g.ShutPlayerInventory();
            }
            catch { }
            _invGraceFrame = Time.frameCount + 4;

            _inst._entityId = entityId;
            _inst._planetId = GameMain.localPlanet != null ? GameMain.localPlanet.id : -1;
            _inst._page = 0;
            _inst.gameObject.SetActive(true);
            _inst.transform.SetAsLastSibling();
            _inst.Refresh(true);
        }

        public static void CloseIfOpen()
        {
            if (_inst != null && _inst.gameObject.activeSelf)
                _inst.gameObject.SetActive(false);
        }

        private static void Create()
        {
            if (UIRoot.instance == null || UIRoot.instance.overlayCanvas == null)
            {
                Plugin.Log.LogWarning("UIRoot 尚未就绪，无法创建窗口");
                return;
            }
            Skin.Load();
            Transform parent = UIRoot.instance.overlayCanvas.transform;
            var go = new GameObject("EnergyHubWindow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _inst = go.AddComponent<HubWindow>();
            _inst.Build();
        }

        private void Build()
        {
            _root = (RectTransform)transform;
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.sizeDelta = new Vector2(W, H);
            _root.anchoredPosition = new Vector2(-370f, -60f);

            // 背景：原版能量枢纽窗口的九宫格底图。
            // 1.21.2：连 pixelsPerUnitMultiplier 一起复制 —— 九宫格拉伸时边框的像素厚度
            // 由它决定，不复制就会把原版的细边框拉成粗边，怎么调色都不像。
            Image bg = UI.Panel(_root, "bg", 0f, 0f, W, H, Skin.PanelColor, Skin.PanelSprite, Skin.PanelType);
            if (bg != null && Skin.PanelPPU > 0.01f)
                bg.pixelsPerUnitMultiplier = Skin.PanelPPU;

            // 标题栏底条：原版窗口顶部那条比窗口底略亮的横条（抓到才画）
            if (Skin.TitleBarSprite != null && Skin.TitleBarHeight > 8f)
                UI.Panel(_root, "titleBar", 0f, 0f, W, Skin.TitleBarHeight, Skin.TitleBarColor,
                    Skin.TitleBarSprite, Image.Type.Sliced);

            // 边框：原版是细描边，不是我们以前那条 2px 亮青色高光边（一眼就"不是原版"）
            Color edgeC = new Color(Skin.LineColor.r, Skin.LineColor.g, Skin.LineColor.b, 0.7f);
            UI.Panel(_root, "edgeT", 0f, 0f, W, 1f, edgeC);
            UI.Panel(_root, "edgeB", 0f, H - 1f, W, 1f, edgeC);
            UI.Panel(_root, "edgeL", 0f, 0f, 1f, H, edgeC);
            UI.Panel(_root, "edgeR", W - 1f, 0f, 1f, H, edgeC);

            _title = UI.Label(_root, "title", "星际能量枢纽塔", 34f, 6f, 460f, 40f,
                Skin.TitleSize > 0 ? Skin.TitleSize : 20, Skin.TitleColor);

            // 1.19.5：标题旁显示当前联机角色（单机 / 联机 · 主机 / 联机 · 客户端）
            _netTag = UI.Label(_root, "netTag", MP.StatusText, 178f, 22f, 300f, 18f, 12, Skin.DimColor);
            if (Skin.CloseTemplate != null)
            {
                // 原版关闭按钮整件克隆：外观（底图/×图标/悬停变色/按压缩放）与原版分毫不差，
                // 只改挂载位置并接管点击。以前手抓单张贴图拼装，怎么看都不像原版。
                GameObject cb = Instantiate(Skin.CloseTemplate, _root, false);
                cb.SetActive(true);
                var crt = (RectTransform)cb.transform;
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(0f, 1f);
                float cw = Mathf.Max(crt.rect.width, 30f);
                float ch = Mathf.Max(crt.rect.height, 30f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = new Vector2(W - 10f - cw * 0.5f, -9f - ch * 0.5f);
                var uib = cb.GetComponent<UIButton>();
                if (uib != null)
                {
                    UIButton captured = uib;
                    captured.onClick += new Action<int>(delegate { CloseIfOpen(); });
                }
                else
                {
                    var ub = cb.GetComponent<Button>();
                    if (ub != null) ub.onClick.AddListener(CloseIfOpen);
                }
                Plugin.Log.LogInfo("[UI] 关闭按钮已整件克隆原版: " + cb.name +
                                   " size=" + cw.ToString("0") + "x" + ch.ToString("0"));
            }
            else if (Skin.CloseSprite != null)
            {
                // 兜底：抓不到整件时用底图（原版染色）+ × 文字拼一个
                Button cb = UI.Btn(_root, "close", "", W - 46f, 8f, 36f, 36f, Skin.CloseTint,
                    Color.white, CloseIfOpen, 14, Skin.CloseSprite);
                var t = UI.Label(cb.transform, "xtext", "×", 0f, 0f, 36f, 36f, 18,
                    new Color(1f, 1f, 1f, 0.9f));
                t.alignment = TextAnchor.MiddleCenter;
                t.rectTransform.anchorMin = Vector2.zero;
                t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = Vector2.zero;
                t.rectTransform.offsetMax = Vector2.zero;
            }
            else
            {
                UI.Btn(_root, "close", "×", W - 48f, 10f, 32f, 30f, Skin.BtnColor, Skin.DimColor,
                    CloseIfOpen, 20, Skin.ButtonSprite);
            }

            // 标题栏拖动：透明图片盖住顶栏（避开右上角关闭按钮），拖它移动整个窗口
            var dragGo = UI.Node(_root, "dragArea");
            UI.Rect(dragGo, 0f, 0f, W - 60f, 52f);
            var dragImg = dragGo.gameObject.AddComponent<Image>();
            dragImg.sprite = UI.White;
            dragImg.color = new Color(0f, 0f, 0f, 0f);
            dragImg.raycastTarget = true;
            var drag = dragGo.gameObject.AddComponent<WindowDrag>();
            drag.target = _root;

            // 模式色条（原版 tap）：随工作模式变色的横条，紧贴标题栏下沿
            _tap = UI.Panel(_root, "tap", 18f, 52f, W - 36f, 4f, Skin.BtnOffColor);
            UI.Panel(_root, "line0", 18f, 58f, W - 36f, 1f, CLine);

            // --- 电网信息（对齐原版 UIPowerNetworkDesc：六项数据两列排布）---
            UI.Label(_root, "lblNet", "电网信息", 28f, 64f, 120f, 22f, 14, Skin.DimColor);
            _gridText = UI.Label(_root, "valGrid", "-", 116f, 64f, 500f, 22f, 14, Skin.TextColor);

            UI.Label(_root, "lblGen", "发电功率", 28f, 88f, 84f, 22f, 12, Skin.DimColor);
            _genText = UI.Label(_root, "valGen", "-", 116f, 88f, 200f, 22f, 13, Skin.TextColor);
            UI.Label(_root, "lblReq", "需求功率", 330f, 88f, 84f, 22f, 12, Skin.DimColor);
            _reqText = UI.Label(_root, "valReq", "-", 418f, 88f, 200f, 22f, 13, Skin.TextColor);

            UI.Label(_root, "lblSrv", "实际供电", 28f, 110f, 84f, 22f, 12, Skin.DimColor);
            _srvText = UI.Label(_root, "valSrv", "-", 116f, 110f, 200f, 22f, 13, Skin.TextColor);
            UI.Label(_root, "lblSto", "电网储量", 330f, 110f, 84f, 22f, 12, Skin.DimColor);
            _stoText = UI.Label(_root, "valSto", "-", 418f, 110f, 200f, 22f, 13, Skin.TextColor);

            UI.Label(_root, "lblBat", "电池充放", 28f, 132f, 84f, 22f, 12, Skin.DimColor);
            _batText = UI.Label(_root, "valBat", "-", 116f, 132f, 200f, 22f, 13, Skin.TextColor);
            UI.Label(_root, "lblExp", "输出功率", 330f, 132f, 84f, 22f, 12, Skin.DimColor);
            _expText = UI.Label(_root, "valExp", "-", 418f, 132f, 200f, 22f, 13, Skin.TextColor);

            UI.Panel(_root, "line1", 18f, 158f, W - 36f, 1f, CLine);

            // --- 能量池 ---
            UI.Label(_root, "lblPool", "能量池", 28f, 164f, 84f, 22f, 12, Skin.DimColor);
            UI.Panel(_root, "poolBg", 116f, 168f, 300f, 14f, CBar);
            // ⚠ 填充条必须和背景同宽（300）：Filled 模式的 fillAmount 是在矩形内部缩放的，
            //   两者宽度不一致时进度条会超出/缩在背景里（1.2.8 进度条消失的根因）。
            _poolFill = UI.Panel(_root, "poolFill", 116f, 168f, 300f, 14f, Skin.AccentColor,
                Skin.BarSprite, Image.Type.Filled);
            if (_poolFill.type == Image.Type.Filled)
            {
                _poolFill.fillMethod = Image.FillMethod.Horizontal;
                _poolFill.fillOrigin = 0;
            }
            // 1.10.0：这一格改成「当前储量 / 总容量 + 百分比」，玩家一眼能看到池子有多大、存了多少
            _poolText = UI.Label(_root, "poolVal", "-", 424f, 164f, 202f, 22f, 12, Skin.TextColor);

            // --- 充能 / 放能功率（对齐原版 powerTextValue1/2）---
            UI.Label(_root, "lblCharge", "充能功率", 28f, 186f, 84f, 22f, 12, Skin.DimColor);
            _chargeText = UI.Label(_root, "valCharge", "0", 116f, 186f, 200f, 22f, 13, Skin.TextColor);
            UI.Label(_root, "lblDis", "放能功率", 330f, 186f, 84f, 22f, 12, Skin.DimColor);
            _dischargeText = UI.Label(_root, "valDis", "0", 418f, 186f, 200f, 22f, 13, Skin.TextColor);

            UI.Panel(_root, "line2", 18f, 212f, W - 36f, 1f, CLine);

            // --- 模式 ---
            UI.Label(_root, "lblMode", "工作模式", 28f, 220f, 120f, 24f, 14, Skin.DimColor);
            _modeIdleBtn = ModeButton("mIdle", "关机", 116f, 214f, () => SetMode(HubMode.Idle), out _modeIdleLine, out _modeIdleText);
            // 1.20.0：新增「仅蓄电」——只当超大蓄电器，不传输也不消耗耗材
            _modeBatBtn = ModeButton("mBat", "仅蓄电", 234f, 214f, () => SetMode(HubMode.Battery), out _modeBatLine, out _modeBatText);
            _modeSendBtn = ModeButton("mSend", "发送能量", 352f, 214f, () => SetMode(HubMode.Send), out _modeSendLine, out _modeSendText);
            _modeRecvBtn = ModeButton("mRecv", "接收能量", 470f, 214f, () => SetMode(HubMode.Receive), out _modeRecvLine, out _modeRecvText);

            // --- 对接状态 ---
            UI.Label(_root, "lblLink", "对接状态", 28f, 256f, 120f, 22f, 14, Skin.DimColor);
            _linkText = UI.Label(_root, "valLink", "-", 116f, 256f, 500f, 22f, 14, Skin.TextColor);

            // --- 终端列表 ---
            UI.Panel(_root, "line3", 18f, 280f, W - 36f, 1f, CLine);
            _listTitle = UI.Label(_root, "lblList", "对接终端", 28f, 284f, 380f, 26f, 14, Skin.DimColor);
            _pageText = UI.Label(_root, "page", "-", 400f, 284f, 56f, 26f, 12, Skin.DimColor,
                TextAnchor.MiddleRight);
            _btnPrev = UI.Btn(_root, "pgPrev", "◀", 462f, 286f, 28f, 24f, Skin.BtnColor, Skin.TextColor,
                () => TurnPage(-1), 11, Skin.ButtonSprite);
            _btnNext = UI.Btn(_root, "pgNext", "▶", 494f, 286f, 28f, 24f, Skin.BtnColor, Skin.TextColor,
                () => TurnPage(1), 11, Skin.ButtonSprite);

            for (int i = 0; i < PerPage; i++)
                _rows.Add(BuildRow(i));

            // --- 速率与累计（同一行：速率居左，累计居右）---
            UI.Panel(_root, "line4", 18f, 510f, W - 36f, 1f, CLine);
            _rateText = UI.Label(_root, "rate", "传输速率  -", 20f, 514f, 320f, 22f, 13, Skin.TextColor);
            _totalText = UI.Label(_root, "total", "累计  -", 340f, 514f, 280f, 22f, 12, Skin.DimColor,
                TextAnchor.MiddleRight);

            // --- 消耗品卡槽（三格，仅发送能量时显示；接收/仅蓄电/关机收起）---
            _slotLine = UI.Panel(_root, "line5", 18f, 540f, W - 36f, 1f, CLine);
            _slotTitle = UI.Label(_root, "lblSlot", "消耗物品卡槽", 28f, 544f, 420f, 22f, 14, Skin.DimColor);
            _pendingText = UI.Label(_root, "pending", "-", 320f, 544f, 280f, 22f, 12, Skin.DimColor,
                TextAnchor.MiddleRight);

            // 接收端专用：显示发送端卡槽的存量（发送端每 0.2 秒同步一次快照）。
            // 1.21.2：位置从 548 下移到 568 —— 以前和标题行（544~568）压在一起，
            // 接收模式下「对端耗材」那行字正好糊在「消耗物品卡槽」标题上，
            // 看着就是「接收端却在显示发送端的耗材卡槽」。
            _peerStockText = UI.Label(_root, "peerStock", "", 28f, 568f, 592f, 24f, 13, Skin.TextColor);
            _peerHintText = UI.Label(_root, "peerHint", "", 34f, 596f, 566f, 44f, 12, Skin.WarnColor);
            _peerHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _peerHintText.verticalOverflow = VerticalWrapMode.Overflow;
            _peerHintText.alignment = TextAnchor.UpperLeft;

            // 1.21.2：非发送模式（接收 / 仅蓄电 / 关机）显示的「塔内存料」一行。
            // 以前这三格在接收模式也照常画出来、标一句「发送时消耗」，
            // 玩家会以为接收端也得往里放料 —— 实际上接收端一个耗材都不吃。
            _ownStockText = UI.Label(_root, "ownStock", "", 28f, 570f, 588f, 40f, 12, Skin.DimColor);
            _ownStockText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _ownStockText.verticalOverflow = VerticalWrapMode.Overflow;
            _ownStockText.alignment = TextAnchor.UpperLeft;

            _cells.Add(BuildCell(Items.InformationMatrix, false, "同星系", 566f));
            _cells.Add(BuildCell(ItemProto.kWarperId, false, "跨星系", 594f));
            _cells.Add(BuildCell(Items.PrimaryLens, true, "跨星系", 622f));
            // 1.20.0：取消「提速」格（增产剂 Mk.III），传输速率只由能量池等级决定

            // --- 能量池状态（1.21.0 起只读）---
            // 1.20.x 这里是一整块升级交互（按钮 + 两种材料格 + 沙盒一键满级）。
            // 1.21.0 把升级搬到科技树「升级」页（可重复研究，只吃引力矩阵），
            // 面板只留一行状态：等级 / 容量 / 功率，以及「去哪里升」的提示 ——
            // 这样拆了重建也不会丢等级（等级是全局研究进度，不再是建筑状态）。
            _upLine = UI.Panel(_root, "line6", 18f, 654f, W - 36f, 1f, CLine);
            _upTitle = UI.Label(_root, "lblUp", "能量池", 28f, 658f, 64f, 24f, 14, Skin.DimColor);
            _levelText = UI.Label(_root, "level", "-", 96f, 658f, 512f, 24f, 12, Skin.TextColor);
            UI.Label(_root, "upTip", "升级：科技 → 升级页「能量池扩容」", 28f, 684f, 560f, 20f, 11,
                Skin.DimColor);

            _hintText = UI.Label(_root, "hint", "", 34f, 758f, 572f, 44f, 11, Skin.DimColor);
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hintText.verticalOverflow = VerticalWrapMode.Overflow;
            _hintText.alignment = TextAnchor.UpperLeft;

            gameObject.SetActive(false);
        }

        /// <summary>模式按钮：原版按钮底图 + 底部一条状态线（选中时亮）。</summary>
        private Button ModeButton(string name, string text, float x, float y, Action onClick, out Image line, out Text label)
        {
            Button b = UI.Btn(_root, name, text, x, y, 112f, 36f, Skin.BtnColor, Skin.TextColor,
                onClick, 14, Skin.ButtonSprite);
            line = UI.Panel(_root, name + "Line", x + 8f, y + 36f, 96f, 3f, new Color(0f, 0f, 0f, 0f));
            label = b.GetComponentInChildren<Text>();
            return b;
        }

        /// <summary>一行终端：底图 + 名称 + 状态 + 连接按钮。</summary>
        private Row BuildRow(int i)
        {
            float y = RowY0 + RowStep * i;
            var holder = UI.Node(_root, "row" + i);
            UI.Rect(holder, 20f, y, 600f, RowH);

            var row = new Row { holder = holder.gameObject };
            row.holder.SetActive(false);

            row.bg = UI.Panel(holder, "bg", 0f, 0f, 600f, RowH, CRow);
            row.name = UI.Label(holder, "name", "-", 8f, 0f, 300f, RowH, 14, Skin.TextColor);
            row.note = UI.Label(holder, "note", "", 312f, 0f, 158f, RowH, 12, Skin.DimColor);

            int idx = i;
            row.btn = UI.Btn(holder, "link", "连接", 476f, 3f, 110f, 30f, Skin.BtnColor, Skin.TextColor,
                () => OnRowClick(idx), 13, Skin.ButtonSprite);
            row.btnLabel = row.btn.GetComponentInChildren<Text>();
            row.btnImg = row.btn.GetComponent<Image>();
            return row;
        }

        /// <summary>一格消耗品：图标 + 名称 + 用途 + 数量 + 放入/取出。</summary>
        private Cell BuildCell(int itemId, bool isLens, string tag, float y)
        {
            var holder = UI.Node(_root, "cell-" + (isLens ? "lens" : itemId.ToString()));
            UI.Rect(holder, 20f, y, 600f, 28f);

            var cell = new Cell { itemId = itemId, isLens = isLens, holder = holder.gameObject, tagBase = tag };
            UI.Panel(holder, "bg", 0f, 0f, 600f, 28f, new Color(0.09f, 0.14f, 0.19f, 0.5f));
            cell.icon = UI.Icon(holder, "icon", 2f, 1f, 26f, Items.Icon(itemId));
            cell.name = UI.Label(holder, "name", isLens ? Items.LensLabel : Items.Name(itemId),
                36f, 0f, 150f, 28f, 13, Skin.TextColor);
            cell.tag = UI.Label(holder, "tag", tag, 190f, 0f, 100f, 28f, 11, Skin.DimColor);
            cell.count = UI.Label(holder, "count", "× 0", 292f, 0f, 120f, 28f, 13, Skin.TextColor);

            cell.btnAdd = UI.Btn(holder, "add", "放入 " + cell.addCount, 416f, 2f, 82f, 24f,
                Skin.BtnColor, Skin.TextColor, () => AddCell(cell, cell.addCount), 11, Skin.ButtonSprite);
            cell.btnAddImg = cell.btnAdd.GetComponent<Image>();
            cell.btnTake = UI.Btn(holder, "take", "取出", 504f, 2f, 66f, 24f, Skin.BtnColor, Skin.DimColor,
                () => TakeCell(cell), 11, Skin.ButtonSprite);
            return cell;
        }

        // ---------------- 交互 ----------------

        private HubInfo Info
        {
            get
            {
                if (_planetId < 0) return null;
                return HubManager.Get(_planetId, _entityId);
            }
        }

        private void SetMode(HubMode m)
        {
            var info = Info;
            if (info == null) return;
            info.mode = (int)m;
            // 离开「接收」就不再是客户端，连接关系由自己持有，顺手清掉
            if (m != HubMode.Receive && info.peerPlanetId >= 0)
                HubManager.Disconnect(info);
            _page = 0;
            // 联机：客户端改完把这一台的状态上报给主机（单机 / 主机端是空操作）
            HubManager.ReportState(info);
            Refresh(true);
        }

        private void TurnPage(int dir)
        {
            int pages = Mathf.Max(1, (_peers.Count + PerPage - 1) / PerPage);
            _page = (_page + dir + pages) % pages;
            Refresh(true);
        }

        private void OnRowClick(int i)
        {
            var info = Info;
            if (info == null) return;
            int index = _page * PerPage + i;
            if (index < 0 || index >= _peers.Count) return;

            PeerRow p = _peers[index];
            if (!p.selectable)
            {
                UIRealtimeTip.Popup("该终端是对方连过来的（或不可连接），不能在这里再点一次");
                return;
            }

            Plugin.Log.LogInfo("[连接点击] 本机 #" + info.entityId + "@星球" + info.planetId +
                               "（mode=" + info.mode + "）→ 终端 #" + p.entityId + "@星球" + p.planetId +
                               " 对端mode=" + HubManager.PeekMode(p.planetId, p.entityId));

            if (p.connected)
            {
                HubManager.Disconnect(info);
                UIRealtimeTip.Popup("已断开与终端 #" + p.entityId + " 的连接");
            }
            else if (HubManager.Connect(info, p.planetId, p.entityId))
            {
                UIRealtimeTip.Popup("已连接发送终端 #" + p.entityId + "（" + p.planetName +
                                    (p.crossStar ? "，跨星系" : "，同星系") + "）");
            }
            else
            {
                Plugin.Log.LogInfo("[连接点击] 连接失败 → 终端 #" + p.entityId + "@星球" + p.planetId +
                                   "（对端已不是「发送能量」状态，mode=" + HubManager.PeekMode(p.planetId, p.entityId) + "）");
                UIRealtimeTip.Popup("连接失败：该终端已不是「发送能量」状态");
            }
            HubManager.ReportState(info);
            Refresh(true);
        }

        // ---------------- 卡槽交互 ----------------

        private bool TryGetHubEntity(out PlanetFactory factory, out EntityData ed)
        {
            factory = GameMain.localPlanet != null ? GameMain.localPlanet.factory : null;
            ed = default(EntityData);
            if (factory == null || factory.entityPool == null || _entityId >= factory.entityPool.Length)
                return false;
            ed = factory.entityPool[_entityId];
            return ed.id == _entityId;
        }

        private void AddCell(Cell cell, int n)
        {
            PlanetFactory factory;
            EntityData ed;
            if (!TryGetHubEntity(out factory, out ed))
                return;

            // 卡槽有条件才允许放：
            //   接收 → 不需要消耗品；关机 → 不能放；链路类型对不上 → 不能放；还没链路 → 可以放（先备料）。
            HubInfo info = Info;
            string label = cell.isLens ? Items.LensLabel : Items.Name(cell.itemId);
            HubMode hm = info != null ? (HubMode)info.mode : HubMode.Idle;
            if (info != null && !CellActive(info, cell, hm))
            {
                string why = hm == HubMode.Receive ? "接收端不消耗任何耗材（矩阵 / 翘曲器 / 透镜都由发送端出）"
                    : hm == HubMode.Battery ? "仅蓄电模式不消耗耗材"
                    : hm == HubMode.Idle ? "关机时不消耗物品"
                    : null;
                UIRealtimeTip.Popup(why != null
                    ? why + "：先把模式切到「发送能量」再放料"
                    : "当前链路用不到" + label +
                      (cell.itemId == Items.InformationMatrix ? "（同星系链路才消耗）" : "（跨星系链路才消耗）"));
                return;
            }

            int want = Math.Max(n, HubManager.BagCount(cell.itemId));

            int added = cell.isLens
                ? HubManager.AddLens(factory, ed.storageId, n)
                : HubManager.AddItem(factory, ed.storageId, cell.itemId, want);

            if (added > 0)
            {
                // 联机：把这一笔变动报给主机（主机按增量往它的卡槽里补/扣）
                HubManager.NoteSlotDelta(info, cell.itemId, cell.isLens, added);
                UIRealtimeTip.Popup("已放入" + label + " ×" + added);
            }
            else
            {
                int bag = HubManager.BagCount(cell.itemId);
                UIRealtimeTip.Popup(bag > 0
                    ? "卡槽放不下" + label + "（卡槽已满）"
                    : "背包里没有" + label);
                // 日志把「背包数量 / 卡槽余量」都写出来，一眼能分清是背包没有还是卡槽满了。
                Plugin.Log.LogInfo("[放入失败] " + label +
                                   (cell.isLens
                                       ? " | " + HubManager.LensDebug(factory, ed.storageId)
                                       : " itemId=" + cell.itemId +
                                         " 背包=" + bag +
                                         " 卡槽余量=" + HubManager.SlotRoom(factory, ed.storageId, cell.itemId)) +
                                   " | " + Items.Summary);
            }
            HubManager.ReportState(info);
            Refresh(true);
        }

        private void TakeCell(Cell cell)
        {
            PlanetFactory factory;
            EntityData ed;
            if (!TryGetHubEntity(out factory, out ed))
                return;

            HubInfo info = Info;
            string label = cell.isLens ? Items.LensLabel : Items.Name(cell.itemId);

            int got = cell.isLens
                ? HubManager.TakeLens(factory, ed.storageId)
                : HubManager.TakeItem(factory, ed.storageId, cell.itemId);

            if (got > 0 && info != null)
                HubManager.NoteSlotDelta(info, cell.itemId, cell.isLens, -got);

            UIRealtimeTip.Popup(got > 0 ? "已取回" + label + " ×" + got : "卡槽里没有这种物品");
            if (info != null)
                HubManager.ReportState(info);
            Refresh(true);
        }

        // ---------------- 刷新 ----------------

        /// <summary>
        /// 玩家是否打开了背包。
        /// 只有背包会触发自动关窗——星图 / 科技树 / 戴森球编辑器 / 建造面板等
        /// 打开时本窗口保持原样，不跟着一起收起来（那些界面玩家可能只是瞄一眼）。
        /// </summary>
        private static bool InventoryOpen()
        {
            UIRoot root = UIRoot.instance;
            UIGame g = root != null ? root.uiGame : null;
            if (g == null || g.inventoryWindow == null)
                return false;
            if (Time.frameCount <= _invGraceFrame)
                return false;
            try
            {
                return g.inventoryWindow.active;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 玩家是否打开了科技树（含「升级」页 —— 能量池扩容就在那一页）。
        ///
        /// 1.21.3：科技界面和本窗口会抢同一块屏幕，而且玩家点「能量池扩容」时
        /// 本窗口还盖在下面、挡住科技树，所以科技界面一打开就把本窗口收起来。
        /// </summary>
        private static bool TechTreeOpen()
        {
            UIRoot root = UIRoot.instance;
            UIGame g = root != null ? root.uiGame : null;
            if (g == null || g.techTree == null)
                return false;
            if (Time.frameCount <= _invGraceFrame)
                return false;
            try
            {
                return g.techTree.active;
            }
            catch { }
            return false;
        }

        private void Update()
        {
            if (!gameObject.activeSelf) return;

            // 退到主菜单：DSP 只销毁 GameMain、UIRoot 是复用的，
            // 本窗口挂在 overlayCanvas 下不会跟着消失 —— 不主动收起来就会残留成
            // 主菜单上的一个「星际能量枢纽塔」面板。加载新存档的瞬间同样适用。
            if (GameMain.data == null || GameMain.localPlanet == null)
            {
                CloseIfOpen();
                return;
            }

            // 玩家按 E 打开背包、或打开科技树时自动关闭本窗口（其它界面不关）
            if (InventoryOpen() || TechTreeOpen())
            {
                CloseIfOpen();
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.15f;
                Refresh(false);
            }
        }

        private void Refresh(bool force)
        {
            var info = Info;
            if (info == null)
            {
                _hintText.text = "该枢纽数据尚未初始化，请稍候或重新放置。";
                return;
            }

            var factory = GameMain.localPlanet != null ? GameMain.localPlanet.factory : null;
            var ps = factory != null ? factory.powerSystem : null;
            if (ps == null) return;

            // 建筑被拆掉 / 换星球时自动关窗，避免读到无效数据
            if (factory.entityPool == null || _entityId >= factory.entityPool.Length)
            {
                CloseIfOpen();
                return;
            }
            EntityData ed = factory.entityPool[_entityId];
            if (ed.id != _entityId || ed.powerExcId <= 0 || ed.powerExcId >= ps.excPool.Length)
            {
                CloseIfOpen();
                return;
            }
            PowerExchangerComponent exc = ps.excPool[ed.powerExcId];
            bool powered = exc.networkId > 0;
            if (_netTag != null)
                _netTag.text = MP.StatusText;

            // 电网
            _gridText.text = powered
                ? "已接入电网  #" + exc.networkId
                : "未接入电网（需连接到电力网络）";
            _gridText.color = powered ? Skin.TextColor : Skin.WarnColor;

            // 能量池
            // 1.19.7：联机客户端本地也跑电网结算（为了让本地电网知道这台机器在充/放电），
            // exc.currPoolEnergy 可能被原版改掉一两个 tick 的量。显示一律用主机广播的值。
            long showPool = MP.Authoritative
                ? exc.currPoolEnergy
                : Math.Min(info.cachedPool, Math.Max(exc.maxPoolEnergy, 0L));
            float ratio = exc.maxPoolEnergy > 0 ? (float)showPool / exc.maxPoolEnergy : 0f;
            ratio = Mathf.Clamp01(ratio);
            if (_poolFill.type == Image.Type.Filled)
                _poolFill.fillAmount = ratio;
            else
                _poolFill.rectTransform.sizeDelta = new Vector2(300f * ratio, 14f);
            _poolFill.color = Color.Lerp(Skin.WarnColor, Skin.AccentColor, ratio);
            // 当前储量 / 总容量（1.10.0）：池子容量随等级从 100 GJ 涨到 1 PJ，
            // 只显示百分比看不出「存了多少、还能存多少」，所以两个数一起给。
            _poolText.text = UI.FmtEnergy(showPool) + " / " + UI.FmtEnergy(exc.maxPoolEnergy) +
                             "  " + Mathf.RoundToInt(ratio * 100f) + "%";

            // 充能 / 放能功率（原版 powerTextValue1/2 的做法：per-tick × 60 = 每秒瓦特）
            long perTick = MP.Authoritative ? exc.currEnergyPerTick : info.netExcPerTick;
            _chargeText.text = perTick > 0 ? UI.FmtPower(perTick * 60L) : "0";
            _dischargeText.text = perTick < 0 ? UI.FmtPower(-perTick * 60L) : "0";
            Color pc = powered && perTick != 0 ? Skin.OkColor : Skin.WarnColor;
            _chargeText.color = pc;
            _dischargeText.color = pc;

            // 电网信息（对齐原版 UIPowerNetworkDesc._OnUpdate 的数据源）
            if (powered && exc.networkId > 0 && exc.networkId < ps.netPool.Length)
            {
                PowerNetwork net = ps.netPool[exc.networkId];
                long battery = net.energyAccumulated + net.energyExchanged;
                long export = net.energyExport > 0L ? net.energyExport : 0L;
                _genText.text = UI.FmtPower(net.energyCapacity * 60L);
                _reqText.text = UI.FmtPower(net.energyRequired * 60L);
                _srvText.text = UI.FmtPower((net.energyServed + battery) * 60L);
                _stoText.text = UI.FmtEnergy(net.energyStored);
                _batText.text = (battery >= 0L ? "充电 " : "放电 ") + UI.FmtPower(Math.Abs(battery) * 60L);
                _batText.color = battery >= 0L ? Skin.OkColor : Skin.WarnColor;
                _expText.text = export > 0L ? UI.FmtPower(export * 60L) : "-";
                // 1.8.1：电网空闲量（发电容量 − 用电需求），给蓄电器状态行用
                _gridIdle = net.energyCapacity - net.energyRequired;
                _gridIdleValid = true;
            }
            else
            {
                _genText.text = "-"; _reqText.text = "-"; _srvText.text = "-";
                _stoText.text = "-"; _batText.text = "-"; _expText.text = "-";
                _batText.color = Skin.TextColor;
                _gridIdleValid = false;
            }

            // 模式按钮 / 状态条：全部取原版能量枢纽的三档模式色（1.21.2）。
            // 原版窗口顶部那条 tap 就是"当前模式下的一种固定颜色"，我们照抄这套映射：
            //   关机 = marqueeOff 灰、仅蓄电 = mode1、发送 = mode2、接收 = mode3。
            var mode = (HubMode)info.mode;
            if (_tap != null)
                _tap.color = mode == HubMode.Idle ? Skin.ModeOffColor
                    : mode == HubMode.Send ? Skin.Mode2Color
                    : mode == HubMode.Receive ? Skin.Mode3Color
                    : Skin.Mode1Color;
            SetModeVisual(mode == HubMode.Idle, _modeIdleLine, _modeIdleText, Skin.ModeOffColor);
            SetModeVisual(mode == HubMode.Battery, _modeBatLine, _modeBatText, Skin.Mode1Color);
            SetModeVisual(mode == HubMode.Send, _modeSendLine, _modeSendText, Skin.Mode2Color);
            SetModeVisual(mode == HubMode.Receive, _modeRecvLine, _modeRecvText, Skin.Mode3Color);
            if (Skin.ButtonSprite != null)
            {
                SetModeBg(_modeIdleBtn, mode == HubMode.Idle, Skin.ModeOffColor);
                SetModeBg(_modeBatBtn, mode == HubMode.Battery, Skin.Mode1Color);
                SetModeBg(_modeSendBtn, mode == HubMode.Send, Skin.Mode2Color);
                SetModeBg(_modeRecvBtn, mode == HubMode.Receive, Skin.Mode3Color);
            }

            RefreshLinkText(info, mode);
            RefreshPeerStock(info, mode);

            // 速率
            switch (mode)
            {
                case HubMode.Send:
                    if (!info.linked)
                    {
                        _rateText.text = "传输速率  0 W    （无接收端，仅蓄电器）";
                        _rateText.color = Skin.WarnColor;
                    }
                    else if (info.fxTarget < 0.5f)
                    {
                        // 1.3.8：本星球电网抽不到电（发电被工厂吃光 / 电池也空），
                        // 池子会一直是 0，这时候必须明说，否则玩家只看到「连着却不动」。
                        _rateText.text = "传输速率  0 W    ⚠ " +
                            (info.fxReason != null && info.fxReason.Contains("接入") ? "未接入电网" : "电网无空闲能量");
                        _rateText.color = Skin.WarnColor;
                    }
                    else
                    {
                        // 消耗品是真条件了：缺料时要么按料限速、要么直接停，
                        // 两种状态要分开写，否则玩家看到「0 W + 降速」会以为是 bug。
                        // 文案里带上缺的具体是哪种料（矩阵 / 翘曲器+透镜），玩家才知道补什么。
                        string need = info.starveNeed ?? "耗材";
                        _rateText.text = info.itemStarved
                            ? (info.ratePerSecond < 1f
                                ? "传输速率  0 W    ⚠ 缺" + need + "·已停机"
                                : "传输速率  " + UI.FmtPower(info.ratePerSecond) + "  ⚠ 缺" + need + "·限速")
                            : "传输速率  " + UI.FmtPower(info.ratePerSecond) + "    →  " + info.receiverCount + " 条链路";
                        _rateText.color = info.itemStarved ? Skin.WarnColor : Skin.OkColor;
                    }
                    break;
                case HubMode.Receive:
                    _rateText.text = "接收速率  " + UI.FmtPower(info.ratePerSecond) + "    →  本地电网";
                    _rateText.color = info.linked ? Skin.OkColor : Skin.WarnColor;
                    break;
                case HubMode.Battery:
                    // 仅蓄电：没有传输速率这回事，直接显示当前充 / 放功率
                    if (info.storageDir > 0)
                    {
                        _rateText.text = "仅蓄电  正在充电  " +
                                         UI.FmtPower((_gridIdleValid ? _gridIdle : 0L) * 60L) + "  →  池子";
                        _rateText.color = Skin.OkColor;
                    }
                    else if (info.storageDir < 0)
                    {
                        _rateText.text = "仅蓄电  正在放电  池子  →  本地电网";
                        _rateText.color = Skin.OkColor;
                    }
                    else
                    {
                        _rateText.text = "仅蓄电  待机（电网没有余电 / 池子已满）";
                        _rateText.color = Skin.DimColor;
                    }
                    break;
                default:
                    _rateText.text = "传输速率  0 W    （关机）";
                    _rateText.color = Skin.DimColor;
                    break;
            }

            _totalText.text = "累计发送  " + UI.FmtEnergy(info.totalSent) +
                              "        累计接收  " + UI.FmtEnergy(info.totalReceived);

            // 终端列表先刷新，卡槽高亮要用到「链路是跨星系还是同星系」
            RefreshPeers(info, mode);
            RefreshCells(factory, ed, info, mode);
            RefreshUpgrade(factory, ed, info);

            // 账本：只看这台接收端自己的在途量（按接收端记账，别家连的不算我的）
            if (mode == HubMode.Send)
            {
                // 发送端这一行改显示扣料速率 —— 1.21.4 起扣料按能量算，
                // 玩家能直接看到「多少个耗材换 1 GJ」「本秒扣了几个」。
                long per = C.EnergyPerItemAt(info.level);
                _pendingText.text = "消耗 " + (per / 1_000_000_000.0).ToString("0.0") +
                                    " GJ/个 · 本秒 -" + info.chargedLastSec;
                _pendingText.color = info.chargedLastSec > 0 ? Skin.AccentColor : Skin.DimColor;
            }
            else
            {
                long pend = HubManager.GetPending(_planetId, _entityId);
                _pendingText.text = "本机待接收  " + UI.FmtEnergy(pend);
                _pendingText.color = pend > 0 ? Skin.AccentColor : Skin.DimColor;
            }

            if (force || mode != HubMode.Send)
            {
                // 底部提示只有 44 px 高（约 2 行），文案必须控制在两行以内，否则会画出窗口底边。
                _hintText.text = mode == HubMode.Idle
                    ? "关机：池子不参与电网充放，也不参与星际传输（保持当前电量）。"
                    : mode == HubMode.Battery
                        ? "仅蓄电：只当超大蓄电器用 —— 电网有余电就充、缺电就放，不传输也不消耗耗材。"
                        : mode == HubMode.Send
                            ? "发送：抽电网的电存池，接收端「连接」后外送；速率由能量池等级决定。"
                            : "接收：在列表点「连接」选一台发送终端；跨星系链路的翘曲消耗由发送端提供。";
                _hintText.text += info.level >= C.MaxPoolLevel
                    ? "\n能量池已满级（" + C.MaxPoolLevel + " 级）。"
                    : "\n能量池升级在「科技 → 升级」页研究（" +
                      C.PoolTechCostOf(info.level) + " 引力矩阵升到 " + (info.level + 1) + " 级）。";
            }

        }

        private void RefreshCells(PlanetFactory factory, EntityData ed, HubInfo info, HubMode mode)
        {
            bool sendMode = mode == HubMode.Send;
            bool idle = mode == HubMode.Idle;
            bool recvMode = mode == HubMode.Receive;

            // 1.21.2：耗材卡槽是「发送能量」专属。
            //
            // 以前（1.20.1）为了「传送带送进来的料能在面板上看到」，把这三种模式都改成
            // 照常画三格、标一句「发送时消耗」；结果接收端面板上摆着「消耗物品卡槽 +
            // 信息矩阵 / 空间翘曲器 / 引力透镜」三格，看起来就像接收端也得放料 ——
            // 实际上接收端一个耗材都不吃（矩阵 / 翘曲器 / 透镜全部由发送端出）。
            //
            // 现在：发送模式画三格；接收模式只显示「对端耗材」；仅蓄电 / 关机显示一行库存文本。
            SetCellsVisible(sendMode);

            if (_slotTitle != null)
            {
                _slotTitle.text = sendMode ? "消耗物品卡槽（每格 " + C.SlotStackMax + " 个）"
                    : recvMode ? "对端耗材（由发送端提供，本端不消耗）"
                    : "塔内存料（本模式不消耗耗材）";
                _slotTitle.color = Skin.DimColor;
            }

            // 「本机待接收」是接收端的在途账本；发送端这一行改显示扣料速率
            bool showPending = recvMode || sendMode;
            if (_pendingText != null && _pendingText.gameObject.activeSelf != showPending)
                _pendingText.gameObject.SetActive(showPending);

            // 非发送、非接收（仅蓄电 / 关机）：一行纯文本库存，替代原来的三格
            bool showOwn = !sendMode && !recvMode;
            if (_ownStockText != null && _ownStockText.gameObject.activeSelf != showOwn)
                _ownStockText.gameObject.SetActive(showOwn);
            if (showOwn && _ownStockText != null)
            {
                int m = HubManager.CountItem(factory, ed.storageId, Items.InformationMatrix);
                int w = HubManager.CountItem(factory, ed.storageId, ItemProto.kWarperId);
                int l = HubManager.CountLens(factory, ed.storageId);
                _ownStockText.text = "塔内存料  信息矩阵 × " + m + "    空间翘曲器 × " + w +
                                     "    引力透镜 × " + l +
                                     (idle
                                         ? "\n关机不消耗任何物品；切到「发送能量」才能取放耗材。"
                                         : "\n仅蓄电不消耗任何物品；切到「发送能量」才能取放耗材。");
                _ownStockText.color = Skin.DimColor;
            }

            if (!sendMode)
                return;                                  // 三格收起时不用刷它们

            bool sameStar = false, crossStar = false, hasLink = false;
            HubManager.LinkUsage(info, out sameStar, out crossStar, out hasLink);
            bool canStock = !hasLink;

            Color dim = new Color(Skin.DimColor.r, Skin.DimColor.g, Skin.DimColor.b, 0.6f);
            Color offBg = new Color(0.10f, 0.13f, 0.16f, 0.55f);
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell c = _cells[i];
                if (c.holder == null) continue;
                int n = c.isLens
                    ? HubManager.CountLens(factory, ed.storageId)
                    : HubManager.CountItem(factory, ed.storageId, c.itemId);

                string label = c.isLens ? Items.LensLabel : Items.Name(c.itemId);
                if (c.name.text != label) c.name.text = label;
                if (c.icon != null)
                {
                    int showId = c.isLens ? Items.PrimaryLens : c.itemId;
                    Sprite sp = Items.Icon(showId);
                    if (sp != null && c.icon.sprite != sp) c.icon.sprite = sp;
                }

                bool match;
                if (c.isLens) match = crossStar;
                else if (c.itemId == Items.InformationMatrix) match = sameStar;
                else match = crossStar;                   // 空间翘曲器

                bool active = match || canStock;

                // 标签：类型匹配 → 原用途；还没链路 → 待连接；类型不符 → 未启用
                c.tag.text = match ? c.tagBase : (canStock ? "待连接" : "未启用");
                c.tag.color = match ? Skin.AccentColor : dim;
                c.name.color = active ? Skin.TextColor : dim;
                c.count.text = n + " / " + C.SlotStackMax;
                c.count.color = n > 0 ? (active ? Skin.TextColor : Skin.DimColor) : (active ? Skin.WarnColor : dim);
                c.icon.color = active ? Color.white : new Color(1f, 1f, 1f, 0.35f);

                // 「放入」只在发送模式可用；「取出」永远可用，否则玩家断链 / 改模式后取不回卡槽里的东西
                if (c.btnAdd != null) c.btnAdd.interactable = active;
                if (c.btnAddImg != null)
                    c.btnAddImg.color = active ? Skin.BtnColor : offBg;
            }
        }

        /// <summary>
        /// 刷新能量池状态区（1.21.0 起只读）：等级 / 容量 / 充放功率 / 下一次研究要多少引力矩阵。
        ///
        /// 升级本身在科技树「升级」页（<see cref="PoolTech"/>），面板这边只负责显示，
        /// 所以这里不再有任何按钮可用性判断 —— 点了也没东西可点。
        /// </summary>
        private void RefreshUpgrade(PlanetFactory factory, EntityData ed, HubInfo info)
        {
            if (_levelText == null || info == null)
                return;

            int lv = Mathf.Clamp(info.level, 1, C.MaxPoolLevel);
            bool max = lv >= C.MaxPoolLevel;

            _levelText.text = lv + " / " + C.MaxPoolLevel + " 级    容量 " + UI.FmtEnergy(info.Capacity) +
                              "    充放 " + UI.FmtPower(C.ExchangePerTickOf(lv) * 60L) +
                              (max
                                  ? "    （已满级）"
                                  : "    （下一级 " + C.PoolTechCostOf(lv) + " 引力矩阵）");
            _levelText.color = max ? Skin.OkColor : Skin.TextColor;
        }

        /// <summary>进度百分比（0~100，向上取整，避免 99% 看起来像卡住）。</summary>
        private static string Pct(long have, int need)
        {
            if (need <= 0) return "";
            int p = (int)((have * 100L + need - 1) / need);
            return p > 100 ? "100%" : p + "%";
        }

        /// <summary>
        /// 只显示/收起「三格耗材卡槽」。
        /// 1.21.2：分隔线、标题、库存文本由 <see cref="RefreshCells"/> 按模式单独控制，
        /// 这三格本身只有发送能量模式才出现。
        /// </summary>
        private void SetCellsVisible(bool on)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell c = _cells[i];
                if (c == null || c.holder == null)
                    continue;
                if (c.holder.activeSelf != on)
                    c.holder.SetActive(on);
            }
        }

        /// <summary>
        /// 这一格当前能不能往卡槽里放东西。
        /// 1.21.2：耗材卡槽只属于发送端 —— 接收端一个耗材都不吃（全由发送端出），
        /// 仅蓄电 / 关机也不消耗，所以这三种模式一律拒绝放料。
        /// 发送端：按链路类型决定，还没链路时允许先备料。
        /// </summary>
        private static bool CellActive(HubInfo info, Cell c, HubMode mode)
        {
            if (mode != HubMode.Send)
                return false;

            bool sameStar, crossStar, hasLink;
            HubManager.LinkUsage(info, out sameStar, out crossStar, out hasLink);
            bool match;
            if (c.isLens) match = crossStar;
            else if (c.itemId == Items.InformationMatrix) match = sameStar;
            else match = crossStar;
            return match || !hasLink;
        }

        private void RefreshLinkText(HubInfo info, HubMode mode)
        {
            switch (mode)
            {
                case HubMode.Send:
                    if (info.linked)
                    {
                        _linkText.text = "已对接  " + info.receiverCount + " 台接收端，按需自动分配" +
                                         BatteryNote(info);
                        _linkText.color = Skin.OkColor;
                    }
                    else
                    {
                        _linkText.text = "等待接收端连接（对方在其列表点「连接」）" + BatteryNote(info);
                        _linkText.color = Skin.WarnColor;
                    }
                    break;

                case HubMode.Receive:
                    if (info.linked)
                    {
                        _linkText.text = (info.peerUnloaded
                            ? "已对接终端 #" + info.peerEntityId + "（对端星球未加载）"
                            : "已对接终端 #" + info.peerEntityId +
                              (info.restored ? "（存档恢复）" : "") + "，正在接收") + BatteryNote(info);
                        _linkText.color = Skin.OkColor;
                    }
                    else if (info.peerEntityId > 0 && info.peerPlanetId >= 0)
                    {
                        _linkText.text = info.peerValid
                            ? "已选终端 #" + info.peerEntityId + "，本机未接电网，待生效"
                            : "已选终端 #" + info.peerEntityId + "，对方未在发送状态";
                        _linkText.color = Skin.WarnColor;
                    }
                    else
                    {
                        _linkText.text = "未连接（在下方列表点「连接」）" + BatteryNote(info);
                        _linkText.color = Skin.WarnColor;
                    }
                    break;

                case HubMode.Battery:
                    _linkText.text = "仅蓄电，不参与对接" +
                                     (info.storageDir > 0 ? "    ｜池子正在充电" :
                                      info.storageDir < 0 ? "    ｜池子正在放电" : "");
                    _linkText.color = info.storageDir != 0 ? Skin.OkColor : Skin.DimColor;
                    break;

                default:
                    _linkText.text = "关机，不参与对接";
                    _linkText.color = Skin.DimColor;
                    break;
            }
        }

        /// <summary>
        /// 接收端专用：把「发送端卡槽」的存货画出来（1.19.3）。
        ///
        /// 卡槽在发送端身上，接收端的窗口里本来是看不到的 —— 玩家只看到「已对接，但 0 W」，
        /// 分不清是对方没料、自己没通电、还是电网满。发送端每 0.2 秒把存量写进接收端的
        /// <see cref="HubInfo"/>（见 <c>HubManager.PublishStock</c>），这里负责展示：
        ///   · 有货 → 绿字「发送端卡槽  信息矩阵 × 12（同星系链路）」；
        ///   · 没货 → 红字 + 一句「请让发送端放入「信息矩阵」，才可以使用能量传输系统」；
        ///   · 快照过期（发送端所在星球没加载 / 对端没在发送）→ 灰字「未同步」。
        /// </summary>
        private void RefreshPeerStock(HubInfo info, HubMode mode)
        {
            // 1.21.2：接收模式下这块**始终**显示 —— 没连上时也要说明
            // 「接收端不用放料，耗材由发送端出」，否则玩家看到一块空白只会更困惑。
            bool show = mode == HubMode.Receive && info != null;

            if (_peerStockText != null && _peerStockText.gameObject.activeSelf != show)
                _peerStockText.gameObject.SetActive(show);
            if (_peerHintText != null && _peerHintText.gameObject.activeSelf != show)
                _peerHintText.gameObject.SetActive(show);
            if (!show)
                return;

            if (info.peerPlanetId < 0 || info.peerEntityId <= 0)
            {
                _peerStockText.text = "尚未连接发送端 —— 在本窗口列表里点「连接」即可";
                _peerStockText.color = Skin.DimColor;
                _peerHintText.text = "接收端不消耗任何耗材（信息矩阵 / 空间翘曲器 / 引力透镜都由发送端提供）";
                _peerHintText.color = Skin.DimColor;
                return;
            }

            bool sameStar, crossStar;
            HubManager.LinkUsage(info, out sameStar, out crossStar);
            bool cross = crossStar;

            bool fresh = info.peerStockStamp > 0f && Time.time - info.peerStockStamp <= 6f;
            if (!fresh)
            {
                _peerStockText.text = "发送端卡槽  未同步（发送端所在星球未加载，或对方没在发送状态）";
                _peerStockText.color = Skin.DimColor;
                _peerHintText.text = "接收端不消耗任何耗材（信息矩阵 / 空间翘曲器 / 引力透镜都由发送端提供）";
                _peerHintText.color = Skin.DimColor;
                return;
            }

            if (cross)
            {
                int w = info.peerStockWarper;
                int l = info.peerStockLens;
                bool ok = w > 0 && l > 0;
                _peerStockText.text = "发送端卡槽  空间翘曲器 × " + w + "    引力透镜 × " + l + "（跨星系链路）";
                _peerStockText.color = ok ? Skin.OkColor : Skin.WarnColor;
                _peerHintText.text = ok
                    ? "接收端不消耗任何耗材（这些料由发送端出）"
                    : "⚠ 请让发送端放入「空间翘曲器 + 引力透镜」，才可以使用能量传输系统";
            }
            else
            {
                int m = info.peerStockMatrix;
                _peerStockText.text = "发送端卡槽  信息矩阵 × " + m + "（同星系链路）";
                _peerStockText.color = m > 0 ? Skin.OkColor : Skin.WarnColor;
                _peerHintText.text = m > 0
                    ? "接收端不消耗任何耗材（这个矩阵由发送端出）"
                    : "⚠ 请让发送端放入「信息矩阵」，才可以使用能量传输系统";
            }
            _peerHintText.color = _peerHintText.text.StartsWith("⚠") ? Skin.WarnColor : Skin.DimColor;
        }

        /// <summary>
        /// 对接状态行尾巴上的蓄电器状态（1.7.1：储能并入发送/接收，不再单独占一行）。
        /// info.storageDir：1 = 正在吸收电网余电，-1 = 正在向电网放电。
        /// 1.8.2：充电功率 = 电网空闲量，所以充电时显示的功率本身就是空闲量；
        /// 文案必须短——状态行整行只有 500 px，超长会画出窗口边框。
        /// </summary>
        private string BatteryNote(HubInfo info)
        {
            if (info.storageDir > 0)
                return "    ｜蓄电器：吸收余电 " + (_gridIdleValid ? UI.FmtPower(_gridIdle * 60L) : "-");
            if (info.storageDir < 0)
                return "    ｜蓄电器：放电中" +
                       (_gridIdleValid && _gridIdle < 0 ? "（缺口 " + UI.FmtPower(-_gridIdle * 60L) + "）" : "");
            return "";
        }

        // 电网空闲量（发电容量 − 用电需求），Refresh 里随电网块一起更新
        private long _gridIdle;
        private bool _gridIdleValid;

        private void RefreshPeers(HubInfo info, HubMode mode)
        {
            HubManager.FillPeers(info, _peers);

            int pages = Mathf.Max(1, (_peers.Count + PerPage - 1) / PerPage);
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            bool many = _peers.Count > PerPage;
            _btnPrev.gameObject.SetActive(many);
            _btnNext.gameObject.SetActive(many);
            _pageText.gameObject.SetActive(many);
            _pageText.text = many ? (_page + 1) + "/" + pages : "";

            switch (mode)
            {
                case HubMode.Send:
                    _listTitle.text = _peers.Count > 0
                        ? "已连接的接收端（共 " + _peers.Count + " 台）"
                        : "已连接的接收端（还没有客户端连进来）";
                    break;
                case HubMode.Receive:
                    _listTitle.text = _peers.Count > 0
                        ? "可用发送终端（点「连接」建立链路）"
                        : "可用发送终端（银河内暂无处于「发送」状态的枢纽）";
                    break;
                case HubMode.Battery:
                    _listTitle.text = "对接终端（仅蓄电模式不参与对接）";
                    break;
                default:
                    _listTitle.text = "对接终端（先选择工作模式）";
                    break;
            }

            // 发送端行里没有「连接」按钮，状态列可以向右扩张，用来放「需求 / 实得」。
            float noteW = mode == HubMode.Send ? 262f : 158f;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                RectTransform noteRt = r.note.rectTransform;
                if (!Mathf.Approximately(noteRt.sizeDelta.x, noteW))
                    noteRt.sizeDelta = new Vector2(noteW, noteRt.sizeDelta.y);

                int index = _page * PerPage + i;
                if (index >= _peers.Count)
                {
                    r.holder.SetActive(false);
                    continue;
                }

                PeerRow p = _peers[index];
                r.holder.SetActive(true);

                string tag = p.samePlanet ? "本星球" : p.planetName;
                r.name.text = tag + "  ·  终端 #" + p.entityId;
                r.name.color = p.connected ? Skin.OkColor : Skin.TextColor;

                if (p.samePlanet)
                {
                    string state = p.online ? "在线" : "未通电";
                    string pool = p.peerPool >= 0 ? "  池 " + UI.FmtEnergy(p.peerPool) : "";
                    r.note.text = "同星球·同一电网" + "  " + state + pool;
                    r.note.color = p.online ? Skin.DimColor : Skin.WarnColor;
                }
                else if (mode == HubMode.Send)
                {
                    // 发送端：这一行是一台接收端，显示自动分配的结果。
                    // 「需求」是它上报的权重（近期能吸收多少），「得」是本帧实际分到的量。
                    string need = p.demandValid ? "需 " + UI.FmtEnergy(p.demand) : "需求未知";
                    r.note.text = (p.crossStar ? "跨星系" : "同星系") + " · " + need +
                                  " · 得 " + UI.FmtEnergy(p.granted);
                    r.note.color = p.granted > 0 ? Skin.OkColor : Skin.WarnColor;
                }
                else
                {
                    r.note.text = (p.crossStar ? "跨星系" : "同星系") + "  累计 " + UI.FmtEnergy(p.peerTotal);
                    r.note.color = p.crossStar ? Skin.WarnColor : Skin.DimColor;
                }

                if (r.bg != null)
                    r.bg.color = p.connected ? CRowOn : CRow;

                r.btn.gameObject.SetActive(p.selectable);
                if (p.selectable)
                {
                    r.btnLabel.text = p.connected ? "断开" : "连接";
                    r.btnLabel.color = Skin.TextColor;
                    r.btnImg.color = p.connected ? Skin.BtnOnColor : Skin.BtnColor;
                }
            }
        }

        private static void SetModeVisual(bool on, Image line, Text label, Color accent)
        {
            if (line != null)
                line.color = on ? accent : new Color(accent.r, accent.g, accent.b, 0f);
            if (label != null)
                label.color = on ? Color.white : Skin.TextColor * 0.85f;
        }

        /// <summary>
        /// 模式按钮底色：未选中 = 原版未选中灰；选中 = 该模式的指示色压暗。
        /// 1.21.2：以前四个按钮选中全用同一个 tap1 色，和原版「每种模式一条自己的颜色」不符。
        /// </summary>
        private static void SetModeBg(Button btn, bool on, Color accent)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img == null) return;
            img.color = on
                ? new Color(accent.r * 0.42f, accent.g * 0.42f, accent.b * 0.42f, 1f)
                : Skin.BtnOffColor;
        }
    }
}
