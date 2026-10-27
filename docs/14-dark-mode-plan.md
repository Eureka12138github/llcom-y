# 黑暗模式可行性与实施计划

> 状态：**仅计划，未修改代码**。  
> 结论：**可行，难度中等**。基础主题切换不难，但要做得不“半黑半白”，主要工作量在自定义配色、日志/数据区、Lua 编辑器和 ScottPlot 图表。

## 1. 现状盘点

### 1.1 有利条件

- 项目使用 **AdonisUI**，NuGet 包内同时包含 `ColorSchemes/Light.xaml` 和 `ColorSchemes/Dark.xaml`，标准控件（窗口、按钮、下拉框、Tab、Menu、ToolTip、滚动条等）可以直接复用官方深色方案。
- App 的自定义画刷资源目前基本都通过 **`DynamicResource Brush.*`** 使用，审计中没有发现自定义画刷的 `StaticResource` 引用，运行时切换理论上可以即时生效。
- 语言切换已经有“替换 MergedDictionaries”的成熟模式（`LocalizationService`），主题可以复用同样的思路。
- 大部分页面（MainWindow、SettingWindow、QuickSend、About、工具页）都使用统一的 `Styles/DesignTokens.xaml` 资源，不是逐页硬编码颜色。

### 1.2 需要额外处理的地方

| 位置 | 现状 | 深色模式需要做的事 |
|---|---|---|
| `App.xaml` | 固定合并 `AdonisUI/ColorSchemes/Light.xaml` | 运行时替换为 Light/Dark |
| `Styles/DesignTokens.xaml` | 约 20 个 `Brush.*` 颜色是浅色硬编码（白底、黑字、浅灰边框） | 拆出 Light/Dark 两套调色板，样式与颜色分离 |
| `Model/DataShowItem.cs` | 收发色用 `Brushes.DarkRed / DarkGreen` | 改用主题色/触发式绑定；主题切换时刷新已有条目 |
| `Pages/SerialMonitorPage.xaml.cs` | `Brushes.Black / DarkRed / DarkGreen` | 改为主题色 |
| `Pages/WinUSBPage.xaml.cs` | `Brushes.DarkRed / DarkGreen` | 同上 |
| `Tools/NetworkLog.cs` | `Brushes.DarkRed / DarkGreen` | 同上 |
| `Pages/PlotPage.xaml.cs` | ScottPlot 默认浅色背景；十字线 `LightGray`；已有手动换肤按钮 | 在主题切换时设置 ScottPlot 深色 Style / 坐标轴 / 网格颜色 |
| `Lua.xshd` | 语法高亮颜色硬编码（橙/蓝/绿等） | 增加 Dark 版 XSHD，或按主题加载不同语法定义 |
| `MainWindow` / `SettingWindow` 的 AvalonEdit | 父容器用了 `Brush.Surface`，但编辑器自身的 `Foreground/Background`、行号等未显式主题化 | 显式设置编辑器前景/背景/行号/选区/当前行颜色 |
| `MainWindow` 数据区边框 | 代码中用 `Color.FromRgb(0,148,0)` | 改为主题语义色 |
| `DesignTokens.xaml` 中少量 `White` / `#26000000` | 个别按钮/遮罩 | 逐个确认深色下对比度；透明遮罩通常可保留 |
| `Tools.MessageBox` | 实际是 WPF `InputDialog`，跟随 Adonis 主题 | 主题切换后应自动跟随；仅异常回退的 WinForms MessageBox 不受控（可接受） |
| 系统窗口标题栏 / 文件对话框 | 非 WPF 自绘 | 标题栏可选 `DwmSetWindowAttribute` 深色；系统文件对话框接受系统主题 |
| README 截图 | 目前是浅色截图 | 做深色后补一张深色截图 |

### 1.3 结论

- **只做“能看”的深色**：1–2 天左右。
- **做到所有页面/图表/编辑器都协调**：约 **4–6 个开发日**，再加一轮人工视觉验收。
- 不建议只切换 Adonis 主题而不处理日志、数据区、AvalonEdit 和 Plot；那样会出现明显的白色区块，观感比不做更差。

## 2. 推荐架构

### 2.1 主题模型

```csharp
public enum AppTheme
{
    Light,
    Dark,
    System
}
```

- `Settings.theme` 持久化（`light` / `dark` / `system`）。
- 默认建议：
  - 首版默认 **Light**（保持现有观感，风险最低）；
  - 稳定后可以把默认改为 **System**。
- 主题选择放在「更多设置」或主窗口语言菜单旁边，带「浅色 / 深色 / 跟随系统」。

### 2.2 资源字典拆分

现在 `DesignTokens.xaml` 同时包含尺寸、样式和颜色。建议拆成：

```
Styles/
├─ DesignTokens.xaml       # 尺寸、间距、样式（不含具体颜色）
├─ Palette.Light.xaml      # Brush.* 浅色值
└─ Palette.Dark.xaml       # Brush.* 深色值
```

`App.xaml` 合并顺序：

```xml
languages/zh-cn.xaml
AdonisUI ColorSchemes/{Light|Dark}.xaml
AdonisUI.ClassicTheme/Resources.xaml
Styles/DesignTokens.xaml
Styles/Palette.{Light|Dark}.xaml
```

- 颜色放在最后，确保覆盖基础主题中的同名资源（如有）。
- 所有界面继续使用 `DynamicResource Brush.*`；不要新增 `StaticResource` 颜色。
- `ThemeService` 负责替换 **Adonis ColorScheme** 和 **Palette** 两个字典；可以按 `Source` 查找目标字典，避免依赖固定索引。

### 2.3 ThemeService

建议新增 `Services/ThemeService.cs` / `IThemeService`：

```csharp
interface IThemeService
{
    AppTheme Current { get; }
    bool IsDark { get; }
    void Apply(AppTheme theme);
    event EventHandler ThemeChanged;
    Brush GetBrush(string key);
}
```

职责：

1. 启动时在加载 `Settings` 之后、主窗口显示之前应用主题。
2. 设置变更时切换 Adonis ColorScheme + Palette。
3. 提升 `ThemeChanged` 事件，供无法靠 `DynamicResource` 自动更新的区域订阅。
4. `System` 模式：
   - 读取 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`；
   - 监听 `Microsoft.Win32.SystemEvents.UserPreferenceChanged`；
   - 用户切换系统主题时重新应用。
5. 可选：`DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` 让原生标题栏跟随深色（Windows 10 1809+）。

### 2.4 深色调色板建议

不要简单反色，建议使用中性深灰 + 高对比语义色：

| Token | Light（现状） | Dark（建议） |
|---|---|---|
| `Brush.Foreground` | `#000000` | `#E6E6E6` |
| `Brush.Foreground.Muted` | `#696969` | `#A0A0A0` |
| `Brush.Foreground.Subtle` | `#8C8C8C` | `#7A7A7A` |
| `Brush.Surface` | `#FFFFFF` | `#1E1E1E` |
| `Brush.Surface.Subtle` | `#F6F6F6` | `#252526` |
| `Brush.Surface.Muted` | `#D3D3D3` | `#3A3A3A` |
| `Brush.Border` | `#A9A9A9` | `#4A4A4A` |
| `Brush.Border.Subtle` | `#D3D3D3` | `#333333` |
| `Brush.Data.Received` | `#006400` | `#7EE787` |
| `Brush.Data.Sent` | `#8B0000` | `#FF7B72` |
| `Brush.State.Success` | `#008000` | `#7EE787` |
| `Brush.State.Error` | `#FF0000` | `#FF7B72` |
| `Brush.State.Danger` | `#8B0000` | `#FF9B8F` |
| `Brush.State.Info` | `#00BFFF` | `#79C0FF` |
| `Brush.Accent.Blue` | `#0000FF` | `#79C0FF` |
| `Brush.Accent.Teal` | `#008B8B` | `#56D4DD` |

- 实际值需在 100% / 125% / 150% DPI 下检查对比度；
- 收发数据颜色不能只用深红/深绿，在深色背景上会糊成一片；
- 选中文本、焦点框、禁用态需要单独确认。

## 3. 分阶段实施计划

### Phase 0：审计与基线（0.5 天）

- 截图记录所有页面/弹窗在浅色下的现状，作为回归对照；
- 确认 AdonisUI Dark 对所有标准控件的外观；
- 列出所有 `Brushes.*` / 硬编码颜色 / 图表 / 编辑器点。

### Phase 1：基础主题切换（1–1.5 天）

- 拆分 `Palette.Light` / `Palette.Dark`；
- 新增 `ThemeService` 与 `Settings.theme`；
- 启动时应用、设置页切换、持久化；
- 把 Adonis ColorScheme 换成 Dark，验证主窗口、Tab、按钮、下拉框、菜单、滚动条、ToolTip、状态栏；
- 此时日志/数据区可能仍是浅色，属于中间态，不单独发版。

### Phase 2：文本/日志/编辑区（1–1.5 天）

- `DataShowPage` 流式文本、分包 RichTextBox、收发颜色；
- `DataShowItem` 主题化，避免 `Brushes.DarkRed/DarkGreen`；
- `SerialMonitorPage`、`WinUSBPage`、`NetworkLog` 语义色；
- AvalonEdit：
  - 前景/背景/行号/当前行/选区；
  - `Lua.xshd` 深色版（或按主题加载不同 XSHD）；
  - 搜索面板、自动补全弹窗（如有）；
- `MainWindow` 数据区边框色。

### Phase 3：图表与特殊控件（1 天）

- `PlotPage`：在主题切换时调用 ScottPlot 的深色 Style，设置背景、坐标轴、网格、十字线；
- 现有“换肤按钮”保留，但默认跟随 App 主题；
- 确认 MQTT / TCP / 工具页没有残留白色控件；
- 原生标题栏（可选）与文件对话框（系统主题，接受）。

### Phase 4：系统主题、验收与文档（1–2 天）

- `System` 模式（注册表 + `SystemEvents`）；
- 所有页面/弹窗/DPI 视觉验收；
- 深色截图、README/CHANGES 更新；
- x64 / x86 构建 + 单元测试；
- 如果运行时切换有控件不刷新，优先修复或明确“切换后重启”。

## 4. 验收清单

- [ ] 启动直接进入深色，无白色闪屏/白色区块；
- [ ] 主窗口所有 Tab、按钮、菜单、下拉框、ToolTip、滚动条、状态栏；
- [ ] 收发数据 Abc/Hex、分包/流式、锁定/清空/保存；
- [ ] 快捷发送、发送历史、脚本/参数弹层；
- [ ] Lua 编辑器和运行日志（语法高亮、行号、选区、复制）；
- [ ] 设置窗口、关于页、工具页、串口监听、WinUSB、MQTT/TCP；
- [ ] ScottPlot 曲线、坐标轴、网格、十字线；
- [ ] 错误提示/确认对话框/输入框；
- [ ] 深色 ↔ 浅色运行中切换，已有日志和图表不残留旧颜色；
- [ ] 重启后主题设置保留；
- [ ] 跟随系统模式能响应系统切换；
- [ ] 100% / 125% / 150% DPI；
- [ ] x64 / x86 构建与 44 个单元测试通过。

## 5. 风险与非目标

### 风险

- AdonisUI 部分控件内部可能使用 `StaticResource`，运行时切换后需要重建窗口/控件；需逐项验证。
- ScottPlot 是独立绘图库，主题更新必须显式调用并重绘。
- AvalonEdit 的语法高亮不跟随 WPF 资源，必须单独处理。
- 代码里散落的 `Brushes.DarkRed/DarkGreen` 容易漏改；建议统一收口到 `ThemeService.GetBrush(...)` 或语义转换器。
- 深色下对比度不足比浅色更明显；需要真实截图验收，不能只看编译通过。
- 系统标题栏 / 文件对话框属于 Windows，不完全受 WPF 控制。

### 非目标（首版不做）

- Windows 高对比度模式的完整适配；
- 自定义强调色/完整主题编辑器；
- Acrylic/透明/动态壁纸取色；
- 每个页面独立主题；
- 大规模 UI 重设计。

## 6. 工作量结论

| 范围 | 估算 |
|---|---|
| 基础可切换（Adonis + 调色板 + 设置） | 1–1.5 天 |
| 日志/数据区/AvalonEdit | 1–1.5 天 |
| ScottPlot/特殊控件/标题栏 | 1 天 |
| 系统主题 + 全量验收 + 文档 | 1–2 天 |
| **完整可发布** | **4–6 天** |
| **仅“可用 MVP”**（基础 + 文本/日志/编辑器，图表暂不完美） | **约 2 天** |

建议先做 Phase 1 + Phase 2，确保日志、数据区、Lua 编辑器不是白色；再决定是否投入 ScottPlot 和系统主题。不要发布“半黑半白”的中间版本。
