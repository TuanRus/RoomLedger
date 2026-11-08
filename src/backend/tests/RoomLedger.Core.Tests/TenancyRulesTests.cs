using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Core.StateMachines;

namespace RoomLedger.Core.Tests;

/**
 * Test luật hợp đồng BR-11 đến BR-14.
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyRulesTests
{
    private static readonly ContractTerms ValidTerms = new(3_500_000, 7_000_000, new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31), 3_500, 20_000);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidateTerms_AcceptsValidTerms() => TenancyService.ValidateTerms(ValidTerms);

    [Fact]
    public void ValidateTerms_RejectsEndBeforeStart()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            TenancyService.ValidateTerms(ValidTerms with { EndDate = new DateOnly(2026, 10, 1) }));
        Assert.Equal("endDate", Assert.Single(ex.Errors).Field);
    }

    [Fact]
    public void ValidateTerms_AllowsZeroDepositButNotNegative()
    {
        TenancyService.ValidateTerms(ValidTerms with { Deposit = 0 });
        Assert.Throws<ValidationException>(() => TenancyService.ValidateTerms(ValidTerms with { Deposit = -1 }));
    }

    [Fact]
    public void EditedFields_ComparesWithAiValues()
    {
        var ai = TenancyService.ParseAiTerms("""
            {"monthlyRent":{"value":3500000,"confidence":0.95},"deposit":{"value":6000000,"confidence":0.7},
             "startDate":{"value":"2026-11-01","confidence":0.9},"endDate":{"value":"2027-10-31","confidence":0.9},
             "electricityRate":{"value":3500,"confidence":0.9}}
            """);
        Assert.Equal(["deposit", "waterRate"], TenancyService.EditedFields(ValidTerms, ai));
    }

    [Fact]
    public void ParseAiTerms_ReturnsEmptyForBrokenJson() => Assert.Empty(TenancyService.ParseAiTerms("{not json"));

    [Fact]
    public void EnsureCanEditTerms_LockedVersionThrowsRecordLocked()
    {
        var ex = Assert.Throws<RecordLockedException>(() =>
            TenancyStateMachine.EnsureCanEditTerms(new TenancyVersion { Status = TenancyVersionStatus.Locked }));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Invite_RequiresSavedTerms()
    {
        var tenancy = new Tenancy();
        var ex = Assert.Throws<InvalidStateException>(() =>
            TenancyStateMachine.EnsureCanInvite(tenancy, new TenancyVersion { Status = TenancyVersionStatus.Uploaded }));
        Assert.Equal("Hãy lưu điều khoản trước khi mời người thuê", ex.Message);
    }

    [Fact]
    public void FullConfirmation_LocksVersionAndActivatesTenancy()
    {
        var tenancy = new Tenancy();
        var version = new TenancyVersion { Status = TenancyVersionStatus.TermsReviewed };
        TenancyStateMachine.EnsureCanInvite(tenancy, version);
        TenancyStateMachine.OnInvited(tenancy, version, Now);
        Assert.Equal(TenancyStatus.AwaitingTenant, tenancy.Status);
        Assert.Equal(TenancyVersionStatus.AwaitingConfirm, version.Status);

        tenancy.TenantId = Guid.NewGuid();
        TenancyStateMachine.EnsureCanConfirm(version);
        Assert.True(TenancyStateMachine.OnTenantConfirmed(tenancy, version, Now));
        Assert.Equal(TenancyVersionStatus.Locked, version.Status);
        Assert.Equal(TenancyStatus.Active, tenancy.Status);
        Assert.Throws<RecordLockedException>(() => TenancyStateMachine.EnsureCanConfirm(version));
    }

    [Fact]
    public void TermsSaved_WithTenantGoesStraightToAwaitingConfirm()
    {
        var tenancy = new Tenancy { TenantId = Guid.NewGuid(), Status = TenancyStatus.Active };
        var addendum = new TenancyVersion { Type = TenancyVersionType.Addendum };
        TenancyStateMachine.OnTermsSaved(tenancy, addendum, Now);
        Assert.Equal(TenancyVersionStatus.AwaitingConfirm, addendum.Status);
        Assert.Equal(Now, addendum.LandlordConfirmedAt);
    }

    [Fact]
    public void AddVersion_RequiresActiveTenancyAndLockedPrevious()
    {
        var latest = new TenancyVersion { Status = TenancyVersionStatus.Locked, Type = TenancyVersionType.Initial };
        Assert.Throws<InvalidStateException>(() =>
            TenancyStateMachine.EnsureCanAddVersion(new Tenancy { Status = TenancyStatus.AwaitingTenant }, latest, TenancyVersionType.Addendum));
        Assert.Equal(TenancyVersionType.Renewal,
            TenancyStateMachine.EnsureCanAddVersion(new Tenancy { Status = TenancyStatus.Active }, latest, TenancyVersionType.Renewal));
    }

    [Fact]
    public void AddVersion_AfterRejectionReplacesWithSameType()
    {
        var rejected = new TenancyVersion { Status = TenancyVersionStatus.Rejected, Type = TenancyVersionType.Initial };
        Assert.Equal(TenancyVersionType.Initial,
            TenancyStateMachine.EnsureCanAddVersion(new Tenancy { Status = TenancyStatus.AwaitingTenant }, rejected, TenancyVersionType.Addendum));
    }

    [Theory]
    [InlineData("ngắn")]
    [InlineData("          ")]
    public void RejectReason_NeedsTenCharacters(string reason) =>
        Assert.Throws<ValidationException>(() => TenancyService.ValidateRejectReason(reason));
}
