namespace CoffeePeek.MediaService.Services.Backfill;

public sealed class PhotoBackfillObject(Stream body, long? length, IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Body { get; } = body;
    public long? Length { get; } = length;

    public async ValueTask DisposeAsync()
    {
        try { await Body.DisposeAsync(); }
        finally { owner?.Dispose(); }
    }
}
