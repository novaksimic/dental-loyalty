using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class ServiceRepository : IServiceRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public ServiceRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<Service?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Services
            .SingleOrDefaultAsync(
                s => s.Id == id && s.ClinicId == clinicId,
                cancellationToken);
    }
}
