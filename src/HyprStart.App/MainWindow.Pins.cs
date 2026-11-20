using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HyprStart.App.Interop;
using HyprStart.App.Services;
using HyprStart.Core.Chat;
using HyprStart.Core.Input;
using HyprStart.Core.Library;
using HyprStart.Core.Layout;
using HyprStart.Core.Settings;
using WinForms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace HyprStart.App;

// закреплённое, папки, частое, перетаскивание плиток
public partial class MainWindow : Window
{
    // ---------- закреплённое / папки ----------

    private HyprStart.Core.Storage.PinnedItem? _openFolder;
    private HyprStart.Core.Storage.PinnedItem? _renameTarget;
    private bool _pinsWired;

    private void RefreshPins()
    {
        WirePinsOnce();

        var source = _openFolder is null
            ? _pins.Items
            : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

        var items = new List<PinVm>(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var mini = new List<ImageSource?>();

            if (item.IsFolder)
            {
                foreach (var child in item.Children.Take(4))
                {
                    mini.Add(IconForTarget(child.Target, child.Name, KindOf(child)));
                }
            }

            items.Add(new PinVm(item, IconForTarget(item.Target, item.Name, KindOf(item)), mini, i));
        }

        PinsList.ItemsSource = items;
        FolderList.ItemsSource = items;

        PinsRoot.Visibility = _openFolder is null ? Visibility.Visible : Visibility.Collapsed;
        FolderRoot.Visibility = _openFolder is null ? Visibility.Collapsed : Visibility.Visible;
        PinsHint.Visibility = _openFolder is null && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderTitle.Text = _openFolder?.Name ?? string.Empty;

        PanelPinned.Header = TextFx.Spaced(Localizer.T("PanelPinnedHeader")) + (_pins.Items.Count > 0 ? "   " + _pins.Items.Count : string.Empty);

        // Свежие плитки создаются с размером по умолчанию — сразу подгоняем под карточку.
        if (_lastLayout is { } layout) FitPinTiles(layout.Pinned, _settings.Current.HideChatPanel);
    }

    private void WirePinsOnce()
    {
        if (_pinsWired) return;
        _pinsWired = true;

        PanelPinned.Tag = CreateFolderButton();

        PinsList.PreviewMouseLeftButtonUp += OnPinsClick;
        FolderList.PreviewMouseLeftButtonUp += OnPinsClick;
        PinsList.PreviewMouseRightButtonDown += OnPinsRightButton;
        FolderList.PreviewMouseRightButtonDown += OnPinsRightButton;

        FolderList.AllowDrop = true;
        FolderList.PreviewMouseLeftButtonDown += OnPinsMouseDown;
        FolderList.MouseMove += OnPinsMouseMove;
        FolderList.Drop += OnPinsDrop;

        // Файлы из проводника добавляем в закреплённое, а не в открытую папку
        PanelPinned.AllowDrop = true;
        PanelPinned.DragOver += OnPinnedDragOver;
        PanelPinned.Drop += OnPinnedFilesDrop;
    }

    private static void OnPinnedDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPinnedFilesDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return;

        bool added = false;

        foreach (var path in paths)
        {
            if (System.IO.Directory.Exists(path) && _pins.AddPath(path)) added = true;
        }

        if (added) RefreshPins();
        e.Handled = true;
    }

    private System.Windows.Controls.Button CreateFolderButton()
    {
        var button = new System.Windows.Controls.Button
        {
            Content = Localizer.T("MenuNewFolder"),
            Style = (Style)FindResource("QuickButtonStyle"),
            Margin = new Thickness(0),
            Padding = new Thickness(8, 1, 8, 1),
            FontSize = 10.5,
        };

        button.Click += OnAddFolder;
        return button;
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var folder = _pins.AddFolder();
        RefreshPins();
        BeginRename(folder);
    }

    private void BeginRename(HyprStart.Core.Storage.PinnedItem item)
    {
        _renameTarget = item;
        RenameBox.Text = item.Name;
        RenameBox.Visibility = Visibility.Visible;

        // Ставим фокус после отрисовки, чтобы TextBox был уже в дереве визуалов.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            RenameBox.Focus();
            Keyboard.Focus(RenameBox);
            RenameBox.SelectAll();
            AppLog.Write($"pins: переименование '{item.Name}'");
        }));
    }

    private void EndRename(bool apply)
    {
        var target = _renameTarget;
        _renameTarget = null;
        RenameBox.Visibility = Visibility.Collapsed;

        if (apply && target is not null)
        {
            _pins.Rename(target, RenameBox.Text);
            RefreshPins();
        }
    }

    private void OnRenameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            EndRename(true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndRename(false);
        }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => EndRename(true);

    private void OnFolderBack(object sender, RoutedEventArgs e)
    {
        _openFolder = null;
        RefreshPins();
        AnimateView(PinsRoot);
    }

    /// <summary>Открыть вид папки пинов: содержимое её списка плиток.</summary>
    private void ShowFolderView(HyprStart.Core.Storage.PinnedItem folder)
    {
        _openFolder = folder;
        RefreshPins();
        AnimateView(FolderRoot);
    }

    private static void AnimateView(FrameworkElement view)
    {
        var duration = TimeSpan.FromMilliseconds(170);
        var ease = new System.Windows.Media.Animation.QuadraticEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        var shift = new TranslateTransform(0, 14);
        view.RenderTransform = shift;
        view.Opacity = 1;

        var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(14, 0, duration) { EasingFunction = ease };

        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(opacity, 45);
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 45);

        view.BeginAnimation(OpacityProperty, opacity);
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>Бросок пина на плитку: в корень закреплённого, а если бросили прямо на папку-плитку,
    /// то внутрь этой папки. Так проще перетаскивать между группами.</summary>
    private void DropIntoPinned(AppEntry entry)
    {
        var hit = PanelPinned.InputHitTest(Mouse.GetPosition(PanelPinned)) as DependencyObject;
        if (hit is null)
        {
            AppLog.Write($"drag: '{entry.Name}' брошен вне области закреплённого");
            return;
        }

        var list = _openFolder is null
            ? _pins.Items
            : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

        int index = IndexOfPin(hit);
        var folder = index >= 0 && index < list.Count && list[index].IsFolder ? list[index] : _openFolder;

        if (_pins.Add(entry, folder)) RefreshPins();
    }

    /// <summary>
    /// Ищем презентацию внутри плитки: поднимаемся по дереву до плитки
    /// ContentPresenter, где лежит наш шаблон (PinVm). Если не поднялись — это был content-лист
    /// другого типа, и плитки в нём не подходят нам по типу.
    /// </summary>
    private static PinVm? PinFromSource(DependencyObject? source)
    {
        var current = source;

        while (current is not null)
        {
            if (current is ContentPresenter presenter && presenter.DataContext is PinVm pin)
            {
                return pin;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnPinsClick(object sender, MouseButtonEventArgs e)
    {
        // Клик уже обработан мышью при отпускании — перерисовку не запускаем
        if (_pinDragging)
        {
            _pinDragging = false;
            _pinDragIndex = -1;
            return;
        }

        var pin = PinFromSource(e.OriginalSource as DependencyObject);
        if (pin is null) return;

        if (pin.Item.IsFolder)
        {
            ShowFolderView(pin.Item);
        }
        else
        {
            LaunchPinned(pin.Item);
        }

        e.Handled = true;
    }

    private void LaunchPinned(HyprStart.Core.Storage.PinnedItem item)
    {
        var entry = _entries.FirstOrDefault(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase))
                    ?? new AppEntry { Name = item.Name, Target = item.Target, Kind = (AppEntryKind)item.Kind };

        Launch(entry);
    }

    /// <summary>
    /// Alt+1..9 (или Ctrl+1..9) запускают закреплённое по номеру плитки.
    /// При зажатом Alt WPF присылает Key.System, а цифра лежит в SystemKey —
    /// поэтому читаем её оттуда, иначе сочетание молча не срабатывает.
    /// Если открыта папка пинов, номера адресуют ЕЁ элементы, а не корневой список.
    /// </summary>
    private bool TryActivatePinnedByDigit(System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == 0) return false;
        if (SearchBox.Text.Length > 0) return false;

        // Фокус в мини-чате: цифры принадлежат тексту сообщения, а не плиткам.
        // Без этой защиты Alt+<цифра> внутри чата запускал приложение и гасил оверлей.
        if (ReferenceEquals(Keyboard.FocusedElement, ChatInput)) return false;

        // Alt+цифра: e.Key == Key.System, а настоящая клавиша — в e.SystemKey.
        // Без этого сочетание доходило до нас как Key.System и молча игнорировалось.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        int digit = PinDigitFromKey(key);
        if (digit <= 0) return false;

        // Список, который сейчас видит пользователь: корень или содержимое открытой папки.
        // Без этого Alt+1 внутри папки запускал первую плитку корня, а не папки.
        var list = _openFolder is null
            ? (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_pins.Items
            : _openFolder.Children;

        // Гасим событие до проверки границ: иначе Windows проиграет системный
        // звук меню на Alt+digit даже тогда, когда такой плитки нет.
        e.Handled = true;

        int index = PinDigit.ResolveIndex(digit, _pins.Items.Count, list.Count);
        if (index < 0) return true;

        var item = list[index];
        AppLog.Write($"ui: Alt+{digit} -> '{item.Name}' ({( _openFolder is null ? "root" : "folder:" + _openFolder.Name )}, focused={Keyboard.FocusedElement?.GetType().Name})");

        if (item.IsFolder)
        {
            ShowFolderView(item);
            return true;
        }

        LaunchPinned(item);
        DismissRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// Номер плитки по клавише цифры: D1..D9 и NumPad1..NumPad9; 0 — не цифра.
    /// Логика живёт в <see cref="HyprStart.Core.Input.PinDigit"/> и покрыта тестами,
    /// чтобы UI-копия не разъехалась с проверенным правилом.
    /// </summary>
    private static int PinDigitFromKey(Key key)
        => HyprStart.Core.Input.PinDigit.FromWpfKey((int)key, (int)Key.D0, (int)Key.NumPad0);

    private void OnPinsRightButton(object sender, MouseButtonEventArgs e)
    {
        var pin = PinFromSource(e.OriginalSource as DependencyObject);
        if (pin is null) return;

        var menu = new ContextMenu
        {
            Background = (MediaBrush)FindResource("GlassTintBrush"),
            Foreground = (MediaBrush)FindResource("TextBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = 12,
        };

        if (pin.Item.IsFolder)
        {
            menu.Items.Add(MenuAction("MenuRename", () => BeginRename(pin.Item)));
            menu.Items.Add(MenuAction("MenuDeleteFolder", () =>
            {
                _pins.Remove(pin.Item);
                if (ReferenceEquals(_openFolder, pin.Item)) _openFolder = null;
                RefreshPins();
            }));
        }
        else if (System.IO.Directory.Exists(pin.Item.Target))
        {
            menu.Items.Add(MenuAction("MenuOpen", () => LaunchPinned(pin.Item)));
            menu.Items.Add(MenuAction("MenuOpenInTerminal", () => OpenInTerminal(pin.Item.Target)));
            menu.Items.Add(MenuAction("MenuRemove", () => { _pins.Remove(pin.Item); RefreshPins(); }));
        }
        else
        {
            menu.Items.Add(MenuAction("MenuLaunch", () => LaunchPinned(pin.Item)));
            menu.Items.Add(MenuAction("MenuLaunchAsAdmin", () => LaunchPinnedAsAdmin(pin.Item)));

            var intoFolder = new MenuItem
            {
                Header = Localizer.T("MenuToFolder"),
                Style = MenuItemStyle,
            };

            foreach (var folder in _pins.Items.Where(i => i.IsFolder && !ReferenceEquals(i, _openFolder)).ToList())
            {
                var target = folder;
                intoFolder.Items.Add(MenuActionLiteral(folder.Name, MenuGlyph.Folder, () =>
                {
                    _pins.MoveInto(target, pin.Item);
                    RefreshPins();
                }));
            }

            if (intoFolder.Items.Count == 0)
            {
                intoFolder.Items.Add(new MenuItem
                {
                    Header = Localizer.T("MenuNoFolders"),
                    IsEnabled = false,
                    Style = MenuItemStyle,
                });
            }

            menu.Items.Add(intoFolder);
            menu.Items.Add(MenuAction("MenuRemove", () =>
            {
                _pins.Remove(pin.Item);
                RefreshPins();
            }));
        }

        if (FindAncestor<Border>(e.OriginalSource as DependencyObject) is { } anchor) anchor.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Запуск закреплённого от админа (также по Ctrl + Shift+Enter).</summary>
    private void LaunchAdmin(AppEntry entry)
    {
        AppLog.Write($"ui: '{entry.Name}' запрошен от администратора");
        LaunchAsAdmin?.Invoke(entry);
    }

    private void LaunchPinnedAsAdmin(HyprStart.Core.Storage.PinnedItem item)
    {
        if (System.IO.Directory.Exists(item.Target)) return; // папка уже открыта в проводнике

        var entry = _entries.FirstOrDefault(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase))
                    ?? new AppEntry { Name = item.Name, Target = item.Target, Kind = (AppEntryKind)item.Kind };

        LaunchAdmin(entry);
    }

    /// <summary>Обновляет тексты пилюль из Theme.xaml (ресурсы — на случай смены темы).</summary>
    private static Style? ContextMenuStyle => System.Windows.Application.Current?.TryFindResource(typeof(ContextMenu)) as Style;

    private static Style? MenuItemStyle => System.Windows.Application.Current?.TryFindResource(typeof(MenuItem)) as Style;

    private void RefreshVolumeText()
    {
        _monitor.SampleVolume();
        PillVol.Text = _monitor.VolumePercent >= 0 ? $"VOL {_monitor.VolumePercent}%" : "VOL —";
    }

    /// <summary>Путь к папке для администратора нужен: в трее путь был бы не виден.</summary>
    private static string? ResolvePath(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return path;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return path;

            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            return shortcut?.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string ?? path;
        }
        catch
        {
            return path;
        }
    }

    private void ShowInExplorer(AppEntry entry)
    {
        var path = ResolvePath(entry.Target);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            AppLog.Write($"ui: запрашиваем админа '{path}'");
        }
        catch (Exception ex)
        {
            AppLog.Error($"ui: не удалось запросить администратора '{path}'", ex);
        }
    }

    /// <summary>Открыть в Windows Terminal, а не отдельным cmd/powershell.</summary>
    private void OpenInTerminal(string path)
    {
        if (!System.IO.Directory.Exists(path)) return;

        try
        {
            var startInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            AppLog.Write($"ui: терминал в '{path}'");
            return;
        }
        catch
        {
            // Windows Terminal не установлен — откатываемся на cmd
        }

        try
        {
            var fallback = new ProcessStartInfo("cmd.exe") { UseShellExecute = true };
            fallback.ArgumentList.Add("/k");
            fallback.ArgumentList.Add($"cd /d \"{path}\"");
            Process.Start(fallback);
        }
        catch (Exception ex)
        {
            AppLog.Error($"ui: не удалось открыть терминал в '{path}'", ex);
        }
    }

    /// <summary>
    /// Значок пункта контекстного меню. Раньше глиф подбирался по русскому префиксу
    /// заголовка (StartsWith("Запустить") и т.п.) - после перевода на EN он бы пропал,
    /// поэтому действие передаётся явно.
    /// </summary>
    public enum MenuGlyph
    {
        Launch,
        LaunchAsAdmin,
        Open,
        OpenInTerminal,
        Pin,
        Unpin,
        Remove,
        RemoveFromFrequent,
        Rename,
        DeleteFolder,
        ShowInExplorer,
        Folder,
    }

    private static string GlyphFor(MenuGlyph glyph) => glyph switch
    {
        MenuGlyph.Launch => "\uE756",
        MenuGlyph.LaunchAsAdmin => "\uE7EF",
        MenuGlyph.Open => "\uE8E5",
        MenuGlyph.OpenInTerminal => "\uE756",
        MenuGlyph.Pin => "\uE718",
        MenuGlyph.Unpin => "\uE77A",
        MenuGlyph.Remove => "\uE738",
        MenuGlyph.RemoveFromFrequent => "\uE737",
        MenuGlyph.Rename => "\uE8AC",
        MenuGlyph.DeleteFolder => "\uE74D",
        MenuGlyph.ShowInExplorer => "\uE838",
        MenuGlyph.Folder => "\uE8B7",
        _ => "\u2022",
    };

    /// <summary>Пункт меню по ключу словаря: заголовок переводится, глиф берётся из действия.</summary>
    private MenuItem MenuAction(string key, Action action)
        => MenuAction(key, GlyphForKey(key), action);

    /// <summary>Пункт меню с готовым заголовком (имя папки переводить не нужно).</summary>
    private MenuItem MenuActionLiteral(string header, MenuGlyph glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = MenuItemStyle,
            Tag = glyph,
        };

        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem MenuAction(string key, MenuGlyph glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = Localizer.T(key),
            Style = MenuItemStyle,
            Tag = glyph,
        };

        item.Click += (_, _) => action();
        return item;
    }

    private static MenuGlyph GlyphForKey(string key) => key switch
    {
        "MenuLaunch" => MenuGlyph.Launch,
        "MenuLaunchAsAdmin" => MenuGlyph.LaunchAsAdmin,
        "MenuOpen" => MenuGlyph.Open,
        "MenuOpenInTerminal" => MenuGlyph.OpenInTerminal,
        "MenuPin" => MenuGlyph.Pin,
        "MenuUnpin" => MenuGlyph.Unpin,
        "MenuRemove" => MenuGlyph.Remove,
        "MenuRemoveFromFrequent" => MenuGlyph.RemoveFromFrequent,
        "MenuRename" => MenuGlyph.Rename,
        "MenuDeleteFolder" => MenuGlyph.DeleteFolder,
        "MenuShowInExplorer" => MenuGlyph.ShowInExplorer,
        _ => MenuGlyph.Folder,
    };

    public void RefreshFrequent()
    {
        var vms = new List<FrequentVm>();
        foreach (var (target, count) in _usage.Top(_entries, 8))
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            vms.Add(new FrequentVm(entry, IconFor(entry), count));
        }

        FrequentList.ItemsSource = vms;
        FrequentHint.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelFrequent.Header = TextFx.Spaced(Localizer.T("PanelFrequentHeader")) + (vms.Count > 0 ? "   " + vms.Count : string.Empty);
    }

    private void OnAppsListRightButton(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not AppRowVm { IsFolder: false, Entry: { } entry }) return;

        var menu = new ContextMenu
        {
            Background = (MediaBrush)FindResource("GlassTintBrush"),
            Foreground = (MediaBrush)FindResource("TextBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = 12,
        };

        var pinItem = new MenuItem
        {
            Header = Localizer.T(_pins.Contains(entry) ? "MenuPin" : "MenuUnpin"),
            Style = MenuItemStyle,
            Tag = _pins.Contains(entry) ? MenuGlyph.Pin : MenuGlyph.Unpin,
        };
        pinItem.Click += (_, _) =>
        {
            bool added = _pins.Toggle(entry);
            AppLog.Write($"pins: '{entry.Name}' {(added ? "закреплён" : "откреплён")}");
            RefreshPins();
        };
        menu.Items.Add(pinItem);

        var launchItem = new MenuItem { Header = Localizer.T("MenuOpen"), Style = MenuItemStyle, Tag = MenuGlyph.Open };
        launchItem.Click += (_, _) => Launch(entry);
        menu.Items.Add(launchItem);

        var adminItem = new MenuItem
        {
            Header = Localizer.T("MenuLaunchAsAdmin"),
            Style = MenuItemStyle,
            Tag = MenuGlyph.LaunchAsAdmin,
        };
        adminItem.Click += (_, _) => LaunchAdmin(entry);
        menu.Items.Add(adminItem);
        menu.Items.Add(MenuAction("MenuShowInExplorer", () => ShowInExplorer(entry)));

        if (FindAncestor<Border>(e.OriginalSource as DependencyObject) is { } anchor) anchor.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------- перетаскивание плиток (только внутренние, для OLE-групп) ----------

    private bool _pinDragging;
    private long _pinPressAt;

    /// <summary>Порог перетаскивания плитки: чтобы не мешать клику, ждём удержание и drag.</summary>
    private const double PinDragThreshold = 16;
    private const long PinDragHoldMs = 220;

    private void OnPinsMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pinDragStart = e.GetPosition(PinsList);
        _pinDragIndex = IndexOfPin(e.OriginalSource as DependencyObject);
        _pinDragging = false;
        _pinPressAt = Environment.TickCount64;
    }

    private void OnPinsMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pinDragIndex < 0) return;

        var position = e.GetPosition(PinsList);

        if (!_pinDragging)
        {
            // Первое движение по плитке, чтобы отличить клик от начала захвата: настоящий drag начинается
            // только после паузы удержания
            if (Environment.TickCount64 - _pinPressAt < PinDragHoldMs) return;
            if (Math.Abs(position.X - _pinDragStart.X) < PinDragThreshold
                && Math.Abs(position.Y - _pinDragStart.Y) < PinDragThreshold) return;

            _pinDragging = true;
        }

        // Если навести на другую плитку списка-цели, меняем индексы наведения местами
        var source = (UIElement)sender;
        var hit = IndexOfPin(source.InputHitTest(e.GetPosition(source)) as DependencyObject);
        if (hit < 0 || hit == _pinDragIndex) return;

        if (_openFolder is null) _pins.Move(_pinDragIndex, hit);
        else _pins.MoveInFolder(_openFolder, _pinDragIndex, hit);

        _pinDragIndex = hit;
        RefreshPins();
    }

    private void OnPinsDrop(object sender, System.Windows.DragEventArgs e)
    {
        // Перетаскивание текста из другого списка
        if (e.Data.GetDataPresent(typeof(AppEntry)))
        {
            if (e.Data.GetData(typeof(AppEntry)) is AppEntry dropped)
            {
                var list = _openFolder is null
                    ? _pins.Items
                    : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

                int index = IndexOfPin(e.OriginalSource as DependencyObject);
                var folder = index >= 0 && index < list.Count && list[index].IsFolder ? list[index] : _openFolder;

                _pins.Add(dropped, folder);
                RefreshPins();
            }

            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(typeof(int))) return;

        int from = (int)e.Data.GetData(typeof(int))!;
        int to = IndexOfPin(e.OriginalSource as DependencyObject);

        if (to < 0) to = _pins.Items.Count - 1;
        _pins.Move(from, to);
        RefreshPins();
        e.Handled = true;
    }
    private int IndexOfPin(DependencyObject? source)
    {
        var element = FindAncestor<ContentPresenter>(source);
        if (element?.DataContext is PinVm pin)
        {
            var list = (IReadOnlyList<PinVm>)PinsList.ItemsSource;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], pin)) return i;
            }
        }

        return -1;
    }

    // ---------- быстрые ----------

    private void OnQuickDownloads(object sender, RoutedEventArgs e) => RunShell("shell:Downloads");
    private void OnQuickExplorer(object sender, RoutedEventArgs e) => RunShell("explorer.exe");

    private void OnQuickSettings(object sender, RoutedEventArgs e) => RunShell("ms-settings:");

    private void OnQuickTerminal(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true });
        }
        catch
        {
            RunShell("powershell.exe");
        }
    }

    private void OnMediaPrev(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TrySkipPreviousAsync().AsTask(), 0xB1);
    private void OnMediaPlayPause(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TryTogglePlayPauseAsync().AsTask(), 0xB3);
    private void OnMediaNext(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TrySkipNextAsync().AsTask(), 0xB0);

    /// <summary>
    /// Управляющий GSMTC: если сессия есть и жива, шлём команду ей,
    /// иначе (браузер / YouTube), если API не умеет — шлём глобальный медиа-ключ.
    /// </summary>
    private async Task RunMediaAsync(
        Func<Windows.Media.Control.GlobalSystemMediaTransportControlsSession, Task<bool>> action,
        byte fallbackVk)
    {
        bool ok = await _monitor.ControlPlaybackAsync(action, fallbackVk);
        AppLog.Write($"media: медиа {(ok ? "ок" : "мимо цели")} (код-клавиша 0x{fallbackVk:X2})");

        if (ok) OnTrackChanged();
    }

    private void RunShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            AppLog.Write($"quick: {target}");
            DismissRequested?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"quick: не удалось открыть {target}", ex);
        }
    }

    private void OnSleep(object sender, RoutedEventArgs e) => AskPower("sleep", Localizer.T("PowerAskSleep"));
    private void OnRestart(object sender, RoutedEventArgs e) => AskPower("restart", Localizer.T("PowerAskRestart"));
    private void OnShutdown(object sender, RoutedEventArgs e) => AskPower("shutdown", Localizer.T("PowerAskShutdown"));

    private void AskPower(string action, string question)
    {
        _pendingPower = action;
        PowerConfirmText.Text = question;
        PowerConfirm.Visibility = Visibility.Visible;
    }

    private void OnPowerCancel(object sender, RoutedEventArgs e)
    {
        _pendingPower = null;
        PowerConfirm.Visibility = Visibility.Collapsed;
    }

    private void OnPowerConfirm(object sender, RoutedEventArgs e)
    {
        var action = _pendingPower;
        _pendingPower = null;
        PowerConfirm.Visibility = Visibility.Collapsed;
        if (action is null) return;

        try
        {
            switch (action)
            {
                case "sleep":
                    SetSuspendState(false, false, false);
                    break;
                case "restart":
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });
                    break;
                case "shutdown":
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { UseShellExecute = true });
                    break;
            }

            AppLog.Write($"power: {action} подтверждён");
        }
        catch (Exception ex)
        {
            AppLog.Error($"power: не удалось выполнить {action}", ex);
        }
    }

    // ---------- подсветка / рамка ----------

    /// <summary>Одна кисть на карточку: красим её рамку градиентом, а изменение прозрачности анимируем.</summary>
    private readonly Dictionary<HeaderedContentControl, LinearGradientBrush> _panelBrushes = new();

    /// <summary>Порядок карточек, участвующих в подсветке фокуса.</summary>
    private HeaderedContentControl[] Panels => new[] { PanelApps, PanelPinned, PanelChat, PanelFrequent, PanelQuick };

    private MediaBrush BrushFor(HeaderedContentControl panel)
    {
        if (!_panelBrushes.TryGetValue(panel, out var brush))
        {
            brush = new LinearGradientBrush(Colors.White, Colors.White, 45);
            _panelBrushes[panel] = brush;
            panel.BorderBrush = brush;
        }

        // Берём цвета из градиента темы, чтобы рамка всегда подходила к палитре карточки.
        if (System.Windows.Application.Current?.TryFindResource("PanelBorderGradient") is LinearGradientBrush theme
            && theme.GradientStops.Count >= 2
            && brush.GradientStops.Count >= 2)
        {
            brush.GradientStops[0].Color = theme.GradientStops[0].Color;
            brush.GradientStops[1].Color = theme.GradientStops[1].Color;
        }

        return brush;
    }

    /// <summary>
    /// Плавная смена (120 мс): меняем прозрачность и толщину,
    /// у активной толщина 1.5, у остальных 1.0 — так виден фокус.
    /// На MouseLeave каждая карточка возвращается к обычному виду сама.
    /// </summary>
    private void SetPanelFocus(HeaderedContentControl? active, bool animate = true)
    {
        _focused = active;

        foreach (var panel in Panels)
        {
            var brush = BrushFor(panel);
            bool isActive = ReferenceEquals(panel, active);

            double target = isActive ? 1.0 : DimmedBorderOpacity;
            double thickness = isActive ? 1.5 : 1.0;

            if (animate)
            {
                var animation = new System.Windows.Media.Animation.DoubleAnimation(
                    target,
                    TimeSpan.FromMilliseconds(120));
                brush.BeginAnimation(MediaBrush.OpacityProperty, animation);

                panel.BeginAnimation(BorderThicknessProperty,
                    new System.Windows.Media.Animation.ThicknessAnimation(
                        new Thickness(thickness), TimeSpan.FromMilliseconds(120)));
            }
            else
            {
                brush.BeginAnimation(MediaBrush.OpacityProperty, null);
                brush.Opacity = target;

                panel.BeginAnimation(BorderThicknessProperty, null);
                panel.BorderThickness = new Thickness(thickness);
            }
        }
    }

    /// <summary>Прозрачность рамки неактивной карточки: чтобы активная была заметна.</summary>
    private const double DimmedBorderOpacity = 0.28;

    private void OnThemeApplied()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ThemePillText.Text = _themes.CurrentName;
            SetPanelFocus(_focused ?? PanelApps, animate: false);
            RefreshCavaGradient();
            AppLog.Write($"ui: тема сменилась на {_themes.CurrentName}");
        }));
    }
}
