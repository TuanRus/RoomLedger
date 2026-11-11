using Amazon;
using Amazon.RDS.Util;
using Npgsql;

namespace RoomLedger.Infrastructure.Persistence;

/**
 * Sinh mật khẩu tạm (RDS IAM auth token, sống 15 phút) cho Npgsql, nên không có mật khẩu
 * database trong code hay biến môi trường. Npgsql gọi lại định kỳ để làm mới.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class RdsIamTokenProvider
{
    /**
     * Gắn password provider vào data source builder.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static NpgsqlDataSourceBuilder UseRdsIamAuth(this NpgsqlDataSourceBuilder builder, string region)
    {
        var cs = builder.ConnectionStringBuilder;
        builder.UsePeriodicPasswordProvider(
            (_, _) => ValueTask.FromResult(RDSAuthTokenGenerator.GenerateAuthToken(
                RegionEndpoint.GetBySystemName(region), cs.Host!, cs.Port, cs.Username!)),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(5));
        return builder;
    }
}
