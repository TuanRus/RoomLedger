using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp MD-02 (BR-05 đến BR-08) và định dạng lỗi chung.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PropertyRoomTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateProperty_InvalidData_ReturnsValidationErrorWithDetails()
    {
        var client = factory.ClientFor(TestUser.Landlord());
        var res = await client.PostJsonAsync("/api/v1/properties", new { name = "Nhà A", address = "", electricityRate = 0, waterRate = 20000 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        var fields = body.GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString()).ToList();
        Assert.Equal(["address", "electricityRate"], fields);
    }

    [Fact]
    public async Task CreateRoom_AddsMeterAreaAndStartsVacant()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}");
        var room = await res.DataAsync();
        Assert.Equal("VACANT", room.GetProperty("status").GetString());
        Assert.Equal("VACANT", room.GetProperty("displayStatus").GetString());
        Assert.Equal(["Phòng ngủ", "Đồng hồ điện nước"],
            room.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, room.GetProperty("currentTenancy").ValueKind);
    }

    [Fact]
    public async Task CreateRoom_DuplicateCode_ReturnsRoomCodeExists()
    {
        var s = await new Scenario(factory).WithRoomAsync("P.101");
        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/rooms",
            new { code = "P.101", monthlyRent = 3000000, areas = new[] { new { name = "Bếp", minPhotos = 2 } } });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ROOM_CODE_EXISTS", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task OtherLandlord_GetsNotFound_NotForbidden()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var other = factory.ClientFor(TestUser.Landlord("other@example.com"));
        var room = await other.GetAsync($"/api/v1/rooms/{s.RoomId}");
        Assert.Equal(HttpStatusCode.NotFound, room.StatusCode);
        Assert.Equal("NOT_FOUND", (await room.JsonAsync()).GetProperty("errorCode").GetString());
        var create = await other.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/rooms",
            new { code = "X.1", monthlyRent = 1, areas = new[] { new { name = "Bếp", minPhotos = 1 } } });
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        var list = await (await other.GetAsync("/api/v1/properties")).DataAsync();
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task TenantRole_IsForbiddenOnLandlordEndpoints()
    {
        var res = await factory.ClientFor(TestUser.Tenant()).GetAsync("/api/v1/properties");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Anonymous_GetsUnauthorizedJson()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/properties");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UpdateRoom_StaleVersion_ReturnsVersionConflict()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        var version = room.GetProperty("version").GetUInt32();

        var first = await s.LandlordClient.PatchJsonAsync($"/api/v1/rooms/{s.RoomId}", new { monthlyRent = 3600000, version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var updated = await first.DataAsync();
        Assert.Equal(3600000, updated.GetProperty("monthlyRent").GetInt64());
        Assert.NotEqual(version, updated.GetProperty("version").GetUInt32());

        var stale = await s.LandlordClient.PatchJsonAsync($"/api/v1/rooms/{s.RoomId}", new { monthlyRent = 3700000, version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("VERSION_CONFLICT", (await stale.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UpdateRoom_AreasWhileOccupied_ReturnsRoomOccupied()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        // Hợp đồng qua API thuộc MD-03; ở đây ghi thẳng một hợp đồng ACTIVE vào DB
        await factory.WithDbAsync(async db =>
        {
            var landlordId = s.Landlord.Id;
            db.AppUsers.Add(new AppUser { Id = s.Tenant.Id, Email = s.Tenant.Email, FullName = "Người thuê B", Role = UserRole.Tenant });
            db.Tenancies.Add(new Tenancy { RoomId = s.RoomId, LandlordId = landlordId, TenantId = s.Tenant.Id, Status = TenancyStatus.Active });
            await db.Rooms.Where(r => r.Id == s.RoomId).ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, RoomStatus.Occupied));
            return await db.SaveChangesAsync();
        });
        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        Assert.Equal("OCCUPIED", room.GetProperty("status").GetString());
        Assert.Equal("ACTIVE", room.GetProperty("displayStatus").GetString());
        Assert.Equal("Người thuê B", room.GetProperty("currentTenancy").GetProperty("tenantName").GetString());

        var res = await s.LandlordClient.PatchJsonAsync($"/api/v1/rooms/{s.RoomId}",
            new { areas = new[] { new { name = "Phòng ngủ", minPhotos = 4 } }, version = room.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ROOM_OCCUPIED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ListRooms_PaginatesWithCursorAndFiltersByDisplayStatus()
    {
        var s = await new Scenario(factory).WithRoomAsync("A.1");
        foreach (var code in new[] { "A.2", "A.3" })
        {
            await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/rooms",
                new { code, monthlyRent = 1000000, areas = new[] { new { name = "Bếp", minPhotos = 1 } } });
        }

        var page1 = await (await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/rooms?limit=2")).DataAsync();
        Assert.Equal(["A.3", "A.2"], page1.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()));
        var cursor = page1.GetProperty("nextCursor").GetString();
        var page2 = await (await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/rooms?limit=2&cursor={cursor}")).DataAsync();
        Assert.Equal(["A.1"], page2.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()));
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextCursor").ValueKind);

        var awaiting = await (await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/rooms?status=AWAITING_CONFIRM")).DataAsync();
        Assert.Empty(awaiting.GetProperty("items").EnumerateArray());
        var vacant = await (await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/rooms?status=VACANT")).DataAsync();
        Assert.Equal(3, vacant.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ListRooms_LimitOutOfRange_ReturnsValidationError()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/rooms?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonAndUnknownRoute_ReturnStandardErrors()
    {
        var client = factory.ClientFor(TestUser.Landlord());
        var bad = await client.PostAsync("/api/v1/properties", new StringContent("{oops", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await bad.JsonAsync()).GetProperty("errorCode").GetString());

        var missing = await client.GetAsync("/api/v1/khong-co");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("NOT_FOUND", (await missing.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
