# 07 · 重构继续 — 进度与后续计划

> 分支：`feature/refactor-continue`（基于上游 `origin/refactor`）
> 目的：记录本次「继续重构优化」的已完成项、验证状态、后续计划与交接注意事项，方便后续接手。

---

## 一、背景与基线

- 上游已有两条现成路线：
  - `origin/refactor`：WPF + net462 的 **MVVM 渐进重构版**（拆上帝类、去 Fody、修 8 个 bug、带 `docs/`）。
  - `origin/next`：**全新重写**（net10 + Avalonia + NLua + DI + LiteDB，v2.0，功能未对齐）。
- 本分支选择以 **`origin/refactor` 为基线**继续（低风险、保留全部功能与绿色单文件形态）。
- 总体分阶段：Phase 0 基线 → Phase 1 工程化（环境/构建/SDK-style/单测/CI）→
  Phase 2 架构深化（DI/线程/健壮性）→ Phase 3 终点形态决策（WPF-modern vs Avalonia）。

---

## 二、已完成（提交按时间序，均在 `feature/refactor-continue`）

| 提交 | 内容 |
|---|---|
| `0afa865` | **build**：引入 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net462`（带 `Exists` 条件），支持在**缺少 net462 目标包**的机器上构建，解决 `MSB3644`；修正 `CompilerGeneratedFilesOutputPath`（原 `$(IntermediateOutputPath)` 未定义，源生成输出错误落到 `llcom/generated/`，改用 `$(BaseIntermediateOutputPath)`） |
| `d36e410` | **fix**：修复**全新安装启动崩溃**。根因：`MainWindow` 先建 `MainViewModel` → `LuaEditorViewModel.RefreshList()` 扫描尚未创建的 `user_script_run/`，抛未处理的 `DirectoryNotFoundException`。修法：把 `Global.Initial()` 提前到 VM 构造之前；`LuaEditorViewModel` 增加目录缺失容错 |
| `fba0147` | **docs**：新增 `AGENTS.md`（环境注意事项、构建、已知坑） |
| `a6ab1a7` | **test**：新增 `tests/llcom.Tests`（xUnit/net48）并接入 CI；主程序集加 `InternalsVisibleTo`；清理 `llcom.sln` 的 `Any CPU` 配置 |
| `bb8261b` | **refactor(threading)**：AvalonEdit 折叠刷新、`PlotPage` 绘制刷新 → `DispatcherTimer`；`LuaLogPrintTask`/USB 收发线程设后台；热更改 `Task.Run` |
| `ad0382f` | **refactor(usb)**：USB 热插拔防抖 → `DispatcherTimer`（替换原无效的计数器忙等 hack） |
| `7b954ff` | **refactor**：`LuaRunEnv.canRun` 忙等改 `ManualResetEventSlim` 事件等待（可取消）；`LuaRunEnv`/`LuaApis` 事件调用加 `?.Invoke`；补充 `QuickSendViewModel`/`LuaEditorViewModel` 单测 |
| `013e242` | **poc(sdk-style)**：主工程迁移为 **SDK-style csproj**（`Microsoft.NET.Sdk` + `UseWPF`/`UseWindowsForms` + `PackageReference` + 隐式 globbing）；删除 `packages.config`；移除老旧源包 `Walterlv.Environment.Source`（改用本地 `Services/NdpInfo.cs`）；CI 改 `dotnet build`/`dotnet test`。验证：四配置构建、单文件、`dotnet build`/`dotnet test`、20 测试全通过 |
| `387a6c5` | **docs**：更新进度文档，标记 SDK-style 完成 |
| `4ddf297` | **refactor(di)**：引入组合根 `Services/AppHost.cs`（`Microsoft.Extensions.DependencyInjection` 7.0.0），注册既有单例与 `MainViewModel`，`MainWindow` 改为从容器解析（DI 第一步） |

---

## 三、验证状态（截至该里程碑）

- **构建**：`llcom` 四种配置全部成功 —— x86/x64 × Debug/Release（单文件 `llcom.exe`）。
  迁移 SDK-style 后 **`dotnet build` 亦可**（x86/x64 需各自还原）。
- **测试**：`tests/llcom.Tests` 共 **20 个用例全部通过**（Debug/Release x64 均验证）。
- **冒烟**：清空输出目录模拟全新安装 → 正常启动（主窗口标题正确）+ 生成完整文件结构；
  正常关闭（CloseMainWindow）退出码 0、`lock` 清理、`settings.json` 落盘。

---

## 四、后续计划

### Phase 1（已完成）
- [x] 环境打通（net462 参考程序集包）、单元测试工程、CI 接入、**SDK-style csproj 迁移**（提交 `013e242`）。
  - SDK-style 结果：`dotnet build`/`dotnet test` 均可直接使用；x86/x64 需各自还原
    （.NET Framework exe 工程按 `PlatformTarget` 推断 RID：`win-x64`/`win-x86`）。

### Phase 2 剩余
- [~] **保守 DI**：*第一步（组合根 `AppHost`）已完成*（提交 `4ddf297`）。
  - 剩余：把调用点从 `Tools.Global.setting`/`Tools.Global.uart` 逐步迁移为构造函数注入，
    并陆续把 `QuickSendViewModel`/`LuaEditorViewModel`/工具页也纳入容器。
- [ ] **收敛 code-behind**：`MainWindow` 的导航/Lua 运行/日志打印；工具页弹窗（依赖 Popup，收益低，可保留）。
- [~] **更多健壮性**：
  - [x] `LuaLoader.Run` 两处无界忙等改有界重试（提交 `51aff1e`）。
  - [ ] 其他 `catch {}` 吞异常处按需加日志。

### Phase 3（终点形态，决策闸口）
- [ ] 用 PoC 对比 **WPF on net8/net9-windows** vs **Avalonia（复用 `origin/next`）**，再决定并升级老化依赖
      （RestSharp 106、MQTTnet 3、ScottPlot 4、LibUsbDotNet alpha、无维护的 XLua 等）。

### 收尾
- [ ] 推分支 / 发起 PR / 合并到 `master`（合并后跑一次 CI 与发布冒烟）。

---

## 五、构建与测试命令（本机，SDK-style 后）

```powershell
# 构建主程序（x64 Debug 示例；x86 换 -p:Platform=x86，会自动按该平台还原）
dotnet build llcom\llcom.csproj -c Debug -p:Platform=x64

# 运行单元测试
dotnet test tests\llcom.Tests\llcom.Tests.csproj -p:Platform=x64
```

> VS MSBuild（`D:\Microsoft Visual Studio\MSBuild\Current\Bin\MSBuild.exe`）亦可。详见 `AGENTS.md`。

---

## 六、交接注意事项

- **本机无 ripgrep**：内置 `grep`/`glob` 工具不可用，改用 `shell` + `Select-String`/`Get-ChildItem`（见 `AGENTS.md`）。
- **本机缺 .NET Framework 4.6.2 目标包**：已用 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net462` 解决。
- **SDK-style 注意**：.NET Framework 的 exe 工程按 `PlatformTarget` 推断 RID（`win-x64`/`win-x86`），
  x86/x64 需各自还原（`dotnet build -p:Platform=x86` 会自动处理）。
- **exe 运行约束**：必须叫 `llcom.exe`，且不能在压缩包/临时目录直接运行（源码 `ProfileInitializer.Initial()` 校验）。
- **已知坑**沿用 `docs/02-architecture.md` 第四节与 `AGENTS.md`：WPF Page 需手写 INPC、AvalonEdit Text 不可绑定、
  `Settings.quickSend` 必须 `[JsonIgnore]`、串口 SafeHandle 释放勿改同步等。
- **测试约定**：涉及静态全局状态（`Global.setting`/`AppPaths.ProfilePath`），已禁用并行；
  访问 `internal` 成员靠 `InternalsVisibleTo("llcom.Tests")`。