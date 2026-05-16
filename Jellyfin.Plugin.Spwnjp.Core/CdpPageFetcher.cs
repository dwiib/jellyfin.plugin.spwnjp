using System.Buffers;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Spwnjp.Core;

/// <summary>
/// Fetches pages by driving a headless-shell instance over the Chrome DevTools Protocol:
/// opens a new target on the URL, waits for the page to hydrate, evaluates
/// <c>document.documentElement.outerHTML</c> inside the target, then closes it.
/// Used when the user has configured a headless-shell URL — needed for spwn.jp pages whose
/// event data only appears post-render.
/// </summary>
public sealed class CdpPageFetcher : IPageFetcher
{
    /// <summary>
    /// Maximum wall-clock time to wait for a caller-supplied readiness expression to evaluate truthy.
    /// When this elapses we capture <c>outerHTML</c> anyway — readiness is best-effort, not a hard
    /// gate. The parser tolerates missing optional fields.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Interval between readiness polls.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Fallback wait used when the caller doesn't supply a readiness expression
    /// (raw <c>fetch &lt;url&gt;</c> from the CLI). Long enough for most pages
    /// to emit a load event; not long enough to fully hydrate a heavy SPA.
    /// </summary>
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CdpPageFetcher"/> class.
    /// </summary>
    /// <param name="http">An <see cref="HttpClient"/> used for the <c>/json/new</c> and <c>/json/close</c> control endpoints.</param>
    /// <param name="baseUrl">The headless-shell base URL (for example <c>http://localhost:9222</c>).</param>
    public CdpPageFetcher(HttpClient http, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseUrl);
        _http = http;
        _baseUrl = baseUrl;
    }

    /// <inheritdoc/>
    public async Task<PageFetchResult> FetchHtmlAsync(Uri url, string? readyExpression = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var sw = Stopwatch.StartNew();

        var target = await OpenTargetAsync(url, ct).ConfigureAwait(false);
        try
        {
            var wsUri = RewriteWebSocketHost(new Uri(target.WebSocketDebuggerUrl!));
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);

            if (readyExpression is null)
            {
                await Task.Delay(FallbackDelay, ct).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await WaitUntilReadyAsync(ws, readyExpression, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Readiness is best-effort. Fall through and capture whatever's in the DOM
                    // so e.g. an event without performers still returns the rest of the metadata.
                }
            }

            var html = await EvaluateOuterHtmlAsync(ws, ct).ConfigureAwait(false);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);

            sw.Stop();
            return new PageFetchResult(html, url, PageFetchBackend.Cdp, sw.Elapsed);
        }
        finally
        {
            await CloseTargetAsync(target.Id!).ConfigureAwait(false);
        }
    }

    private async Task<CdpTarget> OpenTargetAsync(Uri url, CancellationToken ct)
    {
        var openUrl = new Uri(_baseUrl, $"/json/new?{Uri.EscapeDataString(url.ToString())}");
        using var request = new HttpRequestMessage(HttpMethod.Put, openUrl);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var target = await response.Content.ReadFromJsonAsync<CdpTarget>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("CDP /json/new returned an empty body");
        if (string.IsNullOrEmpty(target.Id) || string.IsNullOrEmpty(target.WebSocketDebuggerUrl))
        {
            throw new InvalidOperationException("CDP /json/new response missing id or webSocketDebuggerUrl");
        }

        return target;
    }

    private async Task CloseTargetAsync(string targetId)
    {
        try
        {
            var closeUrl = new Uri(_baseUrl, $"/json/close/{targetId}");
            using var response = await _http.GetAsync(closeUrl, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Target may already be gone; swallow.
        }
    }

    private Uri RewriteWebSocketHost(Uri rawWs)
    {
        // headless-shell reports webSocketDebuggerUrl using its own view of the host
        // (e.g. ws://localhost:9222/...), which is unreachable when the shell runs
        // on a different machine than the caller. Substitute the configured base URL's host.
        return new UriBuilder(rawWs)
        {
            Scheme = _baseUrl.Scheme == "https" ? "wss" : "ws",
            Host = _baseUrl.Host,
            Port = _baseUrl.Port,
        }.Uri;
    }

    private static async Task WaitUntilReadyAsync(ClientWebSocket ws, string readyExpression, CancellationToken ct)
    {
        // Wrap the caller's expression in a try/catch so a parse error in the page
        // (querySelector throwing on bad input, etc.) doesn't kill the whole fetch.
        var wrapped = $"(function(){{ try {{ return !!({readyExpression}); }} catch (e) {{ return false; }} }})()";
        var deadline = Stopwatch.GetTimestamp() + (long)(ReadyTimeout.TotalSeconds * Stopwatch.Frequency);
        var requestId = 100;
        while (true)
        {
            var ready = await EvaluateBooleanAsync(ws, requestId++, wrapped, ct).ConfigureAwait(false);
            if (ready)
            {
                return;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                throw new TimeoutException(
                    $"CDP readiness expression did not become true within {ReadyTimeout.TotalSeconds}s: {readyExpression}");
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> EvaluateBooleanAsync(ClientWebSocket ws, int requestId, string expression, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CdpRequest(
            requestId,
            "Runtime.evaluate",
            new CdpEvaluateParams(expression, ReturnByValue: true)));
        await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

        var raw = await WaitForResponseAsync(ws, requestId, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _))
        {
            // Treat evaluation errors as "not ready yet" rather than fatal — the page
            // may still be loading the symbols our expression references.
            return false;
        }

        if (root.TryGetProperty("result", out var outer)
            && outer.TryGetProperty("result", out var inner)
            && inner.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static async Task<string> EvaluateOuterHtmlAsync(ClientWebSocket ws, CancellationToken ct)
    {
        const int RequestId = 1;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CdpRequest(
            RequestId,
            "Runtime.evaluate",
            new CdpEvaluateParams("document.documentElement.outerHTML", ReturnByValue: true)));
        await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

        var raw = await WaitForResponseAsync(ws, RequestId, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException("CDP Runtime.evaluate failed: " + err.GetRawText());
        }

        if (root.TryGetProperty("result", out var outer)
            && outer.TryGetProperty("result", out var inner)
            && inner.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        throw new InvalidOperationException("CDP Runtime.evaluate response shape unexpected: " + raw);
    }

    /// <summary>
    /// Reads frames until one arrives whose <c>id</c> matches <paramref name="requestId"/>;
    /// spontaneous CDP events (which have <c>method</c> but no matching <c>id</c>) are ignored.
    /// </summary>
    private static async Task<string> WaitForResponseAsync(ClientWebSocket ws, int requestId, CancellationToken ct)
    {
        while (true)
        {
            var raw = await ReceiveTextMessageAsync(ws, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("id", out var idProp)
                && idProp.ValueKind == JsonValueKind.Number
                && idProp.GetInt32() == requestId)
            {
                return raw;
            }
        }
    }

    private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(rented), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("CDP WebSocket closed mid-message");
                }

                ms.Write(rented, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private sealed record CdpTarget(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string? WebSocketDebuggerUrl);

    private sealed record CdpRequest(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] CdpEvaluateParams Params);

    private sealed record CdpEvaluateParams(
        [property: JsonPropertyName("expression")] string Expression,
        [property: JsonPropertyName("returnByValue")] bool ReturnByValue);
}
