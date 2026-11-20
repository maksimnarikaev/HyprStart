using System.Text;

namespace HyprStart.App;

/// <summary>Мелкие текстовые хелперы. В WPF нет letter-spacing, поэтому разрядку делаем вручную.</summary>
internal static class TextFx
{
    private const char HairSpace = '\u200A';

    /// <summary>«Приложения» → «П\u200AР\u200AИ\u200A…»: разрядка для заголовков секций.</summary>
    public static string Spaced(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var source = text.ToUpperInvariant();
        var builder = new StringBuilder(source.Length * 2);
        for (int i = 0; i < source.Length; i++)
        {
            if (i > 0) builder.Append(HairSpace);
            builder.Append(source[i]);
        }
        return builder.ToString();
    }
}
