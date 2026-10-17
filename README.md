# LLCOM-Y

> ⚠️ **非官方个人维护版**
>
> 本项目基于 [**chenxuuu/LLCOM**](https://github.com/chenxuuu/llcom)（Apache-2.0）修改而来。
> 原作者：chenxuuu 及[上游贡献者](https://github.com/chenxuuu/llcom/graphs/contributors)。
> 由 [Eureka12138github](https://github.com/Eureka12138github/llcom-y) 个人学习与自用维护，**不是官方版本**，
> 与原作者无隶属关系，也不代表原作者背书。
> 相对上游的改动见 [CHANGES.md](CHANGES.md)。

<!-- ALL-CONTRIBUTORS-BADGE:START - Do not remove or modify this section -->
[![All Contributors](https://img.shields.io/badge/all_contributors-6-orange.svg?style=flat-square)](#contributors-)
<!-- ALL-CONTRIBUTORS-BADGE:END -->

[English readme click here](/README_EN.md)

<p align="center">
    <br>
    <img src="./llcom/llcom.ico" width="150"/>
    <br>
</p>

<p align="center">
    <img alt="GitHub" src="https://img.shields.io/github/license/Eureka12138github/llcom-y">
    <img alt="GitHub release (latest by date)" src="https://img.shields.io/github/v/release/Eureka12138github/llcom-y">
    <img alt="GitHub top language" src="https://img.shields.io/github/languages/top/Eureka12138github/llcom-y">
    <img alt="code-size" src="https://img.shields.io/github/languages/code-size/Eureka12138github/llcom-y.svg">
</p>

可运行 lua 脚本的高自由度串口调试工具（个人维护版）。

## 下载

- **exe 便携版**：从 [GitHub Releases](https://github.com/Eureka12138github/llcom-y/releases) 下载（首次发布后可用）
- **CI 快照**：[GitHub Actions](https://github.com/Eureka12138github/llcom-y/actions)
- **上游官方版本（原作者）**：[chenxuuu/llcom](https://github.com/chenxuuu/llcom)

> 本仓库不发布到微软商店；商店里的 LLCOM 是原作者版本，与本项目无关。

## 相对上游的改动

见 [CHANGES.md](CHANGES.md)。主要方向：界面与交互打磨、日志性能与内存上限、发送记录、串口设置整合、串口监听的错误诊断等。


## 功能列表

- 其他串口调试功能具有的功能
- 收发日志清晰明了，可同时显示HEX值与实际字符串
- 自动保存串口与Lua脚本日志，并附带时间
- 串口断开后，如果再次连接，会自动重连
- 发送的数据可被用户自定义的Lua脚本提前处理
- 右侧快捷发送栏，快捷发送条目数量不限制
- 右侧快捷发送栏，支持10页数据，互相独立
- 可独立运行Lua脚本，并拥有定时器与协程任务特性（移植自[合宙Luat Task架构](http://wiki.openluat.com/doc/luatFramework/)）
- 可选文字编码格式
- 终端功能，直接敲键盘发送数据（包含ctrl+字母键）
- 可单独隐藏发送数据
- 集成TCP、UDP、SSL测试服务端/客户端功能，并且支持IPV6
- 集成各种编码互转功能
- 集成乱码恢复功能
- 集成mqtt测试功能
- 集成串口监听功能，可监听其他软件的串口通信数据
- （本版本）日志列表虚拟化 + 缓冲上限，长时间挂机不卡顿、不涨内存
- （本版本）发送记录可设置上限、支持键盘输入与一键清空
- （本版本）快捷发送支持每行独立定时发送；发送区可直接开关串口
- （本版本）串口参数整合到右侧「串口设置」标签页，减少来回切换

![screen](/image/screen.png)
![screen3](/image/screen3.png)
![screen2](/image/screen2.jpg)

## 特色功能示范

### 使用Lua脚本提前处理待发送的数据

1. 结尾加上换行回车

```lua
return uartData.."\r\n"
```

2. 发送16进制数据

```lua
return uartData:fromHex()
```

此脚本可将形如`30313233`发送数据，处理为`0123`的结果

3. 更多玩法等你发现

```lua
json = require("JSON")
t = uartData:split(",")
return json:encode({
    key1 = t[1],
    key2 = t[2],
    key3 = t[3],
})
```

此脚本可将形如`a,b,c`发送数据，处理为`{"key1":"a","key2":"b","key3":"c"}`的结果

**此处理脚本，同样对右侧快捷发送区域有效。**

### 独立的Lua脚本自动处理串口收发

右侧的Lua脚本调试区域，可直接运行你写的串口测试脚本，如软件自带的：

```lua
--注册串口接收函数
uartReceive = function (data)
    log.info("uartReceive",data)
    sys.publish("UART",data)--发布消息
end

--新建任务，等待接收到消息再继续运行
sys.taskInit(function()
    while true do
        local _,udata = sys.waitUntil("UART")--等待消息
        log.info("task waitUntil",udata)
        local sendResult = apiSendUartData("ok!")--发送串口消息
        log.info("uart send",sendResult)
    end
end)

--新建任务，每休眠1000ms继续一次
sys.taskInit(function()
    while true do
        sys.wait(1000)--等待1000ms
        log.info("task wait",os.time())
    end
end)

--1000ms循环定时器
sys.timerLoopStart(log.info,1000,"timer test")
```

甚至你可以利用xlua框架的特性，调用C#接口完成任何你想做的事情

```lua
request = CS.System.Net.WebRequest.Create("http://example.com")
request.ContentType = "text/html;charset=UTF-8";
request.Timeout = 5000;--超时时间
request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36 Vivaldi/2.2.1388.37";

response = request:GetResponse():GetResponseStream()

myStreamReader = CS.System.IO.StreamReader(response, CS.System.Text.Encoding.UTF8);

print(myStreamReader:ReadToEnd())--打印获取的body内容

myStreamReader:Close()
response:Close()
```

使用此功能，你可以完成大部分的自动化串口调试操作。

## 接口文档

接口文档可以在[这个页面](https://github.com/Eureka12138github/llcom-y/blob/master/LuaApi.md)查看（内容继承自上游 LLCOM）。

## 已知问题与待添加的功能（请大家反馈，谢谢！）

- [x] ~~bug：某些条件下（比如Air720重启），COM口消失后不会被释放，导致无法再次开启该COM口，只能重启软件（[.net 框架的bug，微软的人在看了](https://github.com/dotnet/corefx/issues/39464)）~~（已解决 #2f26e68）

## 开源

如果各位大佬不觉得麻烦的话，欢迎对本项目进行pr或直接重构。

项目已完成一轮分层重构：从早期的"上帝类 + 巨型 code-behind"整理为清晰的 MVVM 分层结构——`ViewModels/`（界面逻辑）、`Services/`（可复用服务）、`Model/`（数据模型）、`Pages/`（工具页）、`LuaEnv/`（Lua 引擎），并配套完整文档与单元测试，**整体结构清晰、分层明确，适合作为阅读与学习的范例**。架构说明与关键设计决策见 [docs/02-architecture.md](docs/02-architecture.md)，文档索引见 [docs/README.md](docs/README.md)。

本项目沿用上游 LLCOM 的 **Apache-2.0** 协议（见 [LICENSE](LICENSE)）。
上游项目：[chenxuuu/llcom](https://github.com/chenxuuu/llcom)。如有借用，请保留指向本项目与上游项目的链接。

## Contributors ✨

> 以下为**上游项目 LLCOM** 的贡献者（本项目继承了全部历史提交）。

Thanks goes to these wonderful people ([emoji key](https://allcontributors.org/docs/en/emoji-key)):

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tr>
    <td align="center"><a href="https://github.com/whc2001"><img src="https://avatars2.githubusercontent.com/u/16266909?v=4?s=100" width="100px;" alt=""/><br /><sub><b>whc2001</b></sub></a><br /><a href="https://github.com/chenxuuu/llcom/commits?author=whc2001" title="Code">💻</a> <a href="https://github.com/chenxuuu/llcom/issues?q=author%3Awhc2001" title="Bug reports">🐛</a></td>
    <td align="center"><a href="https://www.chenxublog.com/"><img src="https://avatars3.githubusercontent.com/u/10357394?v=4?s=100" width="100px;" alt=""/><br /><sub><b>chenxuuu</b></sub></a><br /><a href="#projectManagement-chenxuuu" title="Project Management">📆</a></td>
    <td align="center"><a href="https://github.com/neomissing"><img src="https://avatars0.githubusercontent.com/u/22003930?v=4?s=100" width="100px;" alt=""/><br /><sub><b>neomissing</b></sub></a><br /><a href="#ideas-neomissing" title="Ideas, Planning, & Feedback">🤔</a></td>
    <td align="center"><a href="https://github.com/RYLF"><img src="https://avatars3.githubusercontent.com/u/28991981?v=4?s=100" width="100px;" alt=""/><br /><sub><b>RuoYun</b></sub></a><br /><a href="https://github.com/chenxuuu/llcom/issues?q=author%3ARYLF" title="Bug reports">🐛</a></td>
    <td align="center"><a href="http://www.diycms.com"><img src="https://avatars.githubusercontent.com/u/13432299?v=4?s=100" width="100px;" alt=""/><br /><sub><b>王龙</b></sub></a><br /><a href="#ideas-wanglong126" title="Ideas, Planning, & Feedback">🤔</a> <a href="https://github.com/chenxuuu/llcom/issues?q=author%3Awanglong126" title="Bug reports">🐛</a> <a href="https://github.com/chenxuuu/llcom/commits?author=wanglong126" title="Code">💻</a></td>
    <td align="center"><a href="https://github.com/linhongz"><img src="https://avatars.githubusercontent.com/u/49241612?v=4?s=100" width="100px;" alt=""/><br /><sub><b>linhongz</b></sub></a><br /><a href="#ideas-linhongz" title="Ideas, Planning, & Feedback">🤔</a> <a href="https://github.com/chenxuuu/llcom/issues?q=author%3Alinhongz" title="Bug reports">🐛</a></td>
  </tr>
</table>

<!-- markdownlint-restore -->
<!-- prettier-ignore-end -->

<!-- ALL-CONTRIBUTORS-LIST:END -->

This project follows the [all-contributors](htts://github.com/all-contributors/all-contributors) specification. Contributions of any kind welcome!


## 特别感谢

[![icon-resharper](/image/icon-resharper.svg)](https://www.jetbrains.com/?from=LLCOM)

本项目基于 [chenxuuu/LLCOM](https://github.com/chenxuuu/llcom)，在此感谢原作者与所有上游贡献者。
