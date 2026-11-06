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
    Other,

    // 新增类型必须追加在末尾：旧版缓存文件里存的是枚举数值，
    // 插在中间会把历史记录的“外设”读成新类型。
    Remote
}

internal static class PeripheralKindText
{
    public static string ToChinese(this PeripheralKind kind) => kind switch
    {
        PeripheralKind.Keyboard => "键盘",
        PeripheralKind.Mouse => "鼠标",
        PeripheralKind.Headset => "耳机",
        PeripheralKind.Remote => "遥控器",
        _ => "外设"
    };
}

internal sealed class PeripheralReading
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public PeripheralKind Kind { get; set; } = PeripheralKind.Other;
    public int? Level { get; init; }
    public bool? Charging { get; init; }
    public string Source { get; init; } = "";
    public string? Error { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
    public bool IsStale { get; init; }
    /// <summary>设备已断开（如蓝牙离线），当前无法读到电量。</summary>
    public bool IsOffline { get; init; }
    /// <summary>最后一次成功读到电量的时刻；在线时接近当前时刻，离线时用于计算离线时长。</summary>
    public DateTime LastSeenAt { get; init; } = DateTime.Now;
    /// <summary>本轮连续在线的起始时刻；刷新不会重置，只有设备真正离线后重连才重新计时。</summary>
    public DateTime OnlineSince { get; set; }
    public bool IsOnline => Level is not null;

    /// <summary>在线计时用的起点：优先连续在线起点，缺失时退回最近读数时刻。</summary>
    public DateTime OnlineSinceOrUpdated => OnlineSince == DateTime.MinValue ? UpdatedAt : OnlineSince;

    public string KindText => Kind.ToChinese();
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "无线外设" : Name;

    public string StatusText => IsOffline
        ? "已离线"
        : IsStale
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
    /// <summary>最后一次成功读到电量的时刻；设备全部离线时保持历史值，不随刷新时刻漂移。</summary>
    public DateTime LastSuccessAt { get; init; } = DateTime.MinValue;
    public int? MinimumLevel { get; init; }
    public string? Message { get; init; }
    public bool IsDemo { get; init; }

    public int OnlineCount => Devices.Count(d => d.IsOnline && !d.IsStale && !d.IsOffline);
    public int Count => Devices.Count;
}

