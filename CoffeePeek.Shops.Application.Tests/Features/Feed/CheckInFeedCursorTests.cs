using System;
using System.Text;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Feed;
using Xunit;

namespace CoffeePeek.Shops.Application.Tests.Features.Feed;

public class CheckInFeedCursorTests
{
    [Fact]
    public void CursorPreservesUtcPositionAndCanonicalFilterScope()
    {
        var position = new CheckInFeedPosition(DateTime.UtcNow, Guid.NewGuid());
        var scope = new CheckInFeedScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var cursor = CheckInFeedCursor.Encode(position, scope);
        Assert.DoesNotContain("+", cursor);
        Assert.DoesNotContain("/", cursor);
        Assert.DoesNotContain("=", cursor);
        Assert.Equal(position, CheckInFeedCursor.Decode(cursor, scope));
        Assert.Equal(DateTimeKind.Utc, CheckInFeedCursor.Decode(cursor, scope).PublishedAtUtc.Kind);
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(cursor, scope with { CityId = null }));
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(cursor, scope with { ShopId = Guid.NewGuid() }));
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(cursor, scope with { AuthorId = null }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("not+a/feed=cursor")]
    [InlineData("a b")]
    public void MalformedCursorsAreValidationErrors(string cursor) =>
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(cursor, new()));

    [Theory]
    [InlineData("2|1|11111111-1111-1111-1111-111111111111|-|-|-")]
    [InlineData("1|0|11111111-1111-1111-1111-111111111111|-|-|-")]
    [InlineData("1|9223372036854775807|11111111-1111-1111-1111-111111111111|-|-|-")]
    [InlineData("1|1|00000000-0000-0000-0000-000000000000|-|-|-")]
    [InlineData("1|1|11111111-1111-1111-1111-111111111111|-|-|-|extra")]
    public void UnknownVersionsAndInvalidPositionsAreRejected(string payload)
    {
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(cursor, new()));
    }

    [Fact]
    public void OversizedCursorIsRejected() =>
        Assert.Throws<ValidationException>(() => CheckInFeedCursor.Decode(new string('a', 513), new()));
}
