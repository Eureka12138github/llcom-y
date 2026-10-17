# 12 · 串口监听（Serial Monitor）改进计划

> 状态：**待实施**（2026-10-17 制定；"可诊断性"部分已完成，见 [11-ui-fixes-plan.md](11-ui-fixes-plan.md) 第 5 节）
> 目标：让「串口监听」在真实场景下稳定可用——**监听开始前已打开的串口也能抓**，并支持 **32 位目标**。
> 前提：需要 **Rust 工具链 + MSVC**（本机当前未安装）；否则只能停留在"先开始监听、再让目标打开串口"。

---

## 1. 现状（2026-10-17 实测）

自建两个测试程序（x86 / x64 各一个，打开 COM5 并周期写数据）逐个验证：

| 场景 | 结果 |
|---|---|
| x64 目标 + **先开始监听、后打开串口** | ✅ 正常捕获 |
| x64 目标 + **先打开串口、后开始监听** | ❌ 零数据（Hook 只在 `CreateFileW` 时登记句柄） |
| x86 目标 + **x64 版 LLCOM** | ❌ `MonitorComm` 返回成功，但目标里没有 `serial_monitor_hook.dll` → 零数据（静默失败） |
| x86 目标 + **x86 版 LLCOM**（旧闭源 DLL） | 💥 目标程序被搞崩（同一目标独立运行 12 秒正常，一监听就在打开串口后退出） |

已完成（C# 侧，不需要 Rust）：

- 启动前用 `IsWow64Process` 检查目标位数，不一致时明确提示并拒绝启动；
- `MonitorComm` 成功后校验目标进程是否真的加载了 hook 模块，失败自动停止并提示；
- `MonitorComm` 返回 false / 抛异常均有明确提示（权限、杀软、目标退出）；
- 回调按所选 COM 端口过滤（DLL 实际转发目标的所有串口）；
- 页面常驻提示 + 帮助文档同步；32 位目标提示改为"暂不支持"。

---

## 2. 根因

1. **位数绑定**：用户态 Hook 必须与目标进程同位数。x64 版无法注入 32 位目标；Rust 的 `inject_dll` 只等待远端线程、不检查 `LoadLibraryW` 结果，所以会"假成功"。
2. **句柄登记时机**：`serial_monitor_hook.dll` 只在目标的 `CreateFileW/A` 被调用时登记 COM 句柄；监听前已打开的句柄不在 `COM_HANDLES` 里，读/写钩子自然不命中。
3. **旧 32 位 DLL 不可用**：`costura32/serial_monitor.dll` 是闭源旧版，实测会让目标崩溃，不能作为 32 位方案。

---

## 3. 方案对比

| 方案 | 32 位目标 | 已打开的端口 | 免驱动/免签名 | 稳定性 | 工作量 |
|---|---|---|---|---|---|
| 现状：x64 Hook（单进程内） | ❌ | ❌ | ✅ | 中 | — |
| **P1：Hook 加载时枚举已有句柄** | ❌ | ✅ | ✅ | 中 | 小（~半天） |
| **P2a：i686 Hook + x86 版 LLCOM** | ✅（需换 x86 版） | ✅ | ✅ | 中 | 中（~1 天） |
| **P2b：32 位注入助手进程（单 exe）** | ✅ | ✅ | ✅ | 中 | 大（2–3 天） |
| 内核串口过滤驱动（商业级：Eltima / HHD 等） | ✅ | ✅ | ❌ 必须签名 | 高（但蓝屏风险） | 很大（不现实） |

结论：**最佳实践 = 按位数准备 x86/x64 两套 Hook 宿主 + Hook 加载时接管已有句柄 + 主程序通过 IPC 收数据**。
不推荐内核驱动：需要 EV 证书 + 微软签名、驱动崩溃即蓝屏、还要兼容各家 USB-serial 驱动栈，对个人修改版投入产出比极差。

---

## 4. 实施路线

### P0（已完成）

见 11 号文档第 5 节：位数检查、注入校验、明确错误提示、按端口过滤、页面/帮助提示。

### P1 · 接管"已经打开"的串口（收益最大）

在 `serial_monitor_hook.dll` 的 `DllMain`（或首次 Hook 安装后）补一次**自扫描**：

1. `NtQueryInformationProcess(ProcessHandleInformation)` 取**本进程**句柄表（自己的进程无需提权）；
2. 对每个句柄 `DuplicateHandle` → `GetFinalPathNameByHandleW`，匹配 `\\.\COMx` / `\Device\SerialN`；
3. 用 `QueryDosDevice("COMx")` 建立 `\Device\SerialN → COMx` 映射；
4. 写入现有 `COM_HANDLES`（`HashMap<handle, com>`），后续 `ReadFile/WriteFile` 自动命中。

**顺手修**：Rust 的 `inject_dll` 用 `GetExitCodeThread` 或目标模块列表校验注入结果，失败返回 0 并 `SetLastError`（C# 侧就不用再自己查模块）。

验收：
- 目标先开串口、后开始监听 → 也能出数据；
- 目标是多串口程序 → 只显示所选端口（已有过滤）。

### P2a · i686 Hook + x86 版 LLCOM（省事版）

1. 用 Rust 编译 `i686-pc-windows-msvc` 的 hook 与 host DLL，替换 `costura32/serial_monitor.dll`（旧闭源版）；
2. 发版同时提供 x64 / x86 两个包，用户按目标位数选择；
3. 更新文案：32 位目标请用 x86 版。

验收：
- x86 版监听 32 位目标不崩溃、能出数据；
- x64 版监听 64 位目标保持现状。

### P2b · 32 位注入助手（单 exe 体验，长期方案）

1. 新增辅助工程 `llcom_monitor_helper`（同一套宿主代码，x86 编译）；
2. x64 LLCOM 检测到 32 位目标 → 启动助手，传入自己的管道名；
3. 助手注入 i686 hook，把 `Udata` 通过命名管道转发给主程序；
4. 主程序侧把"进程内 host"与"助手 host"抽象成同一个数据源接口。

验收：
- 单个 x64 版即可监听 32 位 / 64 位目标；
- 助手异常退出时主程序自动收尾并提示。

### P3 · 健壮性（可选）

- 补 Hook 覆盖面：`ReadFileEx`、完成端口（`GetQueuedCompletionStatus`）；
- 目标退出 / 重开端口时自动复位按钮并重新接管；
- "开始监听 N 秒仍无数据"时给一次性提示（现在只有静态提示）。

---

## 5. 前置条件与构建

- 安装：`winget install Rustlang.Rustup`（或 rustup-init），并确认为 **MSVC** 工具链；
- 目标：`rustup target add x86_64-pc-windows-msvc i686-pc-windows-msvc`；
- 构建（x64）：
  ```powershell
  cd serial_monitor_rs
  cargo build --release -p serial_monitor_hook --target x86_64-pc-windows-msvc
  cargo build --release -p serial_monitor --target x86_64-pc-windows-msvc
  ```
  `build.ps1` 会把 `serial_monitor.dll` 复制到 `llcom/costura64/`；
- 构建（i686，P2a）：同样命令，`--target i686-pc-windows-msvc`，产物复制到 `llcom/costura32/`；
- 改完 DLL 后重新 `dotnet build` 才会被 Costura 嵌入。

---

## 6. 验收清单（回归用）

| 场景 | 期望 |
|---|---|
| x64 目标，先监听后开串口 | ✅ 出数据 |
| x64 目标，先开串口后监听 | ✅ 出数据（P1 后） |
| 32 位目标（x64 版 LLCOM） | 明确提示；P2b 后 → 出数据 |
| 32 位目标（x86 版 LLCOM） | 不崩溃且出数据（P2a 后） |
| 多串口目标 | 只显示所选端口 |
| 安全软件拦截注入 | 明确提示，不影响主程序 |
| 目标中途退出 | 按钮复位、提示（P3） |

测试程序：`%TEMP%\opencode\serial_target\serial_target_x86.exe|x64.exe`（可用参数指定端口与延迟），本次排查时自建。

---

## 7. 明确的非目标

- **不做内核驱动**（签名、蓝屏、维护成本）；
- 不改变"旁路监听"的定位（不替代抓包/协议分析）；
- 不保证覆盖所有第三方驱动绕过 `kernel32` 的 I/O 路径（例如某些带自有驱动的 USB-serial 工具）。

---

## 8. 待确认

1. 实际要监听的目标程序是 **32 位还是 64 位**？（决定 P2 要不要做）
2. 本机能否安装 **Rust + MSVC 构建工具**？（P1/P2 都需要）
3. 若做 32 位：选 **P2a（两套安装包）** 还是 **P2b（单 exe + 助手）**？
