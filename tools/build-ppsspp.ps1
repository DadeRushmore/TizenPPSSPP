param([Parameter(Mandatory=$true)][string]$ZigPath,[switch]$ConfigureOnly,[switch]$ArmJit)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $coreBuild=if($ArmJit){'build/ppsspp-arm-jit'}else{'build/ppsspp-core'}
    $coreAudit=if($ArmJit){'build/ppsspp-arm-jit-elf.json'}else{'build/ppsspp-core-elf.json'}
    $logPrefix=if($ArmJit){'build/arm-jit'}else{'build/core'}
    $jitOption=if($ArmJit){'ON'}else{'OFF'}
    $compiler=(Resolve-Path -LiteralPath $ZigPath).Path
    if ((& $compiler version) -ne '0.13.0') {throw 'Use Zig 0.13.0'}
    $env:ZIG_GLOBAL_CACHE_DIR="$projectRoot/.cache/zig-global"
    $env:ZIG_LOCAL_CACHE_DIR="$projectRoot/.cache/zig-local"
    $tc="$projectRoot/build/core-toolchain"
    New-Item -ItemType Directory -Force $tc | Out-Null
    foreach($item in @(@('cc','cc'),@('cxx','c++'),@('ar','ar'),@('ranlib','ranlib'))) {
        $extra=if($item[0] -in @('cc','cxx')) {' -target arm-linux-gnueabi.2.17 -mcpu=cortex_a9 -marm'} else {''}
        $line='"'+$compiler+'" '+$item[1]+$extra+' %*'
        [IO.File]::WriteAllText("$tc/$($item[0]).cmd","@echo off`r`n$line`r`n")
    }
    $forward=$tc.Replace('\','/')
    $config="set(CMAKE_SYSTEM_NAME Linux)`nset(CMAKE_SYSTEM_PROCESSOR armv7)`nset(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)`n"
    foreach($item in @(@('C_COMPILER','cc'),@('CXX_COMPILER','cxx'),@('ASM_COMPILER','cc'),@('AR','ar'),@('RANLIB','ranlib'))) {
        $config+='set(CMAKE_'+$item[0]+' "'+$forward+'/'+$item[1]+'.cmd")'+"`n"
    }
    $config+='set(CMAKE_POSITION_INDEPENDENT_CODE ON)'+"`n"+'set(CMAKE_C_FLAGS_INIT "-femulated-tls")'+"`n"+'set(CMAKE_CXX_FLAGS_INIT "-femulated-tls")'+"`n"
    [IO.File]::WriteAllText("$tc/arm32.cmake",$config)
    $cmake="$projectRoot/build/core-tools/cmake/data/bin/cmake.exe"
    if (!(Test-Path -LiteralPath $cmake)) {throw 'Prepare pinned core tools first; see native/core-build/README.md'}
    # The opt-in core needs the bundled H.264 decoder for games that load mpeg.prx.
    # Keep the interpreter build configuration reproducible as the existing fallback.
    & $cmake -S vendor/ppsspp -B $coreBuild -G Ninja "-DCMAKE_MAKE_PROGRAM=$projectRoot/build/core-tools/bin/ninja.exe" "-DCMAKE_TOOLCHAIN_FILE=$tc/arm32.cmake" "-DCMAKE_PROJECT_PPSSPP_INCLUDE=$projectRoot/native/core-build/options.cmake" "-DTIZENPSP_ARM_JIT=$jitOption" "-DUSE_FFMPEG=$jitOption" -DUSE_SYSTEM_FFMPEG=OFF -DCMAKE_BUILD_TYPE=Release -DLIBRETRO=ON -DUSING_GLES2=ON -DUSING_EGL=OFF -DARM_NO_VULKAN=ON -DVULKAN=OFF -DOPENXR=OFF -DUSE_MINIUPNPC=OFF -DUSE_DISCORD=OFF -DUSE_SYSTEM_LIBPNG=OFF -DHEADLESS=OFF -DUNITTEST=OFF -DATLAS_TOOL=OFF -DHTTPS_NOT_AVAILABLE=ON -DUSE_CCACHE=OFF -DARMIPS_USE_STD_FILESYSTEM=ON -DCMAKE_DISABLE_PRECOMPILE_HEADERS=ON -DENABLE_PCH=OFF *> "$logPrefix-configure.log"
    if ($LASTEXITCODE -ne 0) {Get-Content "$logPrefix-configure.log" -Tail 20;throw 'Core configure failed'}
    if (!$ConfigureOnly) {
        & $cmake --build $coreBuild --target ppsspp_libretro -j 4 *> "$logPrefix-build.log"
        if ($LASTEXITCODE -ne 0) {Get-Content "$logPrefix-build.log" -Tail 24;throw 'Core compile failed'}
        & $compiler objcopy --strip-debug "$coreBuild/lib/ppsspp_libretro.so" "$coreBuild/ppsspp_libretro-arm32.so"
        if ($LASTEXITCODE -ne 0) {throw 'Core debug stripping failed'}
        python tools/inspect-core-elf.py "$coreBuild/ppsspp_libretro-arm32.so" --output $coreAudit
        if ($LASTEXITCODE -ne 0) {throw 'ELF audit failed'}
        if($ArmJit){python checks/arm-jit-build-checks.py}else{python checks/core-build-checks.py}
        if ($LASTEXITCODE -ne 0) {throw 'Core build policy checks failed'}
    }
} finally {Pop-Location}
