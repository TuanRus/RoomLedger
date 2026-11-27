using System.Collections.Concurrent;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.Config;

/**
 * IConfigProvider trên SSM Parameter Store (tham số Standard, String). Tên được ghép sau tiền tố
 * (ví dụ /roomledger/dev) nên mỗi stage đọc đúng bộ tham số của mình. Giá trị được nhớ 5 phút trong môi trường
 * Lambda đang chạy: sửa prompt trên Parameter Store có hiệu lực sau tối đa 5 phút, không cần deploy.
 *
 * @author Tuấn
 * @version 1.0
 */
public class SsmConfigProvider(IAmazonSimpleSystemsManagement ssm, string prefix, TimeProvider? clock = null) : IConfigProvider
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _cache = new();

    public async Task<string> GetAsync(string name, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        if (_cache.TryGetValue(name, out var hit) && hit.ExpiresAt > now)
        {
            return hit.Value;
        }

        var response = await ssm.GetParameterAsync(new GetParameterRequest { Name = $"{prefix.TrimEnd('/')}/{name.TrimStart('/')}" }, ct);
        var value = response.Parameter.Value;
        _cache[name] = (value, now + CacheFor);
        return value;
    }
}
