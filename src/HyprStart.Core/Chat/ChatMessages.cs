namespace HyprStart.Core.Chat;

/// <summary>
/// Пользовательские тексты ошибок MINI AI. Core-слой не знает о WPF,
/// поэтому строки живут здесь, а не в словарях ресурсов окна.
/// Английский — язык по умолчанию (публичный репозиторий), см. <see cref="Russian"/>.
/// </summary>
public static class ChatMessages
{
    public const string EmptyRequest = "Empty request.";
    public const string MissingKey = "No API key: set DEEPSEEK_API_KEY or chatApiKey in settings.json.";
    public const string HttpError = "error";
    public const string NetworkUnavailable = "Network unavailable: ";
    public const string Timeout = "Response timed out.";
    public const string ErrorPrefix = "Error: ";

    /// <summary>
    /// Активный набор строк: по умолчанию английский. App-слой переключает его через
    /// <see cref="UseLanguage"/> при смене языка интерфейса.
    /// </summary>
    public static class Active
    {
        public static string EmptyRequest { get; private set; } = ChatMessages.EmptyRequest;
        public static string MissingKey { get; private set; } = ChatMessages.MissingKey;
        public static string HttpError { get; private set; } = ChatMessages.HttpError;
        public static string NetworkUnavailable { get; private set; } = ChatMessages.NetworkUnavailable;
        public static string Timeout { get; private set; } = ChatMessages.Timeout;
        public static string ErrorPrefix { get; private set; } = ChatMessages.ErrorPrefix;

        internal static void Use(string tag)
        {
            bool ru = string.Equals(tag, "ru", StringComparison.OrdinalIgnoreCase);
            EmptyRequest = ru ? Russian.EmptyRequest : ChatMessages.EmptyRequest;
            MissingKey = ru ? Russian.MissingKey : ChatMessages.MissingKey;
            HttpError = ru ? Russian.HttpError : ChatMessages.HttpError;
            NetworkUnavailable = ru ? Russian.NetworkUnavailable : ChatMessages.NetworkUnavailable;
            Timeout = ru ? Russian.Timeout : ChatMessages.Timeout;
            ErrorPrefix = ru ? Russian.ErrorPrefix : ChatMessages.ErrorPrefix;
        }
    }

    /// <summary>Переключает пользовательские сообщения на язык интерфейса (<c>en</c>/<c>ru</c>).</summary>
    public static void UseLanguage(string? tag) => Active.Use(tag ?? string.Empty);

    /// <summary>Русские варианты — те же ключи, для локали ru.</summary>
    public static class Russian
    {
        public const string EmptyRequest = "Пустой запрос.";
        public const string MissingKey = "Нет ключа: задай DEEPSEEK_API_KEY или chatApiKey в settings.json.";
        public const string HttpError = "ошибка";
        public const string NetworkUnavailable = "Сеть недоступна: ";
        public const string Timeout = "Таймаут ответа.";
        public const string ErrorPrefix = "Ошибка: ";
    }
}
