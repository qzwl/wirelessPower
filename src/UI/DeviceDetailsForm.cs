using System.Drawing;
using System.Drawing.Drawing2D;
using BatteryTray.Hid;

namespace BatteryTray.UI;

internal sealed class DeviceDetailsForm : Form
{
    private readonly FlowLayoutPanel _list;
    private readonly Label _summary;
    private readonly Label _footer;
    private readonly CheckBox _autoStartCheck;
    private readonly Button _refreshButton;
    private readonly ComboBox _intervalBox;
    private readonly List<DeviceCard> _cards = [];
    private readonly Func<Task>? _refreshAction;
    private MonitorResult? _result;
    private bool _syncingAutoStart;

    public event Action<int>? IntervalChanged;

    public DeviceDetailsForm(Func<Task>? refreshAction)
    {
        _refreshAction = refreshAction;
        Text = "无线外设电量";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 600);
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        _summary = new Label
        {
            AutoSize = false,
            Location = new Point(20, 54),
            Size = new Size(460, 24),
            ForeColor = Color.FromArgb(95, 101, 110),
            BackColor = Color.Transparent,
            Text = "正在读取…"
        };

        _list = new FlowLayoutPanel
        {
            Location = new Point(16, 92),
            Size = new Size(468, 430),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(247, 248, 250),
            Padding = new Padding(0, 0, 6, 0)
        };
        _list.SizeChanged += (_, _) => ResizeCards();

        _footer = new Label
        {
            AutoSize = false,
            Location = new Point(153, 562),
            Size = new Size(140, 20),
            ForeColor = Color.FromArgb(130, 135, 142),
            Text = "仅本机读取"
        };

        _autoStartCheck = new CheckBox
        {
            Text = "开机自启动",
            AutoSize = false,
            Location = new Point(20, 559),
            Size = new Size(125, 24),
            ForeColor = Color.FromArgb(80, 86, 94),
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.System
        };
        _autoStartCheck.CheckedChanged += OnAutoStartCheckedChanged;

        _refreshButton = new Button
        {
            Text = "立即刷新",
            Location = new Point(300, 559),
            Size = new Size(86, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.System
        };
        _refreshButton.Click += async (_, _) => await RefreshAsync();

        _intervalBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(394, 559),
            Size = new Size(86, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.System
        };
        _intervalBox.Items.AddRange(
        [
            new IntervalItem(30, "30 秒"),
            new IntervalItem(60, "1 分钟"),
            new IntervalItem(120, "2 分钟"),
            new IntervalItem(300, "5 分钟"),
            new IntervalItem(600, "10 分钟"),
            new IntervalItem(1800, "30 分钟")
        ]);
        _intervalBox.SelectedIndexChanged += (_, _) =>
        {
            if (_intervalBox.SelectedItem is IntervalItem item)
            {
                IntervalChanged?.Invoke(item.Seconds);
            }
        };

        Controls.AddRange([_summary, _list, _footer, _autoStartCheck, _refreshButton, _intervalBox]);
        AlignFooterControls();
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AlignFooterControls();
        SyncAutoStartCheck();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        AlignFooterControls();
    }

    private void AlignFooterControls()
    {
        if (!_intervalBox.IsHandleCreated)
        {
            return;
        }

        // ComboBox 的高度由字体/DPI 决定，Size 里的高度不一定生效；
        // 以它运行时的实际高度为准，保证底部控件上下边界一致。
        _refreshButton.Top = _intervalBox.Top;
        _refreshButton.Height = _intervalBox.Height;
        _autoStartCheck.Top = _intervalBox.Top;
        _autoStartCheck.Height = _intervalBox.Height;
        _footer.Top = _intervalBox.Top +
            Math.Max(0, (_intervalBox.Height - _footer.Height) / 2);
    }

    private void SyncAutoStartCheck()
    {
        _syncingAutoStart = true;
        try
        {
            _autoStartCheck.Checked = AutoStartManager.IsEnabled();
        }
        finally
        {
            _syncingAutoStart = false;
        }
    }

    private void OnAutoStartCheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingAutoStart)
        {
            return;
        }

        bool enable = _autoStartCheck.Checked;
        if (AutoStartManager.TrySetEnabled(enable, out string? error))
        {
            return;
        }

        SyncAutoStartCheck();
        MessageBox.Show(
            $"无法{(enable ? "开启" : "关闭")}开机自启动：\n{error}",
            "无线外设电量",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, Width, 86),
            Color.White,
            Color.FromArgb(238, 243, 248),
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, 0, 0, Width, 86);
        using var line = new Pen(Color.FromArgb(228, 231, 235));
        e.Graphics.DrawLine(line, 0, 85, Width, 85);

        using var titleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold,
            GraphicsUnit.Point);
        TextRenderer.DrawText(e.Graphics, "设备电量", titleFont,
            new Rectangle(20, 14, 300, 34), Color.FromArgb(28, 31, 36),
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            BeginInvoke(new Action(Hide));
        }
    }

    public void UpdateResult(MonitorResult result, int refreshSeconds)
    {
        _result = result;
        if (IsDisposed)
        {
            return;
        }

        string summary = result.Count == 0
            ? $"{result.Message ?? "未发现受支持的设备"} · 上次更新 {result.UpdatedAt:HH:mm:ss}"
            : $"{result.OnlineCount}/{result.Count} 台在线 · 最低 " +
              (result.MinimumLevel is null ? "未知" : $"{result.MinimumLevel}%") +
              $" · 上次更新 {result.UpdatedAt:HH:mm:ss}" +
              (result.Devices.Any(d => d.IsStale) ? " · 含上次读数" : "");
        if (result.IsDemo)
        {
            summary += " · 演示模式";
        }

        _summary.Text = summary;

        while (_cards.Count > 0)
        {
            DeviceCard card = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            _list.Controls.Remove(card);
            card.Dispose();
        }

        if (result.Devices.Count == 0)
        {
            var empty = new Label
            {
                AutoSize = false,
                Size = new Size(Math.Max(300, _list.ClientSize.Width - 26), 80),
                Text = result.Message ?? "未发现受支持的无线外设。\r\n" +
                    "请确认接收器已插入、外设已开机并完成配对。",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(115, 120, 128),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            };
            _list.Controls.Add(empty);
        }
        else
        {
            foreach (var device in result.Devices)
            {
                var card = new DeviceCard(device)
                {
                    Width = Math.Max(320, _list.ClientSize.Width - 26),
                    Height = ScaleForDpi(66),
                    Margin = new Padding(0, 0, 0, ScaleForDpi(4))
                };
                _cards.Add(card);
                _list.Controls.Add(card);
            }
        }

        SetInterval(refreshSeconds);
    }

    private int ScaleForDpi(int pixels)
    {
        return (int)Math.Round(pixels * DeviceDpi / 96.0);
    }

    public void SetInterval(int seconds)
    {
        for (int i = 0; i < _intervalBox.Items.Count; i++)
        {
            if (_intervalBox.Items[i] is IntervalItem item && item.Seconds == seconds)
            {
                if (_intervalBox.SelectedIndex != i)
                {
                    _intervalBox.SelectedIndex = i;
                }

                return;
            }
        }
    }

    public void SetRefreshing(bool refreshing)
    {
        _refreshButton.Enabled = !refreshing;
        _refreshButton.Text = refreshing ? "刷新中…" : "立即刷新";
    }

    private async Task RefreshAsync()
    {
        if (_refreshAction is null)
        {
            return;
        }

        SetRefreshing(true);
        try
        {
            await _refreshAction();
        }
        finally
        {
            SetRefreshing(false);
        }
    }

    private void ResizeCards()
    {
        int width = Math.Max(320, _list.ClientSize.Width - 26);
        foreach (var card in _cards)
        {
            card.Width = width;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var card in _cards)
            {
                card.Dispose();
            }

            _cards.Clear();
        }

        base.Dispose(disposing);
    }

    private sealed record IntervalItem(int Seconds, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class DeviceCard : Control
{
    private readonly PeripheralReading _reading;
    private Bitmap _icon;
    private int _iconSize;
    private readonly Font _nameFont;
    private readonly Font _percentFont;
    private readonly Font _smallFont;

    public DeviceCard(PeripheralReading reading)
    {
        _reading = reading;
        _nameFont = new Font("Microsoft YaHei UI", 9.8F, FontStyle.Bold, GraphicsUnit.Point);
        _percentFont = new Font("Segoe UI", 15.5F, FontStyle.Bold, GraphicsUnit.Point);
        _smallFont = new Font("Microsoft YaHei UI", 7.9F, FontStyle.Regular, GraphicsUnit.Point);
        _icon = BatteryIconRenderer.CreateBitmap(
            reading.Level, 34, reading.Charging == true,
            reading.Key.StartsWith("BT:", StringComparison.Ordinal) ||
            reading.Source.Contains("蓝牙", StringComparison.Ordinal));
        _iconSize = 34;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(247, 248, 250);
    }

    private float DpiScale => DeviceDpi / 96f;

    private int Px(float value) => (int)Math.Round(value * DpiScale);

    private void EnsureIcon()
    {
        int size = Math.Max(20, Px(34));
        if (_iconSize == size)
        {
            return;
        }

        _icon.Dispose();
        _icon = BatteryIconRenderer.CreateBitmap(
            _reading.Level, size, _reading.Charging == true,
            _reading.Key.StartsWith("BT:", StringComparison.Ordinal) ||
            _reading.Source.Contains("蓝牙", StringComparison.Ordinal));
        _iconSize = size;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Px(66);
        Margin = new Padding(0, 0, 0, Px(4));
        EnsureIcon();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        EnsureIcon();

        float s = DpiScale;
        var card = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(card, 10 * s);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Color.FromArgb(228, 232, 237), Math.Max(1f, s));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        graphics.DrawImageUnscaled(_icon, Px(13), Px(16));

        int textLeft = Px(62);
        int rightPadding = Px(14);
        int percentWidth = Px(84);
        var nameRect = new Rectangle(textLeft, Px(5),
            Math.Max(Px(60), Width - textLeft - percentWidth - rightPadding), Px(22));
        TextRenderer.DrawText(graphics, _reading.DisplayName, _nameFont, nameRect,
            Color.FromArgb(32, 35, 40),
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        string percentText = _reading.Level is null ? "—" : $"{_reading.Level}%";
        Color percentColor = _reading.Level is null
            ? Color.FromArgb(140, 144, 150)
            : LevelTextColor(_reading.Level.Value);
        var percentRect = new Rectangle(
            Width - percentWidth - rightPadding, Px(3), percentWidth, Px(26));
        TextRenderer.DrawText(graphics, percentText, _percentFont, percentRect,
            percentColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter);

        string subtitle = $"{_reading.KindText} · {_reading.Source}";
        if (_reading.Error is not null && _reading.Level is null)
        {
            subtitle = _reading.Error;
        }

        var subtitleRect = new Rectangle(textLeft, Px(27),
            Math.Max(Px(100), Width - textLeft - Px(90) - rightPadding - Px(8)), Px(16));
        TextRenderer.DrawText(graphics, subtitle, _smallFont, subtitleRect,
            Color.FromArgb(116, 122, 130),
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        string status = _reading.Level is null
            ? "未读取"
            : _reading.IsStale
                ? "上次读数"
                : _reading.Charging == true
                    ? "充电中"
                    : _reading.Charging == false
                        ? "放电中"
                        : "已读取";
        var statusRect = new Rectangle(
            Width - Px(90) - rightPadding, Px(27), Px(90), Px(16));
        TextRenderer.DrawText(graphics, status, _smallFont, statusRect,
            _reading.Level is null ? Color.FromArgb(170, 80, 80) : Color.FromArgb(110, 116, 124),
            TextFormatFlags.NoPrefix | TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter);

        int barLeft = textLeft;
        int barTop = Px(51);
        int barWidth = Math.Max(Px(50), Width - barLeft - rightPadding);
        var barBack = new Rectangle(barLeft, barTop, barWidth, Px(6));
        using var barPath = RoundedRect(barBack, 3 * s);
        using var barBrush = new SolidBrush(Color.FromArgb(236, 239, 243));
        graphics.FillPath(barBrush, barPath);

        if (_reading.Level is not null)
        {
            int fillWidth = (int)Math.Round(barWidth * Math.Clamp(_reading.Level.Value / 100.0, 0, 1));
            fillWidth = Math.Max(Px(4), fillWidth);
            var fillRect = new Rectangle(barLeft, barTop, fillWidth, Px(6));
            using var fillPath = RoundedRect(fillRect, 3 * s);
            using var fillBrush = new SolidBrush(BatteryIconRenderer.LevelColor(_reading.Level));
            graphics.FillPath(fillBrush, fillPath);
        }
    }

    private static Color LevelTextColor(int level)
    {
        return level >= 60
            ? Color.FromArgb(25, 135, 75)
            : level >= 20
                ? Color.FromArgb(178, 124, 0)
                : Color.FromArgb(198, 45, 45);
    }

    private static GraphicsPath RoundedRect(Rectangle rectangle, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter,
            diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _nameFont.Dispose();
            _percentFont.Dispose();
            _smallFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

