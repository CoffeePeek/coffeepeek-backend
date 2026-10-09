using System.Diagnostics;
using System.Net.Http.Json;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public static class ShopsFaultControl
{
    public static bool IsConfigured => Environment.GetEnvironmentVariable("COFFEEPEEK_LIVE_FAULTS") == "1";
    public static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing isolated fault-test configuration: " + name);

    public static async Task ProxyAsync(string name, bool enabled, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsJsonAsync("http://127.0.0.1:8474/proxies/" + name, new { enabled }, ct);
        response.EnsureSuccessStatusCode();
    }

    public static async Task PostgresAckDelayAsync(bool enabled, CancellationToken ct)
    {
        using var http = new HttpClient();
        const string url = "http://127.0.0.1:8474/proxies/shops-postgres/toxics";
        using var response = enabled
            ? await http.PostAsJsonAsync(url, new { name = "commit-ack", type = "latency", stream = "downstream",
                toxicity = 1, attributes = new { latency = 30000, jitter = 0 } }, ct)
            : await http.DeleteAsync(url + "/commit-ack", ct);
        if (!enabled && response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }

    public static async Task UntilAsync(Func<Task<bool>> condition, CancellationToken ct, Process? worker = null)
    {
        var deadline = Stopwatch.StartNew();
        while (!await condition())
        {
            if (worker?.HasExited == true) throw new InvalidOperationException("Fault worker exited; inspect its log.");
            if (deadline.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("Fault-test checkpoint was not reached.");
            await Task.Delay(50, ct);
        }
    }

    public static string Marker(string root, string name) => Path.Combine(root, name);
    public static Task WaitMarkerAsync(string root, string name, CancellationToken ct, Process? worker = null) =>
        UntilAsync(() => Task.FromResult(File.Exists(Marker(root, name))), ct, worker);
}
