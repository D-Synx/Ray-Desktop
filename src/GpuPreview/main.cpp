#include "gpu_preview.h"

#include <cstdio>
#include <fcntl.h>
#include <io.h>

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    const std::string mode(argv[1]);
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);

    try
    {
        if (mode == "probe")
        {
            const std::string report = ProbeD3D() + ProbeVulkan();
            return std::fwrite(report.data(), 1, report.size(), stdout) == report.size() ? 0 : 4;
        }

        if (mode != "d3d11" && mode != "vulkan") return 2;
        PreviewRequest request{};
        static_assert(sizeof(PreviewRequest) == 16, "Preview protocol header");
        if (std::fread(&request, 1, sizeof(request), stdin) != sizeof(request)) return 2;
        if (!request.width || !request.height || request.width > 8192 || request.height > 8192 ||
            uint64_t(request.width) * request.height > 16777216 ||
            !request.outputWidth || !request.outputHeight || request.outputWidth > 160 || request.outputHeight > 90)
            return 2;

        Pixels input(size_t(request.width) * request.height * 4);
        if (std::fread(input.data(), 1, input.size(), stdin) != input.size()) return 2;
        Pixels output(size_t(request.outputWidth) * request.outputHeight * 4);
        const bool completed = mode == "d3d11"
            ? ResizeD3D(request, input, output) : ResizeVulkan(request, input, output);
        if (!completed) return 3;
        return std::fwrite(output.data(), 1, output.size(), stdout) == output.size() ? 0 : 4;
    }
    catch (...)
    {
        // Preview creation is optional. The WinForms client retries with Windows rendering.
        return 4;
    }
}
