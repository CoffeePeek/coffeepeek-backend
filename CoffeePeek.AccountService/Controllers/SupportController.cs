using System.Text.Encodings.Web;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Infrastructure.Options;
using CoffeePeek.AccountService.Controllers.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/support")]
[Tags("Support")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportController(ISupportEmailSender sender, IOptions<SupportOptions> options) : ControllerBase
{
    /// <summary>Public support page for the App Store Support URL.</summary>
    [HttpGet("/support")]
    [Produces("text/html")]
    public IActionResult Page()
    {
        var email = HtmlEncoder.Default.Encode(options.Value.Email);
        var html = $$"""
                     <!doctype html>
                     <html lang="en">
                     <head>
                       <meta charset="utf-8">
                       <meta name="viewport" content="width=device-width, initial-scale=1">
                       <title>CoffeePeek Support</title>
                     </head>
                     <body>
                       <main>
                         <h1>CoffeePeek Support</h1>
                         <p>For app issues, questions, feedback, or feature requests, email us at
                           <a href="mailto:{{email}}">{{email}}</a>.</p>
                         <p>По вопросам работы приложения и предложениям пишите на
                           <a href="mailto:{{email}}">{{email}}</a>.</p>
                       </main>
                     </body>
                     </html>
                     """;
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Get the public email address for user support.</summary>
    [HttpGet("contact")]
    [ProducesResponseType<SupportContactResponse>(StatusCodes.Status200OK)]
    public IActionResult Contact() => Ok(new SupportContactResponse(options.Value.Email));

    /// <summary>Send a support request without requiring an account.</summary>
    [HttpPost("requests")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit([FromBody] CreateSupportRequest request, CancellationToken ct)
    {
        var message = request.Message.Trim();
        if (message.Length < 10)
        {
            ModelState.AddModelError(nameof(request.Message), "Message must contain at least 10 non-whitespace characters.");
            return ValidationProblem(ModelState);
        }

        await sender.SendAsync(request.Email.Trim(), message, ct);
        return NoContent();
    }
}
