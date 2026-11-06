namespace RoomLedger.Infrastructure.S3;

/**
 * Tên bucket theo stage, đọc từ cấu hình (CDK truyền vào biến môi trường của Lambda).
 *
 * @author Tuấn
 * @version 1.0
 */
public class StorageOptions
{
    public string IncomingBucket { get; set; } = "";
    public string EvidenceBucket { get; set; } = "";
    public string DerivedBucket { get; set; } = "";
}
