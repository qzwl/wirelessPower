using System.Text.Json;

namespace BatteryTray;

internal sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 60;
    public bool TrayHintShown { get; set; }
    /// <summary>设备筛选：all / online / offline。</summary>
    public string FilterMode { get; set; } = "all";
    /// <summary>电量变化时是否用语音播报，默认开启。</summary>
    public bool VoiceEnabled { get; set; } = true;

    private static string SettingsDirectory => AppPaths.DataDirectory;

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null)
                {
                    settings.RefreshSeconds = Normalize(settings.RefreshSeconds);
                    settings.FilterMode = NormalizeFilter(settings.FilterMode);
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时回到默认值，不写日志、不打断托盘启动。
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            string temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, true);
        }
        catch
        {
            // 设置保存失败不影响电量读取。
        }
    }

    public static int Normalize(int seconds)
    {
        int[] allowed = [30, 60, 120, 300, 600, 1800];
        return allowed.Contains(seconds) ? seconds : 60;
    }

    public static string NormalizeFilter(string? mode)
    {
        return mode switch
        {
            "online" => "online",
            "offline" => "offline",
            _ => "all"
        };
    }
}
