using Microsoft.Win32;

namespace BatteryTray;

internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "2.4GBatteryTray";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开当前用户的启动项注册表。");

            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 程序被移动后，把旧的启动项路径更新为当前 EXE 路径。
    /// </summary>
    public static void RepairPathIfNeeded()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string current ||
                string.Equals(current, BuildCommand(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TrySetEnabled(true, out _);
        }
        catch
        {
            // 启动项修复失败不应影响托盘程序本身。
        }
    }

    private static string BuildCommand()
    {
        string executable = Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前 EXE 路径。");
        }

        return $"\"{Path.GetFullPath(executable)}\"";
    }
}
