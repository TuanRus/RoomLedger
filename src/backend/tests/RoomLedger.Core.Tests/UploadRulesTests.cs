using RoomLedger.Core.Common;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Quy tắc tải ảnh (BR-16 RULE 1–2): định dạng, dung lượng, SHA-256, key S3, giới hạn ảnh mỗi khu vực.
 *
 * @author Sang
 * @version 1.0
 */
public class UploadRulesTests
{
    private static readonly string ValidSha = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/heic", "heic")]
    [InlineData("IMAGE/JPEG", "jpg")]
    public void ValidatePhoto_AcceptsJpegPngHeic_AndReturnsExtension(string contentType, string ext)
        => Assert.Equal(ext, UploadRules.ValidatePhoto(contentType, 1_000_000, ValidSha));

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void ValidatePhoto_RejectsOtherTypes(string contentType)
        => AssertInvalidFile(() => UploadRules.ValidatePhoto(contentType, 1_000_000, ValidSha));

    [Fact]
    public void ValidatePhoto_AcceptsExactly25MB()
        => Assert.Equal("jpg", UploadRules.ValidatePhoto("image/jpeg", 25L * 1024 * 1024, ValidSha));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25L * 1024 * 1024 + 1)]
    public void ValidatePhoto_RejectsEmptyOrOver25MB(long size)
        => AssertInvalidFile(() => UploadRules.ValidatePhoto("image/jpeg", size, ValidSha));

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]                                                              // 3 byte, không phải 32
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // hex, không phải base64
    public void ValidatePhoto_RequiresBase64Sha256Of32Bytes(string sha)
        => AssertInvalidFile(() => UploadRules.ValidatePhoto("image/jpeg", 1_000, sha));

    [Fact]
    public void BuildIncomingKey_FollowsLandlordTenancyInspectionPhotoLayout()
    {
        Guid landlord = Guid.NewGuid(), tenancy = Guid.NewGuid(), inspection = Guid.NewGuid(), photo = Guid.NewGuid();
        Assert.Equal($"{landlord}/{tenancy}/{inspection}/{photo}.heic",
            UploadRules.BuildIncomingKey(landlord, tenancy, inspection, photo, "heic"));
    }

    [Fact]
    public void Limits_MatchBusinessRules()
    {
        Assert.Equal(20, UploadRules.MaxPhotosPerArea);
        Assert.Equal(TimeSpan.FromMinutes(5), UploadRules.UrlLifetime);
    }

    private static void AssertInvalidFile(Action act)
    {
        var ex = Assert.Throws<BadRequestException>(act);
        Assert.Equal(ErrorCodes.InvalidFile, ex.ErrorCode);
    }
}
