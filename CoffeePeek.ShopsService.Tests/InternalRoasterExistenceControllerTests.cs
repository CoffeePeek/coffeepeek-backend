using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Tests;

public sealed class InternalRoasterExistenceControllerTests
{
    private const string Secret = "test-internal-secret-at-least-32-characters";

    [Fact]
    public async Task Exists_RejectsCallsWithoutInternalSecret()
    {
        var repository = new StubRepository();
        var controller = CreateController(repository);

        var result = await controller.Exists("Coffee Circus", TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
        repository.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Exists_UsesExactNameQueryForTrustedCall()
    {
        var repository = new StubRepository { Exists = true };
        var controller = CreateController(repository);
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = Secret;

        var result = await controller.Exists("Coffee%_Circus", TestContext.Current.CancellationToken);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(true);
        repository.Name.Should().Be("Coffee%_Circus");
        repository.Calls.Should().Be(1);
    }

    private static InternalRoasterExistenceController CreateController(IQueryRoasterRepository repository) => new(
        repository, Options.Create(new GatewayAuthOptions { SecretKey = Secret }))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class StubRepository : IQueryRoasterRepository
    {
        public bool Exists { get; init; }
        public string? Name { get; private set; }
        public int Calls { get; private set; }

        public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        {
            Name = name;
            Calls++;
            return Task.FromResult(Exists);
        }

        public Task<Roaster[]> GetAll() => throw new NotImplementedException();
        public Task<IEnumerable<Roaster>> GetByIds(List<Guid> ids, CancellationToken ct) => throw new NotImplementedException();
        public Task<Roaster?> GetByIdWithShopsAndPhotosAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid?> GetIdByModerationId(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Roaster[] Items, int TotalCount)> GetByOwnerAsync(Guid ownerUserId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
    }
}
