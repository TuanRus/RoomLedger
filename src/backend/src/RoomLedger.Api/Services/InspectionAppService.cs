using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Core.StateMachines;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Biên bản nhận phòng phía API (MD-04). Chủ nhà hoặc người thuê của hợp đồng mới thấy biên bản
 * (global query filter); người khác nhận 404.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionAppService(
    AppDbContext db,
    ICurrentUser user,
    IObjectStore objects,
    IStorage storage,
    TimeProvider clock,
    IEventPublisher events,
    ILogger<InspectionAppService> log)
{
    private const string OneMoveInIndex = "ux_inspection_tenancy_id_type";
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    /**
     * Xác nhận biên bản (MD-04-FE-04, BR-18 RULE 1–3). Trong một transaction:
     * 1. UPDATE có điều kiện (OPEN hoặc AWAITING_CONFIRM) ghi xác nhận của bên gọi → giữ khóa dòng inspection,
     *    nên xóa ảnh (cũng UPDATE dòng này trước) không chen vào giữa lúc kiểm ảnh và lúc khóa được;
     * 2. kiểm đủ ảnh READY mỗi khu vực theo bản sao (thiếu → rollback, 409 INCOMPLETE_INSPECTION);
     * 3. UPDATE … SET LOCKED WHERE status = AWAITING_CONFIRM và đủ hai xác nhận: đúng một request khóa được.
     * Sau commit: phát InspectionLocked (evidence-seal) hoặc InspectionConfirmed (notify báo bên còn lại).
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<InspectionResponse> ConfirmAsync(Guid inspectionId, ConfirmInspectionRequest req, CancellationToken ct)
    {
        new Validator().Require(req.Agreed == true, "agreed", "Cần tích ô cam kết trước khi xác nhận").ThrowIfInvalid();
        var inspection = await db.Inspections.AsNoTracking().Include(i => i.Tenancy)
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
            ?? throw new NotFoundException("Không tìm thấy biên bản");
        InspectionStateMachine.EnsureCanConfirm(inspection.Status);
        var tenancy = inspection.Tenancy!;
        var isLandlord = user.UserId == tenancy.LandlordId;
        if (!isLandlord && user.UserId != tenancy.TenantId)
        {
            throw new ForbiddenException("Chỉ chủ nhà hoặc người thuê của hợp đồng mới xác nhận được");
        }

        var now = clock.GetUtcNow();
        bool locked;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var pending = db.Inspections.Where(i => i.Id == inspectionId
                && (i.Status == InspectionStatus.Open || i.Status == InspectionStatus.AwaitingConfirm));
            var rows = isLandlord
                ? await pending.ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.LandlordConfirmedAt, i => i.LandlordConfirmedAt ?? now)
                    .SetProperty(i => i.Status, InspectionStatus.AwaitingConfirm)
                    .SetProperty(i => i.UpdatedAt, now), ct)
                : await pending.ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.TenantConfirmedAt, i => i.TenantConfirmedAt ?? now)
                    .SetProperty(i => i.Status, InspectionStatus.AwaitingConfirm)
                    .SetProperty(i => i.UpdatedAt, now), ct);
            if (rows == 0)
            {
                throw new RecordLockedException("Biên bản đã khóa");   // tx chưa commit → tự rollback khi dispose
            }

            var photos = await db.Photos.Where(p => p.InspectionId == inspectionId)
                .Select(p => new { p.RoomAreaId, p.Status }).ToListAsync(ct);
            var missing = InspectionService.FindIncompleteAreas(
                InspectionService.ParseSnapshot(inspection.AreaSnapshotJson), photos.Select(p => (p.RoomAreaId, p.Status)));
            if (missing.Count > 0)
            {
                throw new ConflictException(ErrorCodes.IncompleteInspection, "Biên bản chưa đủ ảnh: " + string.Join(", ", missing));
            }

            locked = await db.Inspections
                .Where(i => i.Id == inspectionId && i.Status == InspectionStatus.AwaitingConfirm
                    && i.LandlordConfirmedAt != null && i.TenantConfirmedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InspectionStatus.Locked).SetProperty(i => i.UpdatedAt, now), ct) == 1;

            var role = isLandlord ? "LANDLORD" : "TENANT";
            db.Audit(locked ? AuditEventTypes.InspectionLocked : AuditEventTypes.InspectionConfirmed, nameof(Inspection), inspectionId,
                $$"""{"confirmedBy":"{{role}}"}""");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        if (locked)
        {
            await events.PublishAsync("InspectionLocked", new { inspectionId, tenancyId = tenancy.Id, type = inspection.Type }, ct);
        }
        else
        {
            await events.PublishAsync("InspectionConfirmed", new
            {
                inspectionId,
                tenancyId = tenancy.Id,
                confirmedBy = isLandlord ? "LANDLORD" : "TENANT",
                notifyUserId = isLandlord ? tenancy.TenantId : tenancy.LandlordId,
            }, ct);
        }

        return await GetAsync(inspectionId, ct);
    }

    /**
     * Xóa ảnh trước khi khóa (MD-04-FE-03, BR-17). Ảnh của biên bản người gọi không thấy → 404; không phải người gửi
     * → 403; biên bản đã khóa → 409 RECORD_LOCKED (K1). Hai trường hợp bị từ chối đều ghi PHOTO_DELETE_DENIED.
     * Xóa dùng UPDATE có điều kiện trạng thái nên không chen được vào sau lúc khóa; đang chờ xác nhận thì xác nhận
     * bị hủy và biên bản mở lại (ảnh đã đổi nên xác nhận cũ không còn đúng). Object S3 xóa sau khi commit.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<InspectionResponse> DeletePhotoAsync(Guid photoId, CancellationToken ct)
    {
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId, ct)
            ?? throw new NotFoundException("Không tìm thấy ảnh");
        var inspection = await db.Inspections.AsNoTracking().FirstOrDefaultAsync(i => i.Id == photo.InspectionId, ct)
            ?? throw new NotFoundException("Không tìm thấy ảnh");

        if (photo.UploadedBy != user.UserId)
        {
            await DenyAsync(photo.Id, "NOT_UPLOADER", ct);
            throw new ForbiddenException("Chỉ người gửi ảnh mới xóa được ảnh");
        }
        if (inspection.Status is not (InspectionStatus.Open or InspectionStatus.AwaitingConfirm))
        {
            await DenyAsync(photo.Id, "LOCKED", ct);
            InspectionStateMachine.EnsureCanChangePhotos(inspection.Status);
        }

        var now = clock.GetUtcNow();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var stillOpen = await db.Inspections
                .Where(i => i.Id == inspection.Id && (i.Status == InspectionStatus.Open || i.Status == InspectionStatus.AwaitingConfirm))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, InspectionStatus.Open)
                    .SetProperty(i => i.LandlordConfirmedAt, (DateTimeOffset?)null)
                    .SetProperty(i => i.TenantConfirmedAt, (DateTimeOffset?)null)
                    .SetProperty(i => i.UpdatedAt, now), ct);
            if (stillOpen == 0)
            {
                await tx.RollbackAsync(ct);
                await DenyAsync(photo.Id, "LOCKED", ct);   // vừa bị khóa giữa lúc kiểm tra và lúc xóa
                throw new RecordLockedException("Biên bản đã khóa, không xóa được ảnh");
            }

            db.Photos.Remove(photo);
            db.Audit(AuditEventTypes.PhotoDeleted, nameof(Photo), photo.Id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await DeleteObjectsAsync(photo.IncomingKey, ct);
        return await GetAsync(inspection.Id, ct);
    }

    private async Task DenyAsync(Guid photoId, string reason, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.Audit(AuditEventTypes.PhotoDeleteDenied, nameof(Photo), photoId, $$"""{"reason":"{{reason}}"}""");
        await db.SaveChangesAsync(ct);
    }

    /** Ảnh gốc (incoming) và bản xem, thumbnail (derived). Lỗi S3 chỉ ghi log: incoming tự xóa sau 7 ngày. */
    private async Task DeleteObjectsAsync(string incomingKey, CancellationToken ct)
    {
        try
        {
            await objects.DeleteAsync(StorageBucket.Incoming, incomingKey, ct);
            await objects.DeleteAsync(StorageBucket.Derived, PhotoRules.ViewKey(incomingKey), ct);
            await objects.DeleteAsync(StorageBucket.Derived, PhotoRules.ThumbnailKey(incomingKey), ct);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Đã xóa bản ghi ảnh nhưng xóa object S3 lỗi: {Key}", incomingKey);
        }
    }

    /**
     * Mở biên bản nhận phòng (MD-04-FE-01, BR-15). Đã có thì trả biên bản đó (created = false).
     * Hai bên mở cùng lúc: unique index (tenancy_id, type) chặn bản thứ hai, request thua trả biên bản của bên thắng.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<(InspectionResponse Inspection, bool Created)> OpenMoveInAsync(Guid tenancyId, CancellationToken ct)
    {
        var tenancy = await db.Tenancies.FirstOrDefaultAsync(t => t.Id == tenancyId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
        InspectionStateMachine.EnsureCanOpenMoveIn(tenancy);

        if (await FindMoveInIdAsync(tenancyId, ct) is { } existingId)
        {
            return (await GetAsync(existingId, ct), false);
        }

        var areas = await db.RoomAreas.Where(a => a.RoomId == tenancy.RoomId).ToListAsync(ct);
        var inspection = new Inspection
        {
            TenancyId = tenancy.Id,
            Type = InspectionType.MoveIn,
            Status = InspectionStatus.Open,
            AreaSnapshotJson = InspectionService.SerializeSnapshot(InspectionService.BuildAreaSnapshot(areas)),
        };
        db.Inspections.Add(inspection);
        db.Audit(AuditEventTypes.InspectionOpened, nameof(Inspection), inspection.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneMoveInIndex })
        {
            db.ChangeTracker.Clear();
            var winner = await FindMoveInIdAsync(tenancyId, ct);
            if (winner is null)
            {
                throw;
            }
            return (await GetAsync(winner.Value, ct), false);
        }

        return (await GetAsync(inspection.Id, ct), true);
    }

    /**
     * Xem biên bản: khu vực theo bản sao lúc mở, kèm số ảnh đã gửi mỗi khu vực.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<InspectionResponse> GetAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await db.Inspections.AsNoTracking().FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
            ?? throw new NotFoundException("Không tìm thấy biên bản");
        var counts = await db.Photos.Where(p => p.InspectionId == inspection.Id)
            .GroupBy(p => p.RoomAreaId)
            .Select(g => new { AreaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AreaId, x => x.Count, ct);
        var areas = InspectionService.ParseSnapshot(inspection.AreaSnapshotJson)
            .Select(a => new InspectionAreaResponse(a.RoomAreaId, a.Name, a.MinPhotos, a.SortOrder, counts.GetValueOrDefault(a.RoomAreaId)))
            .ToList();
        return new InspectionResponse(inspection.Id, inspection.TenancyId, inspection.Type, inspection.Status,
            inspection.LandlordConfirmedAt, inspection.TenantConfirmedAt, areas);
    }

    /**
     * Danh sách ảnh của biên bản theo thứ tự gửi (màn hình biên bản MD-04, ảnh đối chiếu MD-06-FE-01).
     * Người không thuộc hợp đồng không thấy biên bản (global query filter) → 404. Ảnh PENDING_UPLOAD (chưa tải lên)
     * bị bỏ; ảnh chưa có bản xem trả URL null. Bản xem và thumbnail lấy từ bucket derived, kể cả khi đã niêm phong.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<IReadOnlyList<InspectionPhotoResponse>> ListPhotosAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await db.Inspections.AsNoTracking().FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
            ?? throw new NotFoundException("Không tìm thấy biên bản");
        var canChange = inspection.Status is InspectionStatus.Open or InspectionStatus.AwaitingConfirm;
        var photos = await db.Photos.AsNoTracking()
            .Where(p => p.InspectionId == inspectionId && p.Status != FileStatus.PendingUpload)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);
        return photos.Select(p =>
        {
            var mine = p.UploadedBy == user.UserId;
            return new InspectionPhotoResponse(p.Id, p.RoomAreaId, p.FileName, p.Status, p.UploaderRole, mine,
                p.CaptureSource, p.ExifTakenAt, p.OldPhotoWarning, p.CreatedAt,
                p.DerivedKey is null ? null : storage.CreateDownloadUrl(StorageBucket.Derived, p.DerivedKey, DownloadLifetime),
                p.DerivedKey is null ? null
                    : storage.CreateDownloadUrl(StorageBucket.Derived, PhotoRules.ThumbnailKey(p.IncomingKey), DownloadLifetime),
                mine && canChange);
        }).ToList();
    }

    private async Task<Guid?> FindMoveInIdAsync(Guid tenancyId, CancellationToken ct) =>
        await db.Inspections.Where(i => i.TenancyId == tenancyId && i.Type == InspectionType.MoveIn)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
}
