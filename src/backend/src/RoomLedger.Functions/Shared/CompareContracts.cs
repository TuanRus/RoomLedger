using RoomLedger.Ai.Compare;
using RoomLedger.Core.Services;

namespace RoomLedger.Functions.Shared;

/**
 * Dữ liệu đi giữa các bước Step Functions so ảnh trả phòng (MD-06-FE-02). Lambda dùng DefaultLambdaJsonSerializer nên
 * tên thuộc tính giữ PascalCase; định nghĩa state machine trong WorkflowStack tham chiếu đúng các tên này
 * ($.InspectionId, $.Pairs, $.ToCompare, $.MoveOutPhotoId, $.Persisted…).
 *
 * @author Sang
 * @version 1.0
 */
public sealed record CompareStart(Guid InspectionId, Guid MoveInInspectionId, int Threshold, int MaxCalls);

/** Kết quả plan-compare. Proceed = false: biên bản không còn COMPARING, các bước sau không làm gì. */
public sealed record ComparePlan(bool Proceed, Guid InspectionId, Guid TenancyId, IReadOnlyList<PlannedPair> Pairs,
    IReadOnlyList<CompareTask> ToCompare);

/** Một phần tử của bước Map: hai khóa bản xem JPEG trong bucket derived. */
public sealed record CompareTask(Guid MoveOutPhotoId, Guid MoveInPhotoId, string AreaName, string MoveInKey, string MoveOutKey);

/**
 * Kết quả compare-pairs. Bước Catch của Map tạo phần tử chỉ có MoveOutPhotoId và Status = NOT_ANALYZED, nên
 * Findings có thể null khi đọc lại.
 */
public sealed record PairResult(Guid MoveOutPhotoId, string Status, IReadOnlyList<AiFinding>? Findings, string? ModelId, bool UsedFallback)
{
    public const string Analyzed = "ANALYZED";
    public const string NotAnalyzed = "NOT_ANALYZED";
}

/** Đầu vào persist-results: cặp từ plan-compare và kết quả của Map (có thể thiếu phần tử). */
public sealed record PersistInput(Guid InspectionId, IReadOnlyList<PlannedPair> Pairs, IReadOnlyList<PairResult?> Results);

/** Số đếm cho bước PutMetricData và sự kiện MoveOutCompared. Persisted = false: đã ghi trước đó hoặc không còn COMPARING. */
public sealed record PersistOutput(bool Persisted, Guid InspectionId, Guid TenancyId, int Analyzed, int Suggested, int Skipped,
    int NotAnalyzed, int UsedFallback);
