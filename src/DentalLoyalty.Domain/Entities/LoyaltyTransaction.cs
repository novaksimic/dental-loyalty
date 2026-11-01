using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Domain.Entities;

// Append-only record of one loyalty balance change (BUSINESS.md §14; ADR-004).
// Fields follow BUSINESS.md §14 / BACKEND & API ARCHITECTURE.md §23. Instances are
// created only by the LoyaltyAccount operation that changes the balance, so a
// balance change and its history entry cannot be produced independently.
//
// Persisted only together with the account change it explains, in one database
// transaction, and never updated or deleted (ILoyaltyAccountRepository; ADR-005).
public class LoyaltyTransaction
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    public LoyaltyTransactionType Type { get; private set; }

    // The change actually applied to the balance.
    public int PointsChange { get; private set; }

    public int ReferralChange { get; private set; }

    public Guid? RewardId { get; private set; }

    // The catalog service, where applicable (BUSINESS.md §14).
    public Guid? ServiceId { get; private set; }

    // The service occurrence this transaction credits points for. The database
    // allows at most one transaction per occurrence, so one recorded service can
    // never earn points twice.
    public Guid? PatientServiceId { get; private set; }

    public Guid? ReferralId { get; private set; }

    // For a CycleReset: the cycle whose end it records. The database allows one
    // per cycle, so a cycle can never be ended - and its progress reset - twice.
    public Guid? LoyaltyCycleId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // The user responsible for the operation, where applicable (BUSINESS.md §14).
    public Guid? CreatedBy { get; private set; }

    private LoyaltyTransaction()
    {
    }

    internal static LoyaltyTransaction ServicePointsEarned(
        LoyaltyAccount account,
        PatientService patientService,
        int pointsCredited,
        Guid createdBy)
    {
        return new LoyaltyTransaction
        {
            Id = Guid.NewGuid(),
            ClinicId = account.ClinicId,
            PatientId = account.PatientId,
            Type = LoyaltyTransactionType.ServicePointsEarned,
            PointsChange = pointsCredited,
            ReferralChange = 0,
            ServiceId = patientService.ServiceId,
            PatientServiceId = patientService.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
    }

    // BUSINESS.md §14: a redemption changes neither balance, so both changes are
    // zero; the transaction records which reward was used, when and by whom.
    internal static LoyaltyTransaction RewardRedeemed(
        LoyaltyAccount account,
        RewardRedemption redemption)
    {
        return new LoyaltyTransaction
        {
            Id = Guid.NewGuid(),
            ClinicId = account.ClinicId,
            PatientId = account.PatientId,
            Type = LoyaltyTransactionType.RewardRedeemed,
            PointsChange = 0,
            ReferralChange = 0,
            RewardId = redemption.RewardId,
            CreatedAt = redemption.RedeemedAt,
            CreatedBy = redemption.RedeemedBy,
        };
    }

    // BUSINESS.md §4.2, §14: one patient's referral reward - the points actually
    // credited (below 50,000 only at the maximum balance) and one referral
    // credit. The database allows one per patient and referral.
    internal static LoyaltyTransaction ReferralPointsEarned(
        LoyaltyAccount account,
        Referral referral,
        int pointsCredited,
        Guid createdBy)
    {
        return new LoyaltyTransaction
        {
            Id = Guid.NewGuid(),
            ClinicId = account.ClinicId,
            PatientId = account.PatientId,
            Type = LoyaltyTransactionType.ReferralPointsEarned,
            PointsChange = pointsCredited,
            ReferralChange = 1,
            ReferralId = referral.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
    }

    // BUSINESS.md §5.4, §14: the reset of the active progress when a cycle
    // ended, recorded at its end. The changes are the negated balances, so the
    // history still explains the balance; nobody is responsible for it.
    internal static LoyaltyTransaction CycleReset(
        LoyaltyAccount account,
        LoyaltyCycle endedCycle,
        int pointsChange,
        int referralChange)
    {
        return new LoyaltyTransaction
        {
            Id = Guid.NewGuid(),
            ClinicId = account.ClinicId,
            PatientId = account.PatientId,
            Type = LoyaltyTransactionType.CycleReset,
            PointsChange = pointsChange,
            ReferralChange = referralChange,
            LoyaltyCycleId = endedCycle.Id,
            CreatedAt = endedCycle.EndedAt!.Value,
        };
    }
}
