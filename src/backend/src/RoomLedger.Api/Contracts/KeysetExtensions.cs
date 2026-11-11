using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Domain;

namespace RoomLedger.Api.Contracts;

/**
 * Phân trang keyset mới nhất trước: ORDER BY created_at DESC, id DESC, điều kiện (created_at, id) &lt; cursor.
 * Dùng so sánh row value của PostgreSQL nên tận dụng được index (…, created_at, id), không dùng OFFSET.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class KeysetExtensions
{
    /**
     * Áp cursor và thứ tự, lấy dư một bản ghi để biết còn trang sau.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IQueryable<T> Keyset<T>(this IQueryable<T> query, PageCursor? cursor, int limit) where T : AuditableEntity
    {
        if (cursor is not null)
        {
            query = query.Where(e => EF.Functions.LessThan(
                ValueTuple.Create(e.CreatedAt, e.Id),
                ValueTuple.Create(cursor.CreatedAt, cursor.Id)));
        }

        return query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(limit + 1);
    }
}
