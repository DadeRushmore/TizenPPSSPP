param([string]$ZigPath = $env:TIZENPSP_ZIG)
$ErrorActionPreference = 'Stop'
if (-not $ZigPath) { throw 'Pass -ZigPath with the path to Zig 0.13.0.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    python tools/build-native.py --zig $ZigPath
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    python tools/check-native.py --zig $ZigPath
    if ($LASTEXITCODE -ne 0) { throw 'Native mocked-driver checks failed' }
    dotnet run --project checks/Check.csproj -c Release -- app/res/libtizenpsp_triangle.so
    if ($LASTEXITCODE -ne 0) { throw 'ELF validation failed' }
    dotnet run --project checks/Check.csproj -c Release --no-build -- app/res/libtizenpsp_triangle_v010.so tizenpsp_triangle
    if ($LASTEXITCODE -ne 0) { throw 'Baseline ELF validation failed' }
    dotnet run --project checks/Check.csproj -c Release --no-build -- app/res/libtizenpsp_glview.so tizenpsp_glview_clear
    if ($LASTEXITCODE -ne 0) { throw 'GLView ELF validation failed' }
    dotnet run --project checks/Check.csproj -c Release --no-build -- app/res/ppsspp_libretro-arm32.so --core
    if ($LASTEXITCODE -ne 0) { throw 'Actual core ELF validation failed' }
    dotnet run --project checks/Check.csproj -c Release --no-build -- app/res/ppsspp_libretro-arm32-jit.so --arm-jit-core
    if ($LASTEXITCODE -ne 0) { throw 'Experimental ARM JIT core ELF validation failed' }
    dotnet run --project checks/Check.csproj -c Release --no-build -- app/res/libtizenpsp_corebridge.so psp_bridge_begin
    if ($LASTEXITCODE -ne 0) { throw 'Core bridge ELF validation failed' }
    python tools/check-library-view.py
    if ($LASTEXITCODE -ne 0) { throw 'Library artwork lifecycle checks failed' }
    # Keep redistributed notices current even when native binaries are reused.
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination app/res/THIRD-PARTY-NOTICES.md
    Copy-Item -LiteralPath vendor/ppsspp/ext/ffmpeg/COPYING.LGPLv2.1 -Destination app/res/core-licenses/LICENSE-FFmpeg-LGPLv2.1.txt
    Copy-Item -LiteralPath vendor/ppsspp/ext/ffmpeg/LICENSE.md -Destination app/res/core-licenses/FFmpeg-LICENSE.md
    dotnet build app/TizenPSP.csproj -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tizen build failed' }
    python tools/verify-package.py
    if ($LASTEXITCODE -ne 0) { throw 'Package validation failed' }
} finally { Pop-Location }
