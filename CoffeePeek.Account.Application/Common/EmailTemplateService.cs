using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;

namespace CoffeePeek.Account.Application.Common;

public class EmailTemplateService : IEmailTemplateService
{
    private const string BrandGold = "#EAB308";
    private const string BrandBg = "#1A1412";
    private const string BrandSurface = "#2D241F";
    private const string BrandBorder = "#3D2F28";
    private const string TextPrimary = "#FFFFFF";
    private const string TextMuted = "#A39E93";
    private const string TextDim = "#5C544F";

    private const string LogoUrl = "https://coffeepeek.by/logo/logo.png";

    private const string ConfirmationEmailCss = """
                                                html, body { margin: 0 !important; padding: 0 !important; width: 100% !important; }
                                                body, table, td, a { -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }
                                                table, td { mso-table-lspace: 0pt; mso-table-rspace: 0pt; border-collapse: collapse !important; }
                                                img { border: 0; height: auto; line-height: 100%; outline: none; text-decoration: none; }
                                                a[x-apple-data-detectors] { color: inherit !important; text-decoration: none !important; }
                                                .button:hover { background-color: #FACC15 !important; }
                                                @media only screen and (max-width: 620px) {
                                                    .page { padding: 16px 12px !important; }
                                                    .card { border-radius: 20px !important; }
                                                    .content { padding: 30px 22px 26px !important; }
                                                    .footer { padding: 20px 22px 24px !important; }
                                                    .title { font-size: 26px !important; line-height: 32px !important; }
                                                    .button { display: block !important; width: 100% !important; box-sizing: border-box !important; }
                                                }
                                                """;

    public string GetConfirmationHtml(string username, string confirmationUrl)
    {
        var safeUsername = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(confirmationUrl);
        var year = DateTime.UtcNow.Year;

        return $"""
                <!doctype html>
                <html lang="ru" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <meta name="color-scheme" content="dark" />
                    <meta name="supported-color-schemes" content="dark" />
                    <title>Подтвердите email — CoffeePeek</title>
                    <!--[if mso]>
                    <noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript>
                    <![endif]-->
                    <style type="text/css">{ConfirmationEmailCss}</style>
                </head>
                <body style="margin:0;padding:0;background:#1A1412;color:#FFFFFF;font-family:Manrope,'Segoe UI',Arial,sans-serif;">
                    <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;color:#1A1412;font-size:1px;line-height:1px;">
                        Остался один шаг: подтвердите email. Ссылка действует 10 минут.&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;
                    </div>

                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;background:#1A1412;">
                        <tr>
                            <td class="page" align="center" style="padding:48px 16px;">
                                <table role="presentation" class="card" width="560" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:560px;background:#2D241F;border:1px solid #3D2F28;border-radius:24px;overflow:hidden;box-shadow:0 24px 48px rgba(0,0,0,.32);">
                                    <tr>
                                        <td style="height:4px;background:#EAB308;font-size:0;line-height:0;">&nbsp;</td>
                                    </tr>
                                    <tr>
                                        <td class="content" style="padding:38px 42px 32px;">
                                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                                                <tr>
                                                    <td align="left" style="padding-bottom:34px;">
                                                        <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                                                            <tr>
                                                                <td width="44" height="44" align="center" valign="middle" style="width:44px;height:44px;background:#1A1412;border:1px solid #3D2F28;border-radius:12px;">
                                                                    <img src="{LogoUrl}" width="22" height="22" alt="" style="display:block;width:22px;height:22px;margin:0 auto;" />
                                                                </td>
                                                                <td style="padding-left:12px;font-size:20px;line-height:24px;font-weight:800;letter-spacing:-.8px;color:#FFFFFF;">
                                                                    Coffee<span style="color:#EAB308;">Peek</span>
                                                                </td>
                                                            </tr>
                                                        </table>
                                                    </td>
                                                    <td align="right" valign="top" style="padding-bottom:34px;">
                                                        <span style="display:inline-block;padding:6px 10px;background:#3A321D;border:1px solid #5B4A1E;border-radius:999px;color:#EAB308;font-size:11px;line-height:14px;font-weight:700;letter-spacing:.3px;">ПОДТВЕРЖДЕНИЕ</span>
                                                    </td>
                                                </tr>
                                            </table>

                                            <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                                                <tr>
                                                    <td width="64" height="64" align="center" valign="middle" style="width:64px;height:64px;background:#3A321D;border:1px solid #5B4A1E;border-radius:18px;color:#EAB308;font-size:28px;line-height:64px;">&#9993;</td>
                                                </tr>
                                            </table>

                                            <h1 class="title" style="margin:24px 0 10px;color:#FFFFFF;font-size:30px;line-height:37px;font-weight:700;letter-spacing:-.6px;">Привет, {safeUsername}!</h1>
                                            <p style="margin:0 0 10px;color:#A39E93;font-size:15px;line-height:23px;">Спасибо за регистрацию в CoffeePeek.</p>
                                            <p style="margin:0;color:#A39E93;font-size:15px;line-height:23px;">Подтвердите адрес электронной почты, чтобы завершить создание аккаунта и открыть все возможности сервиса.</p>

                                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;margin-top:28px;">
                                                <tr>
                                                    <td align="center" bgcolor="#EAB308" style="background:#EAB308;border-radius:12px;box-shadow:0 8px 20px rgba(234,179,8,.16);">
                                                        <a class="button" href="{safeUrl}" target="_blank" style="display:block;padding:15px 24px;border-radius:12px;background:#EAB308;color:#1A1412;font-size:15px;line-height:20px;font-weight:700;text-align:center;text-decoration:none;">Подтвердить email&nbsp;&nbsp;&#8594;</a>
                                                    </td>
                                                </tr>
                                            </table>

                                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;margin-top:18px;">
                                                <tr>
                                                    <td style="padding:13px 15px;background:#241C18;border:1px solid #3D2F28;border-radius:12px;color:#A39E93;font-size:12px;line-height:18px;">
                                                        <strong style="color:#FFFFFF;font-weight:600;">Ссылка действует 10 минут.</strong><br />Если время истекло, запросите новое письмо на странице входа.
                                                    </td>
                                                </tr>
                                            </table>

                                            <p style="margin:24px 0 7px;color:#5C544F;font-size:12px;line-height:18px;">Кнопка не работает? Скопируйте эту ссылку:</p>
                                            <p style="margin:0;font-family:Consolas,'Courier New',monospace;font-size:11px;line-height:18px;word-break:break-all;"><a href="{safeUrl}" target="_blank" style="color:#EAB308;text-decoration:underline;">{safeUrl}</a></p>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td class="footer" style="padding:22px 42px 26px;background:#241C18;border-top:1px solid #3D2F28;">
                                            <p style="margin:0 0 8px;color:#A39E93;font-size:12px;line-height:18px;">Не регистрировались в CoffeePeek? Просто проигнорируйте письмо.</p>
                                            <p style="margin:0;color:#5C544F;font-size:12px;line-height:18px;">&copy; {year} CoffeePeek &middot; coffeepeek.by</p>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
                """;
    }

    public string GetPasswordResetHtml(string username, string resetUrl)
    {
        var safeUsername = WebUtility.HtmlEncode(username);
        var safeUrlDisplay = WebUtility.HtmlEncode(resetUrl);
        var year = DateTime.UtcNow.Year;

        return $"""
                <!DOCTYPE html>
                <html lang="ru" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
                <head>
                    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                    <meta name="color-scheme" content="dark" />
                    <meta name="supported-color-schemes" content="dark" />
                    <title>Сброс пароля — CoffeePeek</title>
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
                        {ConfirmationEmailCss}
                    </style>
                </head>
                <body style="margin:0;padding:0;background-color:{BrandBg};font-family:'Noto Sans','Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;">
                    <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:{BrandBg};">
                        Сбросьте пароль CoffeePeek. Ссылка действует 30 минут.
                    </div>

                    <table role="presentation" width="100%" border="0" cellspacing="0" cellpadding="0" style="background-color:{BrandBg};">
                        <tr>
                            <td align="center" class="email-wrapper" style="padding:40px 16px;">
                                <table role="presentation" class="email-card" width="480" border="0" cellspacing="0" cellpadding="0" style="max-width:480px;width:100%;border:1px solid {BrandBorder};border-radius:24px;overflow:hidden;background-color:{BrandSurface};box-shadow:0 24px 48px -12px rgba(0,0,0,0.5);">
                                    <tr>
                                        <td height="4" style="background:linear-gradient(90deg,#B8860B 0%,{BrandGold} 50%,#B8860B 100%);background-color:{BrandGold};font-size:0;line-height:0;">&nbsp;</td>
                                    </tr>
                                    <tr>
                                        <td class="email-content" style="padding:40px 36px 32px;text-align:center;">
                                            <table role="presentation" align="center" border="0" cellspacing="0" cellpadding="0" style="margin:0 auto 28px;">
                                                <tr>
                                                    <td align="center" style="padding-right:10px;vertical-align:middle;">
                                                        <table role="presentation" border="0" cellspacing="0" cellpadding="0">
                                                            <tr>
                                                                <td align="center" style="width:36px;height:36px;background-color:{BrandBg};border:1px solid {BrandBorder};border-radius:11px;">
                                                                    <img src="{LogoUrl}" width="18" height="18" alt="" style="display:block;margin:9px auto;" />
                                                                </td>
                                                            </tr>
                                                        </table>
                                                    </td>
                                                    <td align="left" style="vertical-align:middle;font-family:'Segoe UI',system-ui,sans-serif;font-size:18px;font-weight:700;letter-spacing:-0.025em;color:{TextPrimary};">
                                                        Coffee<span style="color:{BrandGold};">Peek</span>
                                                    </td>
                                                </tr>
                                            </table>

                                            <table role="presentation" align="center" border="0" cellspacing="0" cellpadding="0" style="margin:0 auto 20px;">
                                                <tr>
                                                    <td align="center" style="width:72px;height:72px;border-radius:99px;background-color:rgba(234,179,8,0.10);font-size:32px;line-height:72px;color:{BrandGold};">
                                                        &#128274;
                                                    </td>
                                                </tr>
                                            </table>

                                            <h1 class="email-heading" style="margin:0 0 10px;font-family:'Segoe UI',system-ui,sans-serif;font-size:28px;line-height:36px;font-weight:700;color:{TextPrimary};">
                                                Привет, {safeUsername}!
                                            </h1>
                                            <p style="margin:0 0 8px;font-size:14px;line-height:22px;color:{TextMuted};">
                                                Мы получили запрос на сброс пароля для вашего аккаунта CoffeePeek.
                                            </p>
                                            <p style="margin:0 0 28px;font-size:14px;line-height:22px;color:{TextMuted};">
                                                Нажмите кнопку ниже, чтобы задать новый пароль.
                                                Ссылка действительна <strong style="color:{TextPrimary};font-weight:600;">30 минут</strong>.
                                            </p>

                                            <table role="presentation" align="center" border="0" cellspacing="0" cellpadding="0" style="margin:0 auto 28px;width:100%;max-width:320px;">
                                                <tr>
                                                    <td align="center" bgcolor="{BrandGold}" style="border-radius:12px;box-shadow:0 4px 6px -4px rgba(180,140,75,0.2),0 10px 15px -3px rgba(180,140,75,0.2);">
                                                        <a href="{resetUrl}" target="_blank" class="email-cta" style="display:block;padding:14px 24px;font-family:'Segoe UI',system-ui,sans-serif;font-size:15px;font-weight:600;color:{BrandBg};text-decoration:none;border-radius:12px;background-color:{BrandGold};">
                                                            Сбросить пароль
                                                        </a>
                                                    </td>
                                                </tr>
                                            </table>

                                            <table role="presentation" align="center" border="0" cellspacing="0" cellpadding="0" style="margin:0 auto;width:100%;">
                                                <tr>
                                                    <td style="padding:14px 16px;border-radius:12px;background-color:rgba(26,20,18,0.6);border:1px solid {BrandBorder};text-align:left;">
                                                        <p style="margin:0 0 6px;font-size:12px;line-height:18px;color:{TextDim};">
                                                            Если кнопка не работает, скопируйте ссылку:
                                                        </p>
                                                        <p style="margin:0;font-size:11px;line-height:18px;word-break:break-all;">
                                                            <a href="{resetUrl}" target="_blank" style="color:{BrandGold};text-decoration:underline;font-family:monospace;">{safeUrlDisplay}</a>
                                                        </p>
                                                    </td>
                                                </tr>
                                            </table>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td style="padding:20px 36px 28px;border-top:1px solid {BrandBorder};text-align:center;background-color:{BrandBg};">
                                            <p style="margin:0 0 6px;font-size:12px;line-height:18px;color:{TextDim};">
                                                Если вы не запрашивали сброс пароля, проигнорируйте это письмо.
                                            </p>
                                            <p style="margin:0;font-size:12px;line-height:18px;color:{TextDim};">
                                                &copy; {year} CoffeePeek &middot; coffeepeek.by
                                            </p>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
                """;
    }

    public string GetAccountDeletionConfirmationHtml(string username, string confirmationUrl)
    {
        var safeUsername = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(confirmationUrl);

        return $"""
                <!doctype html>
                <html lang="ru">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <title>Подтвердите удаление аккаунта — CoffeePeek</title>
                </head>
                <body style="margin:0;padding:32px 16px;background:{BrandBg};color:{TextPrimary};font-family:'Segoe UI',Arial,sans-serif;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                        <tr>
                            <td align="center">
                                <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:{BrandSurface};border:1px solid {BrandBorder};border-radius:20px;">
                                    <tr>
                                        <td style="padding:36px;">
                                            <h1 style="margin:0 0 18px;font-size:26px;color:{TextPrimary};">Удаление аккаунта</h1>
                                            <p style="color:{TextMuted};line-height:1.6;">Привет, {safeUsername}. Мы получили запрос на удаление вашего аккаунта CoffeePeek.</p>
                                            <p style="color:{TextMuted};line-height:1.6;">Аккаунт пока остаётся активным. Чтобы продолжить, подтвердите запрос в течение 24 часов.</p>
                                            <p style="margin:28px 0;">
                                                <a href="{safeUrl}" style="display:inline-block;padding:14px 22px;border-radius:12px;background:{BrandGold};color:{BrandBg};font-weight:700;text-decoration:none;">Подтвердить удаление</a>
                                            </p>
                                            <p style="color:{TextDim};font-size:12px;line-height:1.5;">Если вы не создавали этот запрос, не подтверждайте его. Запрос можно отменить по ссылке на странице подтверждения.</p>
                                            <p style="color:{TextDim};font-size:11px;word-break:break-all;"><a href="{safeUrl}" style="color:{BrandGold};">{safeUrl}</a></p>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
                """;
    }

    public string GetWelcomeBackHtml(string username)
    {
        var safeUsername = WebUtility.HtmlEncode(username);

        return $"""
                <!DOCTYPE html>
                <html lang="ru">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="margin: 0; padding: 0; background-color: {BrandBg}; font-family: sans-serif; color: {TextPrimary};">
                    <table align="center" border="0" cellpadding="0" cellspacing="0" width="100%" style="max-width: 600px; margin-top: 40px; background-color: {BrandBg}; border: 1px solid {BrandBorder}; border-radius: 16px; overflow: hidden;">
                        <tr>
                            <td align="center" style="padding: 40px 0 20px 0; background-color: {BrandSurface};">
                                <div style="width: 60px; height: 60px; background-color: {BrandBg}; border: 1px solid {BrandBorder}; border-radius: 12px; display: inline-block; line-height: 60px; text-align: center;">
                                    <span style="font-size: 30px;">&#9749;</span>
                                </div>
                                <h1 style="margin: 20px 0 0 0; color: {TextPrimary}; font-size: 24px; letter-spacing: 1px;">
                                    Coffee<span style="color: {BrandGold};">Peek</span>
                                </h1>
                            </td>
                        </tr>
                        <tr>
                            <td style="padding: 40px; text-align: center;">
                                <h2 style="color: {TextPrimary}; font-size: 28px; margin-bottom: 20px;">С возвращением, {safeUsername}!</h2>
                                <p style="color: {TextMuted}; font-size: 16px; line-height: 1.6; margin-bottom: 30px;">
                                    Рады снова видеть вас в CoffeePeek. Ваши любимые кофейни и новые открытия ждут вас.
                                </p>
                                <p style="color: {TextDim}; font-size: 14px; margin-top: 20px;">
                                    Если вы не входили в аккаунт, немедленно смените пароль.
                                </p>
                            </td>
                        </tr>
                        <tr>
                            <td style="padding: 30px; background-color: {BrandBg}; border-top: 1px solid {BrandBorder}; text-align: center;">
                                <p style="color: {TextDim}; font-size: 12px; margin: 0;">&copy; {DateTime.UtcNow.Year} CoffeePeek. Все права защищены.</p>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
                """;
    }
}