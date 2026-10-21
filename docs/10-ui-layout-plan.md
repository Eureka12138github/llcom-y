# LLCOM UI 布局优化计划

> 状态：**阶段 A、B、C 已实施**（提交 `9e2ba28`、`f2e3400`、`2318db8`、`3db2f1b`、`184799e`）；阶段 D 与“右侧面板可折叠”待定。
> 目标分支：`feature/refactor-continue`（**不得直接改 master**）。
> 约束：本计划为**纯 UI/排版**优化，**不改变功能与绑定逻辑**。
> 参考：`MainWindow.xaml`(815 行)、`SettingWindow.xaml`(400 行)、`Pages/*.xaml`。

---

## 一、背景与目标

现有界面功能完整，但排版存在信息层次不清、间距不统一、颜色硬编码、交互发现性差等问题。
目标：让界面**更易用、更简洁明了**，并为后续深色主题/一致性打好基础。

---

## 二、现状诊断（均有 XAML 依据）

### 信息架构
- 主窗口三区：左＝收发日志（`dataShowFrame`, `11*`）+ 发送框（左列底部）；右＝`TabControl`（`7*`）含 快捷发送 / Lua / 在线脚本 / **工具（内部再嵌一层 TabControl，10 页）** / 关于。
- **工具真实藏在三层 Tab 里**，发现性差。
- 发送框只占左列底部（非“日志在上、发送跨全宽在下”的常见范式）。

### 布局技术债（可低风险直接修）
- `MainWindow.xaml`：`TabControl Grid.Column="3"`、`Menu Grid.Column="3"` 列号越界（仅 3 列 0/1/2，靠 WPF 容错落末列）。
- 发送区 Grid 有 3 个 `ColumnDefinition Width="0"` 空列。
- 魔法数字：行高 `35/30/60`、边距 `2/3/5/10`、宽度 `55/75/50/70/150/155/250`。
- 状态栏硬拆 **20 列**；无最小窗口尺寸（默认 900×500）。

### 视觉 / 主题
- 硬编码颜色：`#F6F6F6`、`Foreground="LightYellow/Black/Brown/DarkGreen/DarkRed…"` → **深色主题必崩**、与主题色不一致。
- 无分组标题、无层次；状态栏端口/波特率/状态/计数全平铺。

### 交互 / 可达性
- 大量**纯图标按钮**仅靠 tooltip。
- `DataShowPage` 底部 7 个复选（RTS/DTR/HEX/HEX发送/额外回车/符号/禁用日志）水平平铺，窄屏溢出。
- 各工具页各行其是（行高、边距、发送框/按钮位置、命名不统一）；`Title` 有复制粘贴错误（如 `UdpLocalPage` 原为 `TcpLocalPage`，已修）。

### 一致性
- 全局**无设计资源**：`App.xaml` 只有 8 个转换器；样式散落各页或内联。

---

## 三、目标布局

保持“串口调试”为主工作流，主窗口改三段式：

```
┌──────────────────────────────────────────────────────────────┐
│ 顶部工具栏： [端口▾][波特率▾] [打开/关闭]  ●状态灯   ⚙设置  🌐语言 │
├───────────────────────────────┬──────────────────────────────┤
│   收发日志（主视角，占比更大）  │  右侧面板（可折叠）            │
│                               │  快捷发送 / Lua / 工具▾ 单页    │
├───────────────────────────────┴──────────────────────────────┤
│ 发送区（跨全宽）： [文本域....................] [HEX☐] [发送]   │
├──────────────────────────────────────────────────────────────┤
│ 状态栏（精简）：●connected  COM3 115200  |  ↑1234 ↓5678        │
└──────────────────────────────────────────────────────────────┘
```

关键决策（待最终确认）：
1. 发送区**提到跨全宽底部**。
2. 工具由“二级 Tab”改为“右侧面板顶部**选择器 + 单页**”，消除三层 Tab。
3. 状态栏**瘦身**：只留 连接状态灯 + 端口 + 计数；波特率/数据位移到顶部工具栏或设置窗。
4. 右侧面板**可折叠**（日志可占满全宽）。

---

## 四、设计规范（Design Tokens）

| 维度 | 规范 |
|---|---|
| 间距 | 4px 网格：4/8/12/16/24，禁止 2/3/5 混用 |
| 控件高度 | 标准控件 32，工具栏 40，图标按钮 **32×32** |
| 栅格 | 表单统一 2 列（label 固定宽 + field 拉伸）或 `WrapPanel` 间距 8 |
| 颜色 | **只用 AdonisUI 动态资源**；语义色集中定义（成功绿/错误红/警告黄/次要灰） |
| 字体 | 等宽 `Consolas` 仅用于数据/编辑器；正文系统字体 12/13；时间戳次级灰、数据主色 |
| 复用 | 抽 `Styles/DesignTokens.xaml` + 通用样式：`IconButton`、`Toolbar`、`LogRow`、`SendPanel`、`ConnectionBar` |

---

## 五、分阶段计划

### Phase A｜纯样式零逻辑（**本次首选，风险低**）
- 新建 `llcom/Styles/DesignTokens.xaml`：间距/字号/语义色 + `IconButton`/`Toolbar`/`SectionHeader` 等样式；在 `App.xaml` 合并进 `Application.Resources`。
- 把各页**硬编码颜色**替换为动态资源（画笔）。
- 统一边距/行高到 4px 网格（优先主窗口 + 设置窗口 + 工具页）。
- **不改信息架构、不改绑定/命令/事件**。
- 验收：`dotnet build`（含 Release）0 错误；启动后浅色主题观感一致；无功能回归。
- 回滚：独立提交，可 `git revert`。

### Phase B｜主窗口重排
- 顶部工具栏 + 跨全宽发送区 + 精简单状态栏 + 右侧可折叠。
- 修 `Column="3"`、空列、`Title` 等 bug。
- 需 GUI 冒烟（`docs/03-test-checklist.md`）。

### Phase C｜工具页统一
- 统一“连接栏 / 收发日志 / 发送区”模板；抽 UserControl/Style。
- 工具选择器替代二级 Tab。

### Phase D｜可选
- 深色主题适配、键盘可达性、连接状态可视化、空状态提示。

---

## 六、Phase A 实施清单（执行时逐项勾选）

- [ ] 盘点 AdonisUI 提供的画笔资源键（`Brush`/`Color`），确定可用的主题动态资源；不可用时自定义语义色资源。
- [ ] 新建 `llcom/Styles/DesignTokens.xaml`：
  - [ ] 间距/尺寸（4/8/12/16/24、控件高度 32、工具栏 40）
  - [ ] 字号/字体族（正文、等宽、时间戳次级）
  - [ ] 语义色画笔（成功/错误/警告/次要/边框/日志背景）
  - [ ] `IconButton`、`Toolbar`、`SectionHeader` 样式
- [ ] `App.xaml` 合并 `DesignTokens.xaml`。
- [ ] 替换硬编码颜色：
  - [ ] `MainWindow.xaml`（`#F6F6F6`、`LightYellow/Black/Brown/Green` 等）
  - [ ] `DataShowPage.xaml`（`DarkSlateGray/DarkGreen/Black`）
  - [ ] 工具页/设置窗口中的硬编码前景色
- [ ] 统一边距/行高（4px 网格），不改变控件层级。
- [ ] 构建 x64 Debug/Release；启动冒烟；提交（独立 commit，中文信息）。

---

## 七、待确认事项

1. **范围**：本次确定只做 Phase A（纯视觉，不改结构）？后续是否继续 B/C？
2. **主题**：只保证浅色，还是同时保证深色可用（决定是否必须消灭全部硬编码颜色）？
3. **主工作流**：日志为主 + 右侧面板可折叠、发送区跨全宽底部 —— 是否认可？
4. **目标分辨率**：是否以 1366×768 为最小适配目标？

---

## 九、阶段 A 实施记录（已完成）

- 新增 `llcom/Styles/DesignTokens.xaml`：间距（4px 网格）、尺寸（控件 32 / 工具栏 40 / 图标按钮 32）、
  结构/日志/状态/强调/图标 语义色画笔、`IconButton` 样式；在 `App.xaml` 合并。
- 消除硬编码颜色（改为 `{DynamicResource Brush.*}`）：`MainWindow.xaml`、`DataShowPage.xaml`、
  `SettingWindow.xaml`、`OnlineScriptsPage.xaml`、`ConvertPage.xaml`、`MqttTestPage.xaml`（共 ~60 处）。
- 统一图标按钮尺寸：`MainWindow`（11 个）、`SettingWindow`（16 个）应用 `IconButton`（32×32）。
- 间距规范化：`MainWindow` `Margin 2→4`、`SettingWindow` `Margin 5→8`/`3→4`、`DataShowPage` 底部 `5→8`。
- 颜色暂用**浅色主题值**（与改造前一致，避免观感回归）；深色主题适配留待后续。
- 提交：`9e2ba28`（阶段A-1 令牌+颜色）、`f2e3400`（阶段A-2 尺寸+间距）。
- 验证：x64 Debug/Release 构建 0 错误；单测 20/20 通过；已启动冒烟。

## 十、阶段 B 实施记录（已完成）

`MainWindow.xaml` 重排为 **4 行 × 3 列**：

- **顶部工具栏（第 0 行，跨 3 列）**：刷新端口 / 端口下拉 / 波特率下拉 / 打开-关闭 / 清空日志 / 设置 / 语言菜单。
  - 语言菜单原浮于右上角覆盖 Tab 标题，现并入工具栏。
- **中部（第 1 行）**：左＝收发日志（第 0 列，占比 12*），右＝功能 `TabControl`（第 2 列，8*），
  中间 `GridSplitter`（第 1 列）可拖拽调宽。
- **发送区（第 2 行，跨全宽）**：文本框拉伸 + 发送按钮（原在左列底部、半宽）。
- **状态栏（第 3 行，跨全宽）**：精简为“状态 + 收发计数”（原 20 列含端口/波特率/多项分隔符全部移出）。
- 顺带修正：原 `Grid.Column="3"` 列号越界、发送区 3 个 `Width="0"` 空列。

提交：`2318db8`。验证：x64 Debug/Release 构建 0 错误；单测 20/20；启动加载正常（窗口标题与响应正常）。
未做（可选）：右侧面板“一键折叠”按钮。

## 十一、阶段 C 实施记录（部分完成）

**已完成（C-1、C-2）：**
- **C-1 工具选择器**（`3db2f1b`）：把“工具”里的**嵌套 `TabControl`（10 个二级 Tab）**换成**顶部 `ComboBox` 选择器 + 单页内容**；
  10 个 `Frame` 仍只 `Navigate` 一次，切换仅切可见性（`ToolSelector_SelectionChanged`）。
  消除了“三层 Tab”，发现性更好。
- **C-2 工具页公共样式**（`184799e`）：`DesignTokens.xaml` 新增 `Tool.Content`（内容边距 8）、
  `Tool.SendBox`（等宽/多行/自动滚动条发送框）、`Tool.SectionLabel`；应用到 `TcpLocalPage` / `tcpTest` /
  `SocketClientPage` / `UdpLocalPage` / `MqttTestPage`（发送框由 8 行属性收敛为一行 `Style`）。

**未做（需可靠的 UI 运行验证，风险较高）：**
- **“连接栏/收发/发送”整块 UserControl 抽取**：三页的发送区布局差异较大（`tcpTest` 的发送行还含
  ClientList/Kick），且当前为 code-behind 事件驱动（非命令），抽 UserControl 需同步改造绑定/命令，
  建议在有稳定 UI 冒烟环境时单独做、逐步替换。

## 八、执行方式

- 在 **执行 agent**（非 Plan 模式）中，于 `feature/refactor-continue` 分支实施；
- 严格限定为“样式/排版”，不改功能与绑定；
- 每个阶段独立提交，便于回退。

---

# 十二、第二轮改进意见（2026-10-14，待确认 → 待实施）

> 状态：**计划中**。目标分支 `feature/refactor-continue`（不得改 master、不 push）。
> 与第一轮不同：本轮既有**布局调整**，也有**功能新增**（字号范围、显示格式切换、定时发送），
> 因此会改动**绑定/命令/模型**，不再是“纯样式零逻辑”。

## 12.0 条目总览

| # | 条目 | 类型 | 复杂度 | 影响面 |
|---|---|---|---|---|
| 1 | 数据字号限制合理范围（min/max） | 设置+UI | 低 | Settings、设置窗 |
| 2 | 收发箭头方向对调（发→ / 收←） | 显示逻辑 | 低 | DataShowItem、SaveLog、单测 |
| 3 | 发送输入框字号跟随“数据字号” | 绑定 | 低 | MainViewModel、MainWindow.xaml |
| 4 | 发送区拖拽上下限（min/max 高度） | 布局 | 低 | MainWindow.xaml 行定义 |
| 5 | 接收显示 字符串/HEX 可切换 | 显示逻辑（部分回退 1.1.4.3） | 中 | Settings、DataShowItem/VM、UI |
| 6 | “关闭串口”按钮悬停变白、白字不可见 | 样式 | 低 | DesignTokens、MainWindow.xaml |
| 7 | 串口控制移入右侧新增“串口设置”页 | 布局重构 | 中 | MainWindow.xaml（大改）、languages |
| 8 | 快捷发送增加“定时发送” | 功能新增 | 高 | ToSendData、VM、MainWindow 模板/code-behind |

---

## 12.1 数据字号限制合理范围

**现状**：`Settings.Display.logFontSize`（默认 15）为裸 `int`，无上下限；`SettingWindow.xaml` 用 `TextBox` 直接绑定（`UpdateSourceTrigger=PropertyChanged`），输入任意数字都会生效（过大撑爆、过小不可读）。

**方案**
- 在 `logFontSize` setter 内做 `Math.Clamp(value, Min, Max)`，越界取边界（并 `Save()`）。
- 设置窗把裸 TextBox 换成 `Slider`（`Minimum`/`Maximum` 与常量一致，`TickFrequency=1`）+ 当前值显示；或保留 TextBox 但 clamp（滑块体验更好）。
- 常量集中定义（如 `Settings.LogFontSizeMin/Max`），UI 滑块与 setter 共用，避免两处不一致。
- 新增语言键 `SettingLogFontSizeRange`（提示区间文案）。

**影响文件**：`Model/Settings.Display.cs`、`View/SettingWindow.xaml`、`languages/zh-CN.xaml`、`languages/en-US.xaml`。

**待确认**：区间取 **10–40**？还是 8–36 / 12–30？控件用滑块还是文本框？

---

## 12.2 收发箭头方向对调

**现状**：`Model/DataShowItem.cs` 中 `ArrowText = sent ? " ← " : " → "`（**发送=左、接收=右**）。

**方案**：改为 `sent ? " → " : " ← "`（**发送=右箭头 →，接收=左箭头 ←**）。

**联动（关键）**：`ViewModels/DataShowViewModel.SaveLog()` 里用 `item.ArrowText == " ← "` 判定为 `[send]`，对调后必须同步改为 `== " → "`，否则导出的日志 send/recv 会反。
单测 `tests/llcom.Tests/DataShowItemTests.cs` 的箭头断言同步更新。

**影响文件**：`Model/DataShowItem.cs`、`ViewModels/DataShowViewModel.cs`、`tests/..DataShowItemTests.cs`。

**待确认**：无。

---

## 12.3 发送输入框字号跟随“数据字号”

**现状**：`MainWindow.xaml` 的 `toSendDataTextBox` 未设 `FontSize`（用默认值）；`MainViewModel` 未对外暴露 `Setting`（只在内部用 `Tools.Global.setting`）。而数据区字号已由 `logFontSize` 控制。

**方案**
- `MainViewModel` 增加只读属性 `public Model.Settings Setting => Tools.Global.setting;`。
- `toSendDataTextBox` 增加 `FontSize="{Binding Setting.logFontSize}"`。
- （可选）快捷发送行内的文本框（`toSendListStyle` 模板里的 `<TextBox>`）同样跟随。

**影响文件**：`ViewModels/MainViewModel.cs`、`View/MainWindow.xaml`。

**待确认**：是否只跟随“主发送框”，还是连“快捷发送”行内文本框、（设置里的）Lua 测试输入框也一起跟随？

---

## 12.4 发送区拖拽的上限/下限

**现状**：`MainWindow.xaml` 行定义为 `Row0 Auto / Row1 *（日志）/ Row2 Auto（分隔条）/ Row3 72（发送区）/ Row4 Auto（状态栏）`，中间 `GridSplitter`（`ResizeBehavior=PreviousAndNext`，`ResizeDirection=Rows`）。发送区高度 `72`，无 Min/Max，可被拖到 0 或过高。

**方案**：给行定义加约束——
- `Row3`（发送区）：`MinHeight`（往下最小）+ `MaxHeight`（往上最大）；
- `Row1`（日志）：`MinHeight`，防止拖拽时日志被压没。
- 建议值：发送区 `Min 40 / Max 240`，日志 `Min 120`。

**影响文件**：`View/MainWindow.xaml`（仅行定义）。

**待确认**：min/max 数值？是否需要把拖拽后的高度**持久化**（存入 settings，下次启动恢复）？

---

## 12.5 接收显示“字符串 / HEX”可切换（部分回退上一轮“固定 HEX”）

**现状（上一轮 1.1.4.3 改动）**：显示被**固定为 HEX 单行**，旧 `showHexFormat` 设置与其 UI（设置窗下拉、数据区三态勾选）已移除；`DataShowItem`/`DataShowViewModel` 不再读格式设置。

**本条目**：重新提供**可切换**能力（接收既能字符串显示、也能 HEX 显示）。

**方案**
- 重新引入格式设置（可沿用旧键名 `showHexFormat`）：`0=字符串 / 1=HEX / 2=两者`；默认值待定（建议默认 HEX，延续上一轮观感）。
- `DataShowItem`（分包）与 `DataShowViewModel`（流式）按设置生成文本。
- UI：设置窗加下拉；（可选）`DataShowPage` 底部恢复三态快速勾选（语言键 `ShowHexFormat`/`SettingShow*` 仍在文件里，可直接复用）。
- “两者”模式的排布需定：同行 `文本 (HEX)`，还是两行（文本行 + HEX 行）。

**影响文件**：`Model/Settings.Display.cs`（或 `.Uart.cs`）、`Model/DataShowItem.cs`、`ViewModels/DataShowViewModel.cs`、`View/SettingWindow.xaml`、`Pages/DataShowPage.xaml`、`languages/*`。

**待确认**
1. 开关**只作用于“接收”**，还是**收发都作用**？
2. 选项保留“两者”吗，还是只做“字符串 / HEX”两项？
3. “两者”如何排（同行 / 两行）？
4. 是否要数据区底部的**快速切换**勾选（原 HEX 三态框）？

---

## 12.6 “关闭串口”按钮悬停变白、白字被覆盖

**现状**：`MainWindow.xaml` 的 `openClosePortButton`使用 `BasedOn="{StaticResource {x:Type Button}}"`（Adonis 默认）+ 一个 `DataTrigger`：串口打开时设 `Background=Brush.State.Success`、`Foreground=White`。Adonis 默认模板的**悬停态**另有浅色背景/渐变，覆盖了绿色底，导致白字在浅色上不可见。

**方案**：为“打开/关闭串口”提供**专用样式**（`DesignTokens.xaml` 新增 `PortToggleButton`），自定义 `ControlTemplate` 显式给出 `Normal / IsMouseOver / IsPressed / IsEnabled=False` 四态背景：
- 悬停/按下用**同色系加深**（或用 `Opacity` 叠加半透明黑）而非改成白色，保证白字始终可读；
- 打开(=绿)/关闭(默认) 仍由现有 `DataTrigger` 切换 `Background`。

**影响文件**：`Styles/DesignTokens.xaml`、`View/MainWindow.xaml`。

**待确认**：打开态配色（绿）+ 悬停加深比例是否 OK；关闭态是否也用自定义配色。

---

## 12.7 串口控制移入右侧新增“串口设置”页

**现状**：`MainWindow.xaml` 第 0 行顶部工具栏左侧依次为：`refreshPortButton`、`serialPortsListComboBox`、`baudRateComboBox`、`openClosePortButton`（右侧为 帮助 `?` / ☰ 菜单 / 🌐 语言）。右侧 `TabControl` 页序为：**快捷发送 / Lua(运行脚本) / 在线脚本(仅图标) / 工具 / 关于**。`refreshPortButton.IsEnabled` 依赖 `ElementName=serialPortsListComboBox`；状态栏另行显示 端口/波特率/状态/计数。

**方案**
- 第 0 行**移除**上述 4 个串口控件，仅保留右侧的 帮助 / ☰ / 语言（可整行改为右对齐）。
- 右侧 `TabControl` **首位**新增 `<TabItem Header="{DynamicResource SerialSettingsCard}">`“串口设置”，把 **刷新 + 端口 + 波特率 + 打开/关闭** 整块剪切进去（绑定、`ElementName` 关系原样保留）。
- 目标页序：**串口设置 → 快捷发送 → 运行脚本 → 在线脚本 → 小工具 → 关于**。
- （可选）把 数据位/校验/停止位/编码/自动重连 也并入该页（当前在设置窗），做成完整串口表单。
- 语言键：新增 `SerialSettingsCard`（“串口设置”/“Serial Port”）。可选给“在线脚本”Tab 补文字标题。

**影响文件**：`View/MainWindow.xaml`（较大改动）、`languages/*`。

**待确认**
1. 第 0 行是否保留（只剩右对齐的 帮助/☰/语言）？还是整行去掉、把图标移到别处？
2. “串口设置”页是否要含**完整参数**（数据位/校验/停止位/编码/自动重连）？
3. 页序是否按上面所列？
4. “在线脚本”Tab 是否补文字标题？

---

## 12.8 “快捷发送”增加定时发送

**现状**：快捷发送是 `ListBox`（`ItemsSource="{Binding QuickSend.Items}"`），条目模型 `Model/ToSendData.cs` 字段：`id / text / commit / hex / recvScriptPath / recvScriptPara`。实际发送在 code-behind `MainWindow.knowSendDataButton_click`：先按 `recvScriptPath` 切换接收脚本 → 按 `hex` 做 `Hex2Byte`/`GetEncoding().GetBytes` → `_vm.SendUartData(data, true)`。模板 `toSendListStyle` 在 `MainWindow.xaml` 内。

**方案（推荐 A：每条独立定时）**
- `ToSendData` 增 `timerEnabled(bool)` 与 `timerIntervalMs(int, 默认=最小间隔)`，随 `quickSend` 一起持久化。
- 列表模板与表头增列：**“定时”勾选** + **“间隔(ms)”** 输入框。
- **发送逻辑下沉**：把 `knowSendDataButton_click` 的“切脚本 + 编码转换 + 发送”抽成 `MainViewModel.SendQuickItem(ToSendData)`，按钮点击与定时器共用。
- 单个基准 `DispatcherTimer`（如 10ms）遍历启用项，按各自间隔到点调用 `SendQuickItem`；窗口关闭时 `Stop()`。
- 串口未打开时的行为需定（跳过 / 缓存等串口打开 / 自动打开）。

**备选 B：全局定时**——一个间隔输入 + 起/停，周期性发送“主发送框内容”或“选中的一条”。

**影响文件**：`Model/ToSendData.cs`、`Model/Settings.QuickSend.cs`、`ViewModels/MainViewModel.cs`、`ViewModels/QuickSendViewModel.cs`、`View/MainWindow.xaml`（模板+表头）、`View/MainWindow.xaml.cs`、`languages/*`。

**待确认**
1. 采用 **A（每条独立定时）** 还是 **B（全局定时）**？
2. 最小间隔（10ms / 50ms / 100ms）？是否允许任意毫秒输入？
3. 串口未打开时：跳过 / 缓存 / 自动打开？
4. 是否需要“全部开始 / 全部停止”按钮？

---

## 12.9 语言键与单测

- 新增语言键：`SettingLogFontSizeRange`、`SerialSettingsCard`、`TimedSend`、`TimedSendInterval` 等（中英双语）。
- 复用（文件里仍在、上轮已无引用）：`ShowHexFormat`、`ShowHexFormatTip`、`SettingShowFormat`、`SettingShowBoth`、`SettingShoText`、`SettingShowHexOnly`。
- 单测：更新 `DataShowItemTests`（箭头方向）；若 12.5 引入格式设置，补充“字符串/HEX/两者”生成用例。

## 12.10 建议实施顺序与验证

- 建议顺序：**2 → 1 → 3 → 4 → 6**（低风险、互不冲突）→ **5**（显示逻辑）→ **7**（布局重构，单独提交）→ **8**（功能，最后）。
- 每个条目/条目组**独立提交**（中文信息），便于回退。
- 每步验证：`dotnet build llcom\llcom.csproj -c Release -p:Platform=x64` **0 错误**；
  `dotnet test tests\llcom.Tests\llcom.Tests.csproj -p:Platform=x64` 全绿；
  启动 `artifacts\Release\x64\llcom-y.exe` 冒烟（无串口设备时也能开）。
- 12.7/12.8 需人工 GUI 冒烟（见 `docs/03-test-checklist.md`），本机无法可靠截图，需你实测。

---

# 十三、第二轮实施记录（2026-10-14）

> 提交：`ebf47b1`（分支 `feature/refactor-continue`，未 push）。构建 x64 Debug/Release 0 错误；单测 28/28 通过。

**已实施（1~7 条）：**

1. **字号范围**：`Settings.Display` 新增 `LogFontSizeMin=10/Max=40`，`logFontSize` setter 内夹取；设置窗改为
   `Slider(10~40, 步进1)` + 数值显示。
2. **箭头对调**：`DataShowItem` 发送= `→`、接收= `←`；`DataShowViewModel.SaveLog()` 判定 send 的箭头同步改为 `" → "`；单测同步。
3. **输入框字号**：`MainViewModel` 暴露 `Setting`；`toSendDataTextBox` 绑定 `Setting.logFontSize`。仅主发送框（未跟随快捷发送行）。
4. **拖拽上下限**：主窗口发送区行 `Height=40 MinHeight=40 MaxHeight=240`，日志行 `MinHeight=120`；默认高度 40（不持久化拖拽结果）。
5. **收发显示切换**：新增 `sendShowHex` / `recvShowHex`（默认均为 HEX，无“两者”模式）；`DataShowItem` 支持切换并即时刷新
   （`Refresh()` + `INotifyPropertyChanged`，`DataShowViewModel` 订阅设置变化重算）。设置窗与数据区底部各加勾选框
   （“发送显示Hex”/“接收显示Hex”）。数据区底部由 `StackPanel` 改 `WrapPanel`，窄窗口自动换行。
6. **串口按钮悬停**：`DesignTokens` 新增 `PortToggleButton`（自定义模板，悬停/按下用叠加半透明黑加深，保持白字可读）；
   打开态（`IsPortComboEnabled=False`）切绿底白字。
7. **串口设置页**：顶栏移除串口控件，仅保留右对齐的 帮助/☰/语言；右侧 `TabControl` 首位新增“串口设置”页，
   含 刷新/端口/波特率/数据位/校验位/停止位/编码/自动重连/打开关闭（数据位·停止位·编码用 code-behind 处理器，
   与设置窗逻辑一致）。页序：串口设置 → 快捷发送 → 运行脚本 → 在线脚本 → 小工具 → 关于。
   另：全局 `CheckBox` 样式改为清晰的 ✓（覆盖主题默认外观）。

**本轮未做（按你的要求顺延）：**

- **第 8 条 定时发送**：仅保留在计划中（见 12.8），本轮未改。待你确定“每条独立定时 / 全局定时、最小间隔、未开串口行为”。

**需你实测确认的点：**

- 第 4 条默认高度 40 是否合适（可再调，值待定）。
- 第 5 条两个勾选框的位置/文案是否顺手（数据区底部 + 设置窗）。
- 第 7 条“串口设置”页是否要把设置窗里的同名项删掉（当前两处并存，绑定同一设置，互相同步）。
- 本机截图/自动化验证不稳定，以上 UI 效果请以你实际运行为准。

**回归修复（`a735c16`）：**

- 症状：无串口时点“发送”（会自动尝试打开串口并发送），日志区一旦出现条目就反复弹出
  “恭喜你触发了一个BUG！…无法对 'llcom.Model.DataShowItem' 类型的只读 'DataText' 进行 TwoWayToSource 绑定”。
- 原因：WPF 的 `Run.Text` **默认双向绑定**；第 5 条把 `DataText`/`RawText` 改成只读（私有 setter）后，
  条目模板里的 `<Run Text="{Binding DataText}">` 试图回写 → 抛 `InvalidOperationException`。
- 修复：`DataShowPage.xaml` 中该模板的 5 个 `<Run Text=...>` 全部显式 `Mode=OneWay`。
- 备注：全局异常处理 `App.SendReport` 未设 `e.Handled=true`，绑定异常在嵌套消息循环里会被反复触发，
  表现为“关一个又开一个”。若要更稳，可考虑在该处理器里对绑定类异常直接忽略（待确认是否要改）。

**行为变更（`7668f52`）：发送不再自动打开/选择串口**

- 原行为：`SendUartData` 在串口未开时会 `OpenPort()`（用下拉框当前选中项；从没选过时可能是默认的 COM1 或列表第一项），
  并把数据存入 `_pendingSendData`，等串口打开后补发 → 多端口下“替你随便挑一个端口发出去”，且可能延迟补发。
- 现行为：未打开串口时点 发送/快捷发送 → **不打开、不选端口、不跑脚本、不缓存**，仅弹文字提示「请先打开串口！」。
  新增 `MainViewModel.EnsurePortOpenForSend()`（主发送框与快捷发送共用）、语言键 `ErrorPortNotOpen`；
  删除 `_pendingSendData` 与 `OpenPort` 里的补发逻辑。
- 仍保留：`autoReconnect`（USB 拔插后自动重连**上次用过的**端口）——这是独立开关，未改动。

**崩溃处理改进（`027976b`）：**

- 现状问题：`DispatcherOnUnhandledException` 未设 `e.Handled=true`，异常继续冒泡；弹窗期间嵌套消息循环会反复触发同一异常
  → “关一个又开一个”；且没有“关闭程序”入口，只能进任务管理器强杀。
- 改法（`App.xaml.cs` 重写）：
  1. `e.Handled = true`，不再让进程被直接终止；`_isHandlingFatal` 重入保护，处理期间再抛错只记录不弹窗。
  2. 统一 `HandleFatal`：记录 `logs/fatal.log` → 弹 `CrashWindow`（**只有“关闭程序”一个按钮**，不提供“继续运行”）→ 尽力上报（`ReportCrash` 静默、后台）。
  3. `ExitApplication()`：`isMainWindowsClosed=true` → `Application.Shutdown()` → `Environment.Exit(0)` 兜底，**保证一定能退出**。
  4. 非 UI 线程异常（`AppDomain.UnhandledException`）无法继续，改为“记录 + 提示后退出”。
- 说明：`autoReconnect` 本就是设置项（默认开启，位于 设置窗 与「串口设置」页），未改动。
- 迭代（`94b9a3e`）：去掉“继续运行”，改为 **`CrashWindow` 只给“关闭程序”按钮**；错误详情可选中复制、显示日志文件路径。
  并临时在 ☰ 菜单加了「触发测试崩溃（临时）」入口，**验证后已删除**（`c8fc31c`）。
- 已实测（Release）：点测试入口 → 弹“关闭程序”窗 → 点击后进程正常退出；`artifacts\Release\x64\logs\fatal.log` 写入完整堆栈。
- 注意：`App.xaml.cs` 的异常处理注册在 `#if DEBUG` 的 `#else` 分支里，**Debug 构建不注册**，测试入口请用 Release 版。

**布局/设置整理（`60b9c0b`）：**

- 删除「更多设置」中与「串口设置」重复的项：自动重连、数据位、停止位、校验位、字符编码（含对应 code-behind 处理器与初始化）。
- 合并「发送Hex」与「发送显示Hex」为单一 **发送Hex**（复用 `hexSend`）：勾选=输入按 HEX 解析且发送数据显示 HEX，取消=字符串；
  接收侧 `recvShowHex`（接收显示Hex）保持不变。`Settings.sendShowHex` 与语言键 `SendHexView` 删除，`HexSendMode` 文案改「发送Hex」并加 ToolTip。
- RTS / DTR 从数据区底部移入「串口设置」页（`MainViewModel` 新增 `Rts`/`Dtr` 代理），并加 ToolTip；
  `HelpWindow` 新增「十二、RTS / DTR 详解」，同时更新过时说明（顶部工具栏→串口设置页、去掉“Hex 三态显示”等）。
- 单测：`DataShowItemTests` 改为按 `hexSend` 断言（新增字符串显示用例），共 29 个用例。

**文案/命名（`74d725b`）：**

- 「更多设置」窗口标题：`SettingTitle` 由“更多串口设置”改为“更多设置”。
- 设置窗首个页签 `SettingBasic`：“串口基本设置”→“更多串口设置”；`SettingSendLuaScript`→“发送脚本”；`SettingRecvLuaScript`→“接收脚本”。
- 设置窗移除与首页数据区重复的「接收显示Hex」（`recvShowHex` 仍由首页底部勾选控制）。

**置顶小问题（`23b5189`）：**

- 反馈：在「更多设置」勾选/取消“窗口保持置顶显示”时，设置窗口像是被“最小化”。
- 排查：代码里没有最小化逻辑（无 `WindowState`/`SW_MINIMIZE`）；用 UI 自动化切换后 `WindowVisualState=Normal`、`IsIconic=False`，无法复现真正的“最小化”。
  推测是主窗口变为置顶后被激活、把非子窗口的设置窗口压到了后面。
- 修复：`MoreSettingButton_Click` 里设置 `settingPage.Owner = this`（子窗口恒在主窗口之上）；`topEvent` 中置顶切换后若设置窗口可见则 `Activate()` 回到前台。
- 另：`HelpWindow` 第九节补充说明“窗口保持置顶显示”的作用。

**发送区三控件（`fca44cd`）：**

- **清除发送框**：发送按钮左侧第一个图标按钮（橡皮），清空 `SendText`。
- **发送记录**：下拉（动态生成，`SubmenuOpened` 时构建）：列出最近发送内容（最新在前，点击填回发送框）；底部有「记录上限（条）：N」带 －/＋。
  设置项 `sendHistory`（`List<string>`，持久化）与 `sendHistoryLimit`（默认 30，夹取 1~200）。
- **追加换行**：下拉替换原先的「末尾加回车换行」勾选（避免重复），选项 无追加/`\n`/`\r`/`\n\r`/`\r\n`；
  设置 `extraEnter`(bool) → `appendNewline`(int 0~4)，`MainViewModel.AppendNewlineBytes` 映射字节。
- 单测：`SendOptionsTests`（换行字节映射、范围夹取、JSON 往返），共 32 个用例。
- **顺延**：发送按钮右侧下拉（连续发送：发送后延时/重复次数；按Enter发送 / 按Ctrl+Enter发送）——与快捷发送的“定时发送”一并后续再做。

**发送区修复（`c9f7556`）：**

- 清除发送框后 UI 不刷新：`SendText` 是普通属性，代码里改 `setting.dataToSend` 不通知 View；在 `MainViewModel` 订阅 `setting.PropertyChanged` 的 `dataToSend` → `OnPropertyChanged(nameof(SendText))`。
- 发送记录点不开：`MenuItem` 无子项时 WPF 不认为有子菜单，`SubmenuOpened` 不触发；改用「图标按钮 + `ContextMenu`」（`SendHistoryButton_Click` 打开、`Opened` 时构建），并给两个按钮加 `AutomationProperties.Name`。
- 布局：发送记录由会拉伸的 `Menu` 改为 32×32 的 `FlatIconButton`。
- 已用 UI 自动化验证：清除后文本框即时变空且 settings.json 的 `dataToSend` 同步；发送记录下拉能展开并列出历史项。

**发送区样式微调（`ca944bd`）：**

- 清除/发送记录图标放大到 `FontSize=16`；发送记录按钮改用与其它按钮相同的 `IconButton`（带边框），去掉小箭头。
- 清除/记录/换行/发送 四个控件统一 `VerticalAlignment="Center"`，不再随发送框高度拉伸。
- 顺带修：改用带边框按钮后，点击“发送记录”会弹出后立刻被同一次点击关掉 → 改为 `Dispatcher.BeginInvoke` 延迟打开（已用自动化验证可展开）。

**按钮颜色/高度统一（`c0e8c80`）：**

- 根因：`DesignTokens.IconButton` 样式没有 `BasedOn`，导致显式样式**丢弃了主题按钮外观**、退回系统(Aero)外观 → 比主题按钮偏灰。
- 修复：`IconButton` 加 `BasedOn="{StaticResource {x:Type Button}}"`（全应用的图标按钮一并变回主题外观）；发送区的下拉与发送按钮设 `Height=32`，与 32×32 图标按钮高度一致（宽度各自独立）。

**底部开关图标化（`ad7e273`）：**

- 数据区底部一行改为紧凑控件（均有 tooltip）：`Abc/Hex` 胶囊（发送）、`Abc/Hex` 胶囊（接收显示）、眼睛图标（替换不可见字符）、暂停/继续图标（停止打印）、锁图标（锁定视图）。
- 「锁定视图禁止滚动」由原来“悬停才显示、在日志区右侧”的浮动按钮**移入底部栏**（停止打印之后）；`SaveLogButton` 仍保留悬停浮动。
- `DesignTokens` 新增 `LogBarButton`（28×28 图标按钮）、`LogBarIconToggle`（28×28 图标开关）、`LogBarToggle`（Abc/Hex 胶囊，选中态高亮）。
- `HelpWindow` 第三节同步更新为图标说明。

**发送模式提示与兜底（`40fd7eb` → `7dee33f`）：**

- Abc/Hex 胶囊的 tooltip 随状态变化：「发送信息 - 字符串 / 十六进制」「接收信息 - 字符串 / 十六进制」（`SendHexToggleStyle` / `RecvHexToggleStyle`）。
- **不做“像不像”判断、不做模式不符提示**（按用户意见：靠用户看到发送内容与输入不一致自行切换）。
  `IsHexLike` 启发式已删除；仅保留最小兜底 `MainViewModel.HexModeBytes`：Hex 模式下**十六进制解析结果为 0 字节**时（如输入 `test`）
  直接按原字符串发送，避免“什么都没发出去”；该兜底只在解析完全失败时生效，不会覆盖一次成功的十六进制解析。

**Hex 模式发送规则修正（`b34bfb0`）：**

- 上一版“仅在解析结果为 0 字节时兜底”有漏洞：`03kkkkk` 会被 `Hex2Byte` 啃成只剩 `03`（丢字符）。
- 改为：`HexModeBytes` = **整串是合法十六进制 → 按十六进制解析；否则 → 整串按字符串发送**。
  判定 `IsValidHexText`：去掉空白/分隔符/`0x` 后必须全部是十六进制字符且长度为偶数。
- 字符串模式不变（诚实按字符串发送）；仍无任何“提示”。
- 单测：`0303`/`0A 0B` → 正常解析；`test`、`03kkkkk` → 原样字符串；`IsValidHexText` 用例含 `03kkkkk=false`。

**改为“按钮决定，不做内容判断”（`fcc2906`）：**

- 用户意见：内容判断有“把 Hex 误判成字符串”的风险 → 去掉 `IsValidHexText` 等全部内容判断。
- 最终规则：**发字符串还是发 Hex 完全由「Abc/Hex」按钮决定**——字符串模式原样发送；Hex 模式一律 `Hex2Byte` 解析。
- 已知后果（如实输出，用户自行切换模式）：`0303`→`03 03`；`0A 0B`→`0A 0B`；`test`→0 字节（不发）；`03kkkkk`→只剩 `03`。

**底部栏整合（`f6d361a`）：**

- 「清空日志」「保存日志」统一到数据区底部栏，顺序为：`发送Hex / 接收显示Hex / 替换不可见字符 / 停止打印 / 锁定视图 / 清空日志 / 保存日志`（均有 tooltip）。
- 移除原来“悬停才在日志区右侧浮出”的保存按钮；`☰` 菜单里的「清空日志」也一并移除（避免重复），`☰` 现在只剩「更多设置」。
- `HelpWindow` 相关说明同步更新。

**☰ 直达设置（`55ef059`）：**

- 顶栏 `☰` 由“只含一项的菜单”改为普通图标按钮，点击**直接打开「更多设置」**（`FlatIconButton`，tooltip 为「更多设置」）。

**排版优化（`3db0a7e`）：**

- 「串口设置」页改为表单式 `Grid`：标签列统一宽度（Auto, MinWidth 84），右侧下拉统一对齐、统一高度 28；端口行改为 刷新按钮 + 拉伸下拉。
- 「在线脚本」列表项重排：作者（左）/脚本名（右，加粗）、分隔线、版本（左，`v{0}`）/简介（右），各占独立列并 `TextTrimming="CharacterEllipsis"`，修复文字重叠/溢出。
- 列表项微调（`d41451f`）：作者列改 `Auto`（MaxWidth 120）让**脚本名占剩余宽度**（减少截断）；作者/脚本名/简介加 `ToolTip` 悬停看全文。

**定时发送（`5ce534c`）：**

- **快捷发送·每条独立定时**：`ToSendData` 增 `timerEnabled` / `timerIntervalMs`（默认 1000，使用时限 10~3600000）；列表增「定时」勾选 + 「间隔ms」两列（表头同步）。
- **主发送框·连续发送**：发送按钮右侧「▾」打开选项菜单：连续发送（开关）、发送后延时(ms)（±100）、重复次数（±1，-1=一直重复）、按 Enter / 按 Ctrl+Enter 发送。
  设置项 `continuousSend` / `continuousDelayMs` / `continuousRepeat` / `sendByEnter`；主发送框 `PreviewKeyDown` 支持 Enter 发送（Shift+Enter 换行）。
- **调度**：单个 `DispatcherTimer`(10ms) 扫描；快捷发送按各自间隔、连续发送按延时；串口未打开时有到期任务 → 提示「请先打开串口！」并**停止全部定时**（逐条取消勾选）。
- 发送逻辑下沉 `MainViewModel.SendQuickItem`（按钮与定时共用）；`StopContinuousSending` / `StopAllTimedSending` 用于停止。
- 单测：`NormalizeInterval` 夹取、`continuous*` 夹取、`ToSendData` 定时字段 JSON 往返（共 39 用例）。
- 实测：串口未开时勾「定时」→ 弹提示 → 关闭后所有定时勾选自动取消。

**定时 UI 折叠 + 语义说明（`97ead45`）：**

- 快捷发送的定时设置由「常显两列（勾选 + 间隔输入框）」改为**每行一个时钟图标下拉**（和主发送框一样折叠）：
  菜单内「定时」开关（勾选态与图标变色表示启用）+「间隔(ms)」的 －/＋（步长 100，10~3600000）。
- 修正说明（非 bug）：主发送框「重复次数」= **首次发送之后**重复的次数 → 填 3 = 共 4 次；填 0 = 仅 1 次；填 -1 = 无限。
  已在菜单项加 tooltip 并写入使用说明。
- 实测：快捷发送定时的 －/＋ 与主发送框延时 －/＋ 均能改值并持久化到 settings.json。

**事故与修复 + 键盘输入（`5411794`）：**

- 事故：我用 PowerShell 直接改 `artifacts\Release\x64\settings.json`（`Get-Content -Raw` 默认 ANSI 读 → `WriteAllText` 按 UTF-8 写）导致中文被破坏、JSON 非法 → 程序启动报「配置文件加载失败」。
  用 `settings.json.bakup`（程序自动备份，当时完好）恢复并校验可解析，程序已正常打开；期间测试改动的 `continuousSend/continuousDelayMs/continuousRepeat/timerIntervalMs` 已复位。
  **教训：不要用 PowerShell 直接改 settings.json（编码陷阱）；要改也应显式 `[System.IO.File]::ReadAllText/WriteAllText(path, Encoding.UTF8)`。**
- 键盘输入：主发送框「▾」里的「发送后延时(ms)」「重复次数」、快捷发送每行的「间隔(ms)」都改为**可编辑文本框**（± 仍保留）；
  失焦时校验并夹取（延时 10~3600000，重复 -1~100000，间隔 10~3600000），非法输入则还原。
  相应把 `ToSendData.timerIntervalMs` / `Settings.continuousDelayMs` / `continuousRepeat` 的 setter 去掉夹取，改为使用时/失焦时夹取。
- 实测：在菜单里聚焦输入框键入 `555`，失焦后 `settings.json` 的 `continuousDelayMs` 变为 555（说明菜单内可真实键盘输入）。
- 单测：`ContinuousSettings_AreClamped` → 改为 JSON 往返（夹取逻辑移到 UI/使用时）。

**取值范围写进说明（`65dfa2c`）：**

- 使用说明第四节：补充「发送后延时」10~3600000 ms（± 步长 100）、「重复次数」-1~100000（-1=无限），均可键盘输入、失焦校验夹取。
- 使用说明第五节：补充快捷发送「间隔(ms)」10~3600000（± 步长 100）、可键盘输入。

**使用说明全面校对（`353d250`）：**

- 发送区：补「记录上限（条）」范围 1~200；发送区高度限制 40~240；「是否按 HEX 发送」改为指向日志区底部「Abc/Hex（发送）」开关。
- 快捷发送：修正标题「右侧第二个标签」（原写第一个）、页签图标描述（便签图标，原写 ☰）。
- 串口设置：新增校验位「高电平/低电平」= Mark/Space 校验的说明。
- 更多设置：标题改为「点顶栏 ☰ 直接打开」，并列出三个页签。
- 在线脚本：补充“过长截断、悬停看全文”。
- 快速上手：补充「可在发送选项里改为按 Enter 发送」。
- 其他：补充崩溃时弹「关闭程序」窗口并写 `logs/fatal.log`。