using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Infrastructure.Options;
using CoffeePeek.Account.Infrastructure.Support;
using CoffeePeek.AccountService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Resend;

namespace CoffeePeek.Account.Infrastructure.Tests;

public class SupportEndpointTests
{
    [Fact]
    public async Task PublicEndpoints_ShowContactAndAcceptValidRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = new Mock<ISupportEmailSender>();
        sender.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(SupportController).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<SupportOptions>(o => o.Email = "info@coffeepeek.by");
        builder.Services.AddSingleton(sender.Object);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(SupportController).Assembly);
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(ct);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var page = await client.GetAsync("/support", ct);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await page.Content.ReadAsStringAsync(ct)).Should().Contain("mailto:info@coffeepeek.by");

        using var contact = await client.GetAsync("/api/support/contact", ct);
        contact.StatusCode.Should().Be(HttpStatusCode.OK);
        (await contact.Content.ReadAsStringAsync(ct)).Should().Contain("info@coffeepeek.by");

        using var submitted = await client.PostAsJsonAsync("/api/support/requests",
            new { email = "user@example.com", message = "  Please help with login.  " }, ct);
        submitted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sender.Verify(x => x.SendAsync("user@example.com", "Please help with login.", It.IsAny<CancellationToken>()), Times.Once);

        using var invalidEmail = await client.PostAsJsonAsync("/api/support/requests",
            new { email = "not-an-email", message = "Please help with login." }, ct);
        invalidEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var blankMessage = await client.PostAsJsonAsync("/api/support/requests",
            new { email = "user@example.com", message = "          " }, ct);
        blankMessage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sender.VerifyNoOtherCalls();
        await app.StopAsync(ct);
    }

    [Fact]
    public async Task Sender_UsesConfiguredInboxAndReplyToWithoutHtml()
    {
        var resend = new Mock<IResend>();
        EmailMessage? sent = null;
        resend.Setup(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((email, _) => sent = email)
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null!));
        var sender = new SupportEmailSender(resend.Object,
            Microsoft.Extensions.Options.Options.Create(new SupportOptions { Email = "info@coffeepeek.by" }));

        await sender.SendAsync("user@example.com", "Help with my account", TestContext.Current.CancellationToken);

        sent.Should().NotBeNull();
        sent!.To.Single().Email.Should().Be("info@coffeepeek.by");
        sent.ReplyTo!.Single().Email.Should().Be("user@example.com");
        sent.TextBody.Should().Contain("Help with my account");
        sent.HtmlBody.Should().BeNull();
    }

    [Fact]
    public async Task Sender_DoesNotTreatRejectedEmailAsDelivered()
    {
        var resend = new Mock<IResend>();
        resend.Setup(x => x.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResendResponse<Guid>((ResendException)null!, null!));
        var sender = new SupportEmailSender(resend.Object,
            Microsoft.Extensions.Options.Options.Create(new SupportOptions { Email = "info@coffeepeek.by" }));

        var act = () => sender.SendAsync("user@example.com", "Help with my account", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
