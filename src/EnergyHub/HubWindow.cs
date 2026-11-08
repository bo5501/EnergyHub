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
    /// 玩家打开背包时本窗口自动关闭（见 <see cref="InventoryOpen"/>）；星图/科技树等其它界面不会。
    /// </summary>
    public class HubWindow : MonoBehaviour
    {
        private const float W = 640f;
        // 1.10.0：消耗品卡槽多了一格「提速」（三级增产剂），窗口加高到 800；
        // 1.12.0：升级要两种材料，再加一个「引力矩阵」格，窗口加高到 836
        private const float H = 836f;
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
        /// <summary>能量池升级区（1.7.0）：分隔线 / 标题 / 等级容量文本 / 升级按钮 / 升级耗材格。</summary>
        private Image _upLine;
        private Text _upTitle;
        private Text _levelText;
        private Button _btnUpgrade;
        private Cell _upCell;
        /// <summary>第二种升级材料（引力矩阵）格。</summary>
        private Cell _upCell2;
        /// <summary>电网信息六项（对齐原版 UIPowerNetworkDesc）。</summary>
        private Text _genText, _reqText, _srvText, _stoText, _batText, _expText;
        /// <summary>充能 / 放能功率（对齐原版 powerTextValue1/2）。</summary>
        private Text _chargeText, _dischargeText;
        private Image _modeIdleLine, _modeSendLine, _modeRecvLine;
        private Text _modeIdleText, _modeSendText, _modeRecvText;
        private Button _modeIdleBtn, _modeSendBtn, _modeRecvBtn;
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
            /// <summary>「取出」按钮：升级格上是「把已投材料退回背包」，其余格子是清空卡槽。</summary>
            public Button btnTake;
            public GameObject holder;
            /// <summary>「放入」按钮一次放几个。</summary>
            public int addCount = 20;
            /// <summary>升级耗材格：不受「发送模式 / 链路类型」的限制，任何时候都能放。</summary>
            public bool isUpgrade;
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

        private static readonly Color CBar = new Color(0.10f, 0.16f, 0.21f, 1f);
        private static readonly Color CLine = new Color(0.20f, 0.34f, 0.44f, 0.9f);
        private static readonly Color CRow = new Color(0.09f, 0.14f, 0.19f, 0.55f);
        private static readonly Color CRowOn = new Color(0.14f, 0.30f, 0.38f, 0.85f);
        private static readonly Color COrange = new Color(1.00f, 0.65f, 0.28f);
        private static readonly Color CViolet = new Color(0.72f, 0.55f, 1.00f);

        // ---------------- 生命周期 ----------------

        public static bool IsOpen => _inst != null && _inst.gameObject.activeSelf;

        public static void OpenWindow(int entityId)
        {
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

            // 背景：原版能量枢纽窗口的九宫格底图
            UI.Panel(_root, "bg", 0f, 0f, W, H, Skin.PanelColor, Skin.PanelSprite, Skin.PanelType);
            UI.Panel(_root, "edge", 0f, 0f, W, 2f, Skin.AccentColor * 0.75f);

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
            _modeIdleBtn = ModeButton("mIdle", "待机", 116f, 214f, () => SetMode(HubMode.Idle), out _modeIdleLine, out _modeIdleText);
            _modeSendBtn = ModeButton("mSend", "发送能量", 234f, 214f, () => SetMode(HubMode.Send), out _modeSendLine, out _modeSendText);
            _modeRecvBtn = ModeButton("mRecv", "接收能量", 352f, 214f, () => SetMode(HubMode.Receive), out _modeRecvLine, out _modeRecvText);
            // 1.7.1：不再有独立的「储能」按钮——发送 / 接收模式下能量池自动充当蓄电器
            // （电网余电就充、缺电就放，见 HubManager.BatteryDir）。

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

            // --- 消耗品卡槽（三格，仅发送能量时显示；接收/待机收起）---
            _slotLine = UI.Panel(_root, "line5", 18f, 540f, W - 36f, 1f, CLine);
            _slotTitle = UI.Label(_root, "lblSlot", "消耗物品卡槽", 28f, 544f, 300f, 24f, 14, Skin.DimColor);
            _pendingText = UI.Label(_root, "pending", "-", 320f, 544f, 280f, 24f, 12, Skin.DimColor,
                TextAnchor.MiddleRight);

            // 接收端专用：显示发送端卡槽的存量（发送端每 0.2 秒同步一次快照）。
            // 位置借用发送端卡槽区那一块 —— 接收模式整块收起，这里正好空着。
            _peerStockText = UI.Label(_root, "peerStock", "", 28f, 548f, 592f, 24f, 13, Skin.TextColor);
            _peerHintText = UI.Label(_root, "peerHint", "", 34f, 580f, 566f, 46f, 12, Skin.WarnColor);
            _peerHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _peerHintText.verticalOverflow = VerticalWrapMode.Overflow;
            _peerHintText.alignment = TextAnchor.UpperLeft;

            _cells.Add(BuildCell(Items.InformationMatrix, false, "同星系", 566f));
            _cells.Add(BuildCell(ItemProto.kWarperId, false, "跨星系", 594f));
            _cells.Add(BuildCell(Items.PrimaryLens, true, "跨星系", 622f));
            // 1.10.0：提速格 —— 放三级增产剂 Mk.III，传输速率 ×2（每提速 1 秒扣 1 个）
            _cells.Add(BuildCell(C.BoostItem, false, "提速", 650f));
            _cells[_cells.Count - 1].always = true;
            _cells[_cells.Count - 1].tagBase = "提速 ×" + C.BoostRate;

            // --- 能量池升级（1.7.0）---
            // 和「消耗物品卡槽」分开放：卡槽那几格只在发送模式显示，
            // 升级是任何模式都要用的，所以单独一块、常驻显示。
            _upLine = UI.Panel(_root, "line6", 18f, 682f, W - 36f, 1f, CLine);
            _upTitle = UI.Label(_root, "lblUp", "能量池升级", 28f, 686f, 100f, 24f, 14, Skin.DimColor);
            _levelText = UI.Label(_root, "level", "-", 132f, 686f, 330f, 24f, 12, Skin.TextColor);
            _btnUpgrade = UI.Btn(_root, "btnUp", "升级", 470f, 684f, 140f, 26f,
                Skin.BtnColor, Skin.TextColor, DoUpgrade, 13, Skin.ButtonSprite);

            _upCell = BuildCell(C.UpgradeItem, false, "升级消耗", 714f);
            _upCell.isUpgrade = true;
            // 阶梯材料一档要上千个，按「一叠」点太累：升级格直接一次投入背包里的全部
            // （受当前等级需求封顶，多出来的留在背包）。
            int upStack = 20;
            var upProto = LDB.items.Select(C.UpgradeItem);
            if (upProto != null && upProto.StackSize > 0)
                upStack = upProto.StackSize;
            _upCell.addCount = upStack;
            _upCell.btnAdd.GetComponentInChildren<Text>().text = "投入全部";
            if (_upCell.btnTake != null)
                _upCell.btnTake.GetComponentInChildren<Text>().text = "退回";

            // 1.12.0：第二种升级材料「引力矩阵」——两种材料必须同时凑齐才能升级。
            _upCell2 = BuildCell(Items.GravitonMatrix, false, "升级消耗", 742f);
            _upCell2.isUpgrade = true;
            int upStack2 = 100;
            var upProto2 = LDB.items.Select(Items.GravitonMatrix);
            if (upProto2 != null && upProto2.StackSize > 0)
                upStack2 = upProto2.StackSize;
            _upCell2.addCount = upStack2;
            _upCell2.btnAdd.GetComponentInChildren<Text>().text = "投入全部";
            if (_upCell2.btnTake != null)
                _upCell2.btnTake.GetComponentInChildren<Text>().text = "退回";

            _hintText = UI.Label(_root, "hint", "", 34f, 786f, 572f, 44f, 11, Skin.DimColor);
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
            //   接收 → 不需要消耗品；待机 → 不能放；链路类型对不上 → 不能放；还没链路 → 可以放（先备料）。
            HubInfo info = Info;
            string label = cell.isLens ? Items.LensLabel : Items.Name(cell.itemId);
            HubMode hm = info != null ? (HubMode)info.mode : HubMode.Idle;
            // 升级耗材格不受工作模式限制（发送 / 接收时也能先把蓄电器塞进去）
            if (info != null && !cell.isUpgrade && !CellActive(info, cell, hm))
            {
                UIRealtimeTip.Popup(hm == HubMode.Receive
                    ? "接收能量不需要消耗品，卡槽只属于发送端"
                    : hm == HubMode.Idle
                        ? "先选「发送能量」，待机时不消耗物品"
                        : "当前链路用不到" + label +
                          (cell.itemId == Items.InformationMatrix ? "（同星系链路才消耗）" : "（跨星系链路才消耗）"));
                return;
            }

            // 升级格「投入全部」：把背包里现有的全投进去（HubManager 会按当前等级需求封顶）
            int want = cell.isUpgrade ? Math.Max(n, HubManager.BagCount(cell.itemId)) : n;

            // 联机客户端：升级材料也走「放进卡槽」这条路，由主机吸收进进度池。
            // 走 InvestUpgrade 会直接改客户端的进度池，和主机那份对不上（等于凭空多出材料）。
            bool asSlot = cell.isUpgrade && MP.CanReport;

            int added = cell.isUpgrade && !asSlot
                ? HubManager.InvestUpgrade(info, factory, ed.storageId, cell.itemId, want)
                : cell.isLens
                    ? HubManager.AddLens(factory, ed.storageId, n)
                    : HubManager.AddItem(factory, ed.storageId, cell.itemId, want);

            if (added > 0)
            {
                // 联机：把这一笔变动报给主机（主机按增量往它的卡槽里补/扣）
                HubManager.NoteSlotDelta(info, cell.itemId, cell.isLens, added);
                UIRealtimeTip.Popup((cell.isUpgrade ? "已投入" : "已放入") + label + " ×" + added);
            }
            else if (cell.isUpgrade)
            {
                int bagUp = HubManager.BagCount(cell.itemId);
                UIRealtimeTip.Popup(bagUp > 0
                    ? "本级材料已经够了，点「升级」即可"
                    : "背包里没有" + label);
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

        /// <summary>
        /// 点「升级」：卡槽里凑不出 1000 个蓄电器时，自动从背包补足，再扣料升级。
        /// 逻辑在 <see cref="HubManager.Upgrade"/>，这里只负责提示与刷新。
        /// </summary>
        private void DoUpgrade()
        {
            PlanetFactory factory;
            EntityData ed;
            if (!TryGetHubEntity(out factory, out ed))
                return;
            HubInfo info = Info;
            if (info == null)
                return;

            string msg;
            if (HubManager.Upgrade(info, factory, ed.storageId, out msg))
                UIRealtimeTip.Popup(msg);
            else
                UIRealtimeTip.Popup("升级失败：" + msg);
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

            // 升级格：把「已投入进度池」的材料退回背包（卡槽里那点由 tick 自动吸收，取不出来）
            if (cell.isUpgrade)
            {
                // 联机客户端：进度池在主机那边，客户端只能取回「还留在卡槽里」的那部分
                if (MP.CanReport)
                {
                    int mine = HubManager.TakeItem(factory, ed.storageId, cell.itemId);
                    UIRealtimeTip.Popup(mine > 0 ? "已取回" + label + " ×" + mine : "卡槽里没有" + label);
                    if (info != null)
                    {
                        HubManager.NoteSlotDelta(info, cell.itemId, false, -mine);
                        HubManager.ReportState(info);
                    }
                    Refresh(true);
                    return;
                }

                int back = info != null
                    ? HubManager.RefundUpgrade(info, factory, ed.storageId, cell.itemId)
                    : 0;
                UIRealtimeTip.Popup(back > 0 ? "已退回" + label + " ×" + back : "还没有投入" + label);
                if (info != null)
                    HubManager.ReportState(info);
                Refresh(true);
                return;
            }

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

            // 玩家按 E 打开背包时自动关闭本窗口（其它界面不关）
            if (InventoryOpen())
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

            // 模式按钮：选中项用原版选中色（mode1TapColor），未选中用原版未选中灰（mode2TapColor），
            // 下方亮线 + 文字提亮——三态观感对齐原版能量枢纽窗口
            var mode = (HubMode)info.mode;
            if (_tap != null)
                _tap.color = mode == HubMode.Idle ? Skin.BtnOffColor
                    : mode == HubMode.Send ? CViolet : COrange;
            SetModeVisual(mode == HubMode.Idle, _modeIdleLine, _modeIdleText, Skin.AccentColor);
            SetModeVisual(mode == HubMode.Send, _modeSendLine, _modeSendText, CViolet);
            SetModeVisual(mode == HubMode.Receive, _modeRecvLine, _modeRecvText, COrange);
            if (Skin.ButtonSprite != null)
            {
                SetModeBg(_modeIdleBtn, mode == HubMode.Idle);
                SetModeBg(_modeSendBtn, mode == HubMode.Send);
                SetModeBg(_modeRecvBtn, mode == HubMode.Receive);
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
                            : info.boostActive
                                ? "传输速率  " + UI.FmtPower(info.ratePerSecond) + "（提速×" + C.BoostRate + "）"
                                : "传输速率  " + UI.FmtPower(info.ratePerSecond) + "    →  " + info.receiverCount + " 条链路";
                        _rateText.color = info.itemStarved ? Skin.WarnColor : Skin.OkColor;
                    }
                    break;
                case HubMode.Receive:
                    _rateText.text = "接收速率  " + UI.FmtPower(info.ratePerSecond) + "    →  本地电网";
                    _rateText.color = info.linked ? Skin.OkColor : Skin.WarnColor;
                    break;
                // 1.7.1：储能不再是独立模式，充放状态并入对接状态行（RefreshLinkText）
                default:
                    _rateText.text = "传输速率  0 W    （待机）";
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
            long pend = HubManager.GetPending(_planetId, _entityId);
            _pendingText.text = "本机待接收  " + UI.FmtEnergy(pend);
            _pendingText.color = pend > 0 ? Skin.AccentColor : Skin.DimColor;

            if (force || mode != HubMode.Send)
            {
                // 底部提示只有 44 px 高（约 2 行），文案必须控制在两行以内，否则会画出窗口底边。
                _hintText.text = mode == HubMode.Idle
                    ? "待机：能量池保持当前电量，不参与电网充放与星际传输。"
                    : mode == HubMode.Send
                        ? "发送：抽电网的电存池，接收端「连接」后外送；卡槽放「三级增产剂 Mk.III」可提速 ×" +
                          C.BoostRate + "。"
                        : "接收：在列表点「连接」选一台发送终端；跨星系链路的翘曲消耗由发送端提供。";
                _hintText.text += "\n升级 本级 " + C.UpgradeCostOf(info.level) + " 蓄电器 + " +
                                  C.UpgradeCost2Of(info.level) + " 引力矩阵（自动累积）";
            }

        }

        private void RefreshCells(PlanetFactory factory, EntityData ed, HubInfo info, HubMode mode)
        {
            // 1.2.6：接收能量模式整个卡槽区收起（消耗品只属于发送端，接收端放东西没有意义）。
            //   发送端：按链路类型决定哪几格可用——同星系只吃「信息矩阵」，跨星系只吃「空间翘曲器 + 透镜」；
            //           已有链路但类型对不上 → 该格禁用；还没链路 → 标「待连接」，允许先备料。
            //   待机：卡槽保留但全部禁用（待机不消耗物品）。
            bool showSlots = mode != HubMode.Receive;
            SetSlotsVisible(showSlots);
            if (!showSlots)
                return;

            bool sendMode = mode == HubMode.Send;
            bool idle = !sendMode;

            bool sameStar = false, crossStar = false, hasLink = false;
            if (sendMode)
                HubManager.LinkUsage(info, out sameStar, out crossStar, out hasLink);
            bool canStock = sendMode && !hasLink;

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
                if (c.always) match = true;               // 提速格：两种链路都用得上
                else if (c.isLens) match = crossStar;
                else if (c.itemId == Items.InformationMatrix) match = sameStar;
                else match = crossStar;                   // 空间翘曲器

                // 待机不消耗任何耗材，整区置灰（提速格 always=true 也必须跟着待机一起灰，
                // 否则会出现「按钮亮着但点了没反应」的错觉——AddCell 里还有一道 CellActive 校验）
                bool active = !idle && (match || canStock);

                // 标签：类型匹配 → 原用途；还没链路 → 待连接；类型不符 → 未启用；待机 → 待机
                c.tag.text = idle
                    ? "待机"
                    : (match ? c.tagBase : (canStock ? "待连接" : "未启用"));
                c.tag.color = match ? Skin.AccentColor : dim;
                c.name.color = active ? Skin.TextColor : dim;
                c.count.text = "× " + n;
                c.count.color = n > 0 ? (active ? Skin.TextColor : dim) : (active ? Skin.WarnColor : dim);
                c.icon.color = active ? Color.white : new Color(1f, 1f, 1f, 0.35f);

                // 「取出」永远可用，否则玩家断链后取不回卡槽里的东西
                if (c.btnAdd != null) c.btnAdd.interactable = active;
                if (c.btnAddImg != null)
                    c.btnAddImg.color = active ? Skin.BtnColor : offBg;
            }
        }

        /// <summary>
        /// 刷新能量池升级区：等级 / 容量 / 两种材料的进度 / 按钮可用性。
        ///
        /// 1.13.0 起材料是阶梯需求，且卡槽里的料会被自动吸进「升级进度池」，
        /// 所以这里显示的是「已投 + 卡槽里还没来得及吸的 + 背包」的合计，
        /// 后面跟当前等级的需求，玩家一眼能看出还差多少。
        /// </summary>
        private void RefreshUpgrade(PlanetFactory factory, EntityData ed, HubInfo info)
        {
            if (_upCell == null || _levelText == null)
                return;

            // 引力矩阵 id 可能在 Resolve 时被本地化名字校正过，这里同步一下
            if (_upCell2 != null)
                _upCell2.itemId = Items.GravitonMatrix;

            int need = C.UpgradeCostOf(info.level);
            int need2 = C.UpgradeCost2Of(info.level);

            long have = info.upAcc +
                        HubManager.CountItem(factory, ed.storageId, C.UpgradeItem) +
                        HubManager.CountBag(C.UpgradeItem);
            long have2 = info.upMat +
                         HubManager.CountItem(factory, ed.storageId, Items.GravitonMatrix) +
                         HubManager.CountBag(Items.GravitonMatrix);

            int lv = info.level;
            bool max = lv >= C.MaxPoolLevel;
            bool okA = max || have >= need;
            bool okM = max || have2 >= need2;

            _upCell.count.text = max ? "已投 " + info.upAcc : have + " / " + need;
            _upCell.count.color = max ? Skin.TextColor : okA ? Skin.OkColor : Skin.WarnColor;
            if (_upCell.tag != null)
            {
                _upCell.tag.text = max ? "已满级" : Pct(have, need);
                _upCell.tag.color = max ? Skin.OkColor : okA ? Skin.OkColor : Skin.DimColor;
            }

            if (_upCell2 != null)
            {
                _upCell2.count.text = max ? "已投 " + info.upMat : have2 + " / " + need2;
                _upCell2.count.color = max ? Skin.TextColor : okM ? Skin.OkColor : Skin.WarnColor;
                if (_upCell2.tag != null)
                {
                    _upCell2.tag.text = max ? "已满级" : Pct(have2, need2);
                    _upCell2.tag.color = max ? Skin.OkColor : okM ? Skin.OkColor : Skin.DimColor;
                }
            }

            _levelText.text = lv + " / " + C.MaxPoolLevel + " 级    容量 " + UI.FmtEnergy(info.Capacity) +
                              (max
                                  ? "    （已满级）"
                                  : "    →  " + UI.FmtEnergy(C.PoolOf(lv + 1)));
            _levelText.color = max ? Skin.OkColor : Skin.TextColor;

            // 材料齐了按钮才亮：点亮 = 现在点就能升级，不用点一下才知道缺什么
            if (_btnUpgrade != null)
                _btnUpgrade.interactable = !max && okA && okM;
        }

        /// <summary>进度百分比（0~100，向上取整，避免 99% 看起来像卡住）。</summary>
        private static string Pct(long have, int need)
        {
            if (need <= 0) return "";
            int p = (int)((have * 100L + need - 1) / need);
            return p > 100 ? "100%" : p + "%";
        }

        /// <summary>显示/收起整个消耗品卡槽区（分隔线 + 标题 + 待处理文本 + 三格）。</summary>
        private void SetSlotsVisible(bool on)
        {
            if (_slotLine != null) _slotLine.gameObject.SetActive(on);
            if (_slotTitle != null) _slotTitle.gameObject.SetActive(on);
            if (_pendingText != null) _pendingText.gameObject.SetActive(on);
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell c = _cells[i];
                if (c != null && c.holder != null && c.holder.activeSelf != on)
                    c.holder.SetActive(on);
            }
        }

        /// <summary>
        /// 这一格当前能不能往卡槽里放东西。
        /// 1.2.6：卡槽只属于发送端——接收端整区收起，这里兜底拒绝；
        /// 待机不放；发送端按链路类型决定，无链路允许先备料。
        /// </summary>
        private static bool CellActive(HubInfo info, Cell c, HubMode mode)
        {
            if (mode != HubMode.Send)
                return false;
            bool sameStar, crossStar, hasLink;
            HubManager.LinkUsage(info, out sameStar, out crossStar, out hasLink);
            bool match;
            if (c.always) match = true;
            else if (c.isLens) match = crossStar;
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

                default:
                    _linkText.text = "待机，不参与对接";
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
            bool show = mode == HubMode.Receive && info != null &&
                        info.peerPlanetId >= 0 && info.peerEntityId > 0;

            if (_peerStockText != null && _peerStockText.gameObject.activeSelf != show)
                _peerStockText.gameObject.SetActive(show);
            if (_peerHintText != null && _peerHintText.gameObject.activeSelf != show)
                _peerHintText.gameObject.SetActive(show);
            if (!show)
                return;

            bool sameStar, crossStar;
            HubManager.LinkUsage(info, out sameStar, out crossStar);
            bool cross = crossStar;

            bool fresh = info.peerStockStamp > 0f && Time.time - info.peerStockStamp <= 6f;
            if (!fresh)
            {
                _peerStockText.text = "发送端卡槽  未同步（发送端所在星球未加载，或对方没在发送状态）";
                _peerStockText.color = Skin.DimColor;
                _peerHintText.text = "";
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
                    ? ""
                    : "⚠ 请让发送端放入「空间翘曲器 + 引力透镜」，才可以使用能量传输系统";
            }
            else
            {
                int m = info.peerStockMatrix;
                _peerStockText.text = "发送端卡槽  信息矩阵 × " + m + "（同星系链路）";
                _peerStockText.color = m > 0 ? Skin.OkColor : Skin.WarnColor;
                _peerHintText.text = m > 0
                    ? ""
                    : "⚠ 请让发送端放入「信息矩阵」，才可以使用能量传输系统";
            }
            _peerHintText.color = Skin.WarnColor;
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

        /// <summary>模式按钮底色：选中=原版选中色，未选中=原版未选中灰。</summary>
        private static void SetModeBg(Button btn, bool on)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img != null)
                img.color = on ? Skin.BtnOnColor : Skin.BtnOffColor;
        }
    }
}
