using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using JabberAlertBridge.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JabberAlertBridge.Api.Authentication;

public sealed class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<WebhookAuthOptions> credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WebhookBasic";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out AuthenticationHeaderValue? header)
            || !header.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            byte[] supplied = Convert.FromBase64String(header.Parameter);
            byte[] expected = Encoding.UTF8.GetBytes($"{credentials.Value.Username}:{credentials.Value.Password}");

            bool valid = CryptographicOperations.FixedTimeEquals(SHA256.HashData(supplied), SHA256.HashData(expected));

            CryptographicOperations.ZeroMemory(supplied);
            CryptographicOperations.ZeroMemory(expected);

            if (!valid)
            {
                return Task.FromResult(AuthenticateResult.Fail("Неверные учётные данные."));
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "webhook")], SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Некорректный заголовок аутентификации."));
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"JabberAlertBridge\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }
}
