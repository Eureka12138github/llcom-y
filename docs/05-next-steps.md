# LLCOM 下一步计划

> 状态：上游重构（`origin/refactor`）与「继续重构」的**工程化阶段**（`feature/refactor-continue`）均已完成。
> 本文档给出**当前路线图**与**操作流程**；已完成记录见 [07-refactor-continue-status.md](07-refactor-continue-status.md)，
> 质量改进项见 [08-code-quality-audit.md](08-code-quality-audit.md)。

## 一、阶段状态速览

| 阶段 | 状态 | 说明 |
|---|---|---|
| Phase 0 基线 | ✅ 完成 | 基线构建、冒烟清单、可回退分支 |
| Phase 1 工程化 | ✅ 完成 | net462 参考程序集包、单元测试（20 例）、CI、**SDK-style csproj** |
| Phase 2 架构深化 | 🚧 进行中 | DI 组合根 `Services/AppHost.cs` 已起步；网络工具页去重进行中（见 08） |
| Phase 3 终点形态 | ⏳ 待决策 | WPF-modern（net8/net9-windows）vs Avalonia（复用 `origin/next`） |

## 二、路线图（唯一维护点）

后续任务的**权威清单只在 [08-code-quality-audit.md](08-code-quality-audit.md)**（第四、六节）与
[07-refactor-continue-status.md](07-refactor-continue-status.md)（第四节）维护，本文档不再重抄，
以免多处副本产生偏差。当前优先级：

1. 收尾网络工具页去重（公共组件，见 08 第六节「尚未做」）
2. 继续 DI：把 `Tools.Global.*` 调用逐步迁为构造函数注入
3. 拆分 `MainWindow` code-behind 的导航 / Lua / 日志逻辑

## 三、开发新功能

流程见 [04-maintenance.md](04-maintenance.md) 第五节「开发新功能流程」。关键点：

- 数据/服务放 `Services/` 或 `Model/`，页面逻辑放 `ViewModels/`，视图只做绑定。
- WPF `Page` 必须**手写 `INotifyPropertyChanged`**（原因见 [02-architecture.md](02-architecture.md) 第四节）。
- 当前为 **SDK-style 工程（隐式 globbing）**，新增 `.cs`/`.xaml` **无需**手动登记到 csproj。
- 需要 Lua 通道时：`LuaApis.SendChannelsRegister("xxx", ...)`。
- 完成前按 [03-test-checklist.md](03-test-checklist.md) 冒烟。

## 四、发布流程（沿用现有）

1. 合并到 `master`。
2. 打 tag → CI 自动构建 Release 并打包 zip（`.github/workflows/build.yml`）。
3. 发布到 GitHub Releases，并更新 `changelog/autoUpdate.xml`（AutoUpdater.NET 检查更新用）。

## 五、历史评估归档（结论已定，勿重复评估）

| 事项 | 结论 |
|---|---|
| SDK-style csproj 迁移 | ✅ 已完成（提交 `013e242`）；`dotnet build`/`dotnet test` 可用，x86/x64 需各自还原 |
| 单元测试 | ✅ 已建 `tests/llcom.Tests`（xUnit/net48，20 例，CI 覆盖） |
| 升级 .NET Framework | ❌ 不推荐（`xlua`/`serial_monitor` 等原生依赖需重编译） |
| 迁移 Avalonia | ❌ 暂不采用（`origin/next` 功能未对齐、收益未验证） |
| 移除 Costura | ❌ 不推荐（会破坏单文件绿色版发布形态） |

## 六、当前已知限制

- `Tools/Global.cs` 仍是静态外观层（`setting`/`uart` 全局单例），功能够用；若要彻底 DI 化可继续推进（见 07、08）。
- `MainWindow.xaml.cs` 约 730 行：Lua 日志（`Services/LuaLogPump`）与升级/热更（`Services/AppUpdateService`）
  已下沉为服务，导航 / Lua 运行等 UI 绑定逻辑保留在 code-behind。
- 工具页弹窗（接收脚本 / 参数选择）保留 code-behind（依赖 Popup 控件，VM 化收益低）。

## 七、发布前待办（个人维护版 / LLCOM-Y）

> 配套：[09-github-publish-plan.md](09-github-publish-plan.md)、[12-serial-monitor-plan.md](12-serial-monitor-plan.md)。

- [ ] **在线脚本列表的数据源**：`Services/OnlineScriptService` 目前从 `https://llcom.papapoi.com/token.txt` 拉取（仅打开「在线脚本」页时才请求）。发布前决定：改为自己的静态文件/仓库，或保留并在页面/README 注明「数据来自上游 LLCOM」。
- [ ] **MSIX 身份重新关联**：`WapProj/Package.appxmanifest` 的 `Identity Name/Publisher` 仍是原作者信息。自行打包（Desktop Bridge / MSIX）前需在 Visual Studio 里重新关联到自己的开发者账号；仅发布 exe 不受影响。
- [ ] （建议）发布 zip 内一并附带 `LICENSE` 与 `CHANGES.md`：Apache-2.0 要求随分发提供许可证副本，并保留版权/归属声明。