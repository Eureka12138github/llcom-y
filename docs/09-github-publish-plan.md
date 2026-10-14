# 09 · GitHub 发布与归因计划

> 状态：**待执行**（2026-10-14 制定；尚未执行）
> 目标：把本仓库发布到**自己的** GitHub 供他人使用；**明确署名原作者**；**不推送/不改动原作者仓库**。
> 关联文档：[05-next-steps.md](05-next-steps.md)（路线图/发布流程）、[07-refactor-continue-status.md](07-refactor-continue-status.md)（当前分支与进度）。

---

## 一、背景与现状（已核实，2026-10-14）

- 本地仓库：`D:\学习\瞎鼓捣\LLCOM`，git 历史**完整保留上游**（chenxuuu/llcom）的提交。
- 远端：只有一个 `origin` = `https://github.com/chenxuuu/llcom.git`（**原作者的，不推送**）。
- 分支：
  - `feature/refactor-continue`：比 `origin/refactor` **领先 72 个提交**（当前主要开发线）。
  - 本地 `master`：比 `origin/master` 领先 51、落后 1（已合并过重构成果）。
- 授权：根目录 `LICENSE` = **Apache-2.0**；**没有** `NOTICE` 文件。
- `.gitignore` 已忽略 `artifacts/`（构建产物不会误提交）；`artifacts/` 未被 git 跟踪。
- 本机环境：**未配置 MCP**、**无 Docker**、**无 `gh` CLI**；git 凭据管理器已有 `Eureka12138github` 的 GitHub 凭据（`git push` 可直接用）。
- CI：已有 `.github/workflows/build.yml`（产物输出到 `artifacts/`）。

---

## 二、已确定的决策

1. **发布形态：GitHub Fork**（保留 “forked from chenxuuu/llcom” 徽章 = 最强归因；便于同步上游）。
   将来若要独立运营，再把 fork **detach** 成独立仓库（历史保留）。
2. **名称：保持 `LLCOM`**，不改名；但明确标注「**非官方个人修改版**」。
3. **不做**「逐步迁移到未修改的干净仓库」（没必要：git 已记录来源；重放改动只会丢历史、徒增风险）。
4. **不推送/不修改**原作者的 `origin`。
5. 若日后想给上游贡献：从 `upstream/master` 拉**小而聚焦**的分支发 PR，把大改动留在自己 fork。

---

## 三、合规清单（Apache-2.0 要求）

- [ ] 根目录 `LICENSE` **原样保留**（不得删除/修改）。
- [ ] README 顶部**声明改动**：基于 chenxuuu/LLCOM、Apache-2.0、非官方、相对上游的改动入口（§4b「State Changes」）。
- [ ] 新增 `CHANGES.md`：集中列出相对上游的主要改动。
- [ ] **保留**源码中的版权头与作者署名（不要删除原作者名字）。
- [ ] **不暗示原作者背书**（README/关于页写明“未经原作者背书”）。
- [ ] 徽章/下载链接改为指向**自己的仓库**（避免误导到官方）。
- [ ] 版本号**区分**上游（如 `1.1.4.4-mymod`，或另起 `2.x`）。
- [ ] （无 `NOTICE` 文件，暂无需随附；若上游以后新增，需一并保留。）

---

## 四、实施步骤

### A. 文档与合规（纯本地，可先做，不需要 GitHub）
- [ ] `README.md` 顶部加**归因/非官方声明块**（草稿见附录 A）。
- [ ] `README_EN.md` 加英文版声明。
- [ ] 新增 `CHANGES.md`（草稿见附录 B）。
- [ ] 修正 README 中指向官方仓库的**徽章与下载链接**（badge / nightly / release / 商店链接）→ 改为自己仓库，或删除。
- [ ] 【可选】软件界面标注：窗口标题或「关于」页加「个人修改版」（**需确认**）。
- [ ] 复核 `LICENSE` 保留、确认无 `NOTICE`。

### B. 发布到 GitHub（手动 1 次 + git 命令）
- [ ] 在 GitHub 网页对 `chenxuuu/llcom` 点 **Fork** → 得到 `<你>/llcom`。
- [ ] 改远端并推送（替换 `<你>`）：
  ```powershell
  git remote rename origin upstream
  git remote add origin https://github.com/<你>/llcom.git
  git push -u origin master
  git push -u origin feature/refactor-continue
  ```
- [ ] 把对外主推的分支设为默认（建议 `master`）。
- [ ] （可选）打 tag / 发 Release 出安装包（见 D）。

### C. 长期同步上游
```powershell
git fetch upstream
git checkout master
git merge upstream/master
```

### D.（可选）出可下载的 Release
- 已有 CI（`build.yml`）→ 打 tag（如 `v1.1.4.4-mymod`）触发构建，把产物上传到 Release；
- 或用 GitHub Releases 手传 `artifacts/Release/x64` 的 exe。

### E.（可选）让 agent 能直接操作 GitHub
- 方案 1：**GitHub MCP（远端 + PAT）**——配置片段见附录 C。
- 方案 2：装 `gh` CLI：`winget install GitHub.cli` → `gh auth login`。
- 方案 3：只用 git + 网页（最省事，零配置）。
- 注意：PAT 建议 **fine-grained、最小 scope**（`repo`；动 Actions 再加 `workflow`），用**环境变量**引用、不写入文件；MCP 配置在 `~/.config/opencode/opencode.jsonc` 的 `mcp.servers` 下，配置后可能需重启/重连会话。

---

## 五、待确认

1. 仓库形态：**Fork（推荐）** 还是独立新仓库？
2. 名称：保持 `LLCOM`（推荐）还是改名？
3. 界面是否加「个人修改版」标注（窗口标题/关于页）？
4. 版本号策略（后缀 `-mymod` / 走 `2.x` / 其他）？
5. 是否要做 Release 流水线（tag → CI → Release）？
6. 以后要不要配 **MCP / PAT / gh**（决定能否让 agent 直接操作 GitHub）？
7. 默认分支用 `master` 还是 `feature/refactor-continue`？

---

## 附录 A：README 顶部归因段草稿（中文）

```markdown
> ⚠️ **本仓库是个人修改版（非官方）**
>
> 基于 [**chenxuuu/LLCOM**](https://github.com/chenxuuu/llcom)（Apache-2.0）修改而来。
> 原作者：chenxuuu 及 [贡献者](https://github.com/chenxuuu/llcom/graphs/contributors)。
> 本仓库由下游 fork 维护，**未经原作者背书**，也不代表官方版本。
> 相对上游的主要改动见 [CHANGES.md](./CHANGES.md)。

# LLCOM（个人修改版）
```

## 附录 A2：README_EN 归因段草稿

```markdown
> ⚠️ **This is an unofficial, personally modified fork.**
>
> Based on [**chenxuuu/LLCOM**](https://github.com/chenxuuu/llcom) (Apache-2.0).
> Original author: chenxuuu and contributors. Not endorsed by the original author.
> See [CHANGES.md](./CHANGES.md) for changes relative to upstream.

# LLCOM (personal mod)
```

## 附录 B：`CHANGES.md` 草稿（初期条目）

```markdown
# 相对上游 chenxuuu/LLCOM 的主要改动

> 基线：origin/refactor（上游）。以下为下游 fork 的改动汇总，按主题归类。

## 界面 / 交互
- 收发日志：显示格式可选（字符串 / 十六进制），发送与接收**分别**控制；日志字号可调（10~40）。
- 日志底部开关图标化：Abc/Hex（发送/接收）、替换不可见字符、停止打印、锁定视图、清空日志、保存日志。
- 发送区新增：清除输入框、发送记录（可设上限 1~200）、追加换行下拉（无追加 / \n / \r / \n\r / \r\n）。
- 发送按钮右侧「发送选项」：连续发送（延时 10~3600000ms、重复次数 -1~100000）与按 Enter / Ctrl+Enter 发送。
- 快捷发送：每行可独立**定时发送**（折叠在时钟图标里，间隔 10~3600000ms）。
- 右侧新增「串口设置」页（端口/波特率/数据位/校验位/停止位/编码/自动重连/RTS/DTR/开关串口）。
- 「更多设置」去重、重命名；顶栏 ☰ 直接打开「更多设置」。
- 社区脚本列表排版修复（作者/名称/版本/简介不再重叠、过长截断并悬停显示全文）。

## 行为
- 未打开串口时点发送：不再自动打开/选择端口，只提示先打开串口（也不再缓存补发）。
- 定时发送到点而串口未开：提示并停止所有定时。
- 崩溃处理：弹出只有「关闭程序」按钮的窗口，错误写入 logs/fatal.log。

## 工程 / 构建
- 工程：SDK-style csproj + PackageReference、可空引用类型、依赖注入（AppHost）、拆分上帝类。
- 构建产物统一到仓库根 `artifacts/`（Debug/Release × x64/x86）。
- 单元测试工程（xUnit）与 CI 调整。

## 文档
- 新增/更新 `docs/`（重构总结、维护指南、对照清单）与使用说明（帮助窗口）。
```

## 附录 C：GitHub MCP 配置片段（远端 + PAT）

```jsonc
// ~/.config/opencode/opencode.jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "update": "notify",
  "skills": ["~/.opencode/skills"],
  "mcp": {
    "servers": {
      "github": {
        "type": "remote",
        "url": "https://api.githubcopilot.com/mcp/",
        "oauth": false,
        "headers": { "Authorization": "Bearer {env:GITHUB_MCP_PAT}" }
      }
    }
  }
}
```
- 另设环境变量 `GITHUB_MCP_PAT`（fine-grained，最小 scope：`repo`；动 Actions 加 `workflow`）。
- 或先试 OAuth：`opencode mcp add github --global --url https://api.githubcopilot.com/mcp/`，再用 `/mcps` 登录（GitHub 要求宿主自己注册 OAuth App，未必能直接过）。
- 加完执行 `opencode mcp list` 确认 `connected`；工具名为 `github_*`。

## 附录 D：发布命令速查

```powershell
# 一次性：改远端
git remote rename origin upstream
git remote add origin https://github.com/<你>/llcom.git
git push -u origin master
git push -u origin feature/refactor-continue

# 以后同步上游
git fetch upstream
git checkout master
git merge upstream/master

# （可选）打 tag
git tag -a v1.1.4.4-mymod -m "personal mod release"
git push origin v1.1.4.4-mymod
```
