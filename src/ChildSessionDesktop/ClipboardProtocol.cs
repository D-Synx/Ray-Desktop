using System;
using System.IO;

namespace ChildSessionDesktop
{
    // Binary format shared by the host and child clipboard agents.
    internal static class ClipboardProtocol
    {
        internal const int Magic = 0x31465344;
        internal const int Version = 2;
        internal const int MaxPaths = 4096;

        internal static bool Parse(BinaryReader reader, out Guid messageId, out string[] paths, out string text)
        {
            messageId = Guid.Empty;
            paths = new string[0];
            text = null;
            if (reader.ReadInt32() != Magic) return false;
            if (reader.ReadInt32() != Version) return false;
            byte[] idBytes = reader.ReadBytes(16);
            if (idBytes.Length != 16) return false;
            messageId = new Guid(idBytes);
            text = reader.ReadString();
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxPaths) return false;
            paths = new string[count];
            for (int i = 0; i < count; i++) paths[i] = reader.ReadString();
            return true;
        }
    }
}
