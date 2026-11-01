using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class LoyaltyCycleRepository : ILoyaltyCycleRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public LoyaltyCycleRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<LoyaltyCycle?> GetActiveForPatientAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyCycles
            .SingleOrDefaultAsync(
                lc => lc.PatientId == patientId
                    && lc.ClinicId == clinicId
                    && lc.Status == LoyaltyCycleStatus.Active,
                cancellationToken);
    }

    public async Task<DateTime?> GetRegisteredAtAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyCycles
            .AsNoTracking()
            .Where(lc => lc.PatientId == patientId && lc.ClinicId == clinicId)
            .MinAsync(lc => (DateTime?)lc.StartedAt, cancellationToken);
    }
}
