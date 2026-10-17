using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BatteryTray.Bluetooth;

internal sealed class BluetoothBatteryReader
{
    private static readonly TimeSpan MaxBleIdle = TimeSpan.FromDays(7);

    public IReadOnlyList<Hid.PeripheralReading> Read()
    {
        var classicDevices = BluetoothConnectionFilter.GetClassicDevices();
        var found = new Dictionary<string, BluetoothNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (enumerator, flags) in new (string? Enumerator, uint Flags)[]
                 {
                     ("BTHENUM", BluetoothNative.DIGCF_PRESENT),
                     (null, BluetoothNative.DIGCF_PRESENT | BluetoothNative.DIGCF_ALLCLASSES)
                 })
        {
            IntPtr set = BluetoothNative.SetupDiGetClassDevsW(
                IntPtr.Zero, enumerator, IntPtr.Zero, flags);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
            {
                continue;
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    var info = new BluetoothNative.SP_DEVINFO_DATA
                    {
                        cbSize = Marshal.SizeOf<BluetoothNative.SP_DEVINFO_DATA>()
                    };
                    if (!BluetoothNative.SetupDiEnumDeviceInfo(set, index, ref info))
                    {
                        break;
                    }

                    index++;
                    string instanceId = BluetoothNative.GetInstanceId(set, ref info);
                    if (!IsBluetoothInstance(instanceId))
                    {
                        continue;
                    }

                    int? level = BluetoothNative.ReadBatteryLevel(set, ref info);
                    if (level is null || level <= 0 || level > 100)
                    {
                        continue;
                    }

                    if (!BluetoothNative.IsStarted(info.DevInst))
                    {
                        continue;
                    }

                    string originalName = BluetoothNative.GetDeviceName(set, ref info);
                    string name = CleanName(originalName);
                    bool handsFree = LooksLikeHandsFree(originalName);
                    DateTime? lastConnected = BluetoothNative.ReadLastConnectedTime(set, ref info);
                    if (!IsConnected(instanceId, name, lastConnected, classicDevices))
                    {
                        continue;
                    }

                    var node = new BluetoothNode(
                        instanceId, name, level.Value, handsFree);
                    string key = $"{node.Name}|{node.Level}";
                    if (!found.TryGetValue(key, out var existing) ||
                        (!existing.HandsFree && node.HandsFree))
                    {
                        found[key] = node;
                    }
                }
            }
            finally
            {
                BluetoothNative.SetupDiDestroyDeviceInfoList(set);
            }
        }

        var result = new List<Hid.PeripheralReading>();
        foreach (var node in found.Values
                     .OrderByDescending(n => n.HandsFree)
                     .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(new Hid.PeripheralReading
            {
                Key = $"BT:{node.InstanceId}",
                Name = node.Name,
                Kind = node.HandsFree ? Hid.PeripheralKind.Headset : InferKind(node.Name),
                Level = node.Level,
                Charging = null,
                Source = node.HandsFree ? "蓝牙 HFP 电量上报" : "蓝牙 PnP 电量属性",
                UpdatedAt = DateTime.Now
            });
        }

        return result;
    }

    private static bool IsConnected(
        string instanceId,
        string cleanedName,
        DateTime? lastConnected,
        IReadOnlyList<ClassicBluetoothDevice> classicDevices)
    {
        if (instanceId.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            var matching = classicDevices
                .Where(d => NamesEqual(d.Name, cleanedName))
                .ToList();
            if (matching.Count > 0)
            {
                return matching.Any(d => d.Connected);
            }

            // 少数 HFP 节点在经典 API 中不可见，退回到最近连接时间。
            return lastConnected is not null &&
                   DateTime.UtcNow - lastConnected.Value.ToUniversalTime() <= MaxBleIdle;
        }

        if (instanceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase))
        {
            return lastConnected is not null &&
                   DateTime.UtcNow - lastConnected.Value.ToUniversalTime() <= MaxBleIdle;
        }

        return false;
    }

    private static bool NamesEqual(string left, string right)
    {
        return string.Equals(
            NormalizeName(left), NormalizeName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name)
    {
        return new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool LooksLikeHandsFree(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("hands-free") || lower.Contains("handsfree") ||
               lower.Contains("ag audio") || lower.Contains("免提") ||
               lower.Contains("headset") || lower.Contains("耳机");
    }

    private static bool IsBluetoothInstance(string instanceId)
    {
        return instanceId.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
               instanceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase);
    }

    private static Hid.PeripheralKind InferKind(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("mouse") || lower.Contains("鼠标") ||
            lower.Contains("triathlon") || lower.Contains("mx master") ||
            lower.Contains("mx anywhere") || lower.Contains("trackball"))
        {
            return Hid.PeripheralKind.Mouse;
        }

        if (lower.Contains("keyboard") || lower.Contains("键盘"))
        {
            return Hid.PeripheralKind.Keyboard;
        }

        if (LooksLikeHandsFree(name) || lower.Contains("headphone") ||
            lower.Contains("earbud") || lower.Contains("buds") ||
            lower.Contains("airpods") || lower.Contains("耳机"))
        {
            return Hid.PeripheralKind.Headset;
        }

        return Hid.PeripheralKind.Other;
    }

    private static string CleanName(string name)
    {
        string value = name.Trim();
        string[] suffixes =
        [
            " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free Audio",
            " Hands-Free", " Handsfree", " Hands Free", " AG Audio",
            " 免提音频", " 免提 AG", " 免提", " Avrcp Transport",
            " AVRCP Transport", " A2DP", " Stereo", " 立体声"
        ];
        foreach (string suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length].Trim();
            }
        }

        value = Regex.Replace(value, @"^LE[_\s-]+", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s{2,}", " ").Trim();
        return string.IsNullOrWhiteSpace(value) ? "蓝牙设备" : value;
    }

    private sealed record BluetoothNode(
        string InstanceId, string Name, int Level, bool HandsFree);
}

internal sealed record ClassicBluetoothDevice(string Name, bool Connected);

internal static class BluetoothConnectionFilter
{
    public static IReadOnlyList<ClassicBluetoothDevice> GetClassicDevices()
    {
        var result = new List<ClassicBluetoothDevice>();
        try
        {
            var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = 1,
                fReturnRemembered = 1,
                fReturnUnknown = 0,
                fReturnConnected = 1,
                fIssueInquiry = 0,
                cTimeoutMultiplier = 0,
                hRadio = IntPtr.Zero
            };
            var info = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
            };

            IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
            if (handle == IntPtr.Zero)
            {
                return result;
            }

            try
            {
                do
                {
                    result.Add(new ClassicBluetoothDevice(
                        info.szName ?? "", info.fConnected != 0));
                    info = new BLUETOOTH_DEVICE_INFO
                    {
                        dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
                    };
                }
                while (BluetoothFindNextDevice(handle, ref info));
            }
            finally
            {
                BluetoothFindDeviceClose(handle);
            }
        }
        catch
        {
            // 蓝牙 API 不可用时按无连接设备处理。
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct BLUETOOTH_ADDRESS
    {
        [FieldOffset(0)]
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public BLUETOOTH_ADDRESS Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams,
        ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(
        IntPtr findHandle, ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr findHandle);
}

internal static class BluetoothNative
{
    internal const uint DIGCF_PRESENT = 0x02;
    internal const uint DIGCF_ALLCLASSES = 0x04;
    private const uint SPDRP_DEVICEDESC = 0x00;
    private const uint SPDRP_FRIENDLYNAME = 0x0C;
    private const uint DEVPROP_TYPE_BYTE = 0x00000003;
    private const uint DEVPROP_TYPE_UINT32 = 0x00000007;

    private static readonly Guid BluetoothBatteryFmtid =
        new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    private const uint BatteryLevelPid = 2;
    private static readonly Guid BluetoothLastConnectedFmtid =
        new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A");
    private const uint LastConnectedPid = 11;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer,
        uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref DEVPROPKEY propertyKey, out uint propertyType, byte[] propertyBuffer,
        uint propertyBufferSize, out uint requiredSize, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(
        out uint status, out uint problemNumber, uint devInst, uint flags);

    internal static bool IsStarted(uint devInst)
    {
        if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != 0)
        {
            return false;
        }

        const uint dnStarted = 0x00000008;
        const uint dnHasProblem = 0x00000400;
        return (status & dnStarted) != 0 && (status & dnHasProblem) == 0 && problem == 0;
    }

    internal static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var buffer = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceIdW(
                set, ref info, buffer, (uint)buffer.Capacity, out _))
        {
            return "";
        }

        return buffer.ToString();
    }

    internal static string GetDeviceName(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        string name = GetRegistryString(set, ref info, SPDRP_FRIENDLYNAME);
        return string.IsNullOrWhiteSpace(name)
            ? GetRegistryString(set, ref info, SPDRP_DEVICEDESC)
            : name;
    }

    internal static int? ReadBatteryLevel(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var key = new DEVPROPKEY
        {
            fmtid = BluetoothBatteryFmtid,
            pid = BatteryLevelPid
        };
        byte[] buffer = new byte[8];
        if (!SetupDiGetDevicePropertyW(
                set, ref info, ref key, out uint type, buffer,
                (uint)buffer.Length, out _, 0))
        {
            return null;
        }

        return type switch
        {
            DEVPROP_TYPE_BYTE => buffer[0],
            DEVPROP_TYPE_UINT32 => (int)BitConverter.ToUInt32(buffer, 0),
            _ => null
        };
    }

    internal static DateTime? ReadLastConnectedTime(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var key = new DEVPROPKEY
        {
            fmtid = BluetoothLastConnectedFmtid,
            pid = LastConnectedPid
        };
        byte[] buffer = new byte[8];
        if (!SetupDiGetDevicePropertyW(
                set, ref info, ref key, out _, buffer,
                (uint)buffer.Length, out _, 0))
        {
            return null;
        }

        ulong fileTime = BitConverter.ToUInt64(buffer, 0);
        if (fileTime == 0)
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTimeUtc((long)fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string GetRegistryString(
        IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        byte[] buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryPropertyW(
                set, ref info, property, out _, buffer,
                (uint)buffer.Length, out uint requiredSize))
        {
            return "";
        }

        int length = (int)Math.Min(requiredSize, (uint)buffer.Length);
        string value = Encoding.Unicode.GetString(buffer, 0, length);
        int nul = value.IndexOf('\0');
        return (nul >= 0 ? value[..nul] : value).Trim();
    }
}
