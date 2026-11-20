namespace HyprStart.Core.Input;

/// <summary>
/// Горячая клавиша как пара «модификаторы + виртуальный код» — то, что нужно
/// для RegisterHotKey. Парсер вынесен в Core, чтобы покрыть его юнит-тестами.
/// </summary>
public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static HotkeySpec AltSpace => new(ModAlt, 0x20);

    public bool IsValid => VirtualKey != 0;

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        uint modifiers = 0;
        uint key = 0;

        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "alt":
                    modifiers |= ModAlt;
                    continue;
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    continue;
                case "shift":
                    modifiers |= ModShift;
                    continue;
                case "win":
                    modifiers |= ModWin;
                    continue;
            }

            if (key != 0) return false; // две основные клавиши не поддерживаем
            if (!TryParseKey(raw, out key)) return false;
        }

        // Голая клавиша без модификатора как глобальный хоткей — это перехват ввода
        // по всему системе, такую конфигурацию отвергаем.
        if (key == 0 || modifiers == 0) return false;

        spec = new HotkeySpec(modifiers, key);
        return true;
    }

    /// <summary>
    /// Собирает комбинацию из WPF-ввода (окно настроек). Возвращает null, если
    /// клавиша не поддерживается или модификатор не выбран: голая клавиша как
    /// глобальный хоткей забрала бы ввод у всей системы.
    /// </summary>
    public static HotkeySpec? FromKey(uint modifiers, uint virtualKey)
    {
        if (modifiers == 0 || virtualKey == 0) return null;
        if ((modifiers & ~(ModAlt | ModControl | ModShift | ModWin)) != 0) return null;
        if (KeyName(virtualKey).StartsWith("0x", StringComparison.Ordinal)) return null;

        return new HotkeySpec(modifiers, virtualKey);
    }

    /// <summary>Годится ли виртуальный код как основная клавиша хоткея.</summary>
    public static bool IsAssignableKey(uint virtualKey)
        => virtualKey != 0 && !KeyName(virtualKey).StartsWith("0x", StringComparison.Ordinal);
    public string Describe()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    public override string ToString() => Describe();

    private static bool TryParseKey(string text, out uint key)
    {
        key = 0;
        if (text.Length == 1)
        {
            var ch = char.ToUpperInvariant(text[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = ch;
                return true;
            }
        }

        var upper = text.ToUpperInvariant();
        if (upper.Length >= 2 && upper[0] == 'F'
            && int.TryParse(upper[1..], out int fn)
            && fn is >= 1 and <= 24)
        {
            key = (uint)(0x70 + fn - 1);
            return true;
        }

        switch (upper)
        {
            case "SPACE": key = 0x20; return true;
            case "ENTER": key = 0x0D; return true;
            case "TAB": key = 0x09; return true;
            case "ESC":
            case "ESCAPE": key = 0x1B; return true;
            case "INSERT": key = 0x2D; return true;
            case "DELETE": key = 0x2E; return true;
            case "HOME": key = 0x24; return true;
            case "END": key = 0x23; return true;
            case "PGUP": key = 0x21; return true;
            case "PGDN": key = 0x22; return true;
            case "`":
            case "OEM3": key = 0xC0; return true;
            default: return false;
        }
    }

    private static string KeyName(uint key)
    {
        if (key is >= (uint)'A' and <= (uint)'Z') return ((char)key).ToString();
        if (key is >= (uint)'0' and <= (uint)'9') return ((char)key).ToString();
        if (key is >= 0x70 and <= 0x87) return "F" + (key - 0x70 + 1);

        return key switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0xC0 => "`",
            _ => $"0x{key:X2}",
        };
    }
}
