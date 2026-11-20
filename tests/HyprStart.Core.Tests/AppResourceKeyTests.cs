using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Регрессия на краш ПКМ в «частом»: обработчик звал
/// FindResource("DarkContextMenuStyle"), а такого ключа в XAML нет — WPF бросал
/// ResourceReferenceKeyNotFoundException, и процесс падал целиком.
/// Тест сверяет каждый строковый ключ из FindResource/TryFindResource с x:Key в XAML.
/// </summary>
public class AppResourceKeyTests
{
    private static readonly Regex KeyRegex = new(
        "x:Key=\"(?<k>[^\"]+)\"", RegexOptions.Compiled);

    private static readonly Regex FindRegex = new(
        "(?:Try)?FindResource\\(\\s*\"(?<k>[^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Корень исходников приложения (поднимаемся от каталога теста).</summary>
    private static string? FindAppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "HyprStart.App");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static IEnumerable<string> SourceFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EveryFindResourceKeyExistsInXaml()
    {
        string? root = FindAppRoot();
        Assert.False(root is null, "не найден каталог src/HyprStart.App от каталога теста");

        var declared = SourceFiles(root!, "*.xaml")
            .SelectMany(f => KeyRegex.Matches(File.ReadAllText(f)).Select(m => m.Groups["k"].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        var missing = new List<string>();
        foreach (string file in SourceFiles(root!, "*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in FindRegex.Matches(text))
            {
                string key = m.Groups["k"].Value;
                if (!declared.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: FindResource(\"{key}\")");
            }
        }

        Assert.True(missing.Count == 0, "нет в XAML: " + string.Join("; ", missing));
    }
}
