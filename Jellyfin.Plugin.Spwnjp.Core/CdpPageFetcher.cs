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
/// opens a new target on the URL, waits for the page to settle, evaluates
/// <c>document.documentElement.outerHTML</c> inside the target, then closes it.
/// Used when the user has configured a headless-shell URL — needed for spwn.jp pages whose
/// event data only appears post-render.
/// </summary>
public sealed class CdpPageFetcher : IPageFetcher
{
    // TODO: replace fixed delay with a Runtime.evaluate poll for the hero <img> element.
    // Smoke test showed event content was missing at 4s and present at 12s on a cold cache.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(12);

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
    public async Task<PageFetchResult> FetchHtmlAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var sw = Stopwatch.StartNew();

        var target = await OpenTargetAsync(url, ct).ConfigureAwait(false);
        try
        {
            var wsUri = RewriteWebSocketHost(new Uri(target.WebSocketDebuggerUrl!));
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);

            await Task.Delay(SettleDelay, ct).ConfigureAwait(false);

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

    private static async Task<string> EvaluateOuterHtmlAsync(ClientWebSocket ws, CancellationToken ct)
    {
        const int RequestId = 1;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CdpRequest(
            RequestId,
            "Runtime.evaluate",
            new CdpEvaluateParams("document.documentElement.outerHTML", ReturnByValue: true)));

        await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

        while (true)
        {
            var raw = await ReceiveTextMessageAsync(ws, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
            {
                // Spontaneous CDP event (has "method" but no "id"). Ignore.
                continue;
            }

            if (idProp.GetInt32() != RequestId)
            {
                continue;
            }

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
