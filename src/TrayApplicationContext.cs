using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BatteryTray.Hid;
using BatteryTray.UI;
using Microsoft.Win32;

namespace BatteryTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _trayRetryTimer;
    private readonly BatteryMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly ReadingCache _cache;
    private readonly EventWaitHandle? _showEvent;
    private readonly RegisteredWaitHandle? _showWait;
    private readonly Font _menuFont;
    private readonly Font _boldMenuFont;
    private readonly string? _trayInfoPath;
    private bool _trayInfoWritten;

    private ContextMenuStrip? _menu;
    private Icon? _currentIcon;
    private MonitorResult? _lastResult;
    private DeviceDetailsForm? _details;
    private SynchronizationContext? _sync;
    private bool _refreshing;
    private bool _firstTick = true;
    private bool _menuOpen;
    private bool _pendingMenuRebuild;
    private volatile bool _pendingShow;

    // 低电量提醒：低于 20% 提醒一次，低于 10% 再升级提醒一次；
    // 电量回升到 20% 以上才清除记录，避免每轮刷新都弹。
    private const int WarnLevel = 20;
    private const int CriticalLevel = 10;
    private readonly Dictionary<string, int> _batteryAlerts = new();
    private readonly HashSet<string> _chargedAnnounced = new();
    private readonly VoiceAnnouncer _voice;
    private readonly bool _demo;

    public TrayApplicationContext(
        bool demo = false, EventWaitHandle? showEvent = null, string? trayInfoPath = null,
        bool openOnStart = false)
    {
        _showEvent = showEvent;
        _trayInfoPath = trayInfoPath;
        _demo = demo;
        _settings = AppSettings.Load();
        AutoStartManager.RepairPathIfNeeded();
        var tracker = new DeviceTracker();
        _cache = ReadingCache.Load();
        tracker.Seed(_cache.All());
        _monitor = new BatteryMonitor(demo, tracker);
        _menuFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _boldMenuFont = new Font(_menuFont, FontStyle.Bold);
        _voice = new VoiceAnnouncer(_settings.VoiceEnabled);

        _currentIcon = BatteryIconRenderer.CreateTrayIcon(null, false);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "无线外设电量 · 正在读取…",
            // 通知区域在消息循环启动后才可靠注册，首帧定时器里再显示。
            Visible = false
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowDetails();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDetails();

        _menu = BuildMenu();
        _notifyIcon.ContextMenuStrip = _menu;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += OnTimerTick;
        _refreshTimer.Start();

        _trayRetryTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _trayRetryTimer.Tick += OnTrayRetryTick;
        _trayRetryTimer.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // --show 启动时直接把详情窗口显示出来，方便从快捷方式一眼看到界面。
        _pendingShow = openOnStart;

        if (showEvent is not null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                (_, timedOut) =>
                {
                    if (timedOut)
                    {
                        return;
                    }

                    _pendingShow = true;
                    _sync?.Post(_ => ShowDetails(), null);
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    /// <summary>诊断用：把托盘右键菜单渲染成 PNG，方便核对菜单排版。</summary>
    internal void RenderMenuPreview(MonitorResult result, string path)
    {
        var menu = BuildMenu(result);
        menu.Show(new Point(4, 4));
        Application.DoEvents();
        var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(0, 0, menu.Width, menu.Height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        bitmap.Dispose();
        menu.Close();
        menu.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs e)    {
        _sync = SynchronizationContext.Current;
        if (_firstTick)
        {
            _firstTick = false;
            _notifyIcon.Visible = true;
            _refreshTimer.Interval = Math.Max(10, _settings.RefreshSeconds) * 1000;
            if (_pendingShow)
            {
                _pendingShow = false;
                ShowDetails();
            }

            _ = RefreshAsync();
        }
        else
        {
            _ = RefreshAsync();
        }
    }

    private void OnTrayRetryTick(object? sender, EventArgs e)
    {
        _trayRetryTimer.Stop();
        if (!_notifyIcon.Visible)
        {
            return;
        }

        // 某些 Windows 11 任务栏在启动瞬间会丢弃第一次 NIM_ADD，重挂一次。
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _sync?.Post(_ => _ = RefreshAsync(), null);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        _details?.SetRefreshing(true);
        try
        {
            var raw = await _monitor.ReadAsync();
            _cache.Store(raw);
            var result = _cache.Enrich(raw);
            _lastResult = result;
            UpdatePresentation(result);
            TryDumpTrayInfo();
        }
        catch (Exception ex)
        {
            _lastResult = new MonitorResult
            {
                Devices = Array.Empty<PeripheralReading>(),
                Message = $"刷新失败：{ex.Message}",
                UpdatedAt = DateTime.Now,
                LastSuccessAt = DateTime.Now
            };
            UpdatePresentation(_lastResult);
        }
        finally
        {
            _refreshing = false;
            _details?.SetRefreshing(false);
        }
    }

    private void UpdatePresentation(MonitorResult result)
    {
        int? minimum = result.MinimumLevel;
        var minimumDevice = result.Devices.FirstOrDefault(d => d.Level == minimum);
        bool charging = minimumDevice?.Charging == true;
        bool bluetooth = minimumDevice?.Key.StartsWith("BT:", StringComparison.Ordinal) == true;

        Icon icon = BatteryIconRenderer.CreateTrayIcon(minimum, charging, bluetooth);
        Icon? oldIcon = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        oldIcon?.Dispose();

        SetTooltip(result);
        RebuildMenuIfNeeded(result);
        CheckLowBattery(result);
        if (_details is { IsDisposed: false })
        {
            _details.UpdateResult(result, _settings.RefreshSeconds);
        }

        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            _notifyIcon.BalloonTipTitle = "无线外设电量";
            _notifyIcon.BalloonTipText =
                "已在通知区域运行。若看不到图标，请在“任务栏设置 → 其他系统托盘图标”中启用。";
            _notifyIcon.ShowBalloonTip(4500);
        }
    }

    /// <summary>
    /// 电量跨过 20% / 10% 阈值时弹一次托盘通知并语音播报一次，回升后自动复位；
    /// 充到 100% 说一句已充满。充电中、离线、读数缺失的设备都不参与提醒。
    /// </summary>
    private void CheckLowBattery(MonitorResult result)
    {
        if (_demo)
        {
            // 演示模式默认不弹提醒，允许用环境变量打开以便自测文案。
            if (Environment.GetEnvironmentVariable("BATTERY_TRAY_DEMO_ALERTS") is null)
            {
                return;
            }
        }

        List<string> speech = new();
        bool urgent = false;

        Dictionary<string, int> current = new();
        foreach (var device in result.Devices)
        {
            if (device.IsOffline || device.Charging == true || device.Level is not int level)
            {
                continue;
            }

            if (level >= WarnLevel)
            {
                continue;
            }

            current[device.Key] = level < CriticalLevel ? 2 : 1;
        }

        // 电量回升或设备消失时清掉记录，下次再变低还会提醒。
        foreach (string key in _batteryAlerts.Keys.ToList())
        {
            if (current.ContainsKey(key))
            {
                continue;
            }

            _batteryAlerts.Remove(key);
            var restored = result.Devices.FirstOrDefault(device => device.Key == key);
            if (restored is { IsOffline: false, Level: >= WarnLevel })
            {
                speech.Add($"{DisplayName(restored)} 电量已恢复到 {restored.Level}%");
            }
        }

        List<(string Name, int Level, int Severity)> raised = new();
        foreach (var device in result.Devices)
        {
            if (!current.TryGetValue(device.Key, out int severity))
            {
                continue;
            }

            int seen = _batteryAlerts.TryGetValue(device.Key, out int previous) ? previous : 0;
            if (severity > seen)
            {
                raised.Add((DisplayName(device), device.Level!.Value, severity));
            }
        }

        foreach (var entry in current)
        {
            _batteryAlerts[entry.Key] = entry.Value;
        }

        if (raised.Count > 0)
        {
            bool critical = raised.Exists(item => item.Severity == 2);
            urgent = critical;
            _notifyIcon.BalloonTipTitle = critical ? "电量紧急" : "电量警告";
            _notifyIcon.BalloonTipIcon = critical ? ToolTipIcon.Error : ToolTipIcon.Warning;
            _notifyIcon.BalloonTipText = BuildBatteryAlertText(raised, critical);
            _notifyIcon.ShowBalloonTip(critical ? 15000 : 10000);

            foreach (var item in raised)
            {
                speech.Add(item.Severity == 2
                    ? $"{item.Name} 电量仅剩 {item.Level}%，请及时更换电池或充电"
                    : $"{item.Name} 电量剩余 {item.Level}%，请关注");
            }
        }

        // 充满提醒：只在“正在充电且到 100%”那一刻说一次，电量掉下来后复位。
        foreach (var device in result.Devices)
        {
            if (device.IsOffline || device.Charging != true || device.Level is not int level)
            {
                _chargedAnnounced.Remove(device.Key);
                continue;
            }

            if (level < 100)
            {
                _chargedAnnounced.Remove(device.Key);
                continue;
            }

            if (_chargedAnnounced.Add(device.Key))
            {
                speech.Add($"{DisplayName(device)} 电量已充满，可以断开充电器了");
            }
        }

        if (speech.Count > 0)
        {
            _voice.Announce(string.Join("。", speech) + "。", urgent);
        }
    }

    private static string DisplayName(PeripheralReading device)
    {
        return string.IsNullOrWhiteSpace(device.Name) ? "设备" : device.Name!;
    }

    private static string BuildBatteryAlertText(
        List<(string Name, int Level, int Severity)> items, bool critical)
    {
        var ordered = items
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Level)
            .ToList();
        string detail = string.Join("、",
            ordered.Take(2).Select(item => $"{item.Name} 仅剩 {item.Level}%"));
        string advice = critical ? "请立即充电。" : "建议尽快充电。";
        if (ordered.Count > 2)
        {
            advice += $"另有 {ordered.Count - 2} 台设备电量偏低。";
        }

        return detail + "，" + advice;
    }

    private void SetTooltip(MonitorResult result)
    {
        try
        {
            _notifyIcon.Text = BuildTooltip(result, 127);
        }
        catch (ArgumentException)
        {
            try
            {
                _notifyIcon.Text = BuildTooltip(result, 63);
            }
            catch
            {
                _notifyIcon.Text = "无线外设电量";
            }
        }
        catch
        {
            _notifyIcon.Text = "无线外设电量";
        }
    }

    internal static string BuildTooltip(MonitorResult result, int maxLength)
    {
        if (result.Count == 0)
        {
            string empty = result.Message ?? "未发现受支持的无线外设";
            return Clip(empty, maxLength);
        }

        for (int nameLimit = 22; nameLimit >= 4; nameLimit -= 2)
        {
            var builder = new StringBuilder();
            DateTime lastRead = result.LastSuccessAt == DateTime.MinValue
                ? result.UpdatedAt
                : result.LastSuccessAt;
            string header = result.MinimumLevel is null
                ? $"无线外设 · {lastRead:HH:mm}"
                : $"无线外设 最低{result.MinimumLevel}% · {lastRead:HH:mm}";
            builder.Append(header);

            int shown = 0;
            foreach (var device in result.Devices)
            {
                string name = device.DisplayName;
                if (name.Length > nameLimit)
                {
                    name = name[..Math.Max(1, nameLimit - 1)] + "…";
                }

                string line = device.Level is null
                    ? device.IsOffline
                        ? $"\n{name}  离线 {device.LastSeenAt:MM-dd HH:mm}"
                        : $"\n{name}  未读取"
                    : $"\n{name}  {device.Level}%";
                if (device.IsOffline)
                {
                    // 离线状态已在上方体现。
                }
                else if (device.IsStale)
                {
                    line += " 上次";
                }
                else if (device.Charging == true)
                {
                    line += " 充电中";
                }

                if (builder.Length + line.Length > maxLength - 2)
                {
                    break;
                }

                builder.Append(line);
                shown++;
            }

            if (shown == 0)
            {
                continue;
            }

            if (shown == result.Count)
            {
                return builder.ToString();
            }

            string text = builder.ToString();
            if (nameLimit <= 4)
            {
                return text.Length <= maxLength
                    ? text
                    : Clip(BuildCompactTooltip(result), maxLength);
            }
        }

        return Clip(BuildCompactTooltip(result), maxLength);
    }

    private static string BuildCompactTooltip(MonitorResult result)
    {
        var builder = new StringBuilder("无线外设 ");
        builder.Append(result.MinimumLevel is null ? "未知" : $"最低{result.MinimumLevel}%");
        foreach (var device in result.Devices.Take(3))
        {
            string name = device.DisplayName;
            if (name.Length > 8)
            {
                name = name[..7] + "…";
            }

            builder.Append($" · {name} {device.Level?.ToString() ?? "?"}%");
        }

        return builder.ToString();
    }

    private static string Clip(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 1 ? value[..maxLength] : value[..(maxLength - 1)] + "…";
    }

    private void RebuildMenuIfNeeded(MonitorResult result)
    {
        if (_menuOpen)
        {
            _pendingMenuRebuild = true;
            return;
        }

        RebuildMenu(result);
    }

    private void RebuildMenu(MonitorResult? result = null)
    {
        result ??= _lastResult;
        var newMenu = BuildMenu(result);
        ContextMenuStrip? old = _menu;
        _menu = newMenu;
        _notifyIcon.ContextMenuStrip = newMenu;
        if (old is not null && !old.Visible)
        {
            old.Dispose();
        }
    }

    private static readonly Dictionary<Color, Image> _dotCache = new();

    private static Image MenuDot(PeripheralReading device)
    {
        Color color = device.IsOffline
            ? Color.FromArgb(190, 198, 208)
            : device.Level is null
                ? Color.FromArgb(210, 84, 84)
                : device.Level >= 60
                    ? Color.FromArgb(34, 197, 94)
                    : device.Level >= 20
                        ? Color.FromArgb(240, 165, 40)
                        : Color.FromArgb(225, 70, 70);
        if (_dotCache.TryGetValue(color, out Image? cached) && cached is not null)
        {
            return cached;
        }

        var bitmap = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 3, 3, 6, 6);
        }

        _dotCache[color] = bitmap;
        return bitmap;
    }

    private ContextMenuStrip BuildMenu(MonitorResult? result = null)
    {
        result ??= _lastResult;
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            // 关掉独立的勾选列：勾直接落在图标列里，和设备的彩色圆点一条竖线上。
            ShowCheckMargin = false,
            ImageScalingSize = new Size(12, 12),
            Font = _menuFont,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(32, 35, 40),
            Renderer = new UI.ModernMenuRenderer(),
            Padding = new Padding(4, 6, 4, 6),
            DropShadowEnabled = true
        };
        menu.Opening += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_pendingMenuRebuild)
            {
                _pendingMenuRebuild = false;
                BeginInvokeOnUi(() => RebuildMenu());
            }
        };

        var title = new ToolStripMenuItem("无线外设电量")
        {
            Enabled = false,
            Font = _boldMenuFont,
            Tag = "menu-header"
        };
        menu.Items.Add(title);

        DateTime menuLastRead = result?.LastSuccessAt is { } ls && ls != DateTime.MinValue
            ? ls
            : result?.UpdatedAt ?? DateTime.Now;
        string summary = result is null
            ? "正在读取…"
            : result.Count == 0
                ? $"{result.Message ?? "未发现受支持的设备"} · 上次更新 {menuLastRead:HH:mm:ss}"
                : $"最低 {result.MinimumLevel?.ToString() ?? "未知"}% · 上次更新 {menuLastRead:HH:mm:ss}";
        menu.Items.Add(new ToolStripMenuItem(summary) { Enabled = false, Tag = "menu-subheader" });

        if (result is { Count: > 0 })
        {
            menu.Items.Add(new ToolStripSeparator());
            var visibleDevices = result.Devices.Where(MatchesFilter).ToList();
            if (visibleDevices.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("（当前筛选下没有设备）") { Enabled = false });
            }

            foreach (var device in visibleDevices)
            {
                string level = device.Level is null ? "未读取" : $"{device.Level}%";
                string text = device.IsOffline
                    ? $"{device.DisplayName}（{device.KindText}）  离线"
                    : device.Level is null
                        ? $"{device.DisplayName}（{device.KindText}）  {level}"
                        : $"{device.DisplayName}（{device.KindText}）  {level}  {device.StatusText}";
                var item = new ToolStripMenuItem(text)
                {
                    ForeColor = device.Level is null
                        ? Color.FromArgb(160, 65, 65)
                        : MenuColor(device.Level.Value),
                    Image = MenuDot(device),
                    ToolTipText = device.Source
                };
                item.Click += (_, _) => ShowDetails();
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var interval = new ToolStripMenuItem("刷新间隔");
        int[] choices = [30, 60, 120, 300, 600, 1800];
        foreach (int seconds in choices)
        {
            var item = new ToolStripMenuItem(IntervalLabel(seconds))
            {
                Checked = _settings.RefreshSeconds == seconds,
                CheckOnClick = false
            };
            item.Click += (_, _) => SetRefreshInterval(seconds);
            interval.DropDownItems.Add(item);
        }

        menu.Items.Add(interval);
        menu.Items.Add(new ToolStripSeparator());

        var filterMenu = new ToolStripMenuItem("显示筛选");
        foreach (var (key, label) in new (string Key, string Label)[]
                 {
                     ("all", "全部"),
                     ("online", "仅在线"),
                     ("offline", "仅离线")
                 })
        {
            var filterItem = new ToolStripMenuItem(label)
            {
                Checked = _settings.FilterMode == key,
                CheckOnClick = false
            };
            filterItem.Click += (_, _) => SetFilterMode(key);
            filterMenu.DropDownItems.Add(filterItem);
        }

        menu.Items.Add(filterMenu);

        var voiceItem = new ToolStripMenuItem("语音提醒")
        {
            Checked = _settings.VoiceEnabled,
            CheckOnClick = true
        };
        voiceItem.CheckedChanged += (_, _) =>
        {
            _settings.VoiceEnabled = voiceItem.Checked;
            _voice.Enabled = voiceItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(voiceItem);
        menu.Items.Add(new ToolStripSeparator());

        var details = new ToolStripMenuItem("设备详情…");
        details.Click += (_, _) => ShowDetails();
        menu.Items.Add(details);

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);

        ApplyMenuSpacing(menu);

        return menu;
    }

    /// <summary>统一行距，并让所有子菜单的勾也落在图标列，跟主菜单保持同一套间距。</summary>
    private static void ApplyMenuSpacing(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items)
        {
            if (item is ToolStripSeparator)
            {
                continue;
            }

            item.Padding = new Padding(0, 2, 0, 2);

            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                if (menuItem.DropDown is ContextMenuStrip dropDown)
                {
                    dropDown.ShowCheckMargin = false;
                }

                ApplyMenuSpacing(menuItem.DropDown);
            }
        }
    }

    private static Color MenuColor(int level)    {
        return level >= 20
            ? Color.FromArgb(52, 58, 66)
            : level >= 10
                ? Color.FromArgb(176, 118, 10)
                : Color.FromArgb(190, 42, 42);
    }

    private static string IntervalLabel(int seconds) => seconds switch
    {
        30 => "30 秒",
        60 => "1 分钟",
        120 => "2 分钟",
        300 => "5 分钟",
        600 => "10 分钟",
        1800 => "30 分钟",
        _ => $"{seconds} 秒"
    };

    private void SetRefreshInterval(int seconds)
    {
        _settings.RefreshSeconds = AppSettings.Normalize(seconds);
        _settings.Save();
        _refreshTimer.Interval = _settings.RefreshSeconds * 1000;
        if (_lastResult is not null)
        {
            RebuildMenu(_lastResult);
            _details?.UpdateResult(_lastResult, _settings.RefreshSeconds);
        }
    }

    private bool MatchesFilter(PeripheralReading device)
    {
        return _settings.FilterMode switch
        {
            "online" => device.Level is not null && !device.IsOffline,
            "offline" => device.IsOffline,
            _ => true
        };
    }

    private void SetFilterMode(string mode)
    {
        _settings.FilterMode = AppSettings.NormalizeFilter(mode);
        _settings.Save();
        if (_lastResult is not null)
        {
            RebuildMenu(_lastResult);
            _details?.SetFilter(_settings.FilterMode);
        }
    }

    private void OnDetailsFilterChanged(string mode)
    {
        _settings.FilterMode = AppSettings.NormalizeFilter(mode);
        _settings.Save();
        if (_lastResult is not null)
        {
            RebuildMenu(_lastResult);
        }
    }

    private void ShowDetails()
    {
        if (_details is { IsDisposed: false })
        {
            if (_details.WindowState == FormWindowState.Minimized)
            {
                _details.WindowState = FormWindowState.Normal;
            }

            if (!_details.Visible)
            {
                _details.Show();
            }

            _details.WindowState = FormWindowState.Normal;
            _details.Activate();
            _details.BringToFront();
            if (_lastResult is not null)
            {
                _details.UpdateResult(_lastResult, _settings.RefreshSeconds);
            }

            return;
        }

        _details = new DeviceDetailsForm(RefreshAsync, _settings.FilterMode);
        _details.IntervalChanged += SetRefreshInterval;
        _details.FilterChanged += OnDetailsFilterChanged;
        _details.ExitRequested += ExitApplication;
        _details.FormClosed += (_, _) =>
        {
            _details?.Dispose();
            _details = null;
        };
        _details.Show();
        _details.Activate();
        _details.SetInterval(_settings.RefreshSeconds);
        if (_lastResult is not null)
        {
            _details.UpdateResult(_lastResult, _settings.RefreshSeconds);
        }
        else
        {
            _ = RefreshAsync();
        }
    }

    private void BeginInvokeOnUi(Action action)
    {
        if (_sync is not null)
        {
            _sync.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    private void TryDumpTrayInfo()
    {
        if (_trayInfoPath is null || _trayInfoWritten)
        {
            return;
        }

        _trayInfoWritten = true;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(NotifyIcon);
            bool added = type.GetField("_added", flags)?.GetValue(_notifyIcon) is true;
            uint id = type.GetField("_id", flags)?.GetValue(_notifyIcon) is uint value ? value : 0;
            object? window = type.GetField("_window", flags)?.GetValue(_notifyIcon);
            IntPtr handle = IntPtr.Zero;
            if (window is not null)
            {
                var handleProperty = window.GetType().GetProperty(
                    "Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (handleProperty?.GetValue(window) is IntPtr h)
                {
                    handle = h;
                }
            }

            var identifier = new NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = handle,
                uID = id,
                guidItem = Guid.Empty
            };
            int hr = Shell_NotifyIconGetRect(ref identifier, out RECT rect);

            var testData = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = handle,
                uID = 0xFFFF,
                uFlags = 0x01 | 0x02 | 0x04,
                uCallbackMessage = 0x8000 + 77,
                hIcon = SystemIcons.Application.Handle,
                szTip = "TRAY TEST",
                szInfo = "",
                szInfoTitle = ""
            };
            bool testAdded = Shell_NotifyIcon(0, ref testData);
            int testError = Marshal.GetLastWin32Error();
            var testIdentifier = new NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = handle,
                uID = testData.uID,
                guidItem = Guid.Empty
            };
            int testHr = Shell_NotifyIconGetRect(ref testIdentifier, out RECT testRect);
            if (testAdded)
            {
                Shell_NotifyIcon(2, ref testData);
            }

            var icon = _notifyIcon.Icon;
            if (icon is not null)
            {
                using var bitmap = icon.ToBitmap();
                bitmap.Save(_trayInfoPath + ".png", System.Drawing.Imaging.ImageFormat.Png);
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(_trayInfoPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_trayInfoPath,
                $"visible={_notifyIcon.Visible}\n" +
                $"added={added}\n" +
                $"hwnd=0x{handle.ToInt64():X}\n" +
                $"uID={id}\n" +
                $"Shell_NotifyIconGetRect hr=0x{hr:X8}\n" +
                $"rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}\n" +
                $"testAdd={testAdded} error={testError}\n" +
                $"testRectHr=0x{testHr:X8} testRect={testRect.Left},{testRect.Top},{testRect.Right},{testRect.Bottom}\n" +
                $"textLength={_notifyIcon.Text.Length}\n" +
                $"text={_notifyIcon.Text}\n");
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(_trayInfoPath, ex.ToString());
            }
            catch
            {
                // 诊断模式写文件失败时忽略。
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    private void ExitApplication()
    {
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
        _details?.Close();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
        Application.ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _refreshTimer.Dispose();
            _trayRetryTimer.Dispose();
            _notifyIcon.Dispose();
            _menu?.Dispose();
            _currentIcon?.Dispose();
            _details?.Dispose();
            _menuFont.Dispose();
            _boldMenuFont.Dispose();
            _voice.Dispose();
        }

        base.Dispose(disposing);
    }
}

