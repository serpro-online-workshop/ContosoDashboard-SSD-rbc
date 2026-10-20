using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests.TestInfrastructure;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationSchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userId) ||
            !int.TryParse(userId, out var parsedUserId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parsedUserId.ToString()),
            new Claim(ClaimTypes.Name, $"Test user {parsedUserId}"),
            new Claim(ClaimTypes.Role, "Employee")
        };
        var identity = new ClaimsIdentity(claims, AuthenticationSchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationSchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}