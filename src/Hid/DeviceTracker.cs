namespace BatteryTray.Hid;

using System.Text.Json;

/// <summary>
/// 进程内跟踪每台设备最后一次成功读到电量的时刻。
/// 用途：
///  1) 设备断开（尤其是蓝牙）后标记为“离线”并计算离线时长；
///  2) 全部设备离线圈时，“上次更新”仍显示最后一次真实读取时间，而不是随刷新时刻漂移。
/// 状态同时落盘，重启程序后在线/离线时长继续累加而不是从零开始。
/// </summary>
internal sealed class DeviceTracker
{
    private readonly Dictionary<string, TrackedDevice> _map = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSuccessAt = DateTime.MinValue;

    private static string StatePath =>
        Path.Combine(AppPaths.DataDirectory, "device-state.json");

    /// <summary>最近一次有设备成功读到电量的时刻（全局）。</summary>
    public DateTime LastSuccessAt => _lastSuccessAt;

    /// <summary>读取上次运行留下的设备状态，让在线/离线时长跨重启延续。</summary>
    public void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, PersistedDevice>>(
                File.ReadAllText(StatePath));
            if (data is null)
            {
                return;
            }

            foreach (var pair in data)
            {
                var e = pair.Value;
                if (e.LastSeenAt == default)
                {
                    continue;
                }

                _map[pair.Key] = new TrackedDevice
                {
                    Name = string.IsNullOrWhiteSpace(e.Name) ? "无线外设" : e.Name,
                    Kind = e.Kind,
                    Charging = e.Charging,
                    Source = e.Source ?? "",
                    LastSeenAt = e.LastSeenAt,
                    OnlineSince = e.OnlineSince ?? e.LastSeenAt,
                    WasOnline = e.WasOnline
                };
                if (e.LastSeenAt > _lastSuccessAt)
                {
                    _lastSuccessAt = e.LastSeenAt;
                }
            }
        }
        catch
        {
            // 状态文件损坏时按空状态启动。
        }
    }

    /// <summary>把当前跟踪状态写入磁盘，供下次启动继续计时。</summary>
    public void SaveState()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var data = new Dictionary<string, PersistedDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _map)
            {
                var e = pair.Value;
                data[pair.Key] = new PersistedDevice
                {
                    Name = e.Name,
                    Kind = e.Kind,
                    Charging = e.Charging,
                    Source = e.Source,
                    LastSeenAt = e.LastSeenAt,
                    OnlineSince = e.OnlineSince,
                    WasOnline = e.WasOnline
                };
            }

            string temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = false }));
            File.Move(temp, StatePath, true);
        }
        catch
        {
            // 写盘失败不影响读数。
        }
    }

    /// <summary>用跨进程持久化的读数缓存为刚启动的进程预置“最后在线时间”。</summary>
    public void Seed(IEnumerable<KeyValuePair<string, ReadingCache.CachedReading>> entries)
    {
        foreach (var pair in entries)
        {
            var e = pair.Value;
            if (_map.ContainsKey(pair.Key))
            {
                continue;
            }

            _map[pair.Key] = new TrackedDevice
            {
                Name = e.Name,
                Kind = e.Kind,
                Charging = e.Charging,
                Source = e.Source,
                LastSeenAt = e.UpdatedAt,
                OnlineSince = e.UpdatedAt
            };
            if (e.UpdatedAt > _lastSuccessAt)
            {
                _lastSuccessAt = e.UpdatedAt;
            }
        }
    }

    /// <summary>本轮读到电量的设备记为在线，并刷新其最后在线时间。</summary>
    public void RecordOnline(IEnumerable<PeripheralReading> online)
    {
        DateTime now = DateTime.Now;

        // 先快照上一轮“是否在线”，再把全部设备标记为本轮“未在线”，
        // 这样刷新不会重置在线起点，只有真正离线后重连才重新计时。
        var wasOnline = _map.ToDictionary(p => p.Key, p => p.Value.WasOnline,
            StringComparer.OrdinalIgnoreCase);
        foreach (var tracked in _map.Values)
        {
            tracked.WasOnline = false;
        }

        bool any = false;
        foreach (var reading in online)
        {
            if (reading.Level is null)
            {
                continue;
            }

            any = true;
            var prior = _map.TryGetValue(reading.Key, out var tracked) ? tracked : null;
            bool continuous = prior is not null &&
                wasOnline.TryGetValue(reading.Key, out bool was) && was;
            DateTime since = continuous ? prior!.OnlineSince : now;
            reading.OnlineSince = since;
            _map[reading.Key] = new TrackedDevice
            {
                Name = reading.Name,
                Kind = reading.Kind,
                Charging = reading.Charging,
                Source = reading.Source,
                LastSeenAt = now,
                OnlineSince = since,
                WasOnline = true
            };
        }

        if (any)
        {
            _lastSuccessAt = now;
        }
    }

    /// <summary>设备节点已从系统移除（例如拔出接收器、取消配对）时，停止跟踪。</summary>
    public void RemoveAbsent(IEnumerable<string> presentKeys)
    {
        var present = new HashSet<string>(presentKeys, StringComparer.OrdinalIgnoreCase);
        foreach (string key in _map.Keys.ToList())
        {
            if (!present.Contains(key))
            {
                _map.Remove(key);
            }
        }
    }

    /// <summary>
    /// 对“节点仍在系统、但本轮未读到电量”的已知蓝牙设备生成离线读数。
    /// HID 2.4G 设备由各协议适配器生成“失败/上次读数”，不在此重复处理。
    /// </summary>
    public IReadOnlyList<PeripheralReading> BuildOffline(IReadOnlySet<string> presentKeys)
    {
        DateTime now = DateTime.Now;
        var result = new List<PeripheralReading>();
        foreach (var pair in _map)
        {
            if (!pair.Key.StartsWith("BT:", StringComparison.Ordinal) ||
                !presentKeys.Contains(pair.Key))
            {
                continue;
            }

            // 6 秒内刚在线过的设备避免边界误标。
            if (now - pair.Value.LastSeenAt < TimeSpan.FromSeconds(6))
            {
                continue;
            }

            result.Add(new PeripheralReading
            {
                Key = pair.Key,
                Name = pair.Value.Name,
                Kind = pair.Value.Kind,
                Level = null,
                Charging = null,
                Source = pair.Value.Source,
                UpdatedAt = pair.Value.LastSeenAt,
                LastSeenAt = pair.Value.LastSeenAt,
                IsOffline = true
            });
        }

        return result;
    }

    private sealed class TrackedDevice
    {
        public string Name = "无线外设";
        public PeripheralKind Kind = PeripheralKind.Other;
        public bool? Charging;
        public string Source = "";
        public DateTime LastSeenAt;
        public DateTime OnlineSince;
        public bool WasOnline;
    }

    private sealed class PersistedDevice
    {
        public string? Name { get; set; }
        public PeripheralKind Kind { get; set; } = PeripheralKind.Other;
        public bool? Charging { get; set; }
        public string? Source { get; set; }
        public DateTime LastSeenAt { get; set; }
        public DateTime? OnlineSince { get; set; }
        public bool WasOnline { get; set; }
    }
}
