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

Output goes to `%USERPROFILE%\Pictures\Blancoshot` by default, nested by year and month:

```
<root>\2026\08 Aug\chrome_2026-08-10_143052.png
```

A capture confirms itself with a small card in the bottom-right corner — thumbnail,
filename, size — that fades out after a few seconds. It never steals focus; hovering
pauses it, clicking reveals the file in Explorer. While a fullscreen game, presentation,
or Focus Assist is active no card appears at all (showing one could knock a game out of
exclusive fullscreen); a failed save is queued to the notification center instead so it
cannot vanish silently. Taking a second screenshot hides the card first, so it never
appears inside its successor.

The app has no main window. The tray icon is the entry point — double-click for settings,
right-click for capture actions, open/copy last capture, and the screenshots folder.

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

## Print Screen and Snipping Tool

Windows has its own setting — *Use the Print screen key to open screen capture* — that
hands the key to Snipping Tool, and it defaults to on. When it is on, `RegisterHotKey` on
Print Screen still *succeeds*, so nothing looks broken; the key simply never arrives.

While either hotkey is bound to Print Screen, SnapKit turns that setting off itself: it
writes the same `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` value the
Settings toggle writes and broadcasts the change so it applies without a sign-out. This
runs at launch and after a rebind, and says so with a card rather than acting silently.
If the registry write ever fails, a one-time notice points at the Settings page instead.
Rebind away from Print Screen and SnapKit stops touching the setting.

## Where things are kept

- Configuration: `%APPDATA%\SnapKit\config.json` — hotkeys and save location only.
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The registry is
  the only record of it, so the checkbox stays honest if the value is removed elsewhere.

## Tests

```
dotnet run --project tests/SnapKit.Tests
```

47 assertions over the logic that does not need a live desktop: hotkey parsing and
formatting (a round-trip failure here would quietly reset the user's shortcuts on every
launch), source-app name sanitising, output path construction including same-second
collision suffixes, and the legacy month-folder migration. The project compiles the
production source files in directly rather than copying them.

## Verification status

Capture, clipboard hand-off, PNG output, the tray icon and menu, the settings window,
hotkey rebinding and the capture card have all been exercised on Windows 11 hardware.
Still outstanding: the mixed-DPI pass — 150% primary + 100% secondary, checking that
captures are pixel-exact and that region selection lands where the mouse was dragged on
both monitors. The overlay deliberately swallows `WM_DPICHANGED` and re-asserts its
bounds, which is the part most worth confirming.
