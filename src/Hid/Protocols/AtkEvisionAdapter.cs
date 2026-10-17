using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class AtkEvisionAdapter : IProtocolAdapter
{
    public string Name => "ATK / VXE EVISION";
    public int Priority => 10;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.UsagePage == 0xFF1C &&
            i.Usage == 0x0092 &&
            i.OutputReportLength >= 9);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.UsagePage == 0xFF1C &&
            i.Usage == 0x0092 &&
            i.OutputReportLength >= 9);
        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            device.Drain();

            foreach (int reportId in new[] { 4, 5 })
            {
                if (iface.OutputReportLength < 9)
                {
                    continue;
                }

                byte[] request = new byte[iface.OutputReportLength];
                request[0] = (byte)reportId;
                request[1] = 0x01; // request code, little endian
                request[2] = 0x00;
                request[3] = 0x1A; // power info

                try
                {
                    device.WriteOutput(request, allowHidDeeFallback: false);
                }
                catch (IOException)
                {
                    continue;
                }

                var timeout = Stopwatch.StartNew();
                while (timeout.ElapsedMilliseconds < 900)
                {
                    byte[]? response = device.ReadInput(250);
                    if (response is null || response.Length < 9)
                    {
                        continue;
                    }

                    if (response[0] != reportId || response[3] != 0x1A)
                    {
                        continue;
                    }

                    int level = response[8];
                    if (!ProtocolHelper.IsValidLevel(level))
                    {
                        continue;
                    }

                    bool? charging = response.Length > 9 ? response[9] != 0 : null;
                    return ProtocolHelper.Success(
                        group, level, charging,
                        "ATK / VXE EVISION 私有协议", PeripheralKind.Keyboard);
                }
            }

            return ProtocolHelper.Failure(
                group, "键盘未响应（按任意键唤醒后重试）",
                PeripheralKind.Keyboard, "ATK / VXE EVISION 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Keyboard, "ATK / VXE EVISION 私有协议");
        }
    }
}
