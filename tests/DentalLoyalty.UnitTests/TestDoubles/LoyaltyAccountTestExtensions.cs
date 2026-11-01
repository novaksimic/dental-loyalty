using System.Reflection;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

// Reward eligibility and the referral maximum depend on both balances. Tests set
// them directly through the private setters - rather than recording services
// and validating referrals until they are reached - without giving production
// code a way to change them outside a balance-changing operation (ADR-004).
public static class LoyaltyAccountTestExtensions
{
    private static readonly PropertyInfo Points =
        typeof(LoyaltyAccount).GetProperty(nameof(LoyaltyAccount.Points))!;

    private static readonly PropertyInfo ReferralBalance =
        typeof(LoyaltyAccount).GetProperty(nameof(LoyaltyAccount.ReferralBalance))!;

    public static LoyaltyAccount WithBalances(this LoyaltyAccount account, int points, int referralBalance)
    {
        Points.SetValue(account, points);
        ReferralBalance.SetValue(account, referralBalance);
        return account;
    }
}
