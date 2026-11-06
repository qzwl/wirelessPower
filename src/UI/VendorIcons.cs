using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using BatteryTray.Hid;

namespace BatteryTray.UI;

/// <summary>按设备名识别常见厂商，绘制品牌配色 + 缩写徽标；无法识别时按设备类型画通用图形。</summary>
internal static class VendorIcons
{
    private sealed record Rule(string Badge, Color Color, string[] Keywords);

    private static readonly Rule[] Rules =
    [
        new("MS", Color.FromArgb(0, 164, 239), ["microsoft", "微软", "surface"]),
        new("mi", Color.FromArgb(255, 103, 0), ["xiaomi", "小米", "redmi", "红米"]),
        new("HW", Color.FromArgb(199, 0, 11), ["huawei", "华为", "honor", "荣耀"]),
        new("LOGI", Color.FromArgb(0, 168, 224), ["logitech", "罗技"]),
        new("RZ", Color.FromArgb(68, 214, 44), ["razer", "雷蛇"]),
        new("RP", Color.FromArgb(27, 94, 155), ["rapoo", "雷柏"]),
        new("EDF", Color.FromArgb(227, 6, 19), ["edifier", "漫步者"]),
        new("DR", Color.FromArgb(240, 78, 35), ["dareu", "达尔优"]),
        new("PHL", Color.FromArgb(11, 94, 215), ["philips", "飞利浦"]),
        new("LEN", Color.FromArgb(226, 35, 26), ["lenovo", "联想", "thinkpad", "thinkplus"]),
        new("DELL", Color.FromArgb(0, 125, 184), ["dell", "戴尔"]),
        new("SNY", Color.FromArgb(22, 22, 26), ["sony", "索尼"]),
        new("BSE", Color.FromArgb(0, 0, 0), ["bose", "博士"]),
        new("JBL", Color.FromArgb(255, 110, 0), ["jbl"]),
        new("SEN", Color.FromArgb(0, 60, 130), ["sennheiser", "森海塞尔"]),
        new("ANK", Color.FromArgb(0, 186, 186), ["soundcore", "anker", "安克"]),
        new("BSU", Color.FromArgb(62, 62, 68), ["baseus", "倍思"]),
        new("UGN", Color.FromArgb(0, 145, 100), ["ugreen", "绿联"]),
        new("RM", Color.FromArgb(255, 196, 0), ["realme", "真我"]),
        new("OP", Color.FromArgb(0, 130, 60), ["oppo"]),
        new("VVO", Color.FromArgb(65, 90, 190), ["vivo"]),
        new("1+", Color.FromArgb(235, 0, 40), ["oneplus", "一加"]),
        new("MEI", Color.FromArgb(0, 130, 200), ["meizu", "魅族"]),
        new("MSI", Color.FromArgb(200, 0, 0), ["msi", "微星"]),
        new("GIG", Color.FromArgb(0, 110, 185), ["gigabyte", "技嘉", "aorus"]),
        new("BS", Color.FromArgb(0, 0, 0), ["blackshark", "黑鲨"]),
        new("NUB", Color.FromArgb(220, 20, 60), ["nubia", "努比亚"]),
        new("8B", Color.FromArgb(40, 40, 46), ["8bitdo", "八位堂"]),
        new("GS", Color.FromArgb(0, 120, 200), ["gamesir", "盖世小鸡"]),
        new("HP", Color.FromArgb(0, 150, 214), ["hp", "惠普"]),
        new("ASUS", Color.FromArgb(0, 83, 155), ["asus", "华硕", "rog"]),
        new("SAM", Color.FromArgb(20, 40, 160), ["samsung", "三星"]),
        new("SS", Color.FromArgb(255, 81, 0), ["steelseries", "赛睿"]),
        new("SW", Color.FromArgb(46, 134, 171), ["seewo", "希沃"]),
        new("HV", Color.FromArgb(214, 0, 28), ["hitevision", "鸿合"]),
        new("MHB", Color.FromArgb(0, 87, 184), ["maxhub", "视源"]),
        new("IF", Color.FromArgb(18, 102, 255), ["iflytek", "讯飞", "科大讯飞"]),
        new("RJ", Color.FromArgb(0, 166, 81), ["ruijie", "锐捷"]),
        new("BBK", Color.FromArgb(230, 0, 18), ["bbk", "步步高"]),
        new("XT", Color.FromArgb(255, 106, 0), ["小天才", "imoo", "xtc"]),
        new("RB", Color.FromArgb(243, 152, 0), ["readboy", "读书郎"]),
        new("YX", Color.FromArgb(30, 136, 229), ["优学派", "youxuepai", "noah"]),
        new("JY", Color.FromArgb(31, 111, 235), ["jiyu", "极域"]),
        new("HS", Color.FromArgb(11, 91, 158), ["hisense", "海信"]),
        new("TCL", Color.FromArgb(230, 0, 18), ["tcl"]),
        new("SKW", Color.FromArgb(0, 153, 204), ["skyworth", "创维"]),
        new("CH", Color.FromArgb(228, 0, 43), ["changhong", "长虹"]),
        new("ATK", Color.FromArgb(46, 58, 89), ["atk"]),
        new("A4", Color.FromArgb(166, 38, 38), ["a4tech", "双飞燕", "血手"]),
        new("KC", Color.FromArgb(40, 40, 44), ["keychron"]),
        new("ROC", Color.FromArgb(0, 0, 0), ["roccat", "冰豹"]),
        new("TH", Color.FromArgb(0, 110, 185), ["thunderobot", "雷神"]),
        new("MC", Color.FromArgb(20, 90, 170), ["machenike", "机械师"]),
        new("VXE", Color.FromArgb(255, 122, 26), ["vxe"]),
        new("Nj", Color.FromArgb(0, 122, 255), ["ninjutso", "忍"]),
        new("DKR", Color.FromArgb(30, 34, 44), ["darkrim"]),
        new("COR", Color.FromArgb(0, 0, 0), ["corsair", "海盗船"]),
        new("HX", Color.FromArgb(196, 0, 30), ["hyperx", "极度未知"]),
        new("APL", Color.FromArgb(0, 0, 0), ["iphone", "ipad", "airpods", "apple"]),
        new("AMD", Color.FromArgb(0, 0, 0), ["amd", "ryzen", "radeon"]),
        new("EPS", Color.FromArgb(0, 86, 179), ["epson", "爱普生"]),
        new("TSB", Color.FromArgb(255, 0, 0), ["toshiba", "东芝"]),
        new("TP", Color.FromArgb(78, 167, 214), ["tp-link", "tplink", "普联"]),
        new("HIK", Color.FromArgb(213, 0, 20), ["hikvision", "海康"]),
        new("DH", Color.FromArgb(0, 120, 200), ["dahua", "大华"]),
        new("ACR", Color.FromArgb(131, 183, 21), ["acer", "宏碁"]),
        new("INT", Color.FromArgb(0, 104, 181), ["intel", "英特尔"]),
        new("NOK", Color.FromArgb(18, 65, 145), ["nokia", "诺基亚"]),
        new("MOT", Color.FromArgb(0, 130, 195), ["motorola", "摩托罗拉", "moto"]),
        new("NG", Color.FromArgb(0, 100, 0), ["netgear", "网件"]),
        new("PS", Color.FromArgb(0, 60, 145), ["playstation", "ps5", "ps4"]),
        new("AW", Color.FromArgb(0, 0, 0), ["alienware", "外星人"]),
        new("FUJ", Color.FromArgb(200, 0, 20), ["fujitsu", "富士通"]),
        new("D-L", Color.FromArgb(0, 120, 200), ["dlink", "d-link", "友讯"])
    ];

    private static readonly Dictionary<string, string> SlugMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xiaomi"] = "xiaomi",
        ["huawei"] = "huawei",
        ["honor"] = "honor",
        ["razer"] = "razer",
        ["sony"] = "sony",
        ["lenovo"] = "lenovo",
        ["dell"] = "dell",
        ["bose"] = "bose",
        ["jbl"] = "jbl",
        ["sennheiser"] = "sennheiser",
        ["oppo"] = "oppo",
        ["vivo"] = "vivo",
        ["oneplus"] = "oneplus",
        ["meizu"] = "meizu",
        ["msi"] = "msi",
        ["asus"] = "asus",
        ["samsung"] = "samsung",
        ["steelseries"] = "steelseries",
        ["corsair"] = "corsair",
        ["hyperx"] = "hyperx",
        ["seewo"] = "seewo",
        ["microsoft"] = "microsoft",
        ["logitech"] = "logitech",
        ["rapoo"] = "rapoo",
        ["ruijie"] = "ruijie",
        ["hitevision"] = "hitevision",
        ["amd"] = "amd",
        ["apple"] = "apple",
        ["iphone"] = "apple",
        ["ipad"] = "apple",
        ["airpods"] = "apple",
        ["epson"] = "epson",
        ["toshiba"] = "toshiba",
        ["tplink"] = "tplink",
        ["tp-link"] = "tplink",
        ["hikvision"] = "hikvision",
        ["dahua"] = "dahua",
        ["nubia"] = "nubia",
        ["hisense"] = "hisense",
        ["skyworth"] = "skyworth",
        ["anker"] = "anker",
        ["soundcore"] = "anker",
        ["acer"] = "acer",
        ["intel"] = "intel",
        ["nokia"] = "nokia",
        ["motorola"] = "motorola",
        ["moto"] = "motorola",
        ["netgear"] = "netgear",
        ["playstation"] = "playstation",
        ["ps5"] = "playstation",
        ["alienware"] = "alienware",
        ["fujitsu"] = "fujitsu",
        ["thinkpad"] = "thinkpad",
        ["hp"] = "hp",
        ["gigabyte"] = "gigabyte",
        ["thunderobot"] = "thunderobot",
        ["changhong"] = "changhong",
        ["dlink"] = "dlink",
        ["d-link"] = "dlink",
        ["a4tech"] = "a4tech"
    };

    private static readonly Dictionary<string, Bitmap?> LogoCache = new();
    private static readonly Assembly Assembly = typeof(VendorIcons).Assembly;

    public static void Draw(Graphics g, Rectangle bounds, string name, PeripheralKind kind)
    {
        Rule? rule = null;
        string n = name.ToLowerInvariant();
        foreach (var r in Rules)
        {
            if (r.Keywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                rule = r;
                break;
            }
        }

        if (rule is not null)
        {
            DrawBrand(g, bounds, rule);
        }
        else
        {
            DrawGlyph(g, bounds, kind);
        }
    }

    /// <summary>
    /// 优先使用内置的品牌矢量 logo（来自 Simple Icons 与品牌官网），保持原始配色与宽高比；
    /// 图标区可以是长方形，横版 logo（如希沃）会按比例缩放居中。
    /// 拿不到 logo 时回退为品牌配色 + 缩写徽标。
    /// </summary>
    private static void DrawBrand(Graphics g, Rectangle bounds, Rule rule)
    {
        float radius = Math.Max(4f, bounds.Height * 0.22f);
        Bitmap? logo = LoadLogo(rule, bounds);
        if (logo is not null)
        {
            using var path = Rounded(bounds, radius);
            using var fill = new SolidBrush(Color.FromArgb(248, 250, 253));
            using var border = new Pen(Color.FromArgb(228, 233, 240));
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            g.DrawImage(logo,
                bounds.X + (bounds.Width - logo.Width) / 2f,
                bounds.Y + (bounds.Height - logo.Height) / 2f,
                logo.Width, logo.Height);
            return;
        }

        using var brandPath = Rounded(bounds, radius);
        using var brandFill = new SolidBrush(rule.Color);
        g.FillPath(brandFill, brandPath);

        bool light = rule.Color.GetBrightness() > 0.62;
        float h = bounds.Height;
        float pt = rule.Badge.Length <= 2
            ? h * 0.46f
            : rule.Badge.Length == 3
                ? h * 0.37f
                : h * 0.3f;
        using var font = new Font("Segoe UI", Math.Max(5f, pt * 0.72f), FontStyle.Bold,
            GraphicsUnit.Point);
        Color text = light ? Color.FromArgb(26, 28, 32) : Color.White;
        TextRenderer.DrawText(g, rule.Badge, font, bounds, text,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }

    private static string? ResolveSlug(Rule rule)
    {
        foreach (var keyword in rule.Keywords)
        {
            if (SlugMap.TryGetValue(keyword, out string? found))
            {
                return found;
            }
        }

        return null;
    }

    private static Bitmap? LoadLogo(Rule rule, Rectangle bounds)
    {
        string? slug = ResolveSlug(rule);
        if (slug is null)
        {
            return null;
        }

        // 图标区已经比旧版长很多，内边距压到最小让 logo 尽量填满；
        // 再用 3 倍尺寸栅格化后裁掉 svg 自带的空白边，避免“看着很小”。
        int pad = Math.Max(1, bounds.Height / 20);
        int maxWidth = Math.Max(8, bounds.Width - pad * 2);
        int maxHeight = Math.Max(8, bounds.Height - pad * 2);
        return LoadLogoBySlug(slug, maxWidth, maxHeight);
    }

    internal static Bitmap? LoadLogoBySlug(string slug, int maxWidth, int maxHeight)
    {
        string key = $"{slug}:{maxWidth}x{maxHeight}";
        if (LogoCache.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            // PNG 优先：官网下载的彩色 logo 比单色 SVG 更接近真实品牌标。
            using (var pngStream = Assembly.GetManifestResourceStream(
                       $"BatteryTray.Assets.brands.{slug}.png"))
            {
                if (pngStream is not null)
                {
                    using var source = new Bitmap(pngStream);
                    bitmap = CropAndFit(source, maxWidth, maxHeight);
                }
            }

            if (bitmap is null)
            {
                using var stream = Assembly.GetManifestResourceStream(
                    $"BatteryTray.Assets.brands.{slug}.svg");
                if (stream is not null)
                {
                    var document = Svg.SvgDocument.Open<Svg.SvgDocument>(stream);
                    float vbWidth = document.ViewBox.Width > 0
                        ? document.ViewBox.Width
                        : Math.Max(1f, document.Width.Value);
                    float vbHeight = document.ViewBox.Height > 0
                        ? document.ViewBox.Height
                        : Math.Max(1f, document.Height.Value);
                    float aspect = vbWidth / vbHeight;

                    int rawWidth;
                    int rawHeight;
                    if (aspect >= (float)maxWidth / maxHeight)
                    {
                        rawWidth = Math.Max(8, maxWidth * 3);
                        rawHeight = Math.Max(8, (int)Math.Round(rawWidth / aspect));
                    }
                    else
                    {
                        rawHeight = Math.Max(8, maxHeight * 3);
                        rawWidth = Math.Max(8, (int)Math.Round(rawHeight * aspect));
                    }

                    using var raw = document.Draw(rawWidth, rawHeight);
                    bitmap = CropAndFit(raw, maxWidth, maxHeight);
                }
            }
        }
        catch
        {
            bitmap = null;
        }

        LogoCache[key] = bitmap;
        return bitmap;
    }

    /// <summary>诊断用：把已登记品牌的 logo 拼成一张图，方便逐个核对是不是抓对了。</summary>
    internal static Bitmap RenderBrandSheet(int cellWidth = 160, int cellHeight = 84, int columns = 6)
    {
        var slugs = SlugMap.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int rows = Math.Max(1, (slugs.Count + columns - 1) / columns);
        var bitmap = new Bitmap(columns * cellWidth, rows * cellHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var cellPen = new Pen(Color.FromArgb(222, 228, 236));
        using var nameFont = new Font("Consolas", 8F, FontStyle.Regular, GraphicsUnit.Point);
        using var nameBrush = new SolidBrush(Color.FromArgb(118, 126, 140));

        for (int i = 0; i < slugs.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            var cell = new Rectangle(col * cellWidth + 4, row * cellHeight + 4,
                cellWidth - 8, cellHeight - 8);
            using var path = DeviceDetailsForm.RoundRect(cell, 8);
            using var fill = new SolidBrush(Color.FromArgb(248, 250, 253));
            graphics.FillPath(fill, path);
            graphics.DrawPath(cellPen, path);

            var logoArea = new Rectangle(cell.X + 10, cell.Y + 8,
                Math.Max(8, cell.Width - 20), Math.Max(8, cell.Height - 34));
            var logo = LoadLogoBySlug(slugs[i], logoArea.Width, logoArea.Height);
            if (logo is not null)
            {
                int x = logoArea.X + (logoArea.Width - logo.Width) / 2;
                int y = logoArea.Y + (logoArea.Height - logo.Height) / 2;
                graphics.DrawImage(logo, x, y, logo.Width, logo.Height);
            }

            graphics.DrawString(slugs[i], nameFont, nameBrush, cell.X + 8, cell.Bottom - 20);
        }

        return bitmap;
    }

    /// <summary>裁掉图片自带空白边后，按宽高比等比撑满目标区域，保证不变形。</summary>
    private static Bitmap CropAndFit(Bitmap raw, int maxWidth, int maxHeight)
    {
        Rectangle content = FindContentBounds(raw);
        float contentAspect = content.Width / (float)Math.Max(1, content.Height);

        int drawWidth;
        int drawHeight;
        if (contentAspect >= (float)maxWidth / maxHeight)
        {
            drawWidth = maxWidth;
            drawHeight = Math.Max(1, (int)Math.Round(maxWidth / contentAspect));
        }
        else
        {
            drawHeight = maxHeight;
            drawWidth = Math.Max(1, (int)Math.Round(maxHeight * contentAspect));
        }

        var bitmap = new Bitmap(drawWidth, drawHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(raw,
            new Rectangle(0, 0, drawWidth, drawHeight), content, GraphicsUnit.Pixel);
        return EnsureVisible(bitmap);
    }

    /// <summary>
    /// 官网深色页头抓下来的 logo 常常是纯白版，贴在白底图标卡上等于看不见。
    /// 这里检测内容整体是否过亮，过亮就整体反相成深色（单色白标反相后就是黑标）。
    /// </summary>
    private static Bitmap EnsureVisible(Bitmap bitmap)
    {
        long sum = 0;
        int count = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 60)
                {
                    continue;
                }

                sum += (pixel.R + pixel.G + pixel.B) / 3;
                count++;
            }
        }

        if (count == 0 || sum / (double)count < 220)
        {
            return bitmap;
        }

        var inverted = new Bitmap(bitmap.Width, bitmap.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(inverted);
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(
        [
            new[] { -1f, 0f, 0f, 0f, 1f },
            new[] { 0f, -1f, 0f, 0f, 1f },
            new[] { 0f, 0f, -1f, 0f, 1f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        ]));
        graphics.DrawImage(bitmap,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            0, 0, bitmap.Width, bitmap.Height,
            GraphicsUnit.Pixel, attributes);
        bitmap.Dispose();
        return inverted;
    }

    /// <summary>
    /// 找出位图里真正有内容的矩形：很多品牌 svg 的 viewBox 四周留了大片空白，
    /// 直接等比缩放会让 logo 看起来偏小。
    /// </summary>
    private static Rectangle FindContentBounds(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        Color background = bitmap.GetPixel(0, 0);

        // 背景透明时只按 alpha 判断前景：simple-icons 的黑色 logo 与透明背景的
        // RGB 都是 0，若仍用颜色差比较会把整块黑色图形当成背景忽略掉。
        bool transparentBackground = background.A < 10;

        int left = width, top = height, right = -1, bottom = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 10)
                {
                    continue;
                }

                if (!transparentBackground &&
                    Math.Abs(pixel.R - background.R) < 18 &&
                    Math.Abs(pixel.G - background.G) < 18 &&
                    Math.Abs(pixel.B - background.B) < 18)
                {
                    continue;
                }

                if (x < left)
                {
                    left = x;
                }

                if (x > right)
                {
                    right = x;
                }

                if (y < top)
                {
                    top = y;
                }

                if (y > bottom)
                {
                    bottom = y;
                }
            }
        }

        if (right < left || bottom < top)
        {
            return new Rectangle(0, 0, width, height);
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static void DrawGlyph(Graphics g, Rectangle bounds, PeripheralKind kind)
    {
        float radius = Math.Max(4f, bounds.Height * 0.24f);
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(Color.FromArgb(236, 239, 243));
        g.FillPath(brush, path);

        Color ink = Color.FromArgb(148, 154, 162);
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float s = Math.Min(bounds.Width, bounds.Height);
        switch (kind)
        {
            case PeripheralKind.Keyboard:
                DrawKeyboard(g, cx, cy, s, ink);
                break;
            case PeripheralKind.Headset:
                DrawHeadset(g, cx, cy, s, ink);
                break;
            case PeripheralKind.Remote:
                DrawRemote(g, cx, cy, s, ink);
                break;
            default:
                DrawMouse(g, cx, cy, s, ink);
                break;
        }
    }

    private static void DrawRemote(Graphics g, float cx, float cy, float s, Color ink)
    {
        float w = s * 0.3f;
        float h = s * 0.58f;
        using var pen = new Pen(ink, Math.Max(1f, s * 0.045f));
        using var path = Rounded(
            new Rectangle((int)(cx - w / 2), (int)(cy - h / 2), (int)w, (int)h),
            Math.Max(2f, w * 0.34f));
        g.DrawPath(pen, path);
        using var solid = new SolidBrush(ink);
        float dot = Math.Max(1.2f, s * 0.07f);
        g.FillEllipse(solid, cx - dot / 2, cy - h * 0.26f, dot, dot);
        g.FillRectangle(solid, cx - w * 0.22f, cy + h * 0.06f, w * 0.44f, Math.Max(1f, s * 0.05f));
    }

    private static void DrawMouse(Graphics g, float cx, float cy, float s, Color ink)
    {
        float w = s * 0.34f;
        float h = s * 0.5f;
        using var pen = new Pen(ink, Math.Max(1f, s * 0.045f));
        g.DrawEllipse(pen, cx - w / 2, cy - h / 2, w, h);
        g.DrawLine(pen, cx, cy - h / 2, cx, cy - h / 2 + h * 0.22f);
    }

    private static void DrawKeyboard(Graphics g, float cx, float cy, float s, Color ink)
    {
        float w = s * 0.56f;
        float h = s * 0.32f;
        using var pen = new Pen(ink, Math.Max(1f, s * 0.045f));
        g.DrawRectangle(pen, cx - w / 2, cy - h / 2, w, h);
        using var solid = new SolidBrush(ink);
        for (int i = 0; i < 3; i++)
        {
            float y = cy - h / 2 + h * (0.22f + i * 0.24f);
            g.FillRectangle(solid, cx - w / 2 + w * 0.1f, y, w * 0.8f, Math.Max(1f, h * 0.09f));
        }
    }

    private static void DrawHeadset(Graphics g, float cx, float cy, float s, Color ink)
    {
        float w = s * 0.48f;
        using var pen = new Pen(ink, Math.Max(1f, s * 0.045f));
        g.DrawArc(pen, cx - w / 2, cy - s * 0.24f, w, s * 0.44f, 180, 180);
        float cup = s * 0.11f;
        g.FillEllipse(new SolidBrush(ink), cx - w / 2 - cup * 0.4f, cy, cup, cup * 1.2f);
        g.FillEllipse(new SolidBrush(ink), cx + w / 2 - cup * 0.6f, cy, cup, cup * 1.2f);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
