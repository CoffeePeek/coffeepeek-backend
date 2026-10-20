using System.Net;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Infrastructure.Consumers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Tests.Consumers;

public class AccountEmailHandlersTests
{
    [Theory]
    [InlineData("confirm-email")]
    [InlineData("reset-password")]
    [InlineData("confirm-account-deletion")]
    public async Task Handle_SendsHostedTemplateWithSafeVariablesAndExistingLink(string template)
    {
        const string username = "<script>alert('x')</script> & Арсен";
        const string token = "a+b/c=?&\"#%";
        var ct = TestContext.Current.CancellationToken;
        var resend = new Mock<IResend>(MockBehavior.Strict);
        EmailMessage? sent = null;
        resend.Setup(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), ct))
            .Callback<EmailMessage, CancellationToken>((email, _) => sent = email)
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null!));
        var yearBeforeSend = DateTime.UtcNow.Year;

        await SendAsync(template, resend.Object, username, token, ct);

        sent.Should().NotBeNull();
        sent!.To.Single().Email.Should().Be("user@example.com");
        sent.From.Should().BeNull();
        sent.Subject.Should().BeNull();
        sent.HtmlBody.Should().BeNull();
        sent.TextBody.Should().BeNull();
        sent.Template.Should().NotBeNull();
        sent.Template!.TemplateId.Should().Be(template);
        var variables = sent.Template.Variables!;
        variables.Should().HaveCount(3);
        variables["USERNAME"].Should().Be(WebUtility.HtmlEncode(username));
        var url = WebUtility.HtmlDecode((string)variables["CONFIRMATION_URL"]);
        url.Should().Be($"https://coffeepeek.by/{template}?token={Uri.EscapeDataString(token)}");
        Uri.UnescapeDataString(new Uri(url).Query["?token=".Length..]).Should().Be(token);
        ((int)variables["YEAR"]).Should().BeInRange(yearBeforeSend, DateTime.UtcNow.Year);
        resend.Verify(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), ct), Times.Once);
        resend.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RejectedEmailThrowsForWolverineRetry()
    {
        var resend = new Mock<IResend>();
        resend.Setup(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResendResponse<Guid>((ResendException)null!, null!));

        var act = () => SendAsync("confirm-email", resend.Object, "Арсен", "token",
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Account email was not accepted by Resend.");
    }

    [Fact]
    public async Task Handle_PropagatesSendFailure()
    {
        var resend = new Mock<IResend>();
        var failure = new HttpRequestException("Resend unavailable");
        resend.Setup(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var act = () => SendAsync("reset-password", resend.Object, "Арсен", "token",
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Should().BeSameAs(failure);
    }

    private static Task SendAsync(string template, IResend resend, string username, string token, CancellationToken ct)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebClientUrl"] = "https://coffeepeek.by"
        }).Build();

        return template switch
        {
            "confirm-email" => new UserRegisteredEventHandler(resend, config).Handle(
                new UserRegisteredInternalEvent(Guid.NewGuid(), "user@example.com", username, token), ct),
            "reset-password" => new PasswordResetRequestedEventHandler(resend, config).Handle(
                new PasswordResetRequestedInternalEvent(Guid.NewGuid(), "user@example.com", username, token), ct),
            "confirm-account-deletion" => new AccountDeletionRequestedEventHandler(resend, config).Handle(
                new AccountDeletionRequestedInternalEvent(Guid.NewGuid(), Guid.NewGuid(), "user@example.com", username, token), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }
}
