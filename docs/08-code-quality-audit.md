# 08 · 代码质量审计（对照作者"代码零散、不适合阅读"的评价）

> 背景：原作者在 README 写道"本项目在前期只是为了实现功能，代码相当零散，所以不太适合阅读我的源码进行学习"。
> 本文档审计经重构后的现状（`feature/refactor-continue`），记录仍存在的问题、本次已修复项与后续建议。

---

## 一、审计指标（`llcom/`，排除 `obj`/`bin`/`generated`）

| 指标 | 数值 | 评价 |
|---|---|---|
| 代码文件数 | 74 | — |
| 总行数 | ~13600 | — |
| **空 `catch { }`（静默吞异常）** | **48 → 0（本次修复）** | ✅ 已解决 |
| `Tools.Global.*` 静态引用 | 278 处 / 34 文件 | ⚠️ 全局耦合仍偏重 |
| `TODO/FIXME/HACK` | 1 | ✅ 基本没有 |

**最大文件**（行数）：

| 文件 | 行数 | 说明 |
|---|---|---|
| `View/MainWindow.xaml.cs` | ~830 | 导航 + Lua 运行 + 日志打印等 UI 密集逻辑（上游认为属合理边界） |
| `LuaEnv/LuaEnv.cs` | 550 | 内嵌 Luat 协程框架的 Lua 源码字符串 |
| `ViewModels/MainViewModel.cs` | ~526 | 主窗口串口控制区 |
| `Pages/SocketClientPage.xaml.cs` | ~481 | TCP 客户端工具页 |
| `Pages/tcpTest.xaml.cs` | ~476 | TCP 测试工具页 |
| `Pages/MqttTestPage.xaml.cs` | ~411 | MQTT 工具页 |
| `Pages/WinUSBPage.xaml.cs` | ~407 | WinUSB 工具页 |

---

## 二、对照作者评价的结论

重构后已**大幅缓解**"零散"问题：项目从"上帝类 + 巨型 code-behind"变为
`ViewModels/`（主窗口各功能 VM）、`Services/`（可复用服务）、`Model/`（数据模型）、`Pages/`（工具页）的分层结构，
并去除了 `PropertyChanged.Fody` 织入黑魔法、合并了重复的 Lua 编辑器。

**但仍存在作者所说的"零散"残留**，主要为：

1. **静默吞异常**（本次已修复）——48 处空 `catch { }`，出问题无从排查。
2. **全局静态耦合**——`Tools.Global.setting` / `Tools.Global.uart` 被 34 个文件直接引用，
   是"零散"体感的主要来源（DI 组合根已起步，见 `Services/AppHost.cs`）。
3. **重复的网络工具页**——`SocketClientPage` / `TcpLocalPage` / `UdpLocalPage` / `tcpTest` / `MqttTestPage`
   结构高度相似（连接参数、收发框、日志、分包显示），存在可抽取的公共模式。
4. **超大文件**——上述几个工具页与 `MainWindow.xaml.cs` 偏大，阅读成本高。

---

## 三、本次修复：消除静默吞异常

**约定**：不再允许空 `catch { }`。二选一：
- **逻辑类**（可能掩盖 bug）：用 `Logger.Swallowed("<context>", ex)` 以 debug 级别记录（不改变容错行为）。
- **清理类**（dispose/close/cancel 等尽力而为）：写显式注释说明，如 `catch { /* 忽略：释放旧串口资源失败不影响主流程 */ }`。

新增帮手：`Tools/Logger.Swallowed(string context, Exception ex)`（记录 `[swallowed] context: 类型 消息`，自身绝不再抛）。

修复范围：核心逻辑（`SerialPortService`、`MainViewModel`、`OnlineScriptService`、`LuaEditorViewModel`、
`Settings`、`LuaEnv`、`LuaRunEnv`）改为日志；UI 工具页与清理路径改为带日志/显式注释。
**空 catch 数量：48 → 0。**

---

## 四、后续建议（按性价比排序）

1. **继续 DI**：把 `Tools.Global.*` 调用逐步迁移为构造函数注入（先从 `Services` 与被 DI 创建的 VM 开始）。
2. **抽取网络工具页公共基类/组件**：连接参数板、收发日志、分包显示、通道注册可复用，消除 5 个页面的重复。
3. **拆分超大 code-behind**：`MainWindow` 的导航/Lua 运行/日志打印可进一步下沉到 VM/服务。
4. 其它：`catch{}` 已清零后，保持该约定（可在 review/CI 层面提醒）。

---

## 五、与计划的关系

- 本审计属于 Phase 2「架构深化」的一部分；第 1 项即 Phase 2 剩余项（DI 完善），
  第 2/3 项即 Phase 2「收敛 code-behind」的扩展。
- 详见 `07-refactor-continue-status.md`。

---

## 六、网络工具页去重进展

参照对 5 个网络工具页的重复分析，已按「低风险 → 高」完成以下几批：

- [x] **状态类归位**：`StateObject` / `SocketObj` / `UdpState` → `Model/NetworkStates.cs`，
  删除 `TcpLocalPage`/`UdpLocalPage` 对 `SocketClientPage` 的 `using static` 跨页耦合（`ce4b4c8`）
- [x] **日志出口统一**：`Tools/NetworkLog.cs`；`SocketClient`/`tcpTest`/`TcpLocal`/`UdpLocal`
  重复的 `ShowData` 方法体改为一行转发（`ce4b4c8`）
- [x] **值转换器集中**：`App.xaml` 统一声明 8 个转换器，删除 9 处页面/窗口重复声明（`5a5f710`）
- [x] **`RefreshIp` 去重**：抽到 `Tools/NetworkHelper.cs`（`64e388d`）

尚未做（中高风险，建议后续逐步、每步独立提交）：

- [ ] 共享网络 VM 基类（消除 5 份 `SetProperty` / `IsConnected` 等样板；注意 WPF Page 不能自定义基类，需组合）
- [ ] 本地监听面板 / 发送面板 UserControl（`TcpLocal`/`UdpLocal`/`SocketClient`/`tcpTest`）
- [ ] `LuaChannelBridge`（统一通道注册/注销；需先给 `LuaApis` 补注销 API）
- [ ] `tcpTest` 内部 `ws`/`wsV6` 双份去重（单文件，自包含）
- [ ] Mqtt 内联 `ShowDataRaw`（6 处）统一走 helper（其标题/颜色各异，需逐个核对）