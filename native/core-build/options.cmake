get_filename_component(TIZENPSP_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
# Graphics imports are resolved by the frontend, not linked against host GPU libs.
add_library(tizenpsp_graphics_imports INTERFACE)
set(OPENGL_LIBRARIES tizenpsp_graphics_imports CACHE STRING "Frontend GLES imports" FORCE)
option(TIZENPSP_ARM_JIT "Build the opt-in ARM JIT experiment" OFF)
if(TIZENPSP_ARM_JIT)
    # Mode 2 samples coarse work and draws, without per-uniform/state scopes.
    add_compile_definitions(TIZENPSP_ARM_JIT TIZENPSP_DETAILED_PROFILE=2)
    include_directories("${TIZENPSP_ROOT}/native/core-build/runtime")
else()
    add_compile_definitions(TIZENPSP_NO_JIT)
endif()
include_directories("${TIZENPSP_ROOT}/build/core-headers")
