using System.Security.Cryptography;
using CoffeePeek.MediaService.Configuration;

namespace CoffeePeek.MediaService.Services.Variants;

/// <summary>Hash the immutable source without retaining it in memory; enforce an independent byte cap.</summary>
internal sealed class PhotoSourceDigestStream : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long length;
    public string Digest() => length > 0 ? Convert.ToHexStringLower(hash.GetHashAndReset())
        : throw new InvalidDataException("Empty photo source.");
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (length + buffer.Length > PhotoVariantOptions.MaxSourceBytes) throw new InvalidDataException("Photo source exceeds byte limit.");
        hash.AppendData(buffer); length += buffer.Length;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask; }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
    protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => length;
    public override long Position { get => length; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
