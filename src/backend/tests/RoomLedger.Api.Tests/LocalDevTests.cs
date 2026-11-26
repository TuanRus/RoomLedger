using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RoomLedger.Api.Tests.Infrastructure;

namespace RoomLedger.Api.Tests;

/**
 * API chạy ở chế độ LocalDev (Development + LocalDev:Enabled=true): đăng nhập bằng X-Dev-User,
 * S3/EventBridge trong bộ nhớ. Dùng đúng các service thật của LocalDev, không thay bằng bản giả của test.
 *
 * @author Tuấn
 * @version 1.0
 */
public class LocalDevFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RoomLedger", ConnectionString);
        builder.UseSetting("LocalDev:Enabled", "true");
        builder.UseSetting("OpenApi:Enabled", "false");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public HttpClient DevClientFor(TestUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", $"{user.Id}|{user.Email}|{user.Group}|true|{Uri.EscapeDataString(user.Name)}");
        return client;
    }
}

/**
 * Luồng chính chạy được qua LocalDev như FE sẽ gọi: tạo nhà, phòng, hợp đồng, mời, nhận lời mời,
 * ghi chỉ số kèm kết quả AI giả, duyệt, lập và gửi hóa đơn, người thuê xem hóa đơn.
 *
 * @author Tuấn
 * @version 1.0
 */
public class LocalDevTests(LocalDevFactory factory) : IClassFixture<LocalDevFactory>
{
    [Fact]
    public async Task MainFlow_WorksWithDevHeader()
    {
        var landlord = factory.DevClientFor(TestUser.Landlord($"ll-{Guid.NewGuid():N}@example.com"));
        var tenantUser = TestUser.Tenant($"tn-{Guid.NewGuid():N}@example.com") with { Name = "Trần Minh Anh" };
        var tenant = factory.DevClientFor(tenantUser);

        var p = await landlord.PostJsonAsync("/api/v1/properties",
            new { name = "Nhà trọ Cô Lan", address = "12 Lê Lợi, Q1", electricityRate = 3500, waterRate = 20000, serviceFee = 100000 });
        Assert.Equal(HttpStatusCode.Created, p.StatusCode);
        var propertyId = (await p.DataAsync()).GetProperty("propertyId").GetGuid();
        var r = await landlord.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms",
            new { code = "P.204", monthlyRent = 3500000, areas = new[] { new { name = "Phòng ngủ", minPhotos = 4 } } });
        var roomId = (await r.DataAsync()).GetProperty("roomId").GetGuid();
        var t = await landlord.PostAsync($"/api/v1/rooms/{roomId}/tenancies", null);
        var tenancyId = (await t.DataAsync()).GetProperty("tenancyId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await landlord.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await landlord.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/invites", new { email = tenantUser.Email })).StatusCode);

        // Token lời mời lấy từ EventBridge trong bộ nhớ, thay cho email
        var invite = await landlord.GetAsync($"/api/v1/local-dev/invites/latest?email={tenantUser.Email}");
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        var token = (await invite.DataAsync()).GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.OK, (await tenant.PostAsync($"/api/v1/invites/{token}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenant.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm", new { decision = "CONFIRM" })).StatusCode);

        var me = await (await tenant.GetAsync("/api/v1/me")).DataAsync();
        Assert.Equal("Trần Minh Anh", me.GetProperty("fullName").GetString());

        // Chỉ số: kỳ trước xác nhận tay, kỳ này có kết quả AI giả rồi chủ nhà duyệt
        foreach (var (kind, previous, current) in new[] { ("ELECTRICITY", 1100L, 1260L), ("WATER", 60L, 66L) })
        {
            var prev = await CreateReadingAsync(landlord, roomId, kind, "2026-10");
            Assert.True((await landlord.PatchJsonAsync($"/api/v1/meter-readings/{prev}", new { value = previous, confirm = true })).IsSuccessStatusCode);
            var id = await CreateReadingAsync(landlord, roomId, kind, "2026-11");
            var ai = await landlord.PostJsonAsync($"/api/v1/local-dev/meter-readings/{id}/ai-result", new { aiValue = current, aiConfidence = 0.95, needsReview = false });
            Assert.Equal(HttpStatusCode.OK, ai.StatusCode);
        }

        var rows = await (await landlord.GetAsync($"/api/v1/properties/{propertyId}/meter-readings?period=2026-11")).DataAsync();
        var elec = rows[0].GetProperty("electricity");
        Assert.Equal("PENDING_CONFIRM", elec.GetProperty("status").GetString());
        Assert.Equal(1260, elec.GetProperty("aiValue").GetInt64());
        Assert.Equal(1100, elec.GetProperty("previousValue").GetInt64());

        var all = await (await landlord.PostJsonAsync($"/api/v1/properties/{propertyId}/meter-readings:confirm-all", new { period = "2026-11" })).DataAsync();
        Assert.Equal(2, all.GetProperty("confirmed").GetInt32());

        var gen = await (await landlord.PostJsonAsync($"/api/v1/properties/{propertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        var invoiceId = gen.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await landlord.PostAsync($"/api/v1/invoices/{invoiceId}/send", null)).StatusCode);

        var list = await (await tenant.GetAsync("/api/v1/invoices")).DataAsync();
        Assert.Equal(invoiceId, list.GetProperty("items")[0].GetProperty("invoiceId").GetGuid());
        var detail = await (await tenant.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(160, detail.GetProperty("electricity").GetProperty("usage").GetInt64());
        Assert.Equal(6, detail.GetProperty("water").GetProperty("usage").GetInt64());
    }

    [Fact]
    public async Task Upload_PutThenGet_MarksMeterPhotoReceived()
    {
        var landlord = factory.DevClientFor(TestUser.Landlord($"ll-{Guid.NewGuid():N}@example.com"));
        var tenantUser = TestUser.Tenant($"tn-{Guid.NewGuid():N}@example.com");
        var tenant = factory.DevClientFor(tenantUser);
        var p = await (await landlord.PostJsonAsync("/api/v1/properties",
            new { name = "Nhà A", address = "1 Lê Lợi", electricityRate = 3500, waterRate = 20000, serviceFee = 0 })).DataAsync();
        var propertyId = p.GetProperty("propertyId").GetGuid();
        var r = await (await landlord.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms",
            new { code = "P.1", monthlyRent = 3000000, areas = new[] { new { name = "Phòng", minPhotos = 1 } } })).DataAsync();
        var roomId = r.GetProperty("roomId").GetGuid();
        var tenancyId = (await (await landlord.PostAsync($"/api/v1/rooms/{roomId}/tenancies", null)).DataAsync()).GetProperty("tenancyId").GetGuid();
        await landlord.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms);
        await landlord.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/invites", new { email = tenantUser.Email });
        var token = (await (await landlord.GetAsync($"/api/v1/local-dev/invites/latest?email={tenantUser.Email}")).DataAsync()).GetProperty("token").GetString();
        await tenant.PostAsync($"/api/v1/invites/{token}/accept", null);
        await tenant.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm", new { decision = "CONFIRM" });

        var created = await (await landlord.PostJsonAsync($"/api/v1/rooms/{roomId}/meter-readings",
            new { kind = "ELECTRICITY", period = "2026-11", fileName = "dong-ho.jpg", contentType = "image/jpeg", size = 3, sha256 = Scenario.Sha })).DataAsync();
        var uploadUrl = created.GetProperty("upload").GetProperty("uploadUrl").GetString()!;
        Assert.StartsWith("/api/v1/local-dev/files/incoming/", uploadUrl);

        // PUT không cần đăng nhập, như presigned URL của S3
        var put = await factory.CreateClient().PutAsync(uploadUrl, new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/jpeg") } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var rows = await (await landlord.GetAsync($"/api/v1/properties/{propertyId}/meter-readings?period=2026-11")).DataAsync();
        var elec = rows[0].GetProperty("electricity");
        Assert.Equal("RECEIVED", elec.GetProperty("photoStatus").GetString());
        var photo = await factory.CreateClient().GetAsync(elec.GetProperty("photoUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal([1, 2, 3], await photo.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MalformedDevHeader_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "not-a-guid|a@example.com|landlord|true|A");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/properties")).StatusCode);
    }

    [Fact]
    public async Task LocalDevEndpoints_AreLandlordOnly()
    {
        var tenant = factory.DevClientFor(TestUser.Tenant($"tn-{Guid.NewGuid():N}@example.com"));
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync("/api/v1/local-dev/invites/latest?email=a@example.com")).StatusCode);
    }

    private static async Task<Guid> CreateReadingAsync(HttpClient landlord, Guid roomId, string kind, string period)
    {
        var res = await landlord.PostJsonAsync($"/api/v1/rooms/{roomId}/meter-readings",
            new { kind, period, fileName = "dong-ho.jpg", contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.DataAsync()).GetProperty("reading").GetProperty("readingId").GetGuid();
    }
}

/**
 * Ngoài LocalDev (môi trường Testing ở đây, Lambda trên AWS), header X-Dev-User không có tác dụng
 * và không có endpoint /local-dev.
 *
 * @author Tuấn
 * @version 1.0
 */
public class LocalDevDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task DevHeader_IsIgnoredOutsideLocalDev()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", $"{Guid.NewGuid()}|a@example.com|landlord|true|A");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/properties")).StatusCode);
    }

    [Fact]
    public async Task LocalDevEndpoints_DoNotExistOutsideLocalDev()
    {
        var landlord = factory.ClientFor(TestUser.Landlord());
        Assert.Equal(HttpStatusCode.NotFound, (await landlord.GetAsync("/api/v1/local-dev/invites/latest?email=a@example.com")).StatusCode);
    }
}
