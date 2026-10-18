namespace CoffeePeek.MediaService.Tests;

internal sealed class NonSeekableImageStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override bool CanSeek => false;
}
