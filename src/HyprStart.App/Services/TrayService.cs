using System.Drawing;
using System.Windows.Forms;
using HyprStart.App.Interop;

namespace HyprStart.App.Services;

/// <summary>Иконка в трее + меню (открыть, автозапуск, выход).</summary>
internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _autostartItem;

    public TrayService(OverlayController overlay, ThemeService themes, SettingsStore settings, Action shutdown)
    {
        _icon = CreateIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "HyprStart — меню в стиле Hyprland",
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Открыть  (Win)", null, (_, _) => overlay.Toggle()));

        // Подменю «Тема → …»: применяется сразу, без перезапуска.
        var themeMenu = new ToolStripMenuItem("Тема");

        foreach (var theme in themes.Themes)
        {
            var item = new ToolStripMenuItem(theme.Name) { Checked = theme.Name == themes.CurrentName };
            var name = theme.Name;

            item.Click += (_, _) =>
            {
                if (!themes.ApplyByName(name)) return;

                settings.Current.Theme = name;
                settings.Save();

                // ToolStripSeparator не приводится к ToolStripMenuItem — фильтруем по типу
                foreach (var sibling in themeMenu.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    sibling.Checked = string.Equals(sibling.Text, name, StringComparison.CurrentCultureIgnoreCase);
                }

                AppLog.Write($"tray: тема переключена на '{name}'");
            };

            themeMenu.DropDownItems.Add(item);
        }

        // при открытии подменю сверяем галочки с фактической темой (её могли сменить из polybar)
        themeMenu.DropDownOpening += (_, _) =>
        {
            foreach (var sibling in themeMenu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                sibling.Checked = string.Equals(sibling.Text, themes.CurrentName, StringComparison.CurrentCultureIgnoreCase);
            }
        };

        themeMenu.DropDownItems.Add(new ToolStripSeparator());
        themeMenu.DropDownItems.Add(new ToolStripMenuItem("Открыть папку тем", null, (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(themes.ThemesDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Error("tray: не удалось открыть папку тем", ex);
            }
        }));

        menu.Items.Add(themeMenu);

        _autostartItem = new ToolStripMenuItem("Автозапуск")
        {
            CheckOnClick = true,
            Checked = AutostartService.IsEnabled(),
        };
        _autostartItem.CheckedChanged += (_, _) =>
        {
            AutostartService.Set(_autostartItem.Checked);
            AppLog.Write($"tray: автозапуск = {_autostartItem.Checked}");
        };
        menu.Items.Add(_autostartItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => shutdown()));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => overlay.Toggle();
    }

    public void Install()
    {
        _notifyIcon.Visible = true;
        AppLog.Write("tray: icon installed");
    }

    /// <summary>Иконка рисуется кодом: тёмный квадрат, красная рамка, монограмма HS.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(0x0F, 0x0F, 0x19));
            using var pen = new Pen(Color.FromArgb(0xFF, 0x33, 0x55), 2f);
            graphics.DrawRectangle(pen, 1, 1, 29, 29);
            using var brush = new SolidBrush(Color.FromArgb(0xE8, 0xE6, 0xE8));
            using var font = new Font("Cascadia Mono", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString("HS", font, brush, new RectangleF(0, 0, 32, 32), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void RefreshAutostartState() => _autostartItem.Checked = AutostartService.IsEnabled();

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
