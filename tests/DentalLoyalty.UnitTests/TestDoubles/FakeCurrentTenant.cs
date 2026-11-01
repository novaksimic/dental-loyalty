using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class FakeCurrentTenant : ICurrentTenant
{
    public static FakeCurrentTenant ForClinic(Guid clinicId) => new() { ClinicId = clinicId };

    public static FakeCurrentTenant NoTenant() => new();

    public bool HasTenant => ClinicId is not null;

    public Guid? ClinicId { get; private init; }
}
