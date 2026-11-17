using System.Text;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.TestUtilities;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using UglyToad.PdfPig;
using Render = RoomLedger.Functions.RenderPdf.Function;

namespace RoomLedger.Functions.Tests.Settlement;

/**
 * Lambda render-pdf (MD-06-FE-05, BR-29): nghe TenancySettled, đọc quyết toán đã chốt, dựng biên bản thanh lý PDF, ghi
 * vào bucket derived ở reports/{tenancyId}/thanh-ly-{settlementId}.pdf và điền settlement.pdf_key (API trả link tải).
 * Chỉ in các khoản đã trừ (ACCEPTED, RESOLVED) và hóa đơn chưa PAID. Chạy lại (EventBridge gửi trùng) không dựng lại.
 *
 * @author Sang
 * @version 1.0
 */
public class RenderPdfTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private readonly FakeObjectStore _store = new();
    private Render NewRender() => new(db.NewDb, _store);

    private sealed record Seed(Guid TenancyId, Guid SettlementId);

    private static CloudWatchEvent<TenancySettledDetail> Settled(Guid tenancyId, Guid settlementId) =>
        new() { DetailType = "TenancySettled", Source = "roomledger.api", Detail = new TenancySettledDetail(tenancyId, settlementId) };

    /**
     * Hợp đồng đã quyết toán: cọc 7.000.000; trừ "Vết bẩn trên tường" 500.000 (ACCEPTED, Phòng ngủ) và "Vỡ kính tủ bếp"
     * 300.000 (RESOLVED, Bếp); mục DISMISSED "Trầy sàn nhẹ" không in; hóa đơn 2026-10 PAID không in, 2026-11 SENT 4.200.000.
     */
    private async Task<Seed> SeedAsync(bool withSettlement = true)
    {
        await using var ctx = db.NewDb();
        var landlord = new AppUser { Email = $"chu-{Guid.NewGuid():N}@example.com", FullName = "Nguyễn Văn Chủ", Role = UserRole.Landlord };
        var tenant = new AppUser { Email = $"thue-{Guid.NewGuid():N}@example.com", FullName = "Trần Thị Thuê", Role = UserRole.Tenant };
        var property = new Property { LandlordId = landlord.Id, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi, Quận 1" };
        var room = new Room { Property = property, LandlordId = landlord.Id, Code = "P.204", MonthlyRent = 3_500_000 };
        var bedroom = new RoomArea { RoomId = room.Id, Name = "Phòng ngủ", MinPhotos = 1, SortOrder = 0 };
        var kitchen = new RoomArea { RoomId = room.Id, Name = "Bếp", MinPhotos = 1, SortOrder = 1 };
        room.Areas.AddRange([bedroom, kitchen]);
        var tenancy = new Tenancy { Room = room, LandlordId = landlord.Id, TenantId = tenant.Id, Status = TenancyStatus.Settled };
        var moveOut = new Inspection
        {
            Tenancy = tenancy, Type = InspectionType.MoveOut, Status = InspectionStatus.Settled, FinalPeriod = "2026-11",
            AreaSnapshotJson = InspectionService.SerializeSnapshot(InspectionService.BuildAreaSnapshot(room.Areas)),
        };
        ctx.AppUsers.AddRange(landlord, tenant);
        ctx.Inspections.Add(moveOut);
        ctx.Findings.AddRange(
            new Finding { Inspection = moveOut, RoomAreaId = bedroom.Id, Title = "Vết bẩn trên tường", Status = FindingStatus.Accepted, Amount = 500_000 },
            new Finding { Inspection = moveOut, RoomAreaId = kitchen.Id, Title = "Vỡ kính tủ bếp", Status = FindingStatus.Resolved, Amount = 300_000 },
            new Finding { Inspection = moveOut, RoomAreaId = kitchen.Id, Title = "Trầy sàn nhẹ", Status = FindingStatus.Dismissed });
        foreach (var (period, status, total) in new[] { ("2026-10", InvoiceStatus.Paid, 3_900_000L), ("2026-11", InvoiceStatus.Sent, 4_200_000L) })
        {
            var e = new MeterReading { RoomId = room.Id, Tenancy = tenancy, Period = period, Kind = MeterKind.Electricity, Status = MeterReadingStatus.Confirmed, Value = 100 };
            var w = new MeterReading { RoomId = room.Id, Tenancy = tenancy, Period = period, Kind = MeterKind.Water, Status = MeterReadingStatus.Confirmed, Value = 10 };
            ctx.MeterReadings.AddRange(e, w);
            ctx.Invoices.Add(new Invoice { Tenancy = tenancy, Period = period, Status = status, RoomRent = 3_500_000, ElectricityReadingId = e.Id, WaterReadingId = w.Id, Total = total });
        }
        var settlement = new Core.Domain.Settlement
        {
            Tenancy = tenancy, InspectionId = moveOut.Id, Deposit = 7_000_000, DeductionsTotal = 800_000, UnpaidInvoicesTotal = 4_200_000,
            Refund = 2_000_000, SettledAt = new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero), SettledBy = landlord.Id,
        };
        if (withSettlement)
        {
            ctx.Settlements.Add(settlement);
        }
        else
        {
            ctx.Tenancies.Add(tenancy);
        }
        await ctx.SaveChangesAsync();
        return new Seed(tenancy.Id, settlement.Id);
    }

    private static string Key(Seed seed) => $"reports/{seed.TenancyId}/thanh-ly-{seed.SettlementId}.pdf";

    private static string Squash(string s) => new string(s.Normalize(NormalizationForm.FormC).Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string TextOf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return Squash(string.Concat(doc.GetPages().Select(p => p.Text)));
    }

    [Fact]
    public async Task Settled_WritesThePdfToDerivedReports_AndRecordsTheKey()
    {
        var seed = await SeedAsync();

        await NewRender().HandleAsync(Settled(seed.TenancyId, seed.SettlementId), new TestLambdaContext());

        Assert.True(_store.Objects.TryGetValue((StorageBucket.Derived, Key(seed)), out var pdf));
        Assert.Equal("application/pdf", pdf.ContentType);
        await using var ctx = db.NewDb();
        Assert.Equal(Key(seed), await ctx.Settlements.Where(s => s.Id == seed.SettlementId).Select(s => s.PdfKey).SingleAsync());
        Assert.Equal(1, await ctx.AuditEvents.CountAsync(a => a.TargetId == seed.SettlementId && a.EventType == "SETTLEMENT_PDF_CREATED"));
    }

    [Fact]
    public async Task ThePdf_ShowsThePartiesRoomDeductedItemsAndUnpaidInvoices_FromTheDatabase()
    {
        var seed = await SeedAsync();

        await NewRender().HandleAsync(Settled(seed.TenancyId, seed.SettlementId), new TestLambdaContext());

        var text = TextOf(_store.Objects[(StorageBucket.Derived, Key(seed))].Content);
        foreach (var expected in new[]
                 {
                     "Nhà trọ Hoa Sen", "12 Lê Lợi, Quận 1", "P.204", "Nguyễn Văn Chủ", "Trần Thị Thuê", "2026-11", "20/11/2026 10:00",
                     "Phòng ngủ", "Vết bẩn trên tường", "500.000 đ", "Bếp", "Vỡ kính tủ bếp", "300.000 đ", "4.200.000 đ",
                     "Tiền trả lại người thuê", "2.000.000 đ",
                 })
        {
            Assert.True(text.Contains(Squash(expected)), $"PDF thiếu \"{expected}\"");
        }
        Assert.DoesNotContain(Squash("Trầy sàn nhẹ"), text);   // mục đã bỏ qua không bị trừ
        Assert.DoesNotContain("3.900.000", text);               // hóa đơn đã trả
    }

    [Fact]
    public async Task DeliveredTwice_RendersOnce()
    {
        var seed = await SeedAsync();
        await NewRender().HandleAsync(Settled(seed.TenancyId, seed.SettlementId), new TestLambdaContext());
        _store.Objects.Clear();

        await NewRender().HandleAsync(Settled(seed.TenancyId, seed.SettlementId), new TestLambdaContext());

        Assert.Empty(_store.Objects);
        await using var ctx = db.NewDb();
        Assert.Equal(1, await ctx.AuditEvents.CountAsync(a => a.TargetId == seed.SettlementId && a.EventType == "SETTLEMENT_PDF_CREATED"));
    }

    [Fact]
    public async Task NoSettlementForTheEvent_DoesNothing()
    {
        var seed = await SeedAsync(withSettlement: false);

        await NewRender().HandleAsync(Settled(seed.TenancyId, Guid.NewGuid()), new TestLambdaContext());

        Assert.Empty(_store.Objects);
    }
}
