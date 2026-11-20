using System;
using System.Globalization;
using System.Windows;
using HyprStart.Core.Chat;

// HyprStart.App ссылается и на WinForms (трей-иконка), поэтому Application неоднозначен.
using Application = System.Windows.Application;

namespace HyprStart.App.Services;

/// <summary>
/// Двуязычный интерфейс: словари <c>Resources/Strings.en.xaml</c> и <c>Strings.ru.xaml</c>.
/// <para>
/// Правило выбора языка: явное значение из настроек (<c>en</c>/<c>ru</c>), иначе
/// автоопределение по <see cref="CultureInfo.CurrentUICulture"/> - русская система
/// получает RU, все остальные EN (EN - дефолт для публикации на GitHub).
/// </para>
/// </summary>
public static class Localizer
{
    /// <summary>Поддерживаемые языки.</summary>
    public static readonly string[] Supported = { "en", "ru" };

    /// <summary>Значение настройки «язык не выбран - определить автоматически».</summary>
    public const string Auto = "";

    /// <summary>Текущий язык интерфейса: <c>en</c> или <c>ru</c>.</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>Сменился язык - окна должны перечитать строки.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Язык по культуре ОС: RU только для русскоязычной системы.</summary>
    public static string DetectFromSystem()
    {
        var culture = CultureInfo.CurrentUICulture;
        return string.Equals(culture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase)
            ? "ru"
            : "en";
    }

    /// <summary>Приводит значение настройки к поддерживаемому языку (пусто - авто).</summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return Auto;

        var value = language.Trim().ToLowerInvariant();
        return value is "en" or "ru" ? value : Auto;
    }

    /// <summary>Язык, который реально надо загрузить для значения настройки.</summary>
    public static string Resolve(string? language)
    {
        var normalized = Normalize(language);
        return normalized.Length == 0 ? DetectFromSystem() : normalized;
    }

    /// <summary>
    /// Подключает словарь нужного языка в ресурсы приложения. Повторный вызов с тем же
    /// языком ничего не делает, поэтому дёргать метод из окна настроек безопасно.
    /// </summary>
    public static void Apply(string? language)
    {
        var target = Resolve(language);
        ChatMessages.UseLanguage(target);
        if (target == Current && IsLoaded(target)) return;

        Current = target;

        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Strings.", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{target}.xaml", UriKind.Relative),
        });

        // Окна перерисовывают строки, которые ставятся из кода, а не через DynamicResource.
        LanguageChanged?.Invoke();
    }

    private static bool IsLoaded(string language)
    {
        var merged = Application.Current?.Resources.MergedDictionaries;
        if (merged is null) return false;

        foreach (var dictionary in merged)
        {
            var source = dictionary.Source?.OriginalString ?? string.Empty;
            if (source.EndsWith($"Strings.{language}.xaml", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Строка по ключу: отсутствующий ключ возвращает сам ключ, а не падение.</summary>
    public static string T(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>Подпись языка в переключателе настроек: «English» / «Русский».</summary>
    public static string DisplayName(string tag)
        => tag == "ru" ? "Русский" : "English";

    /// <summary>Обратный разбор подписи из переключателя в тег языка.</summary>
    public static string TagForLabel(string label)
    {
        foreach (var tag in Supported)
        {
            if (string.Equals(DisplayName(tag), label, StringComparison.Ordinal)) return tag;
        }

        return Auto;
    }

    /// <summary>Форматирование строки из словаря: <c>{0}</c>-плейсхолдеры.</summary>
    public static string Format(string key, params object?[] args) => T(key, args);

    /// <summary>
    /// Строка по ключу с подстановкой значений. Формат-строка берётся из словаря,
    /// поэтому плейсхолдеры <c>{0}</c> можно переставлять при переводе.
    /// </summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        if (args.Length == 0) return format;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
