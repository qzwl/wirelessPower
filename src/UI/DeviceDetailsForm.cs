using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using BatteryTray.Hid;

namespace BatteryTray.UI;

internal sealed class DeviceDetailsForm : Form
{
    private const int HeaderHeight = 92;
    private const int CardHeight = 88;

    private readonly FlowLayoutPanel _list;
    private readonly Label _summary;
    private readonly Label _version;
    private readonly AutoStartToggle _autoStartCheck;
    private readonly RefreshSplitButton _refreshButton;
    private readonly Label _closeButton;
    private readonly Label _minButton;
    private readonly FilterPill _allPill;
    private readonly FilterPill _onlinePill;
    private readonly FilterPill _offlinePill;
    private readonly List<DeviceCard> _cards = [];
    private readonly Func<Task>? _refreshAction;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private MonitorResult? _result;
    private Bitmap? _titleIcon;
    private string _timeText = "最近更新时间：--:--:--";
    private string _filter;
    private bool _syncingAutoStart;

    public event Action<int>? IntervalChanged;
    public event Action<string>? FilterChanged;
    public event Action? ExitRequested;

    private static string AppVersion()
    {
        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.2.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public DeviceDetailsForm(Func<Task>? refreshAction, string filter = "all")
    {
        _refreshAction = refreshAction;
        _filter = AppSettings.NormalizeFilter(filter);
        Text = "无线外设电量";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 760);
        BackColor = Color.FromArgb(245, 246, 248);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        _closeButton = MakeCaptionButton("✕", new Point(520, 16));
        _closeButton.Click += (_, _) =>
        {
            Action? exit = ExitRequested;
            if (exit is not null)
            {
                // 延迟到消息循环空闲再退出，避免在窗体自身事件里重复关闭。
                BeginInvoke(new Action(exit));
            }
            else
            {
                Close();
            }
        };
        _minButton = MakeCaptionButton("—", new Point(480, 16));
        _minButton.Click += (_, _) => Hide();

        _summary = new Label
        {
            AutoSize = false,
            Location = new Point(24, 72),
            Size = new Size(280, 20),
            Visible = false,
            ForeColor = Color.FromArgb(96, 105, 118),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "正在读取…"
        };

        _autoStartCheck = new AutoStartToggle
        {
            Location = new Point(294, 105),
            Size = new Size(96, 28)
        };
        _autoStartCheck.Toggled += OnAutoStartToggled;

        // 刷新与定时间隔整合到同一个按钮：点左侧立即刷新，点右侧箭头选定时周期。
        _refreshButton = new RefreshSplitButton
        {
            Location = new Point(398, 105),
            Size = new Size(132, 28)
        };
        _refreshButton.RefreshClicked += async (_, _) => await RefreshAsync();
        _refreshButton.IntervalChanged += seconds => IntervalChanged?.Invoke(seconds);

        _allPill = new FilterPill("all", "全部") { Location = new Point(24, 104), Size = new Size(82, 30) };
        _onlinePill = new FilterPill("online", "在线") { Location = new Point(112, 104), Size = new Size(84, 30) };
        _offlinePill = new FilterPill("offline", "离线") { Location = new Point(202, 104), Size = new Size(84, 30) };
        _allPill.Click += (_, _) => ApplyFilter("all");
        _onlinePill.Click += (_, _) => ApplyFilter("online");
        _offlinePill.Click += (_, _) => ApplyFilter("offline");

        _list = new FlowLayoutPanel
        {
            Location = new Point(24, 144),
            Size = new Size(506, 582),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(245, 246, 248),
            Padding = new Padding(0, 2, 0, 0)
        };
        _list.SizeChanged += (_, _) => ResizeCards();
        _list.Paint += (_, e) => e.Graphics.Clear(Color.FromArgb(245, 246, 248));

        _version = new Label
        {
            AutoSize = false,
            Location = new Point(24, 734),
            Size = new Size(512, 20),
            ForeColor = Color.FromArgb(163, 170, 180),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "v" + AppVersion() + " · 基于 MrZQAQ 的 wirelessPower 项目优化而来"
        };

        Controls.AddRange(
        [
            _closeButton, _minButton, _summary, _allPill, _onlinePill, _offlinePill,
            _list, _autoStartCheck, _refreshButton, _version
        ]);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        UpdatePills();
        UpdateRegion();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => InvalidateStatus();
        _statusTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    private Label MakeCaptionButton(string text, Point location)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = false,
            Location = location,
            Size = new Size(32, 32),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(214, 222, 232),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            Cursor = Cursors.Hand
        };
        label.MouseEnter += (_, _) => label.ForeColor = Color.White;
        label.MouseLeave += (_, _) => label.ForeColor = Color.FromArgb(214, 222, 232);
        return label;
    }

    private void UpdateRegion()
    {
        if (WindowState != FormWindowState.Normal || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = RoundRect(new Rectangle(0, 0, Width, Height), 12);
        Region? old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private void InvalidateStatus()
    {
        if (IsDisposed)
        {
            return;
        }

        foreach (var card in _cards)
        {
            card.Invalidate();
        }
    }

    private void ApplyFilter(string mode)
    {
        mode = AppSettings.NormalizeFilter(mode);
        if (_filter == mode)
        {
            return;
        }

        _filter = mode;
        UpdatePills();
        RenderDevices();
        FilterChanged?.Invoke(mode);
    }

    public void SetFilter(string mode)
    {
        mode = AppSettings.NormalizeFilter(mode);
        if (_filter == mode)
        {
            UpdatePills();
            return;
        }

        _filter = mode;
        UpdatePills();
        RenderDevices();
    }

    private void UpdatePills()
    {
        int online = _result?.Devices.Count(d => d.Level is not null && !d.IsOffline) ?? 0;
        int offline = _result?.Devices.Count(d => d.IsOffline) ?? 0;
        int total = _result?.Devices.Count ?? 0;
        _allPill.SetState(_filter == "all", total);
        _onlinePill.SetState(_filter == "online", online);
        _offlinePill.SetState(_filter == "offline", offline);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateRegion();
        SyncAutoStartCheck();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
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

    private void OnAutoStartToggled(object? sender, EventArgs e)
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
            "蓝牙设备电量",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y <= HeaderHeight)
        {
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // 深色标题栏，让窗口与系统其他浅色窗口明显区分。
        var header = new Rectangle(0, 0, Width, HeaderHeight);
        using var brush = new LinearGradientBrush(
            header,
            Color.FromArgb(44, 54, 70),
            Color.FromArgb(29, 37, 50),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(brush, header);

        // 图标与标题作为一个整体在标题栏内水平居中，不再靠左悬挂。
        _titleIcon ??= BatteryIconRenderer.CreateBitmap(75, 32, false, true);
        using var titleFont = new Font("Microsoft YaHei UI", 15.5F, FontStyle.Bold,
            GraphicsUnit.Point);
        const string titleText = "无线外设电量";
        int titleWidth = TextRenderer.MeasureText(titleText, titleFont).Width;
        const int iconSize = 26;
        const int iconGap = 10;
        int groupWidth = iconSize + iconGap + titleWidth;
        int groupLeft = Math.Max(24, (Width - groupWidth) / 2);

        graphics.DrawImage(_titleIcon,
            new Rectangle(groupLeft, 22, iconSize, iconSize));
        TextRenderer.DrawText(graphics, titleText, titleFont,
            new Rectangle(groupLeft + iconSize + iconGap, 20, titleWidth + 6, 30),
            Color.White,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);

        // 第二行三段并排：数据来源 / 当前设备中最低电量 / 最近更新时间。
        using var metaFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular,
            GraphicsUnit.Point);
        int metaY = 64;

        (string Text, Color Ink)[] meta =
        [
            ("数据来源均为本机实际读取", Color.FromArgb(168, 182, 200)),
            (_timeText, Color.FromArgb(146, 162, 184))
        ];

        // 先量出总宽，让整行也居中，避免左侧悬空。
        int dotWidth = TextRenderer.MeasureText("·", metaFont).Width;
        int totalWidth = 0;
        for (int i = 0; i < meta.Length; i++)
        {
            totalWidth += TextRenderer.MeasureText(meta[i].Text, metaFont).Width;
            if (i > 0)
            {
                totalWidth += dotWidth + 12;
            }
        }

        int x = Math.Max(24, (Width - totalWidth) / 2);
        int metaRight = Width - 24;

        for (int i = 0; i < meta.Length; i++)
        {
            if (i > 0)
            {
                TextRenderer.DrawText(graphics, "·", metaFont,
                    new Rectangle(x, metaY, dotWidth + 2, 22), Color.FromArgb(112, 128, 150),
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
                x += dotWidth + 12;
            }

            int width = TextRenderer.MeasureText(meta[i].Text, metaFont).Width;
            if (x + width > metaRight)
            {
                // 空间不够时宁可截断也不压到右侧按钮上。
                width = Math.Max(0, metaRight - x);
            }

            TextRenderer.DrawText(graphics, meta[i].Text, metaFont,
                new Rectangle(x, metaY, width, 22), meta[i].Ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            x += width;
        }

        // 窗口外描边：无边框窗口若没有边界会显得融进桌面。
        // 用与 Region 相匹配的半径，避免圆角处被裁掉造成边框看起来断裂。
        using var borderPath = RoundRect(new Rectangle(1, 1, Width - 2, Height - 2), 11.5f);
        using var borderPen = new Pen(Color.FromArgb(156, 167, 183), 1f);
        graphics.DrawPath(borderPen, borderPath);
    }

    public void UpdateResult(MonitorResult result, int refreshSeconds)
    {
        _result = result;
        if (IsDisposed)
        {
            return;
        }

        DateTime lastUpdate = result.LastSuccessAt == DateTime.MinValue
            ? result.UpdatedAt
            : result.LastSuccessAt;
        string summary = result.Count == 0
            ? result.Message ?? "未发现受支持的设备"
            : "当前设备中最低电量：" +
              (result.MinimumLevel is null ? "未知" : $"{result.MinimumLevel}%");
        _timeText = $"最近更新时间：{lastUpdate:HH:mm:ss}";
        _summary.Text = summary;
        Invalidate();
        UpdatePills();
        RenderDevices();
        SetInterval(refreshSeconds);
    }

    private IEnumerable<PeripheralReading> FilterDevices()
    {
        if (_result is null)
        {
            return [];
        }

        return _filter switch
        {
            "online" => _result.Devices.Where(d => d.Level is not null && !d.IsOffline),
            "offline" => _result.Devices.Where(d => d.IsOffline),
            _ => _result.Devices
        };
    }

    private void RenderDevices()
    {
        while (_list.Controls.Count > 0)
        {
            var control = _list.Controls[0];
            _list.Controls.Remove(control);
            control.Dispose();
        }

        _cards.Clear();

        var devices = FilterDevices().ToList();
        if (devices.Count == 0)
        {
            string text = _result is null || _result.Devices.Count == 0
                ? _result?.Message ?? "未发现受支持的无线外设。\r\n请确认接收器已插入、外设已开机并完成配对。"
                : _filter == "online"
                    ? "当前没有在线设备。"
                    : "当前没有离线设备。";
            var empty = new Label
            {
                AutoSize = false,
                Size = new Size(Math.Max(300, _list.ClientSize.Width - 26), 100),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(140, 147, 158),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            };
            _list.Controls.Add(empty);
            return;
        }

        foreach (var device in devices)
        {
            var card = new DeviceCard(device)
            {
                Width = CardWidth(devices.Count),
                Height = ScaleForDpi(CardHeight),
                Margin = new Padding(0, 0, 0, ScaleForDpi(8))
            };
            _cards.Add(card);
            _list.Controls.Add(card);
        }
    }

    private int ScaleForDpi(int pixels)
    {
        return (int)Math.Round(pixels * DeviceDpi / 96.0);
    }

    public void SetInterval(int seconds)
    {
        _refreshButton.SetCurrent(seconds);
    }

    public void SetRefreshing(bool refreshing)
    {
        _refreshButton.SetBusy(refreshing);
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
        int width = CardWidth(_cards.Count);
        foreach (var card in _cards)
        {
            card.Width = width;
        }
    }

    /// <summary>
    /// 卡片宽度跟随列表客户区：设备不超过 6 台时正好贴到“立即刷新”右边界；
    /// 需要竖向滚动条时让出滚动条宽度，避免卡片被压在滚动条下面、也不触发横向滚动条。
    /// </summary>
    private int CardWidth(int count)
    {
        int width = _list.ClientSize.Width - _list.Padding.Horizontal;
        int cardHeight = ScaleForDpi(CardHeight);
        int gap = ScaleForDpi(8);
        int needed = Math.Max(1, count) * (cardHeight + gap) + _list.Padding.Vertical;
        if (needed > _list.ClientSize.Height)
        {
            width -= SystemInformation.VerticalScrollBarWidth;
        }

        return Math.Max(320, width);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer?.Dispose();
            _titleIcon?.Dispose();
            _titleIcon = null;
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

    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;
    private const int CsDropShadow = 0x00020000;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    internal static GraphicsPath RoundRect(Rectangle rectangle, float radius)
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
}

/// <summary>筛选按钮（圆角胶囊，选中高亮）。</summary>
internal sealed class FilterPill : Control
{
    private readonly string _label;

    public FilterPill(string key, string label)
    {
        FilterKey = key;
        _label = label;
        Size = new Size(88, 30);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public string FilterKey { get; }

    public bool Selected { get; private set; }

    public int Count { get; private set; }

    public void SetState(bool selected, int count)
    {
        Selected = selected;
        Count = count;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = DeviceDetailsForm.RoundRect(rect, Height / 2f);
        if (Selected)
        {
            using var fill = new SolidBrush(Color.FromArgb(42, 106, 240));
            graphics.FillPath(fill, path);
        }
        else
        {
            using var fill = new SolidBrush(Color.White);
            graphics.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(222, 227, 234));
            graphics.DrawPath(border, path);
        }

        string text = $"{_label} {Count}";
        using var font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(graphics, text, font, ClientRectangle,
            Selected ? Color.White : Color.FromArgb(96, 103, 112),
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>刷新 + 定时间隔的组合按钮：左半立即刷新，右半箭头弹出间隔菜单。</summary>
internal sealed class RefreshSplitButton : Control
{
    private const int ArrowWidth = 34;

    private static readonly (int Seconds, string Label)[] Options =
    [
        (30, "30 秒"),
        (60, "1 分钟"),
        (120, "2 分钟"),
        (300, "5 分钟"),
        (600, "10 分钟"),
        (1800, "30 分钟")
    ];

    private readonly ToolTip _tip = new();
    private bool _hoverMain;
    private bool _hoverArrow;
    private bool _pressed;
    private bool _busy;
    private int _seconds = 60;
    private ContextMenuStrip? _intervalMenu;

    public event EventHandler? RefreshClicked;
    public event Action<int>? IntervalChanged;

    public RefreshSplitButton()
    {
        Cursor = Cursors.Hand;
        Size = new Size(176, 28);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(245, 246, 248);
        UpdateTip();
    }

    public void SetCurrent(int seconds)
    {
        _seconds = seconds;
        UpdateTip();
        Invalidate();
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        Invalidate();
    }

    private string CurrentLabel()
    {
        foreach (var option in Options)
        {
            if (option.Seconds == _seconds)
            {
                return option.Label;
            }
        }

        return "自定义";
    }

    private void UpdateTip()
    {
        _tip.SetToolTip(this, $"点左侧立即刷新；点右侧箭头设置定时间隔（当前：{CurrentLabel()}）");
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool onArrow = e.X >= Width - ArrowWidth;
        if (onArrow != _hoverArrow)
        {
            _hoverArrow = onArrow;
            _hoverMain = !onArrow;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverMain = false;
        _hoverArrow = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
        if (_busy)
        {
            return;
        }

        if (e.X >= Width - ArrowWidth)
        {
            ShowIntervalMenu();
        }
        else
        {
            RefreshClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowIntervalMenu()
    {
        // 菜单复用一个实例，不在 Closed 里 Dispose：
        // WinForms 在关闭后会继续访问菜单对象，提前释放会抛
        // "Cannot access a disposed object: ContextMenuStrip"。
        if (_intervalMenu is null)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.White,
                Renderer = new ModernMenuRenderer(),
                Padding = new Padding(4, 6, 4, 6)
            };
            foreach (var option in Options)
            {
                var item = new ToolStripMenuItem(option.Label)
                {
                    Padding = new Padding(0, 2, 0, 2)
                };
                item.Click += (_, _) =>
                {
                    _seconds = option.Seconds;
                    UpdateTip();
                    UpdateMenuChecks();
                    Invalidate();
                    IntervalChanged?.Invoke(option.Seconds);
                };
                menu.Items.Add(item);
            }

            _intervalMenu = menu;
        }

        UpdateMenuChecks();
        _intervalMenu.Show(this, new Point(Math.Max(0, Width - 118), Height + 2));
    }

    private void UpdateMenuChecks()
    {
        if (_intervalMenu is null)
        {
            return;
        }

        for (int i = 0; i < Options.Length && i < _intervalMenu.Items.Count; i++)
        {
            if (_intervalMenu.Items[i] is ToolStripMenuItem item)
            {
                item.Checked = Options[i].Seconds == _seconds;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = DeviceDetailsForm.RoundRect(rect, 7);
        Color fill = _busy
            ? Color.FromArgb(150, 175, 215)
            : _pressed
                ? Color.FromArgb(28, 84, 204)
                : _hoverMain || _hoverArrow
                    ? Color.FromArgb(58, 120, 246)
                    : Color.FromArgb(42, 106, 240);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);

        int mainWidth = Width - ArrowWidth;

        using var font = new Font("Microsoft YaHei UI", 8.9F, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(graphics, _busy ? "刷新中…" : "立即刷新", font,
            new Rectangle(0, 0, mainWidth, Height), Color.White,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        using var divider = new Pen(Color.FromArgb(70, 255, 255, 255));
        graphics.DrawLine(divider, mainWidth, 5, mainWidth, Height - 5);

        if (_hoverArrow && !_busy)
        {
            using var hoverPath = DeviceDetailsForm.RoundRect(
                new Rectangle(mainWidth + 2, 2, ArrowWidth - 4, Height - 4), 5);
            using var hoverFill = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            graphics.FillPath(hoverFill, hoverPath);
        }

        using var arrowFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(graphics, "▾", arrowFont,
            new Rectangle(mainWidth, 0, ArrowWidth, Height), Color.White,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _intervalMenu?.Dispose();
            _intervalMenu = null;
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>开机自启开关：按钮式，右侧圆点指示灯，开启时亮绿灯。</summary>
internal sealed class AutoStartToggle : Control
{
    private bool _checked;
    private bool _hover;
    private bool _pressed;

    public event EventHandler? Toggled;

    public AutoStartToggle()
    {
        Cursor = Cursors.Hand;
        Size = new Size(142, 28);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(245, 246, 248);
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
        if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location))
        {
            return;
        }

        Checked = !Checked;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = DeviceDetailsForm.RoundRect(rect, 7);

        Color fill = _pressed
            ? Color.FromArgb(238, 242, 248)
            : _hover
                ? Color.FromArgb(250, 251, 253)
                : Color.White;
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);

        Color border = _checked
            ? Color.FromArgb(42, 106, 240)
            : _hover
                ? Color.FromArgb(190, 200, 214)
                : Color.FromArgb(216, 222, 230);
        using var pen = new Pen(border);
        graphics.DrawPath(pen, path);

        using var font = new Font("Microsoft YaHei UI", 8.9F, FontStyle.Bold, GraphicsUnit.Point);
        TextRenderer.DrawText(graphics, "开机自启", font,
            new Rectangle(10, 0, Width - 32, Height),
            _checked ? Color.FromArgb(26, 32, 44) : Color.FromArgb(112, 120, 132),
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine);

        // 状态指示灯：开启亮绿，关闭为灰。
        float cx = Width - 20;
        float cy = Height / 2f;
        float radius = 5f;
        if (_checked)
        {
            using var glow = new SolidBrush(Color.FromArgb(52, 34, 197, 94));
            graphics.FillEllipse(glow, cx - radius - 3, cy - radius - 3,
                (radius + 3) * 2, (radius + 3) * 2);
            using var lamp = new SolidBrush(Color.FromArgb(34, 197, 94));
            graphics.FillEllipse(lamp, cx - radius, cy - radius, radius * 2, radius * 2);
            using var shine = new SolidBrush(Color.FromArgb(255, 255, 255, 150));
            graphics.FillEllipse(shine, cx - radius + 1.2f, cy - radius + 1.2f,
                radius * 0.9f, radius * 0.9f);
        }
        else
        {
            using var off = new SolidBrush(Color.FromArgb(205, 212, 222));
            graphics.FillEllipse(off, cx - radius, cy - radius, radius * 2, radius * 2);
            using var ring = new Pen(Color.FromArgb(232, 237, 243));
            graphics.DrawEllipse(ring, cx - radius, cy - radius, radius * 2, radius * 2);
        }
    }
}

internal sealed class DeviceCard : Control
{
    private readonly PeripheralReading _reading;
    private readonly Font _nameFont;
    private readonly Font _percentFont;
    private readonly Font _smallFont;

    public DeviceCard(PeripheralReading reading)
    {
        _reading = reading;
        _nameFont = new Font("Microsoft YaHei UI", 10.3F, FontStyle.Bold, GraphicsUnit.Point);
        _percentFont = new Font("Segoe UI", 16.5F, FontStyle.Bold, GraphicsUnit.Point);
        _smallFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(245, 246, 248);
    }

    private float DpiScale => DeviceDpi / 96f;

    private int Px(float value) => (int)Math.Round(value * DpiScale);

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = (int)Math.Round(88 * DpiScale);
        Margin = new Padding(0, 0, 0, Px(8));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);

        float s = DpiScale;
        bool offline = _reading.IsOffline;
        var card = new Rectangle(0, 0, Width - 1, Height - 1);

        using var path = DeviceDetailsForm.RoundRect(card, 12 * s);
        using var fill = new SolidBrush(offline ? Color.FromArgb(251, 252, 253) : Color.White);
        using var border = new Pen(Color.FromArgb(224, 229, 236), Math.Max(1f, s));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        Color accent = offline
            ? Color.FromArgb(203, 213, 225)
            : Color.FromArgb(34, 197, 94);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, 0, Px(18), Math.Max(3, Px(3.5f)), Height - Px(36));

        int badgeWidth = Px(82);
        int badgeHeight = Px(40);
        var badgeRect = new Rectangle(Px(16), (Height - badgeHeight) / 2, badgeWidth, badgeHeight);
        VendorIcons.Draw(graphics, badgeRect, _reading.DisplayName, _reading.Kind);

        int textLeft = Px(112);
        int rightPadding = Px(18);
        int percentWidth = Px(92);

        Color nameColor = offline ? Color.FromArgb(154, 160, 168) : Color.FromArgb(28, 32, 38);
        var nameRect = new Rectangle(textLeft, Px(10),
            Math.Max(Px(60), Width - textLeft - percentWidth - rightPadding), Px(24));
        TextRenderer.DrawText(graphics, _reading.DisplayName, _nameFont, nameRect, nameColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        string subtitle = $"{_reading.KindText} · {_reading.Source}";
        if (_reading.Error is not null && _reading.Level is null && !offline)
        {
            subtitle = _reading.Error;
        }

        var subtitleRect = new Rectangle(textLeft, Px(36),
            Math.Max(Px(100), Width - textLeft - Px(150) - rightPadding), Px(16));
        TextRenderer.DrawText(graphics, subtitle, _smallFont, subtitleRect,
            Color.FromArgb(140, 147, 156),
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        string percentText = _reading.Level is null ? "—" : $"{_reading.Level}%";
        Color percentColor = _reading.Level is null
            ? Color.FromArgb(158, 163, 170)
            : LevelTextColor(_reading.Level.Value);
        var percentRect = new Rectangle(
            Width - percentWidth - rightPadding, Px(9), percentWidth, Px(30));
        TextRenderer.DrawText(graphics, percentText, _percentFont, percentRect, percentColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter);

        string status = offline
            ? $"离线 {FormatDuration(DateTime.Now - _reading.LastSeenAt)}"
            : _reading.Level is null
                ? "未读取"
                : _reading.IsStale
                    ? "上次读数"
                    : _reading.Charging == true
                        ? $"充电中 · 在线 {FormatDuration(DateTime.Now - _reading.OnlineSinceOrUpdated)}"
                        : $"{(_reading.Charging == false ? "放电中 · " : "")}在线 {FormatDuration(DateTime.Now - _reading.OnlineSinceOrUpdated)}";
        Color statusColor = offline
            ? Color.FromArgb(150, 158, 168)
            : _reading.Level is null
                ? Color.FromArgb(220, 90, 90)
                : Color.FromArgb(34, 150, 92);
        var statusRect = new Rectangle(
            Width - Px(224) - rightPadding, Px(70), Px(224), Px(15));
        TextRenderer.DrawText(graphics, "● " + status, _smallFont, statusRect, statusColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter);

        int barLeft = textLeft;
        int barTop = Px(58);
        int barWidth = Math.Max(Px(50), Width - barLeft - rightPadding);
        var barBack = new Rectangle(barLeft, barTop, barWidth, Px(6));
        using var barPath = DeviceDetailsForm.RoundRect(barBack, 3f * s);
        using var barBrush = new SolidBrush(Color.FromArgb(234, 237, 242));
        graphics.FillPath(barBrush, barPath);

        if (_reading.Level is not null)
        {
            int fillWidth = (int)Math.Round(
                barWidth * Math.Clamp(_reading.Level.Value / 100.0, 0, 1));
            fillWidth = Math.Max(Px(5), fillWidth);
            var fillRect = new Rectangle(barLeft, barTop, fillWidth, Px(6));
            using var fillPath = DeviceDetailsForm.RoundRect(fillRect, 3f * s);
            Color baseColor = BatteryIconRenderer.LevelColor(_reading.Level);
            using var fillBrush = new LinearGradientBrush(
                fillRect,
                Lighten(baseColor, 0.18f),
                baseColor,
                LinearGradientMode.Horizontal);
            graphics.FillPath(fillBrush, fillPath);
        }
    }

    private static Color Lighten(Color color, float amount)
    {
        int r = color.R + (int)((255 - color.R) * amount);
        int g = color.G + (int)((255 - color.G) * amount);
        int b = color.B + (int)((255 - color.B) * amount);
        return Color.FromArgb(color.A, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
    }

    private static Color LevelTextColor(int level)
    {
        // 与托盘提醒阈值统一：>=20% 正常、10~19% 警告、<10% 紧急。
        return level >= 20
            ? Color.FromArgb(22, 140, 78)
            : level >= 10
                ? Color.FromArgb(178, 124, 0)
                : Color.FromArgb(198, 48, 48);
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d < TimeSpan.Zero)
        {
            d = TimeSpan.Zero;
        }

        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{(int)d.TotalMinutes:D2}:{d.Seconds:D2}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _percentFont.Dispose();
            _smallFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
