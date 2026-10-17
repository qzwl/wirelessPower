namespace BatteryTray;

internal static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable("BATTERY_TRAY_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "2.4GBatteryTray");
        }
    }
}
