# Build everything: GoodbyeDPI (MSVC) -> .\bin, then the WinUI app (VS 2022 MSBuild; `dotnet build` lacks the PRI tasks)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build-goodbyedpi.ps1"
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -version "[17.0,18.0)" -property installationPath
& "$vs\MSBuild\Current\Bin\MSBuild.exe" "$PSScriptRoot\DPIAkio\DPIAkio.csproj" /restore /p:Configuration=Release /p:Platform=x64 /m /v:m
if ($LASTEXITCODE -ne 0) { throw "App build failed" }
Write-Host "App -> $PSScriptRoot\DPIAkio\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\DPIAkio.exe"
