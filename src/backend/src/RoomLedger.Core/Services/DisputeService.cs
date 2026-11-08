using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Luật khiếu nại dùng chung cho chỉ số (MD-05, Tuấn) và mục trừ cọc (MD-06, Sang).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class DisputeService
{
    /**
     * Lý do khiếu nại từ 10 đến 1000 ký tự (BR-23 RULE 3).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length is < 10 or > 1000)
        {
            throw ValidationException.For("reason", "Lý do phải có từ 10 đến 1000 ký tự");
        }

        return trimmed;
    }

    /**
     * Đóng khiếu nại kèm cách xử lý và phản hồi của chủ nhà (BR-23 RULE 4).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void Resolve(Dispute dispute, DisputeResolution resolution, string? response, DateTimeOffset now)
    {
        if (dispute.Status != DisputeStatus.Open)
        {
            throw new InvalidStateException("Khiếu nại đã được xử lý");
        }

        var text = response?.Trim();
        if (resolution == DisputeResolution.Kept && string.IsNullOrEmpty(text))
        {
            throw ValidationException.For("response", "Hãy ghi phản hồi khi giữ nguyên chỉ số");
        }

        if (text is { Length: > 1000 })
        {
            throw ValidationException.For("response", "Phản hồi tối đa 1000 ký tự");
        }

        dispute.Status = DisputeStatus.Resolved;
        dispute.Resolution = resolution;
        dispute.Response = text;
        dispute.ResolvedAt = now;
    }
}
