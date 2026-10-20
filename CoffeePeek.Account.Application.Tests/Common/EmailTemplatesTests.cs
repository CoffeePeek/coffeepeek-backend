#nullable enable

using System.Net;
using System.Text.RegularExpressions;
using CoffeePeek.Account.Application.Common;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Common;

public class EmailTemplatesTests
{
    [Fact]
    public void GetAccountDeletionConfirmationHtml_ShouldEncodeUserControlledValues()
    {

        var html = EmailTemplates.GetAccountDeletionConfirmationHtml(
            "<script>alert('x')</script>",
            "https://coffeepeek.by/confirm?token=<unsafe>");

        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("token=&lt;unsafe&gt;");
        html.Should().NotContain("<script>alert");
        html.Should().Contain("24");
    }

    [Theory]
    [InlineData(0, "Подтвердите почту", "Спасибо за регистрацию в CoffeePeek!",
        "Если вы не регистрировались в CoffeePeek, просто проигнорируйте это письмо.",
        "Подтвердить почту", "Ссылка действует 10 минут.")]
    [InlineData(1, "Новый пароль", "Мы получили запрос на сброс пароля вашего аккаунта CoffeePeek.",
        "Если вы не запрашивали сброс пароля, проигнорируйте это письмо. Ваш пароль останется прежним.",
        "Установить новый пароль", "Ссылка действует 30 минут.")]
    [InlineData(2, "Удаление аккаунта", "Пока аккаунт остаётся активным.",
        "Если вы не запрашивали удаление, не подтверждайте его. Отменить запрос можно по ссылке на странице подтверждения.",
        "Подтвердить удаление", "Подтвердите запрос в течение 24 часов.")]
    [InlineData(3, "С возвращением!", "Любимые кофейни и новые открытия уже ждут вас.",
        "Если вы не входили в аккаунт, немедленно смените пароль.", null, null)]
    public void Templates_ShouldRenderCompleteSafeEmail(
        int kind, string title, string message, string securityFooter, string? buttonText, string? notice)
    {
        const string username = "<script>alert('x')</script> & \"Арсен\"";
        const string url = "https://coffeepeek.by/action?token=<unsafe>\"&source='mail'";

        var html = kind switch
        {
            0 => EmailTemplates.GetConfirmationHtml(username, url),
            1 => EmailTemplates.GetPasswordResetHtml(username, url),
            2 => EmailTemplates.GetAccountDeletionConfirmationHtml(username, url),
            _ => EmailTemplates.GetWelcomeBackHtml(username)
        };

        html.Should().MatchRegex($"<h1\\b[^>]*>\\s*{Regex.Escape(title)}\\s*</h1>");
        html.Should().Contain(message);
        html.Should().Contain(securityFooter);
        html.Should().Contain("CoffeePeek");
        html.Should().Contain("<table role=\"presentation\"");
        html.Should().NotContain("...");
        html.Should().Contain(WebUtility.HtmlEncode(username));
        html.Should().NotContain(username);
        html.Should().NotContain("<script>");

        if (buttonText is null)
        {
            html.Should().NotContain("Если кнопка не работает");
            html.Should().NotContain("Ссылка действует");
            html.Should().NotContain("Подтвердите запрос в течение");
            html.Should().NotContain("Подтвердить почту");
            html.Should().NotContain("Установить новый пароль");
            html.Should().NotContain("Подтвердить удаление");
        }
        else
        {
            var safeUrl = WebUtility.HtmlEncode(url);
            html.Should().Contain(buttonText);
            html.Should().Contain(notice!);
            html.Should().Contain("Если кнопка не работает");
            html.Should().NotContain(url);
            html.Should().Contain($">{safeUrl}</a>");
            Regex.Matches(html, Regex.Escape($"href=\"{safeUrl}\""))
                .Should().HaveCount(2);
        }
    }
}
