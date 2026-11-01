using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class FakeCurrentUser : ICurrentUser
{
    public static FakeCurrentUser For(Guid userId) => new() { UserId = userId };

    public static FakeCurrentUser NoUser() => new();

    public bool HasUser => UserId is not null;

    public Guid? UserId { get; private init; }
}
