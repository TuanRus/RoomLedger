using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RoomLedger.Api.Tests.Infrastructure;

/**
 * Thay Cognito authorizer trong test: header X-Test-User = sub|email|group|emailVerified|name
 * được đổi thành đúng các claim API Gateway truyền vào (sub, email, email_verified, cognito:groups, name).
 *
 * @author Tuấn
 * @version 1.0
 */
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.Split('|');
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", parts[0]),
            new Claim("email", parts[1]),
            new Claim("cognito:groups", parts[2]),
            new Claim("email_verified", parts[3].ToLowerInvariant()),
            new Claim("name", parts[4]),
        ], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
