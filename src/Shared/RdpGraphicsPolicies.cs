using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RayXDesktop.Configuration
{
    internal enum PolicyChoice { Keep, Default, Off, On }

    internal static class RdpGraphicsPolicies
    {
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
        private const uint LoadRegistry = 1, ReadOnly = 2, MachineSection = 2;
        private static readonly Guid GroupPolicyObject = new Guid("EA502722-A23D-11D1-A7D3-0000F87571E3");
        private static readonly string[] ValueNames = { "bEnumerateHWBeforeSW", "AVCHardwareEncodePreferred", "AVC444ModePreferred" };
        internal const int Count = 3;

        internal static PolicyChoice[] ReadEffective()
        {
            RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (var key = machine.OpenSubKey(PolicyPath))
                return ReadValues(key);
        }

        internal static PolicyChoice[] ReadLocal()
        {
            var policy = OpenLocal(true);
            try
            {
                using (var root = OpenMachineSection(policy))
                using (var key = root.OpenSubKey(PolicyPath))
                    return ReadValues(key);
            }
            finally { Marshal.ReleaseComObject(policy); }
        }

        private static PolicyChoice[] ReadValues(RegistryKey key)
        {
            var result = new PolicyChoice[Count];
            for (int i = 0; i < Count; i++)
            {
                object value = key == null ? null : key.GetValue(ValueNames[i]);
                result[i] = !(value is int) ? PolicyChoice.Default : (int)value == 1 ? PolicyChoice.On : PolicyChoice.Off;
            }
            return result;
        }

        internal static bool TryParse(string[] args, out PolicyChoice[] changes)
        {
            changes = null;
            if (args.Length != Count + 2 || args[0] != "--set-rdp-graphics"
                || (args[1] != "fr" && args[1] != "en")) return false;
            var values = new PolicyChoice[Count];
            bool changed = false;
            for (int i = 0; i < Count; i++)
            {
                switch (args[i + 2])
                {
                    case "keep": values[i] = PolicyChoice.Keep; break;
                    case "default": values[i] = PolicyChoice.Default; break;
                    case "off": values[i] = PolicyChoice.Off; break;
                    case "on": values[i] = PolicyChoice.On; break;
                    default: return false;
                }
                changed |= values[i] != PolicyChoice.Keep;
            }
            if (changed) changes = values;
            return changed;
        }

        internal static string Argument(PolicyChoice choice)
        {
            switch (choice)
            {
                case PolicyChoice.On: return "on";
                case PolicyChoice.Off: return "off";
                case PolicyChoice.Default: return "default";
                default: return "keep";
            }
        }

        internal static bool SaveLocal(PolicyChoice[] changes)
        {
            var policy = OpenLocal(false);
            try
            {
                using (var root = OpenMachineSection(policy))
                using (var key = root.CreateSubKey(PolicyPath))
                {
                    for (int i = 0; i < Count; i++)
                    {
                        switch (changes[i])
                        {
                            case PolicyChoice.On: key.SetValue(ValueNames[i], 1, RegistryValueKind.DWord); break;
                            case PolicyChoice.Off: key.SetValue(ValueNames[i], 0, RegistryValueKind.DWord); break;
                            case PolicyChoice.Default: key.DeleteValue(ValueNames[i], false); break;
                        }
                    }
                }
                var registryExtension = new Guid("35378EAC-683F-11D2-A89A-00C04FBBCFA2");
                var editor = new Guid("8FC0B734-A0E1-11D1-A7D3-0000F87571E3");
                policy.Save(true, true, ref registryExtension, ref editor);
            }
            finally { Marshal.ReleaseComObject(policy); }
            // Ask Windows to reprocess computer policy in its normal order, including any domain policy.
            return RefreshPolicy(true);
        }

        private static IGroupPolicyObject OpenLocal(bool forRead)
        {
            var policy = (IGroupPolicyObject)Activator.CreateInstance(Type.GetTypeFromCLSID(GroupPolicyObject, true));
            try
            {
                policy.OpenLocalMachineGPO(LoadRegistry | (forRead ? ReadOnly : 0));
                return policy;
            }
            catch
            {
                Marshal.ReleaseComObject(policy);
                throw;
            }
        }

        private static RegistryKey OpenMachineSection(IGroupPolicyObject policy)
        {
            IntPtr handle;
            policy.GetRegistryKey(MachineSection, out handle);
            return RegistryKey.FromHandle(new SafeRegistryHandle(handle, true));
        }

        [DllImport("userenv.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RefreshPolicy([MarshalAs(UnmanagedType.Bool)] bool machine);

        // Methods before GetRegistryKey are declared in vtable order as defined by gpedit.h.
        [ComImport, Guid("EA502723-A23D-11D1-A7D3-0000F87571E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGroupPolicyObject
        {
            void New([MarshalAs(UnmanagedType.LPWStr)] string domain, [MarshalAs(UnmanagedType.LPWStr)] string displayName, uint flags);
            void OpenDSGPO([MarshalAs(UnmanagedType.LPWStr)] string path, uint flags);
            void OpenLocalMachineGPO(uint flags);
            void OpenRemoteMachineGPO([MarshalAs(UnmanagedType.LPWStr)] string machine, uint flags);
            void Save([MarshalAs(UnmanagedType.Bool)] bool machine, [MarshalAs(UnmanagedType.Bool)] bool add, ref Guid extension, ref Guid snapIn);
            void Delete();
            void GetName(IntPtr name, int characters);
            void GetDisplayName(IntPtr name, int characters);
            void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetPath(IntPtr path, int characters);
            void GetDSPath(uint section, IntPtr path, int characters);
            void GetFileSysPath(uint section, IntPtr path, int characters);
            void GetRegistryKey(uint section, out IntPtr key);
        }
    }
}
