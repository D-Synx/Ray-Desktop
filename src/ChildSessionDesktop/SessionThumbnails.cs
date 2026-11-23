using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ChildSessionDesktop
{
    internal sealed class SessionThumbnails : IDisposable
    {
        internal const int Width = GpuPreview.OutputWidth, Height = GpuPreview.OutputHeight;

        private const uint ClientOnly = 1;
        private const uint RenderFullContent = 2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr window, IntPtr destination, uint flags);

        private readonly HashSet<string> saved = new HashSet<string>();
        internal string Folder { get; private set; }

        internal SessionThumbnails()
        {
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RayDesktop", "Thumbnails", Guid.NewGuid().ToString("N"));
        }

        internal static Bitmap Capture(ChildSessionControl surface, bool requestHardware)
        {
            if (!surface.IsHandleCreated || surface.IsDisposed || !surface.Visible
                || surface.Width < 16 || surface.Height < 16 || (long)surface.Width * surface.Height > 36000000) return null;
            var connection = (MSTSCLib.IMsRdpClient9)surface.GetOcxObject();
            if (connection.Connected != 1) return null;
            var full = new Bitmap(surface.Width, surface.Height,
                requestHardware ? PixelFormat.Format32bppRgb : PixelFormat.Format24bppRgb);
            try
            {
                if (Render(surface.Handle, full, ClientOnly | RenderFullContent) && HasVisibleContent(full))
                    return full;
                if (Render(surface.Handle, full, ClientOnly) && HasVisibleContent(full)) return full;
            }
            catch { full.Dispose(); throw; }
            full.Dispose();
            return null;
        }

        private static bool Render(IntPtr window, Bitmap frame, uint flags)
        {
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.Black);
                IntPtr device = graphics.GetHdc();
                try { return PrintWindow(window, device, flags); }
                finally { graphics.ReleaseHdc(device); }
            }
        }

        private static bool HasVisibleContent(Bitmap frame)
        {
            // Some graphics drivers leave a black surface even when PrintWindow reports success.
            for (int y = 0; y < frame.Height; y += 24)
                for (int x = 0; x < frame.Width; x += 24)
                {
                    Color pixel = frame.GetPixel(x, y);
                    if (pixel.R > 4 || pixel.G > 4 || pixel.B > 4) return true;
                }
            return false;
        }

        internal void Save(string id, Bitmap image)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, id + ".png");
            string pending = path + ".tmp";
            try
            {
                image.Save(pending, ImageFormat.Png);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                saved.Add(id);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }

        internal void Remove(string id)
        {
            string path = Path.Combine(Folder, id + ".png");
            if (File.Exists(path)) File.Delete(path);
            saved.Remove(id);
        }

        public void Dispose()
        {
            foreach (string id in new List<string>(saved))
            {
                try { Remove(id); }
                catch (IOException ex) { Program.Log("Could not remove a session preview: " + ex.GetType().Name); }
                catch (UnauthorizedAccessException ex) { Program.Log("Could not remove a session preview: " + ex.GetType().Name); }
            }
            if (Directory.Exists(Folder) && Directory.GetFileSystemEntries(Folder).Length == 0)
                Directory.Delete(Folder);
        }
    }
}
