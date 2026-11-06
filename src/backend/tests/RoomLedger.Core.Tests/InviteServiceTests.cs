using RoomLedger.Core.Common;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Test luật lời mời BR-04, BR-12.
 *
 * @author Tuấn
 * @version 1.0
 */
public class InviteServiceTests
{
    [Fact]
    public void GenerateToken_StoresOnlyHashOf32RandomBytes()
    {
        var a = InviteService.GenerateToken();
        var b = InviteService.GenerateToken();
        Assert.NotEqual(a.Token, b.Token);
        Assert.Equal(43, a.Token.Length);
        Assert.Equal(44, a.TokenHash.Length);
        Assert.Equal(a.TokenHash, InviteService.HashToken(a.Token));
        Assert.DoesNotContain(a.Token, a.TokenHash);
    }

    [Theory]
    [InlineData("A@X.com ", "a@x.com")]
    [InlineData("minh.tran@gmail.com", "minh.tran@gmail.com")]
    public void NormalizeEmail_LowercasesAndTrims(string input, string expected) =>
        Assert.Equal(expected, InviteService.NormalizeEmail(input));

    [Theory]
    [InlineData("khong-co-a-cong")]
    [InlineData("a@localhost")]
    [InlineData("")]
    public void NormalizeEmail_RejectsInvalidEmail(string input)
    {
        var ex = Assert.Throws<BadRequestException>(() => InviteService.NormalizeEmail(input));
        Assert.Equal(ErrorCodes.InvalidEmail, ex.ErrorCode);
    }

    [Fact]
    public void MaskEmail_HidesMiddleOfLocalPart() =>
        Assert.Equal("m*******n@gmail.com", InviteService.MaskEmail("minh.tran@gmail.com"));

    [Fact]
    public void IsUsable_FalseWhenUsedRevokedOrExpired()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(InviteService.IsUsable(null, null, now.AddHours(1), now));
        Assert.False(InviteService.IsUsable(now, null, now.AddHours(1), now));
        Assert.False(InviteService.IsUsable(null, now, now.AddHours(1), now));
        Assert.False(InviteService.IsUsable(null, null, now, now));
    }

    [Fact]
    public void EnsureEmailMatches_IgnoresCaseButRequiresVerifiedEmail()
    {
        InviteService.EnsureEmailMatches("A@x.com", true, "a@x.com");
        var mismatch = Assert.Throws<ForbiddenException>(() => InviteService.EnsureEmailMatches("b@y.com", true, "a@x.com"));
        Assert.Equal(ErrorCodes.InviteEmailMismatch, mismatch.ErrorCode);
        Assert.Throws<ForbiddenException>(() => InviteService.EnsureEmailMatches("a@x.com", false, "a@x.com"));
    }
}
