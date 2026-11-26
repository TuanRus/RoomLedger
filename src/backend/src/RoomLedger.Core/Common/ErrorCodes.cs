namespace RoomLedger.Core.Common;

/**
 * Bảng errorCode dùng chung giữa backend và frontend (tab 4, mục 9).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidEmail = "INVALID_EMAIL";
    public const string InvalidFile = "INVALID_FILE";
    public const string InvalidPeriod = "INVALID_PERIOD";
    public const string Forbidden = "FORBIDDEN";
    public const string InviteEmailMismatch = "INVITE_EMAIL_MISMATCH";
    public const string NotFound = "NOT_FOUND";
    public const string RecordLocked = "RECORD_LOCKED";
    public const string InvalidState = "INVALID_STATE";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string RoomCodeExists = "ROOM_CODE_EXISTS";
    public const string RoomOccupied = "ROOM_OCCUPIED";
    public const string IncompleteInspection = "INCOMPLETE_INSPECTION";   // Sang: BR-18 EXCEPTION 1
    public const string NoMoveInRecord = "NO_MOVE_IN_RECORD";             // Sang: BR-25 EXCEPTION 1
    public const string PendingFindings = "PENDING_FINDINGS";             // Sang: BR-27 EXCEPTION 1
    public const string AmountExceedsDeposit = "AMOUNT_EXCEEDS_DEPOSIT";  // Sang: BR-27 EXCEPTION 2
    public const string FinalReadingMissing = "FINAL_READING_MISSING";    // Sang: BR-25 RULE 3, quyết định B2
    public const string FinalReadingUnconfirmed = "FINAL_READING_UNCONFIRMED";   // Sang: BR-29, quyết định B3
    public const string FinalInvoiceMissing = "FINAL_INVOICE_MISSING";    // Sang: BR-29 (Tuấn 10/11)
    public const string OpenDisputes = "OPEN_DISPUTES";                   // Sang: BR-29, quyết định B4
    public const string AlreadyInvoiced = "ALREADY_INVOICED";
    public const string PeriodClosed = "PERIOD_CLOSED";
    public const string QrNotUploaded = "QR_NOT_UPLOADED";
    public const string InviteInvalid = "INVITE_INVALID";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string InternalError = "INTERNAL_ERROR";
}
