using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BatteryTray.Hid;

namespace BatteryTray.Bluetooth;

/// <summary>蓝牙扫描结果：在线设备读数 + 本轮所有枚举到的蓝牙节点键。</summary>
internal sealed record BluetoothScanResult(
    IReadOnlyList<Hid.PeripheralReading> Online,
    IReadOnlySet<string> PresentKeys);

internal sealed class BluetoothBatteryReader
{
    public BluetoothScanResult Read()
    {
        var classicDevices = BluetoothConnectionFilter.GetClassicDevices();
        // 用 Windows 官方 WinRT 设备枚举的 IsConnected 属性判定 BLE 当前连接状态，
        // 比 setupapi 的 started/电量缓存/HID 接口存在性可靠。
        var bleConnected = GetBleConnectionStates();
        var found = new Dictionary<string, BluetoothNode>(StringComparer.OrdinalIgnoreCase);
        var presentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                    presentKeys.Add($"BT:{instanceId}");

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
                    if (!IsConnected(instanceId, name, lastConnected, classicDevices, bleConnected))
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

        return new BluetoothScanResult(result, presentKeys);
    }

    private static bool IsConnected(
        string instanceId,
        string cleanedName,
        DateTime? lastConnected,
        IReadOnlyList<ClassicBluetoothDevice> classicDevices,
        IReadOnlyDictionary<string, bool> bleConnected)
    {
        // 经典蓝牙（HFP / 耳机）以系统当前连接状态为准，断开即离线。
        if (instanceId.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            var matching = classicDevices
                .Where(d => NamesEqual(d.Name, cleanedName))
                .ToList();
            return matching.Count > 0 && matching.Any(d => d.Connected);
        }

        // BLE：读取 WinRT 设备枚举里的 IsConnected 属性（官方权威连接状态）。
        if (instanceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase))
        {
            string addr = instanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)
                ? instanceId.Substring("BTHLE\\DEV_".Length, 12)
                : instanceId;
            if (bleConnected.TryGetValue(addr, out bool connected))
            {
                return connected;
            }

            // 枚举里没有该设备记录（未配对/未入缓存）：不误杀，退回“节点在”即视为在线。
            return true;
        }

        return false;
    }

    /// <summary>枚举 BLE 设备，读取官方 IsConnected 属性，映射 蓝牙地址 → 是否已连接。</summary>
    private static Dictionary<string, bool> GetBleConnectionStates()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var selector = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelector();
            var props = new[] { "System.Devices.Aep.IsConnected" };
            var task = Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync(selector, props)
                .AsTask();
            task.Wait();
            foreach (var device in task.Result)
            {
                string id = device.Id ?? "";
                int dash = id.LastIndexOf('-');
                if (dash < 0 || dash + 1 >= id.Length)
                {
                    continue;
                }

                string remote = id.Substring(dash + 1).Replace(":", "").Trim();
                if (remote.Length != 12)
                {
                    continue;
                }

                bool connected = device.Properties.TryGetValue(
                        "System.Devices.Aep.IsConnected", out object? value) &&
                    value is bool b && b;
                map[remote] = connected;
            }
        }
        catch
        {
            // WinRT 调用失败时退回空表（不误杀）。
        }

        return map;
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
        return Hid.KindGuesser.FromName(name);
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
    internal const uint SPDRP_SYMBOLIC_LINK = 0x08;
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
    internal static extern bool SetupDiGetDeviceRegistryPropertyW(
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
            // 该属性在系统上以本地钟面的 FILETIME 保存；按本地解释，
            // 与 DateTime.Now 比较才是同一时区。
            return DateTime.FromFileTime((long)fileTime);
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
