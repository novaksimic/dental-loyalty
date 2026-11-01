using DentalLoyalty.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

// Recognises a failed save that was caused by the service occurrence already
// existing - a repeated or concurrent submission - without depending on
// provider-specific error codes: after the failure, the database is asked
// whether the occurrence id is now taken.
internal static class ServiceOccurrenceDuplicates
{
    public static async Task<DuplicateServiceOccurrenceException?> DetectAsync(
        DentalLoyaltyDbContext context,
        Guid patientServiceId,
        DbUpdateException exception,
        CancellationToken cancellationToken)
    {
        var exists = await context.PatientServices
            .AsNoTracking()
            .AnyAsync(ps => ps.Id == patientServiceId, cancellationToken);

        return exists
            ? new DuplicateServiceOccurrenceException(
                "This service occurrence has already been recorded. Nothing was saved.",
                exception)
            : null;
    }
}
