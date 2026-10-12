using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Moq;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;
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
        public Func<object, object?>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            LastMessage = args![0];
            if (Handler is not null) return Handler(LastMessage!);
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


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpComplaints_RetainExactCanonicalTargetAuditAndAdminAccessAfterHideAndDelete(bool migrated)
    {
        var ct = TestContext.Current.CancellationToken;
        var targetId = migrated ? Guid.Parse("d59022f5-cb38-4c18-a798-b44164e17a68") : Guid.NewGuid();
        var visitTime = DateTime.UtcNow.AddYears(-2);
        var target = DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "original author", "original visit",
            Rating.Create(2, 3, 4), visitTime, CheckInVisibility.Public, id: targetId);
        target.CreatedAtUtc = visitTime.AddHours(1);
        target.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, target.CreatedAtUtc);
        var historicalReport = ReviewReport.Create(targetId, Guid.NewGuid(), "original historical complaint");
        historicalReport.CreatedAtUtc = visitTime.AddDays(1);
        var historicalAudit = ReviewReportDto.From(historicalReport);
        var rows = new Dictionary<Guid, ReviewReport> { [historicalReport.Id] = historicalReport };
        var reports = new Mock<IReviewReportRepository>();
        var checkIns = new Mock<IQueryCheckInRepository>();
        var uow = new Mock<IUnitOfWork>();
        var outbox = new Mock<IMessageBus>();
        reports.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => rows.GetValueOrDefault(id));
        reports.Setup(x => x.Add(It.IsAny<ReviewReport>())).Callback<ReviewReport>(row => rows.Add(row.Id, row));
        checkIns.Setup(x => x.GetTrackedByIdAsync(targetId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        async Task<Response<ReviewReportDto>> Submit(SubmitReviewReportCommand command) =>
            (await SubmitReviewReportHandler.Handle(command, reports.Object, checkIns.Object, ct)).Item1;
        var user = new ReportUserContext();
        var bus = DispatchProxy.Create<IMessageBus, RecordingBus>();
        ((RecordingBus)(object)bus).Handler = message => message switch
        {
            SubmitReviewReportCommand command => Submit(command),
            GetReviewReportQuery query => GetReviewReportHandler.Handle(query, reports.Object, checkIns.Object, ct),
            ResolveReviewReportCommand command => ResolveReviewReportHandler.Handle(command, reports.Object, checkIns.Object, uow.Object, outbox.Object, ct),
            _ => throw new InvalidOperationException("Unexpected report message.")
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("Admin")));
        builder.Services.AddSingleton(bus);
        builder.Services.AddSingleton<IUserContext>(user);
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminReviewReportsController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new CheckInReportsController(bus, user));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
            try { await next(context); }
            catch (NotFoundException) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
        });
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(ct);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var submission = await client.PostAsJsonAsync($"/api/v1/check-ins/{targetId}/reports",
            new { text = "new complaint", userId = Guid.NewGuid() }, ct);
        submission.StatusCode.Should().Be(HttpStatusCode.Created);
        submission.Headers.CacheControl!.NoStore.Should().BeTrue();
        var created = (await submission.Content.ReadFromJsonAsync<Response<ReviewReportDto>>(ct))!.Data;
        created.CheckInId.Should().Be(targetId);
        created.ReportedByUserId.Should().Be(user.Id);
        rows.Count.Should().Be(2);
        target.ChangeVisibility(CheckInVisibility.Private);
        using var hiddenSubmission = await client.PostAsJsonAsync($"/api/v1/check-ins/{targetId}/reports",
            new { text = "ineligible" }, ct);
        hiddenSubmission.StatusCode.Should().Be(HttpStatusCode.NotFound);
        rows.Count.Should().Be(2);
        using var detailResponse = await client.GetAsync($"/api/admin/check-in-reports/{historicalReport.Id}", ct);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        var detail = (await detailResponse.Content.ReadFromJsonAsync<Response<ReviewReportDetail>>(ct))!.Data;
        detail.Report.Should().Be(historicalAudit);
        detail.CheckIn!.Id.Should().Be(targetId);
        detail.CheckIn.Text.Should().Be("original visit");
        detail.CheckIn.VisitedAt.Should().Be(visitTime);
        detail.CheckIn.IsPubliclyVisible.Should().BeFalse();
        using var resolution = await client.PutAsJsonAsync($"/api/admin/check-in-reports/{historicalReport.Id}/resolution",
            new { deleteCheckIn = true, adminId = Guid.NewGuid() }, ct);
        resolution.StatusCode.Should().Be(HttpStatusCode.OK);
        resolution.Headers.CacheControl!.NoStore.Should().BeTrue();
        target.DeletedAtUtc.Should().NotBeNull();
        var deletion = target.DeletedAtUtc;
        var resolvedAudit = ReviewReportDto.From(historicalReport);
        resolvedAudit.ResolvedByAdminId.Should().Be(user.Id);
        resolvedAudit.ResolvedAtUtc.Should().NotBeNull();
        using var retry = await client.PutAsJsonAsync($"/api/admin/check-in-reports/{historicalReport.Id}/resolution",
            new { deleteCheckIn = true }, ct);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        target.DeletedAtUtc.Should().Be(deletion);
        ReviewReportDto.From(historicalReport).Should().Be(resolvedAudit);
        using var after = await client.GetAsync($"/api/admin/check-in-reports/{historicalReport.Id}", ct);
        var deletedDetail = (await after.Content.ReadFromJsonAsync<Response<ReviewReportDetail>>(ct))!.Data;
        deletedDetail.Report.Should().Be(resolvedAudit);
        deletedDetail.CheckIn!.Id.Should().Be(targetId);
        deletedDetail.CheckIn.DeletedAtUtc.Should().Be(deletion);
        uow.Verify(x => x.SaveChangesAsync(ct), Times.Once);
        outbox.Verify(x => x.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == target.Id && message.ShopId == target.ShopId)), Times.Once);
        await app.StopAsync(ct);
    }

    [Fact]
    public void GatewayReports_PreserveAuthenticationAdminPolicyAndSubmissionLimiter()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CoffeePeek.Gateway/appsettings.json")));
        var routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var submit = routes.GetProperty("shops-check-in-report-submission-route");
        submit.GetProperty("ClusterId").GetString().Should().Be("shops-cluster");
        submit.GetProperty("AuthorizationPolicy").GetString().Should().Be("Authenticated");
        submit.GetProperty("RateLimiterPolicy").GetString().Should().Be("moderation-submission");
        submit.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/v1/check-ins/{id:guid}/reports");
        submit.GetProperty("Match").GetProperty("Methods")[0].GetString().Should().Be("POST");
        var admin = routes.GetProperty("shops-admin-check-in-reports-route");
        admin.GetProperty("AuthorizationPolicy").GetString().Should().Be("Admin");
        admin.GetProperty("ClusterId").GetString().Should().Be("shops-cluster");
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
