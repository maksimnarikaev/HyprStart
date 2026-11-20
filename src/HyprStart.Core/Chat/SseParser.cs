using System.Text.Json;

namespace HyprStart.Core.Chat;

/// <summary>
/// Разбор SSE-потока DeepSeek/OpenAI-совместимого chat/completions.
/// Чистые функции без сети — чтобы покрывать тестами без HttpClient.
/// </summary>
public static class SseParser
{
    /// <summary>Маркер конца потока в OpenAI-совместимом SSE.</summary>
    public const string DoneSentinel = "[DONE]";

    /// <summary>
    /// Достаёт полезную нагрузку из строки SSE. Возвращает null, если строка
    /// не является data-строкой (комментарии ": ping", пустые строки, event:, id:).
    /// </summary>
    public static string? ReadDataPayload(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.AsSpan().Trim();
        if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) return null;

        var payload = trimmed[5..].Trim();
        return payload.Length == 0 ? null : payload.ToString();
    }

    /// <summary>
    /// Разбирает один data-чанк и возвращает дельту текста.
    /// Возвращает null, если чанк — [DONE], не содержит choices или delta.content пуст.
    /// </summary>
    public static string? ReadDelta(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        if (payload.Trim() == DoneSentinel) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("choices", out var choices)) return null;
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;

            var first = choices[0];
            if (first.ValueKind != JsonValueKind.Object) return null;
            if (!first.TryGetProperty("delta", out var delta)) return null;
            if (delta.ValueKind != JsonValueKind.Object) return null;
            if (!delta.TryGetProperty("content", out var content)) return null;
            if (content.ValueKind != JsonValueKind.String) return null;

            return content.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Является ли строка маркером конца потока.</summary>
    public static bool IsDone(string? payload)
        => payload is not null && payload.Trim() == DoneSentinel;

    /// <summary>Достаёт текст ошибки из чанка {"error": {"message": "..."}}, иначе null.</summary>
    public static string? ReadErrorMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Trim() == DoneSentinel) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("error", out var error)) return null;
            if (error.ValueKind != JsonValueKind.Object) return null;
            if (!error.TryGetProperty("message", out var message)) return null;
            return message.ValueKind == JsonValueKind.String ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
