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
- Runtime view shows both matched processes and per-display HDR state (`Supported`, `HDR On`, `Desired`, `Action`).
- Matched process table also shows `Fullscreen` (fullscreen/borderless-windowed heuristic).

## Rule Configuration

Fields per rule row:

- `pattern`
- `exactMatch` (default off)
- `caseSensitive` (default off)
- `regexMode` (default off; when enabled, `exactMatch` and `caseSensitive` are ignored)
- `enabled` (default on)

Top-level config field:

- `pollIntervalSeconds` (default 2)

Matching priority is documented in `docs/process-rule-matching.md`.
