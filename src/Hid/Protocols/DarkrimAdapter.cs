using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class DarkrimAdapter : IProtocolAdapter
{
    private const ushort Vid = 0x3654;
    private const ushort UsagePage = 0xFF02;
    private const int ReportLength = 65;

    public string Name => "DARKRIM";
    public int Priority => 30;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == Vid && i.UsagePage == UsagePage &&
            i.OutputReportLength >= ReportLength);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == Vid && i.UsagePage == UsagePage &&
            i.OutputReportLength >= ReportLength);
        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            device.Drain();
            byte[] frame = BuildBatteryQuery(0);
            device.WriteOutput(frame);

            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 900)
            {
                byte[]? response = device.ReadInput(250);
                if (response is null || response.Length < 12 ||
                    response[1] != 0xFE)
                {
                    continue;
                }

                int length = response[8] | (response[9] << 8);
                if (length < 0 || 10 + length + 2 > response.Length)
                {
                    continue;
                }

                ushort expectedCrc = (ushort)((response[10 + length] << 8) |
                                             response[11 + length]);
                ushort actualCrc = Crc16(response, 1, 10 + length);
                if (expectedCrc != actualCrc)
                {
                    continue;
                }

                int offset = 10;
                int end = 10 + length;
                while (offset + 2 <= end)
                {
                    byte type = response[offset];
                    int valueLength = response[offset + 1];
                    if (offset + 2 + valueLength > end)
                    {
                        break;
                    }

                    if (type == 0x01 && valueLength >= 1)
                    {
                        int level = response[offset + 2];
                        if (ProtocolHelper.IsValidLevel(level))
                        {
                            return ProtocolHelper.Success(
                                group, level, null, "DARKRIM TLV 协议",
                                PeripheralKind.Headset);
                        }
                    }

                    offset += 2 + valueLength;
                }
            }

            return ProtocolHelper.Failure(
                group, "耳机未响应（确认耳机已开机并连接）",
                PeripheralKind.Headset, "DARKRIM TLV 协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Headset, "DARKRIM TLV 协议");
        }
    }

    private static byte[] BuildBatteryQuery(byte sequence)
    {
        byte[] payload = [0x01, 0x00];
        byte[] frame = new byte[ReportLength];
        frame[1] = 0xFE;
        frame[2] = sequence;
        frame[3] = 0x10;
        frame[5] = 0x02;
        frame[8] = (byte)payload.Length;
        frame[9] = 0x00;
        payload.CopyTo(frame, 10);
        ushort crc = Crc16(frame, 1, 10 + payload.Length);
        frame[10 + payload.Length] = (byte)(crc >> 8);
        frame[11 + payload.Length] = (byte)(crc & 0xFF);
        return frame;
    }

    private static ushort Crc16(byte[] data, int offset, int count)
    {
        ushort crc = 0xFFFF;
        for (int i = offset; i < offset + count; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}
