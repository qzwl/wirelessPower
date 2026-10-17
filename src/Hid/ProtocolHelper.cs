using System.Text.RegularExpressions;

namespace BatteryTray.Hid;

internal sealed class DeviceGroup
{
    public required string Key { get; init; }
    public required IReadOnlyList<HidInterface> Interfaces { get; init; }

    public string VidPid => Interfaces.Count > 0 ? Interfaces[0].VidPid : "0000:0000";

    public string FriendlyName
    {
        get
        {
            string[] generic = ["HID 设备", "HID-compliant", "USB Composite Device", "HIDI2C Device"];
            var candidates = Interfaces
                .Select(i => i.Product.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Where(s => !generic.Any(g => s.Contains(g, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(s => s.Length)
                .ToList();

            string name = candidates.FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                string manufacturer = Interfaces
                    .Select(i => i.Manufacturer.Trim())
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
                name = string.IsNullOrWhiteSpace(manufacturer)
                    ? "无线外设"
                    : $"{manufacturer} 无线外设";
            }

            return CleanupName(name);
        }
    }

    public HidInterface? Find(ushort usagePage, ushort? usage = null)
    {
        return Interfaces.FirstOrDefault(i =>
            i.Caps is not null &&
            i.UsagePage == usagePage &&
            (usage is null || i.Usage == usage));
    }

    public HidInterface? FindVendorOutput(int minLength)
    {
        return Interfaces
            .Where(i => i.IsVendorPage && i.OutputReportLength >= minLength)
            .OrderByDescending(i => i.OutputReportLength)
            .FirstOrDefault();
    }

    private static string CleanupName(string name)
    {
        string result = name.Trim();
        string[] suffixes =
        [
            " 2.4G Dongle", " 2.4G Receiver", " 2.4G 接收器", " 2.4G无线接收器",
            " 2.4G 无线接收器", " Dongle", " Receiver", " 接收器"
        ];
        foreach (string suffix in suffixes)
        {
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^suffix.Length].Trim();
            }
        }

        result = Regex.Replace(result, @"\s{2,}", " ");
        return string.IsNullOrWhiteSpace(result) ? "无线外设" : result;
    }
}

internal static class ProtocolHelper
{
    public static PeripheralReading Success(
        DeviceGroup group, int level, bool? charging, string source,
        PeripheralKind kind, string? name = null)
    {
        return new PeripheralReading
        {
            Key = group.Key,
            Name = name ?? group.FriendlyName,
            Kind = kind,
            Level = Math.Clamp(level, 0, 100),
            Charging = charging,
            Source = source,
            UpdatedAt = DateTime.Now
        };
    }

    public static PeripheralReading Failure(
        DeviceGroup group, string error, PeripheralKind kind, string source)
    {
        return new PeripheralReading
        {
            Key = group.Key,
            Name = group.FriendlyName,
            Kind = kind,
            Level = null,
            Charging = null,
            Source = source,
            Error = error,
            UpdatedAt = DateTime.Now
        };
    }

    public static bool IsValidLevel(int level) => level is >= 0 and <= 100;

    public static byte[] Report(int reportId, int totalLength, Action<byte[]>? fill = null)
    {
        byte[] report = new byte[Math.Max(totalLength, 1)];
        report[0] = (byte)reportId;
        fill?.Invoke(report);
        return report;
    }
}

