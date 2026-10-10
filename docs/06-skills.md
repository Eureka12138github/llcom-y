# 06 · AI Agent 协作说明

> 本项目使用 AI coding agent 辅助开发。**权威约定见仓库根目录 [AGENTS.md](../AGENTS.md)**，本文档只做索引与历史说明。

## 一、权威入口

| 文档 | 作用 |
|---|---|
| [AGENTS.md](../AGENTS.md) | 环境注意事项、构建/测试命令、已知坑（agent 首要读取） |
| [02-architecture.md](02-architecture.md) | 关键设计决策：WPF Page 手写 INPC、AvalonEdit 文本桥接、Settings 防抖保存等 |
| [04-maintenance.md](04-maintenance.md) | 构建、格式化、依赖、踩坑记录 |

## 二、环境注意事项（摘要，详见 AGENTS.md）

- **本机未安装 ripgrep**：内置 `grep`/`glob` 工具返回 "Tool execution interrupted"，改用手动检索：
  - 内容搜索：`Select-String -Path <文件或 glob> -Pattern <正则>`
  - 文件查找：`Get-ChildItem -Recurse -Filter <名字>`
- **构建/测试**：SDK-style 工程，`dotnet build` / `dotnet test` 均可（命令见 04）。
- **运行约束**：exe 必须叫 `llcom.exe`，且不能在压缩包/临时目录直接运行。

## 三、历史说明

本文件原描述 `.pi/skills/` 下两个 dotnet 官方 skill（`msbuild-modernization`、`dotnet-pinvoke`）的安装与触发方式。
该目录**未随仓库提交**，故相关条目不再适用，现改写为上文索引。

- SDK-style 迁移**已完成**（见 [05-next-steps.md](05-next-steps.md) 第五节），`msbuild-modernization` 的用途已结束。
- 若后续需要 P/Invoke / 单元测试等专项 skill，可按需自行安装并在本文件登记。