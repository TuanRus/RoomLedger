using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Nghiệp vụ kết thúc hợp đồng và chuyển phòng (màn LL-10). Bốn bước: bắt đầu (ngày, hình thức, phòng chuyển đến),
 * chốt chỉ số cuối (dùng lại API chỉ số), lập hóa đơn cuối, chốt hợp đồng. Hợp đồng ACTIVE → MOVING_OUT → SETTLED.
 * Hóa đơn cuối ở trạng thái DRAFT, chủ nhà gửi bằng POST /invoices/{id}/send; hóa đơn cũ chưa thu không gộp vào.
 * Luồng này loại trừ luồng biên bản trả phòng cũ: một hợp đồng chỉ đi theo một trong hai.
 *
 * @author Tuấn
 * @version 1.0
 */
public class EndTenancyAppService(AppDbContext db, ICurrentUser user, MeterAppService meters, RoomAppService rooms, IEventPublisher events, ILogger<EndTenancyAppService> log)
{
    private const string OneInvoicePerPeriodIndex = "ux_invoice_tenancy_id_period";
    private const string OpenTenancyPerRoomIndex = "ux_tenancy_room_id_open";

    /**
     * Bắt đầu kết thúc hợp đồng (hợp đồng ACTIVE → MOVING_OUT) hoặc sửa ngày, hình thức, phòng chuyển đến khi đang MOVING_OUT
     * và chưa chốt. Trả cờ created để endpoint chọn 201 hay 200.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<(EndTenancyResponse Response, bool Created)> StartAsync(Guid tenancyId, EndTenancyRequest req, CancellationToken ct)
    {
        var tenancy = await LoadOwnedAsync(tenancyId, ct);
        var created = tenancy.Status == TenancyStatus.Active;
        if (!created && !IsEnding(tenancy))
        {
            throw new InvalidStateException("Chỉ kết thúc được hợp đồng đang hiệu lực");
        }

        if (await db.Inspections.AnyAsync(i => i.TenancyId == tenancy.Id && i.Type == InspectionType.MoveOut, ct))
        {
            throw new InvalidStateException("Hợp đồng đã mở biên bản trả phòng, không dùng luồng kết thúc hợp đồng được");
        }

        var kind = req.Kind ?? throw ValidationException.For("kind", "Hãy chọn rời đi hoặc chuyển phòng");
        var endDate = req.EndDate ?? throw ValidationException.For("endDate", "Thiếu ngày kết thúc");
        var terms = await LockedTermsAsync(tenancy.Id, ct);
        if (terms?.StartDate is { } start && endDate < start)
        {
            throw ValidationException.For("endDate", "Ngày kết thúc không được trước ngày bắt đầu hợp đồng");
        }

        var finalPeriod = PeriodOf(endDate);
        var latestInvoiced = await db.Invoices.Where(i => i.TenancyId == tenancy.Id).MaxAsync(i => (string?)i.Period, ct);
        if (latestInvoiced?.Trim() is { } latest && string.CompareOrdinal(finalPeriod, latest) <= 0)
        {
            throw new ConflictException(ErrorCodes.AlreadyInvoiced, $"Kỳ {latest} đã có hóa đơn; chọn ngày kết thúc ở kỳ sau");
        }

        var oldPeriod = !created && tenancy.EndDate is { } old ? PeriodOf(old) : null;
        var periodChanged = oldPeriod is not null && oldPeriod != finalPeriod;
        if (periodChanged && await db.MeterReadings.AnyAsync(m => m.TenancyId == tenancy.Id && m.Period == oldPeriod && m.Status == MeterReadingStatus.Confirmed, ct))
        {
            throw new InvalidStateException($"Đã có chỉ số cuối kỳ {oldPeriod} được xác nhận, không đổi sang kỳ khác được; hãy hủy kết thúc thuê rồi bắt đầu lại");
        }

        Guid? targetId = null;
        if (kind == EndKind.Move)
        {
            targetId = (await ValidateTargetAsync(tenancy, req.TargetRoomId, ct)).Id;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = db.Now;
        var rows = await db.Tenancies
            .Where(t => t.Id == tenancy.Id && (t.Status == TenancyStatus.Active
                || (t.Status == TenancyStatus.MovingOut && t.EndDate != null && t.EndedAt == null)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TenancyStatus.MovingOut)
                .SetProperty(t => t.EndDate, (DateOnly?)endDate)
                .SetProperty(t => t.EndKind, (EndKind?)kind)
                .SetProperty(t => t.MoveTargetRoomId, targetId)
                .SetProperty(t => t.UpdatedAt, now), ct);
        if (rows == 0)
        {
            throw new InvalidStateException("Hợp đồng vừa đổi trạng thái, hãy tải lại rồi thử lại");
        }

        if (periodChanged)
        {
            await DeleteUnconfirmedReadingsAsync(tenancy.Id, oldPeriod!, ct);
        }

        db.Audit(AuditEventTypes.TenancyEndStarted, nameof(Tenancy), tenancy.Id,
            JsonSerializer.Serialize(new { endDate = endDate.ToString("yyyy-MM-dd"), kind = kind.ToString().ToUpperInvariant(), targetRoomId = targetId }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(tenancy.Id, ct), created);
    }

    /**
     * Trạng thái kết thúc hợp đồng. Hợp đồng chưa từng kết thúc → 404.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<EndTenancyResponse> GetAsync(Guid tenancyId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        var row = await db.Tenancies.AsNoTracking()
            .Where(t => t.Id == tenancyId && t.LandlordId == landlordId)
            .Select(t => new { Tenancy = t, RoomCode = t.Room!.Code, Property = t.Room.Property! })
            .FirstOrDefaultAsync(ct);
        if (row?.Tenancy is not { EndDate: not null, EndKind: not null } tenancy)
        {
            throw new NotFoundException("Hợp đồng chưa được kết thúc");
        }

        var endDate = tenancy.EndDate!.Value;
        var finalPeriod = endDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var finished = tenancy.EndedAt is not null;
        var terms = await LockedTermsAsync(tenancy.Id, ct);

        var readings = await db.MeterReadings.AsNoTracking()
            .Where(m => m.TenancyId == tenancy.Id && m.Period == finalPeriod)
            .ToListAsync(ct);
        var previous = await meters.PreviousValuesAsync([tenancy.RoomId], finalPeriod, ct);
        var finalInvoice = finished ? await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenancyId == tenancy.Id && i.Period == finalPeriod, ct) : null;

        FinalReadingResponse Reading(MeterKind kind)
        {
            // After finish the room may belong to the next tenant: the numbers used on the final invoice are the record
            var prev = finalInvoice is not null
                ? (kind == MeterKind.Electricity ? finalInvoice.ElectricityPrevious : finalInvoice.WaterPrevious)
                : previous.GetValueOrDefault((tenancy.RoomId, kind));
            return readings.FirstOrDefault(m => m.Kind == kind) is { } m
                ? new FinalReadingResponse(m.Id, prev, m.Value ?? m.AiValue, m.Status, meters.ToResponse(m, prev).PhotoUrl, m.ConfirmedAt, m.MeterReplaced)
                : new FinalReadingResponse(null, prev, null, null, null, null);
        }

        var invoices = db.Invoices.AsNoTracking().Where(i => i.TenancyId == tenancy.Id);
        var unpaid = await invoices
            .Where(i => i.Period != finalPeriod && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Disputed))
            .SumAsync(i => (long?)i.Total, ct) ?? 0;
        Guid? finalInvoiceId = finalInvoice?.Id;

        EndTargetRoomResponse? target = null;
        Guid? newTenancyId = null;
        if (tenancy.MoveTargetRoomId is { } targetId)
        {
            target = await db.Rooms.AsNoTracking().Where(r => r.Id == targetId)
                .Select(r => new EndTargetRoomResponse(r.Id, r.Code, r.MonthlyRent)).FirstOrDefaultAsync(ct);
            newTenancyId = tenancy.NextTenancyId;
        }

        string? tenantName = null;
        if (tenancy.TenantId is { } tid && (await rooms.TenantNamesAsync([tid], ct)).TryGetValue(tid, out var name))
        {
            tenantName = name;
        }

        return new EndTenancyResponse(
            tenancy.Id, tenancy.RoomId, row.RoomCode, tenantName, endDate, tenancy.EndKind!.Value, finalPeriod, target,
            Reading(MeterKind.Electricity), Reading(MeterKind.Water),
            new EndRatesResponse(terms?.ElectricityRate ?? row.Property.ElectricityRate, terms?.WaterRate ?? row.Property.WaterRate),
            unpaid, terms?.Deposit ?? 0, tenancy.LastRent, finalInvoiceId, tenancy.EndedAt, newTenancyId);
    }

    /**
     * Hủy kết thúc hợp đồng: chỉ khi đang MOVING_OUT của luồng này và chưa chốt. Hợp đồng về ACTIVE, xóa thông tin kết thúc.
     * Chỉ số cuối chưa xác nhận bị xóa; chỉ số đã xác nhận giữ nguyên (vẫn là số thật của kỳ đó).
     * Dùng UPDATE có điều kiện trạng thái cũ để không đè lên một lần chốt chạy cùng lúc.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<CancelEndTenancyResponse> CancelAsync(Guid tenancyId, CancellationToken ct)
    {
        var tenancy = await LoadOwnedAsync(tenancyId, ct);
        if (!IsEnding(tenancy))
        {
            throw new InvalidStateException("Hợp đồng không ở bước kết thúc, không hủy được");
        }

        var finalPeriod = PeriodOf(tenancy.EndDate!.Value);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = db.Now;
        var rows = await db.Tenancies
            .Where(t => t.Id == tenancy.Id && t.Status == TenancyStatus.MovingOut && t.EndDate != null && t.EndedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TenancyStatus.Active)
                .SetProperty(t => t.EndDate, (DateOnly?)null)
                .SetProperty(t => t.EndKind, (EndKind?)null)
                .SetProperty(t => t.MoveTargetRoomId, (Guid?)null)
                .SetProperty(t => t.UpdatedAt, now), ct);
        if (rows == 0)
        {
            throw new InvalidStateException("Hợp đồng vừa đổi trạng thái, hãy tải lại rồi thử lại");
        }

        await DeleteUnconfirmedReadingsAsync(tenancy.Id, finalPeriod, ct);
        db.Audit(AuditEventTypes.TenancyEndCancelled, nameof(Tenancy), tenancy.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CancelEndTenancyResponse(tenancy.Id, TenancyStatus.Active);
    }

    /**
     * Chốt kết thúc hợp đồng trong một transaction: kiểm hai chỉ số cuối đã xác nhận, lập hóa đơn cuối (DRAFT, tiền phòng
     * là lastRent), hợp đồng → SETTLED, phòng → VACANT; MOVE thì tạo hợp đồng mới ở phòng đích, chờ người thuê xác nhận.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<EndTenancyResponse> FinishAsync(Guid tenancyId, FinishEndTenancyRequest req, CancellationToken ct)
    {
        var tenancy = await LoadOwnedAsync(tenancyId, ct);
        if (!IsEnding(tenancy))
        {
            throw new InvalidStateException("Hợp đồng chưa bắt đầu kết thúc hoặc đã chốt");
        }

        var kind = tenancy.EndKind!.Value;
        var v = new Validator().Require(req.LastRent is >= 0, "lastRent", "Tiền phòng kỳ cuối phải từ 0 trở lên");
        if (kind == EndKind.Move)
        {
            v.Require(req.StartElectricity is >= 0, "startElectricity", "Hãy nhập chỉ số điện ban đầu ở phòng mới")
                .Require(req.StartWater is >= 0, "startWater", "Hãy nhập chỉ số nước ban đầu ở phòng mới");
        }

        v.ThrowIfInvalid();

        var finalPeriod = PeriodOf(tenancy.EndDate!.Value);
        var readings = await db.MeterReadings
            .Where(m => m.TenancyId == tenancy.Id && m.Period == finalPeriod && m.Status == MeterReadingStatus.Confirmed)
            .ToListAsync(ct);
        var elec = readings.FirstOrDefault(m => m.Kind == MeterKind.Electricity);
        var water = readings.FirstOrDefault(m => m.Kind == MeterKind.Water);
        var missing = new List<FieldError>();
        if (elec?.Value is null)
        {
            missing.Add(new FieldError("electricity", $"Chưa xác nhận chỉ số điện kỳ {finalPeriod}"));
        }

        if (water?.Value is null)
        {
            missing.Add(new FieldError("water", $"Chưa xác nhận chỉ số nước kỳ {finalPeriod}"));
        }

        if (missing.Count > 0)
        {
            throw new ConflictException(ErrorCodes.FinalReadingMissing,
                "Cần xác nhận chỉ số cuối: " + string.Join("; ", missing.Select(m => m.Message)), missing.Cast<object>().ToList());
        }

        if (await db.Invoices.AnyAsync(i => i.TenancyId == tenancy.Id && i.Period == finalPeriod, ct))
        {
            throw AlreadyInvoiced(finalPeriod);
        }

        var property = await db.Properties.FirstAsync(p => p.Id == tenancy.Room!.PropertyId, ct);
        var terms = await LockedTermsAsync(tenancy.Id, ct);
        var previous = await meters.PreviousValuesAsync([tenancy.RoomId], finalPeriod, ct);
        var elecPrev = elec!.MeterReplaced ? 0 : RequirePrevious(previous.GetValueOrDefault((tenancy.RoomId, MeterKind.Electricity)), "điện");
        var waterPrev = water!.MeterReplaced ? 0 : RequirePrevious(previous.GetValueOrDefault((tenancy.RoomId, MeterKind.Water)), "nước");

        var invoice = new Invoice
        {
            TenancyId = tenancy.Id,
            Period = finalPeriod,
            ElectricityReadingId = elec.Id,
            WaterReadingId = water.Id,
        };
        InvoiceService.Calculate(invoice, new InvoiceInput(
            req.LastRent!.Value,
            elecPrev, elec.Value!.Value, terms?.ElectricityRate ?? property.ElectricityRate,
            waterPrev, water.Value!.Value, terms?.WaterRate ?? property.WaterRate,
            property.ServiceFee));

        var now = db.Now;
        Room? target = null;
        ContractTerms? movedTerms = null;
        if (kind == EndKind.Move)
        {
            target = await ValidateTargetAsync(tenancy, tenancy.MoveTargetRoomId, ct);
            movedTerms = MovedTerms(tenancy, target, terms, property);
            TenancyService.ValidateTerms(movedTerms);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Tenancies
            .Where(t => t.Id == tenancy.Id && t.Status == TenancyStatus.MovingOut && t.EndDate != null && t.EndedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TenancyStatus.Settled)
                .SetProperty(t => t.LastRent, req.LastRent)
                .SetProperty(t => t.EndedAt, (DateTimeOffset?)now)
                .SetProperty(t => t.UpdatedAt, now), ct);
        if (rows == 0)
        {
            throw new InvalidStateException("Hợp đồng vừa đổi trạng thái, hãy tải lại rồi thử lại");
        }

        db.Invoices.Add(invoice);
        if (target is not null)
        {
            tenancy.NextTenancyId = AddMovedTenancy(tenancy, target, movedTerms!, req, now);
        }

        await db.Rooms.Where(r => r.Id == tenancy.RoomId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RoomStatus.Vacant).SetProperty(r => r.UpdatedAt, now), ct);
        db.Audit(AuditEventTypes.TenancyEnded, nameof(Tenancy), tenancy.Id,
            JsonSerializer.Serialize(new { kind = kind.ToString().ToUpperInvariant(), period = finalPeriod, lastRent = req.LastRent, invoiceId = invoice.Id }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneInvoicePerPeriodIndex })
        {
            throw AlreadyInvoiced(finalPeriod);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OpenTenancyPerRoomIndex })
        {
            throw new ConflictException(ErrorCodes.RoomOccupied, "Phòng chuyển đến không còn trống");
        }

        await tx.CommitAsync(ct);
        if (target is not null && tenancy.NextTenancyId is { } nextId)
        {
            try
            {
                await PublishMovedAsync(tenancy, nextId, target, property, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Hợp đồng đã chốt xong; không trả 500 chỉ vì chưa gửi được thông báo
                log.LogWarning(e, "Không phát được sự kiện TenancyMoved cho hợp đồng {TenancyId}", tenancy.Id);
            }
        }

        return await GetAsync(tenancy.Id, ct);
    }

    /** Điều khoản v1 của hợp đồng mới: giá thuê của phòng đích, cọc và ngày hết hạn chép từ hợp đồng cũ, giá điện nước cũ hoặc của nhà trọ. */
    private static ContractTerms MovedTerms(Tenancy old, Room target, TenancyVersion? terms, Property property)
    {
        var start = old.EndDate!.Value;
        var end = terms?.EndDate is { } e && e > start ? e : start.AddYears(1);
        return new ContractTerms(
            target.MonthlyRent, terms?.Deposit ?? 0, start, end,
            terms?.ElectricityRate ?? property.ElectricityRate, terms?.WaterRate ?? property.WaterRate);
    }

    /** Sau khi commit: báo người thuê có hợp đồng mới cần xác nhận (notify gửi email). */
    private async Task PublishMovedAsync(Tenancy old, Guid newTenancyId, Room target, Property property, CancellationToken ct)
    {
        var email = old.TenantId is { } tid
            ? await db.AppUsers.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == tid).Select(u => u.Email).FirstOrDefaultAsync(ct)
            : null;
        await events.PublishAsync("TenancyMoved", new
        {
            tenancyId = old.Id,
            newTenancyId,
            tenantId = old.TenantId,
            tenantEmail = email,
            propertyName = property.Name,
            roomCode = target.Code,
        }, ct);
    }

    private Guid AddMovedTenancy(Tenancy old, Room target, ContractTerms terms, FinishEndTenancyRequest req, DateTimeOffset now)
    {
        var moved = new Tenancy
        {
            RoomId = target.Id,
            LandlordId = target.LandlordId,
            TenantId = old.TenantId,
            Status = TenancyStatus.AwaitingTenant,
            OpeningElectricity = req.StartElectricity,
            OpeningWater = req.StartWater,
        };
        // Chủ nhà đã xác nhận ngay; người thuê xác nhận bằng API confirm sẵn có, đủ hai bên thì khóa và hợp đồng ACTIVE
        var version = new TenancyVersion
        {
            TenancyId = moved.Id,
            Number = 1,
            Type = TenancyVersionType.Initial,
            Status = TenancyVersionStatus.AwaitingConfirm,
            LandlordConfirmedAt = now,
        };
        TenancyService.ApplyTerms(version, terms);
        moved.Versions.Add(version);
        db.Tenancies.Add(moved);
        db.Audit(AuditEventTypes.TenancyCreated, nameof(Tenancy), moved.Id);
        return moved.Id;
    }

    private Task<int> DeleteUnconfirmedReadingsAsync(Guid tenancyId, string period, CancellationToken ct) =>
        db.MeterReadings
            .Where(m => m.TenancyId == tenancyId && m.Period == period && m.Status != MeterReadingStatus.Confirmed)
            .ExecuteDeleteAsync(ct);

    private static string PeriodOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private async Task<Room> ValidateTargetAsync(Tenancy tenancy, Guid? targetRoomId, CancellationToken ct)
    {
        var id = targetRoomId ?? throw ValidationException.For("targetRoomId", "Hãy chọn phòng chuyển đến");
        if (id == tenancy.RoomId)
        {
            throw ValidationException.For("targetRoomId", "Phòng chuyển đến phải khác phòng hiện tại");
        }

        var target = await rooms.GetOwnedAsync(id, ct);
        if (target.PropertyId != tenancy.Room!.PropertyId)
        {
            throw ValidationException.For("targetRoomId", "Phòng chuyển đến phải cùng nhà trọ");
        }

        // Hợp đồng nháp cũng giữ chỗ: unique index ux_tenancy_room_id_open chỉ cho một hợp đồng chưa quyết toán mỗi phòng
        if (await db.Tenancies.AnyAsync(t => t.RoomId == target.Id && t.Status != TenancyStatus.Settled, ct))
        {
            throw new ConflictException(ErrorCodes.RoomOccupied, "Phòng chuyển đến không còn trống");
        }

        return target;
    }

    private async Task<Tenancy> LoadOwnedAsync(Guid tenancyId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.Tenancies.Include(t => t.Room)
            .FirstOrDefaultAsync(t => t.Id == tenancyId && t.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
    }

    private static bool IsEnding(Tenancy t) => t.Status == TenancyStatus.MovingOut && t.EndDate is not null && t.EndedAt is null;

    private Task<TenancyVersion?> LockedTermsAsync(Guid tenancyId, CancellationToken ct) =>
        db.TenancyVersions.AsNoTracking()
            .Where(v => v.TenancyId == tenancyId && v.Status == TenancyVersionStatus.Locked)
            .OrderByDescending(v => v.Number)
            .FirstOrDefaultAsync(ct);

    private static long RequirePrevious(long? previous, string meter) =>
        previous ?? throw new InvalidStateException($"Thiếu chỉ số {meter} kỳ trước để tính hóa đơn cuối");

    private static ConflictException AlreadyInvoiced(string period) =>
        new(ErrorCodes.AlreadyInvoiced, $"Kỳ {period} đã có hóa đơn");
}
