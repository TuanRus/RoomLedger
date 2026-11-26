using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RoomLedger.Api.LocalDev;

/**
 * Đăng nhập giả khi chạy API trên máy (chỉ khi bật LocalDev, xem LocalDevSetup), thay cho Cognito trong lúc
 * chưa có đăng nhập thật ở FE. Header X-Dev-User = sub|email|group|emailVerified|name, giống X-Test-User của test,
 * được đổi thành đúng các claim mà Cognito authorizer truyền vào (sub, email, email_verified, cognito:groups, name).
 *
 * @author Tuấn
 * @version 1.0
 */
public class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "LocalDev";
    public const string Header = "X-Dev-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.Split('|');
        if (parts.Length != 5 || !Guid.TryParse(parts[0], out _))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} phải có dạng sub|email|group|emailVerified|name"));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", parts[0]),
            new Claim("email", parts[1]),
            new Claim("cognito:groups", parts[2]),
            new Claim("email_verified", parts[3].ToLowerInvariant()),
            new Claim("name", Uri.UnescapeDataString(parts[4])),
        ], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
