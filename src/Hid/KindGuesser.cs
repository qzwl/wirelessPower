namespace BatteryTray.Hid;

/// <summary>
/// 依据设备名推断外设类型。蓝牙电量只能读到名字，没有可靠的 HID 用途页可用，
/// 所以这里把常见中文品类词、英文关键词，以及 K01/M02 这类型号前缀都纳入判断。
/// </summary>
internal static class KindGuesser
{
    public static PeripheralKind FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PeripheralKind.Other;
        }

        string lower = name.ToLowerInvariant();

        // 遥控器 / 语音遥控：既不是键也不是鼠。
        if (lower.Contains("遥控") || lower.Contains("remote") ||
            lower.Contains("air mouse") || lower.Contains("presenter"))
        {
            return PeripheralKind.Remote;
        }

        if (lower.Contains("mouse") || lower.Contains("鼠标") ||
            lower.Contains("triathlon") || lower.Contains("mx master") ||
            lower.Contains("mx anywhere") || lower.Contains("trackball"))
        {
            return PeripheralKind.Mouse;
        }

        if (lower.Contains("keyboard") || lower.Contains("键盘") ||
            lower.Contains("keypad") || lower.Contains("trackpad"))
        {
            return PeripheralKind.Keyboard;
        }

        if (lower.Contains("headphone") || lower.Contains("headset") ||
            lower.Contains("earbud") || lower.Contains("buds") ||
            lower.Contains("airpods") || lower.Contains("耳机") ||
            lower.Contains("speaker") || lower.Contains("音箱"))
        {
            return PeripheralKind.Headset;
        }

        // 型号前缀兜底：seewo K02W-A / M02W-A 这类只有型号、没有品类词的名字。
        if (HasModelPrefix(lower, 'k'))
        {
            return PeripheralKind.Keyboard;
        }

        if (HasModelPrefix(lower, 'm'))
        {
            return PeripheralKind.Mouse;
        }

        return PeripheralKind.Other;
    }

    /// <summary>判断名字里是否含有 K01/M02 这类「字母 + 两位数字」的型号片段。</summary>
    private static bool HasModelPrefix(string lower, char letter)
    {
        foreach (string token in lower.Split(' ', '-', '_', '/'))
        {
            if (token.Length >= 3 &&
                token[0] == letter &&
                char.IsDigit(token[1]) &&
                char.IsDigit(token[2]))
            {
                return true;
            }
        }

        return false;
    }
}
