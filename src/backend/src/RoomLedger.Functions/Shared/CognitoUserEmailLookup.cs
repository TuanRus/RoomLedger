using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Functions.Shared;

/**
 * IUserEmailLookup trên Cognito User Pool (ListUsers lọc theo sub): Lambda notify chạy ngoài VPC nên không đọc
 * được app_user trong RDS, nhưng Cognito truy cập được qua mạng công cộng. Chỉ lấy email đã xác thực.
 *
 * @author Tuấn
 * @version 1.0
 */
public class CognitoUserEmailLookup(IAmazonCognitoIdentityProvider cognito, string userPoolId) : IUserEmailLookup
{
    public async Task<string?> FindEmailAsync(Guid userId, CancellationToken ct)
    {
        var response = await cognito.ListUsersAsync(new ListUsersRequest
        {
            UserPoolId = userPoolId,
            Filter = $"sub = \"{userId}\"",
            Limit = 1,
        }, ct);
        var attributes = response.Users.FirstOrDefault()?.Attributes;
        var verified = attributes?.FirstOrDefault(a => a.Name == "email_verified")?.Value;
        return verified == "false" ? null : attributes?.FirstOrDefault(a => a.Name == "email")?.Value;
    }
}
