using System;
using System.Drawing;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RayDesktopTests")]

namespace ChildSessionDesktop
{
    // Window sizing calculations shared by the host and regression tests.
    internal static class Geometry
    {
        internal const int WmszLeft = 1, WmszRight = 2, WmszTop = 3, WmszTopLeft = 4,
            WmszTopRight = 5, WmszBottom = 6, WmszBottomLeft = 7, WmszBottomRight = 8;

        internal static Size RemoteDisplaySize(int width, int height, bool fluid)
        {
            int maxWidth = fluid ? 1600 : 2560;
            int maxHeight = fluid ? 900 : 1440;
            return new Size(Math.Max(640, Math.Min(maxWidth, width)) & ~1,
                Math.Max(480, Math.Min(maxHeight, height)));
        }

        internal static uint ToMillimeters(int pixels, float dpi)
        {
            if (dpi <= 0) dpi = 96;
            return (uint)Math.Max(10, Math.Round(pixels * 25.4 / dpi));
        }

        internal static void ComputeLetterbox(int clientW, int clientH, int sessW, int sessH,
            out int dw, out int dh, out int dx, out int dy)
        {
            if (clientW <= 0 || clientH <= 0) { dw = clientW; dh = clientH; dx = 0; dy = 0; return; }
            if (sessW > 0 && sessH > 0)
            {
                double s = Math.Min((double)clientW / sessW, (double)clientH / sessH);
                if (s > 1.0) s = 1.0;
                dw = (int)(sessW * s + 0.5);
                dh = (int)(sessH * s + 0.5);
            }
            else { dw = clientW; dh = clientH; }
            dx = (clientW - dw) / 2;
            dy = (clientH - dh) / 2;
        }

        internal static bool LockAspectRect(Rect drag, Rect anchor, int bw, int bh, double aspect, int dir, out Rect result)
        {
            int cw = (drag.Right - drag.Left) - bw;
            int ch = (drag.Bottom - drag.Top) - bh;
            result = default(Rect);
            if (!(cw > 160 && ch > 120)) return false;

            int ncw, nch;
            if ((double)cw / ch > aspect) { ncw = (int)(ch * aspect + 0.5); nch = ch; }
            else { ncw = cw; nch = (int)(cw / aspect + 0.5); }
            int W = bw + ncw;
            int H = bh + nch;

            int L = anchor.Left, T = anchor.Top, R = anchor.Right, B = anchor.Bottom;
            result = new Rect { Left = L, Top = T, Right = R, Bottom = B };
            switch (dir)
            {
                case WmszLeft:        result.Left = R - W; result.Right = R; result.Top = T; result.Bottom = T + H; break;
                case WmszRight:       result.Right = L + W; result.Left = L; result.Top = T; result.Bottom = T + H; break;
                case WmszTop:         result.Top = B - H; result.Bottom = B; result.Left = L; result.Right = L + W; break;
                case WmszTopLeft:     result.Left = R - W; result.Right = R; result.Top = B - H; result.Bottom = B; break;
                case WmszTopRight:    result.Right = L + W; result.Left = L; result.Top = B - H; result.Bottom = B; break;
                case WmszBottom:      result.Bottom = T + H; result.Top = T; result.Left = L; result.Right = L + W; break;
                case WmszBottomLeft:  result.Left = R - W; result.Right = R; result.Bottom = T + H; result.Top = T; break;
                case WmszBottomRight: result.Right = L + W; result.Left = L; result.Bottom = T + H; result.Top = T; break;
                default:              result.Right = L + W; result.Left = L; result.Bottom = T + H; result.Top = T; break;
            }
            return true;
        }
    }

    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
