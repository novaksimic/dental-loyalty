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

    public async Task<IReadOnlyList<Service>> ListForTenantAsync(
        Guid clinicId,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var services = _context.Services
            .AsNoTracking()
            .Where(s => s.ClinicId == clinicId);

        if (isActive is bool active)
        {
            services = services.Where(s => s.IsActive == active);
        }

        return await services
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        Service service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        await _context.Services.AddAsync(service, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Service service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        _context.Services.Update(service);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
