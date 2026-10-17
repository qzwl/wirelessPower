namespace BatteryTray.Hid.Protocols;

internal sealed class EndgameAdapter : IProtocolAdapter
{
    public string Name => "Endgame Gear";
    public int Priority => 50;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x3367 && i.UsagePage == 0xFF01 &&
            i.FeatureReportLength >= 64);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x3367 && i.UsagePage == 0xFF01 &&
            i.FeatureReportLength >= 64);
        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                byte[] request = new byte[iface.FeatureReportLength];
                request[0] = 0xA1;
                request[1] = 0xB4;
                device.SetFeature(request);
                Thread.Sleep(320);

                byte[] response = device.GetFeature(0xA1, iface.FeatureReportLength);
                if (response.Length < 18 || response[1] != 0x01)
                {
                    continue;
                }

                int level = response[16];
                if (ProtocolHelper.IsValidLevel(level))
                {
                    return ProtocolHelper.Success(
                        group, level, null, "Endgame Gear 私有协议",
                        PeripheralKind.Mouse);
                }
            }

            return ProtocolHelper.Failure(
                group, "设备未响应（唤醒鼠标后重试）",
                PeripheralKind.Mouse, "Endgame Gear 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "Endgame Gear 私有协议");
        }
    }
}

internal sealed class VaxeeAdapter : IProtocolAdapter
{
    public string Name => "VAXEE / ZYGEN";
    public int Priority => 51;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x3057 && i.UsagePage == 0xFF05 &&
            i.FeatureReportLength >= 64);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x3057 && i.UsagePage == 0xFF05 &&
            i.FeatureReportLength >= 64);
        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            byte[] request = new byte[iface.FeatureReportLength];
            request[0] = 0x0E;
            request[1] = 0xA5;
            request[2] = 0x0B;
            request[3] = 0x01;
            request[4] = 0x01;
            device.SetFeature(request);
            Thread.Sleep(140);

            byte[] response = device.GetFeature(0x0E, iface.FeatureReportLength);
            if (response.Length > 5)
            {
                int raw = response[5];
                if (raw is >= 0 and <= 20)
                {
                    return ProtocolHelper.Success(
                        group, raw * 5, null, "VAXEE / ZYGEN 私有协议",
                        PeripheralKind.Mouse);
                }
            }

            return ProtocolHelper.Failure(
                group, "设备未响应", PeripheralKind.Mouse,
                "VAXEE / ZYGEN 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "VAXEE / ZYGEN 私有协议");
        }
    }
}

internal sealed class NordicAdapter : IProtocolAdapter
{
    public string Name => "Nordic";
    public int Priority => 52;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x1915 && i.UsagePage == 0xFFA0 &&
            i.FeatureReportLength >= 32);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x1915 && i.UsagePage == 0xFFA0 &&
            i.FeatureReportLength >= 32);
        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            byte[] request = new byte[iface.FeatureReportLength];
            request[0] = 0x05;
            request[1] = 0x15;
            request[2] = 0x00;
            request[3] = 0x00;
            request[4] = 0x01;
            device.SetFeature(request);
            Thread.Sleep(120);

            byte[] response = device.GetFeature(0x05, iface.FeatureReportLength);
            if (response.Length > 9)
            {
                int level = response[9];
                if (ProtocolHelper.IsValidLevel(level))
                {
                    return ProtocolHelper.Success(
                        group, level, null, "Nordic nRF 私有协议",
                        PeripheralKind.Mouse);
                }
            }

            return ProtocolHelper.Failure(
                group, "设备未响应", PeripheralKind.Mouse, "Nordic nRF 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "Nordic nRF 私有协议");
        }
    }
}
