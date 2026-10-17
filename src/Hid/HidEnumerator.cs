using System.Text;

namespace BatteryTray.Hid;

internal static class HidEnumerator
{
    public static IReadOnlyList<HidInterface> Enumerate()
    {
        var result = new List<HidInterface>();
        Guid hidGuid = HidNative.HidInterfaceGuid;
        IntPtr set = HidNative.SetupDiGetClassDevsW(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);

        if (HidDevice.IsInvalid(set))
        {
            throw new IOException($"枚举 HID 设备失败（错误 {HidNative.LastError}）");
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var data = new HidNative.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>()
                };

                if (!HidNative.SetupDiEnumDeviceInterfaces(
                        set, IntPtr.Zero, ref hidGuid, index, ref data))
                {
                    break;
                }
                index++;

                int required = 0;
                HidNative.SetupDiGetDeviceInterfaceDetailW(
                    set, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                if (required <= 0)
                {
                    continue;
                }

                IntPtr detail = System.Runtime.InteropServices.Marshal.AllocHGlobal(required);
                try
                {
                    System.Runtime.InteropServices.Marshal.WriteInt32(
                        detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!HidNative.SetupDiGetDeviceInterfaceDetailW(
                            set, ref data, detail, required, ref required, IntPtr.Zero))
                    {
                        continue;
                    }

                    string path = System.Runtime.InteropServices.Marshal.PtrToStringUni(
                        IntPtr.Add(detail, 4)) ?? "";
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    HidInterface item = Probe(path);
                    if (result.All(x => !string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(item);
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static HidInterface Probe(string path)
    {
        var (vid, pid) = ParseVidPid(path);
        string group = MakeGroupKey(path, vid, pid);
        IntPtr handle = HidNative.CreateFileW(
            path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);

        if (HidDevice.IsInvalid(handle))
        {
            return new HidInterface(path, vid, pid, 0, "", "", "", null, group,
                $"无法打开（错误 {HidNative.LastError}）");
        }

        try
        {
            var attributes = new HidNative.HIDD_ATTRIBUTES
            {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>()
            };
            if (HidNative.HidD_GetAttributes(handle, ref attributes))
            {
                vid = attributes.VendorID;
                pid = attributes.ProductID;
                group = MakeGroupKey(path, vid, pid);
            }

            string product = HidDevice.ReadWideString(HidNative.HidD_GetProductString, handle);
            string manufacturer = HidDevice.ReadWideString(HidNative.HidD_GetManufacturerString, handle);
            string serial = HidDevice.ReadWideString(HidNative.HidD_GetSerialNumberString, handle);
            HidCaps? caps = null;

            if (HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed) &&
                preparsed != IntPtr.Zero)
            {
                try
                {
                    IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(128);
                    try
                    {
                        System.Runtime.InteropServices.Marshal.Copy(new byte[128], 0, buffer, 128);
                        int status = HidNative.HidP_GetCaps(preparsed, buffer);
                        if (status == HidNative.HIDP_STATUS_SUCCESS)
                        {
                            caps = new HidCaps(
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 2),
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 0),
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 4),
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 6),
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 8),
                                (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 60));
                        }
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    HidNative.HidD_FreePreparsedData(preparsed);
                }
            }

            return new HidInterface(
                path, vid, pid, attributes.VersionNumber,
                product, manufacturer, serial, caps, group, null);
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }

    private static (ushort Vid, ushort Pid) ParseVidPid(string path)
    {
        ushort vid = 0;
        ushort pid = 0;
        string lower = path.ToLowerInvariant();
        int vidIndex = lower.IndexOf("vid_", StringComparison.Ordinal);
        if (vidIndex >= 0 && vidIndex + 8 <= lower.Length)
        {
            ushort.TryParse(lower.AsSpan(vidIndex + 4, 4),
                System.Globalization.NumberStyles.HexNumber, null, out vid);
        }
        int pidIndex = lower.IndexOf("pid_", StringComparison.Ordinal);
        if (pidIndex >= 0 && pidIndex + 8 <= lower.Length)
        {
            ushort.TryParse(lower.AsSpan(pidIndex + 4, 4),
                System.Globalization.NumberStyles.HexNumber, null, out pid);
        }
        return (vid, pid);
    }

    private static string MakeGroupKey(string path, ushort vid, ushort pid)
    {
        string[] parts = path.Split('#');
        string instance = parts.Length > 2 ? parts[2] : path;
        string[] segments = instance.Split('&');
        if (segments.Length >= 3)
        {
            instance = string.Join("&", segments.Take(segments.Length - 2));
        }

        return $"{vid:X4}:{pid:X4}:{instance}";
    }
}
