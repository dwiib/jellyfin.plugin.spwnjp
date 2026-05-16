using Jellyfin.Plugin.Spwnjp.Core;

const string Usage = """
    usage: spwnjp-cli <command> <arg>
      event <event-id>     Fetch the event-detail page (https://spwn.jp/events/<id>).
      search <keyword>     Fetch the search results page (https://spwn.jp/search?keyword=<kw>).
      fetch <url>          Fetch an arbitrary URL.

    env:
      SPWNJP_HEADLESS_URL  Optional base URL of a headless-shell instance. If set,
                           pages are rendered through it via CDP. If unset, plain
                           HttpClient is used.
    """;

if (args.Length != 2)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var command = args[0];
var commandArg = args[1];

Uri target;
try
{
    target = command switch
    {
        "event" => Urls.EventUrl(commandArg),
        "search" => Urls.SearchUrl(commandArg),
        "fetch" => new Uri(commandArg),
        _ => throw new ArgumentException($"unknown command '{command}'"),
    };
}
catch (Exception ex) when (ex is ArgumentException or UriFormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

using var http = new HttpClient();
http.Timeout = TimeSpan.FromMinutes(2);

var headlessRaw = Environment.GetEnvironmentVariable("SPWNJP_HEADLESS_URL");
IPageFetcher fetcher;
if (!string.IsNullOrWhiteSpace(headlessRaw))
{
    if (!Uri.TryCreate(headlessRaw, UriKind.Absolute, out var headlessUri))
    {
        Console.Error.WriteLine($"SPWNJP_HEADLESS_URL is not a valid absolute URL: {headlessRaw}");
        return 2;
    }

    fetcher = new CdpPageFetcher(http, headlessUri);
}
else
{
    fetcher = new DirectPageFetcher(http);
}

Console.Error.WriteLine($"[cli] backend={fetcher.GetType().Name} url={target}");

try
{
    var result = await fetcher.FetchHtmlAsync(target).ConfigureAwait(false);
    Console.Error.WriteLine(
        $"[cli] ok backend={result.Backend} bytes={result.Html.Length} elapsed={result.Elapsed.TotalSeconds:F1}s");
    Console.Out.WriteLine(result.Html);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[cli] error: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
