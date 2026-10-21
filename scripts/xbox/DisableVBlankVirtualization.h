// Included into Unity's generated App.cpp by Build-Xbox.ps1; call it before Unity creates its D3D device.
//
// Experiment: with the console's HDMI output at 119.88 Hz, Unity's player still presents at 59.94 fps with
// VSync on, and so does a native D3D app (https://learn.microsoft.com/answers/questions/2142906). DXGI shows
// swap chains a virtualized 60 Hz v-blank while a display runs at 120 Hz for dynamic refresh rate, unless the
// process opts out with DXGIDisableVBlankVirtualization before creating any swap chain
// (https://learn.microsoft.com/windows/win32/api/dxgi1_6/nf-dxgi1_6-dxgidisablevblankvirtualization). It is
// documented for desktop apps only, so it's looked up at runtime, and the outcome goes to
// TempState\vblank-virtualization.txt.
#pragma once

#include <cstdio>
#include <dxgi1_3.h>
#include <wrl/client.h>

#pragma comment(lib, "dxgi.lib")

inline void YargDisableVBlankVirtualization()
{
    wchar_t result[128];

    // dxgi.dll isn't loaded until something uses it, and a UWP app can't LoadLibrary a system DLL
    Microsoft::WRL::ComPtr<IDXGIFactory2> factory;
    HRESULT factoryResult = CreateDXGIFactory2(0, IID_PPV_ARGS(&factory));

    using DisableFn = HRESULT(WINAPI*)();
    HMODULE dxgi = GetModuleHandleW(L"dxgi.dll");
    auto disable = dxgi != nullptr
        ? reinterpret_cast<DisableFn>(GetProcAddress(dxgi, "DXGIDisableVBlankVirtualization"))
        : nullptr;

    if (disable == nullptr)
    {
        swprintf_s(result, L"DXGIDisableVBlankVirtualization: not exported (dxgi %s, factory 0x%08X)\n",
            dxgi != nullptr ? L"loaded" : L"not loaded", static_cast<unsigned>(factoryResult));
    }
    else
    {
        HRESULT hr = disable();
        swprintf_s(result, L"DXGIDisableVBlankVirtualization: 0x%08X\n", static_cast<unsigned>(hr));
    }

    auto path = Windows::Storage::ApplicationData::Current->TemporaryFolder->Path + L"\\vblank-virtualization.txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, path->Data(), L"w, ccs=UTF-8") == 0 && file != nullptr)
    {
        fputws(result, file);
        fclose(file);
    }
}
