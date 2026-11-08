using System.Security.Cryptography;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.EvidenceSeal;

/**
 * Sự kiện InspectionLocked (EventBridge, trong VPC): niêm phong ảnh biên bản (BR-18 RULE 4).
 * Với mỗi ảnh READY: kiểm SHA-256 ảnh gốc trong incoming → chép sang bucket evidence (Object Lock, ghi một lần)
 * cùng khóa → kiểm lại SHA-256 bản trong evidence → ghi EvidenceKey, ObjectVersionId, READY → SEALED.
 * Ảnh gốc sai hash không được chép (bucket Object Lock không xóa được), ghi EVIDENCE_HASH_MISMATCH và ném lỗi
 * để EventBridge thử lại rồi báo động. Chạy lại an toàn: ảnh đã SEALED được bỏ qua.
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

    public async Task HandleAsync(CloudWatchEvent<InspectionLockedDetail> input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        var inspectionId = input.Detail.InspectionId;
        await using var db = _db();
        var inspection = await db.Inspections.AsNoTracking().SingleOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null || inspection.Status is InspectionStatus.Open or InspectionStatus.AwaitingConfirm)
        {
            context.Logger.LogInformation($"Bỏ qua biên bản {inspectionId}: {inspection?.Status.ToString() ?? "không tồn tại"}");
            return;
        }

        var photos = await db.Photos.Where(p => p.InspectionId == inspectionId && p.Status == FileStatus.Ready).ToListAsync(ct);
        var sealedCount = 0;
        var mismatched = new List<Guid>();
        foreach (var photo in photos)
        {
            if (await HashAsync(StorageBucket.Incoming, photo.IncomingKey, ct) != photo.Sha256)
            {
                mismatched.Add(photo.Id);
                continue;
            }

            var evidenceKey = photo.IncomingKey;
            var versionId = await _store.CopyAsync(StorageBucket.Incoming, photo.IncomingKey, StorageBucket.Evidence, evidenceKey, ct);
            if (await HashAsync(StorageBucket.Evidence, evidenceKey, ct) != photo.Sha256)
            {
                mismatched.Add(photo.Id);
                continue;
            }

            photo.EvidenceKey = evidenceKey;
            photo.ObjectVersionId = versionId;
            photo.Status = FileStatus.Sealed;
            sealedCount++;
        }

        if (sealedCount > 0)
        {
            db.Audit(AuditEventTypes.EvidenceSealed, nameof(Inspection), inspectionId, $$"""{"photos":{{sealedCount}}}""");
        }
        if (mismatched.Count > 0)
        {
            db.Audit(AuditEventTypes.EvidenceHashMismatch, nameof(Inspection), inspectionId,
                $$"""{"photoIds":[{{string.Join(",", mismatched.Select(id => $"\"{id}\""))}}]}""");
        }
        await db.SaveChangesAsync(ct);
        context.Logger.LogInformation($"Biên bản {inspectionId}: niêm phong {sealedCount} ảnh, sai hash {mismatched.Count}");

        if (mismatched.Count > 0)
        {
            throw new InvalidOperationException($"Biên bản {inspectionId}: {mismatched.Count} ảnh sai SHA-256, chưa niêm phong");
        }
    }

    private async Task<string> HashAsync(StorageBucket bucket, string key, CancellationToken ct)
    {
        await using var stream = await _store.OpenReadAsync(bucket, key, ct);
        return Convert.ToBase64String(await SHA256.HashDataAsync(stream, ct));
    }
}
