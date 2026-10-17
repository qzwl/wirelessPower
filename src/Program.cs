using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using BatteryTray.Hid;
using BatteryTray.UI;

namespace BatteryTray;

internal static class Program
{
    private const string MutexName = @"Local\2.4G-Battery-Tray-SingleInstance";
    private const string ShowEventName = @"Local\2.4G-Battery-Tray-Show";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        string? probePath = GetOption(args, "--probe");
        string? previewPath = GetOption(args, "--icon-preview");
        string? uiPreviewPath = GetOption(args, "--ui-preview");
        string? makeIconPath = GetOption(args, "--make-icon");
        string? trayInfoPath = GetOption(args, "--tray-info");

        if (uiPreviewPath is not null)
        {
            RunUiPreview(uiPreviewPath);
            return;
        }

        if (probePath is not null)
        {
            RunProbe(probePath, demo);
            return;
        }

        if (previewPath is not null)
        {
            using var preview = BatteryIconRenderer.CreatePreview();
            preview.Save(previewPath, ImageFormat.Png);
            return;
        }

        if (makeIconPath is not null)
        {
            byte[] ico = BatteryIconRenderer.BuildIco(
                100, false, [16, 20, 24, 32, 40, 48, 64, 128, 256]);
            File.WriteAllBytes(makeIconPath, ico);
            return;
        }

        bool firstInstance;
        using var mutex = new Mutex(true, MutexName, out firstInstance);
        using var showEvent = new EventWaitHandle(
            false, EventResetMode.AutoReset, ShowEventName);
        if (!firstInstance)
        {
            showEvent.Set();
            return;
        }

        if (SecurityContext.IsLowIntegrity())
        {
            MessageBox.Show(
                "当前程序运行在低权限/沙箱环境中，Windows 不允许添加托盘图标。\n\n" +
                "请在文件资源管理器中直接双击 2.4G-Battery-Tray.exe 启动，\n" +
                "不要从受限终端或 AI 文件面板启动。",
                "无线外设电量",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var context = new TrayApplicationContext(demo, showEvent, trayInfoPath);
        Application.Run(context);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1];
            }

            return Path.Combine(Path.GetTempPath(), "2.4g-battery-tray-output");
        }

        return null;
    }

    private static void RunUiPreview(string path)
    {
        var result = new BatteryMonitor(demo: true).Read();
        using var form = new DeviceDetailsForm(null)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        form.SetInterval(60);
        form.UpdateResult(result, 60);
        Application.DoEvents();
        Thread.Sleep(80);
        Application.DoEvents();

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(path, ImageFormat.Png);
        form.Close();
    }

    private static void RunProbe(string path, bool demo)
    {
        var monitor = new BatteryMonitor(demo);
        var raw = monitor.Read();
        var cache = ReadingCache.Load();
        cache.Store(raw);
        var result = cache.Enrich(raw);
        IReadOnlyList<HidInterface> interfaces = Array.Empty<HidInterface>();
        string? enumerationError = null;
        try
        {
            interfaces = HidEnumerator.Enumerate();
        }
        catch (Exception ex)
        {
            enumerationError = ex.ToString();
        }

        var payload = new
        {
            result.UpdatedAt,
            result.MinimumLevel,
            result.Message,
            result.IsDemo,
            Tooltip = TrayApplicationContext.BuildTooltip(result, 127),
            enumerationError,
            interfaces = interfaces.Select(i => new
            {
                i.Path,
                i.VidPid,
                i.UsagePage,
                i.Usage,
                i.InputReportLength,
                i.OutputReportLength,
                i.FeatureReportLength,
                i.Product,
                i.Manufacturer,
                i.GroupKey,
                i.Error
            }),
            devices = result.Devices.Select(d => new
            {
                d.Key,
                d.Name,
                Kind = d.Kind.ToString(),
                d.Level,
                d.Charging,
                d.Source,
                d.Error
            })
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
            // 探测模式没有 UI，写文件失败时静默退出。
        }
    }
}
