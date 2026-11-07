# DPIAkio

WinUI 3 desktop GUI for [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI).

## Layout

- `DPIAkio/` — WinUI 3 app (.NET 9, unpackaged, self-contained).
- `native/` — GoodbyeDPI C source + MSVC compatibility shims (see `native/README.md`).
- `build-goodbyedpi.ps1` — builds `goodbyedpi.exe` with MSVC into `bin/` and copies WinDivert next to it.
- `build-app.ps1` — runs the script above, then builds the app.

## Build

Requirements: Visual Studio 2022 (Desktop C++ + .NET desktop workloads), .NET 9 SDK.

1. Download [WinDivert 2.2.2](https://github.com/basil00/WinDivert/releases/tag/v2.2.2) (`WinDivert-2.2.2-A.zip`)
   and extract to `deps/WinDivert-2.2.2-A/`. Windows Defender may flag it; add an exclusion for this folder.
2. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File build-app.ps1
   ```

Output: `DPIAkio/bin/x64/Release/net9.0-windows10.0.19041.0/win-x64/DPIAkio.exe` (runs as administrator).

`dotnet build` does not work for this project (missing PRI tasks); the script uses Visual Studio's MSBuild.

## License

GoodbyeDPI source in `native/` is Apache-2.0 (`native/LICENSE`).
