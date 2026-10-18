using System.Reflection;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

/// <summary>
/// Locks the public profile address surface (routes, anonymous access, NoStore, owner-id data read) and the single
/// Admin backfill action. Endpoints are verified by reflection and direct controller calls only, not over live HTTP.
/// </summary>
public sealed class PublicUserAddressControllerTests
{
    private readonly Mock<IProfileAddressService> _addresses = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly string[] NewPublicTemplates = ["by-slug/{slug}", "{slug}"];

    #region Reflection

    [Fact]
    public void PublicRoutes_UseSlugsAndKeepGuidLookupInternal()
    {
        Route(typeof(PublicUserAddressesController)).Should().Be("api/Users");
        Templates(typeof(PublicUserAddressesController)).Should().BeEquivalentTo(NewPublicTemplates);
        Templates(typeof(UsersController)).Should().Contain("/internal/users/{id:guid}");
        Templates(typeof(UsersController)).Should().NotContain("{id:guid}");
    }

    [Fact]
    public void PublicReads_AreAnonymousAndNeverCached()
    {
        foreach (var controller in new[] { typeof(PublicUserAddressesController), typeof(AdminUserPublicAddressesController) })
        {
            var policy = controller.GetCustomAttributes<ResponseCacheAttribute>(true).Single();
            policy.NoStore.Should().BeTrue();
            policy.Location.Should().Be(ResponseCacheLocation.None);
        }

        var type = typeof(PublicUserAddressesController);
        type.GetCustomAttributes<AuthorizeAttribute>(true).Should().BeEmpty();
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(true))
            .Should().BeEmpty();
    }

    [Fact]
    public void AdminBackfill_RequiresAdminPolicyAndIsTheOnlyAction()
    {
        var type = typeof(AdminUserPublicAddressesController);
        Route(type).Should().Be("api/admin/users/public-addresses");
        type.GetCustomAttributes<AuthorizeAttribute>(true).Single().Policy.Should().Be(RoleConsts.Admin);
        type.GetCustomAttributes<AllowAnonymousAttribute>(true).Should().BeEmpty();

        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        actions.SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>(true)).Should().BeEmpty();
        actions.Select(m => m.Name).Should().Equal(nameof(AdminUserPublicAddressesController.Backfill));
        actions.Single().GetCustomAttributes<HttpPostAttribute>().Single().Template.Should().Be("backfill");

        // D-06: no initialize, rename or history route was added to the existing admin users controller.
        Templates(typeof(AdminUsersController))
            .Should().NotContain(t => t.StartsWith("public-addresses", StringComparison.Ordinal));
    }

    #endregion

    #region Behavior

    [Fact]
    public async Task GetBySlug_Unresolved_ReturnsNotFoundWithoutReadingProfile()
    {
        _addresses.Setup(a => a.ResolveAsync("ivan-petrov", _ct)).ReturnsAsync((PublicAddressMetadata?)null);

        var result = await Controller().GetBySlug("ivan-petrov", _ct);

        result.Should().BeOfType<NotFoundResult>();
        _bus.Verify(b => b.InvokeAsync<Response<PublicUserProfileResponse>>(
            It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task GetBySlug_ProfileHiddenAfterResolve_ReturnsEmptyNotFound()
    {
        // The user is deleted or blocked between the resolve and the data read: the handler throws (D-04), and the
        // controller answers with the same NotFoundResult as an unknown slug (at runtime [ApiController] renders the
        // framework's generic ProblemDetails body, with no user data), never a descriptive ErrorResponse object result.
        _addresses.Setup(a => a.ResolveAsync("petr", _ct)).ReturnsAsync(Metadata(Guid.NewGuid()));
        SetupProfile().ThrowsAsync(new NotFoundException("User not found"));

        var result = await Controller().GetBySlug("petr", _ct);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetBySlug_ProfileErrorResponse_ReturnsNotFound()
    {
        _addresses.Setup(a => a.ResolveAsync("petr", _ct)).ReturnsAsync(Metadata(Guid.NewGuid()));
        SetupProfile().ReturnsAsync(Response<PublicUserProfileResponse>.Error("x"));

        var result = await Controller().GetBySlug("petr", _ct);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetBySlug_Visible_ReturnsEnvelopeForResolvedOwner()
    {
        var ownerId = Guid.NewGuid();
        var address = new PublicAddressMetadata(ownerId, "petr", "/users/petr", 2, true);
        var profile = new PublicUserProfileResponse("Petr", DateTime.UtcNow, "about", null, 2, 3);
        object? captured = null;
        _addresses.Setup(a => a.ResolveAsync("ivan-petrov", _ct)).ReturnsAsync(address);
        SetupProfile()
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = m)
            .ReturnsAsync(Response<PublicUserProfileResponse>.Success(profile));

        var result = await Controller().GetBySlug("ivan-petrov", _ct);

        // The profile is read for the resolved owner id, not anything derived from the request slug.
        captured.Should().BeOfType<GetPublicUserProfileCommand>().Which.UserId.Should().Be(ownerId);
        var envelope = result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<PublicEntityEnvelope<PublicUserProfileResponse>>().Subject;
        envelope.Data.Should().Be(profile);
        envelope.Address.Should().Be(PublicAddressDto.From(address));
        System.Text.Json.JsonSerializer.Serialize(envelope).Should().NotContain(ownerId.ToString());

        // T-04-10: the public payload has no private contact data.
        typeof(PublicUserProfileResponse).GetProperty("Email").Should().BeNull();
        typeof(PublicUserProfileResponse).GetProperty("PhoneNumber").Should().BeNull();
    }

    [Fact]
    public async Task InternalLookup_RejectsUntrustedRequestsBeforeReadingAddresses()
    {
        var controller = new InternalPublicAddressesController(_addresses.Object,
            Microsoft.Extensions.Options.Options.Create(new CoffeePeek.Shared.Auth.Options.GatewayAuthOptions { SecretKey = "internal-secret" }));
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        (await controller.GetBatch([Guid.NewGuid()], _ct)).Should().BeOfType<UnauthorizedResult>();
        (await controller.Resolve("petr", _ct)).Should().BeOfType<UnauthorizedResult>();
        _addresses.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Backfill_PassesCursorLimitAndDryRun()
    {
        var afterId = Guid.NewGuid();
        var backfill = new PublicAddressBackfillResult(false, [new PublicAddressBackfillItem(Guid.NewGuid(), "anna", false, false)], null);
        _addresses.Setup(a => a.BackfillAsync(afterId, 25, false, _ct)).ReturnsAsync(backfill);

        var result = await new AdminUserPublicAddressesController(_addresses.Object).Backfill(afterId, 25, false, _ct);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(backfill);
        _addresses.Verify(a => a.BackfillAsync(afterId, 25, false, _ct), Times.Once);
    }

    #endregion

    private PublicUserAddressesController Controller() => new(_addresses.Object, _bus.Object, Mock.Of<IUserBlockStore>());

    private Moq.Language.Flow.ISetup<IMessageBus, Task<Response<PublicUserProfileResponse>>> SetupProfile() =>
        _bus.Setup(b => b.InvokeAsync<Response<PublicUserProfileResponse>>(
            It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()));

    private static PublicAddressMetadata Metadata(Guid ownerId) =>
        new(ownerId, "anna", "/users/anna", 1);

    private static string? Route(Type controller) => controller.GetCustomAttribute<RouteAttribute>()?.Template;

    private static string? Template<T>(string method, params Type[] parameters) =>
        (parameters.Length == 0 ? typeof(T).GetMethod(method) : typeof(T).GetMethod(method, parameters))!
            .GetCustomAttributes<HttpMethodAttribute>().Single().Template;

    private static IEnumerable<string> Templates(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template)
            .OfType<string>();
}
