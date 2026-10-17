using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class RapooAdapter : IProtocolAdapter
{
    public string Name => "Rapoo";
    public int Priority => 20;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x24AE &&
            i.UsagePage == 0xFF00 &&
            i.Usage == 0x0002 &&
            i.InputReportLength >= 9) &&
            group.Interfaces.Any(i =>
                i.VendorId == 0x24AE &&
                i.UsagePage == 0xFF00 &&
                i.Usage == 0x000E &&
                i.OutputReportLength >= 9);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var statusIface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x24AE && i.UsagePage == 0xFF00 &&
            i.Usage == 0x0002 && i.InputReportLength >= 9);
        var controlIface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x24AE && i.UsagePage == 0xFF00 &&
            i.Usage == 0x000E && i.OutputReportLength >= 9);

        if (statusIface is null || controlIface is null)
        {
            return null;
        }

        try
        {
            HidDevice status;
            try
            {
                status = HidDevice.Open(statusIface.Path, write: true);
            }
            catch (IOException)
            {
                status = HidDevice.Open(statusIface.Path, write: false);
            }

            using (status)
            using (var control = HidDevice.Open(controlIface.Path, write: true))
            {
                // 24AE:146D 上，向报告 7 写入 0x40/0x20 请求会立刻回一帧状态广播。
                // 某些固件上 WriteFile 返回失败但设备仍收到请求，所以这里忽略写入异常。
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    foreach (byte header in new byte[] { 0x40, 0x20 })
                    {
                        if (status.CanWrite)
                        {
                            try
                            {
                                byte[] request = new byte[statusIface.OutputReportLength];
                                request[0] = 0x07;
                                request[1] = header;
                                status.WriteOutput(request, allowHidDeeFallback: false);
                            }
                            catch (IOException)
                            {
                                // 设备可能已经收到请求；继续读状态。
                            }
                        }

                        if (ReadStatus(status, out int level))
                        {
                            return ProtocolHelper.Success(
                                group, level, null, "Rapoo 状态广播", PeripheralKind.Mouse);
                        }
                    }

                    // 兼容只响应 A5A3 握手的旧固件：先握手，再新开读句柄。
                    byte[] unlock = new byte[controlIface.OutputReportLength];
                    unlock[0] = 0x06;
                    unlock[1] = 0xA5;
                    unlock[2] = 0xA3;
                    try
                    {
                        control.WriteOutput(unlock);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    using var freshStatus = HidDevice.Open(statusIface.Path, write: false);
                    if (ReadStatus(freshStatus, out int legacyLevel))
                    {
                        return ProtocolHelper.Success(
                            group, legacyLevel, null, "Rapoo 状态广播", PeripheralKind.Mouse);
                    }
                }

                return ProtocolHelper.Failure(
                    group, "未收到状态包（移动鼠标后重试）",
                    PeripheralKind.Mouse, "Rapoo 状态广播");
            }
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group, $"读取失败：{ex.Message}",
                PeripheralKind.Mouse, "Rapoo 状态广播");
        }
    }

    private static bool ReadStatus(HidDevice status, out int level)
    {
        level = 0;
        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < 900)
        {
            byte[]? response = status.ReadInput(250);
            if (response is null || response.Length < 9 || response[0] != 0x07)
            {
                continue;
            }

            // 0x20 = 文档中的状态包；0x40 = 本机 24AE:146D 的状态包。
            if (response[1] != 0x20 && response[1] != 0x40)
            {
                continue;
            }

            int value = response[8];
            if (!ProtocolHelper.IsValidLevel(value))
            {
                continue;
            }

            level = value;
            return true;
        }

        return false;
    }
}
