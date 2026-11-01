using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

public class LoyaltyAccountTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private static readonly LoyaltyConfiguration Scoring = LoyaltyConfiguration.CreateDefault(ClinicId);

    private readonly Patient _patient;

    private readonly LoyaltyCycle _cycle;

    // A registered, linked patient with an active first cycle (BUSINESS.md §5.1).
    public LoyaltyAccountTests()
    {
        _patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        _patient.LinkUser(Guid.NewGuid());
        _cycle = LoyaltyCycle.StartFirst(_patient);
    }

    private LoyaltyAccount NewAccount() => new(ClinicId, _patient.Id);

    // A new occurrence of a catalog service at this price, performed now.
    private PatientService RecordService(decimal amountRsd, LoyaltyConfiguration? scoring = null) =>
        new(
            Guid.NewGuid(),
            _patient,
            new Service(ClinicId, "Usluga", amountRsd),
            scoring ?? Scoring,
            _cycle,
            DateTime.UtcNow,
            AdminUserId);

    [Fact]
    public void NewAccount_StartsWithZeroPoints()
    {
        Assert.Equal(0, NewAccount().Points);
    }

    [Fact]
    public void OpenFor_TakesPatientAndClinicFromThePatient_AndStartsAtZeroPoints()
    {
        var account = LoyaltyAccount.OpenFor(_patient);

        Assert.Equal(_patient.Id, account.PatientId);
        Assert.Equal(_patient.ClinicId, account.ClinicId);
        Assert.Equal(0, account.Points);
        Assert.Null(account.UpdatedAt);
        Assert.NotEqual(Guid.Empty, account.Id);
    }

    [Fact]
    public void OpenFor_WithoutPatient_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => LoyaltyAccount.OpenFor(null!));
    }

    [Fact]
    public void NewAccount_WithEmptyIds_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new LoyaltyAccount(Guid.Empty, _patient.Id));
        Assert.Throws<ArgumentException>(() => new LoyaltyAccount(ClinicId, Guid.Empty));
    }

    [Fact]
    public void EarnServicePoints_CreditsCalculatedPointsAndRecordsTransaction()
    {
        var account = NewAccount();
        var patientService = RecordService(7_500m);

        var transaction = account.EarnServicePoints(patientService, _cycle);

        Assert.Equal(75_000, account.Points);
        Assert.Equal(LoyaltyTransactionType.ServicePointsEarned, transaction.Type);
        Assert.Equal(75_000, transaction.PointsChange);
        Assert.Equal(0, transaction.ReferralChange);
        Assert.Equal(patientService.ServiceId, transaction.ServiceId);
        Assert.Equal(patientService.Id, transaction.PatientServiceId);
        Assert.Equal(_patient.Id, transaction.PatientId);
        Assert.Equal(ClinicId, transaction.ClinicId);
        Assert.Equal(AdminUserId, transaction.CreatedBy);
        Assert.Null(transaction.RewardId);
        Assert.Null(transaction.ReferralId);
    }

    [Fact]
    public void EarnServicePoints_AccumulatesAcrossServices()
    {
        var account = NewAccount();

        account.EarnServicePoints(RecordService(10_000m), _cycle);
        account.EarnServicePoints(RecordService(25_000m), _cycle);

        Assert.Equal(350_000, account.Points);
    }

    [Fact]
    public void EarnServicePoints_ReachingExactlyTheMaximum_CreditsEverything()
    {
        var account = NewAccount();

        var transaction = account.EarnServicePoints(RecordService(100_000m), _cycle);

        Assert.Equal(LoyaltyAccount.MaximumPointsBalance, account.Points);
        Assert.Equal(1_000_000, transaction.PointsChange);
    }

    [Fact]
    public void EarnServicePoints_CrossingTheMaximum_CreditsOnlyUpToTheMaximum()
    {
        var account = NewAccount();
        account.EarnServicePoints(RecordService(90_000m), _cycle);

        var transaction = account.EarnServicePoints(RecordService(20_000m), _cycle);

        Assert.Equal(1_000_000, account.Points);
        Assert.Equal(100_000, transaction.PointsChange);
    }

    [Fact]
    public void EarnServicePoints_SingleServiceAboveTheMaximum_IsCappedAtTheMaximum()
    {
        var account = NewAccount();

        var transaction = account.EarnServicePoints(RecordService(150_000m), _cycle);

        Assert.Equal(1_000_000, account.Points);
        Assert.Equal(1_000_000, transaction.PointsChange);
    }

    [Fact]
    public void EarnServicePoints_AtTheMaximum_CreditsNothing()
    {
        var account = NewAccount();
        account.EarnServicePoints(RecordService(100_000m), _cycle);

        var transaction = account.EarnServicePoints(RecordService(100m), _cycle);

        Assert.Equal(1_000_000, account.Points);
        Assert.Equal(0, transaction.PointsChange);
    }

    // ADR-005 §2: a 0-point result is a business event with a history record,
    // not a balance mutation - the account is not artificially marked as changed.
    [Fact]
    public void EarnServicePoints_AtTheMaximum_RecordsTransactionWithoutChangingTheAccount()
    {
        var account = NewAccount();
        account.EarnServicePoints(RecordService(100_000m), _cycle);
        var updatedAtBefore = account.UpdatedAt;

        var transaction = account.EarnServicePoints(RecordService(100m), _cycle);

        Assert.Equal(LoyaltyTransactionType.ServicePointsEarned, transaction.Type);
        Assert.Equal(0, transaction.PointsChange);
        Assert.Equal(updatedAtBefore, account.UpdatedAt);
    }

    // The row version is generated by the database (ADR-005), never by Domain code.
    [Fact]
    public void NewAccount_HasNoRowVersionUntilPersisted()
    {
        var account = NewAccount();

        Assert.Empty(account.RowVersion);

        account.EarnServicePoints(RecordService(1_000m), _cycle);

        Assert.Empty(account.RowVersion);
    }

    [Fact]
    public void EarnServicePoints_OneBelowTheMaximum_CreditsExactlyOnePoint()
    {
        var account = NewAccount();
        account.EarnServicePoints(RecordService(99_999.9m), _cycle);
        Assert.Equal(999_999, account.Points);

        var transaction = account.EarnServicePoints(RecordService(100m), _cycle);

        Assert.Equal(1_000_000, account.Points);
        Assert.Equal(1, transaction.PointsChange);
    }

    // Rounding (BUSINESS.md §3.2) happens before the cap (§3.3): the rounded
    // calculated points are recorded on the service, only what fits is credited.
    [Fact]
    public void EarnServicePoints_RoundedPointsCrossingTheMaximum_CreditsOnlyUpToTheMaximum()
    {
        var account = NewAccount();
        account.EarnServicePoints(RecordService(99_999m), _cycle);
        Assert.Equal(999_990, account.Points);

        var patientService = RecordService(1_234.56m);
        var transaction = account.EarnServicePoints(patientService, _cycle);

        Assert.Equal(12_346, patientService.EarnedPoints);
        Assert.Equal(1_000_000, account.Points);
        Assert.Equal(10, transaction.PointsChange);
    }

    // The cap (BUSINESS.md §3.3) applies the same whatever ratio the clinic uses:
    // with 1 RSD = 100 points, 15,000 RSD calculates 1,500,000 points.
    [Fact]
    public void EarnServicePoints_WithClinicSpecificRatio_IsStillCappedAtTheMaximum()
    {
        var account = NewAccount();
        var clinicScoring = new LoyaltyConfiguration(ClinicId, 1m, 100);

        var patientService = RecordService(15_000m, clinicScoring);
        var transaction = account.EarnServicePoints(patientService, _cycle);

        Assert.Equal(1_500_000, patientService.EarnedPoints);
        Assert.Equal(LoyaltyAccount.MaximumPointsBalance, account.Points);
        Assert.Equal(1_000_000, transaction.PointsChange);
    }

    [Fact]
    public void EarnServicePoints_ForAnotherPatient_IsRejectedAndBalanceUnchanged()
    {
        var account = NewAccount();
        var otherPatient = new Patient(ClinicId, "Marko", "Jovanovic", "+381600000001");
        otherPatient.LinkUser(Guid.NewGuid());
        var otherCycle = LoyaltyCycle.StartFirst(otherPatient);
        var otherPatientService = new PatientService(
            Guid.NewGuid(), otherPatient, new Service(ClinicId, "Usluga", 1_000m), Scoring, otherCycle, DateTime.UtcNow, AdminUserId);

        Assert.Throws<DomainException>(() => account.EarnServicePoints(otherPatientService, otherCycle));
        Assert.Equal(0, account.Points);
    }

    // BUSINESS.md §5.1: a service the patient's cycle does not accept - here one
    // performed before the cycle started - never earns points, even if a caller
    // tries to credit it.
    [Fact]
    public void EarnServicePoints_ForServiceTheCycleDoesNotAccept_IsRejectedAndBalanceUnchanged()
    {
        var account = NewAccount();
        var beforeTheCycle = new PatientService(
            Guid.NewGuid(), _patient, new Service(ClinicId, "Usluga", 1_000m), Scoring, _cycle,
            _cycle.StartedAt.AddMinutes(-1), AdminUserId);

        Assert.Equal(0, beforeTheCycle.EarnedPoints);
        Assert.Throws<DomainException>(() => account.EarnServicePoints(beforeTheCycle, _cycle));
        Assert.Equal(0, account.Points);
    }

    [Fact]
    public void EarnServicePoints_WithAnotherPatientsCycle_IsRejectedAndBalanceUnchanged()
    {
        var account = NewAccount();
        var otherPatient = new Patient(ClinicId, "Marko", "Jovanovic", "+381600000001");
        otherPatient.LinkUser(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            account.EarnServicePoints(RecordService(1_000m), LoyaltyCycle.StartFirst(otherPatient)));
        Assert.Equal(0, account.Points);
    }

    [Fact]
    public void EarnServicePoints_ForAccountInAnotherClinic_IsRejectedAndBalanceUnchanged()
    {
        var account = new LoyaltyAccount(Guid.NewGuid(), _patient.Id);

        Assert.Throws<DomainException>(() => account.EarnServicePoints(RecordService(1_000m), _cycle));
        Assert.Equal(0, account.Points);
    }

    [Fact]
    public void EarnServicePoints_WithoutCycle_IsRejectedAndBalanceUnchanged()
    {
        var account = NewAccount();

        Assert.Throws<ArgumentNullException>(() => account.EarnServicePoints(RecordService(1_000m), null!));
        Assert.Equal(0, account.Points);
    }
}
