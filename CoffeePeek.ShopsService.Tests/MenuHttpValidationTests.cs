using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shops.Application.Features.Admin.Menu;
using CoffeePeek.ShopsService.Controllers;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class MenuHttpValidationTests
{
    [Fact]
    public async Task MenuEditing_AcceptsPriceWithoutVolume_AndRejectsInvalidPacketsBeforeDispatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var recorder = (ReviewReportApiContractTests.RecordingBus)(object)bus;
        recorder.NextResponse = Response<AdminShopMenuDto>.Success(new(null!, []));
        var user = new Mock<IUserContext>();
        user.Setup(x => x.GetUserIdOrThrow()).Returns(Guid.NewGuid());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllersModule();
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminCoffeeShopsController).Assembly);
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(options => options.AddPolicy(RoleConsts.Admin,
            policy => policy.RequireAssertion(_ => true)));
        builder.Services.AddSingleton(bus);
        builder.Services.AddSingleton(user.Object);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var path = $"/api/admin/shops/{Guid.NewGuid()}/menu";
            foreach (var legacyField in new[] { "", ",\"volumeMl\":250" })
            {
                using var valid = await client.PutAsync(path, new StringContent(
                    "{\"items\":[{\"slug\":\"espresso\",\"availability\":\"Present\",\"price\":5.5" + legacyField + "}]}",
                    Encoding.UTF8, "application/json"), ct);
                Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                var command = Assert.IsType<UpdateAdminShopMenuCommand>(recorder.LastMessage);
                Assert.Equal(5.5m, Assert.Single(command.Items).Price);
            }
            var previous = recorder.LastMessage;
            foreach (var packet in new[]
            {
                "{\"items\":null}", "{\"items\":[null]}",
                "{\"items\":[{\"slug\":null,\"availability\":1,\"price\":5}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":1,\"price\":null}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":1,\"price\":0}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":2,\"price\":5}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":99,\"price\":5}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":1,\"price\":100000000}]}",
                "{\"items\":[{\"slug\":\"espresso\",\"availability\":1,\"price\":5},{\"slug\":\" ESPRESSO \",\"availability\":1,\"price\":6}]}"
            })
            {
                using var invalid = await client.PutAsync(path, new StringContent(packet, Encoding.UTF8, "application/json"), ct);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.Same(previous, recorder.LastMessage);
            }
            var json = JsonSerializer.Serialize(new ShopMenuItemDto("espresso", "Эспрессо", "Espresso",
                Contract.Enums.CoffeeDrinkCategory.Espresso, Contract.Enums.MenuItemAvailability.Present,
                5.5m, "BYN", Contract.Enums.MenuItemSource.Manual), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("volume", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { await app.StopAsync(ct); }
    }
}
