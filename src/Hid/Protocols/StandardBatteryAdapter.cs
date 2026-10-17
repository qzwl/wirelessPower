namespace BatteryTray.Hid.Protocols;

internal sealed class StandardBatteryAdapter : IProtocolAdapter
{
    private static readonly (ushort Page, ushort Usage)[] BatteryUsages =
    [
        (0x0006, 0x0020), // Battery Strength
        (0x000D, 0x003B), // Battery Strength (digitizer)
        (0x0085, 0x0001), // Absolute State of Charge
        (0x0085, 0x0066), // Relative State of Charge
        (0x0085, 0x0065)  // Battery Level
    ];

    public string Name => "标准 HID 电量";
    public int Priority => 90;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i => i.FeatureReportLength > 0);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        try
        {
            foreach (var iface in group.Interfaces.Where(i => i.FeatureReportLength > 0))
            {
                using var device = HidDevice.Open(iface.Path, write: false);
                var caps = device.GetValueCaps(HidNative.HIDP_FEATURE);
                foreach (var cap in caps)
                {
                    if (cap.IsRange ||
                        !BatteryUsages.Contains((cap.UsagePage, cap.Usage)))
                    {
                        continue;
                    }

                    byte[] report;
                    try
                    {
                        report = device.GetFeature(cap.ReportId, iface.FeatureReportLength);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    uint? raw = device.GetUsageValue(HidNative.HIDP_FEATURE, cap, report);
                    if (raw is null)
                    {
                        continue;
                    }

                    int max = cap.LogicalMax > 0 ? cap.LogicalMax : 100;
                    if (raw.Value > max)
                    {
                        continue;
                    }

                    long level = max == 100
                        ? raw.Value
                        : (long)Math.Round(raw.Value * 100.0 / max);
                    if (!ProtocolHelper.IsValidLevel((int)level))
                    {
                        continue;
                    }

                    return ProtocolHelper.Success(
                        group, (int)level, null, "标准 HID 电量字段",
                        PeripheralKind.Other);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"标准电量字段读取失败：{ex.Message}",
                PeripheralKind.Other, "标准 HID 电量字段");
        }
    }
}
