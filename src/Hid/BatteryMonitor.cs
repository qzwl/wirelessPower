using BatteryTray.Bluetooth;
using BatteryTray.Hid.Protocols;

namespace BatteryTray.Hid;

internal sealed class BatteryMonitor
{
    private readonly IReadOnlyList<IProtocolAdapter> _adapters =
    [
        new AtkEvisionAdapter(),
        new RapooAdapter(),
        new DarkrimAdapter(),
        new AtkVendorAdapter(),
        new EndgameAdapter(),
        new VaxeeAdapter(),
        new NordicAdapter(),
        new LogitechAdapter(),
        new StandardBatteryAdapter()
    ];

    private readonly bool _demo;
    private readonly BluetoothBatteryReader _bluetooth = new();

    public BatteryMonitor(bool demo = false)
    {
        _demo = demo;
    }

    public Task<MonitorResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Read(cancellationToken), cancellationToken);
    }

    public MonitorResult Read(CancellationToken cancellationToken = default)
    {
        if (_demo)
        {
            return DemoResult();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interfaces = HidEnumerator.Enumerate();
            var groups = interfaces
                .GroupBy(i => i.GroupKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DeviceGroup
                {
                    Key = g.Key,
                    Interfaces = g.OrderByDescending(i => i.Product.Length).ToList()
                })
                .ToList();

            var results = new List<PeripheralReading>();
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = _adapters
                    .Where(a => a.Matches(group))
                    .OrderBy(a => a.Priority)
                    .ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                PeripheralReading? failure = null;
                PeripheralReading? success = null;
                foreach (var adapter in matches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reading = adapter.TryRead(group);
                    if (reading is null)
                    {
                        continue;
                    }

                    if (reading.Level is not null)
                    {
                        success = reading;
                        break;
                    }

                    failure ??= reading;
                }

                if (success is not null)
                {
                    results.Add(success);
                }
                else if (failure is not null)
                {
                    results.Add(failure);
                }
            }

            // 蓝牙设备走 Windows 的 PnP 电量属性（HFP 节点）。HID 已经读到
            // 同名设备时不重复显示。
            try
            {
                var hidNames = results
                    .Select(r => NormalizeName(r.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var bluetooth in _bluetooth.Read())
                {
                    if (!hidNames.Contains(NormalizeName(bluetooth.Name)))
                    {
                        results.Add(bluetooth);
                    }
                }
            }
            catch
            {
                // 蓝牙服务不可用时不影响 USB 设备读取。
            }

            var ordered = results
                .OrderBy(d => KindOrder(d.Kind))
                .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            int? minimum = ordered
                .Where(d => d.Level.HasValue)
                .Select(d => d.Level)
                .Min();

            string? message = ordered.Count == 0
                ? "未发现受支持的无线外设"
                : ordered.All(d => d.Level is null)
                    ? "发现外设，但未能读取电量"
                    : null;

            return new MonitorResult
            {
                Devices = ordered,
                MinimumLevel = minimum,
                Message = message,
                UpdatedAt = DateTime.Now
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new MonitorResult
            {
                Devices = Array.Empty<PeripheralReading>(),
                Message = $"读取 HID 设备失败：{ex.Message}",
                UpdatedAt = DateTime.Now
            };
        }
    }

    private static int KindOrder(PeripheralKind kind) => kind switch
    {
        PeripheralKind.Keyboard => 0,
        PeripheralKind.Mouse => 1,
        PeripheralKind.Headset => 2,
        _ => 3
    };

    private static string NormalizeName(string name)
    {
        return new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static MonitorResult DemoResult()
    {
        var now = DateTime.Now;
        var devices = new List<PeripheralReading>
        {
            new()
            {
                Key = "demo-atk",
                Name = "ATK V75X",
                Kind = PeripheralKind.Keyboard,
                Level = 88,
                Charging = false,
                Source = "演示数据",
                UpdatedAt = now
            },
            new()
            {
                Key = "demo-rapoo",
                Name = "Rapoo Gaming Device",
                Kind = PeripheralKind.Mouse,
                Level = 78,
                Charging = false,
                Source = "演示数据",
                UpdatedAt = now
            },
            new()
            {
                Key = "demo-darkrim",
                Name = "DARKRIM SHADOW",
                Kind = PeripheralKind.Headset,
                Level = 54,
                Charging = true,
                Source = "演示数据",
                UpdatedAt = now
            },
            new()
            {
                Key = "demo-bluetooth",
                Name = "Sony WH-1000XM5",
                Kind = PeripheralKind.Headset,
                Level = 67,
                Charging = null,
                Source = "蓝牙 HFP（演示）",
                UpdatedAt = now
            }
        };

        return new MonitorResult
        {
            Devices = devices,
            MinimumLevel = 54,
            UpdatedAt = now,
            IsDemo = true
        };
    }
}

