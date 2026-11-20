using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using HyprStart.Core.Settings;
using MediaColor = System.Windows.Media.Color;

namespace HyprStart.App.Services;

/// <summary>
/// Темы: хранение в %LOCALAPPDATA%\HyprStart\themes\*.json и мгновенное применение
/// без перезапуска. Применение сделано мутацией существующих кистей из Theme.xaml —
/// так все StaticResource-ссылки в шаблонах подхватывают новые цвета сразу.
/// </summary>
public sealed class ThemeService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly List<ThemePalette> _themes = new();

    public ThemeService(string? preferredName)
    {
        ThemesDirectory = Path.Combine(AppPaths.Root, "themes");
        EnsureDefaults();
        LoadAll();

        // "auto" — не тема из списка: до первого кадра держим фолбэк (sakura-pink),
        // настоящая палитра придёт из снимка экрана в OverlayController.Show().
        if (IsAutoPreferred(preferredName))
        {
            ApplyAuto(AutoFallback);
            return;
        }

        var theme = Find(preferredName) ?? _themes.FirstOrDefault();
        if (theme is not null) Apply(theme);
    }

    /// <summary>Сохраняем в настройках сам режим "auto", а не имя подставленной темы.</summary>
    public string PreferredName => IsAutoActive ? AutoName : CurrentName;

    private static bool IsAutoPreferred(string? name)
        => string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase);

    public string ThemesDirectory { get; }
    public IReadOnlyList<ThemePalette> Themes => _themes;
    public string CurrentName { get; private set; } = string.Empty;
    public ThemePalette? Current { get; private set; }

    /// <summary>Срабатывает после применения темы — окно обновляет свои локальные кисти.</summary>
    public event Action? Applied;

    public ThemePalette? Find(string? name)
        => name is null ? null : _themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Имя режима «подхватывать цвета с экрана». Не тема из списка: палитра
    /// собирается на лету из кадра рабочего стола в момент открытия оверлея.
    /// </summary>
    public const string AutoName = "auto";

    /// <summary>Зеркало обоев под раскладкой, пока не пришёл первый кадр (и для фолбэка).</summary>
    public ThemePalette AutoFallback { get; set; } = ThemePalette.SakuraPink;

    /// <summary>Тема из списка сейчас активна (а не режим auto).</summary>
    public bool IsAutoActive => string.Equals(CurrentName, AutoName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Применяет палитру, собранную из кадра экрана. Клик по пилюле темы
    /// переключает с auto на следующую статичную тему — это ожидаемо и обратимо.
    /// </summary>
    public void ApplyAuto(ThemePalette palette)
    {
        Apply(Clone(palette, AutoName));
    }

    /// <summary>Копия палитры с другим именем: палитра из кадра не должна попасть в список тем.</summary>
    private static ThemePalette Clone(ThemePalette source, string name) => new()
    {
        Name = name,
        Glass = source.Glass,
        Divider = source.Divider,
        Accent = source.Accent,
        Accent2 = source.Accent2,
        Text = source.Text,
        Muted = source.Muted,
        Pill = source.Pill,
        PillBorder = source.PillBorder,
        BorderFrom = source.BorderFrom,
        BorderTo = source.BorderTo,
        Tile = source.Tile,
        TileBorder = source.TileBorder,
        Hover = source.Hover,
        Selection = source.Selection,
        Menu = source.Menu,
        CavaFrom = source.CavaFrom,
        CavaTo = source.CavaTo,
    };

    public bool ApplyByName(string name)
    {
        if (string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase))
        {
            ApplyAuto(AutoFallback);
            return true;
        }

        var theme = Find(name);
        if (theme is null) return false;

        Apply(theme);
        return true;
    }

    /// <summary>Следующая тема по кругу (для пилюли в polybar). Возвращает имя или null.</summary>
    public string? CycleNext()
    {
        if (_themes.Count == 0) return null;

        int index = _themes.FindIndex(t => string.Equals(t.Name, CurrentName, StringComparison.CurrentCultureIgnoreCase));

        // после последней статичной темы идёт режим "auto" — он замыкает круг
        if (index == _themes.Count - 1)
        {
            ApplyAuto(AutoFallback);
            return AutoName;
        }

        var next = _themes[(index + 1) % _themes.Count];

        Apply(next);
        return next.Name;
    }

    private void Apply(ThemePalette theme)
    {
        try
        {
            // Заменяем ресурсы НОВЫМИ экземплярами кистей: DynamicResource подхватит их сразу,
            // а мутировать замороженные кисти (как раньше) бессмысленно — они IsFrozen.
            PutSolid("GlassTintBrush", theme.Glass);
            PutSolid("DividerBrush", theme.Divider);
            PutSolid("AccentBrush", theme.Accent);
            PutSolid("TextBrush", theme.Text);
            PutSolid("MutedBrush", theme.Muted);
            PutSolid("PillBrush", theme.Pill);
            PutSolid("PillBorderBrush", theme.PillBorder);
            PutSolid("TileBrush", theme.Tile);
            PutSolid("TileBorderBrush", theme.TileBorder);
            PutSolid("HoverBrush", theme.Hover);
            PutSolid("SelectionBrush", theme.Selection);
            PutSolid("MenuBrush", theme.Menu);

            PutGradient("PanelBorderGradient", theme.BorderFrom, theme.BorderTo);
            PutGradient("CavaGradient", theme.CavaFrom, theme.CavaTo);

            CurrentName = theme.Name;
            Current = theme;

            AppLog.Write($"theme: применена '{theme.Name}' accent={ReadColor("AccentBrush")} glass={ReadColor("GlassTintBrush")} tile={ReadColor("TileBrush")}");
            Applied?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"theme: не удалось применить '{theme.Name}'", ex);
        }
    }

    private static void PutSolid(string key, string hex)
    {
        var brush = new SolidColorBrush(Parse(hex, Colors.White));
        brush.Freeze();
        System.Windows.Application.Current!.Resources[key] = brush;
    }

    private static void PutGradient(string key, string fromHex, string toHex)
    {
        var brush = new LinearGradientBrush(Parse(fromHex, Colors.White), Parse(toHex, Colors.White), 45);
        brush.Freeze();
        System.Windows.Application.Current!.Resources[key] = brush;
    }

    private static string ReadColor(string key)
        => System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush ? brush.Color.ToString() : "?";

    private static MediaColor Parse(string? hex, MediaColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;

        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(hex) is MediaColor color ? color : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(ThemesDirectory);
        EnsureThemeFile(ThemePalette.IceBlue, "ice-blue.json");
        EnsureThemeFile(ThemePalette.ForestMint, "forest-mint.json");
        EnsureThemeFile(ThemePalette.SakuraPink, "sakura-pink.json");
    }

    /// <summary>
    /// Файл темы старого формата (без новых ключей) перезаписываем актуальным пресетом,
    /// иначе поля вроде tile/cava подставляются из дефолтов и не совпадают с темой.
    /// </summary>
    private void EnsureThemeFile(ThemePalette preset, string fileName)
    {
        try
        {
            var path = Path.Combine(ThemesDirectory, fileName);

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (json.Contains("\"tile\"", StringComparison.OrdinalIgnoreCase)
                    && json.Contains("\"cavaFrom\"", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                AppLog.Write($"theme: обновляю {fileName} до нового формата");
            }

            Save(preset, fileName);
        }
        catch (Exception ex)
        {
            AppLog.Error($"theme: не удалось подготовить {fileName}", ex);
        }
    }

    private void Save(ThemePalette theme, string fileName)
    {
        try
        {
            File.WriteAllText(Path.Combine(ThemesDirectory, fileName), JsonSerializer.Serialize(theme, Options));
        }
        catch (Exception ex)
        {
            AppLog.Error($"theme: не удалось записать {fileName}", ex);
        }
    }

    private void LoadAll()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(ThemesDirectory, "*.json").OrderBy(f => f))
            {
                try
                {
                    var theme = JsonSerializer.Deserialize<ThemePalette>(File.ReadAllText(file), Options);
                    if (theme is not null && !string.IsNullOrWhiteSpace(theme.Name)) _themes.Add(theme);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"theme: файл не читается ({file})", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("theme: не удалось прочитать папку тем", ex);
        }
    }
}
