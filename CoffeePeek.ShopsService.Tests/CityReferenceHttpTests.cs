using System.Reflection;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;
using CoffeePeek.ShopsService.Controllers;
using CoffeePeek.ShopsService.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class CityReferenceHttpTests
{
    [Fact]
    public async Task DeleteCityMapsReferencedCityResponseToHttpConflict()
    {
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var recorder = (ReviewReportApiContractTests.RecordingBus)(object)bus;
        recorder.NextResponse = Response.Error(StatusCodes.Status409Conflict, "City is referenced.");

        var result = await new AdminCitiesController(bus).Delete(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.IsType<DeleteCityCommand>(recorder.LastMessage);
    }

    [Fact]
    public async Task NewReferenceToDeletedCityMapsDatabaseErrorToHttpConflict()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var pg = new PostgresException("reference error", "ERROR", "ERROR",
            PostgresErrorCodes.ForeignKeyViolation, constraintName: "FK_Shops_Cities_CityId");
        var error = new DbUpdateException("save failed", pg);

        var handled = await new CityReferenceExceptionHandler(
            NullLogger<CityReferenceExceptionHandler>.Instance)
            .TryHandleAsync(context, error, TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("CONFLICT", body, StringComparison.Ordinal);
    }
}
