# 新需求评估与实施顺序（#18–#26）

> 状态：**#24、#21 首批、#26 说明、#18 自动导入、CI 精简已完成；其余仅评估与计划**。  
> 本轮根据确认结果更新：主发送框已确认；投稿自动导入采用维护者 `/import`；i18n 推荐 JSON 单一源；脚本分类采用 sidecar JSON；645 首版只做 2007；多脚本方案先归档；Ctrl+C/终端模式行为暂缓。
> #18 已用真实 Discussion/PR 验证通过，测试用 `import-smoke-test.lua` 已清理；Build 已对脚本/文档变更跳过，Validate 增加 Lua 语法检查。

## 结论：推荐顺序

| 顺序 | 编号 | 需求 | 难度 | 预估 | 建议 |
|---:|---|---|---|---|---|
| 1 | #24 | 发送框上下箭头按行移动 | 小 | 已完成 | 已实现并通过单元测试 |
| 2 | #21 | Lua API 梳理与扩展 | 小–中 | 首批已完成 | 文档修正 + 7 个纯函数已做；高风险 API 后置 |
| 3 | #26 | Ctrl+C/终端模式 | 说明已完成 | — | HelpWindow 已补详细说明；是否改行为仍暂缓 |
| 4 | #18 | 社区脚本投稿自动化（`/import` → PR） | 中 | 已完成 | Action 已通过真实 PR 验证；测试脚本已清理 |
| 5 | #20 | i18n 统一管理与英文优化 | 中 | 2–4 天 | 已做少量英文修正；完整 JSON 单一源待做 |
| 6 | #25 | 脚本分类/搜索/收藏/排序 | 中 | 2–4 天 | 已确认 sidecar JSON，先试效果 |
| 7 | #22 | “小工具”增加 DL/T 645-2007 组帧 | 中–大 | 3–5 天 | 设计已归档，独立分支 `feature/dlt645-tool`；暂不做 |
| 8 | #19 | 黑暗模式 | 中–大 | 4–6 天 | 已有 `docs/14-dark-mode-plan.md` |
| 9 | #23 | 多脚本同时运行/相互调用 | 大 | 1–2 周 | 方案已归档，后续再评估 |

**#24、#21 首批、#26 说明、#18 自动导入、CI 精简已完成。** 剩余按“中等 → 大”的顺序：先 #20/#25，再 #22/#19，最后 #23。

---

## 终端模式说明（已写入 HelpWindow）

> 已补充到 `HelpWindow` 的“三、收发数据显示区”，并更新了设置项文案和提示。

### 它是什么

“终端模式”不是完整终端/命令行，也不是伪终端（PTY）。它只是把主日志/接收区域临时当作**键盘直发区**：

- 开启后，点击日志区并输入字符，字符会直接发送到当前串口；
- 不需要先切到下方发送框，也不需要点“发送”；
- 适合设备已经进入交互式命令行、Bootloader、AT 命令等需要“敲一个字符立刻发一个”的场景；
- 也方便直接发送 `Ctrl+C` 的 `0x03`、`Ctrl+D` 的 `0x04` 等控制字符。

### 当前实现行为

代码位置：`llcom/View/MainWindow.xaml.cs` 的 `uartDataFlowDocument_PreviewTextInput` 和 `uartDataFlowDocument_PreviewKeyDown`；设置默认值在 `llcom/Model/Settings.Uart.cs`（`_terminal = true`）。

- 可打印字符：通过 `PreviewTextInput` 直接按 ASCII 发送；
- `Ctrl + A..Z`：通过 `PreviewKeyDown` 发送对应的 ASCII 控制码 `1..26`；
  - `Ctrl+C` = `0x03`（ETX）
  - `Ctrl+D` = `0x04`（EOT）
  - `Ctrl+Z` = `0x1A`（SUB）
- 日志区获得焦点时，边框会变绿，作为“现在键盘会直发”的提示；
- 关闭终端模式后，日志区的键盘输入不再发送，只能通过发送框发送；
- 不会自动打开串口；串口未打开时不会发送。

### 它不是完整终端

- 不模拟 Enter、Backspace、方向键、F1–F12；
- `Ctrl+V` 也会被当作控制码 `0x16`，不能可靠粘贴；
- 非 ASCII 字符会按 ASCII 编码处理，可能变成 `?`；
- 不处理光标移动、行编辑、历史命令等终端功能。

### 注意事项

- 终端模式**默认开启**；点击日志区后误触键盘，可能向设备发送意外数据；
- 终端模式下 `Ctrl+C` 的终端语义是发送 `0x03`，与“复制”快捷键冲突；
- 复制日志时如果不想发送 `0x03`，当前最稳妥的做法是先关闭终端模式；
- 后续是否改成“有选区时复制、无选区才发送 ETX”需要单独评估。

### 建议加入 HelpWindow 的文案

可在“二、串口设置”或“三、主界面与收发日志”中增加一小节：

> **终端模式（键盘直发）**  
> 开启后，点击上方日志/接收区域再敲键盘，字符会直接发送到串口，不需要经过发送框。它适合 Bootloader、AT 命令、交互式命令行等场景，也方便发送 `Ctrl+C`（0x03）等控制字符。  
> 注意：这不是完整终端，不模拟 Enter、Backspace、方向键；`Ctrl+V` 会被当作 0x16 发送；非 ASCII 字符可能变成 `?`。终端模式默认开启，误触键盘可能向设备发送数据。  
> 终端模式下 `Ctrl+C` 会发送 0x03，和复制快捷键冲突；复制日志时建议先关闭终端模式。  
> 如果只是普通收发数据，建议关闭终端模式，使用下方发送框。

同时建议把设置项文案从“终端模式（选中数据区并按键盘发送数据）”改成更直白的“**终端模式（在日志区直接键盘发送）**”，并加 ToolTip。

### 语言资源

需要新增/调整的资源键（实际命名实现时再定）：

- `SettingTerminal`：终端模式（在日志区直接键盘发送）
- `SettingTerminalTip`：开启后点击日志区即可直接发送键盘字符；`Ctrl+C` 会发送 0x03
- `HelpTerminalTitle`
- `HelpTerminalWhat`
- `HelpTerminalBehavior`
- `HelpTerminalLimit`
- `HelpTerminalCaution`

---

## #26 暂缓：Ctrl+C 与终端模式

### 新发现

- 关闭终端模式后不再发送 `0x03`，说明问题只存在于终端模式；
- 终端模式是上游遗留功能，默认开启，但设置项和使用说明都没有解释清楚；
- `Ctrl+C` 在终端语义中本来就是发送 ETX (`0x03`)，所以这**不一定是 bug**，更像是“终端快捷键”和“复制快捷键”的冲突。

### 重新评估

| 角度 | 判断 |
|---|---|
| 终端语义 | `Ctrl+C = 0x03` 是预期行为，不应简单视为错误 |
| 普通用户 | 默认开启且无说明，容易在复制日志时误发数据 |
| 复制体验 | 终端模式下按 Ctrl+C 同时复制并发送，确实干扰使用 |
| 功能价值 | 对 Bootloader/AT/命令行设备有用，不应直接删除 |
| 当前结论 | 先补说明；是否改代码、怎么改，等实际使用反馈 |

### 可选方案（暂不实施）

- **A. 保持现状 + 文档说明**：终端模式就是发送 ETX；复制前关闭终端模式。改动最小，但体验一般。
- **B. 选区优先**：有选中文本时 Ctrl+C 只复制不发；无选区时仍发送 `0x03`。兼顾两者，推荐作为后续候选。
- **C. 始终复制 + 独立发送按钮**：Ctrl+C 永远复制；另设“发送 0x03”按钮或 `Ctrl+Shift+C` 发送 break。会改变终端习惯。
- **D. 默认关闭**：终端模式改为显式开启，减少误触；但要考虑老用户习惯。

**说明已补进 HelpWindow；行为是否修改仍暂缓。**

---

## #24 发送框上下箭头按行移动

### 已确认

主发送框就是 `llcom/View/MainWindow.xaml` 的 `toSendDataTextBox`（图中红框，`00030085` 所在输入框）。当前 `ToSendDataTextBox_PreviewKeyDown` 只处理 Enter。

### 实施记录

- 新增 `llcom/Tools/TextCaretNavigator.cs`：按逻辑行计算上下移动目标，处理 CRLF/CR/LF、空行、短行、末尾无换行和越界光标。
- `MainWindow.ToSendDataTextBox_PreviewKeyDown` 拦截无修饰键的 Up/Down；Shift/Ctrl/Alt 组合保留 WPF 默认行为。
- 新增 `tests/llcom.Tests/TextCaretNavigatorTests.cs`，13 个边界用例。
- 验证：x64/x86 Debug 构建均为 0 错误；`dotnet test` 57/57 通过。

### 实现规则

只拦截 `Key.Up` / `Key.Down`，其余键完全交给默认行为：

- 当前行：`GetLineIndexFromCharacterIndex(CaretIndex)`；
- 当前列：`CaretIndex - GetCharacterIndexFromLineIndex(line)`；
- 上一行/下一行：`line ± 1`；
- 目标行起始：`GetCharacterIndexFromLineIndex(targetLine)`；
- 目标行长度：`GetLineLength(targetLine)`（行尾不含换行符）；
- 目标位置：
  - 目标行长度 < 当前列，或目标行为空：目标行行首；
  - 否则：目标行起始 + 当前列；
- 第一行按上：`CaretIndex = 0`；
- 最后一行按下：`CaretIndex = Text.Length`；
- 设置 `SelectionLength = 0`，必要时 `ScrollToLine`；
- `e.Handled = true`。

### 边界

- `\r\n` / `\n`；
- 文本末尾没有换行；
- 光标在行尾；
- 目标行比当前行短；
- 空文本、空行兜底；
- `TextWrapping=Wrap` 时按**逻辑行**而不是视觉折行计算；
- 不改变左/右箭头、删除、输入、Enter 发送逻辑。

建议把“计算目标 CaretIndex”抽成纯函数 `TryGetVerticalCaretIndex(...)`，便于单元测试；UI 事件只负责调用。

**估算：0.5–1 天，含边界测试。**

---

## #22 “小工具”增加 DL/T 645-2007 组帧

### 已确认

首版只做 **DL/T 645-2007**，暂不兼容 1997。  
详细设计已单独记录在 [`docs/16-dlt645-tool-design.md`](16-dlt645-tool-design.md)，并补充“用 `AAAAAAAAAAAA` 通配地址自动读取电表地址”的通讯连接流程。该功能放在独立分支 `feature/dlt645-tool`，等确定开发时再做。

### 推荐范围

先做“**组帧 + 发送**”，再做“**解析应答**”：

1. **帧构造**
   - 表地址：12 位 BCD（6 字节，低字节在前）；
   - 控制码：读数据、读地址、写数据等常用项；
   - 数据标识 DI：4 字节；
   - 数据域：HEX 输入；
   - 可选前导 `FE` 数量；
   - 自动计算长度 `L` 和校验和 `CS`；
   - 生成完整帧预览，支持复制、发送到当前串口。
2. **应答解析（第二阶段）**
   - 校验 `68 ... 16`、长度、校验和；
   - 拆分地址、控制码、数据域；
   - 显示原始 HEX 和字段解释；
   - 常见 DI 可做可选解码，但不作为首版硬要求。

### 页面建议

- `llcom/Pages/Dlt645Page.xaml` / `.xaml.cs`；
- 在 `ToolSelector` 增加“DL/T 645 组帧”；
- 字段校验：地址必须 12 位数字/BCD，DI 必须 4 字节，数据域必须是合法 HEX；
- 发送前检查串口是否打开，复用现有 `Tools.Global.uart` / `SerialPortService`；
- 增加中英文资源键，后续黑暗模式自动复用主题 token。

### 风险

- 地址 BCD 高低位、校验和范围容易写错，需要用已知 2007 帧做单元测试；
- 真实电表应答可能有前导 FE、异常控制码，解析要容错。

**估算：组帧 1–2 天；加应答解析和模板 2–3 天。**

---

## #18 社区脚本投稿自动化

### 已确认

采用：**维护者在 Discussion 评论 `/import`，由 GitHub Action 生成 PR**。  
不采用“讨论一创建就自动导入”，也不自动合并未审查的脚本。

### 推荐流程

1. 用户在 **Script submissions** 分类提交表单；
2. 维护者确认内容后评论 `/import`；
3. Action 读取 discussion 标题、正文和评论；
4. 解析表单字段（名称、作者、saveTo、版本、描述、备注、Lua 代码块）；
5. 生成安全文件名（slug），校验：
   - 非空、大小上限；
   - UTF-8、无二进制；
   - 不自动执行 Lua；
   - 可选 `luac -p` 语法检查；
6. 更新 `community-scripts/index.json`；
7. 创建分支和 PR（不直接合入）；
8. 在 Discussion 回复结果；失败时说明原因。

### 技术要点

- 事件：`discussion_comment`；判断评论内容是否为 `/import`；
- 权限：仅仓库维护者/写权限用户可触发；
- 读取：GraphQL 获取 discussion 正文；
- 写入：`contents: write`、`pull-requests: write`；
- 不引入 App 内的 GitHub token，避免权限和安全问题；
- 保留手动处理兜底。

### 已完成

- `.github/workflows/import-community-script.yml`：仅维护者评论 `/import` 触发；
- `.github/scripts/import_community_script.py`：解析表单、生成 `.lua`、更新 `index.json`、创建 PR、回复 Discussion；
- 更新投稿模板和 `community-scripts/README.md`；
- 脚本不执行 Lua，限制文件大小，拒绝同名脚本，错误会回复到 Discussion；
- 本地已用示例表单测试解析（dry-run）。

已完成真实链路验证：Discussion 表单 → `/import` → Action → PR #4 → 合并；测试用 `import-smoke-test.lua` 已从本地和 `index.json` 清理。`Validate community scripts` 现已包含 `luac5.3 -p` 语法检查，PR 阶段只需这一个检查；Build 对脚本/文档变更跳过。

分支自动清理：
- PR 关闭/合并后，`Cleanup import branch` workflow 删除对应的 `community-scripts/import-*` 分支；
- 创建 PR 失败时，导入脚本会尝试删除本次失败分支；
- 建议同时开启仓库的 **Automatically delete head branches**。

**估算：1–2 天（Action + 解析 + PR + 文档）；已完成。**

---

## #20 i18n 统一管理与英文优化

### 现状

- `llcom/languages/zh-CN.xaml` 与 `en-US.xaml` 各 **292 个 key**，key 完全对齐；
- 已修正 `ChangeLanguage` 的英文值和终端模式相关文案；其余英文质量仍需逐条审校，例如 `QuickSendRecvScript = Rcvs` 等缩写不自然；
- 仍有硬编码文本：
  - `HelpWindow.xaml` 整页中文硬编码；
  - `ProfileInitializer`、`AppUpdateService`、`SerialMonitorPage` 等错误提示；
  - `Settings.QuickSend` 的默认名称“未命名0…”；
  - 部分 XAML 的 `Text`/`Header`/`Content` 直接写死。
- 当前语言切换通过替换 `MergedDictionaries[0]` 实现，机制可以保留。

### CSV 与 JSON 的选择

**推荐 JSON 作为唯一源，CSV 作为可选的翻译导出格式。**

原因：

- JSON 的转义/UTF-8/嵌套结构更可靠，适合作为程序读取的 canonical source；
- 支持每个 key 附带 `note`、占位符说明等元数据；
- 更容易做 schema 校验、重复 key 检测、CI 差异检查；
- CSV 适合 Excel 编辑，但多行文本、引号、逗号、BOM 容易出问题，且不适合嵌套/元数据；
- 实际流程可以是“**JSON 为源 → 生成 XAML → 可选导出 CSV 给翻译者审阅**”。

推荐结构：

```json
{
  "schema": 1,
  "defaultLanguage": "zh-CN",
  "languages": ["zh-CN", "en-US"],
  "messages": {
    "AppTitle": {
      "zh-CN": "LLCOM-Y - 用 Lua 调试串口!",
      "en-US": "LLCOM-Y - Debug serial port with Lua!",
      "note": "窗口标题"
    }
  }
}
```

配套：

- `scripts/generate-i18n.ps1`：从 JSON 生成 `zh-CN.xaml` / `en-US.xaml`；
- `scripts/export-i18n-csv.ps1`：可选，导出 CSV 供表格审阅；
- CI 检查：
  - key 是否重复/缺失；
  - 每种语言是否有空值；
  - 英文列是否仍含中文；
  - 生成文件是否与 JSON 同步；
- 生成文件仍提交到仓库，保证 WPF 打包和运行时不需要额外解析器。

### 实施步骤

1. 把现有 291 个 key 导入 JSON；
2. 扫描硬编码 UI 文本，补 key 并替换；
3. 英文逐条审校（术语、按钮宽度、换行）；
4. 加入生成脚本和 CI 校验；
5. 语言切换回归；
6. 可选：给 `LocalizationService` 增加缺 key 时的 fallback 日志。

### 风险

- 长文本换行、占位符顺序、英文长度撑破紧凑按钮；
- `HelpWindow` 这类长文本工作量大，建议单独一批；
- 不要改变现有 `DynamicResource` key 名，避免大范围回归。

**估算：工具链 0.5–1 天；硬编码提取 1–2 天；英文审校 0.5–1 天；合计 2–4 天。**

---

## #21 Lua API 梳理与扩展

### 当前可调用接口

**C# 层：**

- `apiSend(channel, data[, table])`
- `apiSetCb(channel, callback)`
- `apiUnsetCb(channel, callback)`
- `apiSendUartData(string)`（旧接口）
- `uartReceive`（旧回调）
- `apiGetPath()`
- `apiUtf8ToHex(str)`
- `apiQuickSendList(id)`
- `apiInputBox(prompt, default, title)`
- `apiAddPoint(num, line)`
- `apiAscii2Utf8(bytes)`
- `apiPrintLog(string)`
- 新增：`apiSum8(data)`、`apiXor8(data)`、`apiCrc16Modbus(data)`、`apiBase64Encode(string)`、`apiBase64Decode(string)`、`apiNowMs()`、`apiGetVersion()`

**Lua 层：**

- `string.toHex` / `fromHex` / `toValue` / `utf8Len` / `formatNumberThousands` / `split` / `urlEncode`
- `log.trace/debug/info/warn/error/fatal`
- `sys.wait` / `waitUntil` / `waitUntilExt` / `taskInit`
- `sys.timerStart` / `timerLoopStart` / `timerStop` / `timerStopAll` / `timerIsActive`
- `sys.subscribe` / `unsubscribe` / `publish`

### 已完成（首批）

- 修正 `LuaApi.md` 的 `apiData` 笔误；
- `AddPoint` 标题改为真实绑定名 `apiAddPoint`；
- 补充 `apiAscii2Utf8`、`apiPrintLog` 文档；
- 新增 7 个低风险纯函数/辅助 API，并在 `LuaLoader` 中绑定；
- 新增 `LuaApisTests`，覆盖 Sum8/Xor8/CRC16/Base64/时间/版本。

### 文档问题

- 旧接口应标注废弃版本和替代方案；
- `apiJsonEncode` 等复杂 API 仍未实现。

### 建议新增（按风险从低到高）

**低风险、纯函数：**

- `apiToBcd` / `apiFromBcd`；
- `apiFormatTime(format)`；
- `apiJsonEncode(table)` / `apiJsonDecode(str)`；
- 其余 CRC 变体。

**中风险、只读：**

- `apiSerialIsOpen()` / `apiSerialPortName()` / `apiSerialBaud()`；
- `apiChannelList()`；
- `apiGetSetting(key)`（只读白名单）。

**中风险、沙箱文件：**

- `apiReadFile(path)` / `apiWriteFile(path, data)`，限制在 `ProfilePath` 下；
- 禁止任意路径、进程启动、注册表、网络原始 socket。

**高风险、不建议首版开放：**

- `apiOpenSerial` / `apiCloseSerial` / `apiSetBaud`；
- 任意文件系统、系统命令、窗口控制。

原则：**只新增、不破坏旧签名**；所有新 API 写文档和示例；文件类 API 必须做路径规范化与白名单。

**估算：首批已完成；剩余复杂 API 1–2 天。**

---

## #25 脚本分类、搜索、收藏与排序

### 已确认

采用 **sidecar JSON 元数据，文件本身保持平铺**，先试效果。

### 现状

- 主脚本页用 `ComboBox`（`luaFileList`）展示 `LuaEditorViewModel.Files`；
- 文件平铺在 `user_script_run/`，没有分类、搜索、收藏、排序；
- 发送/接收转换脚本编辑器复用同一套 VM，也面临同样问题。

### 推荐设计

在脚本目录增加 `scripts.index.json`：

```json
{
  "version": 1,
  "scripts": [
    {
      "file": "crc16.lua",
      "category": "Modbus",
      "favorite": true,
      "order": 10,
      "description": "Modbus CRC16"
    }
  ]
}
```

- 文件仍平铺，避免破坏现有路径；
- 启动时扫描 `.lua`，与 JSON 合并；缺失项自动补默认值；
- 删除文件后清理孤立元数据；
- 分类为单值字符串，收藏为 bool，排序为整数；
- UI：
  - 搜索框（文件名/描述/分类）；
  - 分类下拉；
  - “只看收藏”开关；
  - 列表项显示星标，点击切换；
  - 拖拽排序，写回 `order`；
  - 保留当前选中项和“上次打开脚本”设置。
- 发送/接收转换脚本可复用同一个目录浏览器组件，但各自使用自己的 index。

### 分期

- MVP：搜索 + 分类 + 收藏（约 1–2 天）；
- 完整：拖拽排序、右键重命名/移动、批量清理（约 2–4 天）。

**估算：2–4 天。**

---

## #19 黑暗模式

已有独立计划：`docs/14-dark-mode-plan.md`。

简要结论：

- AdonisUI 自带 Light/Dark，基础切换不难；
- 主要工作在 `DesignTokens` 调色板拆分、日志/数据区颜色、AvalonEdit、ScottPlot；
- 完整可发布约 4–6 天，MVP 约 2 天；
- 建议先做基础主题 + 日志/编辑器，再做图表和系统主题。

本项不重复展开，实施时按 14 号文档推进。

---

## #23 多脚本同时运行 / 相互调用

### 已确认

先作为方案归档，后续再决定；本项不进入近期实现。

### 现状

- `LuaRunEnv` 使用**一个静态 Lua VM**；
- `New(file)` 只加载一个脚本；
- `StopLua` 停止全部任务和定时器；
- `LuaEnv` 的 `package.path` 已包含 `user_script_run/requires/?.lua`，所以“脚本 A require 脚本 B”在**模块化写法**下已经可行；
- 但“两个独立脚本同时运行、分别停止”目前没有运行时支持。

### 三种路线

**A. 模块化 require（推荐先做）**

- 约定 `requires/` 放可复用模块；
- 模块返回 table，提供 `start/stop/onData` 等函数；
- 主脚本 `local b = require("b")`，再调用 `b.start()`；
- 改动小，适合“函数复用”，但不是两个独立脚本各自管理生命周期。

**B. 单 VM 多入口（中等）**

- 增加“运行多个脚本”入口，按顺序加载 A、B；
- 共享全局变量、定时器和消息总线，名字冲突风险高；
- 可先提供多选运行和“全部停止”，但独立停止困难。

**C. 每脚本独立 VM（大）**

- 新增 `ScriptRuntimeManager`，每个脚本一个 `LuaEnv`，独立取消令牌、定时器、日志标签和通道回调；
- UI 显示运行中脚本列表，可单独停止；
- 需要处理线程安全、XLua 对象释放、通道注册/反注册、资源上限；
- 约 1–2 周，适合作为后续架构项目。

### 建议

1. 先补 `requires` 模块规范和示例（0.5–1 天）；
2. 若只是“A 调用 B 的函数”，路线 A 足够；
3. 若确实要“A、B 同时长期运行、分别停止”，再立项路线 C；路线 B 只适合临时过渡。

---

## 建议的实施批次（按剩余难度重排）

### 已完成

- #24 上下箭头；
- #21 首批：LuaApi 文档修正 + 7 个纯函数；
- #26 终端模式说明（HelpWindow + 设置项提示）；
- #18 社区脚本 `/import` 自动导入（真实 PR 验证通过）；
- CI 精简：脚本/文档 PR 跳过 Build，Validate 增加 Lua 语法检查。

### 下一步：中等工作量（约 2–4 天）

1. #20 i18n JSON 单一源 + 英文审校；
2. #25 脚本分类/搜索/收藏/排序。

### 之后：中–大（各约 3–6 天）

- #22 DL/T 645-2007 组帧（设计已归档，独立分支 `feature/dlt645-tool`）；
- #19 黑暗模式（按 `docs/14` 分阶段）。

### 最后：大改动

- #23 多脚本运行时（先设计，约 1–2 周）。

### 暂缓

- #26 Ctrl+C/终端模式行为：说明已补，是否改代码待实际反馈。

---

## 待确认/后续

1. #24 已完成。
2. #18 自动导入已完成并验证；测试脚本已清理。
3. #20 采用 JSON 为唯一源，CSV 仅按需导出。
4. #22 已确认首版只做 645-2007，设计见 `docs/16`。
5. #25 已确认 sidecar JSON。
6. #23 已归档，后续再评估。
7. #26 行为暂缓，说明已补。

下一步建议从 **#20** 开始。
