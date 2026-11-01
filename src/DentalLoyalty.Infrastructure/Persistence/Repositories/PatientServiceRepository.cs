using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class PatientServiceRepository : IPatientServiceRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public PatientServiceRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.PatientServices
            .AsNoTracking()
            .AnyAsync(ps => ps.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        PatientService patientService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientService);

        // A service that earns no points must not carry any - those are saved
        // only with their transaction (ILoyaltyAccountRepository).
        if (patientService.EarnedPoints != 0)
        {
            throw new InvalidOperationException(
                "A service that earns points must be saved together with its loyalty transaction.");
        }

        await _context.PatientServices.AddAsync(patientService, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            var duplicate = await ServiceOccurrenceDuplicates.DetectAsync(_context, patientService.Id, ex, cancellationToken);

            if (duplicate is not null)
            {
                throw duplicate;
            }

            throw;
        }
    }
}
