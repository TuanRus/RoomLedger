using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Ai;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.MeterResult;

/**
 * Hai bước trong VPC của state machine đọc đồng hồ (read-document chạy ngoài VPC nên không chạm được RDS):
 * PrepareAsync: ảnh vừa tải lên bucket incoming có phải ảnh đồng hồ đang chờ AI không; PersistAsync: ghi kết quả AI
 * vào meter_reading theo luật BR-20 (MeterReadingService.Assess) và báo chủ nhà khi cần kiểm tra.
 *
 * Cả hai idempotent: state machine hay EventBridge giao lại cùng sự kiện không ghi đè chỉ số đã được chủ nhà
 * nhập hay xác nhận, và không ghi kết quả của một ảnh cũ đè lên ảnh mới hơn.
 *
 * @author Tuấn
 * @version 1.0
 */
public class Function
{
    public const string NeedsReviewEvent = "MeterReadingNeedsReview";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<AppDbContext> _db;
    private readonly IEventPublisher _events;

    public Function() : this(FunctionDb.Create, FunctionAws.Events)
    {
    }

    public Function(Func<AppDbContext> db, IEventPublisher events)
    {
        _db = db;
        _events = events;
    }

    /**
     * Sự kiện "Object Created" của bucket incoming. Chỉ ảnh có meter_reading đang PENDING_AI trỏ tới đúng key
     * này mới được đưa tiếp cho AI; ảnh của biên bản, hợp đồng, mã QR... được bỏ qua (Found = false).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<MeterPrepareResult> PrepareAsync(CloudWatchEvent<S3ObjectCreatedDetail> input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        var key = System.Net.WebUtility.UrlDecode(input.Detail.Object.Key);
        await using var db = _db();
        var reading = await db.MeterReadings.FirstOrDefaultAsync(m => m.PhotoKey == key, ct);
        if (reading is null || reading.Status != MeterReadingStatus.PendingAi)
        {
            context.Logger.LogInformation($"Bỏ qua {key}: không phải ảnh đồng hồ đang chờ AI");
            return new MeterPrepareResult(false);
        }

        if (reading.PhotoStatus == FileStatus.PendingUpload)
        {
            reading.PhotoStatus = FileStatus.Ready;
            await db.SaveChangesAsync(ct);
        }

        return new MeterPrepareResult(true, reading.Id, key, AiTasks.For(reading.Kind));
    }

    /**
     * Ghi kết quả. Lỗi AI (Error) hay ảnh không đọc được đều thành NEEDS_REVIEW với lý do AI_FAILED, để chủ nhà
     * nhập tay: không bao giờ bỏ chỉ số ở PENDING_AI mãi.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task PersistAsync(MeterPersistInput input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        await using var db = _db();
        var reading = await db.MeterReadings.FirstOrDefaultAsync(m => m.Id == input.ReadingId, ct);
        if (reading is null || reading.PhotoKey != input.Key || reading.Status != MeterReadingStatus.PendingAi)
        {
            context.Logger.LogInformation($"Bỏ qua kết quả của chỉ số {input.ReadingId}: không còn chờ AI cho ảnh này");
            return;
        }

        var aiValue = input is { Readable: true, Error: null } ? input.Value : null;
        var (previous, usages) = await HistoryAsync(db, reading, ct);
        var assessment = MeterReadingService.Assess(aiValue, input.Confidence, previous, usages);

        reading.AiValue = aiValue;
        reading.AiConfidence = aiValue is null ? null : input.Confidence;
        reading.AiRawJson = JsonSerializer.Serialize(new { readable = aiValue is not null, value = aiValue, confidence = input.Confidence, note = input.Note, error = input.Error }, Json);
        reading.Status = assessment.Status;
        reading.ReviewReason = assessment.ReviewReason;
        await db.SaveChangesAsync(ct);
        context.Logger.LogInformation($"Chỉ số {reading.Id}: {assessment.Status} {assessment.ReviewReason}");

        if (assessment.Status == MeterReadingStatus.NeedsReview)
        {
            var landlordId = await db.Tenancies.AsNoTracking().Where(t => t.Id == reading.TenancyId).Select(t => t.LandlordId).SingleAsync(ct);
            await _events.PublishAsync(NeedsReviewEvent, new
            {
                readingId = reading.Id,
                tenancyId = reading.TenancyId,
                roomId = reading.RoomId,
                kind = reading.Kind.ToString().ToUpperInvariant(),
                period = reading.Period,
                reason = assessment.ReviewReason,
                notifyUserId = landlordId,
            }, ct);
        }
    }

    /**
     * Chỉ số kỳ trước và mức dùng của tối đa 3 kỳ gần nhất, cùng cách chọn với API (MeterAppService): chỉ số đã
     * xác nhận gần nhất của hợp đồng này; chưa có thì chỉ số ban đầu ghi lúc nhận phòng; hợp đồng không có chỉ số
     * ban đầu thì lấy chỉ số cuối của phòng. Mức dùng bỏ qua cặp giảm (thay đồng hồ).
     *
     * @author Tuấn
     * @version 1.0
     */
    private static async Task<(long? Previous, IReadOnlyList<long> Usages)> HistoryAsync(AppDbContext db, MeterReading reading, CancellationToken ct)
    {
        var tenancy = await db.Tenancies.AsNoTracking().SingleAsync(t => t.Id == reading.TenancyId, ct);
        var confirmed = await db.MeterReadings.AsNoTracking()
            .Where(m => m.RoomId == reading.RoomId && m.Kind == reading.Kind && m.Status == MeterReadingStatus.Confirmed
                        && m.Value != null && string.Compare(m.Period, reading.Period) < 0)
            .OrderByDescending(m => m.Period).ThenByDescending(m => m.CreatedAt)
            .Select(m => new { m.TenancyId, m.Value })
            .Take(4)
            .ToListAsync(ct);

        var opening = reading.Kind == MeterKind.Electricity ? tenancy.OpeningElectricity : tenancy.OpeningWater;
        var own = confirmed.FirstOrDefault(m => m.TenancyId == tenancy.Id)?.Value;
        var previous = own ?? (opening is null ? confirmed.FirstOrDefault()?.Value : opening);

        var values = confirmed.Select(m => m.Value!.Value).ToList();   // mới → cũ
        var usages = new List<long>();
        for (var i = 0; i + 1 < values.Count; i++)
        {
            var usage = values[i] - values[i + 1];
            if (usage >= 0)
            {
                usages.Add(usage);
            }
        }

        return (previous, usages);
    }
}
