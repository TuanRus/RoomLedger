using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.StateMachines;

/**
 * Luật chuyển trạng thái của hợp đồng và phiên bản hợp đồng (BR-11 đến BR-14).
 * Service gọi các hàm Ensure* trước khi đổi trạng thái; các thao tác dễ bị gọi trùng
 * (dùng link mời) còn chặn thêm bằng UPDATE có điều kiện trạng thái cũ.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class TenancyStateMachine
{
    /**
     * Chỉ sửa điều khoản khi phiên bản ở UPLOADED hoặc TERMS_REVIEWED (BR-11 PRECONDITION, RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureCanEditTerms(TenancyVersion version)
    {
        if (version.Status == TenancyVersionStatus.Locked)
        {
            throw new RecordLockedException("Hợp đồng đã khóa, không sửa được");
        }

        if (version.Status is not (TenancyVersionStatus.Uploaded or TenancyVersionStatus.TermsReviewed))
        {
            throw new InvalidStateException("Phiên bản này không còn sửa được điều khoản");
        }
    }

    /**
     * Chỉ thêm file scan khi phiên bản ở UPLOADED hoặc TERMS_REVIEWED.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureCanUploadFiles(TenancyVersion version) => EnsureCanEditTerms(version);

    /**
     * Sau khi lưu điều khoản: phiên bản đầu (chưa có người thuê) chờ mời → TERMS_REVIEWED.
     * Phụ lục, gia hạn hoặc bản tải lại khi đã có người thuê → AWAITING_CONFIRM ngay,
     * vì chủ nhà lưu điều khoản cũng là chủ nhà xác nhận và không cần link mời mới (BR-14 RULE 3).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void OnTermsSaved(Tenancy tenancy, TenancyVersion version, DateTimeOffset now)
    {
        if (tenancy.TenantId is null)
        {
            version.Status = TenancyVersionStatus.TermsReviewed;
            return;
        }

        version.Status = TenancyVersionStatus.AwaitingConfirm;
        version.LandlordConfirmedAt = now;
    }

    /**
     * Gửi lời mời (BR-12): cần điều khoản đã lưu; cho gửi lại khi lời mời trước chưa được dùng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureCanInvite(Tenancy tenancy, TenancyVersion latest)
    {
        if (tenancy.TenantId is not null || tenancy.Status is not (TenancyStatus.Draft or TenancyStatus.AwaitingTenant))
        {
            throw new InvalidStateException("Hợp đồng đã có người thuê, không cần mời lại");
        }

        if (latest.Status is not (TenancyVersionStatus.TermsReviewed or TenancyVersionStatus.AwaitingConfirm))
        {
            throw new InvalidStateException("Hãy lưu điều khoản trước khi mời người thuê");
        }
    }

    /**
     * Đổi trạng thái khi gửi lời mời (BR-12 RULE 4); chủ nhà xác nhận ngay khi mời (BR-13 ACTOR).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void OnInvited(Tenancy tenancy, TenancyVersion latest, DateTimeOffset now)
    {
        tenancy.Status = TenancyStatus.AwaitingTenant;
        latest.Status = TenancyVersionStatus.AwaitingConfirm;
        latest.LandlordConfirmedAt ??= now;
    }

    /**
     * Người thuê chỉ xác nhận khi phiên bản ở AWAITING_CONFIRM (BR-13 PRECONDITION).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureCanConfirm(TenancyVersion version)
    {
        if (version.Status == TenancyVersionStatus.Locked)
        {
            throw new RecordLockedException("Hợp đồng đã khóa");
        }

        if (version.Status != TenancyVersionStatus.AwaitingConfirm)
        {
            throw new InvalidStateException("Hợp đồng chưa sẵn sàng để xác nhận");
        }
    }

    /**
     * Người thuê xác nhận (BR-13 RULE 3). Trả true khi phiên bản vừa bị khóa (đủ hai bên xác nhận).
     * Khi khóa phiên bản, hợp đồng đang chờ người thuê chuyển sang ACTIVE.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool OnTenantConfirmed(Tenancy tenancy, TenancyVersion version, DateTimeOffset now)
    {
        version.TenantConfirmedAt = now;
        if (version.LandlordConfirmedAt is null)
        {
            return false;
        }

        version.Status = TenancyVersionStatus.Locked;
        version.LockedAt = now;
        if (tenancy.Status == TenancyStatus.AwaitingTenant)
        {
            tenancy.Status = TenancyStatus.Active;
        }

        return true;
    }

    /**
     * Người thuê báo "Không đúng" (BR-13 RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void OnTenantRejected(TenancyVersion version, string reason)
    {
        version.Status = TenancyVersionStatus.Rejected;
        version.RejectReason = reason;
    }

    /**
     * Kiểm điều kiện thêm phiên bản mới và trả loại phiên bản sẽ tạo.
     * Phụ lục, gia hạn cần hợp đồng ACTIVE và phiên bản trước đã khóa (BR-14).
     * Phiên bản trước bị REJECTED thì cho tải bản khác thay thế, giữ nguyên loại cũ (BR-13 RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static TenancyVersionType EnsureCanAddVersion(Tenancy tenancy, TenancyVersion latest, TenancyVersionType requested)
    {
        if (latest.Status == TenancyVersionStatus.Rejected)
        {
            return latest.Type;
        }

        if (requested == TenancyVersionType.Initial)
        {
            throw ValidationException.For("type", "Loại phiên bản phải là ADDENDUM hoặc RENEWAL");
        }

        if (tenancy.Status != TenancyStatus.Active || latest.Status != TenancyVersionStatus.Locked)
        {
            throw new InvalidStateException("Chỉ thêm phụ lục hoặc gia hạn khi hợp đồng đang hiệu lực");
        }

        return requested;
    }
}
