namespace DentalLoyalty.Application.Rewards.Commands;

public enum RewardCommandStatus
{
    // Created or updated; Reward is the saved definition.
    Saved = 1,

    // The caller has no trusted clinic (tenant): nothing can be created.
    NoTenant = 2,

    // No such reward in the caller's clinic - including a reward of another
    // clinic, which is indistinguishable from none.
    NotFound = 3,

    // Rejected by the Reward's own validation before anything was saved;
    // InvalidField names the offending field.
    Invalid = 4,
}

public enum RewardField
{
    Name = 1,
    Description = 2,
    RequiredPoints = 3,
    RequiredReferrals = 4,
}

public sealed record RewardCommandResult(
    RewardCommandStatus Status,
    RewardDefinitionDto? Reward = null,
    RewardField? InvalidField = null)
{
    public static RewardCommandResult Rejected(RewardCommandStatus status) => new(status);

    // Maps the Reward's validation (the single place its rules live) to the
    // offending field. An empty clinic id (a malformed tenant claim) is no
    // tenant at all.
    internal static RewardCommandResult Invalid(ArgumentException exception) =>
        exception.ParamName switch
        {
            "name" => new(RewardCommandStatus.Invalid, InvalidField: RewardField.Name),
            "description" => new(RewardCommandStatus.Invalid, InvalidField: RewardField.Description),
            "requiredPoints" => new(RewardCommandStatus.Invalid, InvalidField: RewardField.RequiredPoints),
            "requiredReferrals" => new(RewardCommandStatus.Invalid, InvalidField: RewardField.RequiredReferrals),
            _ => Rejected(RewardCommandStatus.NoTenant),
        };
}
