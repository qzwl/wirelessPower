using System.Speech.Synthesis;
using System.Media;

namespace BatteryTray.UI;

/// <summary>
/// 低电量语音播报：用 Windows 自带的 SAPI 语音，优先挑中文发音人；
/// 系统没有中文语音、或用户关掉语音提醒时，退化为只响提示音。
/// </summary>
internal sealed class VoiceAnnouncer : IDisposable
{
    private SpeechSynthesizer? _synth;
    private bool _enabled;

    public VoiceAnnouncer(bool enabled)
    {
        _enabled = enabled;
        try
        {
            var synth = new SpeechSynthesizer
            {
                Rate = 0,
                Volume = 90
            };

            foreach (var voice in synth.GetInstalledVoices())
            {
                string culture = voice.VoiceInfo.Culture?.Name ?? string.Empty;
                if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                    voice.VoiceInfo.Name.Contains("Huihui", StringComparison.OrdinalIgnoreCase) ||
                    voice.VoiceInfo.Name.Contains("Kangkang", StringComparison.OrdinalIgnoreCase) ||
                    voice.VoiceInfo.Name.Contains("Yaoyao", StringComparison.OrdinalIgnoreCase))
                {
                    synth.SelectVoice(voice.VoiceInfo.Name);
                    break;
                }
            }

            _synth = synth;
        }
        catch
        {
            // 没有 SAPI 语音组件时静默降级，只保留提示音。
            _synth = null;
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public bool Available => _synth is not null;

    /// <summary>可用的发音人名称，用于诊断。</summary>
    public string VoiceName
    {
        get
        {
            try
            {
                return _synth?.Voice.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>本机已安装的发音人清单（名称|区域），用于诊断。</summary>
    public IReadOnlyList<string> InstalledNames()
    {
        try
        {
            return _synth?.GetInstalledVoices()
                .Select(voice => voice.VoiceInfo.Name + "|" +
                                 (voice.VoiceInfo.Culture?.Name ?? "unknown"))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Announce(string text, bool urgent)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            (urgent ? SystemSounds.Hand : SystemSounds.Asterisk).Play();
        }
        catch
        {
            // 静音设备或无音频会话时忽略。
        }

        if (_synth is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            // 新的提醒要盖过旧的，避免连续播报堆在一起。
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(text);
        }
        catch
        {
            // 播报失败不影响界面与托盘提示。
        }
    }

    /// <summary>把一段话合成到 wav 文件，用于自检语音链路是否可用。</summary>
    public bool SynthesizeToWave(string text, string path)
    {
        try
        {
            var synth = new SpeechSynthesizer();
            try
            {
                if (_synth is not null)
                {
                    synth.SelectVoice(_synth.Voice.Name);
                }
            }
            catch
            {
                // 沿用系统默认发音人。
            }

            synth.SetOutputToWaveFile(path);
            synth.Speak(text);
            synth.Dispose();
            return File.Exists(path) && new FileInfo(path).Length > 1000;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _synth?.SpeakAsyncCancelAll();
            _synth?.Dispose();
        }
        catch
        {
            // 退出路径上容忍异常。
        }

        _synth = null;
    }
}
