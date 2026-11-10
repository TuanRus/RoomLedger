using System.Numerics;
using RoomLedger.Core.Domain;

namespace RoomLedger.Core.Services;

/** Quyết định cho một cặp ảnh: gọi Bedrock, bỏ qua (gần giống hệt) hoặc không phân tích. */
public enum PairDecision
{
    Compare,
    Skipped,
    NotAnalyzed,
}

/** Ảnh đầu vào của planner; Hash null khi không đọc được ảnh. */
public record PlannedPhoto(Guid PhotoId, Guid AreaId, ulong? Hash, DateTimeOffset UploadedAt);

/** Một cặp: ảnh trả phòng, ảnh nhận phòng được chọn (nếu có), khoảng cách và quyết định. */
public record PlannedPair(Guid MoveOutPhotoId, Guid AreaId, Guid? MoveInPhotoId, int? Distance, PairDecision Decision, string? Reason);

/**
 * Lập kế hoạch so ảnh trả phòng (BR-26 RULE 1–2, EXCEPTION 1), không gọi AWS.
 * Mỗi ảnh trả phòng ghép với ảnh nhận phòng cùng khu vực có khoảng cách Hamming nhỏ nhất (bằng nhau → ảnh gửi sớm
 * hơn). Khoảng cách &lt; ngưỡng → SKIPPED. Không có ảnh nhận phòng cùng khu vực → NOT_ANALYZED (NO_MOVE_IN_PHOTO).
 * Ảnh không đọc được hash thì không lọc được nên vẫn gửi so sánh. Giới hạn số lần gọi chia vòng tròn giữa các khu vực
 * để khu vực nào cũng được xem; phần vượt → NOT_ANALYZED (CALL_LIMIT). Kết quả theo thứ tự khu vực rồi thứ tự gửi.
 *
 * @author Sang
 * @version 1.0
 */
public static class ComparePlanner
{
    public const string NoMoveInPhoto = "NO_MOVE_IN_PHOTO";
    public const string CallLimit = "CALL_LIMIT";

    public static IReadOnlyList<PlannedPair> Plan(IReadOnlyList<InspectionArea> areas, IReadOnlyList<PlannedPhoto> moveIn,
        IReadOnlyList<PlannedPhoto> moveOut, int threshold, int maxCalls)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCalls);

        var areaOrder = areas.OrderBy(a => a.SortOrder).Select((a, i) => (a.RoomAreaId, i)).ToDictionary(x => x.RoomAreaId, x => x.i);
        int Order(Guid areaId) => areaOrder.TryGetValue(areaId, out var i) ? i : int.MaxValue;
        var candidates = moveIn.OrderBy(p => p.UploadedAt).ThenBy(p => p.PhotoId).ToLookup(p => p.AreaId);

        var pairs = moveOut
            .OrderBy(p => Order(p.AreaId)).ThenBy(p => p.UploadedAt).ThenBy(p => p.PhotoId)
            .Select(p => Pair(p, candidates[p.AreaId].ToList(), threshold))
            .ToList();

        // Chia giới hạn gọi theo vòng: lượt k lấy cặp cần so thứ k của từng khu vực.
        var allowed = pairs.Where(p => p.Decision == PairDecision.Compare)
            .GroupBy(p => p.AreaId)
            .SelectMany(g => g.Select((p, rank) => (p, rank)))
            .OrderBy(x => x.rank).ThenBy(x => Order(x.p.AreaId))
            .Take(maxCalls)
            .Select(x => x.p.MoveOutPhotoId)
            .ToHashSet();
        return pairs
            .Select(p => p.Decision == PairDecision.Compare && !allowed.Contains(p.MoveOutPhotoId)
                ? p with { Decision = PairDecision.NotAnalyzed, Reason = CallLimit }
                : p)
            .ToList();
    }

    private static PlannedPair Pair(PlannedPhoto photo, List<PlannedPhoto> sameArea, int threshold)
    {
        if (sameArea.Count == 0)
        {
            return new PlannedPair(photo.PhotoId, photo.AreaId, null, null, PairDecision.NotAnalyzed, NoMoveInPhoto);
        }

        var readable = sameArea.Where(c => c.Hash is not null).ToList();
        if (photo.Hash is null || readable.Count == 0)
        {
            return new PlannedPair(photo.PhotoId, photo.AreaId, sameArea[0].PhotoId, null, PairDecision.Compare, null);
        }

        var best = readable
            .Select(c => (c, d: BitOperations.PopCount(c.Hash!.Value ^ photo.Hash.Value)))
            .MinBy(x => x.d);   // MinBy giữ phần tử đầu khi bằng nhau → ảnh gửi sớm hơn
        var decision = best.d < threshold ? PairDecision.Skipped : PairDecision.Compare;
        return new PlannedPair(photo.PhotoId, photo.AreaId, best.c.PhotoId, best.d, decision, null);
    }
}
