using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class AtkVendorAdapter : IProtocolAdapter
{
    private static readonly HashSet<ushort> VendorIds = [0x373B, 0x3554, 0x3710];

    public string Name => "ATK / VXE 鼠标";
    public int Priority => 40;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            VendorIds.Contains(i.VendorId) &&
            (i.UsagePage == 0xFF05 || i.UsagePage == 0xFF02) &&
            i.OutputReportLength >= 17);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        try
        {
            var fast = group.Interfaces.FirstOrDefault(i =>
                VendorIds.Contains(i.VendorId) && i.UsagePage == 0xFF05 &&
                i.OutputReportLength >= 64);
            if (fast is not null)
            {
                int? level = QueryFast(fast, out bool? charging);
                if (level is not null)
                {
                    return ProtocolHelper.Success(
                        group, level.Value, charging,
                        "ATK / VXE 私有协议（协议 2）", PeripheralKind.Mouse);
                }
            }

            var legacy = group.Interfaces.FirstOrDefault(i =>
                VendorIds.Contains(i.VendorId) && i.UsagePage == 0xFF02 &&
                i.OutputReportLength >= 17);
            if (legacy is not null)
            {
                int? level = QueryLegacy(legacy);
                if (level is not null)
                {
                    return ProtocolHelper.Success(
                        group, level.Value, null,
                        "ATK / VXE 私有协议（协议 1）", PeripheralKind.Mouse);
                }
            }

            return ProtocolHelper.Failure(
                group, "设备未响应（动一下鼠标后重试）",
                PeripheralKind.Mouse, "ATK / VXE 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "ATK / VXE 私有协议");
        }
    }

    private static int? QueryFast(HidInterface iface, out bool? charging)
    {
        charging = null;
        using var device = HidDevice.Open(iface.Path, write: true);
        device.Drain();

        byte[] request = new byte[Math.Max(iface.OutputReportLength, 64)];
        request[0] = 0x08;
        request[1] = 0x7D;
        request[2] = 0x72;
        request[3] = 0x02;
        request[4] = 0x00;
        request[5] = 0x01;
        request[6] = 0x07;
        request[7] = 0x01;

        device.WriteOutput(request, allowHidDeeFallback: false);
        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < 900)
        {
            byte[]? response = device.ReadInput(250);
            if (response is null || response.Length < 8)
            {
                continue;
            }

            if (response[1] != 0x72 || response[5] != 0x07)
            {
                continue;
            }

            int level = response[7];
            if (ProtocolHelper.IsValidLevel(level))
            {
                charging = null;
                return level;
            }
        }

        return null;
    }

    private static int? QueryLegacy(HidInterface iface)
    {
        using var device = HidDevice.Open(iface.Path, write: true);
        device.Drain();

        byte[] request = new byte[Math.Max(iface.OutputReportLength, 17)];
        request[0] = 0x08;
        request[1] = 0x04;
        request[^1] = 0x49;

        device.WriteOutput(request, allowHidDeeFallback: false);
        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < 900)
        {
            byte[]? response = device.ReadInput(250);
            if (response is null || response.Length < 7 || response[0] != 0x08)
            {
                continue;
            }

            int level = response[6];
            if (ProtocolHelper.IsValidLevel(level))
            {
                return level;
            }
        }

        return null;
    }
}
