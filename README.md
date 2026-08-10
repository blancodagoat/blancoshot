# SnapKit

A small Windows screenshot utility that does two things: region capture and active-display
capture. No uploads, no editor, no telemetry, no update checker.

| | |
|---|---|
| **Print Screen** | interactive region capture |
| **F8** | full capture of the display holding the foreground window |

Both shortcuts are reconfigurable. Every capture is saved as a PNG and copied to the
clipboard; if the save fails the clipboard copy still happens and the error is left in the
tray tooltip.

Output goes to `%USERPROFILE%\Pictures\Screenshots` by default, nested by year and month:

```
<root>\2026\08\chrome_2026-08-10_143052.png
```

The app has no main window. The tray icon is the entry point — double-click for settings,
right-click for the menu.

## Building

Requires the .NET 10 SDK.

```
dotnet publish src/SnapKit/SnapKit.csproj -c Release
```

The result is a single self-contained `SnapKit.exe` under
`src/SnapKit/bin/Release/net10.0-windows/win-x64/publish/`. Building on a non-Windows host
works too, with `-p:EnableWindowsTargeting=true`.

The icons are generated rather than checked in by hand; `python3 tools/make-icons.py`
rewrites `assets/` from scratch.

### A note on trimming

The build sets `PublishSingleFile` and `SelfContained`, but **not** `PublishTrimmed`. The
SDK rejects that combination outright for Windows Forms:

```
error NETSDK1175: Windows Forms is not supported or recommended with trimming enabled.
```

It is a hard error rather than a warning, so a trimmed WinForms build is not something the
toolchain will produce. `EnableCompressionInSingleFile` and `PublishReadyToRun` are used
instead, which lands the executable at roughly 50 MB.

## Print Screen may not reach SnapKit

Windows has its own setting — *Use the Print screen key to open screen capture* — that
hands the key to Snipping Tool. When it is on, `RegisterHotKey` on Print Screen still
*succeeds*, so nothing looks broken; the key simply never arrives and region capture does
nothing.

SnapKit reads `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` on first run
and, if the conflict is live, shows a one-time notice with a button that opens the relevant
Settings page. It never changes the setting for you. Either turn it off there, or bind
region capture to something else.

## Where things are kept

- Configuration: `%APPDATA%\SnapKit\config.json` — hotkeys and save location only.
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The registry is
  the only record of it, so the checkbox stays honest if the value is removed elsewhere.

## Tests

```
dotnet run --project tests/SnapKit.Tests
```

41 assertions over the logic that does not need a live desktop: hotkey parsing and
formatting (a round-trip failure here would quietly reset the user's shortcuts on every
launch), source-app name sanitising, and output path construction including same-second
collision suffixes. The project compiles the production source files in directly rather
than copying them.

## Verification status

Those 41 assertions pass. The build and the single-file publish are clean, and the
published executable has been checked to be a 64-bit GUI PE with the PerMonitorV2 manifest
and the icon resources embedded.

Everything else is Windows-only behaviour that has **not** been exercised on hardware —
this was built and verified on Linux. Still outstanding, in rough order of risk:

- **The mixed-DPI pass the spec asks for**: 150% primary + 100% secondary, checking that
  captures are pixel-exact and that region selection lands where the mouse was dragged on
  both monitors. The overlay deliberately swallows `WM_DPICHANGED` and re-asserts its
  bounds, which is the part most worth confirming.
- Capture, clipboard hand-off and PNG output.
- Global hotkey registration, rebinding and the in-use warning.
- Tray icon, its light/dark swap, and the settings window.
