using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface IClinicRepository
{
    // A clinic is always persisted together with its scoring configuration
    // (BUSINESS.md §3.1: every new clinic starts with the default), in one save,
    // so a clinic can never exist without one.
    Task AddAsync(
        Clinic clinic,
        LoyaltyConfiguration loyaltyConfiguration,
        CancellationToken cancellationToken = default);
}
