using System;
using System.Drawing;

namespace ChildSessionDesktop
{
    internal enum RdpPerformanceProfile { Standard, Fluid, Fast }

    internal static class GraphicsOptions
    {
        // Wallpaper, full-window drag, menu animations, cursor shadow.
        private const int FluidPerformanceFlags = 0x01 | 0x02 | 0x04 | 0x20;
        private const int DisableTheming = 0x08;
        private const int EnableFontSmoothing = 0x80;
        private const int EnableDesktopComposition = 0x100;

        internal static int PerformanceFlags(RdpPerformanceProfile profile, int defaults)
        {
            switch (profile)
            {
                case RdpPerformanceProfile.Fluid:
                    return FluidPerformanceFlags;
                case RdpPerformanceProfile.Fast:
                    return (defaults | FluidPerformanceFlags | DisableTheming) & ~(EnableFontSmoothing | EnableDesktopComposition);
                default:
                    return defaults;
            }
        }

        internal static string ProfileLabel(RdpPerformanceProfile profile)
        {
            switch (profile)
            {
                case RdpPerformanceProfile.Fast: return Strings.T("rdp_performance_fast");
                case RdpPerformanceProfile.Fluid: return Strings.T("graphics_fluid");
                default: return Strings.T("graphics_default");
            }
        }

        internal static Size LimitFastSize(int width, int height)
        {
            width = Math.Max(200, width);
            height = Math.Max(200, height);
            double ratio = Math.Min(1.0, Math.Min(1280.0 / width, 720.0 / height));
            return new Size(Math.Max(200, (int)(width * ratio) & ~1), Math.Max(200, (int)(height * ratio) & ~1));
        }

        internal static Size DisplaySize(Size automatic, RdpPerformanceProfile profile, Size? manual = null)
        {
            if (!manual.HasValue && profile == RdpPerformanceProfile.Fast)
                return LimitFastSize(automatic.Width, automatic.Height);

            Size target = manual ?? automatic;
            bool fluid = !manual.HasValue && profile == RdpPerformanceProfile.Fluid;
            return new Size(Math.Max(200, Math.Min(fluid ? 1600 : 8192, target.Width)) & ~1,
                Math.Max(200, Math.Min(fluid ? 900 : 8192, target.Height)));
        }
    }
}
