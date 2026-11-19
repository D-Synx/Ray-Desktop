using System;
using System.Runtime.InteropServices;

namespace ChildSessionDesktop
{
    // Use the folder's native Paste command for virtual files offered by the RDP clipboard.
    // The Windows shell owns network reads, duplicate prompts and copy progress in that case.
    internal static class ShellClipboard
    {
        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern void SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint resultAttributes);

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHGetDesktopFolder(out IShellFolder folder);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            void ParseDisplayName();
            void EnumObjects();
            void BindToObject(IntPtr item, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
            void BindToStorage();
            void CompareIDs();
            void CreateViewObject(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IContextMenu menu);
        }

        [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr menu, uint position, uint firstId, uint lastId, uint flags);
            void InvokeCommand(ref CommandInfo command);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CommandInfo
        {
            internal int Size;
            internal uint Mask;
            internal IntPtr Window;
            internal IntPtr Verb;
            internal IntPtr Parameters;
            internal IntPtr Directory;
            internal int Show;
            internal uint HotKey;
            internal IntPtr Icon;
        }

        internal static void PasteInto(IntPtr owner, string destination)
        {
            IntPtr id = IntPtr.Zero, popup = IntPtr.Zero, verb = IntPtr.Zero;
            IShellFolder desktop = null, folder = null;
            IContextMenu context = null;
            try
            {
                uint unused;
                SHParseDisplayName(destination, IntPtr.Zero, out id, 0, out unused);
                SHGetDesktopFolder(out desktop);
                Guid folderInterface = typeof(IShellFolder).GUID;
                desktop.BindToObject(id, IntPtr.Zero, ref folderInterface, out folder);
                Guid menuInterface = typeof(IContextMenu).GUID;
                folder.CreateViewObject(owner, ref menuInterface, out context);
                popup = CreatePopupMenu();
                if (popup == IntPtr.Zero) throw new InvalidOperationException(Strings.T("files_shell_unavailable"));
                Marshal.ThrowExceptionForHR(context.QueryContextMenu(popup, 0, 1, 0x7fff, 0));
                verb = Marshal.StringToHGlobalAnsi("paste");
                var command = new CommandInfo { Size = Marshal.SizeOf(typeof(CommandInfo)), Window = owner, Verb = verb, Show = 1 };
                context.InvokeCommand(ref command);
            }
            finally
            {
                if (verb != IntPtr.Zero) Marshal.FreeHGlobal(verb);
                if (popup != IntPtr.Zero) DestroyMenu(popup);
                if (context != null) Marshal.ReleaseComObject(context);
                if (folder != null) Marshal.ReleaseComObject(folder);
                if (desktop != null) Marshal.ReleaseComObject(desktop);
                if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id);
            }
        }
    }
}
