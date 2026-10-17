# 11 · 界面/交互修复计划（问题 9–15）

> 状态：**部分已完成**（2026-10-17 制定 / 同日实施 9、11、12、13、14、15；10 仍待调查）
> 范围：问题 **9、11、12、13、14、15** 已实现；**10（串口监听）按你的要求放最后**，本轮只做调查与方案，不动代码。
> 约定：中文界面、控件紧凑；**每个纯图标控件必须有悬停提示**；Help 文档随改动同步。

## 0.1 实施记录（2026-10-17）

| # | 状态 | 提交 / 验证 |
|---|---|---|
| 9 | ✅ 完成 | `bde6924` + `c16e599`（悬浮触发器改为 MouseEnter/Leave）。截图验证：移入淡入、移出隐藏 |
| 11 | ✅ 完成 | `bde6924`。UIA 验证：插头按钮位于发送按钮左侧，随串口状态变色 |
| 12 | ✅ 完成 | `b57a398`。UIA 验证：输入 `3→5`、`999→200`、`30→30`，并持久化 |
| 13 | ✅ 完成 | `b57a398`。注入 30 条后弹层列表固定 240px 内滚动，仅实例化 12 项，底栏始终可见 |
| 14 | ✅ 完成 | `b57a398`。点击清空后列表为空、空状态出现、`settings.sendHistory` 持久化为 `[]` |
| 15 | ✅ 完成 | `c16e599`。压测 900 包：日志仅实例化 12 项（虚拟化生效），Hex 切换 52ms / 切回 7ms，清空 20ms |
| 15b | ✅ 完成 | 日志缓冲上限：条数（默认 5 万，范围 1000~20 万）+ 32 MB 负载预算 + 流式字符上限（默认 200 万）。单测覆盖裁剪，运行时 1500 包无异常 |
| 10 | 🔧 已修复“静默失败” | 定位到 2 个根因（位数不一致 / 端口在监听前已打开），见第 5 节；跨位数监听仍需 x86 版 LLCOM |

---

## 0. 总览

| # | 问题 | 类型 | 影响文件（主要） | 风险 |
|---|---|---|---|---|
| 9 | 帮助/更多设置/语言 三按钮移到日志底栏右侧，仅悬浮显示 | 布局 | `View/MainWindow.xaml` | 低 |
| 11 | 发送按钮左侧增加「开/关串口」 | 布局 | `View/MainWindow.xaml` | 低 |
| 12 | 发送记录上限支持键盘输入（5~200） | 功能 | `MainWindow.xaml(.cs)`、`Settings.Uart.cs` | 低 |
| 13 | 发送记录列表固定高度、可滚动（不再挤掉上限行） | 功能 | `MainWindow.xaml(.cs)` | 中 |
| 14 | 发送记录增加「清空」按钮 | 功能 | `MainWindow.xaml(.cs)`、`MainViewModel.cs` | 低 |
| 15 | 日志量大时切换字符串显示/拖拽分隔条卡顿 | 性能 | `Pages/DataShowPage.xaml(.cs)`、`ViewModels/DataShowViewModel.cs`、`Model/DataShowItem.cs` | 中 |
| 10 | 串口监听无数据 | 待调查 | `Pages/SerialMonitorPage.xaml.cs` | 后置 |

建议提交拆分（每步可单独回退、单独编译验证）：
1. `fix(ui): 三按钮并入日志底栏右侧并改为悬浮显示 (#9)`
2. `feat(ui): 发送区新增串口开关 (#11)`
3. `feat(ui): 发送记录改为固定高度弹层，支持输入上限与清空 (#12 #13 #14)`
4. `perf(log): 日志列表真正虚拟化 + 懒格式化 + 合并滚动 (#15)`
5. `fix(monitor): … (#10，最后单独做)`

---

## 1. 问题 9：三按钮移入日志底栏右侧 + 悬浮显示

### 现状
- `MainWindow.xaml` 第 0 行（`Height="Auto"`）只放了一个右对齐 `StackPanel`：`HelpButton`（❓）、`MoreSettingButton`（☰）、语言 `Menu`（🌐）。
- 日志底栏在 `Pages/DataShowPage.xaml` 第 74–168 行：`WrapPanel` 左对齐，依次是 Abc/Hex（发送）、Abc/Hex（接收）、替换不可见字符、停止打印、锁定、清空日志、保存日志。
- 两者不在同一个可视容器里（底栏在 `Frame → DataShowPage` 内部）。

### 方案
1. **删除顶部工具栏行**：删掉 `Grid.Row="0"` 的 StackPanel 与 `RowDefinition Height="Auto"`，行号整体上移：
   - 日志 `Frame`：Row 0；分隔条 Row 1；发送区 Row 2；状态栏 Row 3。
   - 给内容补回原来的上边距：`dataShowFrame Margin="8,6,0,0"`、右侧 `TabControl Margin="0,6,0,0"`。
2. 在 `Frame` **之后**（保证覆盖在日志上方）加一个「悬浮热区」+ 三按钮，位置＝日志底栏同一行、右对齐：
   ```xml
   <Grid Grid.Row="0" Grid.Column="0" x:Name="LogCornerZone"
         Height="32" Margin="0,0,10,3" HorizontalAlignment="Right" VerticalAlignment="Bottom"
         Background="Transparent">
     <StackPanel x:Name="LogCornerTools" Orientation="Horizontal"
                 HorizontalAlignment="Right" VerticalAlignment="Center">
       <!-- HelpButton / MoreSettingButton / 语言 Menu 原样搬入（名字不变，code-behind 无需改） -->
     </StackPanel>
     <Grid.Style>…悬浮时显示/隐藏…</Grid.Style>
   </Grid>
   ```
3. **悬浮显示/隐藏**（只用 0 宽度区域做触发，不挡日志内容）：
   - `LogCornerTools` 默认 `Opacity=0`、`IsHitTestVisible=False`。
   - 用 `Style` + `DataTrigger Binding="{Binding IsMouseOver, ElementName=LogCornerZone}" Value="True"` 切换为 `Opacity=1 / IsHitTestVisible=True`；
   - 显隐各加一个 `DoubleAnimation`（显示 0.12s、隐藏 0.2s），避免生硬闪动。
   - 热区宽约 130–150px（仅覆盖三按钮范围），不会遮住左侧的日志开关按钮。
4. 三个控件已有 `ToolTip`（使用说明 / 更多设置 / 语言），保持不动。

### 验收
- 顶部不再有独立工具栏，右上角不再是常驻按钮；
- 鼠标移到日志区右下角（底栏同一行）时三按钮淡入，移开淡出；
- 隐藏状态下点击该角落**不会**误触发帮助/设置/语言；
- 语言切换、帮助、设置功能正常；窗口拉宽/拉窄后仍贴右下角。

### 备注
- 因为 `DataShowPage` 是 `Frame` 承载的独立 Page，把按钮放进 Page 需要迁移事件处理；这里采用「MainWindow 覆盖层」方案，改动最小、行为等价。

---

## 2. 问题 11：发送按钮左侧增加「开/关串口」

### 现状
- 发送区（`MainWindow.xaml` 第 118–317 行）右侧 `StackPanel`：`sendDataButton`（发送，80×32）+ `sendOptionButton`（▾，24×32）。
- 开关串口目前只在右侧「串口设置」页底部（`openClosePortButton`，第 574–585 行），需要切标签页。

### 方案
在 `sendDataButton` **之前**（同一个 `StackPanel` 内）加一个 32×32 图标按钮，复用 `IconButton` 样式与 `OpenClosePortCommand`：

```xml
<Button Name="sendAreaPortButton" Width="32" Height="32" Margin="8,0,0,0"
        Style="{StaticResource IconButton}"
        Command="{Binding OpenClosePortCommand}">
  <Grid>
    <fa:FontAwesome Icon="Plug" FontSize="14"
                    Foreground="{DynamicResource Brush.State.Success}"
                    Visibility="{Binding IsPortOpen, Converter={StaticResource boolNotVisibeConverter}}" />
    <fa:FontAwesome Icon="Plug" FontSize="14"
                    Foreground="{DynamicResource Brush.State.Danger}"
                    Visibility="{Binding IsPortOpen, Converter={StaticResource boolVisibeConverter}}" />
  </Grid>
  <Button.ToolTip>
    <StackPanel Orientation="Horizontal">
      <TextBlock Text="{Binding OpenCloseButtonText}" />
      <TextBlock Text="{DynamicResource OpenPort_port}" />
    </StackPanel>
  </Button.ToolTip>
</Button>
```

- 语义：**未打开**（绿色 Plug，点击=打开）；**已打开**（红色 Plug，点击=关闭）；提示文字自动变成「打开串口 / 关闭串口」。
- 「串口设置」页里的原按钮**保留**，两处绑定同一命令，状态天然同步（`IsPortOpen`/`OpenCloseButtonText` 已是 `ObservableProperty`）。
- 窄窗口（最小宽 900）下仍需保证发送框不被挤没：必要时把 `sendAreaPortButton` 的 `Margin` 收到 6px。

### 验收
- 不切标签页就能开/关串口；图标颜色与提示文字随状态变化；
- 状态栏圆点、串口设置页按钮、本按钮三者状态一致；
- 打开/关闭失败时仍走原有提示逻辑。

---

## 3. 问题 12 / 13 / 14：发送记录（上限输入 · 固定高度滚动 · 清空）

> 说明：这里的「记录条数」指**发送记录下拉**（`sendHistoryLimit`，默认 30），不是串口日志条数。

### 现状
- 触发按钮：`sendHistoryButton`（🕘），绑定一个 `ContextMenu`（`MainWindow.xaml` 第 155–173 行）。
- 内容在 **code-behind 动态拼**（`MainWindow.xaml.cs` 第 466–543 行）：
  - 每个历史项一个 `MenuItem`；
  - `Separator`；
  - 最后一个 `MenuItem` 里放 `－ / N / ＋`，**现在只能点按钮加减**；
  - 菜单高度随历史条数增长 → 条目多时被屏幕边缘裁掉，**底部的上限行被挤出可视区**（问题 13 的现象）。

### 方案：把 `ContextMenu` 换成固定尺寸的 `Popup`
在 `MainWindow.xaml` 里声明（放在发送区 Grid 内即可）：

```
Popup(sendHistoryPopup)
 ├─ Border（主题底色 + 1px 边框，宽 320，圆角/内边距统一）
 │   ├─ 标题行：发送记录
 │   ├─ ListBox(sendHistoryList)  ← MaxHeight=240，超出滚动，固定宽度，文本单行截断
 │   ├─ 空状态 TextBlock（无记录时显示「（无发送记录）」）
 │   └─ 底栏（固定，不会被挤掉）
 │        ├─ 「记录上限」+ TextBox(50px，键盘输入) + 「5~200」
 │        └─ 「清空记录」按钮（右对齐）
```

要点：
1. **固定高度可滚动**：`ListBox` 设 `MaxHeight="240"`、`ScrollViewer.VerticalScrollBarVisibility="Auto"`、`VirtualizingStackPanel.IsVirtualizing="True"`；底栏固定 `Height="Auto"` 在下方 → 无论多少条，上限/清空永远可见。
2. **键盘输入上限（问题 12）**：
   - `historyLimitBox` 只接受数字：`PreviewTextInput` 过滤非数字；粘贴也过滤（`DataObject.Pasting`）。
   - `Enter` 或 `LostFocus` 应用：`int.TryParse` → **clamp 5~200** → 写入 `_vm.SendHistoryLimit` → 把文本框回写为合法值；非法输入回退到当前值。
   - 保留 `－/＋` 两个按钮作为快捷微调（可选，建议保留）。
3. **数据源**：`ItemsSource="{Binding SendHistory}"`（`ObservableCollection<string>`），项模板用 `TextBlock` + `TextTrimming="CharacterEllipsis"`、`ToolTip="{Binding}"`（悬停看完整内容）；新增 `SendHistoryPreviewConverter` 把多行压成单行并截断 ~60 字。
4. **点击项**：`ListBox.PreviewMouseLeftButtonUp` → 取 `DataContext` 写回 `_vm.SendText` 并关闭 Popup（比 `SelectionChanged` 可靠，重复点同一项也生效）。
5. **清空（问题 14）**：新增 `MainViewModel.ClearSendHistory()`：
   - `SendHistory.Clear()`；`Tools.Global.setting.sendHistory = new List<string>()`（走既有 `Save()` 持久化）。
   - 清空后列表立即变空、空状态文案出现；上限值不变。
6. **打开时**：`Opened` 事件里把 `historyLimitBox.Text` 同步为当前值、焦点放到列表；`Esc` 关闭。
7. **移除旧实现**：删除 `SendHistoryContextMenu_Opened`、`SummarizeHistory`、`sendHistoryContextMenu` 与 `SendHistoryButton_Click` 中的菜单打开逻辑。

### 配套改动
- `Model/Settings.Uart.cs`：`sendHistoryLimit` 的 clamp 从 **1~200** 改为 **5~200**（注释同步）。
- `tests/llcom.Tests/SendOptionsTests.cs`：断言 `0 → 5`、`9999 → 200`。
- 语言资源新增：`ClearSendHistory`（清空记录 / Clear history）、`SendHistoryEmpty` 复用。
- `HelpWindow.xaml`：把「记录上限…范围 1~200」更新为「**5~200，可直接键盘输入；列表固定高度可滚动；可一键清空**」。

### 验收
- 以 20 条记录验证：弹层高度稳定（列表最多 240px 内滚动），底栏始终可见；
- 输入 `3` 回车 → 变成 `5`；输入 `999` → `200`；输入 `abc` → 回退；
- 上限调小后列表立即裁剪；「清空记录」后列表为空且重启后仍为空；
- 点历史项填回发送框并关闭弹层；`Esc`/点外部关闭。

---

## 4. 问题 15：日志量大时切换显示 / 拖拽卡顿

### 现状（已定位，五个叠加原因）
1. **列表其实没有虚拟化**：`DataShowPage.xaml` 第 35–73 行把 `ItemsControl` 放在**外层 `ScrollViewer`** 里。WPF 虚拟化要求滚动宿主是 `ItemsControl` 自身模板的一部分；外层 `ScrollViewer` 会让 `VirtualizingStackPanel` 拿到无限高度 → **所有条目全部实例化**。
2. **每条都是 `RichTextBox` + `FlowDocument`**（第 53–69 行）。几千条 = 几千个重量级编辑器控件（各自带文档、光标、选择、拼写等），布局/测量成本极高。拖拽分隔条会触发全量重新测量 → 明显卡顿。
3. **格式切换是 O(N) 重算**：`DataShowViewModel` 构造里（第 69–79 行）监听 `hexSend`/`recvShowHex`，`foreach (var it in Items) it.Refresh()`；而 `DataShowItem.Refresh()`（第 118–126 行）**同步重算全部字符串**（`Byte2Hex`/`Byte2Readable`，后者逐字节构造 `List<byte>`）。条目越多越慢，切换「字符串显示」时最明显。
   另外：`EnableSymbol`（替换不可见字符）与 `encoding` **没有**触发刷新 → 现有条目不会更新（顺带修的 bug）。
4. **每条都请求滚动**：`Items.Add` 后立即 `ScrollRequested?.Invoke()` → `ScrollToEnd()`，等于**每条数据一次布局**（高波特率下持续抖动）。
5. **跨线程同步等待**：`Logger_DataShowTask` 用 `Application.Current.Dispatcher.Invoke`（同步）→ 串口接收线程被 UI 阻塞，UI 忙时进一步加剧卡顿。

### 方案（按收益排序）

**A. 真正启用虚拟化（核心）**
- `DataShowPage.xaml` 用 `ListBox` 替换「外层 ScrollViewer + ItemsControl」：
  - `VirtualizingPanel.IsVirtualizing="True"`、`VirtualizingPanel.VirtualizationMode="Recycling"`、`VirtualizingPanel.ScrollUnit="Pixel"`、`VirtualizingPanel.CacheLength="1,1"`；
  - `ScrollViewer.CanContentScroll="True"`、`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`；
  - `ItemContainerStyle`：去掉选中高亮/焦点框、`HorizontalContentAlignment=Stretch`、`Focusable=False`、`Padding=0`。
- code-behind 相应替换：`MainListScrollViewer.ScrollToEnd()` → `MainList.ScrollIntoView(MainList.Items[MainList.Items.Count - 1])`；显示模式切换只切 `MainList` / `MainTextBox` 的 `Visibility`。
- 预期：无论 1 万还是 10 万条，界面上只保留「可视区 + 1 屏缓存」的控件。

**B. 懒格式化 + 缓存（消除切换开销）**
- `Model/DataShowItem.cs`：
  - 构造时**不再**立即 `Format()`；
  - `DataText` / `RawText` 改为「按需计算 + 缓存」，缓存键 = `(hexSend/recvShowHex, EnableSymbol, encoding)`；
  - `Refresh()` 只做「清缓存 + 触发 PropertyChanged」，**不做字符串转换**。
- `DataShowViewModel` 的刷新触发条件补上 `EnableSymbol`、`encoding`（保持与 `hexSend`/`recvShowHex` 一致）。
- 预期：切换显示时 O(N) 只发生在「发通知」，真正的字符串转换只对**当前可见的几十条**执行。

**C. 合并滚动 + 不阻塞接收线程**
- `DataShowViewModel`：
  - `Dispatcher.Invoke` → `Dispatcher.BeginInvoke(DispatcherPriority.Background, …)`（保持顺序、让输入优先）；
  - `ScrollRequested` 增加「已排队」标记：一帧内无论来多少条，只滚动一次。
- 预期：拖拽和打字不再被日志刷屏抢占。

**D. 如果仍有卡顿（兜底）**
- 把行模板从 `RichTextBox` 换成 `TextBlock` + 多个 `Run`（轻量得多）。
- 代价：**失去鼠标选中/复制**。若采用，需在右键菜单补「复制此行」，并在 Help 说明。
- 先用 A+B+C 实测，仍不达标再启用 D（不默认做，避免功能回退）。

### 验证
- **压测方法（无需真实串口）**：本机开一个 PowerShell TCP 服务端，持续向客户端发固定长度数据包（2000+ 包）；用「TCP 客户端」页连接 → 日志产生大量条目；然后：
  1. 切换接收 Abc/Hex：应无明显停顿（< 1 帧的可感知卡顿）；
  2. 上下拖拽发送框分隔条、左右拖拽右侧面板分隔条：应流畅；
  3. 向上滚动查看历史、停留时新数据仍能追加；
  4. 「锁定视图」行为不变。
- 自动化：`DataShowItemTests` 增加「切换设置后 `Refresh()` 返回新格式」「缓存命中不重复计算」用例；`SendOptionsTests` 更新 clamp 断言。
- 无真实串口环境：最终流畅度请你在实机长日志下再确认一次。

---

## 4.1 日志缓冲上限（2026-10-17 追加）

虚拟化只解决渲染开销，`Items` 与流式文本仍会无界增长（115200 波特约 400–500 MB/小时）。追加三层保护：

| 缓冲 | 上限 | 设置项 | 说明 |
|---|---|---|---|
| 分包列表 `Items` | 条数 1000–200000（默认 50000） | 更多设置 → 日志保留上限（条） | 达到后从头部逐条丢弃 |
| 分包负载 | 32 MB（固定） | — | 大包时先生效，防止 4 KB/包把内存吃满 |
| 流式文本 | 100000–10000000 字符（默认 2000000） | 更多设置 → 流式保留字符数 | 缓冲与 TextBox 同步裁头 |

- **优先级**：条数与字节预算都是硬限制，谁先到谁生效；小包时通常是条数上限先生效（每行还有 ~250 B 固定开销），大包时是 32 MB 预算先生效。
- **实现要点**：WPF 的 `ListCollectionView` **不支持范围删除通知**（会抛 `NotSupportedException: 不支持范围操作`），所以裁剪用逐条 `RemoveAt(0)`；留 100 条余量再裁，减少集合变更次数。
- **流式裁剪**：超过上限 10% 才裁，`StringBuilder.Remove(0,n)` + 文本框 `Text = Text.Substring(n)`（会触发一次重排，故不宜频繁）。
- **测试**：`DataShowBufferTests`（条数上限 / 32 MB 预算 / 流式字符上限）+ `SettingsTests`（默认值与夹取）。

## 5. 问题 10（串口监听）——已定位并修复“静默失败”

### 5.1 复现与结论（2026-10-17）

用两个自建测试程序（x86/x64 各一个，打开 COM5 并周期性写入）逐一验证：

| 场景 | 结果 |
|---|---|
| x64 目标 + **先开始监听、后打开串口** | ✅ 正常捕获（日志出现收发记录） |
| x64 目标 + **先打开串口、后开始监听** | ❌ 零数据（hook 拿不到已存在的句柄） |
| x86 目标 + x64 版 LLCOM | ❌ `MonitorComm` 返回成功，但目标进程里**没有** `serial_monitor_hook.dll`，零数据（静默失败） |

两个根因：
1. **位数不一致**：x64 的 hook 只能注入 64 位进程；Rust 版 `inject_dll` 只等待远端线程、不检查 `LoadLibraryW` 结果，所以仍返回成功 → 界面显示“停止监听”却没有数据。
2. **端口在监听前已打开**：hook 只在目标调用 `CreateFileW` 时登记 COM 句柄，已经打开的句柄无法映射 → 必须**先开始监听，再让目标程序打开串口**。

### 5.2 本轮修复（C# 侧，未改 Rust DLL）

- 启动前用 `IsWow64Process` 检查目标位数，不一致时明确提示并**拒绝启动**（不再假成功）；
- `MonitorComm` 返回 true 后，用目标进程模块列表校验 `serial_monitor_hook.dll` 是否真的加载（仅 x64 版），失败则自动停止并提示；
- `MonitorComm` 返回 false / 抛异常时都有明确提示（权限不足、安全软件拦截、目标已退出）；
- 回调按所选 COM 端口过滤（Rust DLL 实际会转发目标进程的**所有**串口）；
- 页面底部常驻提示：“先开始监听再打开串口 + 位数需一致”；
- 帮助文档同步，删掉已过时的“仅 32 位 x86 版本可用”。

### 5.3 仍未解决（需要 Rust 工具链或额外工程）

- **目标先打开串口**的场景：需要枚举目标已有句柄并写入 hook 的 COM 映射（Rust 侧改动）；
- **32 位目标**：实测 x86 版（旧闭源 32 位 DLL）会**让被监听的目标程序崩溃**（同一目标独立运行 12 秒正常，一监听就在打开串口后退出），因此当前不建议使用 x86 版监听，提示文案也已改为"32 位目标暂不支持"；要真正支持需用 Rust 重建 i686 hook（或加 32 位注入助手进程）；
- 目标使用 `ReadFileEx`/完成端口等非常规 I/O 时可能仍抓不到（hook 覆盖的是 `ReadFile/WriteFile/GetOverlappedResult`）。

## 6. 不做的事（本轮边界）
- 不改串口收发协议、不改 HEX/字符串判定逻辑；
- ~~不引入日志条数上限~~（已在 4.1 完成）；
- 不动 `serial_monitor_rs` 的 Hook 实现（只可能补文档/提示）；
- 不调整窗口整体布局比例（除第 9 条删掉顶部工具栏行）。

---

## 7. 完成后同步
- `HelpWindow.xaml`：新增「发送区串口开关」「三按钮悬浮位置」「发送记录：键盘输入 5~200 / 固定高度 / 清空」；
- `docs/README.md` 索引保持最新；
- 构建：`dotnet build llcom\llcom.csproj -c Release -p:Platform=x64`；
- 测试：`dotnet test tests\llcom.Tests\llcom.Tests.csproj -p:Platform=x64`（39+ 用例）。
