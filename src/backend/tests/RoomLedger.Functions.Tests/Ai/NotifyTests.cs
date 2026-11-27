using System.Text;
using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using RoomLedger.Infrastructure.Ses;
using NotifyFunction = RoomLedger.Functions.Notify.Function;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Lambda notify (sự kiện outbox → email SES), SesNotifier và mẫu email.
 *
 * @author Tuấn
 * @version 1.0
 */
public class NotifyTests
{
    private sealed class FakeNotifier : INotifier
    {
        public List<(string Template, string To, JsonElement Model)> Sent { get; } = [];

        public Task SendAsync(string templateId, string toEmail, object model, CancellationToken ct)
        {
            Sent.Add((templateId, toEmail, (JsonElement)model));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUsers(Dictionary<Guid, string> emails) : IUserEmailLookup
    {
        public Task<string?> FindEmailAsync(Guid userId, CancellationToken ct) => Task.FromResult(emails.GetValueOrDefault(userId));
    }

    private static (NotifyFunction Function, FakeNotifier Notifier, CloudWatchEvent<S3ObjectCreatedDetail> Event) Build(
        string detailType, string detailJson, Dictionary<Guid, string>? users = null)
    {
        var store = new FakeObjectStore();
        var key = $"events/roomledger.api/{detailType}/2026-11-27/abc.json";
        store.Objects[(StorageBucket.Outbox, key)] = (Encoding.UTF8.GetBytes(detailJson), "application/json");
        var notifier = new FakeNotifier();
        var evt = new CloudWatchEvent<S3ObjectCreatedDetail>
        {
            Detail = new S3ObjectCreatedDetail(new S3BucketRef("roomledger-dev-outbox"), new S3ObjectRef(key, 100)),
        };
        return (new NotifyFunction(store, notifier, new FakeUsers(users ?? [])), notifier, evt);
    }

    [Fact]
    public async Task EventWithAnEmail_SendsToThatAddress()
    {
        var (function, notifier, evt) = Build("InviteCreated", """{"email":"a@example.com","token":"t","propertyName":"Hoa Sen","roomCode":"P.101"}""");
        await function.HandleAsync(evt, new TestLambdaContext());
        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(("InviteCreated", "a@example.com"), (sent.Template, sent.To));
    }

    [Theory]
    [InlineData("MeterReadingNeedsReview", "notifyUserId")]
    [InlineData("InvoiceSent", "tenantId")]
    [InlineData("InvoiceDisputed", "landlordId")]
    public async Task EventWithOnlyAUserId_LooksTheEmailUpInCognito(string detailType, string field)
    {
        var id = Guid.NewGuid();
        var (function, notifier, evt) = Build(detailType, $$"""{"{{field}}":"{{id}}"}""", new() { [id] = "b@example.com" });
        await function.HandleAsync(evt, new TestLambdaContext());
        Assert.Equal("b@example.com", Assert.Single(notifier.Sent).To);
    }

    [Fact]
    public async Task UnknownRecipientOrTemplate_IsSkippedWithoutFailing()
    {
        var (noUser, notifier1, evt1) = Build("InvoiceSent", $$"""{"tenantId":"{{Guid.NewGuid()}}"}""");
        await noUser.HandleAsync(evt1, new TestLambdaContext());
        var (noTemplate, notifier2, evt2) = Build("PhotoReceived", """{"email":"a@example.com"}""");
        await noTemplate.HandleAsync(evt2, new TestLambdaContext());
        Assert.Empty(notifier1.Sent);
        Assert.Empty(notifier2.Sent);
    }

    [Fact]
    public async Task SesFailure_PropagatesSoLambdaRetriesAndTheDlqCatchesIt()
    {
        var store = new FakeObjectStore();
        var key = "events/roomledger.api/InviteCreated/2026-11-27/x.json";
        store.Objects[(StorageBucket.Outbox, key)] = (Encoding.UTF8.GetBytes("""{"email":"a@example.com"}"""), "application/json");
        var ses = FakeAws<IAmazonSimpleEmailServiceV2>.Create((_, _) => throw new MessageRejectedException("Email address is not verified"), out _);
        var function = new NotifyFunction(store, new SesNotifier(ses, new SesOptions("sender@example.com", "https://app.example.com")), new FakeUsers([]));
        var evt = new CloudWatchEvent<S3ObjectCreatedDetail> { Detail = new S3ObjectCreatedDetail(new S3BucketRef("b"), new S3ObjectRef(key, 1)) };
        await Assert.ThrowsAsync<MessageRejectedException>(() => function.HandleAsync(evt, new TestLambdaContext()));
    }

    [Fact]
    public async Task SesNotifier_SendsPlainTextUtf8FromTheConfiguredSender()
    {
        var ses = FakeAws<IAmazonSimpleEmailServiceV2>.Create((_, _) => new SendEmailResponse(), out var fake);
        var notifier = new SesNotifier(ses, new SesOptions("sender@example.com", "https://app.example.com/"));
        using var model = JsonDocument.Parse("""{"token":"a b","propertyName":"Hoa Sen","roomCode":"P.101"}""");

        await notifier.SendAsync("InviteCreated", "to@example.com", model.RootElement, CancellationToken.None);

        var request = Assert.IsType<SendEmailRequest>(Assert.Single(fake.Calls).Request);
        Assert.Equal("sender@example.com", request.FromEmailAddress);
        Assert.Equal(["to@example.com"], request.Destination.ToAddresses);
        Assert.Equal("UTF-8", request.Content.Simple.Body.Text.Charset);
        Assert.Contains("phòng P.101 của Hoa Sen", request.Content.Simple.Body.Text.Data);
        Assert.Contains("https://app.example.com/invite/a%20b", request.Content.Simple.Body.Text.Data);
    }

    [Fact]
    public async Task SesNotifier_WithoutSenderOrTemplate_FailsBeforeCallingSes()
    {
        var ses = FakeAws<IAmazonSimpleEmailServiceV2>.Create((_, _) => throw new InvalidOperationException("không được gọi"), out var fake);
        using var model = JsonDocument.Parse("{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SesNotifier(ses, new SesOptions("", "https://x")).SendAsync("InvoiceSent", "a@example.com", model.RootElement, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SesNotifier(ses, new SesOptions("s@example.com", "https://x")).SendAsync("Nope", "a@example.com", model.RootElement, CancellationToken.None));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void EveryAdvertisedTemplate_Renders()
    {
        using var model = JsonDocument.Parse("{}");
        foreach (var id in EmailTemplates.Supported)
        {
            var email = EmailTemplates.Render(id, model.RootElement, "https://app.example.com");
            Assert.NotNull(email);
            Assert.False(string.IsNullOrWhiteSpace(email.Subject));
        }
    }
}
