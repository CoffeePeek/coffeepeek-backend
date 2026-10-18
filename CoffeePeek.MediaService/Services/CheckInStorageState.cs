using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.MediaService.Services;

/// <summary>A bounded readiness lease, separate from per-reader business authorization.</summary>
public sealed class CheckInStorageState(TimeProvider clock)
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(30);
    private long lastSuccess;
    private int verified;

    public bool IsReady => Volatile.Read(ref verified) == 1
        && clock.GetElapsedTime(Interlocked.Read(ref lastSuccess)) < MaximumAge;

    public void MarkVerified(long observedAt)
    {
        Interlocked.Exchange(ref lastSuccess, observedAt);
        Volatile.Write(ref verified, 1);
    }

    public void Invalidate() => Volatile.Write(ref verified, 0);

    public void RequireReady()
    {
        if (!IsReady) throw new ServiceUnavailableException("Private photo storage is unavailable.");
    }
}
