#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan.h>
#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <sstream>
#include "gpu_preview.h"

namespace
{
    constexpr VkFormat Format = VK_FORMAT_B8G8R8A8_UNORM;
    constexpr VkImageSubresourceRange ColorRange{ VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    constexpr VkImageSubresourceLayers ColorLayer{ VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };

#define VULKAN_FUNCTIONS(X) \
    X(CreateInstance) X(DestroyInstance) X(EnumeratePhysicalDevices) X(GetPhysicalDeviceProperties) \
    X(GetPhysicalDeviceFormatProperties) X(GetPhysicalDeviceQueueFamilyProperties) \
    X(GetPhysicalDeviceMemoryProperties) X(EnumerateDeviceExtensionProperties) X(CreateDevice) \
    X(DestroyDevice) X(GetDeviceQueue) X(CreateBuffer) X(DestroyBuffer) X(GetBufferMemoryRequirements) \
    X(CreateImage) X(DestroyImage) X(GetImageMemoryRequirements) X(AllocateMemory) X(FreeMemory) \
    X(BindBufferMemory) X(BindImageMemory) X(MapMemory) X(UnmapMemory) X(CreateCommandPool) \
    X(DestroyCommandPool) X(AllocateCommandBuffers) X(BeginCommandBuffer) X(EndCommandBuffer) \
    X(CmdPipelineBarrier) X(CmdCopyBufferToImage) X(CmdBlitImage) X(CmdCopyImageToBuffer) \
    X(CreateFence) X(DestroyFence) X(QueueSubmit) X(WaitForFences)

    struct Vulkan
    {
        HMODULE loader = nullptr;
#define DECLARE(name) PFN_vk##name name = nullptr;
        VULKAN_FUNCTIONS(DECLARE)
#undef DECLARE
        VkInstance instance = VK_NULL_HANDLE;
        VkPhysicalDevice physical = VK_NULL_HANDLE;
        VkPhysicalDeviceProperties properties{};
        VkDevice device = VK_NULL_HANDLE;
        uint32_t queueFamily = 0;
        VkBuffer staging = VK_NULL_HANDLE;
        VkDeviceMemory stagingMemory = VK_NULL_HANDLE;
        VkImage source = VK_NULL_HANDLE, target = VK_NULL_HANDLE;
        VkDeviceMemory sourceMemory = VK_NULL_HANDLE, targetMemory = VK_NULL_HANDLE;
        VkCommandPool pool = VK_NULL_HANDLE;
        VkFence fence = VK_NULL_HANDLE;

        ~Vulkan()
        {
            if (device)
            {
                if (fence) DestroyFence(device, fence, nullptr);
                if (pool) DestroyCommandPool(device, pool, nullptr);
                if (target) DestroyImage(device, target, nullptr);
                if (source) DestroyImage(device, source, nullptr);
                if (staging) DestroyBuffer(device, staging, nullptr);
                if (targetMemory) FreeMemory(device, targetMemory, nullptr);
                if (sourceMemory) FreeMemory(device, sourceMemory, nullptr);
                if (stagingMemory) FreeMemory(device, stagingMemory, nullptr);
                DestroyDevice(device, nullptr);
            }
            if (instance) DestroyInstance(instance, nullptr);
            if (loader) FreeLibrary(loader);
        }

        bool Open()
        {
            loader = LoadLibraryExW(L"vulkan-1.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!loader) return false;
#define LOAD(name) name = reinterpret_cast<PFN_vk##name>(GetProcAddress(loader, "vk" #name)); if (!name) return false;
            VULKAN_FUNCTIONS(LOAD)
#undef LOAD
            VkApplicationInfo app{ VK_STRUCTURE_TYPE_APPLICATION_INFO };
            app.pApplicationName = "RayXDesktop Preview";
            app.apiVersion = VK_API_VERSION_1_0;
            VkInstanceCreateInfo info{ VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO };
            info.pApplicationInfo = &app;
            return CreateInstance(&info, nullptr, &instance) == VK_SUCCESS;
        }

        bool PickAdapter()
        {
            uint32_t count = 0;
            if (EnumeratePhysicalDevices(instance, &count, nullptr) != VK_SUCCESS || !count) return false;
            std::vector<VkPhysicalDevice> devices(count);
            if (EnumeratePhysicalDevices(instance, &count, devices.data()) != VK_SUCCESS) return false;
            for (auto adapter : devices)
            {
                VkPhysicalDeviceProperties details{};
                GetPhysicalDeviceProperties(adapter, &details);
                if (details.deviceType != VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU &&
                    details.deviceType != VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU) continue;

                VkFormatProperties format{};
                GetPhysicalDeviceFormatProperties(adapter, Format, &format);
                constexpr VkFormatFeatureFlags required = VK_FORMAT_FEATURE_BLIT_SRC_BIT |
                    VK_FORMAT_FEATURE_BLIT_DST_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT;
                if ((format.optimalTilingFeatures & required) != required) continue;

                uint32_t queues = 0;
                GetPhysicalDeviceQueueFamilyProperties(adapter, &queues, nullptr);
                if (!queues) continue;
                std::vector<VkQueueFamilyProperties> families(queues);
                GetPhysicalDeviceQueueFamilyProperties(adapter, &queues, families.data());
                for (uint32_t index = 0; index < queues; ++index)
                {
                    if (!families[index].queueCount || !(families[index].queueFlags & VK_QUEUE_GRAPHICS_BIT)) continue;
                    physical = adapter;
                    properties = details;
                    queueFamily = index;
                    return true;
                }
            }
            return false;
        }

        bool OpenDevice()
        {
            const float priority = 1;
            VkDeviceQueueCreateInfo queue{ VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO };
            queue.queueFamilyIndex = queueFamily;
            queue.queueCount = 1;
            queue.pQueuePriorities = &priority;
            VkDeviceCreateInfo info{ VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO };
            info.queueCreateInfoCount = 1;
            info.pQueueCreateInfos = &queue;
            return CreateDevice(physical, &info, nullptr, &device) == VK_SUCCESS;
        }

        bool Allocate(const VkMemoryRequirements& requirements, VkMemoryPropertyFlags flags, VkDeviceMemory& memory)
        {
            VkPhysicalDeviceMemoryProperties available{};
            GetPhysicalDeviceMemoryProperties(physical, &available);
            for (uint32_t i = 0; i < available.memoryTypeCount; ++i)
            {
                if (!(requirements.memoryTypeBits & (1u << i)) ||
                    (available.memoryTypes[i].propertyFlags & flags) != flags) continue;
                VkMemoryAllocateInfo info{ VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO };
                info.allocationSize = requirements.size;
                info.memoryTypeIndex = i;
                return AllocateMemory(device, &info, nullptr, &memory) == VK_SUCCESS;
            }
            return false;
        }

        bool MakeStaging(VkDeviceSize bytes)
        {
            VkBufferCreateInfo info{ VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
            info.size = bytes;
            info.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
            if (CreateBuffer(device, &info, nullptr, &staging) != VK_SUCCESS) return false;
            VkMemoryRequirements requirements{};
            GetBufferMemoryRequirements(device, staging, &requirements);
            return Allocate(requirements, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT, stagingMemory) &&
                BindBufferMemory(device, staging, stagingMemory, 0) == VK_SUCCESS;
        }

        bool MakeImage(uint32_t w, uint32_t h, VkImageUsageFlags usage, VkImage& image, VkDeviceMemory& memory)
        {
            VkImageCreateInfo info{ VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
            info.imageType = VK_IMAGE_TYPE_2D;
            info.format = Format;
            info.extent = { w, h, 1 };
            info.mipLevels = info.arrayLayers = 1;
            info.samples = VK_SAMPLE_COUNT_1_BIT;
            info.tiling = VK_IMAGE_TILING_OPTIMAL;
            info.usage = usage;
            if (CreateImage(device, &info, nullptr, &image) != VK_SUCCESS) return false;
            VkMemoryRequirements requirements{};
            GetImageMemoryRequirements(device, image, &requirements);
            return Allocate(requirements, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, memory) &&
                BindImageMemory(device, image, memory, 0) == VK_SUCCESS;
        }

        void Transition(VkCommandBuffer commands, VkImage image, VkImageLayout before, VkImageLayout after,
            VkAccessFlags src, VkAccessFlags dst)
        {
            VkImageMemoryBarrier barrier{ VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER };
            barrier.srcAccessMask = src;
            barrier.dstAccessMask = dst;
            barrier.oldLayout = before;
            barrier.newLayout = after;
            barrier.srcQueueFamilyIndex = barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.image = image;
            barrier.subresourceRange = ColorRange;
            CmdPipelineBarrier(commands, src ? VK_PIPELINE_STAGE_TRANSFER_BIT : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &barrier);
        }

        bool Resize(const PreviewRequest& r, const Pixels& pixels, Pixels& output)
        {
            if (r.width > properties.limits.maxImageDimension2D || r.height > properties.limits.maxImageDimension2D)
                return false;
            const auto usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            if (!MakeStaging(pixels.size() + output.size()) ||
                !MakeImage(r.width, r.height, usage, source, sourceMemory) ||
                !MakeImage(r.outputWidth, r.outputHeight, usage, target, targetMemory)) return false;

            void* mapped = nullptr;
            if (MapMemory(device, stagingMemory, 0, VK_WHOLE_SIZE, 0, &mapped) != VK_SUCCESS) return false;
            std::memcpy(mapped, pixels.data(), pixels.size());
            UnmapMemory(device, stagingMemory);

            VkCommandPoolCreateInfo poolInfo{ VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO };
            poolInfo.queueFamilyIndex = queueFamily;
            if (CreateCommandPool(device, &poolInfo, nullptr, &pool) != VK_SUCCESS) return false;
            VkCommandBufferAllocateInfo allocation{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO };
            allocation.commandPool = pool;
            allocation.commandBufferCount = 1;
            VkCommandBuffer commands = VK_NULL_HANDLE;
            if (AllocateCommandBuffers(device, &allocation, &commands) != VK_SUCCESS) return false;
            VkCommandBufferBeginInfo begin{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO };
            begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
            if (BeginCommandBuffer(commands, &begin) != VK_SUCCESS) return false;

            Transition(commands, source, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 0, VK_ACCESS_TRANSFER_WRITE_BIT);
            VkBufferImageCopy upload{};
            upload.imageSubresource = ColorLayer;
            upload.imageExtent = { r.width, r.height, 1 };
            CmdCopyBufferToImage(commands, staging, source, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &upload);
            Transition(commands, source, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT);
            Transition(commands, target, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 0, VK_ACCESS_TRANSFER_WRITE_BIT);

            VkImageBlit blit{};
            blit.srcSubresource = blit.dstSubresource = ColorLayer;
            blit.srcOffsets[1] = { static_cast<int32_t>(r.width), static_cast<int32_t>(r.height), 1 };
            blit.dstOffsets[1] = { static_cast<int32_t>(r.outputWidth), static_cast<int32_t>(r.outputHeight), 1 };
            CmdBlitImage(commands, source, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                target, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VK_FILTER_LINEAR);
            Transition(commands, target, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT);

            VkBufferImageCopy download{};
            download.bufferOffset = pixels.size();
            download.imageSubresource = ColorLayer;
            download.imageExtent = { r.outputWidth, r.outputHeight, 1 };
            CmdCopyImageToBuffer(commands, target, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, staging, 1, &download);
            VkBufferMemoryBarrier hostRead{ VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
            hostRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            hostRead.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
            hostRead.srcQueueFamilyIndex = hostRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            hostRead.buffer = staging;
            hostRead.offset = pixels.size();
            hostRead.size = output.size();
            CmdPipelineBarrier(commands, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT,
                0, 0, nullptr, 1, &hostRead, 0, nullptr);
            if (EndCommandBuffer(commands) != VK_SUCCESS) return false;

            VkFenceCreateInfo fenceInfo{ VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
            if (CreateFence(device, &fenceInfo, nullptr, &fence) != VK_SUCCESS) return false;
            VkQueue queue = VK_NULL_HANDLE;
            GetDeviceQueue(device, queueFamily, 0, &queue);
            VkSubmitInfo submit{ VK_STRUCTURE_TYPE_SUBMIT_INFO };
            submit.commandBufferCount = 1;
            submit.pCommandBuffers = &commands;
            if (QueueSubmit(queue, 1, &submit, fence) != VK_SUCCESS) return false;
            const auto status = WaitForFences(device, 1, &fence, VK_TRUE, 3000000000ull);
            // Leave pending device resources to Windows if the fence never completes.
            if (status == VK_TIMEOUT) std::exit(4);
            if (status != VK_SUCCESS) return false;
            if (MapMemory(device, stagingMemory, 0, VK_WHOLE_SIZE, 0, &mapped) != VK_SUCCESS) return false;
            std::memcpy(output.data(), static_cast<uint8_t*>(mapped) + pixels.size(), output.size());
            UnmapMemory(device, stagingMemory);
            return true;
        }
    };
#undef VULKAN_FUNCTIONS
}

bool ResizeVulkan(const PreviewRequest& request, const Pixels& input, Pixels& output)
{
    Vulkan vk;
    return vk.Open() && vk.PickAdapter() && vk.OpenDevice() && vk.Resize(request, input, output);
}

std::string ProbeVulkan()
{
    Vulkan vk;
    if (!vk.Open()) return "Vulkan: Windows loader or instance unavailable\n";
    if (!vk.PickAdapter()) return "Vulkan: no integrated/discrete GPU with BGRA linear blit\n";
    std::ostringstream report;
    const uint32_t api = vk.properties.apiVersion;
    report << "Vulkan: " << vk.properties.deviceName << " | API " << VK_VERSION_MAJOR(api) << '.'
        << VK_VERSION_MINOR(api) << '.' << VK_VERSION_PATCH(api)
        << " | raw driverVersion " << vk.properties.driverVersion << '\n';

    uint32_t count = 0;
    if (vk.EnumerateDeviceExtensionProperties(vk.physical, nullptr, &count, nullptr) != VK_SUCCESS || !count)
        return report.str() + "Vulkan Video extensions: unavailable\n";
    std::vector<VkExtensionProperties> extensions(count);
    if (vk.EnumerateDeviceExtensionProperties(vk.physical, nullptr, &count, extensions.data()) != VK_SUCCESS)
        return report.str() + "Vulkan Video extensions: unavailable\n";

    const char* names[] = { VK_KHR_VIDEO_QUEUE_EXTENSION_NAME, VK_KHR_VIDEO_DECODE_QUEUE_EXTENSION_NAME,
        VK_KHR_VIDEO_DECODE_H264_EXTENSION_NAME, VK_KHR_VIDEO_DECODE_H265_EXTENSION_NAME,
        VK_KHR_VIDEO_DECODE_AV1_EXTENSION_NAME };
    for (const char* name : names)
    {
        const bool exposed = std::any_of(extensions.cbegin(), extensions.cend(),
            [name](const VkExtensionProperties& item) { return std::strcmp(name, item.extensionName) == 0; });
        report << name << ": " << (exposed ? "exposed" : "not exposed") << '\n';
    }
    return report.str();
}
