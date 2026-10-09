using System.Diagnostics;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class ShopsFaultProcess : IAsyncDisposable
{
    public Process Process { get; }
    private readonly string log;
    private readonly object gate = new();
    private int disposed;

    public ShopsFaultProcess(string role, string root, string connection, Guid entityId, string checkpoint = "",
        string? mediaConnection = null, string? bucket = null, Guid? actor = null, bool execute = false)
    {
        log = Path.Combine(root, role + "-" + Guid.NewGuid().ToString("N") + ".log");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { typeof(ShopsFaultWorkerTests).Assembly.Location, "-class",
                     typeof(ShopsFaultWorkerTests).FullName!, "-failSkips", "-noAutoReporters" }) start.ArgumentList.Add(arg);
        start.Environment["CP_FAULT_ROLE"] = role;
        start.Environment["CP_FAULT_ROOT"] = root;
        start.Environment["CP_FAULT_DB"] = connection;
        start.Environment["CP_FAULT_ENTITY"] = entityId.ToString();
        start.Environment["CP_FAULT_CHECKPOINT"] = checkpoint;
        start.Environment["CP_FAULT_EXECUTE"] = execute ? "1" : "0";
        if (mediaConnection is not null) start.Environment["CP_FAULT_MEDIA_DB"] = mediaConnection;
        if (bucket is not null) start.Environment["CP_FAULT_BUCKET"] = bucket;
        if (actor.HasValue) start.Environment["CP_FAULT_ACTOR"] = actor.Value.ToString();
        Process = new Process { StartInfo = start };
        Process.OutputDataReceived += Write;
        Process.ErrorDataReceived += Write;
        if (!Process.Start()) throw new InvalidOperationException("Fault worker could not start.");
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    private void Write(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is not null) lock (gate) File.AppendAllText(log, args.Data + Environment.NewLine);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        await Process.WaitForExitAsync();
        Process.Dispose();
    }
}
