# Build GoodbyeDPI from source with MSVC and stage it (plus WinDivert) into .\bin
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src  = Join-Path $root 'native\src'
$wd   = Join-Path $root 'deps\WinDivert-2.2.2-A'
$out  = Join-Path $root 'build\goodbyedpi'
$bin  = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out, $bin | Out-Null

$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$files = @('goodbyedpi.c','blackwhitelist.c','dnsredir.c','fakepackets.c','service.c','ttltrack.c','utils\getline.c','utils\repl_str.c') |
    ForEach-Object { '"' + (Join-Path $src $_) + '"' }

$cmd = @(
  "call `"$vcvars`" >nul",
  "cd /d `"$out`"",
  "rc /nologo /fo goodbyedpi-rc.res `"$src\goodbyedpi-rc.rc`"",
  ("cl /nologo /O2 /std:c11 /W3 /MT /GS /guard:cf /DWIN32_LEAN_AND_MEAN /FI`"$root\native\compat\msvc_compat.h`" " +
   "/I`"$root\native\compat`" /I`"$wd\include`" " + ($files -join ' ') +
   " goodbyedpi-rc.res /Fe:goodbyedpi.exe /link /LIBPATH:`"$wd\x64`" WinDivert.lib ws2_32.lib advapi32.lib /DYNAMICBASE /NXCOMPAT /guard:cf")
) -join ' && '

cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

Copy-Item "$out\goodbyedpi.exe" $bin -Force
Copy-Item "$wd\x64\WinDivert.dll", "$wd\x64\WinDivert64.sys" $bin -Force
Write-Host "OK -> $bin"
