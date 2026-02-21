[English](README.md) | [简体中文](README.zh-CN.md)

## DISCLAIMER

我对Windows相关的API以及GUI等内容一无所知，本项目是使用Codex开发而来，it just works
包括这个README，以及这个DISCLAIMER

# AutoHdrSwitcher

这是一个 Windows 桌面应用，用于配置进程匹配规则并实时展示监控状态。  
当命中的进程在运行时，应用会尝试只在该进程窗口所在的显示器上开启 HDR；当某个显示器上不再有命中进程时，会关闭该显示器的 HDR。  
如果已经检测到命中进程，但其游戏窗口暂时无法解析到具体显示器，会先回退到主显示器开启 HDR，直到能解析出真实窗口所在显示器。  
项目启用了进程启动/退出事件流（WMI），因此匹配与 HDR 切换相比纯轮询更快。

## 构建

```bash
dotnet.exe build AutoHdrSwitcher.sln
```

或使用 bash 脚本（WSL/Linux）：

```bash
./build.sh            # 默认：publish Release
./build.sh build
./build.sh clean
```

## 运行

```bash
dotnet.exe run --project src/AutoHdrSwitcher
dotnet.exe run --project src/AutoHdrSwitcher -- --config C:\path\to\config.json
```

默认行为：

- 应用启动 GUI（规则表 + 运行时状态表）。
- 如果配置文件不存在，应用会自动创建（默认是程序目录下的 `config.json`）。
- 如果没有配置任何规则，应用不会退出，而是继续运行并显示状态。
- 最小化会将应用收纳到系统托盘（从任务栏移除）。双击托盘图标可恢复窗口。
- 运行时视图会显示命中进程、所有全屏进程，以及每个显示器的 HDR 状态（`Supported`、`HDR On`、`Desired`、`Action`）。
- 命中进程表还会显示 `Fullscreen`（全屏/无边框窗口启发式判断）。
- 全屏表支持对每个进程勾选 `Ignore`。被忽略的条目不会影响自动全屏 HDR 模式。
- Ignore 键优先使用可执行文件路径（`path:<fullpath>`），否则使用进程名（`name:<processName>`）。
- 内置默认忽略项包括 `pathprefix:C:\Windows\` 和 `name:TextInputHost`（若缺失会在配置中自动生成）。
- 运行时分割布局（表格高度）会保存到配置并在下次启动时恢复。

## 规则配置

每条规则行包含字段：

- `pattern`
- `exactMatch`（默认关闭）
- `caseSensitive`（默认关闭）
- `regexMode`（默认关闭；开启后会忽略 `exactMatch` 与 `caseSensitive`）
- `enabled`（默认开启）

顶层配置字段：

- `pollIntervalSeconds`（默认 2）
- `monitorAllFullscreenProcesses`（默认 `false`）
- `runtimeTopSplitterDistance` / `runtimeBottomSplitterDistance`（`null` 表示使用内置默认值，默认大约显示 2 行）
- `fullscreenIgnoreMap`（ignore key -> bool 的字典，支持 `path:...`、`pathprefix:...`、`name:...`）

匹配优先级文档见 `docs/process-rule-matching.md`。
