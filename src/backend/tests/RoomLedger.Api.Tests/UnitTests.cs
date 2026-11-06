using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Infrastructure.S3;

namespace RoomLedger.Api.Tests;

/**
 * Test đơn vị của phần API không cần database: đọc claim, cursor, presigned URL.
 *
 * @author Tuấn
 * @version 1.0
 */
public class UnitTests
{
    [Theory]
    [InlineData("[admin, landlord]", UserRole.Admin)]
    [InlineData("landlord", UserRole.Landlord)]
    [InlineData("tenant,landlord", UserRole.Landlord)]
    [InlineData("tenant", UserRole.Tenant)]
    [InlineData("", null)]
    public void CognitoGroups_ParseAllFormats(string claim, UserRole? expected) =>
        Assert.Equal(expected, CurrentUser.ResolveRole(CurrentUser.ParseGroups([claim])));

    [Fact]
    public void Cursor_RoundTripsAndRejectsGarbage()
    {
        var c = new PageCursor(new DateTimeOffset(2026, 11, 6, 8, 30, 0, TimeSpan.Zero), UuidV7.NewGuid());
        Assert.Equal(c, PageCursor.Decode(c.Encode()));
        Assert.Throws<ValidationException>(() => PageCursor.Decode("không-phải-cursor"));
    }

    [Fact]
    public void PresignedPut_SignsChecksumHeader()
    {
        var s3 = new AmazonS3Client(new BasicAWSCredentials("AKIDEXAMPLE", "secret"), RegionEndpoint.APSoutheast1);
        var storage = new S3Storage(s3, Options.Create(new StorageOptions { IncomingBucket = "roomledger-dev-incoming-123456789012" }));
        var upload = storage.CreateUploadUrl("l/t/i/f.jpg", "image/jpeg", "n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=", TimeSpan.FromMinutes(5));

        Assert.StartsWith("https://", upload.Url);
        Assert.Contains("x-amz-checksum-sha256", Uri.UnescapeDataString(upload.Url));
        Assert.Equal("n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=", upload.Headers["x-amz-checksum-sha256"]);
        Assert.Equal("PUT", upload.Method);
    }
}
