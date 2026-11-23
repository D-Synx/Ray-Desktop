#pragma once
#include <cstdint>
#include <string>
#include <vector>

struct PreviewRequest
{
    uint32_t width, height, outputWidth, outputHeight;
};

using Pixels = std::vector<uint8_t>;

bool ResizeD3D(const PreviewRequest& request, const Pixels& input, Pixels& output);
bool ResizeVulkan(const PreviewRequest& request, const Pixels& input, Pixels& output);
std::string ProbeD3D();
std::string ProbeVulkan();
