using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
 * Nghiệp vụ hợp đồng phía API (MD-03): tạo hợp đồng nháp, phiên bản, điều khoản, lời mời, xác nhận.
 * Đọc điều khoản bằng AI (read-document, Bedrock) do Sang làm; API chỉ đọc kết quả trong AiTermsJson.
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyAppService(AppDbContext db, ICurrentUser user, IStorage storage, IEventPublisher events, RoomAppService rooms)
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    /**
     * Tạo hợp đồng nháp DRAFT và phiên bản v1 (MD-03-FE-01). Phòng đã có bản nháp thì trả bản nháp đó.
     * Trả cờ created để endpoint chọn 201 hay 200.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<(TenancyDetailResponse Tenancy, bool Created)> CreateAsync(Guid roomId, CancellationToken ct)
    {
        var room = await rooms.GetOwnedAsync(roomId, ct);
        var open = await db.Tenancies.FirstOrDefaultAsync(t => t.RoomId == room.Id && t.Status != TenancyStatus.Settled, ct);
        if (open is not null)
        {
            return open.Status == TenancyStatus.Draft
                ? (await GetAsync(open.Id, ct), false)
                : throw new InvalidStateException("Phòng đang có hợp đồng, không tạo thêm được");
        }

        var tenancy = new Tenancy { RoomId = room.Id, LandlordId = room.LandlordId, Status = TenancyStatus.Draft };
        tenancy.Versions.Add(new TenancyVersion { TenancyId = tenancy.Id, Number = 1, Type = TenancyVersionType.Initial });
        db.Tenancies.Add(tenancy);
        db.Audit(AuditEventTypes.TenancyCreated, nameof(Tenancy), tenancy.Id);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(tenancy.Id, ct), true);
    }

    /**
     * Chi tiết hợp đồng cho chủ nhà hoặc người thuê của hợp đồng (global query filter); người khác nhận 404.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<TenancyDetailResponse> GetAsync(Guid tenancyId, CancellationToken ct)
    {
        var row = await db.Tenancies.AsNoTracking()
            .Where(t => t.Id == tenancyId)
            .Select(t => new
            {
                Tenancy = t,
                RoomCode = t.Room!.Code,
                PropertyName = t.Room.Property!.Name,
                Versions = t.Versions.OrderByDescending(v => v.Number)
                    .Select(v => new { Version = v, Files = v.Files.OrderBy(f => f.CreatedAt).ToList() })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy hợp đồng");

        string? tenantName = null;
        if (row.Tenancy.TenantId is { } tid && (await rooms.TenantNamesAsync([tid], ct)).TryGetValue(tid, out var name))
        {
            tenantName = name;
        }

        return new TenancyDetailResponse(
            row.Tenancy.Id, row.Tenancy.RoomId, row.RoomCode, row.PropertyName, row.Tenancy.Status, tenantName,
            row.Versions.Select(v => ToVersionResponse(v.Version, v.Files)).ToList());
    }

    /**
     * Thêm phiên bản mới (BR-14): phụ lục hoặc gia hạn khi hợp đồng ACTIVE, hoặc bản thay thế khi bản trước bị REJECTED.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<TenancyVersionResponse> AddVersionAsync(Guid tenancyId, CreateTenancyVersionRequest req, CancellationToken ct)
    {
        var tenancy = await GetOwnedAsync(tenancyId, ct);
        var latest = await LatestVersionAsync(tenancy.Id, ct);
        var type = TenancyStateMachine.EnsureCanAddVersion(tenancy, latest,
            req.Type ?? throw ValidationException.For("type", "Thiếu loại phiên bản"));
        var version = new TenancyVersion { TenancyId = tenancy.Id, Number = latest.Number + 1, Type = type };
        db.TenancyVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return ToVersionResponse(version, []);
    }

    /**
     * Lưu điều khoản sau khi chủ nhà kiểm tra số AI đọc (BR-11), ghi lại các trường đã sửa so với AI.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<TenancyVersionResponse> UpdateTermsAsync(Guid tenancyId, int number, UpdateTermsRequest req, CancellationToken ct)
    {
        var tenancy = await GetOwnedAsync(tenancyId, ct);
        var version = await db.TenancyVersions.Include(v => v.Files)
            .FirstOrDefaultAsync(v => v.TenancyId == tenancy.Id && v.Number == number, ct)
            ?? throw new NotFoundException("Không tìm thấy phiên bản hợp đồng");
        TenancyStateMachine.EnsureCanEditTerms(version);

        var terms = new ContractTerms(req.MonthlyRent, req.Deposit, req.StartDate, req.EndDate, req.ElectricityRate, req.WaterRate);
        TenancyService.ValidateTerms(terms);
        TenancyService.ApplyTerms(version, terms);
        version.EditedFields = TenancyService.EditedFields(terms, TenancyService.ParseAiTerms(version.AiTermsJson));
        TenancyStateMachine.OnTermsSaved(tenancy, version, db.Now);
        db.Audit(AuditEventTypes.TermsSaved, nameof(TenancyVersion), version.Id,
            JsonSerializer.Serialize(new { editedFields = version.EditedFields }));
        await db.SaveChangesAsync(ct);
        return ToVersionResponse(version, version.Files);
    }

    /**
     * Tạo link mời gắn email (BR-12): thu hồi lời mời cũ, lưu hash token, phát InviteCreated để Lambda notify gửi SES.
     * Token gốc chỉ nằm trong sự kiện gửi email, không lưu database và không trả cho chủ nhà.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<InviteResponse> CreateInviteAsync(Guid tenancyId, CreateInviteRequest req, CancellationToken ct)
    {
        var email = InviteService.NormalizeEmail(req.Email);
        var tenancy = await db.Tenancies.Include(t => t.Room!).ThenInclude(r => r.Property)
            .FirstOrDefaultAsync(t => t.Id == tenancyId && t.LandlordId == user.UserId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
        var latest = await LatestVersionAsync(tenancy.Id, ct);
        TenancyStateMachine.EnsureCanInvite(tenancy, latest);

        var now = db.Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Invites
            .Where(i => i.TenancyId == tenancy.Id && i.UsedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now).SetProperty(i => i.UpdatedAt, now), ct);

        var token = InviteService.GenerateToken();
        var invite = new Invite { TenancyId = tenancy.Id, TokenHash = token.TokenHash, Email = email, ExpiresAt = now.Add(InviteService.Lifetime) };
        db.Invites.Add(invite);
        TenancyStateMachine.OnInvited(tenancy, latest, now);
        db.Audit(AuditEventTypes.InviteCreated, nameof(Invite), invite.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await events.PublishAsync("InviteCreated", new
        {
            inviteId = invite.Id,
            tenancyId = tenancy.Id,
            email,
            token = token.Token,
            expiresAt = invite.ExpiresAt,
            propertyName = tenancy.Room!.Property!.Name,
            roomCode = tenancy.Room.Code,
        }, ct);
        return new InviteResponse(invite.Id, email, invite.ExpiresAt);
    }

    /**
     * Người thuê xác nhận hoặc báo "Không đúng" (BR-13). Đủ hai bên xác nhận thì khóa phiên bản
     * và phát TenancyVersionLocked để evidence-seal chép file sang bucket evidence (Object Lock).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<TenancyVersionResponse> ConfirmAsync(Guid tenancyId, int number, ConfirmTenancyVersionRequest req, CancellationToken ct)
    {
        var tenantId = user.UserId;
        var tenancy = await db.Tenancies.FirstOrDefaultAsync(t => t.Id == tenancyId && t.TenantId == tenantId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
        var version = await db.TenancyVersions.Include(v => v.Files)
            .FirstOrDefaultAsync(v => v.TenancyId == tenancy.Id && v.Number == number, ct)
            ?? throw new NotFoundException("Không tìm thấy phiên bản hợp đồng");
        TenancyStateMachine.EnsureCanConfirm(version);

        var decision = req.Decision ?? throw ValidationException.For("decision", "Hãy chọn xác nhận hoặc không đúng");
        var locked = false;
        if (decision == ConfirmDecision.Reject)
        {
            TenancyStateMachine.OnTenantRejected(version, TenancyService.ValidateRejectReason(req.Reason));
            db.Audit(AuditEventTypes.ContractRejected, nameof(TenancyVersion), version.Id);
        }
        else
        {
            locked = TenancyStateMachine.OnTenantConfirmed(tenancy, version, db.Now);
            if (locked && tenancy.Status == TenancyStatus.Active)
            {
                await db.Rooms.Where(r => r.Id == tenancy.RoomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RoomStatus.Occupied), ct);
            }

            db.Audit(AuditEventTypes.ContractConfirmed, nameof(TenancyVersion), version.Id);
        }

        await db.SaveChangesAsync(ct);

        if (decision == ConfirmDecision.Reject)
        {
            await events.PublishAsync("TenancyVersionRejected", new { tenancyId = tenancy.Id, versionId = version.Id }, ct);
        }
        else if (locked)
        {
            await events.PublishAsync("TenancyVersionLocked", new
            {
                tenancyId = tenancy.Id,
                versionId = version.Id,
                landlordId = tenancy.LandlordId,
                fileIds = version.Files.Select(f => f.Id).ToList(),
            }, ct);
        }

        return ToVersionResponse(version, version.Files);
    }

    /**
     * Hợp đồng thuộc chủ nhà đang đăng nhập (tracking), không thuộc thì 404.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Tenancy> GetOwnedAsync(Guid tenancyId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.Tenancies.FirstOrDefaultAsync(t => t.Id == tenancyId && t.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
    }

    /**
     * Phiên bản mới nhất của hợp đồng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<TenancyVersion> LatestVersionAsync(Guid tenancyId, CancellationToken ct) =>
        await db.TenancyVersions.Where(v => v.TenancyId == tenancyId).OrderByDescending(v => v.Number).FirstAsync(ct);

    private TenancyVersionResponse ToVersionResponse(TenancyVersion v, IEnumerable<TenancyFile> files)
    {
        var ai = TenancyService.ParseAiTerms(v.AiTermsJson);
        var saved = new Dictionary<string, JsonElement?>
        {
            ["monthlyRent"] = ToJson(v.MonthlyRent),
            ["deposit"] = ToJson(v.Deposit),
            ["startDate"] = ToJson(v.StartDate?.ToString("yyyy-MM-dd")),
            ["endDate"] = ToJson(v.EndDate?.ToString("yyyy-MM-dd")),
            ["electricityRate"] = ToJson(v.ElectricityRate),
            ["waterRate"] = ToJson(v.WaterRate),
        };
        var terms = TenancyService.TermFields.ToDictionary(f => f, f =>
        {
            ai.TryGetValue(f, out var a);
            var needsReview = v.AiStatus == AiStatus.Succeeded && (a?.Confidence is null || a.Confidence < TenancyService.ReviewConfidence);
            return new TermResponse(saved[f] ?? a?.Value, a?.Value, a?.Confidence, needsReview);
        });

        return new TenancyVersionResponse(
            v.Id, v.Number, v.Type, v.Status, v.AiStatus, terms, v.EditedFields,
            v.LandlordConfirmedAt, v.TenantConfirmedAt, v.LockedAt, v.RejectReason,
            files.Select(f => new TenancyFileResponse(f.Id, f.FileName, f.ContentType, f.Size, f.Status, FileUrl(f))).ToList());
    }

    private string? FileUrl(TenancyFile f) => f switch
    {
        { EvidenceKey: not null } => storage.CreateDownloadUrl(StorageBucket.Evidence, f.EvidenceKey, DownloadLifetime, f.ObjectVersionId),
        { Status: not FileStatus.PendingUpload } => storage.CreateDownloadUrl(StorageBucket.Incoming, f.IncomingKey, DownloadLifetime),
        _ => null,
    };

    private static JsonElement? ToJson<T>(T? value) => value is null ? null : JsonSerializer.SerializeToElement(value);
}
