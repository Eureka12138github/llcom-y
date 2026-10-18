# LLCOM 文档索引

本目录是重构后的项目文档集，**编号即推荐阅读顺序**。

| 文档 | 内容 | 适用场景 |
|---|---|---|
| [01-refactor-summary.md](01-refactor-summary.md) | 重构总结：做了什么、提交历史、修复的 bug、成果数字 | 了解重构全貌 |
| [02-architecture.md](02-architecture.md) | 架构说明：目录结构、模块职责、数据流、关键设计决策 | 上手维护 |
| [03-test-checklist.md](03-test-checklist.md) | 全功能冒烟测试清单（每项可勾选） | 发布前/改动后回归 |
| [04-maintenance.md](04-maintenance.md) | 维护指南：构建命令、代码格式、依赖、踩坑记录 | 日常开发 |
| [05-next-steps.md](05-next-steps.md) | 下一步计划：路线图、新功能/发布流程、历史评估归档 | 规划下一步 |
| [06-skills.md](06-skills.md) | 已安装 agent skills 的说明与触发方式 | 使用 agent skill |
| [07-refactor-continue-status.md](07-refactor-continue-status.md) | 「继续重构」进度与已完成记录（含当前分支基线） | 交接 / 追溯 |
| [08-code-quality-audit.md](08-code-quality-audit.md) | 代码质量审计与网络工具页去重进展 | 质量改进 |
| [09-github-publish-plan.md](09-github-publish-plan.md) | GitHub 发布与归因计划：Fork 流程、Apache-2.0 合规清单、README/CHANGES 草稿 | 准备对外发布时 |
| [10-ui-layout-plan.md](10-ui-layout-plan.md) | UI/交互优化完整计划 + 逐轮改动记录（含待办/待确认项） | 继续做界面与交互调整 |
| [11-ui-fixes-plan.md](11-ui-fixes-plan.md) | 问题 9–15 的修复计划（底栏三按钮/串口开关/发送记录弹层/日志性能） | 本轮修复实施 |
| [12-serial-monitor-plan.md](12-serial-monitor-plan.md) | 串口监听改进计划：位数支持、已打开端口接管、32 位助手、验收清单 | 后续做监听增强时 |
| [13-msix-packaging.md](13-msix-packaging.md) | MSIX 身份、自签名证书、侧载打包与签名、商店关联说明 | 需要打 MSIX 安装包时 |

## 分支说明

- **上游重构**：`origin/refactor`——WPF + net462 的 MVVM 渐进重构（上帝类拆分、去 Fody、修 8 个 bug，对应 01–06 号文档）。
- **当前开发分支**：`feature/refactor-continue`——基于 `origin/refactor` 继续工程化与架构深化（对应 07、08 号文档）。
- **全新重写路线**：`origin/next`——net10 + Avalonia + NLua（v2.0，功能未对齐，暂不采用）。

> 各步骤独立提交，可回退。构建与测试命令见 [04-maintenance.md](04-maintenance.md)。