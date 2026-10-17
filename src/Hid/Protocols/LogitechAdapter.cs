using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class LogitechAdapter : IProtocolAdapter
{
    public string Name => "Logitech HID++";
    public int Priority => 60;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x046D &&
            i.IsVendorPage &&
            (i.OutputReportLength == 20 || i.OutputReportLength == 7));
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces
            .Where(i => i.VendorId == 0x046D && i.IsVendorPage &&
                        (i.OutputReportLength == 20 || i.OutputReportLength == 7))
            .OrderByDescending(i => i.OutputReportLength)
            .FirstOrDefault();

        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            int length = iface.OutputReportLength == 20 ? 20 : 7;
            byte reportId = length == 20 ? (byte)0x11 : (byte)0x10;
            bool anyResponse = false;

            foreach (int deviceIndex in new[] { 0xFF, 1, 2, 3, 4, 5, 6 })
            {
                byte? batteryStatusIndex = FindFeature(
                    device, reportId, length, deviceIndex, 0x1000, ref anyResponse);
                if (batteryStatusIndex is not null)
                {
                    byte[]? reply = HidppCall(
                        device, reportId, length, deviceIndex,
                        batteryStatusIndex.Value, 0x00, [], 350);
                    if (reply is { Length: >= 7 })
                    {
                        int level = reply[4];
                        int status = reply[6];
                        var (charging, mappedLevel) = MapStatus(level, status);
                        if (ProtocolHelper.IsValidLevel(mappedLevel))
                        {
                            return ProtocolHelper.Success(
                                group, mappedLevel, charging,
                                "Logitech HID++ 2.0（0x1000）",
                                PeripheralKind.Mouse);
                        }
                    }
                }

                byte? unifiedIndex = FindFeature(
                    device, reportId, length, deviceIndex, 0x1004, ref anyResponse);
                if (unifiedIndex is not null)
                {
                    byte[]? reply = HidppCall(
                        device, reportId, length, deviceIndex,
                        unifiedIndex.Value, 0x00, [], 350);
                    if (reply is { Length: >= 6 })
                    {
                        int level = reply[4];
                        int status = reply[5];
                        var (charging, mappedLevel) = MapStatus(level, status);
                        if (ProtocolHelper.IsValidLevel(mappedLevel))
                        {
                            return ProtocolHelper.Success(
                                group, mappedLevel, charging,
                                "Logitech HID++ 2.0（0x1004）",
                                PeripheralKind.Mouse);
                        }
                    }
                }
            }

            if (!anyResponse)
            {
                return null;
            }

            return ProtocolHelper.Failure(
                group, "未找到电池特性（接收器可能未连接鼠标）",
                PeripheralKind.Mouse, "Logitech HID++ 2.0");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "Logitech HID++ 2.0");
        }
    }

    private static (bool? Charging, int Level) MapStatus(int level, int status)
    {
        bool? charging = status switch
        {
            0 => false,
            1 or 2 or 4 => true,
            3 => true,
            _ => null
        };

        if (level == 0)
        {
            level = status switch
            {
                1 or 2 or 4 => 80,
                3 => 100,
                _ => level
            };
        }

        return (charging, level);
    }

    private static byte? FindFeature(
        HidDevice device, byte reportId, int length, int deviceIndex,
        ushort featureId, ref bool anyResponse)
    {
        byte[]? reply = HidppCall(
            device, reportId, length, deviceIndex, 0x00, 0x00,
            [(byte)(featureId >> 8), (byte)(featureId & 0xFF)], 300);

        if (reply is null)
        {
            return null;
        }

        anyResponse = true;
        if (reply.Length > 4 && (reply[3] & 0x80) != 0)
        {
            return null;
        }

        foreach (int position in new[] { 4, 6, 5 })
        {
            if (reply.Length > position && reply[position] != 0)
            {
                return reply[position];
            }
        }

        return null;
    }

    private static byte[]? HidppCall(
        HidDevice device, byte reportId, int length, int deviceIndex,
        byte featureIndex, byte function, byte[] parameters, int timeoutMs)
    {
        byte[] request = new byte[length];
        request[0] = reportId;
        request[1] = (byte)deviceIndex;
        request[2] = featureIndex;
        request[3] = (byte)(((function & 0x0F) << 4) | 0x01);
        Array.Copy(parameters, 0, request, 4, Math.Min(parameters.Length, length - 4));

        device.Drain();
        try
        {
            device.WriteOutput(request);
        }
        catch (IOException)
        {
            return null;
        }

        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < timeoutMs)
        {
            byte[]? response = device.ReadInput(Math.Min(180, timeoutMs));
            if (response is null || response.Length < 4)
            {
                continue;
            }

            if (response[0] != reportId ||
                response[1] != (byte)deviceIndex ||
                response[2] != featureIndex ||
                ((response[3] >> 4) & 0x0F) != function)
            {
                continue;
            }

            return response;
        }

        return null;
    }
}
