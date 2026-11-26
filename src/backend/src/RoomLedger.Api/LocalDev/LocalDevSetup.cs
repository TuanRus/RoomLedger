using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Auth;
using RoomLedger.Api.Endpoints;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.LocalDev;

/**
 * Chế độ chạy API trên máy để nối FE và test API khi chưa có Cognito, S3, EventBridge, AI:
 * đăng nhập bằng header X-Dev-User, S3/EventBridge trong bộ nhớ, và vài endpoint /local-dev cho script seed.
 * Chỉ bật khi đủ cả ba: môi trường Development, LocalDev:Enabled=true, không chạy trên Lambda.
 * Không bao giờ bật trên AWS (Lambda có AWS_LAMBDA_FUNCTION_NAME; môi trường không phải Development).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class LocalDevSetup
{
    /**
     * Có bật chế độ LocalDev không.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool IsLocalDevEnabled(this WebApplicationBuilder builder) =>
        builder.Environment.IsDevelopment()
        && builder.Configuration.GetValue<bool>("LocalDev:Enabled")
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME"));

    /**
     * Đăng ký đăng nhập giả và bản thay thế S3, EventBridge. Gọi sau AddAwsServices để ghi đè.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IServiceCollection AddLocalDev(this IServiceCollection services)
    {
        services.AddAuthentication(DevAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.Scheme, _ => { });
        services.AddSingleton<LocalStorage>();
        services.AddSingleton<IStorage>(sp => sp.GetRequiredService<LocalStorage>());
        services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<LocalStorage>());
        services.AddSingleton<LocalEventPublisher>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<LocalEventPublisher>());
        return services;
    }

    /**
     * Endpoint chỉ có ở LocalDev:
     * GET /local-dev/invites/latest?email= lấy token lời mời mới nhất (thay cho email),
     * POST /local-dev/meter-readings/{id}/ai-result ghi kết quả đọc ảnh như Lambda AI (chưa có ở local).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void MapLocalDevEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/local-dev");

        group.MapGet("/invites/latest", (string email, LocalEventPublisher events) =>
        {
            var normalized = email.Trim().ToLowerInvariant();
            var e = events.Last("InviteCreated", d => d.GetProperty("email").GetString() == normalized)
                ?? throw new NotFoundException("Chưa có lời mời cho email này");
            return ApiResults.Ok(new { token = e.GetProperty("token").GetString(), tenancyId = e.GetProperty("tenancyId").GetGuid() });
        }).RequireAuthorization(Policies.Landlord);

        group.MapPost("/meter-readings/{readingId:guid}/ai-result", async (Guid readingId, LocalAiResultRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var reading = await db.MeterReadings.FirstOrDefaultAsync(m => m.Id == readingId, ct)
                ?? throw new NotFoundException("Không tìm thấy chỉ số");
            if (reading.Status == MeterReadingStatus.Confirmed)
            {
                throw new InvalidStateException("Chỉ số đã xác nhận");
            }

            reading.AiValue = req.AiValue;
            reading.AiConfidence = req.AiConfidence;
            reading.ReviewReason = req.ReviewReason;
            reading.Status = req.NeedsReview || req.AiValue is null ? MeterReadingStatus.NeedsReview : MeterReadingStatus.PendingConfirm;
            await db.SaveChangesAsync(ct);
            return ApiResults.Ok(new { readingId, status = reading.Status });
        }).RequireAuthorization(Policies.Landlord);

        // Thay cho presigned PUT/GET của S3: không cần đăng nhập (như presigned URL), key là UUID khó đoán.
        group.MapPut("/files/{bucket}/{**key}", async (string bucket, string key, HttpRequest request, LocalStorage store, AppDbContext db, CancellationToken ct) =>
        {
            var b = ParseBucket(bucket);
            if (request.ContentLength is > MaxUploadBytes)
            {
                throw ValidationException.For("file", "File quá lớn");
            }

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            await store.PutAsync(b, key, buffer.ToArray(), request.ContentType ?? "application/octet-stream", ct);
            await MarkReceivedAsync(db, key, ct);
            return Results.Ok();
        }).AllowAnonymous();

        group.MapGet("/files/{bucket}/{**key}", async (string bucket, string key, LocalStorage store, CancellationToken ct) =>
        {
            var b = ParseBucket(bucket);
            if (!store.TryGet(b, key, out var content, out var contentType))
            {
                throw new NotFoundException("Không tìm thấy file (LocalDev giữ file trong bộ nhớ, mất khi khởi động lại API)");
            }

            return Results.File(content, contentType);
        }).AllowAnonymous();
    }

    private const long MaxUploadBytes = 25 * 1024 * 1024;

    private static StorageBucket ParseBucket(string bucket) =>
        Enum.TryParse<StorageBucket>(bucket, ignoreCase: true, out var b) ? b : throw new NotFoundException("Không có bucket này");

    /**
     * Việc S3 event + Lambda làm trên AWS khi file tới: đánh dấu ảnh đồng hồ, file hợp đồng, ảnh biên bản là RECEIVED.
     * Dùng ExecuteUpdate vì request không có người dùng (không qua audit của SaveChanges).
     *
     * @author Tuấn
     * @version 1.0
     */
    private static async Task MarkReceivedAsync(AppDbContext db, string key, CancellationToken ct)
    {
        await db.MeterReadings.IgnoreQueryFilters()
            .Where(m => m.PhotoKey == key && m.PhotoStatus == FileStatus.PendingUpload)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PhotoStatus, FileStatus.Received), ct);
        await db.TenancyFiles.IgnoreQueryFilters()
            .Where(f => f.IncomingKey == key && f.Status == FileStatus.PendingUpload)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, FileStatus.Received), ct);
        await db.Photos.IgnoreQueryFilters()
            .Where(p => p.IncomingKey == key && p.Status == FileStatus.PendingUpload)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, FileStatus.Received), ct);
    }
}

/**
 * Kết quả AI giả cho một chỉ số (chỉ dùng ở LocalDev).
 *
 * @author Tuấn
 * @version 1.0
 */
public record LocalAiResultRequest(long? AiValue, decimal? AiConfidence, bool NeedsReview, string? ReviewReason);
