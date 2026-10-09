using CoffeePeek.Contract.Events;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class RoasterPhotoCommitObserver(Func<ShopPhotosAttachedEvent, Task> observe)
{
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    public Task Completed => completed.Task;
    public int Calls => Volatile.Read(ref calls);

    public async Task ObserveAsync(ShopPhotosAttachedEvent message)
    {
        await observe(message);
        Interlocked.Increment(ref calls);
        completed.TrySetResult();
    }
}
