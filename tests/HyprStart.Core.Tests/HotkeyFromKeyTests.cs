using HyprStart.Core.Input;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Запись комбинации из окна настроек. HotkeyBox отдаёт WPF-клавишу
/// (<c>e.Key == Key.System ? e.SystemKey : e.Key</c>) и модификаторы, а SettingsWindow
/// переводит их в маску RegisterHotKey + виртуальный код и зовёт HotkeySpec.FromKey.
/// Проект HyprStart.Core не тянет WPF, поэтому здесь используются те же числа,
/// что даёт KeyInterop.VirtualKeyFromKey / Keyboard.Modifiers:
/// VK_Q = 0x51, ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8.
/// </summary>
public class HotkeyFromKeyTests
{
    private const uint VkQ = 0x51;

    [Fact]
    public void AltQ_WritesAltPlusQ()
    {
        var spec = HotkeySpec.FromKey(HotkeySpec.ModAlt, VkQ);

        Assert.NotNull(spec);
        Assert.Equal("Alt+Q", spec!.Value.Describe());
    }

    [Fact]
    public void AltQ_RoundTripsThroughParse()
    {
        var spec = HotkeySpec.FromKey(HotkeySpec.ModAlt, VkQ)!.Value;

        Assert.True(HotkeySpec.TryParse(spec.Describe(), out var parsed));
        Assert.Equal(spec.Modifiers, parsed.Modifiers);
        Assert.Equal(spec.VirtualKey, parsed.VirtualKey);
    }

    [Fact]
    public void CtrlAltQ_WritesCtrlPlusAltPlusQ()
    {
        var spec = HotkeySpec.FromKey(HotkeySpec.ModControl | HotkeySpec.ModAlt, VkQ);

        Assert.NotNull(spec);
        Assert.Equal("Ctrl+Alt+Q", spec!.Value.Describe());
    }

    [Fact]
    public void BareKey_IsRejected()
    {
        // Голая клавиша как глобальный хоткей забрала бы ввод у всей системы.
        Assert.Null(HotkeySpec.FromKey(0, VkQ));
    }

    [Fact]
    public void AltAlone_IsNotAGesture()
    {
        // Пока зажат только Alt, OnHotkeyPreview ждёт обычную клавишу и не пишет комбинацию.
        Assert.Null(HotkeySpec.FromKey(HotkeySpec.ModAlt, 0));
    }

    [Fact]
    public void AltPlusKey_IsNotSwallowedByModifierPath()
    {
        // Регресс на баг: при зажатом Alt WPF присылает Key.System, и если взять e.Key
        // вместо e.SystemKey, в FromKey уедет код Alt (0x12) вместо клавиши — комбинация
        // «не запишется». Проверяем, что Alt остаётся модификатором, а не основной клавишей.
        const uint VkMenu = 0x12; // VK_MENU (Alt)

        var good = HotkeySpec.FromKey(HotkeySpec.ModAlt, VkQ);
        Assert.Equal("Alt+Q", good!.Value.Describe());

        // Если по ошибке подставить Alt как основную клавишу, комбинация не должна
        // выглядеть рабочей: Alt не годится как основная клавиша хоткея.
        Assert.False(HotkeySpec.IsAssignableKey(VkMenu));
    }

    [Theory]
    [InlineData(0x12u)] // VK_MENU / Alt
    [InlineData(0xA2u)] // LeftCtrl
    [InlineData(0xA3u)] // RightCtrl
    [InlineData(0xA0u)] // LeftShift
    [InlineData(0xA1u)] // RightShift
    [InlineData(0x5Bu)] // LWin
    [InlineData(0x5Cu)] // RWin
    public void ModifierVirtualKeysAreNotAssignable(uint vk)
    {
        Assert.False(HotkeySpec.IsAssignableKey(vk));
    }

    [Fact]
    public void LetterVirtualKey_IsAssignable()
    {
        Assert.True(HotkeySpec.IsAssignableKey(VkQ));
    }
}
