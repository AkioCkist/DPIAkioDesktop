# DPIAkio

WinUI 3 desktop GUI for [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) — bypass ISP DNS poisoning / DPI blocking on Windows.

- On/off button, GoodbyeDPI modes `-1` … `-9`, extra arguments
- DNS redirect with 30+ public resolvers grouped by purpose (ad-blocking, security, family, Asia, Europe…), latency test and fake-answer detection
- Tray icon (closing the window keeps it running), run at Windows startup without a UAC prompt
- Survives network switches (Wi-Fi ↔ LAN): flushes stale DNS cache and restarts GoodbyeDPI if it died

## Project layout

| Path | What |
|---|---|
| `DPIAkio/` | WinUI 3 app (.NET 9, unpackaged, self-contained) |
| `native/` | GoodbyeDPI C source + MSVC compatibility shims (see `native/README.md`) |
| `build-goodbyedpi.ps1` | Compiles `goodbyedpi.exe` with MSVC into `bin/` and copies WinDivert next to it |
| `build-app.ps1` | Dev build: `build-goodbyedpi.ps1` + app build (multi-file output) |
| `build-release.ps1` | Release build: one portable `release/DPIAkio.exe` |

## Requirements (build machine only)

- Windows 10/11 x64
- Visual Studio 2022 with workloads **Desktop development with C++** and **.NET desktop development**
- .NET 9 SDK
- WinDivert 2.2.2 (see step 1)

`dotnet build` / `dotnet publish` do **not** work for this project (missing WinUI PRI tasks); the scripts use Visual Studio's MSBuild.

## Build a release

1. Download [`WinDivert-2.2.2-A.zip`](https://github.com/basil00/WinDivert/releases/tag/v2.2.2) and extract it so that
   `deps/WinDivert-2.2.2-A/x64/WinDivert.dll` exists.
   Windows Defender flags WinDivert as a "HackTool" (it is a packet-capture driver) — add a Defender exclusion for this folder first,
   otherwise the zip is quarantined.

2. Build everything into a single exe:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build-release.ps1
   ```

   Reuse an already compiled `bin\goodbyedpi.exe` (faster):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build-release.ps1 -SkipNative
   ```

3. Output: **`release\DPIAkio.exe`** (~85 MB). Ship that one file.

What the script runs, if you want to do it by hand:

```powershell
.\build-goodbyedpi.ps1
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    DPIAkio\DPIAkio.csproj /restore /t:Publish `
    /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
    /p:SelfContained=true /p:PublishSingleFile=true `
    /p:OutDir=build\app\ /p:PublishDir=release\
```

## Dev build

```powershell
powershell -ExecutionPolicy Bypass -File .\build-app.ps1
```

Output: `DPIAkio\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\DPIAkio.exe` (folder with ~500 files).

## Running on another PC

`release\DPIAkio.exe` needs **nothing installed** — no .NET, no Windows App SDK runtime, no VC++ Redistributable:

- .NET 9 and Windows App SDK 1.7 are bundled (self-contained).
- `goodbyedpi.exe` links the C runtime statically; it and `WinDivert.dll` import only Windows system DLLs.
- The remaining dependency is the Universal C Runtime, which ships with Windows 10/11.

Supported: **Windows 10 version 1809 or newer / Windows 11, x64**. Windows 7/8.1 and ARM64 are not supported.

Things the user on the other PC will see:

- **UAC prompt** on launch — WinDivert needs administrator rights.
- **SmartScreen** "Windows protected your PC" — the exe is not code-signed. Click *More info → Run anyway*.
- **Windows Defender may quarantine WinDivert** (`HackTool:Win64/WinDivert`). If the app shows "Thiếu file GoodbyeDPI",
  add a Defender exclusion for `%TEMP%\.net\DPIAkio` and start the app again.
- First launch is a few seconds slower: the exe unpacks itself to `%TEMP%\.net\DPIAkio\<hash>\` (≈170 MB). Later launches reuse it.

Settings are stored in `%LOCALAPPDATA%\DPIAkio\settings.json`. "Run with Windows" creates the Task Scheduler task `DPIAkio`;
turn it off in the app before deleting the exe.

## License

GoodbyeDPI source in `native/` is Apache-2.0 (`native/LICENSE`). WinDivert is LGPLv3 / GPLv2.
