using System.Security.Claims;
using System.Text.Json;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.ModerationService.Controllers;
using CoffeePeek.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class LeaderboardModerationApiTests
{
    [Fact]
    public async Task ModeratorCannotOverrideBySupplyingTrustedFields()
    {
        var command = JsonSerializer.Deserialize<ChangeStatusModerationReviewCommand>("""
            {"submissionId":"11111111-1111-4111-8111-111111111111","moderationStatus":"Approved",
             "countsForLeaderboard":true,"leaderboardReason":"GPS issue","canOverrideLeaderboard":true,
             "userId":"22222222-2222-4222-8222-222222222222"}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(command.CanOverrideLeaderboard);
        Assert.Equal(Guid.Empty, command.UserId);
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var controller = new ModerationReviewsController(bus.Object, Mock.Of<IUserContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Moderator")], "test"))
            } }
        };
        Assert.IsType<ForbidResult>(await controller.ChangeStatusModerationReview(command, TestContext.Current.CancellationToken));
        bus.VerifyNoOtherCalls();
    }
}
