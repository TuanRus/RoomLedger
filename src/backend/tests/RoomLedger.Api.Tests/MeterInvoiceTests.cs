using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp MD-05 (BR-20 đến BR-24).
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterInvoiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ConfirmedPeriod_ReturnsPeriodClosed()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        var res = await s.TenantClient.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/meter-readings",
            new { kind = "ELECTRICITY", period = "2026-10", fileName = "a.jpg", contentType = "image/jpeg", size = 10, sha256 = Scenario.Sha });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PERIOD_CLOSED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Confirm_RejectsValueLowerThanPreviousUnlessMeterReplaced()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        var created = await (await s.LandlordClient.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/meter-readings",
            new { kind = "WATER", period = "2026-11", fileName = "a.jpg", contentType = "image/jpeg", size = 10, sha256 = Scenario.Sha })).DataAsync();
        var id = created.GetProperty("reading").GetProperty("readingId").GetGuid();
        Assert.Equal(50, created.GetProperty("reading").GetProperty("previousValue").GetInt64());

        var lower = await s.LandlordClient.PatchJsonAsync($"/api/v1/meter-readings/{id}", new { value = 40, confirm = true });
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
        var replaced = await s.LandlordClient.PatchJsonAsync($"/api/v1/meter-readings/{id}", new { value = 4, meterReplaced = true, note = "thay đồng hồ", confirm = true });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal("CONFIRMED", (await replaced.DataAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConfirmAll_SkipsFlaggedReadings()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        foreach (var kind in new[] { "ELECTRICITY", "WATER" })
        {
            await s.LandlordClient.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/meter-readings",
                new { kind, period = "2026-11", fileName = "a.jpg", contentType = "image/jpeg", size = 10, sha256 = Scenario.Sha });
        }

        // Giả lập persist-results: điện đọc tốt, nước bị đánh dấu cần kiểm tra
        await factory.WithDbAsync(async db =>
        {
            var readings = await db.MeterReadings.Where(m => m.RoomId == s.RoomId && m.Period == "2026-11").ToListAsync();
            foreach (var m in readings)
            {
                m.AiValue = m.Kind == MeterKind.Electricity ? 1200 : 60;
                m.AiConfidence = m.Kind == MeterKind.Electricity ? 0.95m : 0.5m;
                m.Status = m.Kind == MeterKind.Electricity ? MeterReadingStatus.PendingConfirm : MeterReadingStatus.NeedsReview;
            }

            return await db.SaveChangesAsync();
        });

        var result = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/meter-readings:confirm-all",
            new { period = "2026-11" })).DataAsync();
        Assert.Equal(1, result.GetProperty("confirmed").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetArrayLength());

        var table = await (await s.LandlordClient.GetAsync($"/api/v1/properties/{s.PropertyId}/meter-readings?period=2026-11")).DataAsync();
        var row = table[0];
        Assert.Equal("CONFIRMED", row.GetProperty("electricity").GetProperty("status").GetString());
        Assert.True(row.GetProperty("water").GetProperty("needsReview").GetBoolean());
    }

    [Fact]
    public async Task InvoiceFlow_GenerateSendDisputeResolvePay()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Electricity, 1120);
        var waterId = await s.ConfirmedReadingAsync("2026-11", MeterKind.Water, 56);

        var future = await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-12" });
        Assert.Equal("INVALID_PERIOD", (await future.JsonAsync()).GetProperty("errorCode").GetString());

        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        var created = gen.GetProperty("created")[0];
        // 3.500.000 + 120 kWh × 3.500 + 6 m³ × 20.000 + 100.000 phí dịch vụ
        Assert.Equal(4_140_000, created.GetProperty("total").GetInt64());
        var invoiceId = created.GetProperty("invoiceId").GetGuid();

        var again = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        Assert.Equal("ALREADY_INVOICED", again.GetProperty("skipped")[0].GetProperty("reason").GetString());

        // Người thuê không thấy hóa đơn nháp
        Assert.Equal(HttpStatusCode.NotFound, (await s.TenantClient.GetAsync($"/api/v1/invoices/{invoiceId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/send", null)).StatusCode);
        var list = await (await s.TenantClient.GetAsync("/api/v1/invoices?period=2026-11&status=SENT")).DataAsync();
        Assert.Equal(invoiceId, list.GetProperty("items")[0].GetProperty("invoiceId").GetGuid());

        var detail = await (await s.TenantClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(6, detail.GetProperty("water").GetProperty("usage").GetInt64());

        var dispute = await s.TenantClient.PostJsonAsync($"/api/v1/invoices/{invoiceId}/disputes",
            new { readingId = waterId, reason = "Nước tháng này không thể dùng 6 khối" });
        Assert.Equal(HttpStatusCode.Created, dispute.StatusCode);
        var disputeId = (await dispute.DataAsync()).GetProperty("disputeId").GetGuid();

        var paid = await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/paid", null);
        Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);
        Assert.Equal("Hãy xử lý khiếu nại trước", (await paid.JsonAsync()).GetProperty("message").GetString());

        var resolved = await s.LandlordClient.PatchJsonAsync($"/api/v1/disputes/{disputeId}",
            new { resolution = "CORRECTED", value = 54, response = "Đọc nhầm số, đã sửa" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var after = await (await s.LandlordClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal("SENT", after.GetProperty("status").GetString());
        Assert.Equal(4_100_000, after.GetProperty("total").GetInt64());

        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/paid", null)).StatusCode);
        var late = await s.TenantClient.PostJsonAsync($"/api/v1/invoices/{invoiceId}/disputes", new { readingId = waterId, reason = "Khiếu nại sau khi trả tiền" });
        Assert.Equal("INVALID_STATE", (await late.JsonAsync()).GetProperty("errorCode").GetString());

        var home = await (await s.TenantClient.GetAsync("/api/v1/tenant/home")).DataAsync();
        Assert.Equal("PAID", home.GetProperty("latestInvoice").GetProperty("status").GetString());
        Assert.Equal(7_000_000, home.GetProperty("deposit").GetInt64());
    }

    [Fact]
    public async Task Generate_SkipsRoomsWithoutConfirmedReadings()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        Assert.Empty(gen.GetProperty("created").EnumerateArray());
        Assert.Equal("MISSING_READINGS", gen.GetProperty("skipped")[0].GetProperty("reason").GetString());
    }
}
