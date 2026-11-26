using System.Net;
using System.Net.Http.Json;
using RoomLedger.Api.Services;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp thông tin chuyển khoản theo nhà trọ, mã QR (tải lên → xác nhận → xem → xóa),
 * khối payment trên hóa đơn và PATCH đơn giá nhà trọ.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PaymentInfoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object ValidInfo = new
    {
        bankName = "Vietcombank",
        accountNumber = "0123 456 789",
        accountHolder = "NGUYEN VAN A",
        transferNoteTemplate = (string?)null,
    };

    private static string InfoUrl(Guid propertyId) => $"/api/v1/properties/{propertyId}/payment-info";

    private static string StripHost(string url) => new Uri(url).AbsolutePath.TrimStart('/');

    private static Task<HttpResponseMessage> Put(HttpClient c, string url, object body) =>
        c.PutAsJsonAsync(url, body, ApiFactory.Json);

    private static object QrBody(Guid propertyId, string contentType = "image/png", long size = 1500) => new
    {
        targetType = "PROPERTY_QR", targetId = propertyId, fileName = "qr.png", contentType, size, sha256 = Scenario.Sha,
    };

    /** Xin link, đặt file lên "S3" giả như trình duyệt PUT, trả key incoming. */
    private async Task<(Guid FileId, string Key)> UploadQrAsync(Scenario s, int bytes = 1500, bool put = true)
    {
        var res = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId, size: bytes));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await res.DataAsync();
        var key = StripHost(data.GetProperty("uploadUrl").GetString()!);
        if (put)
        {
            factory.ObjectStore.Objects[(StorageBucket.Incoming, key)] = new byte[bytes];
        }

        return (data.GetProperty("fileId").GetGuid(), key);
    }

    [Fact]
    public async Task Get_NewProperty_ReturnsDefaultTemplateAndNoQr()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await s.LandlordClient.GetAsync(InfoUrl(s.PropertyId));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.DataAsync();
        Assert.Equal("{room} T{month}", data.GetProperty("transferNoteTemplate").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("bankName").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("qrUrl").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("qrStatus").ValueKind);
    }

    [Fact]
    public async Task Put_SavesAndNormalizesAccountNumber()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await Put(s.LandlordClient, InfoUrl(s.PropertyId), new
        {
            bankName = " Vietcombank ", accountNumber = "0123 456 789", accountHolder = "NGUYEN VAN A", transferNoteTemplate = "{room} {month}/{year}",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await (await s.LandlordClient.GetAsync(InfoUrl(s.PropertyId))).DataAsync();
        Assert.Equal("Vietcombank", data.GetProperty("bankName").GetString());
        Assert.Equal("0123456789", data.GetProperty("accountNumber").GetString());
        Assert.Equal("{room} {month}/{year}", data.GetProperty("transferNoteTemplate").GetString());

        // Mẫu rỗng quay về mặc định
        await Put(s.LandlordClient, InfoUrl(s.PropertyId), new { bankName = "VCB", accountNumber = "123456", accountHolder = "A", transferNoteTemplate = "  " });
        var again = await (await s.LandlordClient.GetAsync(InfoUrl(s.PropertyId))).DataAsync();
        Assert.Equal("{room} T{month}", again.GetProperty("transferNoteTemplate").GetString());
    }

    [Theory]
    [InlineData("", "0123456789", "A", null, "bankName")]
    [InlineData("VCB", "12345", "A", null, "accountNumber")]
    [InlineData("VCB", "123456789012345678901", "A", null, "accountNumber")]
    [InlineData("VCB", "0123-456-789", "A", null, "accountNumber")]
    [InlineData("VCB", "0123456789", "", null, "accountHolder")]
    [InlineData("VCB", "0123456789", "A", "{room} {name}", "transferNoteTemplate")]
    [InlineData("VCB", "0123456789", "A", "{room", "transferNoteTemplate")]
    public async Task Put_InvalidInput_ReturnsFieldError(string bank, string account, string holder, string? template, string field)
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await Put(s.LandlordClient, InfoUrl(s.PropertyId),
            new { bankName = bank, accountNumber = account, accountHolder = holder, transferNoteTemplate = template });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(field, (await res.JsonAsync()).GetProperty("details")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Put_TemplateTooLong_ReturnsFieldError()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await Put(s.LandlordClient, InfoUrl(s.PropertyId),
            new { bankName = "VCB", accountNumber = "0123456789", accountHolder = "A", transferNoteTemplate = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Endpoints_OtherLandlordGets404_TenantGets403()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var other = factory.ClientFor(TestUser.Landlord($"other-{Guid.NewGuid():N}@example.com"));
        var tenant = factory.ClientFor(TestUser.Tenant($"t-{Guid.NewGuid():N}@example.com"));
        var url = InfoUrl(s.PropertyId);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(other, url, ValidInfo)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(url + "/qr:confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(url + "/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchJsonAsync($"/api/v1/properties/{s.PropertyId}", new { serviceFee = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(tenant, url, ValidInfo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.PostAsync(url + "/qr:confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.DeleteAsync(url + "/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.PatchJsonAsync($"/api/v1/properties/{s.PropertyId}", new { serviceFee = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId))).StatusCode);
    }

    [Theory]
    [InlineData("application/pdf", 1000)]
    [InlineData("image/gif", 1000)]
    [InlineData("image/png", 2L * 1024 * 1024 + 1)]
    public async Task QrUpload_InvalidFile_ReturnsInvalidFile(string contentType, long size)
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var res = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId, contentType, size));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_FILE", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task QrFlow_UploadConfirmReplaceDelete()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var url = InfoUrl(s.PropertyId);

        var first = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId));
        var firstData = await first.DataAsync();
        var uploadUrl = firstData.GetProperty("uploadUrl").GetString()!;
        Assert.EndsWith($"/{s.PropertyId}/{s.PropertyId}/{firstData.GetProperty("fileId").GetGuid()}.png", uploadUrl);
        Assert.Equal("PENDING_UPLOAD", (await (await s.LandlordClient.GetAsync(url)).DataAsync()).GetProperty("qrStatus").GetString());

        // Chưa PUT file → 409 QR_NOT_UPLOADED
        var missing = await s.LandlordClient.PostAsync(url + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal("QR_NOT_UPLOADED", (await missing.JsonAsync()).GetProperty("errorCode").GetString());

        var incomingKey = StripHost(uploadUrl);
        factory.ObjectStore.Objects[(StorageBucket.Incoming, incomingKey)] = new byte[1500];
        var confirmed = await s.LandlordClient.PostAsync(url + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var data = await confirmed.DataAsync();
        Assert.Equal("RECEIVED", data.GetProperty("qrStatus").GetString());
        var qrUrl = data.GetProperty("qrUrl").GetString()!;
        Assert.Contains("derived.example/qr/", qrUrl);
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, incomingKey)));   // incoming tự xóa sau 7 ngày nên QR sống ở derived
        var liveKey = StripHost(qrUrl);
        Assert.True(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Derived, liveKey)));

        // Xác nhận lần nữa khi không có yêu cầu tải mới: idempotent, trả thông tin hiện tại
        var again = await s.LandlordClient.PostAsync(url + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("RECEIVED", (await again.DataAsync()).GetProperty("qrStatus").GetString());

        // Thay QR: bản cũ vẫn dùng được cho tới khi xác nhận bản mới
        var (_, secondKey) = await UploadQrAsync(s);
        var during = await (await s.LandlordClient.GetAsync(url)).DataAsync();
        Assert.Equal("RECEIVED", during.GetProperty("qrStatus").GetString());
        Assert.Equal(qrUrl, during.GetProperty("qrUrl").GetString());
        var replaced = await (await s.LandlordClient.PostAsync(url + "/qr:confirm", null)).DataAsync();
        Assert.NotEqual(qrUrl, replaced.GetProperty("qrUrl").GetString());
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Derived, liveKey)));
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, secondKey)));

        // Xóa QR
        var newKey = StripHost(replaced.GetProperty("qrUrl").GetString()!);
        var deleted = await s.LandlordClient.DeleteAsync(url + "/qr");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var gone = await deleted.DataAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gone.GetProperty("qrUrl").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gone.GetProperty("qrStatus").ValueKind);
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Derived, newKey)));
    }

    [Fact]
    public void QrLink_LivesOneHour() => Assert.Equal(TimeSpan.FromHours(1), PaymentInfoAppService.QrLifetime);

    [Fact]
    public async Task QrConfirm_ObjectVanishesBeforeCopy_ReturnsQrNotUploaded()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var (_, key) = await UploadQrAsync(s);
        factory.ObjectStore.BeforeCopy = () =>
        {
            factory.ObjectStore.Objects.TryRemove((StorageBucket.Incoming, key), out _);
            return Task.CompletedTask;
        };
        try
        {
            var res = await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal("QR_NOT_UPLOADED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
        }
        finally
        {
            factory.ObjectStore.BeforeCopy = null;
        }
    }

    [Fact]
    public async Task QrConfirm_NewUploadRequestDuringConfirm_KeepsTheNewPendingRequest()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var (_, first) = await UploadQrAsync(s);
        string? second = null;
        factory.ObjectStore.BeforeCopy = async () =>
        {
            factory.ObjectStore.BeforeCopy = null;   // chỉ chen một lần
            var up = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", QrBody(s.PropertyId));
            second = StripHost((await up.DataAsync()).GetProperty("uploadUrl").GetString()!);
            // Xin link mới xóa file chờ cũ; đặt lại để lần chép (đã đọc key cũ) vẫn thành công và chỉ còn xmin chặn
            factory.ObjectStore.Objects[(StorageBucket.Incoming, first)] = new byte[1500];
        };
        try
        {
            var res = await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal("VERSION_CONFLICT", (await res.JsonAsync()).GetProperty("errorCode").GetString());
        }
        finally
        {
            factory.ObjectStore.BeforeCopy = null;
        }

        // Yêu cầu mới vẫn còn chờ, không có QR nào được gắn từ lần xác nhận thua cuộc
        var info = await (await s.LandlordClient.GetAsync(InfoUrl(s.PropertyId))).DataAsync();
        Assert.Equal("PENDING_UPLOAD", info.GetProperty("qrStatus").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, info.GetProperty("qrUrl").ValueKind);
        Assert.DoesNotContain(factory.ObjectStore.Objects.Keys, k => k.Bucket == StorageBucket.Derived && k.Key.Contains($"/{s.PropertyId}/"));

        // Tải file thứ hai rồi xác nhận: thành công
        factory.ObjectStore.Objects[(StorageBucket.Incoming, second!)] = new byte[1500];
        var ok = await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.EndsWith(Path.GetFileName(second!), (await ok.DataAsync()).GetProperty("qrUrl").GetString());
    }

    [Fact]
    public async Task DeletePaymentInfo_ClearsBankDetailsAndQr_InvoiceLosesPaymentBlock()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Water, 56);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        var invoiceId = gen.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();
        await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/send", null);
        await Put(s.LandlordClient, InfoUrl(s.PropertyId), new { bankName = "VCB", accountNumber = "0123456789", accountHolder = "A", transferNoteTemplate = "{room}" });
        await UploadQrAsync(s);
        var withQr = await (await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null)).DataAsync();
        var liveKey = StripHost(withQr.GetProperty("qrUrl").GetString()!);
        var (_, pendingKey) = await UploadQrAsync(s);

        var res = await s.LandlordClient.DeleteAsync(InfoUrl(s.PropertyId));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.DataAsync();
        foreach (var field in new[] { "bankName", "accountNumber", "accountHolder", "qrUrl", "qrStatus" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty(field).ValueKind);
        }

        Assert.Equal("{room} T{month}", data.GetProperty("transferNoteTemplate").GetString());
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Derived, liveKey)));
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, pendingKey)));

        var detail = await (await s.TenantClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, detail.GetProperty("payment").ValueKind);
    }

    [Fact]
    public async Task QrConfirm_OversizeObject_IsRejectedAndRemoved()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var (_, key) = await UploadQrAsync(s, bytes: 1000);
        factory.ObjectStore.Objects[(StorageBucket.Incoming, key)] = new byte[2 * 1024 * 1024 + 1];
        var res = await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_FILE", (await res.JsonAsync()).GetProperty("errorCode").GetString());
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, key)));
    }

    [Fact]
    public async Task QrUpload_NewRequestRemovesPreviousPendingObject()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var (_, first) = await UploadQrAsync(s);
        var (_, second) = await UploadQrAsync(s);
        Assert.False(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, first)));
        Assert.True(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, second)));
    }

    [Fact]
    public async Task InvoiceDetail_PaymentBlock_FilledForTenantAndNullWhenMissing()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Water, 56);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        var invoiceId = gen.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();
        await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/send", null);

        var before = await (await s.TenantClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("payment").ValueKind);

        await Put(s.LandlordClient, InfoUrl(s.PropertyId), new
        {
            bankName = "Vietcombank", accountNumber = "0123456789", accountHolder = "NGUYEN VAN A",
            transferNoteTemplate = "{room} {period} T{month}/{year}",
        });
        await UploadQrAsync(s);
        var confirmed = await s.LandlordClient.PostAsync(InfoUrl(s.PropertyId) + "/qr:confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var liveKey = StripHost((await confirmed.DataAsync()).GetProperty("qrUrl").GetString()!);
        Assert.True(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Derived, liveKey)));

        foreach (var client in new[] { s.TenantClient, s.LandlordClient })
        {
            var payment = (await (await client.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync()).GetProperty("payment");
            Assert.Equal("Vietcombank", payment.GetProperty("bankName").GetString());
            Assert.Equal("0123456789", payment.GetProperty("accountNumber").GetString());
            Assert.Equal("NGUYEN VAN A", payment.GetProperty("accountHolder").GetString());
            Assert.Equal("P204 2026-11 T11/2026", payment.GetProperty("transferNote").GetString());   // P.204 → P204
            Assert.Contains("derived.example/qr/", payment.GetProperty("qrUrl").GetString());
        }

        // Mẫu mặc định khi chưa đặt
        await Put(s.LandlordClient, InfoUrl(s.PropertyId), ValidInfo);
        var defaulted = (await (await s.TenantClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync()).GetProperty("payment");
        Assert.Equal("P204 T11", defaulted.GetProperty("transferNote").GetString());
    }

    [Fact]
    public async Task PatchProperty_ValidatesAndUpdatesRates()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var url = $"/api/v1/properties/{s.PropertyId}";

        var empty = await s.LandlordClient.PatchJsonAsync(url, new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("body", (await empty.JsonAsync()).GetProperty("details")[0].GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PatchJsonAsync(url, new { electricityRate = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PatchJsonAsync(url, new { waterRate = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PatchJsonAsync(url, new { serviceFee = -1 })).StatusCode);

        var res = await s.LandlordClient.PatchJsonAsync(url, new { electricityRate = 4000, serviceFee = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.DataAsync();
        Assert.Equal(4000, data.GetProperty("electricityRate").GetInt64());
        Assert.Equal(20000, data.GetProperty("waterRate").GetInt64());   // không gửi thì giữ nguyên
        Assert.Equal(0, data.GetProperty("serviceFee").GetInt64());
        Assert.Equal(1, data.GetProperty("roomCount").GetInt32());

        var list = await (await s.LandlordClient.GetAsync("/api/v1/properties")).DataAsync();
        Assert.Equal(4000, list.GetProperty("items")[0].GetProperty("electricityRate").GetInt64());
    }

    [Fact]
    public async Task PatchProperty_AppliesToLaterInvoicesOnly_ContractRatesStillWin()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-09", MeterKind.Electricity, 900);
        await s.ConfirmedReadingAsync("2026-09", MeterKind.Water, 40);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Water, 56);

        var october = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-10" })).DataAsync();
        var octoberId = october.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();

        var patch = await s.LandlordClient.PatchJsonAsync($"/api/v1/properties/{s.PropertyId}",
            new { electricityRate = 9999, waterRate = 99999, serviceFee = 250000 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var november = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        var novemberId = november.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();

        var oldInvoice = await (await s.LandlordClient.GetAsync($"/api/v1/invoices/{octoberId}")).DataAsync();
        Assert.Equal(100000, oldInvoice.GetProperty("serviceFee").GetInt64());   // hóa đơn đã lập không đổi
        var newInvoice = await (await s.LandlordClient.GetAsync($"/api/v1/invoices/{novemberId}")).DataAsync();
        Assert.Equal(250000, newInvoice.GetProperty("serviceFee").GetInt64());   // phí dịch vụ lấy từ nhà trọ lúc lập
        Assert.Equal(3500, newInvoice.GetProperty("electricity").GetProperty("rate").GetInt64());   // giá đã khóa trong hợp đồng thắng giá nhà trọ
        Assert.Equal(20000, newInvoice.GetProperty("water").GetProperty("rate").GetInt64());
        // 3.500.000 + 120 × 3.500 + 6 × 20.000 + 250.000
        Assert.Equal(4_290_000, newInvoice.GetProperty("total").GetInt64());
    }
}
