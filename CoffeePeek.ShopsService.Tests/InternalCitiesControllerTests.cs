using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public class InternalCitiesControllerTests
{
    [Theory]
    [InlineData("found")]
    [InlineData("missing")]
    [InlineData("untrusted")]
    public async Task ReadCity_RequiresTrustedServiceAndReturnsCityName(string scenario)
    {
        var city = new City("Брест");
        var ct = TestContext.Current.CancellationToken;
        var cities = new Mock<IQueryCityRepository>(MockBehavior.Strict);
        if (scenario != "untrusted")
            cities.Setup(c => c.GetById(city.Id, ct)).ReturnsAsync(scenario == "found" ? city : null);
        var controller = new InternalCitiesController(cities.Object, Options.Create(new GatewayAuthOptions
        {
            SecretKey = "test-city-lookup-secret"
        })) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        if (scenario != "untrusted")
            controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "test-city-lookup-secret";

        var result = await controller.Get(city.Id, ct);

        if (scenario == "untrusted")
        {
            Assert.IsType<UnauthorizedResult>(result);
            cities.VerifyNoOtherCalls();
        }
        else if (scenario == "missing") Assert.IsType<NotFoundResult>(result);
        else
        {
            var dto = Assert.IsType<CityDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(city.Id, dto.Id);
            Assert.Equal("Брест", dto.Name);
        }
    }
}
