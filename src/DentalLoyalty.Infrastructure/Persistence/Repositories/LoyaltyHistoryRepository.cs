using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

// Read-only history queries. Each list is one SQL query: the names of related
// services, rewards and cycles are correlated subqueries of the projection, so
// nothing is loaded per row. Related rows are matched within the same clinic as
// well, so a reference can never surface another clinic's data. Nothing is
// tracked, so a read can never be saved back.
public sealed class LoyaltyHistoryRepository : ILoyaltyHistoryRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public LoyaltyHistoryRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<int> CountTransactionsAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyTransactions
            .AsNoTracking()
            .CountAsync(t => t.PatientId == patientId && t.ClinicId == clinicId, cancellationToken);
    }

    public async Task<IReadOnlyList<LoyaltyTransactionRecord>> ListTransactionsAsync(
        Guid patientId,
        Guid clinicId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        return await _context.LoyaltyTransactions
            .AsNoTracking()
            .Where(t => t.PatientId == patientId && t.ClinicId == clinicId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Type == LoyaltyTransactionType.CycleReset ? 1 : 0)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take)
            .Select(t => new LoyaltyTransactionRecord(
                t.CreatedAt,
                t.Type,
                t.PointsChange,
                t.ReferralChange,
                _context.Services
                    .Where(s => s.Id == t.ServiceId && s.ClinicId == clinicId)
                    .Select(s => s.Name)
                    .FirstOrDefault(),
                _context.Rewards
                    .Where(r => r.Id == t.RewardId && r.ClinicId == clinicId)
                    .Select(r => r.Name)
                    .FirstOrDefault(),
                _context.Referrals
                    .Where(r => r.Id == t.ReferralId && r.ClinicId == clinicId)
                    .Select(r => (bool?)(r.ReferrerPatientId == patientId))
                    .FirstOrDefault(),
                _context.LoyaltyCycles
                    .Where(c => c.Id == t.LoyaltyCycleId && c.ClinicId == clinicId)
                    .Select(c => (LoyaltyCycleStatus?)c.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoyaltyCycleRecord>> ListCyclesAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyCycles
            .AsNoTracking()
            .Where(c => c.PatientId == patientId && c.ClinicId == clinicId)
            .OrderByDescending(c => c.StartedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new LoyaltyCycleRecord(
                c.Id,
                c.Status,
                c.StartedAt,
                c.EndedAt,
                _context.LoyaltyTransactions
                    .Where(t => t.LoyaltyCycleId == c.Id && t.Type == LoyaltyTransactionType.CycleReset)
                    .Select(t => (int?)t.PointsChange)
                    .FirstOrDefault(),
                _context.LoyaltyTransactions
                    .Where(t => t.LoyaltyCycleId == c.Id && t.Type == LoyaltyTransactionType.CycleReset)
                    .Select(t => (int?)t.ReferralChange)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RewardRedemptionRecord>> ListRedemptionsAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.RewardRedemptions
            .AsNoTracking()
            .Where(rr => rr.PatientId == patientId && rr.ClinicId == clinicId)
            .OrderByDescending(rr => rr.RedeemedAt)
            .ThenByDescending(rr => rr.Id)
            .Select(rr => new RewardRedemptionRecord(
                rr.LoyaltyCycleId,
                _context.Rewards
                    .Where(r => r.Id == rr.RewardId && r.ClinicId == clinicId)
                    .Select(r => r.Name)
                    .FirstOrDefault(),
                rr.RedeemedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferralRecord>> ListReferralsMadeAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Referrals
            .AsNoTracking()
            .Where(r => r.ReferrerPatientId == patientId && r.ClinicId == clinicId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new ReferralRecord(
                r.Status,
                r.CreatedAt,
                r.ExaminedAt,
                r.RewardsForfeited,
                _context.LoyaltyTransactions
                    .Where(t => t.ReferralId == r.Id
                        && t.PatientId == patientId
                        && t.Type == LoyaltyTransactionType.ReferralPointsEarned)
                    .Select(t => (int?)t.PointsChange)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
