using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Đầu vào tính một hóa đơn: chỉ số kỳ trước, kỳ này và đơn giá của phiên bản hợp đồng hiệu lực.
 *
 * @author Tuấn
 * @version 1.0
 */
public record InvoiceInput(
    long RoomRent,
    long ElectricityPrevious,
    long ElectricityCurrent,
    long ElectricityRate,
    long WaterPrevious,
    long WaterCurrent,
    long WaterRate,
    long ServiceFee);

/**
 * Luật hóa đơn (BR-22 đến BR-24).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class InvoiceService
{
    /**
     * Tính tiền (BR-22 RULE 1): (chỉ số kỳ này − kỳ trước) × đơn giá, cộng tiền phòng và phí dịch vụ.
     * Dùng phép tính checked để tràn số là lỗi, không âm thầm sai tiền.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void Calculate(Invoice invoice, InvoiceInput input)
    {
        checked
        {
            invoice.RoomRent = input.RoomRent;
            invoice.ElectricityPrevious = input.ElectricityPrevious;
            invoice.ElectricityCurrent = input.ElectricityCurrent;
            invoice.ElectricityRate = input.ElectricityRate;
            invoice.ElectricityAmount = Math.Max(0, input.ElectricityCurrent - input.ElectricityPrevious) * input.ElectricityRate;
            invoice.WaterPrevious = input.WaterPrevious;
            invoice.WaterCurrent = input.WaterCurrent;
            invoice.WaterRate = input.WaterRate;
            invoice.WaterAmount = Math.Max(0, input.WaterCurrent - input.WaterPrevious) * input.WaterRate;
            invoice.ServiceFee = input.ServiceFee;
            invoice.Total = invoice.RoomRent + invoice.ElectricityAmount + invoice.WaterAmount + invoice.ServiceFee;
        }
    }

    /**
     * Không lập hóa đơn cho kỳ trong tương lai (BR-22 EXCEPTION INVALID_PERIOD).
     * Kỳ hiện tại được phép vì chủ nhà thường chốt vào những ngày cuối tháng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsurePeriodStarted(string period, DateOnly today)
    {
        if (Period.EndExclusive(period).AddMonths(-1) > today)
        {
            throw new BadRequestException(ErrorCodes.InvalidPeriod, "Kỳ này chưa bắt đầu, chưa lập hóa đơn được");
        }
    }

    /**
     * DRAFT → SENT (BR-23 RULE 1).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void Send(Invoice invoice, DateTimeOffset now)
    {
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidStateException("Hóa đơn đã được gửi");
        }

        invoice.Status = InvoiceStatus.Sent;
        invoice.SentAt = now;
    }

    /**
     * SENT → DISPUTED khi người thuê khiếu nại (BR-23 RULE 3). Đã thanh toán thì không khiếu nại được.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void MarkDisputed(Invoice invoice)
    {
        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.Disputed))
        {
            throw new InvalidStateException("Không khiếu nại được hóa đơn ở trạng thái này");
        }

        invoice.Status = InvoiceStatus.Disputed;
    }

    /**
     * SENT → PAID (BR-24). Hóa đơn đang khiếu nại phải xử lý trước.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void MarkPaid(Invoice invoice, DateTimeOffset now)
    {
        if (invoice.Status == InvoiceStatus.Disputed)
        {
            throw new InvalidStateException("Hãy xử lý khiếu nại trước");
        }

        if (invoice.Status != InvoiceStatus.Sent)
        {
            throw new InvalidStateException("Chỉ đánh dấu đã thu với hóa đơn đã gửi");
        }

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = now;
    }
}
