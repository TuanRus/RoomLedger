using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng tenancy_version: một phiên bản hợp đồng (v1 ban đầu, phụ lục, gia hạn).
 * Điều khoản nghiệp vụ nằm ở cột có kiểu rõ ràng; AiTermsJson (jsonb) chỉ là kết quả AI thô
 * do Lambda read-document ghi, dạng {"monthlyRent":{"value":3500000,"confidence":0.93},...}.
 *
 * @author Tuấn
 * @version 1.1
 */
public class TenancyVersion : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public int Number { get; set; }
    public TenancyVersionType Type { get; set; } = TenancyVersionType.Initial;
    public TenancyVersionStatus Status { get; set; } = TenancyVersionStatus.Uploaded;
    public AiStatus AiStatus { get; set; } = AiStatus.Pending;
    public string? AiTermsJson { get; set; }
    public long? MonthlyRent { get; set; }
    public long? Deposit { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public long? ElectricityRate { get; set; }
    public long? WaterRate { get; set; }
    public List<string> EditedFields { get; set; } = [];
    public DateTimeOffset? LandlordConfirmedAt { get; set; }
    public DateTimeOffset? TenantConfirmedAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? RejectReason { get; set; }
    public Tenancy? Tenancy { get; set; }
    public List<TenancyFile> Files { get; set; } = [];
}
