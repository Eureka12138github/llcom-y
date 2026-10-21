# 本项目社区脚本

这里收录 **Eureka12138github/llcom-y** 的社区脚本。App 的「在线脚本」页右上角可以切换：

- **上游**：`chenxuuu/llcom` 的 Discussions 社区脚本（保持原实现）；
- **本项目**：读取本目录的 `index.json` 和对应 `.lua` 文件。

## 目录结构

```
community-scripts/
├─ index.json      # 脚本索引（App 读取）
├─ crc16.lua       # 示例：发送处理脚本
└─ ...
```

## index.json 格式

```json
{
  "schema": 1,
  "scripts": [
    {
      "name": "脚本名称",
      "author": "作者署名",
      "description": "一句话简介",
      "version": 1,
      "type": "send",
      "note": "备注 / 使用说明",
      "file": "example.lua",
      "url": "https://github.com/Eureka12138github/llcom-y/blob/master/community-scripts/example.lua"
    }
  ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | 是 | 脚本名称 |
| `author` | 否 | 作者署名 |
| `description` | 否 | 列表里显示的一句话简介 |
| `version` | 否 | 整数，默认 1 |
| `type` | 否 | `run` / `send` / `recv`，默认 `run` |
| `note` | 否 | 备注/使用说明 |
| `file` | 是* | 本目录下的 `.lua` 文件名；也可填完整 URL |
| `script` | 否* | 也可以直接把 Lua 代码内联到 JSON |
| `url` | 否 | “打开来源页面”按钮的地址；不填则自动生成 GitHub 链接 |

\* `file` 和 `script` 至少填一个。

### 类型与保存位置

| `type` | 含义 | 下载后保存到 |
|---|---|---|
| `run` | 独立运行脚本 | `user_script_run/` |
| `send` | 发送处理脚本 | `user_script_send_convert/` |
| `recv` | 接收处理脚本 | `user_script_recv_convert/` |

## 投稿

App 的「在线脚本」页切到「本项目」后，点右上角 **投稿脚本**，会打开 GitHub Issue 表单。表单填写：

- 脚本名、作者署名、类型、版本、简介、备注；
- Lua 代码；
- 原创/分享授权确认。

提交后会人工审核。审核通过后由维护者把 `.lua` 放进本目录，并在 `index.json` 增加一条记录。也可以直接提 PR。

## 审核与安全

- 社区脚本可能调用串口、文件等 API，**运行前请自行审查代码**；
- 投稿请确认你有权分享，且不包含恶意代码、密钥、个人隐私；
- 维护者保留不收录/下架的权利。

## 当前收录

- `Modbus CRC16`（`send`）：发送前追加 Modbus CRC16，和 `llcom/DefaultFiles/user_script_send_convert/crc16.lua` 内容一致。
