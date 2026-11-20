using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Регрессия на краш при открытии настроек:
/// неявный Style TargetType="ScrollBar" лежал внутри &lt;ScrollViewer.Resources&gt;.
/// WPF валидирует такой стиль против типа самого элемента (ScrollViewer) и бросает
/// XamlParseException «TargetType ScrollBar не соответствует ScrollViewer».
/// </summary>
public class ScrollBarStyleScopeTests
{
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

    [Fact]
    public void NoImplicitScrollBarStyleInsideScrollViewerResources()
    {
        string? root = FindAppRoot();
        Assert.False(root is null, "не найден каталог src/HyprStart.App от каталога теста");

        // <ScrollViewer ...> ... <ScrollViewer.Resources> ... ScrollBar ... </ScrollViewer.Resources>
        var bad = new Regex(
            "<ScrollViewer\\b(?<body>.*?)</ScrollViewer>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        var offenders = new System.Collections.Generic.List<string>();
        foreach (string file in Directory
                     .EnumerateFiles(root!, "*.xaml", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in bad.Matches(text))
            {
                string body = m.Groups["body"].Value;
                bool svResources = body.Contains("<ScrollViewer.Resources>", StringComparison.Ordinal);
                bool scrollBarStyle = body.Contains("TargetType=\"ScrollBar\"", StringComparison.Ordinal);
                if (svResources && scrollBarStyle)
                    offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "ScrollBar-стиль объявлен внутри ScrollViewer.Resources: " + string.Join(", ", offenders));
    }
}
