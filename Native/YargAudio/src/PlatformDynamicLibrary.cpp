#include "PlatformDynamicLibrary.h"

#if defined(_WIN32)
#include <windows.h>
#if WINAPI_FAMILY_PARTITION(WINAPI_PARTITION_APP) && \
    !WINAPI_FAMILY_PARTITION(WINAPI_PARTITION_DESKTOP)
#define YARG_AUDIO_WINDOWS_APP 1
#endif
#else
#include <dlfcn.h>
#if defined(__linux__)
#include <link.h>
#include <cstring>
#endif
#endif

#include <utility>

namespace yarg::audio {

#if defined(__linux__)
namespace {

struct LoadedModuleSearch {
    const char* name;
    void* handle;
};

int findLoadedModule(dl_phdr_info* info, std::size_t, void* data) noexcept {
    auto* search = static_cast<LoadedModuleSearch*>(data);
    if (!info || !info->dlpi_name || info->dlpi_name[0] == '\0') return 0;

    const char* fileName = std::strrchr(info->dlpi_name, '/');
    fileName = fileName ? fileName + 1 : info->dlpi_name;
    if (std::strcmp(fileName, search->name) != 0) return 0;

    // .NET and Unity commonly load native plugins by absolute path with RTLD_LOCAL.
    // RTLD_NOLOAD must use that same path on glibc; the short SONAME can miss it.
    search->handle = dlopen(info->dlpi_name, RTLD_NOW | RTLD_NOLOAD);
    return search->handle ? 1 : 0;
}

} // namespace
#endif

#if defined(YARG_AUDIO_WINDOWS_APP)
namespace {

HMODULE loadPackagedLibrary(const char* name) noexcept {
    wchar_t wideName[MAX_PATH];
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, name, -1,
            wideName, MAX_PATH) == 0) {
        return nullptr;
    }
    return LoadPackagedLibrary(wideName, 0);
}

} // namespace
#endif

PlatformDynamicLibrary::~PlatformDynamicLibrary() {
    reset();
}

PlatformDynamicLibrary::PlatformDynamicLibrary(PlatformDynamicLibrary&& other) noexcept
    : handle_(std::exchange(other.handle_, nullptr)),
      owned_(std::exchange(other.owned_, false)) {}

PlatformDynamicLibrary& PlatformDynamicLibrary::operator=(
    PlatformDynamicLibrary&& other) noexcept {
    if (this != &other) {
        reset();
        handle_ = std::exchange(other.handle_, nullptr);
        owned_ = std::exchange(other.owned_, false);
    }
    return *this;
}

PlatformDynamicLibrary PlatformDynamicLibrary::findLoaded(const char* name) noexcept {
    if (!name) return {};
#if defined(YARG_AUDIO_WINDOWS_APP)
    // GetModuleHandle is desktop-only before SDK 10.0.26100. LoadPackagedLibrary
    // returns the already-loaded module and adds a reference, so own it to drop
    // that reference again. It resolves only from the package graph, the same
    // single copy IL2CPP loads, so it cannot bind a second BASS instance.
    return {reinterpret_cast<void*>(loadPackagedLibrary(name)), true};
#elif defined(_WIN32)
    return {reinterpret_cast<void*>(GetModuleHandleA(name)), false};
#elif defined(RTLD_NOLOAD)
    // Keep loaded-module handles for process lifetime. BASS function tables do the same.
    void* handle = dlopen(name, RTLD_NOW | RTLD_NOLOAD);
#if defined(__linux__)
    if (!handle) {
        LoadedModuleSearch search{name, nullptr};
        dl_iterate_phdr(&findLoadedModule, &search);
        handle = search.handle;
    }
#endif
    return {handle, false};
#else
    return {};
#endif
}

PlatformDynamicLibrary PlatformDynamicLibrary::load(const char* name) noexcept {
    if (!name) return {};
#if defined(YARG_AUDIO_WINDOWS_APP)
    return {reinterpret_cast<void*>(loadPackagedLibrary(name)), true};
#elif defined(_WIN32)
    return {reinterpret_cast<void*>(LoadLibraryA(name)), true};
#else
    return {dlopen(name, RTLD_NOW | RTLD_LOCAL), true};
#endif
}

void* PlatformDynamicLibrary::symbol(const char* name) const noexcept {
    if (!handle_ || !name) return nullptr;
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(
        reinterpret_cast<HMODULE>(handle_), name));
#else
    return dlsym(handle_, name);
#endif
}

void PlatformDynamicLibrary::reset() noexcept {
    if (owned_ && handle_) {
#if defined(_WIN32)
        FreeLibrary(reinterpret_cast<HMODULE>(handle_));
#else
        dlclose(handle_);
#endif
    }
    handle_ = nullptr;
    owned_ = false;
}

} // namespace yarg::audio
