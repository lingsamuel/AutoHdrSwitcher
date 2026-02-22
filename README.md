[English](README.md) | [简体中文](README.zh-CN.md)

## DISCLAIMER

I know nothing about Windows-related APIs, GUI, or similar topics. This project was built with Codex, and it just works.
Including this README, and this DISCLAIMER.

# AutoHdrSwitcher

Windows desktop app for process-rule configuration and live monitor status.
When a matched process is running, the app tries to enable HDR only on the display where that process window is shown; when no matched process remains on a display, HDR is turned off for that display.
If the matched process is found but its game window display is not yet resolvable, HDR fallback target is the primary display until the real window display can be resolved.
Process start/stop event stream is enabled (WMI) so matching and HDR switching react faster than pure polling.

## Build

```bash
dotnet.exe build AutoHdrSwitcher.sln
```

Or use the bash script (WSL/Linux):

```bash
./build.sh            # default: publish Release
./build.sh build
./build.sh clean
```

## Run

```bash
dotnet.exe run --project src/AutoHdrSwitcher
dotnet.exe run --project src/AutoHdrSwitcher -- --config C:\path\to\config.json
```

Default behavior:

- App launches GUI (rules table + runtime status table).
- If config file is missing, app auto-creates it (`config.json` in app directory by default).
- If no rules are configured, app keeps running and shows status instead of exiting.
- Polling is optional and disabled by default. App prefers process event stream for fast reaction.
- Runtime logs are written to `logs/autohdrswitcher.log` under app base directory, and reset on every app launch.
- `Minimize to tray` is enabled by default. When enabled, minimizing sends app to tray (removed from taskbar). Tray icon double-click restores window.
- Runtime view shows matched processes, all fullscreen processes, and per-display HDR state (`Supported`, `HDR On`, `Desired`, `Action`).
- `Auto` column controls per-display auto switching. `Auto=false` means this display is not touched by auto logic.
- `HDR On` in display table is directly editable for manual HDR on/off per display.
- Display HDR status keeps refreshing live even when monitor is stopped.
- `Switch all displays together` can be enabled to ignore per-display mapping and toggle HDR on/off for all displays at once.
- Each process rule can optionally pin a target display. Available target modes:
  - `Default`: window display, or primary display when window is not found.
  - `Switch All Displays`: force HDR desired state to all displays for this match only.
  - Specific display: force HDR desired state to that display.
  If a pinned display is currently unavailable, the value is kept and runtime behavior falls back to `Default`.
- Matched process table also shows `Fullscreen` (fullscreen/borderless-windowed heuristic).
- Fullscreen table supports `Ignore` checkbox per process. Ignored entries do not affect auto-fullscreen HDR mode.
- Ignore key uses executable path when available (`path:<fullpath>`), otherwise process name (`name:<processName>`).
- Built-in default ignores include `pathprefix:C:\Windows\`, `name:TextInputHost`, and `name:dwm` (auto-generated in config when missing).
- Runtime split layout is saved in config; window size/position/maximized state is saved with WinForms user settings.

## Rule Configuration

Fields per rule row:

- `pattern`
- `exactMatch` (default off)
- `caseSensitive` (default off)
- `regexMode` (default off; when enabled, `exactMatch` and `caseSensitive` are ignored)
- `enabled` (default on)
- `targetDisplay` (optional; omitted means `Default`; supports `Switch All Displays` and concrete display names)

Top-level config fields:

- `pollIntervalSeconds` (default 2)
- `pollingEnabled` (default `false`)
- `minimizeToTray` (default `true`)
- `enableLogging` (default `true`; when disabled, file logging is off)
- `autoRequestAdminForTrace` (default `false`; when true and not elevated, app prompts UAC and relaunches as admin to improve trace event availability)
- `monitorAllFullscreenProcesses` (default `false`)
- `switchAllDisplaysTogether` (default `false`)
- `mainSplitterDistance` (rules/runtime split)
- `runtimeTopSplitterDistance` / `runtimeBottomSplitterDistance` (`null` = use built-in defaults, about 2 rows visible by default)
- `fullscreenIgnoreMap` (dictionary of ignore key -> bool, supports `path:...`, `pathprefix:...`, `name:...`)
- `displayAutoModes` (dictionary of display name -> auto flag; omitted/`true` = auto, `false` = manual)
- `processTargetDisplayOverrides` (dictionary of process key -> target display, higher priority than rule `targetDisplay`; process key supports `path:...` and `name:...`)

Matching priority is documented in `docs/process-rule-matching.md`.

System-level feature and design spec is documented in `docs/system-spec.md`.
