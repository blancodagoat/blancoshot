# Memento

Tray screenshot tool (region + display capture → PNG + clipboard). NOTE: the folder is
named `blancoshot` but the project/product is **Memento** (`src/Memento/`).

C# / .NET 10 WinForms, win-x64, zero NuGet dependencies — stdlib + Win32 P/Invoke only.
All P/Invoke lives in `Native.cs`. Don't add packages.

## Build

```
dotnet publish src/Memento/Memento.csproj -c Release -p:SelfContained=false -o publish/framework-dependent
dotnet publish src/Memento/Memento.csproj -c Release -o publish/self-contained
```

If Memento is running in the tray, the publish output exe may be locked — kill it first.

## Tests

No test framework. `tests/Memento.Tests` is a plain exe with asserts; exit 0 = pass.
Tests must stay headless. Production files are pulled in via explicit `<Compile Include>`
in the test csproj — new source files that tests touch need adding there.

```
dotnet run --project tests/Memento.Tests           # + `-- smoke` for live capture geometry
```

## Conventions

- Sibling repos (`../dejavu`, `../recite`) share files by duplication, not a shared lib:
  `AppConfig.cs`, `Native.cs`, `Theme.cs`, `TrayContext.cs`, `HotkeyWindow.cs`,
  `SingleInstance.cs`, `UpdateCheck.cs`, etc. If you fix a bug in one of these, check
  whether the siblings have the same file and port the fix.
- Icons are generated: `python3 tools/make-icons.py` rewrites `assets/` — never hand-edit.
- Runtime config: `%APPDATA%\Memento\config.json`. The Triumvirate launcher
  (`../triumvirate`) reads/writes this file — schema changes must stay compatible.
- CI (`.github/workflows/build.yml`) recreates a rolling `latest` release on every push;
  scoop manifest in `packaging/scoop/` autoupdates from tags.
