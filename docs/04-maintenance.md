# LLCOM 维护指南

日常开发、构建、格式化的操作手册。

## 一、环境与构建

### 本机构建工具
| 工具 | 路径/版本 | 用途 |
|---|---|---|
| dotnet SDK 10 | `C:\Program Files\dotnet` | **主构建工具**（`dotnet build` / `dotnet test`，SDK-style csproj） |
| Visual Studio 2026（VS 18 Community） | `...\MSBuild\Current\Bin\amd64\MSBuild.exe` | 可视化调试 / 可选构建 |
| CSharpier 1.3.0 | `~\.dotnet\tools\csharpier` | 代码格式化 |

> 工程已迁移为 **SDK-style**（`Microsoft.NET.Sdk` + `UseWPF`/`UseWindowsForms`）：`dotnet build` 与 VS MSBuild 均可。
> net462 参考程序集由 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net462` 提供，**无需安装 .NET Framework Dev Pack**。

### 构建命令（x64 Debug）
```powershell
# 推荐：dotnet CLI（x86 换 -p:Platform=x86，会自动触发该平台还原）
dotnet build llcom\llcom.csproj -c Debug -p:Platform=x64

# 或 VS MSBuild
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" llcom\llcom.csproj -p:Configuration=Debug -p:Platform=x64
```
- 平台：x64 / x86（.NET Framework exe 工程按 `PlatformTarget` 推断 RID，**x86/x64 需各自还原**）
- 配置：Debug / Release
- 输出：`artifacts\Debug\x64\llcom-y.exe`（单文件，托管依赖被 Costura 嵌入；**Release 与 Debug 各占一个顶层文件夹**）

### 单元测试
```powershell
dotnet test tests\llcom.Tests\llcom.Tests.csproj -p:Platform=x64
```
测试工程通过 `[assembly: InternalsVisibleTo("llcom.Tests")]` 访问 `internal` 成员；涉及静态全局状态，已禁用并行。

### 构建警告

- 已移除 `App.config` 中冗余的 `System.Runtime` 绑定重定向，消除 `MSB3836`。
- 目前仍有约 **544 条 C# 可空性分析警告**（`CS8618` / `CS8625` / `CS8600` / `CS8602` 等），原因是项目开启了 `<Nullable>enable</Nullable>`，但大量旧代码没有按可空性标注。
- 这些警告不影响构建/运行，但建议单独做“可空性清理”：
  - 不要直接关闭 `<Nullable>` 或用 `NoWarn` 全局隐藏；
  - 按文件分批处理：事件/字段初始化、可为空的返回值、参数和 `null` 判断；
  - 新增代码继续保持可空性标注，避免继续增加基数。

### 代码格式化
```bat
csharpier format llcom\ --skip-write 2>nul  # 或直接
csharpier format <文件或目录>
```
⚠️ 只对 `.cs` 和 UI `.xaml` 执行；**不要**对 `llcom.csproj` / `packages.config` / 语言文件 / FodyWeavers.xml 执行（会被重排）。

### CI（GitHub Actions）
`.github/workflows/build.yml`：windows-latest + `dotnet build` / `dotnet test`。

- `pull_request` / 分支 `push` 会忽略 `community-scripts/**`、`docs/**`、`*.md` 和 `.github/**`，避免脚本/文档 PR 触发整包构建；
- tag 推送仍会执行构建与 Release；
- 同一分支/PR 的旧构建会被 `concurrency` 取消，减少重复运行；
- `.github/workflows/validate-community-scripts.yml` 负责社区脚本校验，并用 `luac5.3 -p` 做 Lua 语法检查；
- `.github/workflows/cleanup-import-branch.yml` 在 `/import` PR 关闭/合并后删除 `community-scripts/import-*` 临时分支；导入失败时脚本也会尝试清理失败分支。

改动构建系统前需同步检查 CI。

## 二、目录速览（改代码前必读）

```
llcom/
├─ ViewModels/     # 各功能 VM（串口/快捷发送/Lua编辑器/数据区）
├─ Services/       # 服务层（路径/配置/编码/串口/在线脚本/本地化/文件）
├─ Model/          # 数据模型（Settings 6个分部文件 + 各条目模型）
├─ Pages/          # 工具页（UI 交互 + 手写 INPC）
├─ View/           # 主窗口/设置窗口
├─ LuaEnv/         # Lua 引擎（XLua + Luat 协程框架 + 3 类 VM）
└─ Tools/Global.cs # 全局状态外观层（setting/uart/事件）
```

详细架构见 [02-architecture.md](02-architecture.md)。

### 默认脚本 / 默认文件（DefaultFiles）

`llcom/DefaultFiles/` 下的内容通过 csproj 的 `<Resource Include="DefaultFiles\**" />` 自动嵌入，由
`Services/ProfileInitializer.cs` 在启动时释放到配置目录（exe 同目录，或 MSIX 的 `%LOCALAPPDATA%\llcom\`）。

新增一个**默认脚本**：

1. 把 `.lua` 放进对应目录：
   - `DefaultFiles/user_script_run/`（独立运行脚本）
   - `DefaultFiles/user_script_send_convert/`（发送处理脚本）
   - `DefaultFiles/user_script_recv_convert/`（接收处理脚本）
2. 在 `ProfileInitializer.cs` 里对应 `ReleaseDefaultScripts("<目录>", "文件名.lua", ...)` 的参数中加上文件名；
3. 重新构建。下次启动时，缺失的默认脚本会补齐；**已存在的同名文件不会被覆盖**（保留用户修改）。
   - 想立即生效，也可以直接把脚本放进配置目录的对应文件夹（无需重建）。

其他默认文件（`反馈网址.txt`、`LICENSE`、`core_script/`、`libusb-1.0` 等）在同一个 `Initial()` 里释放。

### 社区脚本（community-scripts）

- **上游源**保持原实现（`chenxuuu/llcom` 的 Discussions，token 从 `llcom.papapoi.com` 获取）。
- **本项目源**读取 `community-scripts/index.json`，脚本文件放在同一目录；App 优先走 jsDelivr，失败再走 raw。
- 新增本项目脚本：放 `.lua` 文件，并在 `index.json` 加一条记录；`saveTo` 可选 `run` / `send` / `recv`，仅决定下载目录（`user_script_run` / `user_script_send_convert` / `user_script_recv_convert`），**界面不显示为分类标签**。
- 投稿：GitHub Discussions 分类表单 `.github/DISCUSSION_TEMPLATE/script-submissions.yml`；维护者审核后在本帖评论 `/import`，`.github/workflows/import-community-script.yml` 会自动生成 `.lua`、更新 `index.json` 并创建 PR（不自动合并、不执行脚本）。需在仓库 Discussions 创建 **Script submissions** 分类（slug `script-submissions`）。
- 校验：`.github/workflows/validate-community-scripts.yml`（检查 JSON、保存位置、文件是否存在）。
- 格式与投稿说明见 [community-scripts/README.md](../community-scripts/README.md)。

## 三、依赖清单

### 重构后新增
- `CommunityToolkit.Mvvm 8.2.2`（+ `Microsoft.Bcl.AsyncInterfaces 7.0.0`、`System.ComponentModel.Annotations 5.0.0`、`System.Threading.Tasks.Extensions 4.5.4`）

### 重构后移除
- `PropertyChanged.Fody 3.4.0`（全部类已转 ObservableObject 或手写 INPC）

### 保留（勿动版本）
- `Costura.Fody`（单文件绿色版必需，FodyWeavers.xml 仅剩 Costura 配置）
- `XLua.Mini`（本地 `Lib\XLua.Mini.dll`，非 NuGet）
- 其余全部 NuGet 依赖

> 依赖通过 **PackageReference** 管理（SDK-style 工程直接读取 csproj）；新增依赖用 `dotnet add package` 或编辑 csproj。

## 四、踩坑记录（维护时务必注意）

### 1. WPF Page 属性通知必须手写
XAML `.g.cs` 硬编码基类 `: Page`，且 MVVM Toolkit 生成器与接口/特性互斥 → **Page 类一律手写**：
```csharp
public partial class XxxPage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    private bool _isConnected;
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }
}
```
新属性：`[ObservableProperty]` 生成器**不可用**于 Page，用上述模板。

### 2. AvalonEdit 文本桥接
`TextEditor.Text` 不是 DependencyProperty，不能绑定。VM 用 `SetTextBridge(() => editor.Text, t => editor.Text = t)` 接入。

### 3. Settings.quickSend 有 [JsonIgnore]
它是 quickSendList 的视图属性，去掉 [JsonIgnore] 会导致反序列化数据翻倍（历史 bug，勿回退）。

### 4. 串口 SafeHandle 崩溃规避
`SerialPortService.refreshSerialDevice()`：旧 SerialPort 对象入 `useless` 列表 + 后台线程 Dispose。**不要**改为同步 Dispose。

### 5. 接收分包逻辑
`ReadData()`：收到事件后等 `setting.timeout` ms 凑包（防中文分割）。`timeout>=0` 分包模式、`<0` 流式模式（DataShowPage 显示方式随之切换）。

### 6. USB 热插拔
- 端口列表 = WMI 结果 + 注册表权威过滤（`GetPortNames()`）
- 自动重连条件 `!_forcusClosePort && autoReconnect`，`_forcusClosePort` 初始 true（防启动误连）

### 7. 设置防抖保存
属性 setter → `Save()`（600ms 合并写盘），窗口关闭时 `Global.isMainWindowsClosed=true` 触发 `Flush()` 强制落盘。加新设置属性时保持此模式。

## 五、开发新功能流程（推荐）

1. **数据/服务** → 放 `Services/` 或 `Model/`（纯逻辑，带中文注释）
2. **页面逻辑** → 新建 `ViewModels/XxxViewModel.cs`（继承 ObservableObject 或手写 INPC）
3. **视图** → XAML 绑定 VM；控件不可绑定的（AvalonEdit）用 SetTextBridge 桥接
4. **注册** → SDK-style 工程隐式 globbing，新增 `.cs`/`.xaml` 会自动纳入，**无需**手动登记到 csproj
5. **格式化** → `csharpier format <新文件>`
6. **测试** → 按 [03-test-checklist.md](03-test-checklist.md) 冒烟

## 六、常见操作

| 操作 | 方法 |
|---|---|
| 加 NuGet 包 | `dotnet add llcom\llcom.csproj package <名字>`，或编辑 csproj 的 `<PackageReference>` |
| 加 Lua API | `LuaEnv/LuaApis.cs` 静态方法 → `LuaEnv/LuaLoader.cs` 里 `lua.DoString("apiXxx = CS...")` 注册 → 更新 `LuaApi.md` |
| 改语言文案 | `llcom/languages/zh-CN.xaml` + `en-US.xaml`（键一致） |
| 看运行时日志 | `artifacts/Debug/x64/logs/log.txt`（串口）、`user_script_run/logs/log.txt`（Lua） |
