using HyprStart.Core.Layout;

namespace HyprStart.Core.Settings;

public sealed class PanelLayoutSettings
{
    public double Margin { get; set; } = 36;
    public double Gap { get; set; } = 15;
    public double PolybarHeight { get; set; } = 26;
    public double LeftColumnPercent { get; set; } = 27;
    public double RightTopPercent { get; set; } = 62;
    public double FrequentPercent { get; set; } = 55;

    public LayoutSpec ToSpec() => new()
    {
        Margin = Margin,
        Gap = Gap,
        PolybarHeight = PolybarHeight,
        LeftColumnPercent = LeftColumnPercent,
        RightTopPercent = RightTopPercent,
        FrequentPercent = FrequentPercent,
    };
}

public sealed class AppSettings
{
    /// <summary>
    /// Версия схемы настроек. ВАЖНО: по умолчанию 0, а не текущая версия —
    /// иначе файл без поля version (старый) выглядел бы как актуальный и миграция не срабатывала.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Имя темы из themes/*.json.</summary>
    public string Theme { get; set; } = "Ice blue";

    /// <summary>Комбинация открытия меню. Формат: "Alt+Space", "Ctrl+Alt+H", "Shift+F12".</summary>
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>
    /// ОПАСНО. Перехват тапа Win через WH_KEYBOARD_LL.
    /// По умолчанию false: подавление keyup Win оставляло ОС в состоянии «Win зажат»,
    /// из-за чего весь ввод превращался в Win+&lt;клавиша&gt;. Включать только после
    /// переработки на инъекции клавиш.
    /// </summary>
    public bool InterceptWindowsKey { get; set; }

    /// <summary>DWM Acrylic вместо плоской альфы. По умолчанию выключен: в референсе блюра нет.</summary>
    public bool AcrylicBackdrop { get; set; }

    /// <summary>Тонировка стекла панелей в формате #AARRGGBB.</summary>
    public string AcrylicTint { get; set; } = "#B00B0E17";

    /// <summary>
    /// Тонировка подложки DWM (весь экран под оверлеем). Держим лёгкой:
    /// материал DWM накрывает весь прямоугольник окна и регионом не режется,
    /// поэтому сильная тонировка убила бы «плавающие панели».
    /// </summary>
    public string AcrylicBackdropTint { get; set; } = "#400B0E17";

    /// <summary>
    /// Режим подложки: "blur" — ACCENT_ENABLE_BLURBEHIND (чистое размытие, цвет задаёт тонировка),
    /// "acrylic" — ACCENT_ENABLE_ACRYLICBLURBEHIND (материал DWM, цвет частично его).
    /// </summary>
    public string AcrylicMode { get; set; } = "blur";

    public bool ShowPolybar { get; set; } = true;

    /// <summary>Автозапуск уже настроен при первом старте — второй раз реестр не трогаем.</summary>
    public bool AutostartConfigured { get; set; }

    public string AccentColor { get; set; } = "#FF3355";

    /// <summary>Цвет обводки всех панелей. По дизайну — холодный лавандовый.</summary>
    public string PanelBorderColor { get; set; } = "#6E8BFF";

    /// <summary>Толщина обводки панелей в DIP (по дизайну 1.5–2).</summary>
    public double PanelBorderThickness { get; set; } = 1.5;

    public PanelLayoutSettings Layout { get; set; } = new();

    /// <summary>Модель мини-чата. По умолчанию deepseek-chat, для других провайдеров задаётся в окне настроек.</summary>
    public string ChatModel { get; set; } = "deepseek-chat";

    /// <summary>
    /// Ключ API (DeepSeek/OpenAI/OpenRouter/Groq/Ollama). Обычно пусто: ключ берётся из переменной окружения
    /// DEEPSEEK_API_KEY. Поле — для тех, кто не хочет держать переменную.
    /// </summary>
    public string ChatApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Прокси для запросов мини-чата, например <c>http://127.0.0.1:7890</c>
    /// (Koala Clash / mihomo / другой локальный клиент). Пусто — системный прокси Windows.
    /// </summary>
    public string ChatProxy { get; set; } = string.Empty;

    /// <summary>
    /// Базовый адрес OpenAI-совместимого API: https://api.deepseek.com, https://api.openai.com/v1,
    /// https://openrouter.ai/api/v1, http://localhost:11434/v1 (Ollama). Пусто - DeepSeek по умолчанию.
    /// </summary>
    public string ChatBaseUrl { get; set; } = string.Empty;

    /// <summary>Скрыть панель MINI AI целиком: чат не грузит сеть и не занимает место в раскладке.</summary>
    public bool HideChatPanel { get; set; }

    /// <summary>Город и координаты для погоды; при первом старте определяются по IP.</summary>
    public WeatherLocationSettings Weather { get; set; } = new();

    /// <summary>
    /// true, если стартовый набор пинов уже подсеян (или пользователь сам удалил пины -
    /// тогда повторно навязывать не нужно).
    /// </summary>
    public bool SeedPinsLogged { get; set; }
}

public static class SettingsSchema
{
    /// <summary>
    /// 1 — первая версия (перехват Win включён, хоткея нет).
    /// 2 — хоткей Alt+Space, перехват Win выключен по умолчанию.
    /// </summary>
    public const int Version = 6;
}
