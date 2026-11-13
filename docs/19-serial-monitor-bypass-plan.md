# 19 · 串口监听旁路方案实施计划（B′ com0com + hub4com 分流 / C USBPcap 兜底）

> 状态：**待实施**（2026-11-13 制定；2026-11-13 修订：B 方案改用 hub4com 分流实现，取消自研桥接，并补充开源同类软件调研结论）
> 目标：让「串口监听」能覆盖**带壳、带完整性自校验、带反调试/反注入**的工业上位机（不被目标软件发现、不搞崩目标）。
> 使用场景（本次评估依据）：**CH341 USB 转 RS-485 模块**，监听上位机 ↔ 电表（DL/T 645-2007 / Modbus RTU）之间的收发。
> 前置阅读：[12-serial-monitor-plan.md](12-serial-monitor-plan.md)（现有注入方案的现状与根因）。

---

## 1. 背景与结论

### 1.1 现有实现属于「方案 F」：用户态 API Hook + 远程线程注入

| 环节 | 实现 |
|---|---|
| C# 侧 | [SerialMonitorPage.xaml.cs](../llcom/Pages/SerialMonitorPage.xaml.cs) P/Invoke `serial_monitor.dll` 的 `MonitorComm/UnMonitorComm` |
| 宿主 DLL | [serial_monitor/src/lib.rs](../serial_monitor_rs/serial_monitor/src/lib.rs)：建命名管道 `\\.\pipe\llcom_smv2_<pid>` + 共享内存 `Local\llcom_smv2_session`，把内嵌 hook DLL 释放到 `%TEMP%`，`CreateRemoteThread(LoadLibraryW)` 注入，worker 线程轮询管道回调 C# |
| 注入 DLL | [serial_monitor_hook/src/lib.rs](../serial_monitor_rs/serial_monitor_hook/src/lib.rs)：detour `kernelbase/kernel32` 的 `CreateFileW/A`、`ReadFile`、`WriteFile`、`CloseHandle`、`GetOverlappedResult`，命中串口句柄时把数据装进 8202 字节 `Udata` 同步写回管道 |

**为什么对带防护的目标必然失败**：目标进程里出现未知模块、`kernelbase` 代码段被改写、存在 `CreateRemoteThread` —— 壳（Themida/VMProtect 类）与完整性自校验一抓就中，表现就是「一监听就自己退出」。另有固有短板：必须先监听后开串口、只认 `kernel32` I/O、仅 x64、停止监听时远程 `FreeLibrary` 有崩目标风险。

### 1.2 方案排序结论（针对 CH341 + 485 + 电表）

| 顺位 | 方案 | 定位 |
|---|---|---|
| 1 | **E′：485 总线旁听**（第二个 485 节点只收不发） | 现场能接线时最优、零风险；**本次缺模块，仅记录备查（见第 4 节）** |
| 2 | **B′：com0com + hub4com 分流** | 应用无感、串口语义 100% 保留、不自研读写；**本次主攻** |
| 3 | **C：USBPcap 总线抓包** | 真正零侵入（目标进程完全看不见），但 CH341 去帧工程量大；**B′ 不成立时的兜底** |
| 4 | F：现有注入 | 保留为「兼容模式」，仅对无壳无防护程序可用 |
| 不做 | A：内核 Ports 类过滤驱动 | 需 EV 证书 + 微软签名，个人版投入产出比不成立；但 com0com 源码可作将来参考 |

### 1.3 本次执行顺序

1. **阶段 0（0.5 天，先做）**：手工搭 com0com + hub4com 分流，验证目标上位机在「端口被接管」后仍能正常抄表，并确认 RTS/DTR/流控能经分流透传。
2. **阶段 1（主攻，2–2.5 天）**：llcom 内建 B′ —— 环境探测 + hub4com 编排 + 只读监听虚拟口 + 旁路日志。
3. **阶段 2（互补，0.5 天，可并行）**：外部日志 / pcap 导入 + DL/T 645 解析，让走外部抓包的用户也能把数据拿回 llcom 解析。
4. **阶段 3（兜底，按需）**：方案 C —— 先用 Wireshark 手工验证，再决定是否自研解析。

### 1.4 开源 / 免费同类软件调研（结论：没有可直接用的「串口层无感嗅探」）

| 候选 | 工作层次 | 许可 | 无感监听已打开的口 | 结论 |
|---|---|---|---|---|
| com0com + hub4com | 内核虚拟串口 + 用户态分流 | GPLv2 | 否（要改拓扑） | **复用为分流骨架**，见第 2 节 |
| USBPcap + Wireshark | USB 根集线器下过滤驱动 | 驱动 GPLv2 / 命令行 BSD-2 | **是**，目标完全无感 | 兜底方案 C；Wireshark 官方包自带 |
| SerialTool（Duolabs） | 内核串口过滤驱动 | 免费闭源 | **是**，含 RS485 与 IOCTL 捕获 | 最快的「直接用别人的软件」路径 |
| API Monitor（rohitab） | 用户态 API Hook（注入） | 免费闭源 | 能，但同样会被反注入发现 | 仅作交叉验证工具 |
| Portmon（Sysinternals） | 老式过滤驱动 | 已废弃 | — | 仅 32 位、Vista 后不可用，排除 |
| CommMonitor / Eltima / HHD | 内核串口过滤驱动 | 商业 | 是 | 功能对标，不采用 |

为什么开源界在这个细分领域没有成品：串口层无感嗅探只有两条路——内核过滤驱动（必须签名、要处理 IRP/IOCTL 全栈）或注入目标进程（撞 AV 与反注入），两者都属「做了也没法免费分发」的类型。开源成熟度集中在**创建虚拟口（com0com）与分流（hub4com）**，而不是嗅探。

可借鉴的源码 / 设计（按价值排序）：

1. **com0com `sys/`**：完整 WDM 虚拟串口实现，`IOCTL_SERIAL_*`（SetCommState / GetCommState / WaitCommEvent / PurgeComm / EscapeCommFunction）全家桶——将来若真做内核方案，这是教科书。
2. **hub4com 的 `--route` 路由模型 + `--create-filter/--add-filters` 过滤器链**（esc32 转义、pinmap 引脚重映射）：「旁路监听不干扰」的结构可直接沿用。
3. **USBPcapDriver 的 URB 截获与 pcap 序列化**：对应方案 C 的自研解析部分。
4. **Microsoft Detours（MIT）**：若继续维护 F 方案，`DetourTransactionBegin/UpdateThread`（冻结线程）比自研 inline hook 更稳，x86/x64 通吃；当前用的 retour 也能跑。
5. **SerialTool 的存在本身**：证明「内核串口过滤驱动 + pcap 导出」可产品化（它闭源，不能借鉴代码）。

许可约束：llcom 为 Apache-2.0，com0com / hub4com / USBPcap 驱动均为 GPLv2。只能以**外部进程调用 + 用户自装**方式使用，**不打包、不并入源码**（USBPcapCMD 是 BSD-2，仅这一块可借鉴代码）。

---

## 2. 方案 B′：com0com + hub4com 分流对接

### 2.1 原理与数据流（hub4com 分流，不自研桥接）

```
上位机（配置里仍写 COM3）
      │  ReadFile / WriteFile（完整 COM 语义：波特率、8E1、WaitCommEvent、EscapeCommFunction 照旧）
      ▼
COM3（com0com 第一对：A 端）
      ⇅  驱动内核内零拷贝
CNCB0（com0com 第一对：B 端，由 hub4com 独占打开）
      │
      ├─── hub4com（用户态分流进程：一进多出）
      │            └──► CNCB1 ⇅ COM11 ── llcom 只读监听
      ▼
COM9（真实 CH341 端口，设备管理器里改号而来）
      │
      ▼
CH341 → RS-485 A/B ── 电表
```

要点：
- **不写桥接代码**：读写、时序、信号路由全交给 hub4com（20 年历史、工控常用），llcom 只打开分流出来的一个虚拟口读数据。
- **上位机侧完全无感**：端口号、波特率、校验、DTR/RTS 用法都不变，不注入、不改目标代码、不加模块——这是对带壳 / 自校验软件有效的根本原因。
- **真实设备端口号必须改**（COM3 → COM9），一次性操作；改号后上位机配置仍写 COM3。
- **帧边界不再由 OS 保证，但不需要**：hub4com 可能批量转发，而 [Dlt645FrameAssembler](../llcom/Services/Dlt645/Dlt645FrameAssembler.cs) 已处理前导 FE、粘包、分包、噪声与校验失败，帧重组在 llcom 内完成。
- 代价：多一个常驻进程（hub4com）+ 上位机端口号变更；llcom 退出时需清理 hub4com。

### 2.2 端口接管步骤（向导要做的事）

1. **检查环境**：枚举串口设备友好名，检测是否已安装 com0com（名字含 `com0com`）；未安装则给出下载与安装指引（**com0com 为 GPL，我方不打包，由用户自行安装**）。
2. **选真实端口**：列出当前串口（串口设备名 + `PnP` 设备描述 + 是否为 CH34x），让用户选择真实模块所在端口，例如 COM3。
3. **改号指引**：引导把真实端口改为一个空闲号（如 COM9）：
   - 首选：设备管理器 → 端口属性 → 端口设置 → 高级 → 端口号（截图式说明）；
   - 可选自动化：写 `HKLM\SYSTEM\CurrentControlSet\Enum\<DeviceInstance>\Device Parameters\PortName`，需管理员权限，失败时回落到手工指引。
4. **创建两对虚拟口**：执行 com0com 自带的 `setupc.exe`（或提示手工打开 `Setup Command Prompt`）：
   ```
   setupc.exe install PortName=COM3  PortName=CNCB0    ; 上位机侧 ↔ hub4com
   setupc.exe install PortName=COM11 PortName=CNCB1    ; llcom 侧 ↔ hub4com
   ```
   成功判据：`setupc.exe list` 里两对都在；COM3 指向虚拟口、COM11 可被 llcom 打开。
5. **启动分流并自检**：启动 hub4com（参数见 2.3），打开 COM11 与 COM9 各发一次空操作，确认三方都能打开且字节计数一致。

> 若上位机**自动扫描所有串口**找表，可能绕过分流直连 COM9：向导结束前跑一次「5 秒静默检测」，若发现 COM9 上有第三方进程的 I/O（可用 `NtQuerySystemInformation` 句柄枚举近似判断，或直接观察字节数不匹配）则提示用户。

### 2.3 hub4com 编排参数与 llcom 侧职责

llcom **不实现任何字节转发逻辑**，只做三件事：探测环境、拉起/回收 hub4com 子进程、把分流口当普通串口读。

**hub4com 启动参数（草案，T0 阶段必须按现场 `hub4com --help` 核对）**

```
hub4com --route=0:All --route=1:0 --route=2:0 --octs=off \\.\COM9 \\.\CNCB0 \\.\CNCB1
```

端口下标语义：

| 下标 | 端口 | 角色 |
|---|---|---|
| 0 | `COM9` | 真实 CH341 端口（数据源） |
| 1 | `CNCB0` | 上位机侧虚拟口（上位机在 COM3 上读写，落到这里） |
| 2 | `CNCB1` | llcom 侧虚拟口（只读旁路） |

关键参数：

| 参数 | 作用 | 是否必须 |
|---|---|---|
| `--route=0:All` | 真实口收到的数据复制给 1 和 2 | 必须（hub4com 默认 `All:All` 全互联，必须收窄） |
| `--route=1:0` / `--route=2:0` | 两个虚拟口的写操作都只去真实口 | 必须 |
| `--octs=off` | 关闭默认 CTS 握手 | **必须**（485 模块 CTS 常悬空，否则「能收不能发」） |
| `--no-default-fc-route=All:All` | 不把流控状态广播给所有口 | 视目标软件是否用流控 |
| `--create-filter` / `--add-filters` | esc32 转义、pinmap 引脚重映射（RTS 直通） | 目标软件用 RTS 控 485 方向时必须，语法以 `--help` 为准 |

**llcom 侧职责**

1. **环境探测**：判断 com0com 是否已装（枚举端口友好名含 `com0com`，或查注册表）；在 llcom 目录 / `PATH` / `Program Files` 下定位 `hub4com.exe`，找不到给下载指引（GPL，不随包分发）。
2. **编排**：隐藏窗口 `ProcessStartInfo` 拉起 hub4com，参数按上表拼接；stdout/stderr 收进日志便于排障。
3. **监听**：llcom 打开 `CNCB1`，复用现有串口读取链路；每块数据带 `Stopwatch` 时间戳 + 方向，走 [Tools.Logger.ShowDataRaw](../llcom/Tools/Logger.cs)，显示格式/配色与 [SerialMonitorPage.OnMonitorData](../llcom/Pages/SerialMonitorPage.xaml.cs#L68-L104) 一致。
4. **串口参数**：虚拟口不传递线路参数，llcom 侧必须按上位机配置手选（DL/T 645 常用 `2400 8E1`、Modbus RTU 常用 `9600 8N1`），并提示「必须与上位机一致」。
5. **只读**：llcom 侧不向分流口写任何字节，杜绝干扰真实总线。
6. **生命周期**：停止分流 / 退出 llcom / 异常退出都要结束 hub4com，用 Job Object 兜住异常退出，避免残留进程占住虚拟口。

**自检与告警**

| 检测 | 处理 |
|---|---|
| hub4com 进程非主动退出 | 顶部红条 `SerialBridgeExitUnexpected` + 原始 stdout/stderr 折叠展示 |
| 两侧字节数偏差（累计） | 黄条：`已发送 N 字节，对侧仅收到 M 字节（丢 K）`，提示检查波特率是否一致 |
| 单方向连续 3 s 无数据 | 提示 `未检测到上行数据，确认上位机已打开 COM3 且波特率与分流一致` |
| 真实端口被占用（上位机直连 COM9） | 提示「真实端口被占用，可能上位机仍在直连 COM9」 |
| 检测到 RTS/CTS 硬件流控 | 提示「该上位机使用硬件流控，分流无法可靠透传，建议改用方案 C」 |

### 2.4 UI 设计

新增独立页面 `llcom/Pages/SerialBridgePage.xaml`（**不改动** `SerialMonitorPage`，避免与 DLL 注入逻辑纠缠），并在 [ToolCatalog.cs](../llcom/Services/Tooling/ToolCatalog.cs#L21) 注册：

```csharp
new("serial-bridge", "SerialBridgeHeader", "串口旁路监听", "Pages/SerialBridgePage.xaml"),
```

页面结构（三步向导式）：

1. **环境**：com0com 状态卡（已装/未装 + 安装指引）、hub4com 定位结果（路径 / 未找到 + 下载指引）、真实端口下拉（CH34x 加标记）。
2. **接管**：改号说明 + 两条 `setupc install` 命令展示/执行、`setupc list` 回显、结果自检。
3. **分流**：开始/停止按钮、hub4com 命令行预览（可复制）、串口参数选择、实时落日志开关、进程状态灯。

新增 i18n key（`zh-CN.xaml` / `en-US.xaml`）：`SerialBridgeHeader`、`SerialBridgeHint`、`SerialBridgeCom0comMissing`、`SerialBridgeHub4comMissing`、`SerialBridgeStart`、`SerialBridgeStop`、`SerialBridgeExitUnexpected`、`SerialBridgeMismatch`、`SerialBridgeNoData` 等。

### 2.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| **国内工业软件校验转换器**（SetupAPI/WMI 查 VID/PID，发现是虚拟口就拒绝） | 高 | 阶段 0 先手工搭拓扑实测目标软件；若被拒 → 转方案 C。后续可研究伪造设备描述 |
| **com0com 在 Win10/11 装不上或加载失败**（Code 56 / 黄色感叹号，签名策略收紧） | **高** | 三级解法：① 通过 Windows Update 可选驱动「Vyacheslav Frolov - CNCPorts」取微软签名版本；② `bcdedit /set testsigning on` + 重启（若 Secure Boot 开启须先关闭）；③ 兜底改用商业虚拟串口驱动（VSPD 等）。安装均需管理员权限 |
| **上位机用 RTS 手动控制 485 方向（DE/RE）** | 中 | 亚比特级时序无法经 USB 透传 → 检测到握手/方向异常即提示改用 C 或 E′ |
| **上位机自动扫描串口**直连真实口，绕过分流 | 中 | 2.2 第 5 步静默检测 + 字节数偏差告警 |
| **hub4com 版本 / 参数语法差异**（不同版本 `--route` 与过滤器写法不一致） | 中 | T0 固定一个版本并把实测参数写回 2.3；启动前用 `--help` 探测能力，不支持的参数不拼进命令行 |
| **hub4com 残留进程占住虚拟口** | 中 | Job Object 兜住异常退出 + 启动前清理同名进程 + 虚拟口打开失败时给出明确提示 |
| 上位机按 `ReadIntervalTimeout` 切帧，分流引入抖动 | 中 | hub4com 收到即转发、不做缓冲；阶段 0 用真实抄表验证帧完整性（DL/T645 校验和、Modbus CRC 全通过才算过） |
| 关闭 llcom 后上位机仍指向 COM3 → 通信中断 | 低 | 页面与文档明示「分流依赖 llcom 运行」；退出前提示正在分流 |
| 分流增加 ~0.3–2 ms 延迟 | 低 | 电表速率低（2400/9600/19200）、帧间静默 4–16 ms，可接受；实测确认 |
| GPL 传染 | 低 | 不打包 com0com / hub4com，只调用本地已装程序或提示用户自行安装 |

### 2.6 验收清单

| 场景 | 期望 |
|---|---|
| 手工搭好拓扑，PuTTY 打开 COM3 发 `68 ... 16` | llcom 侧立即显示 `→` 方向数据，模块实际发出（485 自环验证） |
| 真实上位机 + 电表，9600 8E1 抄表 100 帧 | 帧完整、零重试，llcom 内现有 DL/T 645 解码器全部解析正确 |
| 分流运行中拔插 CH341 | llcom 不崩、hub4com 退出被捕获并提示，重插后可重启分流 |
| 两侧波特率故意设错 | 出现字节数偏差告警（不静默失败） |
| 停止分流 | hub4com 退出、虚拟口释放，上位机可重新直连真实口 |
| 任务管理器强制结束 llcom | 无 hub4com 残留进程，虚拟口可再次打开 |
| 带壳/带加密狗的上位机 | 不退出、不报错（核心目标） |

### 2.7 工期估算

| 任务 | 工期 |
|---|---|
| 阶段 0 可行性验证（手工 com0com + hub4com + 目标上位机实测）+ 结论记录 | 0.5 天 |
| `Tools/SerialFanoutService.cs`：com0com/hub4com 探测、`setupc` 编排、hub4com 子进程启停（Job Object）与残留清理 | 1 天 |
| 监听侧：复用现有串口读取 + 日志通道 + 字节计数自检 | 0.5 天 |
| `SerialBridgePage` UI + i18n + `ToolCatalog` 注册 | 0.5 天 |
| 自检告警 + 真机抄表回归 + 文档/测试清单 | 0.5 天 |
| （可选，可并行）阶段 2：外部日志 / pcap 导入 + DL/T 645 解析 | 0.5 天 |
| **合计** | **2–2.5 天** |

---

## 3. 方案 C：USBPcap 总线抓包（兜底）

### 3.1 阶段 C0：零代码验证（0.5 天）

1. 安装 USBPcap 1.5.x + Wireshark 4.x（管理员）。
2. 选中 CH341 所在的 USB 根集线器接口开始抓包（部分控制器/版本需重插模块才会抓到，注意会打断正在进行的抄表）。
3. 过滤：`usb.device_address == <CH341>`，观察 bulk IN / bulk OUT。
4. **判据**：能直接在十六进制里看到 DL/T 645 帧 `68 A0..A5 68 ... CS 16`、或 Modbus 请求 `01 03 ... CRC`。
   - 能看到 → 方案 C 可行，进入 3.2；
   - 看不到/包不完整 → 记录现象（控制器型号、CH341 PID），不投入。

### 3.2 阶段 C1：命令行抓包 + 自研解析（按需）

1. `USBPcapCMD.exe` 输出 pcapng（后台进程，llcom 启停）。
2. 自研解析：pcapng 块 → USBPcap header（含 endpoint/direction/transfer 类型）→ 取 bulk payload。
3. **CH341/CH340 去帧**（主要工作量，按 PID 区分芯片）：
   - 常见 PID：CH340 `0x7523`、CH341 `0x5512/0x5523`、CH343 `0x55D3`、CH9102 `0x55D4`；
   - IN 包首字节通常是调制解调器状态位 → 必须剥离；
   - OUT 包在 CH341 上可能带命令/状态字节、单包上限 32 字节 → 需按芯片分包重组；
   - 参考实现：Linux `drivers/usb/serial/ch341.c`、`ch34x.c`（开源，可对照握手与 AT 命令差异）。
4. 解析结果复用现有 `DataShowRaw` 显示；方向来自 bulk IN/OUT。

### 3.3 风险与工期

| 项 | 说明 |
|---|---|
| 驱动安装 | 需管理员；部分 xHCI 控制器兼容性差，可能需重插设备 |
| 抓包体积 | 抓的是整个根集线器流量，长时抓包文件大，需限时/限设备 |
| 去帧正确性 | 芯片型号/固件差异会导致错位，需要「帧头 0x68 + 校验和」自校验兜底 |
| 工期 | C0 0.5 天；C1 3–6 天（含 4 种芯片去帧与校验） |

### 3.4 验收清单（C1）

| 场景 | 期望 |
|---|---|
| CH341 9600 8E1 抄表 | 还原出的字节流与「方案 B′ 分流」记录逐字节一致 |
| 帧头/校验和自校验 | 全部帧通过，无错位 |
| 长时抓包 10 分钟 | llcom 内存不涨、不丢帧 |

---

## 4. 备选：E′ · RS-485 总线旁听（记录备查）

- 做法：第二个 485 转换器（自动方向、空闲高阻）的 `A/B` 并联到总线，只收不发，llcom 直接当普通串口打开。
- 优点：零注入、零驱动、零时序影响，半双工共用一对线所以两个方向都能看到。
- 缺点：方向需按协议推断（DL/T 645 看地址字段：主站 `AA/99…99` vs 表地址；Modbus 按请求-响应配对）；需要额外模块与 1 个 COM 口。
- 本次暂不做（工位无备用模块、COM 口紧张），但值得保留为「现场排障首选」。

---

## 5. 与现有注入方案（F）的关系

- **保留**，但文案与默认行为改为「兼容模式：仅适用于无壳、无自校验的普通程序」，并在页面上引导：带防护的上位机请用「串口旁路监听」。
- 方案 B′/C 落地后，[12-serial-monitor-plan.md](12-serial-monitor-plan.md) 中的 P1/P2（接管已打开句柄、32 位支持）优先级下调——B′/C 天然没有位数与「先开串口」问题。

---

## 6. 明确的非目标

- 不做内核驱动（A 方案）：签名、蓝屏、多驱动栈兼容成本过高。
- 不覆盖「厂商 DLL 直连 USB、不走 COM 号」的软件（B 无效；C 可看但需按厂商协议解码，暂不投入）。
- 不改 `serial_monitor` 的注入实现（除必要的文案/风险提示）；本计划只新增旁路能力。

---

## 7. 待确认

1. 目标上位机是否**自动扫描串口**（会不会直连真实口绕过分流）？
2. 目标上位机是否校验转换器型号（VID/PID/设备描述）？
3. 分流的串口参数（波特率/校验）是否固定为已知值，还是需要「自动嗅探」？
4. 是否接受「分流期间必须保持 llcom 运行」？（否则 hub4com 需交给独立服务/计划任务托管，工期 +1～2 天）
5. com0com 由用户自行安装是否可接受？（GPL，不能随包分发；要随包分发只能用商业授权的虚拟串口驱动）
6. `hub4com.exe` 是否随 llcom 分发？（同 GPL 约束，建议只做「定位 + 下载指引」；若要随安装包分发，需在安装器单列第三方组件与 GPL 声明）
7. 目标现场是否具备管理员权限（装驱动、跑 `setupc`、必要时 `bcdedit` 开测试签名）？

---

## 8. 执行任务清单

- [ ] T0：阶段 0 手工验证（com0com + hub4com + 目标上位机 + 电表），产出「能否被接管」「RTS 方向能否透传」「锁定 hub4com 版本与参数」结论，写回 2.3 / 2.5
- [ ] T1：`Tools/SerialFanoutService.cs`（com0com 与 hub4com 探测、`setupc` 编排、hub4com 子进程启停 + Job Object 兜底 + 残留清理）
- [ ] T2：`Pages/SerialBridgePage.xaml(.cs)` + `ToolCatalog` 注册 + i18n（三步向导）
- [ ] T3：监听虚拟口（复用现有串口读取与 `Tools.Logger.ShowDataRaw`，带时间戳/方向/字节计数）
- [ ] T4：自检告警与回退指引（hub4com 异常退出、字节数偏差、无数据；提示转方案 C / E′）
- [ ] T5：真机抄表回归（DL/T 645 + Modbus RTU），补 03-test-checklist 条目
- [ ] T6：外部日志 / pcap 导入 + DL/T 645 解析（让用 SerialTool / Wireshark 的用户把数据带回 llcom）
- [ ] T7：`SerialMonitorPage` 文案改为「兼容模式」，引导至旁路页