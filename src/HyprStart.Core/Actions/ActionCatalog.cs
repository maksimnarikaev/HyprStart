namespace HyprStart.Core.Actions;

public enum ActionKind
{
    /// <summary>Команда с префиксом "&gt;" в поиске.</summary>
    Command,

    /// <summary>Страница «Параметров Windows» (ms-settings:).</summary>
    SettingsPage,
}

public sealed record ActionItem(string Name, string Payload, ActionKind Kind, string[] Keywords);

/// <summary>
/// Каталог быстрых действий и страниц «Параметров Windows».
/// Префикс "&gt;" в строке поиска переключает список в режим команд.
/// </summary>
public static class ActionCatalog
{
    public const string Prefix = ">";

    public static readonly IReadOnlyList<ActionItem> Commands = new[]
    {
        new ActionItem("Заблокировать компьютер", "lock", ActionKind.Command, new[] { "lock", "блок", "заблок" }),
        new ActionItem("Сон", "sleep", ActionKind.Command, new[] { "sleep", "сон", "спать" }),
        new ActionItem("Перезагрузка", "restart", ActionKind.Command, new[] { "restart", "перезагруз", "ребут" }),
        new ActionItem("Выключение", "shutdown", ActionKind.Command, new[] { "shutdown", "выключ", "выкл" }),
        new ActionItem("Параметры Windows", "settings", ActionKind.Command, new[] { "settings", "параметр", "настрой" }),
        new ActionItem("Диспетчер задач", "taskmgr", ActionKind.Command, new[] { "task", "диспетчер" }),
    };

    public static readonly IReadOnlyList<ActionItem> SettingsPages = new[]
    {
        new ActionItem("Параметры: Система", "ms-settings:", ActionKind.SettingsPage, new[] { "систем", "system" }),
        new ActionItem("Параметры: Дисплей", "ms-settings:display", ActionKind.SettingsPage, new[] { "дисплей", "экран", "display" }),
        new ActionItem("Параметры: Звук", "ms-settings:sound", ActionKind.SettingsPage, new[] { "звук", "sound", "audio" }),
        new ActionItem("Параметры: Уведомления", "ms-settings:notifications", ActionKind.SettingsPage, new[] { "уведомл", "notifications" }),
        new ActionItem("Параметры: Питание", "ms-settings:powersleep", ActionKind.SettingsPage, new[] { "питани", "сон", "power" }),
        new ActionItem("Параметры: Приложения", "ms-settings:appsfeatures", ActionKind.SettingsPage, new[] { "приложен", "apps" }),
        new ActionItem("Параметры: Bluetooth и устройства", "ms-settings:bluetooth", ActionKind.SettingsPage, new[] { "bluetooth", "устройств", "блютуз" }),
        new ActionItem("Параметры: Сеть и Интернет", "ms-settings:network", ActionKind.SettingsPage, new[] { "сеть", "интернет", "network", "wifi" }),
        new ActionItem("Параметры: Персонализация", "ms-settings:personalization", ActionKind.SettingsPage, new[] { "персонал", "обои", "тема", "personalization" }),
        new ActionItem("Параметры: Приложения по умолчанию", "ms-settings:defaultapps", ActionKind.SettingsPage, new[] { "умолчан", "default" }),
        new ActionItem("Параметры: Обновление Windows", "ms-settings:windowsupdate", ActionKind.SettingsPage, new[] { "обновлен", "update" }),
        new ActionItem("Параметры: Конфиденциальность", "ms-settings:privacy", ActionKind.SettingsPage, new[] { "конфиден", "privacy" }),
    };

    /// <summary>Команды по запросу после "&gt;": пустой запрос — весь список.</summary>
    public static List<ActionItem> MatchCommands(string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0) return Commands.ToList();

        return Commands
            .Where(item => Normalize(item.Name).Contains(needle)
                           || item.Keywords.Any(keyword => Normalize(keyword).Contains(needle)))
            .ToList();
    }

    /// <summary>Страницы «Параметров», подходящие под обычный поиск.</summary>
    public static List<ActionItem> MatchSettings(string query, int take = 4)
    {
        var needle = Normalize(query);
        if (needle.Length == 0) return new List<ActionItem>();

        return SettingsPages
            .Where(item => Normalize(item.Name).Contains(needle)
                           || item.Keywords.Any(keyword => Normalize(keyword).Contains(needle)))
            .Take(take)
            .ToList();
    }

    private static string Normalize(string? text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().ToLowerInvariant().Replace('ё', 'е');
}
