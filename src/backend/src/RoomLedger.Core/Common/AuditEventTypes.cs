namespace RoomLedger.Core.Common;

/**
 * Loại sự kiện ghi vào audit_event (UPPER_SNAKE_CASE).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class AuditEventTypes
{
    public const string PropertyCreated = "PROPERTY_CREATED";
    public const string RoomCreated = "ROOM_CREATED";
    public const string RoomUpdated = "ROOM_UPDATED";
    public const string TenancyCreated = "TENANCY_CREATED";
    public const string TermsSaved = "TERMS_SAVED";
    public const string InviteCreated = "INVITE_CREATED";
    public const string InviteAccepted = "INVITE_ACCEPTED";
    public const string ContractConfirmed = "CONTRACT_CONFIRMED";
    public const string ContractRejected = "CONTRACT_REJECTED";
    public const string UploadRequested = "UPLOAD_REQUESTED";
    public const string MeterReadingConfirmed = "METER_READING_CONFIRMED";
    public const string InvoicesGenerated = "INVOICES_GENERATED";
    public const string InvoiceSent = "INVOICE_SENT";
    public const string InvoiceDisputed = "INVOICE_DISPUTED";
    public const string DisputeResolved = "DISPUTE_RESOLVED";
    public const string InvoicePaid = "INVOICE_PAID";

    // MD-04, MD-06 (Sang)
    public const string InspectionOpened = "INSPECTION_OPENED";
    public const string PhotoDeleted = "PHOTO_DELETED";
    public const string PhotoDeleteDenied = "PHOTO_DELETE_DENIED";
    public const string InspectionConfirmed = "INSPECTION_CONFIRMED";
    public const string InspectionLocked = "INSPECTION_LOCKED";
    public const string OpeningReadingsSet = "OPENING_READINGS_SET";   // Sang: BR-18 RULE 5
    public const string EvidenceSealed = "EVIDENCE_SEALED";
    public const string EvidenceHashMismatch = "EVIDENCE_HASH_MISMATCH";
}
