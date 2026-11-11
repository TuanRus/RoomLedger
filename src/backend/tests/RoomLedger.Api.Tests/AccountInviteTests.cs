using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp MD-01: GET /me và lời mời (BR-04, INVITE_INVALID, INVITE_EMAIL_MISMATCH).
 *
 * @author Tuấn
 * @version 1.0
 */
public class AccountInviteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Me_CreatesProfileFromClaimsOnFirstCall()
    {
        var user = TestUser.Landlord($"me-{Guid.NewGuid():N}@example.com");
        var me = await (await factory.ClientFor(user).GetAsync("/api/v1/me")).DataAsync();
        Assert.Equal(user.Id, me.GetProperty("userId").GetGuid());
        Assert.Equal("LANDLORD", me.GetProperty("role").GetString());
        var stored = await factory.WithDbAsync(db => db.AppUsers.SingleAsync(u => u.Id == user.Id));
        Assert.Equal(user.Email, stored.Email);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401Json()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Invite_PreviewMasksEmail_AndAcceptBindsTenant()
    {
        var tenant = TestUser.Tenant($"tenant-{Guid.NewGuid():N}@example.com");
        var (tenancyId, token) = await SeedInviteAsync(tenant.Email);

        var preview = await (await factory.CreateClient().GetAsync($"/api/v1/invites/{token}")).DataAsync();
        Assert.Equal("P.204", preview.GetProperty("roomCode").GetString());
        Assert.DoesNotContain(tenant.Email, preview.GetProperty("emailMasked").GetString());

        var accept = await factory.ClientFor(tenant).PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal("AWAITING_TENANT", (await accept.DataAsync()).GetProperty("status").GetString());

        var stored = await factory.WithDbAsync(db => db.Tenancies.SingleAsync(t => t.Id == tenancyId));
        Assert.Equal(tenant.Id, stored.TenantId);

        // Link chỉ dùng được một lần
        var reuse = await factory.ClientFor(tenant).PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, reuse.StatusCode);
        Assert.Equal("INVITE_INVALID", (await reuse.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Invite_OtherEmail_ReturnsEmailMismatch()
    {
        var (_, token) = await SeedInviteAsync($"tenant-{Guid.NewGuid():N}@example.com");
        var stranger = factory.ClientFor(TestUser.Tenant("stranger@example.com"));

        var res = await stranger.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("INVITE_EMAIL_MISMATCH", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Invite_Expired_ReturnsGone()
    {
        var tenant = TestUser.Tenant($"tenant-{Guid.NewGuid():N}@example.com");
        var (_, token) = await SeedInviteAsync(tenant.Email, expiresIn: TimeSpan.FromHours(-1));

        var res = await factory.ClientFor(tenant).PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
    }

    [Fact]
    public async Task Invite_UnknownToken_ReturnsInviteInvalid()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/invites/khong-ton-tai");
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
        Assert.Equal("INVITE_INVALID", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    /**
     * Tạo sẵn nhà trọ, phòng, hợp đồng chờ người thuê và một lời mời; trả về token gốc.
     * Phần tạo lời mời qua API thuộc MD-03 nên ở đây ghi thẳng vào DB.
     *
     * @author Tuấn
     * @version 1.0
     */
    private async Task<(Guid TenancyId, string Token)> SeedInviteAsync(string email, TimeSpan? expiresIn = null)
    {
        var landlordId = Guid.NewGuid();
        var token = InviteService.GenerateToken();
        var tenancyId = await factory.WithDbAsync(async db =>
        {
            var property = new Property { LandlordId = landlordId, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi, Q1" };
            var room = new Room { Property = property, LandlordId = landlordId, Code = "P.204", MonthlyRent = 3_500_000 };
            var tenancy = new Tenancy { Room = room, LandlordId = landlordId, Status = TenancyStatus.AwaitingTenant };
            // Hợp đồng luôn có phiên bản v1 (tạo cùng lúc với hợp đồng ở MD-03)
            tenancy.Versions.Add(new TenancyVersion { Number = 1, Status = TenancyVersionStatus.AwaitingConfirm });
            db.Invites.Add(new Invite
            {
                Tenancy = tenancy,
                TokenHash = token.TokenHash,
                Email = InviteService.NormalizeEmail(email),
                ExpiresAt = factory.Clock.GetUtcNow() + (expiresIn ?? InviteService.Lifetime),
            });
            await db.SaveChangesAsync();
            return tenancy.Id;
        });
        return (tenancyId, token.Token);
    }
}
