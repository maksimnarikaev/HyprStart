using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HyprStart.App.Interop;
using HyprStart.Core.Library;

namespace HyprStart.App.Services;

/// <summary>
/// Кэш иконок 256×256. Извлечение идёт через GDI+ (без WPF и без фоновых потоков
/// с RenderTargetBitmap): для .ico берётся САМЫЙ КРУПНЫЙ кадр, для .exe/.lnk — иконка
/// по ассоциации, затем картинка обрезается по содержимому и масштабируется в квадрат.
/// Оболочка (IShellItemImageFactory) остаётся вторым путём — для UWP и «сложных» ярлыков.
/// </summary>
internal static class IconCache
{
    private const int IconSize = 256;
    private const string PlaceholderPrefix = "ph_";
    private const string CacheVersion = "ic6|";

    public static string EnsureIcon(AppEntry entry, string iconsDir, bool refresh)
    {
        var hash = Hash(entry.Target);
        var fileName = hash + ".png";
        var fullPath = Path.Combine(iconsDir, fileName);

        if (!refresh && File.Exists(fullPath) && new FileInfo(fullPath).Length > 0)
        {
            return fileName;
        }

        // 1. Прямое извлечение из файла: самый крупный кадр .ico / иконка по ассоциации
        if (TryExtractWithGdi(entry, fullPath))
        {
            return fileName;
        }

        // 2. Оболочка — нужна для UWP и ярлыков, у которых иконка лежит в ресурсах exe
        var parsingName = entry.Kind == AppEntryKind.Shortcut
            ? entry.Target
            : "shell:AppsFolder\\" + entry.Target;

        if (ShellInterop.TrySaveIcon(parsingName, fullPath, IconSize) && !LooksBlank(fullPath))
        {
            return fileName;
        }

        if (entry.Kind == AppEntryKind.Shortcut
            && ShellInterop.TrySaveIcon(entry.Target, fullPath, IconSize)
            && !LooksBlank(fullPath))
        {
            return fileName;
        }

        var placeholderName = PlaceholderPrefix + hash + ".png";
        SavePlaceholder(entry.Name, Path.Combine(iconsDir, placeholderName));
        return placeholderName;
    }

    public static bool IsPlaceholder(string? iconFile)
        => iconFile is not null && iconFile.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

    private static bool TryExtractWithGdi(AppEntry entry, string outputPath)
    {
        var candidates = new List<(Bitmap Image, int Priority, string Source)>();

        try
        {
            AddShortcutCandidates(candidates, entry);
            AddIconCandidate(candidates, entry.IconHint, priority: 5, "ico-ярлыка");
            AddIconCandidate(candidates, entry.Target, priority: 2, "целевой файл");
            AddSteamCandidates(candidates, entry);
            AddShellCandidate(candidates, entry);

            if (candidates.Count == 0) return false;

            Bitmap? best = null;
            double bestScore = -1;
            string bestSource = string.Empty;

            // Сначала считаем очки по метаданным (приоритет + заполнение холста),
            // и только победителю делаем дорогой CropToContent. Раньше кроп
            // выполнялся для каждого кандидата, хотя в кадр попадал один.
            foreach (var (image, priority, source) in candidates)
            {
                double coverage = Coverage(image);
                double score = priority * 40 + coverage * 100;

                if (score <= bestScore) continue;

                bestScore = score;
                best = image; // без Clone: владение переходит сюда (из candidates уберём после цикла)
                bestSource = $"{source} ({coverage:P0})";
            }

            bool saved = false;

            if (best is not null)
            {
                var winner = best;
                best = null;
                candidates.RemoveAll(c => ReferenceEquals(c.Image, winner)); // владение у winner

                using (winner)
                using (var cropped = CropToContent(winner))
                using (var square = ScaleIntoSquare(cropped, IconSize))
                {
                    square.Save(outputPath, ImageFormat.Png);
                }

                saved = true;
            }

            if (!saved) return false;

            AppLog.Write($"icon: '{entry.Name}' <- {bestSource}");
            return new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            AppLog.Error($"icon: извлечение не удалось ({entry.Target})", ex);
            return false;
        }
        finally
        {
            foreach (var candidate in candidates) candidate.Image.Dispose();
        }
    }

    /// <summary>
    /// Иконка из ЦЕЛИ ярлыка: у самого .lnk в углу нарисована стрелка-оверлей,
    /// а у exe/ico из IconLocation её нет — картинка получается чистой.
    /// </summary>
    private static void AddShortcutCandidates(List<(Bitmap Image, int Priority, string Source)> target, AppEntry entry)
    {
        if (entry.Kind != AppEntryKind.Shortcut) return;
        if (!entry.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(entry.Target)) return;

        var (targetPath, iconLocation) = ReadShortcut(entry.Target);

        if (!string.IsNullOrWhiteSpace(iconLocation))
        {
            AddIconCandidate(target, iconLocation.Split(',')[0].Trim(), priority: 7, "иконка из ярлыка");
        }

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            AddIconCandidate(target, targetPath, priority: 6, "цель ярлыка");
        }
    }

    /// <summary>Читает TargetPath и IconLocation ярлыка через WScript.Shell (без ссылок на COM-типы).</summary>
    private static (string? TargetPath, string? IconLocation) ReadShortcut(string path)
    {
        object? shell = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return (null, null);

            shell = Activator.CreateInstance(shellType);
            if (shell is null) return (null, null);

            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            if (shortcut is null) return (null, null);

            var shortcutType = shortcut.GetType();
            var target = shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            var icon = shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;

            return (target, icon);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }

    private static void AddIconCandidate(List<(Bitmap Image, int Priority, string Source)> target, string? path, int priority, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            using var icon = LoadIcon(path);
            if (icon is null) return;

            using var bitmap = icon.ToBitmap();
            if (LooksBlankBitmap(bitmap)) return;

            target.Add((new Bitmap(bitmap), priority, source));
        }
        catch (Exception ex)
        {
            AppLog.Error($"icon: не удалось прочитать {path}", ex);
        }
    }

    /// <summary>
    /// Steam: сначала прозрачный логотип (appcache\librarycache\&lt;appid&gt;_logo.png),
    /// и только в конце — постер _icon.jpg, где сам логотип обычно мелкий.
    /// </summary>
    private static void AddSteamCandidates(List<(Bitmap Image, int Priority, string Source)> target, AppEntry entry)
    {
        if (!entry.Target.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry.Target)) return;

        string? url = null;
        try
        {
            foreach (var line in File.ReadLines(entry.Target))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    url = line[4..].Trim();
                    break;
                }
            }
        }
        catch
        {
            return;
        }

        if (url is null || !url.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase)) return;

        var appId = url[19..].Split('/')[0].Trim();
        if (appId.Length == 0) return;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };

        foreach (var root in roots)
        {
            AddBitmapFile(target, Path.Combine(root, "appcache", "librarycache", appId + "_logo.png"), priority: 4, "steam logo");
            AddBitmapFile(target, Path.Combine(root, "appcache", "librarycache", appId + "_icon.jpg"), priority: 1, "steam poster");
        }
    }

    private static void AddBitmapFile(List<(Bitmap Image, int Priority, string Source)> target, string path, int priority, string source)
    {
        if (!File.Exists(path)) return;

        try
        {
            using var image = Image.FromFile(path);
            target.Add((new Bitmap(image), priority, source));
        }
        catch
        {
            // битый файл — пропускаем
        }
    }

    private static void AddShellCandidate(List<(Bitmap Image, int Priority, string Source)> target, AppEntry entry)
    {
        var temp = Path.Combine(Path.GetTempPath(), "hyprstart-icon-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            var parsingName = entry.Kind == AppEntryKind.Shortcut
                ? entry.Target
                : "shell:AppsFolder\\" + entry.Target;

            if (!ShellInterop.TrySaveIcon(parsingName, temp, IconSize) || LooksBlank(temp)) return;

            using var image = Image.FromFile(temp);
            target.Add((new Bitmap(image), 2, "оболочка"));
        }
        catch
        {
            // оболочка не дала иконку — не страшно
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // не критично
            }
        }
    }

    /// <summary>
    /// Грубая оценка «заполнения холста» без LockBits: доля непрозрачных точек
    /// на сетке 16x16. Используется только для ранжирования кандидатов —
    /// точную обрезку делает CropToContent, и только для победителя.
    /// </summary>
    private static double Coverage(Bitmap image)
    {
        if (image.Width <= 0 || image.Height <= 0) return 0;

        const int Grid = 16;
        int opaque = 0;

        for (int gy = 0; gy < Grid; gy++)
        {
            int y = Math.Min(image.Height - 1, (int)((gy + 0.5) * image.Height / Grid));

            for (int gx = 0; gx < Grid; gx++)
            {
                int x = Math.Min(image.Width - 1, (int)((gx + 0.5) * image.Width / Grid));

                if (image.GetPixel(x, y).A > 24) opaque++;
            }
        }

        return (double)opaque / (Grid * Grid);
    }

    /// <summary>Иконка игры из %Steam%\appcache\librarycache\&lt;appid&gt;_icon.jpg|png.</summary>
    private static Bitmap? UnusedFindSteamLibraryIcon(AppEntry entry)
    {
        if (!entry.Target.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry.Target)) return null;

        string? url = null;
        try
        {
            foreach (var line in File.ReadLines(entry.Target))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    url = line[4..].Trim();
                    break;
                }
            }
        }
        catch
        {
            return null;
        }

        if (url is null || !url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) return null;
        if (!url.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase)) return null;

        var appId = url[19..].Split('/')[0].Trim();
        if (appId.Length == 0) return null;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };

        foreach (var root in roots)
        {
            foreach (var extension in new[] { ".jpg", ".png" })
            {
                var path = Path.Combine(root, "appcache", "librarycache", appId + "_icon" + extension);
                if (!File.Exists(path)) continue;

                try
                {
                    using var image = Image.FromFile(path);
                    return new Bitmap(image);
                }
                catch
                {
                    // битый файл — идём дальше
                }
            }
        }

        return null;
    }

    /// <summary>Картинка от оболочки (через временный файл), чтобы её тоже можно было сравнить.</summary>
    private static Bitmap? TryShellBitmap(AppEntry entry)
    {
        var temp = Path.Combine(Path.GetTempPath(), "hyprstart-icon-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            var parsingName = entry.Kind == AppEntryKind.Shortcut
                ? entry.Target
                : "shell:AppsFolder\\" + entry.Target;

            if (!ShellInterop.TrySaveIcon(parsingName, temp, IconSize) || LooksBlank(temp)) return null;

            using var image = Image.FromFile(temp);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // не критично
            }
        }
    }

    /// <summary>
    /// Для .ico берём самый крупный кадр: конструктор с размером выбирает ближайший
    /// доступный кадр, а цикл снизу вверх гарантирует, что 16px не перекроет 256px.
    /// </summary>
    private static Icon? LoadIcon(string path)
    {
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            foreach (int size in new[] { 256, 128, 96, 64, 48, 32 })
            {
                try
                {
                    var candidate = new Icon(path, size, size);
                    if (candidate.Width >= Math.Min(size, 32)) return candidate;
                    candidate.Dispose();
                }
                catch
                {
                    // кадра такого размера нет — пробуем меньше
                }
            }

            return new Icon(path);
        }

        return Icon.ExtractAssociatedIcon(path);
    }

    /// <summary>Обрезает однотонные/прозрачные поля, оставляя только сам рисунок.</summary>
    private static Bitmap CropToContent(Bitmap source)
    {
        int width = source.Width;
        int height = source.Height;

        using var normalized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.DrawImage(source, 0, 0, width, height);
        }

        var data = normalized.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        int stride;

        try
        {
            stride = data.Stride;
            pixels = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            // разблокируем ДО любой работы с копиями: new Bitmap(locked) бросает исключение
            normalized.UnlockBits(data);
        }

        {
            // фон = средний цвет четырёх углов
            int bgR = 0, bgG = 0, bgB = 0, bgA = 0;
            int[] xs = { 0, width - 1, 0, width - 1 };
            int[] ys = { 0, 0, height - 1, height - 1 };

            for (int i = 0; i < 4; i++)
            {
                int offset = ys[i] * stride + xs[i] * 4;
                bgB += pixels[offset];
                bgG += pixels[offset + 1];
                bgR += pixels[offset + 2];
                bgA += pixels[offset + 3];
            }

            bgR /= 4; bgG /= 4; bgB /= 4; bgA /= 4;

            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;

                for (int x = 0; x < width; x++)
                {
                    int offset = row + x * 4;
                    int b = pixels[offset];
                    int g = pixels[offset + 1];
                    int r = pixels[offset + 2];
                    int a = pixels[offset + 3];

                    bool transparent = a <= 24;
                    int difference = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);

                    bool interesting = !transparent && (bgA <= 24 || difference > 36);
                    if (!interesting) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return new Bitmap(normalized);

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;

            if (cropWidth >= width * 0.85 && cropHeight >= height * 0.85)
            {
                return new Bitmap(normalized);
            }

            var result = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb);
            using var cropped = normalized.Clone(new Rectangle(minX, minY, cropWidth, cropHeight), PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(result))
            {
                graphics.DrawImage(cropped, 0, 0, cropWidth, cropHeight);
            }

            return result;
        }
    }

    /// <summary>Масштабирует картинку в квадрат с сохранением пропорций и центрированием.</summary>
    private static Bitmap ScaleIntoSquare(Bitmap source, int size)
    {
        var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;

            double scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

            graphics.DrawImage(source, (size - drawWidth) / 2, (size - drawHeight) / 2, drawWidth, drawHeight);
        }

        return target;
    }

    private static string Hash(string value)
    {
        // версия в ключе: после правок извлечения иконок кэш пересобирается автоматически
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(CacheVersion + value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static bool LooksBlank(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            if (new FileInfo(path).Length < 200) return true;

            using var bitmap = new Bitmap(path);
            return LooksBlankBitmap(bitmap);
        }
        catch
        {
            return true;
        }
    }

    private static bool LooksBlankBitmap(Bitmap bitmap)
    {
        if (bitmap.Width < 2 || bitmap.Height < 2) return true;

        int samples = 0;
        int blank = 0;

        for (int ix = 0; ix < 5; ix++)
        {
            for (int iy = 0; iy < 5; iy++)
            {
                int x = Math.Min(bitmap.Width - 1, bitmap.Width * ix / 4);
                int y = Math.Min(bitmap.Height - 1, bitmap.Height * iy / 4);
                var pixel = bitmap.GetPixel(x, y);
                samples++;

                bool white = pixel.A > 16 && pixel.R > 235 && pixel.G > 235 && pixel.B > 235;
                bool transparent = pixel.A <= 16;

                if (white || transparent) blank++;
            }
        }

        return samples > 0 && blank == samples;
    }

    /// <summary>Заглушка: тёмный квадрат, лавандовая рамка, первая буква имени.</summary>
    private static void SavePlaceholder(string name, string path)
    {
        try
        {
            using var bitmap = new Bitmap(IconSize, IconSize);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.FromArgb(0x0B, 0x0E, 0x17));

                using var pen = new Pen(Color.FromArgb(0x6E, 0x8B, 0xFF), 6f);
                graphics.DrawRectangle(pen, 3, 3, IconSize - 7, IconSize - 7);

                var letter = FirstLetter(name);
                using var brush = new SolidBrush(Color.FromArgb(0xE8, 0xE6, 0xE8));
                using var font = new Font("Cascadia Mono", 110f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                graphics.DrawString(letter, font, brush, new RectangleF(0, 0, IconSize, IconSize), format);
            }

            bitmap.Save(path, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            AppLog.Error($"index: не удалось нарисовать заглушку для '{name}'", ex);
        }
    }

    private static string FirstLetter(string name)
    {
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        }

        return "?";
    }
}
