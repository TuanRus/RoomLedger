using System.Text.Json;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.Ses;

/** Cấu hình gửi email: người gửi (identity đã xác minh trong SES) và địa chỉ web app cho đường dẫn trong email. */
public sealed record SesOptions(string Sender, string WebOrigin);

/**
 * INotifier trên Amazon SES (API v2, chữ thuần). Người gửi là một identity đã xác minh trong SES, đặt qua cấu hình
 * triển khai (không nằm trong code). Tài khoản SES mới ở chế độ sandbox: chỉ gửi được tới địa chỉ đã xác minh,
 * người nhận khác bị SES từ chối (MessageRejected) và lỗi nổi lên cho Lambda notify (vào DLQ, báo động).
 *
 * @author Tuấn
 * @version 1.0
 */
public class SesNotifier(IAmazonSimpleEmailServiceV2 ses, SesOptions options) : INotifier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /** Model là JsonElement (chi tiết sự kiện) hoặc object bất kỳ (được đổi sang JSON). */
    public async Task SendAsync(string templateId, string toEmail, object model, CancellationToken ct)
    {
        var json = model is JsonElement element ? element : JsonSerializer.SerializeToElement(model, Json);
        var email = EmailTemplates.Render(templateId, json, options.WebOrigin)
            ?? throw new ArgumentException($"Không có mẫu email '{templateId}'", nameof(templateId));
        if (string.IsNullOrWhiteSpace(options.Sender))
        {
            throw new InvalidOperationException("Chưa cấu hình người gửi SES (SES_SENDER)");
        }

        await ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = options.Sender,
            Destination = new Destination { ToAddresses = [toEmail] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = email.Subject, Charset = "UTF-8" },
                    Body = new Body { Text = new Content { Data = email.Body, Charset = "UTF-8" } },
                },
            },
        }, ct);
    }
}
