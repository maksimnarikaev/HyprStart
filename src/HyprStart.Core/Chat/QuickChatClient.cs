using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HyprStart.Core.Settings;

namespace HyprStart.Core.Chat;

/// <summary>
/// Конфигурация мини-чата. Ключ берётся из <c>DEEPSEEK_API_KEY</c>, но его можно
/// переопределить в settings.json (<c>chatApiKey</c>). Модель — <c>chatModel</c>.
/// </summary>
public sealed record QuickChatOptions
{
    public const string Endpoint = ChatEndpoint.DefaultBaseUrl + ChatEndpoint.CompletionsPath;
    public const string DefaultModel = "deepseek-chat";
    public const string EnvKeyName = "DEEPSEEK_API_KEY";

    /// <summary>Короткий системный промпт: без вступлений, 1–4 предложения.</summary>
    public const string SystemPrompt =
        "Ты — встроенный помощник лаунчера Windows. Отвечай максимально сжато и точно: " +
        "1–4 коротких предложения или готовый сниппет без пояснений. " +
        "Без вступлений, без пересказа вопроса, без извинений. Только суть.";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = DefaultModel;

    /// <summary>Base URL провайдера (OpenAI-совместимый). Пусто - дефолт DeepSeek.</summary>
    public string BaseUrl { get; init; } = string.Empty;
    /// <summary>Полный адрес Chat Completions. Собирается из Base URL настроек.</summary>
    /// <summary>Полный адрес запроса: собирается из <see cref="BaseUrl"/> по правилам OpenAI API.</summary>
    public string Url => ChatEndpoint.BuildUrl(BaseUrl);

    /// <summary>
    /// Явный прокси из settings.json (<c>chatProxy</c>), например <c>http://127.0.0.1:7890</c>.
    /// Пустая строка — прямой выход в интернет без прокси (см. <see cref="QuickChatClient.CreateHttpClient"/>).
    /// </summary>
    public string ProxyUrl { get; init; } = string.Empty;

    /// <summary>Разрешает ключ: настройки → переменная окружения. Пустая строка, если ключа нет.</summary>
    public static string ResolveApiKey(string? fromSettings, Func<string, string?>? readEnv = null)
    {
        // Единая логика в ChatEndpoint: её же использует окно настроек для подсказки «ключ не задан».
        return ChatEndpoint.ResolveApiKey(fromSettings, readEnv);
    }
}

/// <summary>
/// Один стриминговый запрос к DeepSeek. HttpClient — единый статический экземпляр
/// (никаких сокетов в состоянии гонки и никаких фоновых циклов).
/// </summary>
public sealed class QuickChatClient
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly HttpClient _http;

    private static HttpClient CreateHttpClient()
    {
        // Один инстанс на процесс: соединение переиспользуется, сокеты не текут.
        HttpClient client = NewClient(CreateHandler(null));
        return client;
    }

    /// <summary>
    /// Единый обработчик для всех клиентов: TLS 1.2/1.3, распаковка, переиспользование
    /// соединений. <paramref name="proxyUrl"/> пуст — прямой выход через TUN-адаптер,
    /// задан — явный <see cref="WebProxy"/> только для этого клиента.
    /// </summary>
    private static SocketsHttpHandler CreateHandler(string? proxyUrl)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // В VPN с TUN-режимом (Koala Clash / mihomo) трафик уже заворачивается на уровне
            // виртуального адаптера, и дополнительный HTTP-прокси поверх TUN рвёт TLS-рукопожатие
            // ("Received an unexpected EOF or 0 bytes from the transport stream"). Поэтому по
            // умолчанию UseProxy=false; явный прокси задаётся через chatProxy в settings.json.
            UseProxy = proxyUrl is not null,
            // Явный список протоколов: TLS 1.2/1.3. Старые версии сервер не принимает,
            // а системные настройки иногда оставляют только их.
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };

        if (proxyUrl is not null) handler.Proxy = new WebProxy(proxyUrl);
        return handler;
    }

    private static HttpClient NewClient(SocketsHttpHandler handler)
    {
        // Стрим идёт долго, но не бесконечно: жёсткий предел спасает от зависания.
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");
        return client;
    }

    public QuickChatOptions Options { get; }

    public QuickChatClient(QuickChatOptions options)
        : this(options, null)
    {
    }

    /// <summary>
    /// Перегрузка для тестов: позволяет подменить транспорт, не поднимая ни сервера,
    /// ни сторонних библиотек. В приложении всегда используется общий <see cref="Http"/>.
    /// </summary>
    public QuickChatClient(QuickChatOptions options, HttpMessageHandler? handler)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _http = handler is not null
            ? new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) }
            : CreateTransport(options.ProxyUrl);
    }

    /// <summary>
    /// Клиент с явным прокси из settings.json (<c>chatProxy</c>). Отдельный инстанс создаётся
    /// один раз на объект клиента: в приложении клиент живёт всё время работы окна.
    /// </summary>
    private static HttpClient CreateProxyClient(string proxyUrl) => NewClient(CreateHandler(proxyUrl));

    /// <summary>
    /// Транспорт по политике прокси: пустой <paramref name="proxyUrl"/> — общий прямой клиент
    /// (без прокси), заданный — отдельный клиент с явным <see cref="WebProxy"/>. Вынесено
    /// из конструктора, чтобы тест мог проверить саму политику, не ходя в сеть.
    /// </summary>
    public static HttpClient CreateTransport(string? proxyUrl) =>
        CreatePlan(proxyUrl).UsesProxy ? CreateProxyClient(proxyUrl!.Trim()) : Http;

    /// <summary>
    /// Политика прокси для текущей конфигурации: <c>true</c> — прямой выход без прокси
    /// (chatProxy пуст), <c>false</c> — используется явный прокси. Нужна тестам и SelfTest.
    /// </summary>
    public static bool UsesDirectConnection(string? proxyUrl) => !CreatePlan(proxyUrl).UsesProxy;

    /// <summary>Описание выбранного транспорта: включён ли прокси и какой адрес.</summary>
    public sealed record TransportPlan(bool UsesProxy, string? ProxyAddress);

    /// <summary>
    /// Единственное место, где решается вопрос «через прокси или напрямую»: конструктор,
    /// <see cref="CreateTransport"/> и тесты пользуются одним и тем же расчётом.
    /// </summary>
    public static TransportPlan CreatePlan(string? proxyUrl) =>
        string.IsNullOrWhiteSpace(proxyUrl)
            ? new TransportPlan(false, null)
            : new TransportPlan(true, proxyUrl.Trim());

    /// <summary>Есть ли ключ. Без ключа UI показывает подсказку, а не падает.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(Options.ApiKey);

    /// <summary>
    /// Стримит ответ построчно: каждая дельта отдаётся в <paramref name="onDelta"/>.
    /// Отмена токена рвёт HTTP-запрос немедленно (используется в Hide()).
    /// </summary>
    public async Task<ChatStreamResult> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        Action<string> onDelta,
        CancellationToken cancellationToken = default)
    {
        if (messages is null || messages.Count == 0)
            return ChatStreamResult.Failed(ChatMessages.Active.EmptyRequest);
        if (!HasApiKey)
            return ChatStreamResult.Failed(ChatMessages.Active.MissingKey);
        onDelta ??= _ => { };

        string body = BuildRequestBody(messages, Options.Model);

        try
        {
            HttpResponseMessage response;
            try
            {
                response = await SendStreamingAsync(body, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                // Сбой на этапе установки соединения (TLS-рукопожатие поверх TUN-адаптера,
                // «unexpected EOF or 0 bytes from the transport stream») — одна повторная
                // попытка через 200 мс: обычно второе рукопожатие проходит.
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                response = await SendStreamingAsync(body, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    string reason = SseParser.ReadErrorMessage(SseParser.ReadDataPayload(raw))
                                    ?? (string.IsNullOrWhiteSpace(raw) ? response.ReasonPhrase ?? ChatMessages.Active.HttpError : raw.Trim());
                    return ChatStreamResult.Failed($"HTTP {(int)response.StatusCode}: {Truncate(reason, 200)}");
                }

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var text = new StringBuilder();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;

                    string? payload = SseParser.ReadDataPayload(line);
                    if (payload is null) continue;

                    if (SseParser.IsDone(payload)) break;

                    string? error = SseParser.ReadErrorMessage(payload);
                    if (error is not null) return ChatStreamResult.Failed(error);

                    string? delta = SseParser.ReadDelta(payload);
                    if (string.IsNullOrEmpty(delta)) continue;

                    text.Append(delta);
                    onDelta(delta);
                }

                return ChatStreamResult.Completed(text.ToString());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ChatStreamResult.Cancelled();
        }
        catch (HttpRequestException ex)
        {
            return ChatStreamResult.Failed(ChatMessages.Active.NetworkUnavailable + Truncate(Describe(ex), 260));
        }
        catch (TaskCanceledException)
        {
            return ChatStreamResult.Failed(ChatMessages.Active.Timeout);
        }
    }

    /// <summary>Пауза перед единственной повторной попыткой установить соединение.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Один POST со стримом. HTTP/1.1 фиксируется явно: поверх TUN-адаптеров (Koala Clash /
    /// mihomo) апгрейд до HTTP/2 обрывает TLS — «Received an unexpected EOF or 0 bytes
    /// from the transport stream». <see cref="HttpVersionPolicy.RequestVersionExact"/>
    /// запрещает согласование версии, поэтому клиент не уйдёт в h2 даже при ALPN.
    /// </summary>
    private Task<HttpResponseMessage> SendStreamingAsync(string body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Options.Url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Options.ApiKey);

        // Запрос освобождается вместе с ответом: SendAsync с ResponseHeadersRead
        // держит поток открытым ещё долго после возврата.
        return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Разворачивает цепочку <see cref="Exception.InnerException"/>: у TLS/прокси-сбоев
    /// верхний Message всегда «The SSL connection could not be established», а причина
    /// (сертификат, отказ прокси, таймаут рукопожатия) лежит глубже.
    /// </summary>
    internal static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Length > 0 && !parts.Contains(current.Message))
                parts.Add(current.Message);
            if (parts.Count >= 4) break;
        }
        return parts.Count == 0 ? ex.GetType().Name : string.Join(" → ", parts);
    }

    /// <summary>
    /// Короткая сводка погоды одним GET-запросом через тот же <see cref="HttpClient"/>,
    /// что и чат (значит, работают те же прокси/TLS-настройки).
    /// Возвращает нормализованную строку или пустую строку при сбое.
    /// </summary>
    public async Task<string> GetWeatherAsync(string city, CancellationToken cancellationToken = default)
    {
        string url = ChatContext.WeatherUrl(city);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // wttr.in отдаёт text/plain, но просим именно текст и идентифицируем клиента.
            request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.0 HyprStart");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return string.Empty;

            string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ChatContext.NormalizeWeather(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Лёгкий веб-поиск через r.jina.ai поверх DuckDuckGo Lite тем же <see cref="HttpClient"/>,
    /// что и чат (значит, работают прокси/TLS-настройки и HTTP/1.1). r.jina.ai отдаёт чистый
    /// текст/markdown — без HTML-классов, без антибот-капчи, которая ловит прямые запросы
    /// к html.duckduckgo.com с VPN-узлов. Пустой список — сбой или пустая выдача.
    /// </summary>
    public async Task<IReadOnlyList<WebSearchResult>> SearchWebAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<WebSearchResult>();

        // Основная точка входа: DDG Lite через ридер (компактная выдача «1. Заголовок»).
        string body = await FetchTextAsync(WebSearch.BuildUrl(query), "text/plain", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<WebSearchResult> results = WebSearch.ParseResults(body);
        if (results.Count > 0)
            return await EnrichWithPageTextAsync(results, cancellationToken).ConfigureAwait(false);

        AppLogWeb($"основная выдача пуста ({body.Length} симв.), пробую резервную по «{query}»");

        // Резерв: DDG HTML через тот же ридер. r.jina.ai периодически закрывает lite-домен
        // (403 AbuseAlleviationError), но html-домен отдаёт выдачу с заголовками-ссылками.
        string fallbackBody = await FetchTextAsync(
                WebSearch.BuildFallbackUrl(query), "text/plain", cancellationToken)
            .ConfigureAwait(false);

        results = WebSearch.ParseHeadingResults(fallbackBody);
        if (results.Count == 0)
        {
            AppLogWeb($"резервная выдача тоже не распознана ({fallbackBody.Length} симв.) по «{query}»");
            return results;
        }

        AppLogWeb($"резервная выдача: {results.Count} результатов");
        return await EnrichWithPageTextAsync(results, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Догружает текст найденных страниц параллельно (не более <see cref="WebSearch.MaxPagesToFetch"/>).
    /// Заголовки и сниппеты из выдачи — только навигация: без текста страницы модель не может
    /// назвать дату релиза или цифру. Любая неудача по отдельной странице просто оставляет
    /// результат со сниппетом — общий поиск при этом не падает.
    /// </summary>
    private async Task<IReadOnlyList<WebSearchResult>> EnrichWithPageTextAsync(
        IReadOnlyList<WebSearchResult> results,
        CancellationToken cancellationToken)
    {
        var targets = new List<int>(WebSearch.MaxPagesToFetch);
        for (int i = 0; i < results.Count && targets.Count < WebSearch.MaxPagesToFetch; i++)
        {
            if (results[i].Url.Length > 0) targets.Add(i);
        }

        if (targets.Count == 0) return results;

        var enriched = results.ToArray();
        var tasks = new List<Task<(int Index, string Text)>>(targets.Count);

        foreach (int index in targets)
        {
            string url = results[index].Url;
            tasks.Add(FetchPageForResultAsync(index, url, cancellationToken));
        }

        (int Index, string Text)[] fetched = await Task.WhenAll(tasks).ConfigureAwait(false);

        int withText = 0;
        foreach ((int index, string text) in fetched)
        {
            if (text.Length == 0) continue;
            enriched[index] = enriched[index] with { PageText = text };
            withText++;
        }

        AppLogWeb($"страницы: {withText} из {targets.Count} дали текст");
        return enriched;
    }

    /// <summary>
    /// Тянет одну страницу через ридер и вырезает из неё содержательный текст.
    /// Возвращает пустую строку при любом сбое — вызывающая сторона деградирует молча.
    /// </summary>
    private async Task<(int Index, string Text)> FetchPageForResultAsync(
        int index,
        string url,
        CancellationToken cancellationToken)
    {
        // Тот же ридер, что и для выдачи: он отдаёт markdown и обходит антибот-заглушки.
        string readerUrl = WebSearch.PageReaderPrefix + url;
        string body = await FetchTextAsync(readerUrl, "text/plain", cancellationToken)
            .ConfigureAwait(false);

        if (body.Length == 0) return (index, string.Empty);

        // Ридер отдаёт «успешный» markdown и для страниц-заглушек: кэш-снимок, 403 из
        // источника, сообщение об антиботе. Такой текст фактов не содержит — выбрасываем,
        // чтобы он не вытеснил из бюджета контекста настоящие абзацы.
        if (WebPageExtractor.LooksLikeStub(body)) return (index, string.Empty);

        string text = WebPageExtractor.Extract(body);
        return (index, text);
    }

    /// <summary>
    /// Текущая погода из Open-Meteo: бесплатно, без ключа и без капчи. Возвращает готовую
    /// строку для системного промпта или пустую строку при сбое.
    /// </summary>
    public async Task<string> GetWeatherLiveAsync(CancellationToken cancellationToken = default)
    {
        string json = await FetchTextAsync(WebSearch.WeatherUrl, "application/json", cancellationToken)
            .ConfigureAwait(false);

        return ChatContext.NormalizeWeather(json);
    }

    /// <summary>
    /// Общий GET «в текст» с браузерным User-Agent и принудительным HTTP/1.1
    /// (HTTP/2 поверх TUN-адаптеров рвётся так же, как в стриме чата).
    /// </summary>
    private async Task<string> FetchTextAsync(string url, string accept, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", accept);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AppLogWeb($"HTTP {(int)response.StatusCode} для {ShortHost(url)}");
                return string.Empty;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLogWeb($"{ex.GetType().Name} для {ShortHost(url)}");
            return string.Empty;
        }
    }

    /// <summary>Хост для лога: полный URL с query в лог не пишем (там пользовательский запрос).</summary>
    private static string ShortHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : "?";

    /// <summary>Тихая запись диагностики поиска: Core не должен зависеть от UI-логгера.</summary>
    private static void AppLogWeb(string message) => WebSearchLog?.Invoke(message);

    /// <summary>Хук для логов: приложение подписывает его на AppLog, тесты — не подписывают.</summary>
    public static Action<string>? WebSearchLog { get; set; }

    /// <summary>Собирает тело запроса chat/completions со stream=true.</summary>
    public static string BuildRequestBody(IReadOnlyList<ChatMessage> messages, string model)
    {
        using var buffer = new MemoryStream();
        var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteString("model", string.IsNullOrWhiteSpace(model) ? QuickChatOptions.DefaultModel : model);
            writer.WriteBoolean("stream", true);
            writer.WriteNumber("temperature", 0.4);
            writer.WriteNumber("max_tokens", 700);
            writer.WriteStartArray("messages");
            foreach (var message in messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.RoleName);
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Итог стрима: успех/ошибка/отмена.</summary>
public sealed record ChatStreamResult(bool Ok, bool IsCancelled, string Text, string Error)
{
    public static ChatStreamResult Completed(string text) => new(true, false, text ?? string.Empty, string.Empty);
    public static ChatStreamResult Failed(string error) => new(false, false, string.Empty, error);
    public static ChatStreamResult Cancelled() => new(false, true, string.Empty, string.Empty);
}
