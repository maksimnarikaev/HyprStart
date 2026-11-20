using System.Runtime.InteropServices;

namespace HyprStart.App.Services;

/// <summary>
/// Громкость системными мультимедиа-клавишами: Windows двигает ползунок шагом 2%,
/// работает независимо от состояния CoreAudio и не требует COM-интерфейсов.
/// </summary>
internal static class AudioControl
{
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaPlayPause = 0xB3;
    private const int KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Шаг ±1 = ±2% системной громкости.</summary>
    public static void Step(int direction) => Press(direction >= 0 ? VkVolumeUp : VkVolumeDown);

    public static void ToggleMute() => Press(VkVolumeMute);

    /// <summary>Play/Pause текущего плеера системной клавишей.</summary>
    public static void PlayPause() => Press(VkMediaPlayPause);

    private static void Press(byte vk)
    {
        try
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KeyEventKeyUp, UIntPtr.Zero);
            AppLog.Write($"audio: отправлено vk=0x{vk:X2}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"audio: не удалось отправить vk=0x{vk:X2}", ex);
        }
    }
}
