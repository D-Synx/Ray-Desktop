using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ChildSessionDesktop
{
    internal sealed class TransferResult
    {
        internal int Copied;
        internal int Skipped;
        internal readonly List<string> Failures = new List<string>();
    }

    internal static class FileTransfer
    {
        internal static void Copy(IEnumerable<string> sources, string destination, bool keepBoth,
            CancellationToken cancellation, Action<string> report, TransferResult result)
        {
            destination = Path.GetFullPath(destination);
            RejectReparsePath(destination);
            foreach (string path in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    string source = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string name = Path.GetFileName(source);
                    if (string.IsNullOrEmpty(name) || source == Path.GetPathRoot(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        throw new IOException(Strings.T("files_invalid_source"));
                    RejectReparsePath(source);
                    string target = Path.Combine(destination, name);
                    if (Exists(target))
                    {
                        if (!keepBoth)
                        {
                            result.Skipped++;
                            continue;
                        }
                        target = UniqueTarget(destination, name, Directory.Exists(source));
                    }
                    if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new IOException(Strings.T("files_recursive"));
                    CopyEntry(source, target, cancellation, report);
                    result.Copied++;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                        || ex is NotSupportedException || ex is System.Security.SecurityException)) throw;
                    result.Failures.Add(path + " — " + ex.Message);
                }
            }
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static string UniqueTarget(string folder, string name, bool isFolder)
        {
            string extension = isFolder ? "" : Path.GetExtension(name);
            string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
            for (int index = 2; index <= 10000; index++)
            {
                string candidate = Path.Combine(folder, stem + " (" + index + ")" + extension);
                if (!Exists(candidate)) return candidate;
            }
            throw new IOException(Strings.T("files_duplicate_limit"));
        }

        private static void RejectReparse(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Strings.T("files_reparse"));
        }

        private static void RejectReparsePath(string path)
        {
            string root = Path.GetPathRoot(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (string entry = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                !string.Equals(entry, root, StringComparison.OrdinalIgnoreCase);
                entry = Path.GetDirectoryName(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                RejectReparse(entry);
        }

        private static void CopyEntry(string source, string target, CancellationToken cancellation, Action<string> report)
        {
            cancellation.ThrowIfCancellationRequested();
            report(Path.GetFileName(source));
            // Stage each top-level selection before publishing its final name. Move never overwrites.
            string stage = Path.Combine(Path.GetDirectoryName(target), ".rayx-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool stagedDirectory = Directory.Exists(source);
            try
            {
                if (stagedDirectory)
                {
                    Directory.CreateDirectory(stage);
                    CopyDirectory(source, stage, cancellation, report);
                    cancellation.ThrowIfCancellationRequested();
                    Directory.Move(stage, target);
                }
                else
                {
                    CopyFile(source, stage, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(stage, target);
                }
            }
            finally
            {
                if (stagedDirectory && Directory.Exists(stage))
                {
                    foreach (string entry in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                        File.SetAttributes(entry, File.GetAttributes(entry) & ~FileAttributes.ReadOnly);
                    Directory.Delete(stage, true);
                }
                else if (File.Exists(stage))
                {
                    File.SetAttributes(stage, File.GetAttributes(stage) & ~FileAttributes.ReadOnly);
                    File.Delete(stage);
                }
            }
        }

        private static void CopyDirectory(string source, string target, CancellationToken cancellation, Action<string> report)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                cancellation.ThrowIfCancellationRequested();
                RejectReparse(entry);
                report(Path.GetFileName(entry));
                string destination = Path.Combine(target, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    CopyDirectory(entry, destination, cancellation, report);
                }
                else CopyFile(entry, destination, cancellation);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint CopyProgress(long total, long transferred, long streamSize, long streamTransferred,
            uint streamIndex, uint reason, IntPtr source, IntPtr destination, IntPtr context);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CopyFileEx(string source, string target, CopyProgress progress,
            IntPtr context, IntPtr cancel, uint flags);

        private static void CopyFile(string source, string target, CancellationToken cancellation)
        {
            const uint FailIfExists = 1;
            const uint Continue = 0, Cancel = 1;
            CopyProgress progress = delegate { return cancellation.IsCancellationRequested ? Cancel : Continue; };
            if (!CopyFileEx(source, target, progress, IntPtr.Zero, IntPtr.Zero, FailIfExists))
            {
                int error = Marshal.GetLastWin32Error();
                cancellation.ThrowIfCancellationRequested();
                throw new IOException(new Win32Exception(error).Message);
            }
        }
    }
}
