using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Lambda.CognitoEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;

namespace RoomLedger.Functions.PostConfirmation;

/**
 * Trigger PostConfirmation của User Pool roomledger-{stage}-users (BR-01 RULE 5, BR-04 RULE 2).
 * Gán group landlord, hoặc tenant khi người dùng đăng ký từ link mời hợp lệ có cùng email
 * (FE gửi clientMetadata.inviteToken lúc ConfirmSignUp), rồi tạo bản ghi app_user.
 * Chạy trong VPC: RDS qua IAM auth, Cognito qua Interface endpoint.
 *
 * @author Tuấn
 * @version 1.0
 */
public class Function
{
    private static readonly AmazonCognitoIdentityProviderClient Cognito = new();

    /**
     * Xử lý sự kiện; lỗi được ném ra để Cognito báo xác minh thất bại thay vì tạo user thiếu group.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<CognitoPostConfirmationEvent> HandleAsync(CognitoPostConfirmationEvent input, ILambdaContext context)
    {
        // Quên mật khẩu cũng gọi PostConfirmation: không gán lại group
        if (input.TriggerSource != "PostConfirmation_ConfirmSignUp")
        {
            return input;
        }

        var attrs = input.Request.UserAttributes;
        var sub = Guid.Parse(attrs["sub"]);
        var email = attrs.GetValueOrDefault("email", "").Trim().ToLowerInvariant();
        var name = attrs.GetValueOrDefault("name", "").Trim();
        var inviteToken = input.Request.ClientMetadata?.GetValueOrDefault("inviteToken");

        await using var db = FunctionDb.Create();
        var role = await IsValidInviteAsync(db, inviteToken, email) ? UserRole.Tenant : UserRole.Landlord;

        await Cognito.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest
        {
            UserPoolId = input.UserPoolId,
            Username = input.UserName,
            GroupName = role == UserRole.Tenant ? "tenant" : "landlord",
        });

        // ON CONFLICT DO NOTHING: Cognito có thể gọi lại trigger, GET /me cũng có thể đã tạo bù hồ sơ
        var now = DateTimeOffset.UtcNow;
        var roleText = role == UserRole.Tenant ? "TENANT" : "LANDLORD";
        var fullName = name[..Math.Min(name.Length, 100)];
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app_user (id, email, full_name, role, created_at, updated_at, created_by, updated_by)
            VALUES ({sub}, {email}, {fullName}, {roleText}, {now}, {now}, {sub}, {sub})
            ON CONFLICT (id) DO NOTHING
            """);
        context.Logger.LogInformation($"PostConfirmation role={role}");
        return input;
    }

    private static async Task<bool> IsValidInviteAsync(Infrastructure.Persistence.AppDbContext db, string? token, string email)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var hash = InviteService.HashToken(token);
        var invite = await db.Invites.AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash);
        return invite is not null
            && InviteService.IsUsable(invite.UsedAt, invite.RevokedAt, invite.ExpiresAt, DateTimeOffset.UtcNow)
            && string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase);
    }
}
