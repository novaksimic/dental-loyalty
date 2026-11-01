using System.Reflection;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

// LoyaltyCycle.StartFirst always starts a cycle now, as registration does. Tests
// about cycle age and expiry move the start into the past through the private
// setter, without giving production code a way to backdate a cycle.
public static class LoyaltyCycleTestExtensions
{
    private static readonly PropertyInfo StartedAt =
        typeof(LoyaltyCycle).GetProperty(nameof(LoyaltyCycle.StartedAt))!;

    public static LoyaltyCycle StartedOn(this LoyaltyCycle cycle, DateTime startedAt)
    {
        StartedAt.SetValue(cycle, startedAt);
        return cycle;
    }
}
