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
    public static void EnsureCanOpenMoveIn(Tenancy tenancy)
    {
        if (tenancy.Status == TenancyStatus.Settled)
        {
            throw new InvalidStateException("Hợp đồng đã quyết toán, không mở biên bản nhận phòng được");
        }
    }
}
