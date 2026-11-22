// TizenPSP process-lifetime core: require glibc's thread-destructor service.
// Replaces libc++abi's optional native-TLS fallback. The host MUST verify this
// import and retain the module mapping until process exit. No dlclose support.
extern "C" int __cxa_thread_atexit_impl(void (*destructor)(void *),void *object,void *dso);
// This address identifies the permanently mapped module to destructor
// registration. It is not a native dynamic-linker handle. The final ELF still
// needs auditing for any CRT metadata retained by the compiler driver.
extern "C" {
__attribute__((visibility("hidden"))) void *__dso_handle=&__dso_handle;
}
extern "C" int __cxa_thread_atexit(void (*destructor)(void *),void *object,void *dso) noexcept {
    return __cxa_thread_atexit_impl(destructor,object,dso);
}
