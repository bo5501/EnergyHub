# 星际能量枢纽塔 (Interstellar Energy Hub Tower)

Dyson Sphere Program mod —— 后期解锁的玻璃棱镜巨构：把**超大容量储能**（20 级 100 GJ→1 PJ，
封顶 50 TW）和**星球间直连输电**做进同一座塔，矿星供电 / 夜面补能 / 戴森球外送 / 削峰填谷都能用。

> 发布包（Thunderstore 格式）在 `dist/EnergyHubTower-1.17.0.zip`。
> 仓库：https://github.com/bo5501/EnergyHub
> 反馈：GitHub Issue，或 QQ 1570246504
> 联机：可选支持 Nebula（主机结算 / 客户端只读，`src/EnergyHub.Nebula` 是可选子模块）；
> 兼容：尽力兼容创世之书（科技节点统一挂「能量枢纽」科技右下方，材料缺失自动降级）。
> 对外描述（中文优先，英文摘要附后）见 `dist/EnergyHub/README.md`；manifest 短描述也是中文。
> **发布合规**（填上传表单前必读 `docs/THUNDERSTORE.md`）：
> 上传时 `Contains NSFW content` **必须选 No**（选错会被判 `Invalid submission` 直接拒掉，且该标志
> 不能事后改、也不跨版本继承，只能发新版本重设）；README 顶部有「内容与合规声明」，写明无成人内容、
> AI 使用披露（程序集带 `AI_Assisted_Creation=true` / `AI_Model_Vendor=DeepSeek`）；
> DSP 社区暂**无** `AI Generated` 分类，故以 README 声明为披露依据。
> 打新包：`bash package.sh`（编译 → 生成图标 → 组装 → zip，含校验）。

## 玩法

1. **先研究科技**：科技树**「能量枢纽」科技右下方一格**（卫星配电系统正下方）的「星际能量枢纽塔」
   （`Tech.cs` 经 LDBTool PreAdd 注册、建表后定位置，找不到该科技时退回「信息矩阵」正后方，
   研究消耗**信息矩阵**：`ItemPoints=20`，即每 180 hash 配 1 个，总量 = hash×20/3600）。
2. 解锁后配方 = **能量枢纽 ×5 + 射线接收站 ×5 + 蓄电器 ×100 + 处理器 ×5**（组装台 5 秒）。
   原料 id 在 `Awake` 阶段拿不到，`ResolveRecipeItems` 在 LDB 就绪后按中英文名反查填回。
3. 接到本地电网上。
4. 点击建筑打开窗口，三种模式：**待机 / 发送 / 接收**。
   - **蓄电器并入发送/接收（1.8.0，无独立储能模式）**：`BatteryDir` 按
     `net.energyCapacity - net.energyRequired` 判余量（1% 死区 + 消抖），
     发送端「放电优先」（缺电支援电网），接收端「充电优先」（富余存起来）；
   - **接收端**在列表里挑一台发送终端，点「连接」；
   - **发送端**只显示谁连了过来（连接由对方发起）；
   - **按需自动分配（1.11.0）**：一台发送端挂多台接收端时不再平均分。每台接收端
     每帧上报「我现在能吃多少」（池子空余 ∩ 满功率放电 5 秒，再加本地电网缺口），
     发送端按权重切分，装不下的端（池子已满）权重为 0 直接退出，
     被在途账本或消耗品卡住的份额进入下一轮重新分给还有空间的端。
5. 卡槽 6×14 = 84 格（信息矩阵 / 空间翘曲器 / 引力透镜 / 提速用三级增产剂 + 升级用的蓄电器与引力矩阵），
   可用传送带送料（1.10.0：底座前后各 4 个接口，共 8 个进口；4~7 号由 `PullItemsFromBelts`
   直接读实体连接点取料，原版 exchanger 只认自己的 belt0~3）。
   同星系每 200 MJ 扣 1 信息矩阵；跨星系每 200 MJ 扣 1 翘曲器 + 1 透镜。缺料时降到 50% 速率。
6. **提速（1.10.0）**：卡槽放「三级增产剂 Mk.III」(1143) 时星际传输 ×2，每秒扣 1 个
   （`ConsumeBoost`，只在真的外送能量时扣）。
7. **能量池升级（1.13.0，阶梯材料）**：共 **20 级**，每级都要**空蓄电器(2206) + 引力矩阵(6005)**。
   材料按档递增：1~4 级 1000+2000，5~8 级 1400+3200，9~12 级 1900+5000，
   13~16 级 2600+7800，17~19 级 3500+12000（累计 38100 + 108000）。
   卡槽里的升级材料每 tick 被 `AbsorbUpgradeItems` 吸进 `HubInfo.upAcc/upMat` 进度池累积，
   不必一次凑齐；`Upgrade()` 先扣进度池、不足再从背包扣。1 级 100 GJ，每级约 ×1.62，满级 **1 PJ**。
8. 面板「能量池」一行显示 **当前储量 / 总容量 + 百分比**（1.10.0）。
9. 发送端列表每行显示该接收端的 **需求 / 本帧实得**（1.11.0），
   权重为 0（池子已满）时显示为红色「得 0 J」。
10. 没有电 / 电网没有空闲能量时，塔顶特效会停下（设计如此）。

## 参数（`src/EnergyHub/C.cs`）

| 项 | 值 |
|---|---|
| 能量池 | 指数曲线：`PoolBase`=100 GJ → `PoolMaxCap`=1 PJ，`MaxPoolLevel`=20（每级 ×1.61） |
| 功率硬顶 | `MaxPowerWatt`=50 TW（充 / 放 / 传输共用） |
| 充/放功率 | `min(50 TW, 容量÷20秒)` —— 任何等级约 20 秒充满，20 级恰好 50 TW |
| 星际传输 | `min(50 TW, 容量÷100秒)` —— 比电网慢 5 倍，鼓励多台并联 |
| 传输提速 | `BoostRate`=2.0 ×，耗材 `BoostItem`=1143（三级增产剂 Mk.III），`BoostPerSec`=1 |
| 分配策略 | 按需权重分配（`DemandSeconds`=5 秒），多轮回填剩余额度（最多 6 轮） |
| 需求上报有效期 | `DemandTimeout`=3 秒，过期退回「满功率放电 5 秒」估算 |
| 升级耗材 | 阶梯表 `UpTierAcc/UpTierMat`：1000+2000(1~4级) → 3500+12000(17~19级)，见 `UpgradeCostOf` |
| 消耗品 | 1 个 / 200 MJ |
| 待接收账本上限 | `PoolOf(level) × 4` |
| 卡槽 | 6 列 × 14 行 = 84 |
| 建筑尺寸 | 底座 r4.30 m，高 32 m |

## 构建

```bash
bash build.sh     # 编译 + 部署到 r2modman 的 lianji-mod profile
bash package.sh   # 只编译 + 打 Thunderstore 发布包（游戏开着也能跑）
```

依赖游戏目录下的 `Assembly-CSharp.dll` 与 BepInEx 5.4.x（路径在 `EnergyHub.csproj` 里配）。
Python 工具链需要 `numpy / matplotlib / Pillow / UnityPy`。

## 目录

```
src/EnergyHub/
  C.cs            常量（id、尺寸、能量参数、版本号）
  Plugin.cs       BepInEx 入口 + 存档 + 每帧 tick
  Protos.cs       ModelProto / ItemProto / RecipeProto 注册 + 程序化图标
  HubPrefab.cs    运行时构建 prefab 与 PrefabDesc（含 8 个传送带接口位：前后各 4）
  HubMesh.cs      程序化网格：六棱玻璃棱镜塔 + 中心核 + 底部接口套筒 + 材质
  HubEffect.cs    特效层：三颗公转双色球、6 圈同心环、电弧、激光、地面光晕
  HubManager.cs   枢纽状态、能量搬运、跨星球账本、点对点链路、等级/升级、存档 v4
  Tech.cs         科技节点：Awake 交 LDBTool PreAdd 建表，PostAdd 里定位置/前置
  HubWindow.cs    专用 UI 窗口（含储能按钮与能量池升级区块）
  Patches.cs      Preload 挂载 / 点击接管 / 配方解锁 / 动画数据钉死
  UI.cs Skin.cs   界面工具与配色
dist/             Thunderstore 发布包（manifest / icon / README / dll）
tools/
  render_prism.py     整塔造型预览（软件渲染器）
  render_orb_head.py  塔顶三球 + 光环预览
  make_icon.py        生成 256×256 图标
  preview_*.py        历史造型预览
```

## 关键技术点

- **不用 AssetBundle**：建筑走 `Graphics.DrawMeshInstancedIndirect`，`lodVertas` 可为 null，
  所以网格和 `PrefabDesc` 都能在 C# 里现造。
- **玻璃材质**：原版 `energy-exchanger-glass` 用 `PBR Standard Glass`（Transparent 队列、
  `_Color.a` 控不透明度、`_RimColor` 做菲涅尔边光），直接克隆它的 shader 再改色。
  玻璃 submesh 必须**最后提交**，保证「先画内部光柱和骨架、再叠玻璃」。
- **点对点账本**：`_pending` 按 `(planetId, entityId)` 记账，不是按星球；`_sendTargets` 存接收端实体 key。
  `HubInfo` 记 `pos/hasPos` 防 entityId 回收复用。存档 v4（v3 加 `level`，老档缺省 1 级）。
- **储能模式不自研充放电**：只给 `PowerExchangerComponent.state/targetState` 下方向指令（+1 充 / −1 放），
  能量搬运与电网结算仍走原版 `InputUpdate/OutputUpdate`。判据用 `energyCapacity − energyRequired`
  （已扣掉交换站自身放电上限），避免自己放电抬高需求导致抖动。
- **等级容量对齐**：`PowerExchangerComponent.Import` 会无条件把 `maxPoolEnergy/energyPerTick`
  重置回 prefabDesc 值，所以每帧 `ApplyLevel` 重新覆盖，读档也不会掉级。
- **科技节点（1.16.0 重写）**：必须走 **LDBTool 的 PreAdd 队列**，不能事后手工往
  `LDB.techs.dataArray` 里塞。创世之书就是这样注册它自己那 143 个科技的
  （`src/Utils/JsonDataUtils.cs`：`LDBTool.PreAddProto(protoJson.ToProto())`），
  由 LDBTool 在 `Pre Loading` 阶段统一建表；中途手工塞进去的条目在那一刻会被建表结果覆盖掉，
  科技树遍历 `LDB.techs.dataArray` 建节点，自然就看不到我们那条了（1.15.x 的真因）。
  所以分两步：
  1. `Awake` → `Tech.PreAdd()`：构造 `id = C.TechFixedId (1999)` 的骨架
     （1999 在所有已知环境里都空着；创世 3.2.4 内嵌 techs.json 最大 1963；必须 &lt;2000 才在主科技树页），
     `LDBTool.PreAddProto()` 提交；
  2. `PostAddDataAction` → `Tech.Layout()`：此时原版 + 创世的科技都已就位，才反查锚点、算坐标、
     写 `PreTechs`，并重算母节点的 `postTechArray`（`UITechNode.SetTechProto` 按父节点的
     `postTechArray` 生成连线，不重算就没有那条线）。
  锚点是「能量枢纽」科技（解锁原版能量枢纽建筑那条，原版叫「星际电力运输」），按
  「物品 → `PrefabDesc.isPowerExchanger` → 配方 → 科技」反查，不依赖科技名/语言。
  位置按真实栅格步长算（`C.TechColStep/TechRowStep = 4`，即 x 456 px / y 192 px）：
  第一优先「右一格 + 下一行」，创世里就是 (21,-3) → (25,-7)，被占则按表逐格退让。
  `Layout` 必须跑在 `ItemProto.Preload` / `RecipeProto.Preload` 之前，否则 `FindPreTech()` 找不到。
  之后每 5 秒确认节点还在表里（被别的 MOD 整表重建冲掉就补一次，最多 3 次）。
- **配方原料延迟解析**：`RecipeProto.Preload` 会对 `Items[i]` 取 `.Productive`，所以占位必须用
  真实存在的 id（`kAccumulator`），LDB 就绪后按名字换成真 id，再补跑一次 `InitRecipeItems()`。
- **特效强度**：参照 `PowerSystem` 给 excPool 充电的 `currEnergyPerTick` 与 `StateUpdate` 的斜坡，
  用 `fxLevel` 0~1 驱动，`FxRisePerSec=0.85 / FxFallPerSec=1.80 / FxHoldSec=1.5`。
  强度为 0 时整组 renderer 关掉、动画不跑。
- **模型闪烁**：根因是 GPU 实例的 `AnimData.state/power` 随工作状态在 0 和 1 之间跳，
  shader 跟着调制自发光。修法是 Harmony Postfix 在 `PowerSystem.GameTick` 之后把
  `entityAnimPool[eid]` 的 `state/time/prepare_length/power` 钉死。
- **塔顶三球**：`BuildOrbMesh` 用顶点色做金/翠阴阳双色（S 形分界 + 纬度亮度衰减），
  加色材质，省掉贴图和自定义 shader。

## 已知限制

- 联机（Nebula）为软依赖：主机结算、客户端只读，客户端点模式/连接/升级/投料会被拦下。
- 旧存档升级：v2 → v3 的链路会以「存档恢复」重建，需要接收端重新点一次连接才恢复传输；
  v3 → v4 只补 `level`（按 1 级），不影响链路。
- 1.7.0 起解锁依赖科技节点：若 `Tech.Layout()` 失败（找不到锚点 / 拿不到空闲 id），会自动把
  `UnlockKey` 回落成 `-1`（永远解锁），保证建筑不会因为科技没建成而永久锁死。
- 塔顶特效是独立 GameObject，不参与建筑的 GPU instancing；枢纽特别多时会有额外 draw call。
