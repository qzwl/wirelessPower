using System.Text.Json;
using BatteryTray.Hid;

namespace BatteryTray;

internal sealed class ReadingCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(36);
    private readonly string _path;
    private Dictionary<string, CachedReading> _entries;

    private ReadingCache(string path, Dictionary<string, CachedReading> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static ReadingCache Load()
    {
        string directory = AppPaths.DataDirectory;
        string path = Path.Combine(directory, "last-readings.json");
        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, CachedReading>>(
                    File.ReadAllText(path)) ?? [];
                entries = entries
                    .Where(pair => DateTime.Now - pair.Value.UpdatedAt <= MaxAge)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                return new ReadingCache(path, entries);
            }
        }
        catch
        {
            // 缓存损坏时不影响正常读取。
        }

        return new ReadingCache(path, []);
    }

    /// <summary>当前缓存中的所有读数（含设备键），用于启动时为设备状态跟踪器预置“最后在线时间”。</summary>
    public IEnumerable<KeyValuePair<string, CachedReading>> All() => _entries;

    public void Store(MonitorResult result)
    {
        if (result.IsDemo)
        {
            return;
        }

        bool changed = false;
        foreach (var device in result.Devices)
        {
            if (device.Level is null || device.IsStale)
            {
                continue;
            }

            if (_entries.TryGetValue(device.Key, out var existing) &&
                existing.Level == device.Level.Value &&
                existing.Charging == device.Charging &&
                existing.Source == device.Source &&
                DateTime.Now - existing.UpdatedAt < TimeSpan.FromMinutes(10))
            {
                continue;
            }

            _entries[device.Key] = new CachedReading
            {
                Name = device.Name,
                Kind = device.Kind,
                Level = device.Level.Value,
                Charging = device.Charging,
                Source = device.Source,
                UpdatedAt = device.UpdatedAt
            };
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    public MonitorResult Enrich(MonitorResult result)
    {
        if (result.IsDemo)
        {
            return result;
        }

        var devices = new List<PeripheralReading>(result.Devices.Count);
        foreach (var device in result.Devices)
        {
            if (device.Level is not null ||
                device.IsOffline || // 离线设备保留原样，不回填为“上次读数”
                !_entries.TryGetValue(device.Key, out var cached) ||
                DateTime.Now - cached.UpdatedAt > MaxAge)
            {
                devices.Add(device);
                continue;
            }

            devices.Add(new PeripheralReading
            {
                Key = device.Key,
                Name = string.IsNullOrWhiteSpace(device.Name) ? cached.Name : device.Name,
                Kind = cached.Kind,
                Level = cached.Level,
                Charging = cached.Charging,
                Source = $"{cached.Source}（{cached.UpdatedAt:MM-dd HH:mm} 上次）",
                UpdatedAt = cached.UpdatedAt,
                IsStale = true
            });
        }

        int? minimum = devices
            .Where(d => d.Level.HasValue)
            .Select(d => d.Level)
            .Min();
        string? message = devices.Count == 0
            ? result.Message
            : devices.All(d => d.Level is null)
                ? "发现外设，但设备处于休眠且没有历史读数"
                : null;

        return new MonitorResult
        {
            Devices = devices,
            UpdatedAt = result.UpdatedAt,
            LastSuccessAt = result.LastSuccessAt,
            MinimumLevel = minimum,
            Message = message,
            IsDemo = result.IsDemo
        };
    }

    private void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries,
                new JsonSerializerOptions { WriteIndented = false }));
            File.Move(temp, _path, true);
        }
        catch
        {
            // 缓存写入失败不影响本次显示。
        }
    }

    public sealed class CachedReading
    {
        public string Name { get; set; } = "无线外设";
        public PeripheralKind Kind { get; set; }
        public int Level { get; set; }
        public bool? Charging { get; set; }
        public string Source { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }
}

