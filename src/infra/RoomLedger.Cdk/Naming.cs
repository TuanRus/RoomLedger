namespace RoomLedger.Cdk;

/**
 * Tên dự án dùng chung để đặt tên tài nguyên AWS, theo mẫu {Project}-{stage}-{tên}.
 * Đổi tên dự án chỉ cần sửa một chỗ này.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class Naming
{
    /** Tiền tố tên tài nguyên (bucket, Lambda, User Pool, role, DB...). */
    public const string Project = "roomledger";

    /** Tên tài nguyên theo stage, ví dụ roomledger-dev-api. */
    public static string Resource(string stage, string name) => $"{Project}-{stage}-{name}";
}
