using HyprStart.Core.Chat;
using HyprStart.Core.Input;
using Xunit;

namespace HyprStart.Core.Tests;

public class SseParserTests
{
    [Fact]
    public void ReadDataPayload_ReturnsPayload_ForDataLine()
    {
        var payload = SseParser.ReadDataPayload("data: {\"id\":\"1\"}");

        Assert.Equal("{\"id\":\"1\"}", payload);
    }

    [Fact]
    public void ReadDataPayload_TrimsSpacesAfterColon()
    {
        var payload = SseParser.ReadDataPayload("data:   {\"a\":1}   ");

        Assert.Equal("{\"a\":1}", payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(": keep-alive")]
    [InlineData("event: message")]
    [InlineData("id: 42")]
    [InlineData("data:")]
    [InlineData(null)]
    public void ReadDataPayload_ReturnsNull_ForNonDataLines(string? line)
    {
        Assert.Null(SseParser.ReadDataPayload(line));
    }

    [Fact]
    public void ReadDelta_ExtractsContent()
    {
        const string chunk = "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Привет\"}}]}";

        Assert.Equal("Привет", SseParser.ReadDelta(chunk));
    }

    [Fact]
    public void ReadDelta_ExtractsEachPieceOfStreamedWord()
    {
        Assert.Equal("При", SseParser.ReadDelta("{\"choices\":[{\"delta\":{\"content\":\"При\"}}]}"));
        Assert.Equal("вет", SseParser.ReadDelta("{\"choices\":[{\"delta\":{\"content\":\"вет\"}}]}"));
    }

    [Theory]
    [InlineData("[DONE]")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"delta\":{}}]}")]
    [InlineData("{\"choices\":[{\"delta\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}")]
    [InlineData("{\"usage\":{\"total_tokens\":10}}")]
    [InlineData("not json at all")]
    [InlineData(null)]
    public void ReadDelta_ReturnsNull_ForNonContentChunks(string? chunk)
    {
        Assert.Null(SseParser.ReadDelta(chunk));
    }

    [Fact]
    public void IsDone_RecognisesSentinel()
    {
        Assert.True(SseParser.IsDone("[DONE]"));
        Assert.True(SseParser.IsDone("  [DONE]  "));
        Assert.False(SseParser.IsDone("{\"choices\":[]}"));
        Assert.False(SseParser.IsDone(null));
    }

    [Fact]
    public void ReadErrorMessage_ExtractsMessage()
    {
        const string error = "{\"error\":{\"message\":\"Insufficient Balance\",\"type\":\"x\"}}";

        Assert.Equal("Insufficient Balance", SseParser.ReadErrorMessage(error));
    }

    [Theory]
    [InlineData("{\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}")]
    [InlineData("[DONE]")]
    [InlineData("{\"error\":{}}")]
    [InlineData(null)]
    public void ReadErrorMessage_ReturnsNull_WhenNoError(string? chunk)
    {
        Assert.Null(SseParser.ReadErrorMessage(chunk));
    }
}

public class ChatHistoryTests
{
    [Fact]
    public void Add_KeepsMessages_InOrder()
    {
        var history = new ChatHistory();

        history.Add(ChatRole.User, "вопрос");
        history.Add(ChatRole.Assistant, "ответ");

        Assert.Equal(2, history.Count);
        Assert.Equal("вопрос", history.Messages[0].Content);
        Assert.Equal(ChatRole.Assistant, history.Messages[1].Role);
    }

    [Fact]
    public void Add_TrimsToLimit_KeepingNewest()
    {
        var history = new ChatHistory(6);

        for (int i = 1; i <= 10; i++)
        {
            history.Add(i % 2 == 1 ? ChatRole.User : ChatRole.Assistant, "m" + i);
        }

        Assert.Equal(6, history.Count);
        Assert.Equal("m5", history.Messages[0].Content);
        Assert.Equal("m10", history.Messages[^1].Content);
    }

    [Fact]
    public void Add_IgnoresEmptyContent()
    {
        var history = new ChatHistory();

        history.Add(ChatRole.User, "");
        history.Add(ChatRole.User, null!);

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        var history = new ChatHistory();
        history.Add(ChatRole.User, "a");
        history.Add(ChatRole.Assistant, "b");

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Empty(history.Messages);
    }

    [Fact]
    public void BuildRequest_PrependsSystemPrompt()
    {
        var history = new ChatHistory();
        history.Add(ChatRole.User, "2+2?");

        var request = history.BuildRequest("кратко");

        Assert.Equal(2, request.Count);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal("кратко", request[0].Content);
        Assert.Equal("2+2?", request[1].Content);
    }

    [Fact]
    public void BuildRequest_SkipsBlankSystemPrompt()
    {
        var history = new ChatHistory();
        history.Add(ChatRole.User, "q");

        var request = history.BuildRequest("   ");

        Assert.Single(request);
        Assert.Equal(ChatRole.User, request[0].Role);
    }

    [Fact]
    public void RoleName_MatchesApiContract()
    {
        Assert.Equal("system", new ChatMessage(ChatRole.System, "x").RoleName);
        Assert.Equal("user", new ChatMessage(ChatRole.User, "x").RoleName);
        Assert.Equal("assistant", new ChatMessage(ChatRole.Assistant, "x").RoleName);
    }
}

public class PinDigitTests
{
    [Theory]
    [InlineData(0x31, 1)]
    [InlineData(0x35, 5)]
    [InlineData(0x39, 9)]
    [InlineData(0x61, 1)]
    [InlineData(0x69, 9)]
    [InlineData(0x30, 0)]
    [InlineData(0x60, 0)]
    [InlineData(0x41, 0)]
    public void FromVirtualKey_MapsDigits(int virtualKey, int expected)
    {
        Assert.Equal(expected, PinDigit.FromVirtualKey(virtualKey));
    }

    [Fact]
    public void FromWpfKey_MapsD1ToD9()
    {
        const int keyD0 = 34;  // Key.D0
        for (int i = 1; i <= 9; i++)
        {
            Assert.Equal(i, PinDigit.FromWpfKey(keyD0 + i, keyD0, 74));
        }
    }

    [Fact]
    public void FromWpfKey_MapsNumPad1ToNumPad9()
    {
        const int keyNumPad0 = 74;
        for (int i = 1; i <= 9; i++)
        {
            Assert.Equal(i, PinDigit.FromWpfKey(keyNumPad0 + i, 34, keyNumPad0));
        }
    }

    [Fact]
    public void FromWpfKey_ReturnsZero_ForNonDigits()
    {
        Assert.Equal(0, PinDigit.FromWpfKey(34, 34, 74));   // D0
        Assert.Equal(0, PinDigit.FromWpfKey(74, 34, 74));   // NumPad0
        Assert.Equal(0, PinDigit.FromWpfKey(44, 34, 74));   // Key.PrintScreen
    }

    [Fact]
    public void EffectiveKey_UsesSystemKey_WhenAltIsHeld()
    {
        const int keySystem = 156;
        const int keyD3 = 36;

        Assert.Equal(keyD3, PinDigit.EffectiveKey(keySystem, keyD3, keySystem));
    }

    [Fact]
    public void EffectiveKey_UsesKey_WhenAltIsNotHeld()
    {
        const int keySystem = 156;
        const int keyD3 = 36;

        Assert.Equal(keyD3, PinDigit.EffectiveKey(keyD3, 0, keySystem));
    }

    [Theory]
    [InlineData(1, 8, true)]
    [InlineData(8, 8, true)]
    [InlineData(9, 8, false)]
    [InlineData(0, 8, false)]
    [InlineData(1, 0, false)]
    public void IsInRange_RespectsPinCount(int digit, int pinCount, bool expected)
    {
        Assert.Equal(expected, PinDigit.IsInRange(digit, pinCount));
    }

    /// <summary>
    /// Сценарий целиком: Alt зажат, WPF прислал Key.System, SystemKey = D4,
    /// а плиток 7 — должна запуститься четвёртая.
    /// </summary>
    [Fact]
    public void AltDigitScenario_ResolvesFourthPin()
    {
        const int keySystem = 156;
        const int keyD0 = 34;

        int effective = PinDigit.EffectiveKey(keySystem, keyD0 + 4, keySystem);
        int digit = PinDigit.FromWpfKey(effective, keyD0, 74);

        Assert.Equal(4, digit);
        Assert.True(PinDigit.IsInRange(digit, 7));
    }

    // ---------- выбор индекса: корень vs открытая папка ----------

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 4)]
    [InlineData(9, 8)]
    public void ResolveIndex_NoFolderSelected_UsesRootList(int digit, int expected)
    {
        // Папка не открыта (null) — номера адресуют корневой список «ЗАКРЕПЛЕНО».
        Assert.Equal(expected, PinDigit.ResolveIndex(digit, rootCount: 9, openFolderCount: null));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void ResolveIndex_FolderOpen_UsesFolderList(int digit, int expected)
    {
        // Открыта папка с 3 элементами: Alt+1..3 бьют по ней, а не по корню.
        Assert.Equal(expected, PinDigit.ResolveIndex(digit, rootCount: 9, openFolderCount: 3));
    }

    [Fact]
    public void ResolveIndex_FolderOpen_IgnoresRootBeyondFolderSize()
    {
        // В корне 9 плиток, в открытой папке — 2. Alt+3 не должен запускать корневую.
        Assert.Equal(-1, PinDigit.ResolveIndex(3, rootCount: 9, openFolderCount: 2));
        // А первые две — работают.
        Assert.Equal(0, PinDigit.ResolveIndex(1, rootCount: 9, openFolderCount: 2));
        Assert.Equal(1, PinDigit.ResolveIndex(2, rootCount: 9, openFolderCount: 2));
    }

    [Fact]
    public void ResolveIndex_OpenEmptyFolder_ResolvesNothing()
    {
        // Папка открыта, но пуста: 0 — это валидный «открыта, пустая», а не «корень».
        Assert.Equal(-1, PinDigit.ResolveIndex(1, rootCount: 9, openFolderCount: 0));
        Assert.Equal(-1, PinDigit.ResolveIndex(5, rootCount: 9, openFolderCount: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    public void ResolveIndex_OutOfDigitRange_ReturnsMinusOne(int digit)
    {
        Assert.Equal(-1, PinDigit.ResolveIndex(digit, rootCount: 9, openFolderCount: null));
        Assert.Equal(-1, PinDigit.ResolveIndex(digit, rootCount: 9, openFolderCount: 9));
    }

    [Fact]
    public void ResolveIndex_RootBeyondPinCount_ReturnsMinusOne()
    {
        // В корне всего 7 плиток — Alt+8 и Alt+9 не запускают ничего.
        Assert.Equal(6, PinDigit.ResolveIndex(7, rootCount: 7, openFolderCount: null));
        Assert.Equal(-1, PinDigit.ResolveIndex(8, rootCount: 7, openFolderCount: null));
        Assert.Equal(-1, PinDigit.ResolveIndex(9, rootCount: 7, openFolderCount: null));
    }
}

public class ChatContextTests
{
    [Fact]
    public void BuildSystemPrompt_CarriesDateAndCity()
    {
        string prompt = ChatContext.BuildSystemPrompt(QuickChatOptions.SystemPrompt, new DateTime(2026, 11, 19), "Владикавказ");

        Assert.Contains("2026-11-19", prompt);
        Assert.Contains("Владикавказ", prompt);
        Assert.Contains(QuickChatOptions.SystemPrompt, prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IgnoresBlankCity()
    {
        string prompt = ChatContext.BuildSystemPrompt(QuickChatOptions.SystemPrompt, new DateTime(2026, 1, 2), "   ");

        Assert.Contains("2026-01-02", prompt);
        Assert.DoesNotContain("город:", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/weather", true)]
    [InlineData("/WEATHER", true)]
    [InlineData("  /weather ", true)]
    [InlineData("/weathers", false)]
    [InlineData("какая погода?", false)]
    [InlineData("", false)]
    public void IsWeatherRequest_RecognizesCommand(string prompt, bool expected)
    {
        Assert.Equal(expected, ChatContext.IsWeatherRequest(prompt));
    }

    [Fact]
    public void BuildWeatherPrompt_CarriesSummaryAndDate()
    {
        string prompt = ChatContext.BuildWeatherPrompt(QuickChatOptions.SystemPrompt, new DateTime(2026, 11, 19), "Владикавказ", "summary-marker");

        Assert.Contains("summary-marker", prompt);
        Assert.Contains("2026-11-19", prompt);
    }

    [Fact]
    public void WeatherUrl_UsesShortFormat()
    {
        string url = ChatContext.WeatherUrl("Vladikavkaz");

        Assert.Equal("https://wttr.in/Vladikavkaz?format=3&lang=ru", url);
    }
}

public class QuickChatOptionsTests
{
    [Fact]
    public void ResolveApiKey_PrefersSettingsValue()
    {
        static string? Env(string name) => "from-env";

        Assert.Equal("from-settings", QuickChatOptions.ResolveApiKey(" from-settings ", Env));
    }

    [Fact]
    public void ResolveApiKey_FallsBackToEnvironment()
    {
        static string? Env(string name) => "from-env";

        Assert.Equal("from-env", QuickChatOptions.ResolveApiKey("   ", Env));
    }

    [Fact]
    public void ResolveApiKey_ReturnsEmpty_WhenNothingFound()
    {
        static string? Env(string name) => null;

        Assert.Equal(string.Empty, QuickChatOptions.ResolveApiKey(null, Env));
    }

    [Fact]
    public void ResolveApiKey_ReadsDeepSeekVariableName()
    {
        string? seen = null;
        string? Env(string name) { seen = name; return null; }

        QuickChatOptions.ResolveApiKey(string.Empty, Env);

        Assert.Equal("DEEPSEEK_API_KEY", seen);
    }

    [Fact]
    public void ProxyUrl_DefaultsToEmpty_MeaningDirectConnection()
    {
        // Пустой chatProxy — прямой выход без прокси (общий HttpClient с UseProxy=false).
        // Системный прокси НЕ подхватывается: в TUN-режиме VPN он рвёт TLS-рукопожатие.
        Assert.Equal(string.Empty, new QuickChatOptions().ProxyUrl);
        Assert.True(QuickChatClient.UsesDirectConnection(null));
        Assert.True(QuickChatClient.UsesDirectConnection(string.Empty));
        Assert.True(QuickChatClient.UsesDirectConnection("   "));
    }

    [Fact]
    public void ProxyUrl_IsCarriedIntoOptions()
    {
        var options = new QuickChatOptions { ProxyUrl = "http://127.0.0.1:7890" };

        Assert.Equal("http://127.0.0.1:7890", options.ProxyUrl);
        Assert.False(QuickChatClient.UsesDirectConnection(options.ProxyUrl));
    }

    [Fact]
    public void CreateTransport_WithoutProxy_ReturnsSharedDirectClient()
    {
        // Один и тот же инстанс на процесс: пустой chatProxy не создаёт новый клиент,
        // а значит и новый пул сокетов, и не включает прокси.
        var a = QuickChatClient.CreateTransport(string.Empty);
        var b = QuickChatClient.CreateTransport(null);

        Assert.Same(a, b);
    }

    [Fact]
    public void CreateTransport_WithProxy_BuildsSeparateClient()
    {
        // Явный прокси даёт отдельный клиент вместо общего «прямого».
        using var client = QuickChatClient.CreateTransport("http://127.0.0.1:7890");

        Assert.NotSame(QuickChatClient.CreateTransport(string.Empty), client);
    }

    [Fact]
    public void TransportPlan_WithoutProxy_IsDirect()
    {
        // Главное требование: без chatProxy трафик идёт напрямую, без системного прокси.
        var plan = QuickChatClient.CreatePlan(string.Empty);

        Assert.False(plan.UsesProxy);
        Assert.Null(plan.ProxyAddress);
    }

    [Fact]
    public void TransportPlan_WithProxy_UsesThatProxy()
    {
        var plan = QuickChatClient.CreatePlan("  http://127.0.0.1:7890  ");

        Assert.True(plan.UsesProxy);
        Assert.Equal("http://127.0.0.1:7890", plan.ProxyAddress);
    }

    [Fact]
    public void TransportPlan_IsSharedByCreateTransportAndConstructor()
    {
        // Конструктор и CreateTransport решают одинаково: расхождения быть не должно.
        var plan = QuickChatClient.CreatePlan("http://127.0.0.1:7890");
        var client = new QuickChatClient(new QuickChatOptions { ProxyUrl = plan.ProxyAddress! });

        Assert.True(plan.UsesProxy);
        Assert.Equal(plan.ProxyAddress, client.Options.ProxyUrl);
        Assert.False(QuickChatClient.UsesDirectConnection(client.Options.ProxyUrl));
    }

    [Fact]
    public void Client_WithProxyUrl_BuildsWebProxyHandler()
    {
        // Явный прокси не должен падать на создании клиента: WebProxy принимает
        // абсолютный http-адрес, TLS 1.2/1.3 задаётся в SocketsHttpHandler.
        var options = new QuickChatOptions
        {
            ApiKey = "test-key",
            ProxyUrl = "http://127.0.0.1:7890",
        };

        var client = new QuickChatClient(options);

        Assert.Same(options, client.Options);
    }

    [Fact]
    public void Client_WithoutProxyUrl_UsesDirectTransport()
    {
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "test-key" });

        Assert.Equal(string.Empty, client.Options.ProxyUrl);
        Assert.True(QuickChatClient.UsesDirectConnection(client.Options.ProxyUrl));
    }

    [Fact]
    public void Settings_ChatProxy_RoundTripsThroughCamelCaseJson()
    {
        // SettingsStore сериализует AppSettings с JsonNamingPolicy.CamelCase —
        // ключ в settings.json называется "chatProxy".
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        };

        var settings = new HyprStart.Core.Settings.AppSettings
        {
            ChatProxy = "http://127.0.0.1:7897",
        };

        string json = System.Text.Json.JsonSerializer.Serialize(settings, jsonOptions);
        Assert.Contains("\"chatProxy\":\"http://127.0.0.1:7897\"", json);

        var back = System.Text.Json.JsonSerializer.Deserialize<HyprStart.Core.Settings.AppSettings>(
            json, jsonOptions);

        Assert.NotNull(back);
        Assert.Equal("http://127.0.0.1:7897", back!.ChatProxy);
    }

    [Fact]
    public void Settings_MissingChatProxy_DefaultsToEmpty()
    {
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        };

        var back = System.Text.Json.JsonSerializer.Deserialize<HyprStart.Core.Settings.AppSettings>(
            "{\"version\":5,\"theme\":\"auto\"}", jsonOptions);

        Assert.NotNull(back);
        Assert.Equal(string.Empty, back!.ChatProxy);
    }

    [Fact]
    public void BuildRequestBody_StreamsAndCarriesSystemPrompt()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "кратко"),
            new(ChatRole.User, "2+2?"),
        };

        string body = QuickChatClient.BuildRequestBody(messages, "deepseek-chat");

        Assert.Contains("\"stream\":true", body);
        Assert.Contains("\"model\":\"deepseek-chat\"", body);
        Assert.Contains("\"role\":\"system\"", body);
        Assert.Contains("\"role\":\"user\"", body);
        Assert.Contains("2+2?", body);
    }

    [Fact]
    public void BuildRequestBody_FallsBackToDefaultModel()
    {
        string body = QuickChatClient.BuildRequestBody(
            new List<ChatMessage> { new(ChatRole.User, "hi") }, "  ");

        Assert.Contains("\"model\":\"deepseek-chat\"", body);
    }

    [Fact]
    public async Task StreamAsync_WithoutKey_ReportsFriendlyError()
    {
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = string.Empty });
        var deltas = new List<string>();

        var result = await client.StreamAsync(
            new List<ChatMessage> { new(ChatRole.User, "привет") },
            deltas.Add);

        Assert.False(result.Ok);
        Assert.False(result.IsCancelled);
        Assert.Contains(QuickChatOptions.EnvKeyName, result.Error);
        Assert.Empty(deltas);
    }

    [Fact]
    public async Task StreamAsync_EmptyRequest_ReportsError()
    {
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "k" });

        var result = await client.StreamAsync(Array.Empty<ChatMessage>(), _ => { });

        Assert.False(result.Ok);
    }

    // ---------- сквозной стриминг через подменённый транспорт ----------

    /// <summary>Транспорт-заглушка: отдаёт заранее заданный SSE-поток, сервер не нужен.</summary>
    private sealed class FakeSseHandler : HttpMessageHandler
    {
        private readonly string _sse;
        private readonly TaskCompletionSource _firstDelta;
        private readonly bool _hangAfterFirstDelta;

        public FakeSseHandler(string sse, bool hangAfterFirstDelta = false)
        {
            _sse = sse;
            _hangAfterFirstDelta = hangAfterFirstDelta;
            _firstDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task FirstDeltaSent => _firstDelta.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent(_sse, System.Text.Encoding.UTF8, "text/event-stream");
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };

            if (!_hangAfterFirstDelta) return response;

            // Стрим «зависает» после первой дельты: так проверяется отмена.
            var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            response.Content = new StreamContent(new HangingStream(stream, _firstDelta));
            return response;
        }
    }

    /// <summary>Поток, который отдаёт данные, а затем ждёт отмены — эмуляция долгого SSE.</summary>
    private sealed class HangingStream : System.IO.Stream
    {
        private readonly System.IO.Stream _inner;
        private readonly TaskCompletionSource _firstDelta;
        private int _reads;

        public HangingStream(System.IO.Stream inner, TaskCompletionSource firstDelta)
        {
            _inner = inner;
            _firstDelta = firstDelta;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Первое чтение отдаёт начало SSE и сразу сигналит «стрим пошёл»,
            // дальше висим до отмены — ровно как живой сокет на долгом ответе.
            if (_reads++ == 0)
            {
                int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                _firstDelta.TrySetResult();
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private static string Sse(params string[] deltas)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var delta in deltas)
        {
            string json = "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" +
                          delta.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}]}";
            sb.Append("data: ").Append(json).Append('\n').Append('\n');
        }
        sb.Append("data: [DONE]").Append('\n').Append('\n');
        return sb.ToString();
    }

    [Fact]
    public async Task StreamAsync_StreamsDeltasInOrder_AndConcatenatesText()
    {
        var handler = new FakeSseHandler(Sse("При", "вет", ", мир"));
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "k" }, handler);
        var deltas = new List<string>();

        var result = await client.StreamAsync(
            new List<ChatMessage> { new(ChatRole.User, "привет") }, deltas.Add);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(new[] { "При", "вет", ", мир" }, deltas);
        Assert.Equal("Привет, мир", result.Text);
    }

    [Fact]
    public async Task StreamAsync_StopsAtDoneSentinel_IgnoringTrailingChunks()
    {
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n" +
                     "data: [DONE]\n\n" +
                     "data: {\"choices\":[{\"delta\":{\"content\":\"ЛИШНЕЕ\"}}]}\n\n";
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "k" }, new FakeSseHandler(sse));
        var deltas = new List<string>();

        var result = await client.StreamAsync(
            new List<ChatMessage> { new(ChatRole.User, "x") }, deltas.Add);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("ok", result.Text);
        Assert.Equal(new[] { "ok" }, deltas);
    }

    [Fact]
    public async Task StreamAsync_ReportsHttpError_WithServerMessage()
    {
        var handler = new FakeStatusHandler(System.Net.HttpStatusCode.Unauthorized,
            "data: {\"error\":{\"message\":\"Invalid API key\"}}\n\n");
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "bad" }, handler);

        var result = await client.StreamAsync(
            new List<ChatMessage> { new(ChatRole.User, "x") }, _ => { });

        Assert.False(result.Ok);
        Assert.Contains("401", result.Error);
        Assert.Contains("Invalid API key", result.Error);
    }

    [Fact]
    public async Task StreamAsync_Cancellation_ReturnsCancelled_WithoutThrowing()
    {
        // Без [DONE]: поток обязан «зависнуть» на середине, иначе отмена не проверится.
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"нач\"}}]}\n\n";
        var handler = new FakeSseHandler(sse, hangAfterFirstDelta: true);
        var client = new QuickChatClient(new QuickChatOptions { ApiKey = "k" }, handler);
        using var cts = new CancellationTokenSource();

        var task = client.StreamAsync(
            new List<ChatMessage> { new(ChatRole.User, "x") }, _ => { }, cts.Token);

        // Отменяем, как только поток реально начал читаться (отдал первую порцию байт).
        var started = await Task.WhenAny(handler.FirstDeltaSent, Task.Delay(5000));
        Assert.Same(handler.FirstDeltaSent, started);
        await cts.CancelAsync();

        var result = await task;
        Assert.True(result.IsCancelled);
        Assert.False(result.Ok);
    }

    private sealed class FakeStatusHandler : HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _status;
        private readonly string _body;

        public FakeStatusHandler(System.Net.HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
