using System.Text.Json;
using Jellyfin.Plugin.Spwnjp.Core;

const string Usage = """
    usage: spwnjp-cli <command> <arg>
      event <event-id>     Look up an event and print the parsed metadata as JSON.
      search <keyword>     Run a search and print the parsed results as JSON.
      fetch <url>          Fetch a URL and print the raw HTML (no parsing).

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

Console.Error.WriteLine($"[cli] backend={fetcher.GetType().Name}");

var jsonOpts = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

try
{
    switch (command)
    {
        case "fetch":
        {
            if (!Uri.TryCreate(commandArg, UriKind.Absolute, out var url))
            {
                Console.Error.WriteLine($"not an absolute URL: {commandArg}");
                return 2;
            }

            var result = await fetcher.FetchHtmlAsync(url).ConfigureAwait(false);
            Console.Error.WriteLine($"[cli] ok backend={result.Backend} bytes={result.Html.Length} elapsed={result.Elapsed.TotalSeconds:F1}s");
            Console.Out.WriteLine(result.Html);
            return 0;
        }

        case "event":
        {
            var client = new SpwnClient(fetcher);
            var ev = await client.GetEventAsync(commandArg).ConfigureAwait(false);
            Console.Error.WriteLine($"[cli] ok event={ev.EventId} title={ev.Title!} performers={ev.Performers.Count}");
            Console.Out.WriteLine(JsonSerializer.Serialize(ev, jsonOpts));
            return 0;
        }

        case "search":
        {
            var client = new SpwnClient(fetcher);
            var results = await client.SearchAsync(commandArg).ConfigureAwait(false);
            Console.Error.WriteLine($"[cli] ok results={results.Count}");
            Console.Out.WriteLine(JsonSerializer.Serialize(results, jsonOpts));
            return 0;
        }

        default:
            Console.Error.WriteLine($"unknown command: {command}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[cli] error: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
