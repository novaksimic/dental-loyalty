using DentalLoyalty.Application.Loyalty;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;

namespace DentalLoyalty.UnitTests.Infrastructure;

internal static class LoyaltyCycleProgressionTestFactory
{
    // The cycle lifecycle over the real repositories of one unit of work. The
    // account repository must be the one the handler saves through, since cycle
    // transitions are tracked on it.
    public static LoyaltyCycleProgression Create(DentalLoyaltyDbContext context, LoyaltyAccountRepository accounts) =>
        new(new LoyaltyCycleRepository(context), accounts, new RewardRepository(context));
}
