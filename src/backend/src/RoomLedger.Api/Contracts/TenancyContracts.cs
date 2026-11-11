using System.Text.Json;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body POST /tenancies/{tenancyId}/versions (MD-03-FE-06).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateTenancyVersionRequest(TenancyVersionType? Type);

/**
 * Body PATCH /tenancies/{tenancyId}/versions/{v}/terms (MD-03-FE-03).
 *
 * @author Tuấn
 * @version 1.0
 */
public record UpdateTermsRequest(
    long? MonthlyRent,
    long? Deposit,
    DateOnly? StartDate,
    DateOnly? EndDate,
    long? ElectricityRate,
    long? WaterRate);

/**
 * Body POST /tenancies/{tenancyId}/invites (MD-03-FE-04).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateInviteRequest(string? Email);

/**
 * Body POST /tenancies/{tenancyId}/versions/{v}/confirm (MD-03-FE-05).
 *
 * @author Tuấn
 * @version 1.0
 */
public record ConfirmTenancyVersionRequest(ConfirmDecision? Decision, string? Reason);

/**
 * Một điều khoản: giá trị hiện tại, giá trị AI đọc, độ tin cậy và cờ cần kiểm tra (confidence &lt; 0,8).
 *
 * @author Tuấn
 * @version 1.0
 */
public record TermResponse(JsonElement? Value, JsonElement? AiValue, decimal? Confidence, bool NeedsReview);

/**
 * File scan của phiên bản hợp đồng; url là presigned GET hạn 5 phút (null khi file chưa lên).
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenancyFileResponse(Guid FileId, string FileName, string ContentType, long Size, FileStatus Status, string? Url);

/**
 * Một phiên bản hợp đồng kèm điều khoản và file.
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenancyVersionResponse(
    Guid VersionId,
    int Number,
    TenancyVersionType Type,
    TenancyVersionStatus Status,
    AiStatus AiStatus,
    IReadOnlyDictionary<string, TermResponse> Terms,
    IReadOnlyList<string> EditedFields,
    DateTimeOffset? LandlordConfirmedAt,
    DateTimeOffset? TenantConfirmedAt,
    DateTimeOffset? LockedAt,
    string? RejectReason,
    IReadOnlyList<TenancyFileResponse> Files);

/**
 * Chi tiết hợp đồng (MD-03-FE-02): phòng, người thuê, mọi phiên bản (mới nhất trước).
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenancyDetailResponse(
    Guid TenancyId,
    Guid RoomId,
    string RoomCode,
    string PropertyName,
    TenancyStatus Status,
    string? TenantName,
    IReadOnlyList<TenancyVersionResponse> Versions);

/**
 * Lời mời vừa tạo. Không trả token: token chỉ đi trong email.
 *
 * @author Tuấn
 * @version 1.0
 */
public record InviteResponse(Guid InviteId, string Email, DateTimeOffset ExpiresAt);

/**
 * Việc người thuê cần làm trên trang chủ.
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenantTodoResponse(string Type, Guid TargetId, string Title);

/**
 * Hóa đơn mới nhất trên trang chủ người thuê.
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenantInvoiceSummary(Guid InvoiceId, string Period, InvoiceStatus Status, long Total);

/**
 * Trang chủ người thuê (GET /tenant/home): phòng đang thuê, việc cần làm, hóa đơn mới nhất.
 *
 * @author Tuấn
 * @version 1.0
 */
public record TenantHomeResponse(
    Guid? TenancyId,
    Guid? RoomId,
    string? RoomCode,
    string? PropertyName,
    string? PropertyAddress,
    TenancyStatus? Status,
    long? MonthlyRent,
    long? Deposit,
    DateOnly? EndDate,
    IReadOnlyList<TenantTodoResponse> Todos,
    TenantInvoiceSummary? LatestInvoice);
