# Build a portable single-file release: .\release\DPIAkio.exe
# Runs on any Windows 10 1809+ / Windows 11 x64 without installing .NET, Windows App SDK or VC++ runtimes.
param(
    [switch]$SkipNative   # reuse .\bin\goodbyedpi.exe instead of recompiling it
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $SkipNative) { & "$root\build-goodbyedpi.ps1" }
foreach ($f in 'goodbyedpi.exe', 'WinDivert.dll', 'WinDivert64.sys') {
    if (-not (Test-Path "$root\bin\$f")) { throw "Missing bin\$f - run build-goodbyedpi.ps1 first (needs WinDivert in deps\)." }
}

# Visual Studio 2022 MSBuild ('dotnet publish' lacks the PRI tasks WinUI needs)
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -version "[17.0,18.0)" -property installationPath
if (-not $vs) { throw 'Visual Studio 2022 not found.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'

$publishDir = Join-Path $root 'release'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

& $msbuild "$root\DPIAkio\DPIAkio.csproj" /restore /t:Publish /m /v:m `
    /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
    /p:SelfContained=true /p:PublishSingleFile=true `
    "/p:OutDir=$root\build\app\" "/p:PublishDir=$publishDir\"
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }

$exe = Get-Item "$publishDir\DPIAkio.exe"
Get-ChildItem $publishDir | Where-Object Name -ne 'DPIAkio.exe' | ForEach-Object { Write-Warning "Extra file in release: $($_.Name)" }
Write-Host ("OK -> {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))
