using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class ReviewReportApiContractTests
{
    [Fact]
    public async Task HttpBinding_RejectsOmittedDecisionAndUsesAuthenticatedIdentity()
    {
        var bus = DispatchProxy.Create<IMessageBus, RecordingBus>();
        var user = new ReportUserContext();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("Admin")));
        builder.Services.AddSingleton(bus);
        builder.Services.AddSingleton<IUserContext>(user);
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminReviewReportsController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new CheckInReportsController(bus, user));
        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
            return next(context);
        });
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var reportId = Guid.NewGuid();
        foreach (var json in new[] { "{}", "{\"deleteCheckIn\":null}" })
        {
            using var response = await client.PutAsync($"/api/admin/check-in-reports/{reportId}/resolution",
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        using var decision = await client.PutAsJsonAsync($"/api/admin/check-in-reports/{reportId}/resolution", new { deleteCheckIn = false, adminId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        decision.StatusCode.Should().Be(HttpStatusCode.OK);
        ((RecordingBus)(object)bus).LastMessage.Should().Be(new ResolveReviewReportCommand(reportId, user.Id, false));
        var checkInId = Guid.NewGuid();
        using var submission = await client.PostAsJsonAsync($"/api/v1/check-ins/{checkInId}/reports", new { text = "complaint", userId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        submission.StatusCode.Should().Be(HttpStatusCode.Created);
        ((RecordingBus)(object)bus).LastMessage.Should().Be(new SubmitReviewReportCommand(checkInId, user.Id, "complaint"));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    public class RecordingBus : DispatchProxy
    {
        public object? LastMessage { get; private set; }
        public object? NextResponse { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            LastMessage = args![0];
            var responseType = method!.ReturnType.GetGenericArguments().Single();
            var response = NextResponse ?? Activator.CreateInstance(responseType);
            return method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                ? typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(responseType).Invoke(null, [response])
                : Activator.CreateInstance(method.ReturnType, response);
        }
    }

    private class ReportUserContext : IUserContext
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid? GetUserId() => Id;
        public Guid GetUserIdOrThrow() => Id;
        public string? GetUsername() => "tester";
        public string GetUsernameOrThrow() => "tester";
        public string? GetUserRole() => "Admin";
        public bool IsAuthenticated => true;
    }

    [Fact]
    public void Reports_RequireAuthenticationAndAdminPolicy()
    {
        typeof(CheckInReportsController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(CheckInReportsController).GetMethod(nameof(CheckInReportsController.Report))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().BeNull();
        typeof(CheckInReportsController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/check-ins/{id:guid}/reports");
        typeof(AdminReviewReportsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("Admin");
        typeof(AdminReviewReportsController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/check-in-reports");
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"deleteCheckIn\":null}", false)]
    [InlineData("{\"deleteCheckIn\":false}", true)]
    [InlineData("{\"deleteCheckIn\":true}", true)]
    public void Resolution_RequiresExplicitDecision(string json, bool valid)
    {
        var request = JsonSerializer.Deserialize<ResolveReviewReportRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Validator.TryValidateObject(request, new ValidationContext(request), [], true).Should().Be(valid);
    }

    [Fact]
    public void SubmissionBody_CannotChooseUserOrCheckInIdentity()
    {
        typeof(SubmitReviewReportRequest).GetProperties().Select(x => x.Name).Should().Equal("Text");
        typeof(ResolveReviewReportRequest).GetProperties().Select(x => x.Name).Should().Equal("DeleteCheckIn");
    }
}
