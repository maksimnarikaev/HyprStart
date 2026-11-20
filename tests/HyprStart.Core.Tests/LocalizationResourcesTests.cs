using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Регрессионные тесты словарей локализации.
/// Ловят ситуацию «XAML ссылается на ключ, которого нет в Strings.*.xaml»:
/// в рантайме это даёт пустой текст, а в худшем случае XamlParseException,
/// который роняет процесс целиком (окно настроек такое уже ловило).
/// </summary>
public sealed class LocalizationResourcesTests
{
    private static readonly string[] UiXamlFiles =
    {
        "App.xaml",
        "MainWindow.xaml",
        "SettingsWindow.xaml",
    };

    private static string AppDir
        => Path.Combine(FindRepoRoot(), "src", "HyprStart.App");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HyprStart.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найден HyprStart.sln выше каталога тестов.");
    }

    /// <summary>Ключи из словаря: каждый ResourceDictionary.&lt;string&gt; = значение по x:Key.</summary>
    private static HashSet<string> LoadDictionaryKeys(string tag)
    {
        var path = Path.Combine(AppDir, "Resources", $"Strings.{tag}.xaml");
        Assert.True(File.Exists(path), $"Нет словаря {path}");

        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);
        var keys = doc.Descendants()
            .Where(e => e.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(keys);
        return keys;
    }

    /// <summary>Ключи, объявленные в Theme.xaml (стили, кисти, шаблоны) — это не строки UI.</summary>
    private static HashSet<string> LoadNonStringKeys()
    {
        var path = Path.Combine(AppDir, "Theme.xaml");
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);
        return doc.Descendants()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Все имена, на которые ссылается {DynamicResource Name} в UI-разметке.</summary>
    private static SortedSet<string> CollectDynamicResourceRefs()
    {
        var rx = new Regex(@"\{\s*DynamicResource\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}", RegexOptions.Compiled);
        var refs = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in UiXamlFiles)
        {
            var path = Path.Combine(AppDir, file);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match m in rx.Matches(text))
            {
                refs.Add(m.Groups[1].Value);
            }
        }

        return refs;
    }

    [Fact]
    public void Dictionaries_EnAndRu_HaveIdenticalKeys()
    {
        var en = LoadDictionaryKeys("en");
        var ru = LoadDictionaryKeys("ru");

        var onlyEn = en.Except(ru).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyRu = ru.Except(en).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(
            onlyEn.Count == 0 && onlyRu.Count == 0,
            $"Словари разошлись. Только в en: [{string.Join(", ", onlyEn)}]. Только в ru: [{string.Join(", ", onlyRu)}].");
    }

    [Fact]
    public void EveryDynamicResourceStringKey_ExistsInBothDictionaries()
    {
        var en = LoadDictionaryKeys("en");
        var ru = LoadDictionaryKeys("ru");
        var nonString = LoadNonStringKeys();
        var refs = CollectDynamicResourceRefs();

        Assert.NotEmpty(refs);

        var missing = refs
            .Where(k => !en.Contains(k) && !ru.Contains(k) && !nonString.Contains(k))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "XAML ссылается на несуществующие ключи: " + string.Join(", ", missing));

        // ключи-строки обязаны быть в ОБОИХ словарях, иначе часть UI останется пустой
        var stringRefs = refs.Where(k => en.Contains(k) || ru.Contains(k)).ToList();
        Assert.NotEmpty(stringRefs);

        var asymmetric = stringRefs
            .Where(k => !en.Contains(k) || !ru.Contains(k))
            .ToList();

        Assert.True(
            asymmetric.Count == 0,
            "Строковые ключи есть только в одном словаре: " + string.Join(", ", asymmetric));
    }

    [Fact]
    public void DictionaryStrings_AreNotBlank()
    {
        foreach (var tag in new[] { "en", "ru" })
        {
            var path = Path.Combine(AppDir, "Resources", $"Strings.{tag}.xaml");
            var doc = XDocument.Load(path, LoadOptions.SetLineInfo);

            var blanks = doc.Descendants()
                .Where(e => e.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase))
                .Where(e => string.IsNullOrWhiteSpace(e.Value))
                .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
                .Where(k => k is not null)
                .ToList();

            Assert.True(blanks.Count == 0, $"Пустые значения в Strings.{tag}.xaml: {string.Join(", ", blanks)}");
        }
    }
}
