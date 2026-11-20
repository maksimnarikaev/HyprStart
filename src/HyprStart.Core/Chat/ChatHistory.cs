namespace HyprStart.Core.Chat;

/// <summary>Роль сообщения в диалоге (совместимо с OpenAI/DeepSeek API).</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
}

/// <summary>Одно сообщение диалога.</summary>
public sealed record ChatMessage(ChatRole Role, string Content)
{
    public string RoleName => Role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        _ => "assistant",
    };
}

/// <summary>
/// История диалога мини-чата: жёсткий лимит на количество сообщений,
/// чтобы запрос оставался лёгким и дешёвым. Самое старое вытесняется первым.
/// </summary>
public sealed class ChatHistory
{
    /// <summary>Лимит «последних» сообщений: 6–8 по требованию задачи.</summary>
    public const int DefaultLimit = 8;

    private readonly List<ChatMessage> _messages = new();

    public ChatHistory(int limit = DefaultLimit)
    {
        Limit = limit < 0 ? 0 : limit;
    }

    public int Limit { get; }

    public int Count => _messages.Count;

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Добавляет сообщение и вытесняет самые старые сверх лимита.</summary>
    public void Add(ChatRole role, string content)
    {
        if (string.IsNullOrEmpty(content)) return;

        _messages.Add(new ChatMessage(role, content));
        Trim();
    }

    /// <summary>Обрезает историю до лимита, оставляя последние сообщения.</summary>
    public void Trim()
    {
        int extra = _messages.Count - Limit;
        if (extra > 0) _messages.RemoveRange(0, extra);
    }

    /// <summary>Сброс контекста (кнопка × или Ctrl+L).</summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// Формирует список для запроса: системный промпт + последние сообщения истории.
    /// </summary>
    public List<ChatMessage> BuildRequest(string systemPrompt)
    {
        var result = new List<ChatMessage>(_messages.Count + 1);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            result.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }
        result.AddRange(_messages);
        return result;
    }
}
