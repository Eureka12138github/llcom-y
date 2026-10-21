# LLCOM-Y

> ⚠️ **Unofficial personal build**
>
> Based on [**chenxuuu/LLCOM**](https://github.com/chenxuuu/llcom) (Apache-2.0).
> Original author: chenxuuu and upstream contributors.
> Maintained by [Eureka12138github](https://github.com/Eureka12138github/llcom-y) for personal learning and use.
> **Not the official version**; not affiliated with or endorsed by the original author.
> See [CHANGES.md](CHANGES.md) for changes relative to upstream.

[中文说明](/README.md)

![LLCOM-Y main window](image/shot-main.png)

A serial port debugger with Lua scripting (personal refactor).

## Scope & risks (read first)

- Personal learning / personal-use project: the main goal was refactoring and everyday usability.
- **Not** an official upstream branch, and behavior may differ from the original.
- **Verified**: x64/x86 builds, 44 unit tests, serial send/receive and log display, send history, quick-send/timers, serial settings.
- **Not fully regression-tested**: TCP/UDP/MQTT, WinUSB, serial monitor, plot, online scripts, terminal mode. Most of these are inherited from upstream and were only refactored/deduplicated here, so regressions are possible.
- **Not recommended for production or critical work.** Back up your settings; verify important operations against the original version.
- Maintenance is best-effort; issues are welcome but response time is not guaranteed.

## Features

- Serial send/receive with a configurable log (Abc/Hex per direction, font size, pause / lock / clear / save)
- Virtualized log list; log retention limit (50k entries / 32 MB payload by default; 2M chars in stream mode)
- Send history (limit 5~200, keyboard input, one-click clear)
- Quick-send panel (10 pages, per-item timer)
- Dedicated serial-settings tab (port / baud / data bits / parity / stop bits / encoding / auto-reconnect / RTS / DTR / open-close)
- Lua scripting via XLua (send/receive processing and standalone scripts; see [LuaApi.md](LuaApi.md))
- Inherited tools: TCP/UDP/MQTT, WinUSB, serial monitor, plot, encoding converter (see the risk note above)

## Screenshots

| Main window | Quick send |
|---|---|
| ![Main window](image/shot-main.png) | ![Quick send](image/shot-quick.png) |

![About](image/shot-about.png)

## Download

- **Portable exe**: [GitHub Releases](https://github.com/Eureka12138github/llcom-y/releases) (available after the first release)
- **CI snapshot**: [GitHub Actions](https://github.com/Eureka12138github/llcom-y/actions)
- **Upstream official version**: [chenxuuu/llcom](https://github.com/chenxuuu/llcom)

> This fork is not published on the Microsoft Store; the store app is the original author's build.

## Build

Requires .NET Framework 4.6.2 (reference assemblies come from NuGet) and the .NET SDK:

```powershell
dotnet build llcom\llcom.csproj -c Release -p:Platform=x64
dotnet build llcom\llcom.csproj -c Release -p:Platform=x86
dotnet test tests\llcom.Tests\llcom.Tests.csproj -p:Platform=x64
```

Output: `artifacts\Release\{x64|x86}\llcom-y.exe` (single file; managed dependencies are embedded by Costura).
The exe must be named `llcom-y.exe`; settings/scripts/logs live next to it (or in `%LOCALAPPDATA%\llcom\` for MSIX installs).

## Relationship to upstream

- Full upstream git history is preserved; licensed under **Apache-2.0** (see [LICENSE](LICENSE)).
- Original code copyright chenxuuu and contributors; modifications copyright Eureka12138github.
- Detailed changes: [CHANGES.md](CHANGES.md). Upstream release notes: the original project's Releases.
- Auto-update now checks **this project's GitHub Releases** (can be disabled); hot-fix (`hotfix.lua`) and crash reports remain disabled, so nothing is sent to the original author's servers.
- Upgrading from v2.0.0.1 (`llcom.exe`) to 2.0.0.2 requires a one-time manual download because the executable is renamed to `llcom-y.exe`; auto-update works normally afterwards.

## Development & docs

- `docs/`: architecture, maintenance, test checklist and plans; start at [docs/README.md](docs/README.md).
- `tests/llcom.Tests`: xUnit tests.
- Layout: `ViewModels/`, `Services/`, `Model/`, `Pages/`, `LuaEnv/`.

## License & credits

- **Apache-2.0**; original copyright `Copyright 2021 chenxuuu`, modifications `Copyright 2026 Eureka12138github`.
- Thanks to [chenxuuu](https://github.com/chenxuuu) and all [upstream contributors](https://github.com/chenxuuu/llcom/graphs/contributors).
- Third-party: xLua, AdonisUI, AvalonEdit, LibUsbDotNet, Costura.Fody.
