#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d2d1_1.h>
#include <d2d1_1helper.h>
#include <wrl/client.h>
#include <cstring>
#include <cwchar>
#include <sstream>
#include "gpu_preview.h"

using Microsoft::WRL::ComPtr;

namespace
{
    struct D3DDevice
    {
        ComPtr<ID3D11Device> device;
        DXGI_ADAPTER_DESC1 info{};
        D3D_FEATURE_LEVEL level{};

        bool Create(IDXGIAdapter* adapter, D3D_DRIVER_TYPE type, UINT flags)
        {
            return SUCCEEDED(D3D11CreateDevice(adapter, type, nullptr, flags | D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                nullptr, 0, D3D11_SDK_VERSION, device.ReleaseAndGetAddressOf(), &level, nullptr));
        }

        bool OpenHardware()
        {
            ComPtr<IDXGIFactory1> factory;
            if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(factory.GetAddressOf())))) return false;
            for (UINT index = 0; ; ++index)
            {
                ComPtr<IDXGIAdapter1> adapter;
                if (FAILED(factory->EnumAdapters1(index, &adapter))) return false;
                if (FAILED(adapter->GetDesc1(&info))) continue;
                // The primary Microsoft Basic adapter can omit the software flag.
                if ((info.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ||
                    (info.VendorId == 0x1414 && info.DeviceId == 0x008c) ||
                    _wcsicmp(info.Description, L"Microsoft Basic Render Driver") == 0 ||
                    _wcsicmp(info.Description, L"Microsoft Basic Display Adapter") == 0) continue;
                if (Create(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, D3D11_CREATE_DEVICE_VIDEO_SUPPORT) ||
                    Create(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, 0)) return true;
            }
        }
    };

    std::string Utf8(const wchar_t* name)
    {
        char text[512]{};
        return WideCharToMultiByte(CP_UTF8, 0, name, -1, text, sizeof(text), nullptr, nullptr) ? text : "";
    }
}

std::string ProbeD3D()
{
    D3DDevice hardware;
    if (!hardware.OpenHardware()) return "Direct3D 11: unavailable (no hardware adapter)\n";
    std::ostringstream report;
    report << "Direct3D 11: " << Utf8(hardware.info.Description) << " | feature level 0x"
        << std::hex << hardware.level << '\n';

    ComPtr<ID3D11VideoDevice> video;
    if (SUCCEEDED(hardware.device.As(&video)))
    {
        // The AV1 GUID is defined by DXVA; spelling it here also supports older Windows SDK headers.
        const GUID av1 = { 0xb8be4ccb, 0xcf53, 0x46ba, { 0x8d, 0x59, 0xd6, 0xb8, 0xa6, 0xda, 0x5d, 0x2a } };
        const GUID* profiles[] = { &D3D11_DECODER_PROFILE_H264_VLD_NOFGT, &D3D11_DECODER_PROFILE_HEVC_VLD_MAIN, &av1 };
        const DXGI_FORMAT formats[] = { DXGI_FORMAT_NV12, DXGI_FORMAT_NV12, DXGI_FORMAT_NV12 };
        const char* labels[] = { "H.264", "HEVC Main", "AV1 Profile 0" };
        for (size_t i = 0; i < 3; ++i)
        {
            BOOL supported = FALSE;
            HRESULT result = video->CheckVideoDecoderFormat(profiles[i], formats[i], &supported);
            report << "D3D11 decoder " << labels[i] << " (NV12): "
                << (FAILED(result) ? "unknown" : supported ? "supported" : "not reported") << '\n';
        }
    }
    else report << "D3D11 decoder profiles: unavailable\n";
    return report.str();
}

bool ResizeD3D(const PreviewRequest& r, const Pixels& input, Pixels& output, bool warpTest)
{
    D3DDevice renderer;
    if (!(warpTest ? renderer.Create(nullptr, D3D_DRIVER_TYPE_WARP, 0) : renderer.OpenHardware())) return false;

    ComPtr<IDXGIDevice> dxgi;
    ComPtr<ID2D1Device> d2d;
    ComPtr<ID2D1DeviceContext> context;
    if (FAILED(renderer.device.As(&dxgi)) ||
        FAILED(D2D1CreateDevice(dxgi.Get(), nullptr, &d2d)) ||
        FAILED(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &context)))
        return false;

    const auto format = D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE);
    auto bitmapProperties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE, format, 96, 96);
    ComPtr<ID2D1Bitmap1> source, target, readback;
    const auto size = D2D1::SizeU(r.outputWidth, r.outputHeight);
    if (FAILED(context->CreateBitmap(D2D1::SizeU(r.width, r.height), input.data(), r.width * 4, bitmapProperties, &source)))
        return false;
    bitmapProperties.bitmapOptions = D2D1_BITMAP_OPTIONS_TARGET;
    if (FAILED(context->CreateBitmap(size, nullptr, 0, bitmapProperties, &target))) return false;

    context->SetDpi(96, 96);
    context->SetTarget(target.Get());
    context->BeginDraw();
    const auto destination = D2D1::RectF(0, 0, static_cast<float>(r.outputWidth), static_cast<float>(r.outputHeight));
    context->DrawBitmap(source.Get(), &destination, 1.0f, D2D1_INTERPOLATION_MODE_LINEAR, nullptr);
    const HRESULT result = context->EndDraw();
    context->SetTarget(nullptr);
    if (FAILED(result) || FAILED(renderer.device->GetDeviceRemovedReason())) return false;

    bitmapProperties.bitmapOptions = D2D1_BITMAP_OPTIONS_CPU_READ | D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
    if (FAILED(context->CreateBitmap(size, nullptr, 0, bitmapProperties, &readback)) ||
        FAILED(readback->CopyFromBitmap(nullptr, target.Get(), nullptr))) return false;
    D2D1_MAPPED_RECT mapped{};
    if (FAILED(readback->Map(D2D1_MAP_OPTIONS_READ, &mapped))) return false;
    for (uint32_t row = 0; row < r.outputHeight; ++row)
        std::memcpy(output.data() + size_t(row) * r.outputWidth * 4,
            mapped.bits + size_t(row) * mapped.pitch, size_t(r.outputWidth) * 4);
    return SUCCEEDED(readback->Unmap());
}
