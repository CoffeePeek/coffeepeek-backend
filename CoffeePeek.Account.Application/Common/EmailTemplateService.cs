using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;

namespace CoffeePeek.Account.Application.Common;

public class EmailTemplateService : IEmailTemplateService
{
    public string GetConfirmationHtml(string username, string confirmationUrl) => Render(
        username, "Подтвердите почту", "Подтверждение почты", "&#9993;",
        "Спасибо за регистрацию в CoffeePeek! Подтвердите адрес электронной почты, чтобы завершить создание аккаунта.",
        "Ссылка действует 10 минут. Если срок истёк, запросите новое письмо на странице входа.",
        "Если вы не регистрировались в CoffeePeek, просто проигнорируйте это письмо.",
        "Подтвердить почту", confirmationUrl);

    public string GetPasswordResetHtml(string username, string resetUrl) => Render(
        username, "Новый пароль", "Сброс пароля", "&#128274;",
        "Мы получили запрос на сброс пароля вашего аккаунта CoffeePeek. Нажмите кнопку ниже, чтобы установить новый пароль.",
        "Ссылка действует 30 минут.",
        "Если вы не запрашивали сброс пароля, проигнорируйте это письмо. Ваш пароль останется прежним.",
        "Установить новый пароль", resetUrl);

    public string GetAccountDeletionConfirmationHtml(string username, string confirmationUrl) => Render(
        username, "Удаление аккаунта", "Удаление аккаунта", "&#9888;",
        "Мы получили запрос на удаление вашего аккаунта CoffeePeek. Пока аккаунт остаётся активным. Чтобы продолжить удаление, подтвердите запрос.",
        "Подтвердите запрос в течение 24 часов.",
        "Если вы не запрашивали удаление, не подтверждайте его. Отменить запрос можно по ссылке на странице подтверждения.",
        "Подтвердить удаление", confirmationUrl);

    public string GetWelcomeBackHtml(string username) => Render(
        username, "С возвращением!", "С возвращением", "&#9749;",
        "Рады снова видеть вас в CoffeePeek. Любимые кофейни и новые открытия уже ждут вас.",
        "", "Если вы не входили в аккаунт, немедленно смените пароль.");

    private static string Render(string username, string title, string label, string icon,
        string message, string notice, string footer, string? buttonText = null, string? url = null)
    {
        var safeUsername = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(url);
        var action = url is null
            ? ""
            : $"""
               <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                 <tr><td style="padding-top:28px;">
                   <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                     <tr><td align="center" bgcolor="#EAB308" style="border-radius:12px;">
                       <a href="{safeUrl}" target="_blank" style="display:block;padding:16px 20px;border:1px solid #EAB308;border-radius:12px;background:#EAB308;color:#1A1412;font-size:15px;line-height:22px;font-weight:700;text-align:center;text-decoration:none;">{buttonText}</a>
                     </td></tr>
                   </table>
                 </td></tr>
                 <tr><td style="padding-top:18px;">
                   <p style="margin:0;padding:14px 16px;background:#1A1412;border:1px solid #3D2F28;border-radius:12px;color:#A39E93;font-size:13px;line-height:20px;">{notice}</p>
                 </td></tr>
                 <tr><td style="padding-top:24px;">
                   <p style="margin:0 0 8px;color:#A39E93;font-size:12px;line-height:19px;">Если кнопка не работает, скопируйте ссылку и откройте её в браузере:</p>
                   <p style="margin:0;font-size:12px;line-height:19px;word-break:break-all;overflow-wrap:anywhere;"><a href="{safeUrl}" target="_blank" style="color:#EAB308;text-decoration:underline;">{safeUrl}</a></p>
                 </td></tr>
               </table>
               """;

        return $$"""
                 <!doctype html>
                 <html lang="ru" xmlns="http://www.w3.org/1999/xhtml" xmlns:o="urn:schemas-microsoft-com:office:office">
                 <head>
                   <meta charset="utf-8" />
                   <meta name="viewport" content="width=device-width, initial-scale=1" />
                   <meta name="color-scheme" content="dark" />
                   <meta name="supported-color-schemes" content="dark" />

                   <title>{{title}} — CoffeePeek</title>

                   <!--[if mso]>
                   <noscript>
                     <xml>
                       <o:OfficeDocumentSettings>
                         <o:PixelsPerInch>96</o:PixelsPerInch>
                       </o:OfficeDocumentSettings>
                     </xml>
                   </noscript>
                   <![endif]-->

                   <style type="text/css">
                     html, body {
                       margin: 0 !important;
                       padding: 0 !important;
                       width: 100% !important;
                     }

                     body, table, td, a {
                       -webkit-text-size-adjust: 100%;
                       -ms-text-size-adjust: 100%;
                     }

                     table, td {
                       mso-table-lspace: 0pt;
                       mso-table-rspace: 0pt;
                     }

                     img {
                       border: 0;
                       outline: none;
                       text-decoration: none;
                     }

                     a[x-apple-data-detectors] {
                       color: inherit !important;
                       text-decoration: none !important;
                     }

                     @media only screen and (max-width: 620px) {
                       .page {
                         padding: 20px 12px !important;
                       }

                       .content {
                         padding: 28px 22px !important;
                       }

                       .footer {
                         padding: 22px !important;
                       }

                       .title {
                         font-size: 26px !important;
                         line-height: 33px !important;
                       }
                     }
                   </style>
                 </head>

                 <body style="margin:0;padding:0;background:#1A1412;color:#FFFFFF;font-family:Manrope,'Segoe UI',Arial,sans-serif;">
                   <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:#1A1412;">
                     {{title}}. {{notice}}
                   </div>

                   ...
                   <h1>{{title}}</h1>
                   ...
                   Привет, {{safeUsername}}!
                   ...
                   {{message}}
                   {{action}}
                   ...
                   &copy; {{DateTime.UtcNow.Year}} CoffeePeek
                 </body>
                 </html>
                 """;
    }
}