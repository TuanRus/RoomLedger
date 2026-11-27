using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Ses;

namespace RoomLedger.Functions.Notify;

/**
 * Gửi email qua SES khi có sự kiện nghiệp vụ (ngoài VPC; rule EventBridge của AiStack chỉ chuyển các sự kiện có mẫu
 * email). Sự kiện đến từ bucket outbox: key events/{source}/{detailType}/... cho biết loại, nội dung object là chi tiết.
 *
 * Người nhận lấy từ chi tiết sự kiện: trường email (email, tenantEmail) nếu có, không thì id người dùng
 * (notifyUserId, tenantId, landlordId) tra email trong Cognito. Không tìm được người nhận hay không có mẫu thì ghi
 * log rồi bỏ qua (thử lại cũng không có kết quả khác); lỗi SES hay Cognito ném ra để Lambda thử lại, hết lần thì
 * vào SQS DLQ và báo động. Log không ghi email người nhận.
 *
 * @author Tuấn
 * @version 1.0
 */
public class Function
{
    private static readonly string[] EmailFields = ["email", "tenantEmail"];
    private static readonly string[] UserIdFields = ["notifyUserId", "tenantId", "landlordId"];

    private readonly IObjectStore _store;
    private readonly INotifier _notifier;
    private readonly IUserEmailLookup _users;

    public Function() : this(FunctionAws.ObjectStore, FunctionAws.Notifier, FunctionAws.UserEmails)
    {
    }

    public Function(IObjectStore store, INotifier notifier, IUserEmailLookup users)
    {
        _store = store;
        _notifier = notifier;
        _users = users;
    }

    public async Task HandleAsync(CloudWatchEvent<S3ObjectCreatedDetail> input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        var evt = await OutboxEvent.ReadAsync<JsonElement>(_store, input, ct);
        var templateId = evt.DetailType;
        if (!EmailTemplates.Supported.Contains(templateId))
        {
            context.Logger.LogInformation($"Bỏ qua {templateId}: chưa có mẫu email");
            return;
        }

        var to = await ResolveRecipientAsync(evt.Detail, ct);
        if (to is null)
        {
            context.Logger.LogWarning($"{templateId}: không tìm được email người nhận, bỏ qua");
            return;
        }

        await _notifier.SendAsync(templateId, to, evt.Detail, ct);
        context.Logger.LogInformation($"Đã gửi email {templateId}");
    }

    private async Task<string?> ResolveRecipientAsync(JsonElement detail, CancellationToken ct)
    {
        if (detail.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var field in EmailFields)
        {
            if (detail.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } email)
            {
                return email;
            }
        }

        foreach (var field in UserIdFields)
        {
            if (detail.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var userId))
            {
                return await _users.FindEmailAsync(userId, ct);
            }
        }

        return null;
    }
}
