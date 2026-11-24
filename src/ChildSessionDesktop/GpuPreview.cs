using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChildSessionDesktop
{
    internal enum PreviewEngine { Auto, Windows, Direct3D, Vulkan }

    internal sealed class GpuPreview : IDisposable
    {
        private const long MaximumGpuPixels = 16777216;
        internal const int OutputWidth = 160, OutputHeight = 90;
        private const string HelperName = "RayXDesktop.GpuPreview.exe";
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string helper;
        private int generation;
        private bool hardwareFailed;
        private PreviewEngine? used;
        private string fallbackReason;
        private double processingMilliseconds;

        internal PreviewEngine Preferred { get; private set; }
        internal bool TriesHardware
        {
            get { return !hardwareFailed && (Preferred == PreviewEngine.Direct3D || Preferred == PreviewEngine.Vulkan); }
        }

        internal GpuPreview(PreviewEngine preferred, string helperPath = null)
        {
            Preferred = preferred;
            helper = helperPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HelperName);
        }

        internal static PreviewEngine ParseEngine(string value)
        {
            PreviewEngine engine;
            return Enum.TryParse(value, false, out engine) && engine.ToString() == value
                ? engine : PreviewEngine.Auto;
        }

        internal static string EngineName(PreviewEngine engine)
        {
            switch (engine)
            {
                case PreviewEngine.Direct3D: return "Direct3D 11";
                case PreviewEngine.Vulkan: return "Vulkan";
                case PreviewEngine.Windows: return Strings.T("gpu_native");
                default: return Strings.T("gpu_auto");
            }
        }

        internal void Select(PreviewEngine engine)
        {
            Preferred = engine;
            hardwareFailed = false;
            used = null;
            fallbackReason = null;
            generation++;
        }

        internal string Status()
        {
            string status = used.HasValue
                ? Strings.T("gpu_used", EngineName(used.Value), processingMilliseconds)
                : Strings.T("gpu_no_preview");
            return fallbackReason == null ? status : status + Environment.NewLine + Strings.T(fallbackReason);
        }

        internal async Task<string> ProbeAsync()
        {
            if (!File.Exists(helper)) return Strings.T("gpu_no_helper");
            byte[] report = await RunHelperAsync("probe", null, 8192, lifetime.Token);
            return report == null || report.Length == 0 ? Strings.T("gpu_probe_failed")
                : Encoding.UTF8.GetString(report).TrimEnd();
        }

        internal async Task<Bitmap> ResizeAsync(Bitmap frame)
        {
            PreviewEngine requested = Preferred;
            int configuration = generation;
            CancellationToken cancel = lifetime.Token;
            int width, height, x, y;
            Geometry.ComputeLetterbox(OutputWidth, OutputHeight, frame.Width, frame.Height, out width, out height, out x, out y);
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            x = (OutputWidth - width) / 2;
            y = (OutputHeight - height) / 2;
            Size size = new Size(width, height);
            var watch = Stopwatch.StartNew();
            Bitmap preview = null;
            string reason = null;
            if (TriesHardware)
            {
                if ((long)frame.Width * frame.Height > MaximumGpuPixels || frame.Width > 8192 || frame.Height > 8192)
                    reason = "gpu_image_limit";
                else if (!File.Exists(helper)) reason = "gpu_no_helper";
                else
                {
                    try
                    {
                        byte[] input = await Task.Run(() => Pack(frame, size));
                        if (cancel.IsCancellationRequested) return null;
                        byte[] output = await RunHelperAsync(requested == PreviewEngine.Direct3D ? "d3d11" : "vulkan",
                            input, width * height * 4, cancel);
                        if (output != null && output.Length == width * height * 4)
                            preview = await Task.Run(() => CreateThumbnail(frame, new Rectangle(x, y, width, height), output));
                        else reason = "gpu_fallback";
                    }
                    catch (ArgumentException) { reason = "gpu_fallback"; }
                    catch (ExternalException) { reason = "gpu_fallback"; }
                    catch (OutOfMemoryException) { reason = "gpu_fallback"; }
                }
                if (configuration == generation && !cancel.IsCancellationRequested && reason != null)
                {
                    fallbackReason = reason;
                    hardwareFailed = reason != "gpu_image_limit";
                }
            }

            bool hardwareUsed = preview != null;
            if (cancel.IsCancellationRequested)
            {
                if (preview != null) preview.Dispose();
                return null;
            }
            if (preview == null)
                preview = await Task.Run(() => CreateThumbnail(frame, new Rectangle(x, y, width, height), null));
            if (configuration != generation || cancel.IsCancellationRequested)
            {
                preview.Dispose();
                return null;
            }
            used = hardwareUsed ? requested : PreviewEngine.Windows;
            if (hardwareUsed) fallbackReason = null;
            processingMilliseconds = watch.Elapsed.TotalMilliseconds;
            return preview;
        }

        private static Bitmap CreateThumbnail(Bitmap frame, Rectangle region, byte[] gpuPixels)
        {
            var thumbnail = new Bitmap(OutputWidth, OutputHeight, PixelFormat.Format32bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.Clear(Color.FromArgb(10, 15, 22));
                    if (gpuPixels == null)
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        graphics.DrawImage(frame, region);
                    }
                }
                if (gpuPixels != null)
                {
                    BitmapData data = thumbnail.LockBits(new Rectangle(Point.Empty, thumbnail.Size),
                        ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
                    try
                    {
                        for (int row = 0; row < region.Height; row++)
                            Marshal.Copy(gpuPixels, row * region.Width * 4,
                                IntPtr.Add(data.Scan0, (row + region.Y) * data.Stride + region.X * 4), region.Width * 4);
                    }
                    finally { thumbnail.UnlockBits(data); }
                }
                return thumbnail;
            }
            catch
            {
                thumbnail.Dispose();
                throw;
            }
        }

        private static byte[] Pack(Bitmap frame, Size output)
        {
            var bounds = new Rectangle(Point.Empty, frame.Size);
            // PrintWindow populates a 32bpp RGB DIB. Alpha is ignored by both renderers.
            BitmapData data = frame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                int rowBytes = frame.Width * 4;
                byte[] input = new byte[16 + rowBytes * frame.Height];
                Buffer.BlockCopy(BitConverter.GetBytes(frame.Width), 0, input, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(frame.Height), 0, input, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(output.Width), 0, input, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(output.Height), 0, input, 12, 4);
                for (int row = 0; row < frame.Height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), input, 16 + rowBytes * row, rowBytes);
                return input;
            }
            finally { frame.UnlockBits(data); }
        }

        private async Task<byte[]> RunHelperAsync(string command, byte[] input, int outputLimit, CancellationToken cancel)
        {
            try
            {
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = helper, Arguments = command, UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true,
                        WorkingDirectory = Environment.SystemDirectory
                    }
                })
                {
                    if (cancel.IsCancellationRequested || !process.Start()) return null;
                    Task<byte[]> read = ReadOutputAsync(process.StandardOutput.BaseStream, outputLimit);
                    Task write = WriteInputAsync(process.StandardInput, input);
                    Task connected = Task.WhenAll(read, write);
                    using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                    {
                        try
                        {
                            if (await Task.WhenAny(connected, Task.Delay(6000, timer.Token)).ConfigureAwait(false) != connected)
                            {
                                ObserveFailure(connected);
                                return null;
                            }
                            // The exit status is checked even if the pipe closes after a partial operation.
                            await connected.ConfigureAwait(false);
                            if (!process.HasExited && !await Task.Run(() => process.WaitForExit(350)).ConfigureAwait(false)) return null;
                            return process.ExitCode == 0 ? await read.ConfigureAwait(false) : null;
                        }
                        finally
                        {
                            timer.Cancel();
                            try { if (!process.HasExited) process.Kill(); }
                            catch (InvalidOperationException) { }
                            catch (Win32Exception) { }
                        }
                    }
                }
            }
            catch (IOException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (Win32Exception) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static async Task WriteInputAsync(StreamWriter writer, byte[] input)
        {
            using (writer)
                if (input != null) await writer.BaseStream.WriteAsync(input, 0, input.Length).ConfigureAwait(false);
        }

        private static void ObserveFailure(Task operation)
        {
            operation.ContinueWith(faulted => GC.KeepAlive(faulted.Exception),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static async Task<byte[]> ReadOutputAsync(Stream stream, int limit)
        {
            var data = new byte[limit + 1];
            int count = 0;
            while (count < data.Length)
            {
                int added = await stream.ReadAsync(data, count, data.Length - count).ConfigureAwait(false);
                if (added == 0) break;
                count += added;
            }
            if (count > limit) return null;
            Array.Resize(ref data, count);
            return data;
        }

        public void Dispose()
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}
