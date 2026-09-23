using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Persistence.Extensions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class TransientDbConflictTests
{
    [Theory]
    [InlineData("23505", true)]  // unique violation: competing insert committed
    [InlineData("40001", true)]  // serialization failure
    [InlineData("23503", false)] // FK violation: permanent
    [InlineData("22003", false)] // numeric overflow: permanent (e.g. bogus parsed menu price)
    public void OnlyTransientPostgresErrorsAreRetried(string sqlState, bool expected)
    {
        var ex = new ConflictException("conflict",
            new DbUpdateException("save failed", new PostgresException("x", "ERROR", "ERROR", sqlState)));

        TransientDbConflict.Matches(ex).Should().Be(expected);
    }

    [Fact]
    public void ConcurrencyConflict_IsRetried() =>
        TransientDbConflict.Matches(new DbUpdateConcurrencyException()).Should().BeTrue();
}
