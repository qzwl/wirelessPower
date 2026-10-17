namespace BatteryTray.Hid;

internal sealed record HidCaps(
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    int FeatureValueCaps)
{
    public bool IsVendorPage => UsagePage >= 0xFF00;
}

internal sealed record HidInterface(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    string Product,
    string Manufacturer,
    string Serial,
    HidCaps? Caps,
    string GroupKey,
    string? Error)
{
    public string VidPid => $"{VendorId:X4}:{ProductId:X4}";
    public ushort UsagePage => Caps?.UsagePage ?? 0;
    public ushort Usage => Caps?.Usage ?? 0;
    public int InputReportLength => Caps?.InputReportLength ?? 0;
    public int OutputReportLength => Caps?.OutputReportLength ?? 0;
    public int FeatureReportLength => Caps?.FeatureReportLength ?? 0;
    public bool IsVendorPage => UsagePage >= 0xFF00;

    public string DisplayName => !string.IsNullOrWhiteSpace(Product)
        ? Product
        : !string.IsNullOrWhiteSpace(Manufacturer)
            ? $"{Manufacturer} HID 设备"
            : "HID 设备";
}

internal enum PeripheralKind
{
    Keyboard,
    Mouse,
    Headset,
    Other
}

internal static class PeripheralKindText
{
    public static string ToChinese(this PeripheralKind kind) => kind switch
    {
        PeripheralKind.Keyboard => "键盘",
        PeripheralKind.Mouse => "鼠标",
        PeripheralKind.Headset => "耳机",
        _ => "外设"
    };
}

internal sealed class PeripheralReading
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public PeripheralKind Kind { get; init; } = PeripheralKind.Other;
    public int? Level { get; init; }
    public bool? Charging { get; init; }
    public string Source { get; init; } = "";
    public string? Error { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
    public bool IsStale { get; init; }
    public bool IsOnline => Level is not null;

    public string KindText => Kind.ToChinese();
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "无线外设" : Name;

    public string StatusText => IsStale
        ? "上次读数"
        : Error is not null && Level is null
            ? Error
            : Charging == true
                ? "充电中"
                : Charging == false
                    ? "放电中"
                    : "已读取";
}

internal sealed class MonitorResult
{
    public required IReadOnlyList<PeripheralReading> Devices { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
    public int? MinimumLevel { get; init; }
    public string? Message { get; init; }
    public bool IsDemo { get; init; }

    public int OnlineCount => Devices.Count(d => d.IsOnline && !d.IsStale);
    public int Count => Devices.Count;
}

