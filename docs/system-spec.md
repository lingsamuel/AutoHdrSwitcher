# AutoHdrSwitcher System Spec

Version: `1.0.0`  
Status: `Implemented`  
Last updated: `2026-02-21`

## 1. Scope

AutoHdrSwitcher is a Windows desktop app that toggles HDR per display based on process matching and fullscreen activity.
The app provides a local GUI for rule management, runtime visibility, and operational controls.

## 2. Goals

1. Enable HDR only where needed (per display), not globally for all monitors.
2. Support launcher-based game startup by reacting to runtime process/window state.
3. Provide a practical UI with editable rules and observable runtime status.
4. Keep operations resilient when process/window/HDR APIs return incomplete data.
5. Provide automated release packaging for tagged versions.

## 3. Functional Requirements

### 3.1 Rule Management

Each watch rule row has:

1. `pattern`
2. `exactMatch`
3. `caseSensitive`
4. `regexMode`
5. `enabled`
6. `targetDisplay` (optional; omitted = `Default`)

Matching behavior:

1. `regexMode=true`: regex matching; `exactMatch` and `caseSensitive` are ignored by design.
2. `regexMode=false && exactMatch=true`: full-string comparison.
3. `regexMode=false && exactMatch=false`: includes matching; if pattern contains `*`, wildcard mode (`*` only).

Candidate strings checked for each process:

1. process name (for example `eldenring`)
2. process name with extension (for example `eldenring.exe`)
3. full executable path (if accessible)
4. executable file name (if accessible)

### 3.2 Display Targeting

For each matched process:

1. Resolve effective target by priority: process-level override (`processTargetDisplayOverrides`) > rule-level `targetDisplay` > `Default`.
2. `Default` means: use window display; if not resolvable, fallback target is the primary display.
3. `Switch All Displays` means this match requests HDR desired state for all displays (without requiring global `switchAllDisplaysTogether`).
4. If effective target is a concrete display and currently available, force that display.
5. If effective target is currently unavailable, keep stored value but treat runtime target as `Default`.

### 3.3 Fullscreen Monitoring

The app detects fullscreen-like windows (exclusive fullscreen or borderless fullscreen-like).

When `monitorAllFullscreenProcesses=true`:

1. Fullscreen processes can request HDR even without explicit watch-rule matches.
2. Ignored fullscreen processes do not affect HDR decisions.

Fullscreen runtime table columns:

1. `PID`
2. `Process`
3. `Executable`
4. `Display`
5. `Matched Rule`
6. `Ignore` (editable)

### 3.4 Ignore System

Ignore map keys support three forms:

1. `path:<full-executable-path>` (highest priority)
2. `pathprefix:<path-prefix>` (middle priority)
3. `name:<process-name>` (fallback)

Default ignore entries are auto-added to config when missing:

1. `pathprefix:C:\Windows\`
2. `name:TextInputHost`
3. `name:dwm`

### 3.5 HDR Action Model

Per display:

1. `desired=true` when at least one matched/eligible fullscreen process maps to that display.
2. `desired=false` when no matched/eligible process maps to that display.
3. If HDR is unsupported, action is reported as unsupported.
4. If desired differs from current HDR state, app attempts change and reports action result.

Display runtime table columns:

1. `Display`
2. `Monitor`
3. `Supported`
4. `Auto`
5. `HDR On`
6. `Desired`
7. `Action`

### 3.6 Monitoring Model

The app uses event-driven monitoring plus optional polling:

1. Event source: WMI start/stop events (`Win32_ProcessStartTrace`, `Win32_ProcessStopTrace`).
2. On relevant event, app performs immediate refresh and short burst refresh.
3. Polling is user-configurable and default `disabled`.
4. If event stream fails to start, app tries a compatible WMI instance-event query path before declaring unavailable.
5. Polling is never forced when `Enable polling` is unchecked.
6. If running on instance fallback, app periodically retries trace mode in background.

Monitor status text distinguishes:

1. events only
2. events + polling
3. polling fallback (event stream unavailable)
4. event stream unavailable (polling disabled)
5. status bar event source label: trace / instance (fallback) / unavailable

### 3.7 UI Requirements

Main UI sections:

1. Process Watch Rules
2. Runtime Status
3. Detected Fullscreen Processes
4. Display HDR Status

Behavior requirements:

1. Runtime tables should not keep distracting row selection highlights.
2. Tooltips are disabled.
3. `Poll (sec)` control is disabled when `Enable polling` is unchecked.
4. Poll controls are on second row, right aligned.
5. `Minimize to tray` is configurable and default `true`.

### 3.8 Persistence

Config file path:

1. Default: `config.json` in app base directory.
2. Override via CLI: `--config <path>`.

Persisted in `config.json`:

1. `pollIntervalSeconds`
2. `pollingEnabled`
3. `minimizeToTray`
4. `enableLogging`
5. `autoRequestAdminForTrace`
6. `monitorAllFullscreenProcesses`
7. `mainSplitterDistance`
8. `runtimeTopSplitterDistance`
9. `runtimeBottomSplitterDistance`
10. `fullscreenIgnoreMap`
11. `processRules`

Window placement persistence:

1. Stored via WinForms user-scoped settings, not `config.json`.
2. Persisted values: bounds + maximized state.
3. On restore, bounds are normalized to visible screen area.

### 3.9 Error Handling and Resilience

1. If config parsing fails, app backs up corrupt config and recreates a default config.
2. Startup exceptions are shown in a message box (no silent exit).
3. Access-denied process metadata reads are tolerated and skipped.
4. Splitter distances are always clamped to safe ranges before applying.

## 4. Release and Distribution

### 4.1 Local Build

1. `./build.sh build`
2. `./build.sh` (publish)

### 4.2 GitHub Release Automation

Workflow: `.github/workflows/release-on-tag.yml`

Trigger:

1. Push any git tag.

Pipeline:

1. Checkout on `windows-latest`.
2. `dotnet publish` to `artifacts/publish`.
3. Zip artifact as `AutoHdrSwitcher-<tag>-win.zip`.
4. Create/update GitHub Release for that tag.
5. Upload zip as release asset.
6. Enable `generate_release_notes`.

## 5. Out of Scope

1. Launching target games/apps directly from AutoHdrSwitcher.
2. Capturing proprietary launcher tokens/arguments.
3. Guaranteeing perfect fullscreen detection for every engine/render path.

## 6. Reference Docs

1. `docs/process-rule-matching.md`
2. `README.md`
3. `.github/workflows/release-on-tag.yml`
