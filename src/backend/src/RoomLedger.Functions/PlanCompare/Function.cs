using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Ai;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PlanCompare;

/**
 * Bước đầu Step Functions so ảnh trả phòng (MD-06-FE-02, BR-26 RULE 1–2, EXCEPTION 1). Chạy trong VPC (đọc RDS).
 * Lấy ảnh READY/SEALED của biên bản trả phòng và biên bản nhận phòng cùng hợp đồng, tính pHash trên thumbnail 400 px
 * trong bucket derived (S3 qua Gateway endpoint), ghép cặp bằng ComparePlanner với ngưỡng và giới hạn gọi Step Functions
 * đọc từ SSM. Trả mọi cặp (để persist-results ghi SKIPPED / NOT_ANALYZED) và danh sách cặp cần gọi Bedrock kèm khóa
 * bản xem JPEG. Không đọc được thumbnail → hash null → vẫn gửi so sánh. Biên bản không còn COMPARING → Proceed = false.
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private readonly Func<AppDbContext> _db;
    private readonly IObjectStore _store;

    public Function() : this(FunctionDb.Create, FunctionAws.ObjectStore)
    {
    }

    public Function(Func<AppDbContext> db, IObjectStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<ComparePlan> HandleAsync(CompareStart input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        await using var db = _db();
        var moveOut = await db.Inspections.AsNoTracking().SingleOrDefaultAsync(i => i.Id == input.InspectionId, ct);
        if (moveOut is null || moveOut.Type != InspectionType.MoveOut || moveOut.Status != InspectionStatus.Comparing)
        {
            context.Logger.LogInformation($"Bỏ qua biên bản {input.InspectionId}: {moveOut?.Status.ToString() ?? "không tồn tại"}");
            return new ComparePlan(false, input.InspectionId, moveOut?.TenancyId ?? Guid.Empty, [], []);
        }

        // Chỉ nhận biên bản nhận phòng của chính hợp đồng này (sự kiện có thể bị sửa tay khi chạy lại)
        var moveInId = await db.Inspections.AsNoTracking()
            .Where(i => i.Id == input.MoveInInspectionId && i.TenancyId == moveOut.TenancyId && i.Type == InspectionType.MoveIn)
            .Select(i => (Guid?)i.Id).SingleOrDefaultAsync(ct);
        var ids = moveInId is { } id ? new[] { moveOut.Id, id } : new[] { moveOut.Id };
        var photos = await db.Photos.AsNoTracking()
            .Where(p => ids.Contains(p.InspectionId) && (p.Status == FileStatus.Ready || p.Status == FileStatus.Sealed))
            .ToListAsync(ct);

        var planned = new List<(Photo Photo, PlannedPhoto Planned)>();
        foreach (var photo in photos)
        {
            planned.Add((photo, new PlannedPhoto(photo.Id, photo.RoomAreaId, await HashAsync(photo, context, ct), photo.CreatedAt)));
        }

        var areas = InspectionService.ParseSnapshot(moveOut.AreaSnapshotJson);
        var pairs = ComparePlanner.Plan(areas,
            planned.Where(p => p.Photo.InspectionId != moveOut.Id).Select(p => p.Planned).ToList(),
            planned.Where(p => p.Photo.InspectionId == moveOut.Id).Select(p => p.Planned).ToList(),
            input.Threshold, input.MaxCalls);

        var byId = photos.ToDictionary(p => p.Id);
        var names = areas.ToDictionary(a => a.RoomAreaId, a => a.Name);
        var tasks = pairs.Where(p => p.Decision == PairDecision.Compare)
            .Select(p => new CompareTask(p.MoveOutPhotoId, p.MoveInPhotoId!.Value, names.GetValueOrDefault(p.AreaId, ""),
                ViewKey(byId[p.MoveInPhotoId.Value]), ViewKey(byId[p.MoveOutPhotoId])))
            .ToList();
        context.Logger.LogInformation($"Biên bản {moveOut.Id}: {pairs.Count} ảnh trả phòng, {tasks.Count} cặp gửi Bedrock, " +
                                      $"{pairs.Count(p => p.Decision == PairDecision.Skipped)} bỏ qua");
        return new ComparePlan(true, moveOut.Id, moveOut.TenancyId, pairs, tasks);
    }

    private static string ViewKey(Photo photo) => photo.DerivedKey ?? PhotoRules.ViewKey(photo.IncomingKey);

    private async Task<ulong?> HashAsync(Photo photo, ILambdaContext context, CancellationToken ct)
    {
        try
        {
            await using var thumbnail = await _store.OpenReadAsync(StorageBucket.Derived, PhotoRules.ThumbnailKey(photo.IncomingKey), ct);
            return PerceptualHash.Compute(thumbnail);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            context.Logger.LogWarning($"Không đọc được thumbnail ảnh {photo.Id}, gửi so sánh không lọc: {e.GetType().Name}");
            return null;
        }
    }
}
