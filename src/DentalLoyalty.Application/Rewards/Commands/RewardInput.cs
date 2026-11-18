namespace DentalLoyalty.Application.Rewards.Commands;

// Input normalization shared by create and update: surrounding whitespace is
// not part of a name or description, and a blank description is no
// description. Everything else is validated by Reward itself.
internal static class RewardInput
{
    public static string Name(string? name) => name?.Trim()!;

    public static string? Description(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
