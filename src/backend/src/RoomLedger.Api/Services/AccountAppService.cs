using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Nghiệp vụ tài khoản phía API (MD-01): hồ sơ GET /me, xem và nhận lời mời, trang chủ người thuê.
 * Đăng ký, đăng nhập, đăng xuất do FE gọi thẳng Cognito.
 *
 * @author Tuấn
 * @version 1.0
 */
public class AccountAppService(AppDbContext db, ICurrentUser user)
{
    /**
     * Hồ sơ người dùng hiện tại. Nếu chưa có app_user (PostConfirmation lỗi hoặc chưa chạy) thì tạo bù từ claim,
     * để các bảng khác luôn tham chiếu được người dùng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<MeResponse> GetMeAsync(CancellationToken ct)
    {
        var profile = await EnsureProfileAsync(ct);
        return new MeResponse(profile.Id, profile.FullName, profile.Email, user.Role);
    }

    /**
     * Xem trước lời mời khi chưa đăng nhập (MD-01-FE-04). Chỉ trả tên nhà trọ, mã phòng và email đã che.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<InvitePreviewResponse> PreviewInviteAsync(string token, CancellationToken ct)
    {
        var invite = await FindUsableInviteAsync(token, ct);
        var room = await db.Rooms.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == invite.Tenancy!.RoomId)
            .Select(r => new { r.Code, PropertyName = r.Property!.Name })
            .FirstAsync(ct);
        return new InvitePreviewResponse(room.PropertyName, room.Code, InviteService.MaskEmail(invite.Email), invite.ExpiresAt);
    }

    /**
     * Nhận lời mời (BR-04). Email đã xác minh trong token phải trùng email được mời.
     * UPDATE invite ... WHERE used_at IS NULL trong transaction chặn hai request dùng cùng một link.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<AcceptInviteResponse> AcceptInviteAsync(string token, CancellationToken ct)
    {
        var invite = await FindUsableInviteAsync(token, ct);
        InviteService.EnsureEmailMatches(user.Email, user.EmailVerified, invite.Email);
        await EnsureProfileAsync(ct);

        var tenantId = user.UserId;
        var now = db.Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Invites.IgnoreQueryFilters()
            .Where(i => i.Id == invite.Id && i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UsedAt, now)
                .SetProperty(i => i.UsedBy, tenantId)
                .SetProperty(i => i.UpdatedAt, now)
                .SetProperty(i => i.UpdatedBy, tenantId), ct);
        if (claimed == 0)
        {
            throw new InviteInvalidException();
        }

        var tenancy = await db.Tenancies.IgnoreQueryFilters().FirstAsync(t => t.Id == invite.TenancyId, ct);
        if (tenancy.TenantId is not null && tenancy.TenantId != tenantId)
        {
            throw new InviteInvalidException();
        }

        tenancy.TenantId = tenantId;
        var latestLocked = await db.TenancyVersions.IgnoreQueryFilters()
            .Where(v => v.TenancyId == tenancy.Id)
            .OrderByDescending(v => v.Number)
            .Select(v => v.Status == TenancyVersionStatus.Locked)
            .FirstAsync(ct);
        if (latestLocked && tenancy.Status == TenancyStatus.AwaitingTenant)
        {
            tenancy.Status = TenancyStatus.Active;
        }

        db.Audit(AuditEventTypes.InviteAccepted, nameof(Invite), invite.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AcceptInviteResponse(tenancy.Id, tenancy.Status);
    }

    private async Task<Invite> FindUsableInviteAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            throw new InviteInvalidException();
        }

        var hash = InviteService.HashToken(token);
        var invite = await db.Invites.IgnoreQueryFilters().AsNoTracking()
            .Include(i => i.Tenancy)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null || !InviteService.IsUsable(invite.UsedAt, invite.RevokedAt, invite.ExpiresAt, db.Now))
        {
            throw new InviteInvalidException();
        }

        return invite;
    }

    private async Task<AppUser> EnsureProfileAsync(CancellationToken ct)
    {
        var id = user.UserId;
        var profile = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (profile is not null)
        {
            return profile;
        }

        profile = new AppUser
        {
            Id = id,
            Email = user.Email ?? "",
            FullName = (user.FullName ?? "").Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 100)] : "",
            Role = user.Role ?? UserRole.Landlord,
        };
        db.AppUsers.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Hai request đầu tiên cùng tạo hồ sơ: bản kia đã ghi, đọc lại là đủ
            db.Entry(profile).State = EntityState.Detached;
            profile = await db.AppUsers.FirstAsync(u => u.Id == id, ct);
        }

        return profile;
    }
}
