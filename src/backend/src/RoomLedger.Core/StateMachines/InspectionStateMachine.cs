using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.StateMachines;

/**
 * Luật chuyển trạng thái của biên bản; mọi thay đổi trạng thái dùng UPDATE có điều kiện trạng thái cũ.
 *
 * @author Sang
 * @version 1.0
 */
public static class InspectionStateMachine
{
    /**
     * Mở biên bản nhận phòng: hợp đồng chưa quyết toán (BR-15 PRECONDITION). Đã SETTLED → 409 INVALID_STATE.
     *
     * @author Sang
     * @version 1.0
     */
    /**
     * Gửi, xóa hoặc thay ảnh chỉ khi biên bản chưa khóa: OPEN hoặc AWAITING_CONFIRM (BR-17 RULE 2, kiểm thử K1).
     * Từ LOCKED trở đi → 409 RECORD_LOCKED.
     *
     * @author Sang
     * @version 1.0
     */
    public static void EnsureCanChangePhotos(InspectionStatus status)
    {
        if (status is not (InspectionStatus.Open or InspectionStatus.AwaitingConfirm))
        {
            throw new RecordLockedException("Biên bản đã khóa, không xóa được ảnh");
        }
    }

    public static void EnsureCanOpenMoveIn(Tenancy tenancy)
    {
        if (tenancy.Status == TenancyStatus.Settled)
        {
            throw new InvalidStateException("Hợp đồng đã quyết toán, không mở biên bản nhận phòng được");
        }
    }
}
