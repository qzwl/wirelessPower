using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryTray.UI;

/// <summary>托盘菜单的浅色配色表：白底、淡边框、柔和高亮。</summary>
internal sealed class ModernMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.White;

    public override Color MenuBorder => Color.FromArgb(216, 222, 230);

    public override Color MenuItemBorder => Color.FromArgb(42, 106, 240);

    public override Color MenuItemSelected => Color.FromArgb(236, 242, 252);

    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(236, 242, 252);

    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(236, 242, 252);

    public override Color MenuItemPressedGradientBegin => Color.FromArgb(226, 234, 248);

    public override Color MenuItemPressedGradientEnd => Color.FromArgb(226, 234, 248);

    public override Color ImageMarginGradientBegin => Color.White;

    public override Color ImageMarginGradientMiddle => Color.White;

    public override Color ImageMarginGradientEnd => Color.White;

    public override Color SeparatorDark => Color.FromArgb(238, 241, 245);

    public override Color SeparatorLight => Color.White;
}

/// <summary>自绘托盘菜单：圆角高亮、细分隔线、统一文字色。</summary>
internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.IsOnDropDown && e.Item.Selected && e.Item.Enabled)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(3, 1, Math.Max(10, e.Item.Width - 6), e.Item.Height - 2);
            using var path = DeviceDetailsForm.RoundRect(rect, 6);
            using var brush = new SolidBrush(Color.FromArgb(236, 242, 252));
            graphics.FillPath(brush, path);
            return;
        }

        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(238, 241, 245));
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
    }

    /// <summary>把系统那个灰蓝小方块换成圆角实心勾，和餐单项的圆点列对齐。</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int size = Math.Min(e.ImageRectangle.Width, e.ImageRectangle.Height);
        if (size <= 0)
        {
            size = 12;
        }

        size = Math.Min(size, 11);

        int x = e.ImageRectangle.X + (e.ImageRectangle.Width - size) / 2;
        int y = e.ImageRectangle.Y + (e.ImageRectangle.Height - size) / 2;
        var box = new Rectangle(x, y, size, size);

        using var path = DeviceDetailsForm.RoundRect(box, Math.Max(2f, size / 4f));
        using var brush = new SolidBrush(Color.FromArgb(42, 106, 240));
        graphics.FillPath(brush, path);

        using var pen = new Pen(Color.White, Math.Max(1.3f, size / 7f))
        {
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(pen,
        [
            new PointF(x + size * 0.22f, y + size * 0.52f),
            new PointF(x + size * 0.42f, y + size * 0.72f),
            new PointF(x + size * 0.78f, y + size * 0.30f)
        ]);
    }

    /// <summary>子菜单箭头自绘成细线小三角，避免系统那个实心黑块。</summary>
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = e.ArrowRectangle;
        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        Color color = e.Item.Enabled ? Color.FromArgb(118, 130, 148) : Color.FromArgb(198, 204, 212);

        using var pen = new Pen(color, 1.3f) { LineJoin = LineJoin.Round };
        graphics.DrawLines(pen,
        [
            new PointF(cx - 2f, cy - 4f),
            new PointF(cx + 2f, cy),
            new PointF(cx - 2f, cy + 4f)
        ]);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Tag is "menu-header")
        {
            e.TextColor = Color.FromArgb(26, 30, 36);
        }
        else if (e.Item.Tag is "menu-subheader")
        {
            e.TextColor = Color.FromArgb(132, 140, 152);
        }
        else if (!e.Item.Enabled)
        {
            e.TextColor = Color.FromArgb(150, 155, 162);
        }

        base.OnRenderItemText(e);
    }
}
