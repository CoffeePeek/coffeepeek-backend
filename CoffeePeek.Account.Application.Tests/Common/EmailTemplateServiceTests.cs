using CoffeePeek.Account.Application.Common;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Common;

public class EmailTemplateServiceTests
{
    [Fact]
    public void GetAccountDeletionConfirmationHtml_ShouldEncodeUserControlledValues()
    {
        var service = new EmailTemplateService();

        var html = service.GetAccountDeletionConfirmationHtml(
            "<script>alert('x')</script>",
            "https://coffeepeek.by/confirm?token=<unsafe>");

        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("token=&lt;unsafe&gt;");
        html.Should().NotContain("<script>alert");
        html.Should().Contain("24");
    }
}
