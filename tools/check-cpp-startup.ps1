param([Parameter(Mandatory=$true)][string]$ZigPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $env:ZIG_GLOBAL_CACHE_DIR = "$projectRoot/.cache/zig-global"
    $env:ZIG_LOCAL_CACHE_DIR = "$projectRoot/.cache/zig-local"
    New-Item -ItemType Directory -Force build/core-audit | Out-Null
    & $ZigPath c++ -target arm-linux-gnueabi.2.17 -mcpu=cortex_a9 -marm -O2 -fPIC -fno-exceptions -fno-rtti -nostdlib -shared '-Wl,--hash-style=sysv' '-Wl,-z,max-page-size=4096' native/core-probes/cpp-startup.cpp -o build/core-audit/cpp-startup.so
    if ($LASTEXITCODE -ne 0) { throw 'Constructor fixture compilation failed' }
    dotnet run --project checks/Check.csproj -c Release -- build/core-audit/cpp-startup.so --expect-initializer-rejection
    if ($LASTEXITCODE -ne 0) { throw 'Loader compatibility check failed' }
} finally { Pop-Location }
