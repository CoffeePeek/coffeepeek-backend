using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class InternalPublicProfileSummaryControllerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetProfiles_RejectsUntrustedRequestWithoutReadingProfiles()
    {
        var profiles = new Mock<IProfileAddressService>();
        var controller = Controller(profiles.Object);

        (await controller.GetProfiles([Guid.NewGuid()], Ct)).Should().BeOfType<UnauthorizedResult>();

        profiles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetProfiles_TrustedRequestReturnsBatch()
    {
        var ids = new[] { Guid.NewGuid() };
        var result = new[] { new PublicProfileSummaryDto(
            new PublicAddressMetadata(ids[0], "anna", "/users/anna", 1), "https://media.example.com/avatars/anna.jpg") };
        var profiles = new Mock<IProfileAddressService>();
        profiles.Setup(p => p.GetProfilesByIdsAsync(ids, Ct)).ReturnsAsync(result);
        var controller = Controller(profiles.Object);
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "internal-secret";

        (await controller.GetProfiles(ids, Ct)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(result);

        profiles.Verify(p => p.GetProfilesByIdsAsync(ids, Ct), Times.Once);
    }

    private static InternalPublicAddressesController Controller(IProfileAddressService profiles) =>
        new(profiles, Microsoft.Extensions.Options.Options.Create(new GatewayAuthOptions { SecretKey = "internal-secret" }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
