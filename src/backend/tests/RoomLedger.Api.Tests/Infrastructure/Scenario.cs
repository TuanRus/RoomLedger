using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests.Infrastructure;

/**
 * Dựng nhanh dữ liệu qua API thật: nhà trọ, phòng, hợp đồng đã khóa có người thuê.
 *
 * @author Tuấn
 * @version 1.0
 */
public class Scenario(ApiFactory factory)
{
    public const string Sha = "n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=";

    public TestUser Landlord { get; } = TestUser.Landlord($"landlord-{Guid.NewGuid():N}@example.com");
    public TestUser Tenant { get; } = TestUser.Tenant($"tenant-{Guid.NewGuid():N}@example.com");
    public HttpClient LandlordClient => _landlord ??= factory.ClientFor(Landlord);
    public HttpClient TenantClient => _tenant ??= factory.ClientFor(Tenant);
    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid TenancyId { get; set; }

    private HttpClient? _landlord;
    private HttpClient? _tenant;

    /**
     * Tạo nhà trọ và một phòng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Scenario> WithRoomAsync(string code = "P.204")
    {
        var p = await LandlordClient.PostJsonAsync("/api/v1/properties",
            new { name = "Nhà trọ Hoa Sen", address = "12 Lê Lợi, Q1", electricityRate = 3500, waterRate = 20000, serviceFee = 100000 });
        Assert.Equal(HttpStatusCode.Created, p.StatusCode);
        PropertyId = (await p.DataAsync()).GetProperty("propertyId").GetGuid();
        var r = await LandlordClient.PostJsonAsync($"/api/v1/properties/{PropertyId}/rooms",
            new { code, monthlyRent = 3500000, areas = new[] { new { name = "Phòng ngủ", minPhotos = 4 } } });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        RoomId = (await r.DataAsync()).GetProperty("roomId").GetGuid();
        return this;
    }

    /**
     * Tạo hợp đồng, lưu điều khoản, mời, người thuê nhận lời mời và xác nhận → hợp đồng ACTIVE, phiên bản v1 LOCKED.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Scenario> WithActiveTenancyAsync()
    {
        await WithRoomAsync();
        var t = await LandlordClient.PostAsync($"/api/v1/rooms/{RoomId}/tenancies", null);
        TenancyId = (await t.DataAsync()).GetProperty("tenancyId").GetGuid();
        var terms = await LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{TenancyId}/versions/1/terms", ValidTerms);
        Assert.Equal(HttpStatusCode.OK, terms.StatusCode);
        var token = await InviteAsync(Tenant.Email);
        var accept = await TenantClient.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var confirm = await TenantClient.PostJsonAsync($"/api/v1/tenancies/{TenancyId}/versions/1/confirm", new { decision = "CONFIRM" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        return this;
    }

    /**
     * Gửi lời mời và lấy token gốc từ sự kiện InviteCreated (thay cho email).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<string> InviteAsync(string email)
    {
        var res = await LandlordClient.PostJsonAsync($"/api/v1/tenancies/{TenancyId}/invites", new { email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return factory.Events.Last("InviteCreated").GetProperty("token").GetString()!;
    }

    /**
     * Ghi chỉ số đã xác nhận trực tiếp vào database (thay cho luồng AI) để test hóa đơn.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Guid> ConfirmedReadingAsync(string period, MeterKind kind, long value)
    {
        var res = await LandlordClient.PostJsonAsync($"/api/v1/rooms/{RoomId}/meter-readings",
            new { kind, period, fileName = "dong-ho.jpg", contentType = "image/jpeg", size = 1000, sha256 = Sha });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.DataAsync()).GetProperty("reading").GetProperty("readingId").GetGuid();
        var patch = await LandlordClient.PatchJsonAsync($"/api/v1/meter-readings/{id}", new { value, confirm = true });
        Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync());
        return id;
    }

    public static readonly object ValidTerms = new
    {
        monthlyRent = 3500000,
        deposit = 7000000,
        startDate = "2026-11-01",
        endDate = "2027-10-31",
        electricityRate = 3500,
        waterRate = 20000,
    };
}
