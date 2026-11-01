using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Loyalty;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class LoyaltyAccountRepository : ILoyaltyAccountRepository
{
    private readonly DentalLoyaltyDbContext _context;

    // Cycle transitions waiting for the next save (TrackCycleTransition).
    private readonly List<CycleTransition> _pendingTransitions = [];

    public LoyaltyAccountRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    // Tracked, so the RowVersion read here is the one checked when the account
    // is saved (ADR-005).
    public async Task<LoyaltyAccount?> GetByPatientIdForTenantAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyAccounts
            .SingleOrDefaultAsync(
                la => la.PatientId == patientId && la.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task SaveBalanceChangesAsync(
        IReadOnlyCollection<LoyaltyAccount> accounts,
        IReadOnlyCollection<LoyaltyTransaction> transactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(transactions);

        if (accounts.Count == 0)
        {
            throw new ArgumentException("At least one account is required.", nameof(accounts));
        }

        foreach (var account in accounts)
        {
            // A detached account was not loaded by this unit of work, so the
            // version it carries was not read here and cannot be relied on.
            if (_context.Entry(account).State == EntityState.Detached)
            {
                throw new InvalidOperationException(
                    "Loyalty accounts must be loaded through this repository before they are saved.");
            }
        }

        foreach (var transaction in transactions)
        {
            if (!accounts.Any(a => a.PatientId == transaction.PatientId && a.ClinicId == transaction.ClinicId))
            {
                throw new InvalidOperationException(
                    "Every loyalty transaction must belong to one of the saved accounts.");
            }
        }

        // ADR-004: history is append-only - existing transactions are never
        // updated or deleted.
        if (_context.ChangeTracker.Entries<LoyaltyTransaction>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Loyalty transactions are append-only and cannot be modified or deleted.");
        }

        foreach (var transition in _pendingTransitions)
        {
            if (!accounts.Any(a => a.PatientId == transition.Reset.PatientId && a.ClinicId == transition.Reset.ClinicId))
            {
                throw new InvalidOperationException(
                    "The account reset by a loyalty cycle transition must be one of the saved accounts.");
            }
        }

        _context.LoyaltyTransactions.AddRange(transactions);

        var transitions = _pendingTransitions.ToList();

        try
        {
            if (transitions.Count == 0)
            {
                // One SaveChanges = one database transaction for every account
                // update, every new transaction and any other change of this
                // unit of work (ADR-005 §3). A version mismatch on any account
                // rolls back all of it.
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await SaveWithCycleTransitionsAsync(accounts, transitions, cancellationToken);
            }

            _pendingTransitions.Clear();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The loyalty account was changed by another operation. Nothing was saved.",
                ex);
        }
        catch (DbUpdateException ex) when (transitions.Count > 0)
        {
            if (await AnyCycleEndedConcurrentlyAsync(transitions, cancellationToken))
            {
                throw new ConcurrencyConflictException(
                    "The patient's loyalty cycle was changed by another operation. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }

    public void TrackCycleTransition(CycleTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        // The ended cycle is either loaded in this unit of work or the next
        // cycle of an earlier transition (a cycle that expired in turn).
        if (_context.Entry(transition.Ended).State == EntityState.Detached
            && !_pendingTransitions.Any(t => t.Next == transition.Ended))
        {
            throw new InvalidOperationException(
                "The ended loyalty cycle must be loaded through ILoyaltyCycleRepository before it is ended.");
        }

        _pendingTransitions.Add(transition);
    }

    // Ending a cycle and starting the next one in a single SaveChanges is not
    // safe: the database allows one active cycle per patient (DATA.md §14), and
    // EF Core does not order the update of the ended cycle before the insert of
    // the next one. So the operation's changes - including the ended cycles -
    // are saved first and the next cycles with their CycleReset transactions
    // second, in one database transaction: all of it or nothing.
    //
    // A cycle transition can be saved before the operation that follows it in
    // the same unit of work (LoyaltyCycleProgression), which then saves the
    // account again. Its RowVersion is therefore read back before the commit -
    // while this transaction still holds the row, so it is this save's version
    // and not a later operation's - instead of relying on the provider to return
    // it with the update.
    private async Task SaveWithCycleTransitionsAsync(
        IReadOnlyCollection<LoyaltyAccount> accounts,
        IReadOnlyList<CycleTransition> transitions,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var transition in transitions)
        {
            _context.LoyaltyCycles.Add(transition.Next);
            _context.LoyaltyTransactions.Add(transition.Reset);
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var account in accounts)
        {
            var stored = await _context.LoyaltyAccounts
                .AsNoTracking()
                .Where(a => a.Id == account.Id)
                .Select(a => a.RowVersion)
                .SingleAsync(cancellationToken);

            var rowVersion = _context.Entry(account).Property(a => a.RowVersion);
            rowVersion.CurrentValue = stored;
            rowVersion.OriginalValue = stored;
            rowVersion.IsModified = false;
        }

        await databaseTransaction.CommitAsync(cancellationToken);
    }

    // Recognised without provider-specific error codes: after the rollback, one
    // of the cycles has a CycleReset, or the patient has an active cycle other
    // than the one this operation ended - so another operation ended it first.
    private async Task<bool> AnyCycleEndedConcurrentlyAsync(
        IReadOnlyList<CycleTransition> transitions,
        CancellationToken cancellationToken)
    {
        var endedIds = transitions.Select(t => t.Ended.Id).ToList();
        var patientIds = transitions.Select(t => t.Ended.PatientId).Distinct().ToList();

        return await _context.LoyaltyTransactions
                .AsNoTracking()
                .AnyAsync(t => t.LoyaltyCycleId != null && endedIds.Contains(t.LoyaltyCycleId.Value), cancellationToken)
            || await _context.LoyaltyCycles
                .AsNoTracking()
                .AnyAsync(
                    c => c.Status == LoyaltyCycleStatus.Active
                        && patientIds.Contains(c.PatientId)
                        && !endedIds.Contains(c.Id),
                    cancellationToken);
    }

    public async Task SaveServicePointsAsync(
        PatientService patientService,
        LoyaltyAccount account,
        LoyaltyTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientService);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.PatientServiceId != patientService.Id
            || transaction.PatientId != patientService.PatientId
            || transaction.ClinicId != patientService.ClinicId)
        {
            throw new InvalidOperationException(
                "The loyalty transaction must be the one earned for this service occurrence.");
        }

        // At the maximum balance the service credits 0 points and the account
        // does not change, but whether - and in which cycle - it earned was
        // decided from the account's state. Marking it as updated makes the save
        // check its RowVersion (ADR-005), as for redemptions, so a service
        // decided before the cycle ended cannot be recorded against it.
        if (_context.Entry(account).State == EntityState.Unchanged)
        {
            _context.Entry(account).Property(a => a.Points).IsModified = true;
        }

        await _context.PatientServices.AddAsync(patientService, cancellationToken);

        try
        {
            // The occurrence is part of this unit of work, so it is saved in the
            // same database transaction as the account change and its history.
            await SaveBalanceChangesAsync([account], [transaction], cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            var duplicate = await ServiceOccurrenceDuplicates.DetectAsync(_context, patientService.Id, ex, cancellationToken);

            if (duplicate is not null)
            {
                throw duplicate;
            }

            throw;
        }
    }

    public async Task SaveRewardRedemptionAsync(
        RewardRedemption redemption,
        LoyaltyAccount account,
        LoyaltyTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redemption);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transaction);

        if (redemption.PatientId != account.PatientId
            || redemption.ClinicId != account.ClinicId
            || transaction.RewardId != redemption.RewardId
            || transaction.PatientId != redemption.PatientId
            || transaction.ClinicId != redemption.ClinicId)
        {
            throw new InvalidOperationException(
                "The loyalty transaction must be the one recorded for this redemption.");
        }

        // A redemption changes neither balance (BUSINESS.md §13), but it was
        // decided from the balances read with this account. Marking the account
        // as updated makes the save check - and advance - its RowVersion
        // (ADR-005), so a redemption decided on a stale account fails instead of
        // being recorded, and an operation that read the account before this
        // redemption fails too. A detached account is rejected below.
        if (_context.Entry(account).State == EntityState.Unchanged)
        {
            _context.Entry(account).Property(a => a.Points).IsModified = true;
        }

        await _context.RewardRedemptions.AddAsync(redemption, cancellationToken);

        try
        {
            await SaveBalanceChangesAsync([account], [transaction], cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            var redeemed = await _context.RewardRedemptions
                .AsNoTracking()
                .AnyAsync(
                    rr => rr.LoyaltyCycleId == redemption.LoyaltyCycleId && rr.RewardId == redemption.RewardId,
                    cancellationToken);

            if (redeemed)
            {
                throw new DuplicateRewardRedemptionException(
                    "This reward has already been redeemed in the patient's loyalty cycle. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }

    public async Task SaveReferralValidationAsync(
        Referral referral,
        LoyaltyAccount referrerAccount,
        LoyaltyAccount referredAccount,
        IReadOnlyCollection<LoyaltyTransaction> transactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referral);
        ArgumentNullException.ThrowIfNull(referrerAccount);
        ArgumentNullException.ThrowIfNull(referredAccount);
        ArgumentNullException.ThrowIfNull(transactions);

        if (referrerAccount.PatientId != referral.ReferrerPatientId
            || referredAccount.PatientId != referral.ReferredPatientId
            || transactions.Any(t => t.ReferralId != referral.Id))
        {
            throw new InvalidOperationException(
                "The accounts and loyalty transactions must be the ones of this referral.");
        }

        // A detached referral was not loaded by this unit of work, so its version
        // was not read here and cannot be relied on.
        if (_context.Entry(referral).State != EntityState.Modified)
        {
            throw new InvalidOperationException(
                "The referral must be loaded through IReferralRepository and validated before it is saved.");
        }

        // Whether each account was credited - or held at its referral maximum -
        // was decided from its balances, so both versions are checked (and
        // advanced) even for an account that did not change, as for redemptions.
        foreach (var account in new[] { referrerAccount, referredAccount })
        {
            if (_context.Entry(account).State == EntityState.Unchanged)
            {
                _context.Entry(account).Property(a => a.Points).IsModified = true;
            }
        }

        try
        {
            // The referral is part of this unit of work, so it is saved in the
            // same database transaction as the accounts and their history.
            await SaveBalanceChangesAsync([referrerAccount, referredAccount], transactions, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // This save was rolled back, so any transaction of the referral now in
            // the database was recorded by another operation.
            var rewarded = await _context.LoyaltyTransactions
                .AsNoTracking()
                .AnyAsync(t => t.ReferralId == referral.Id, cancellationToken);

            if (rewarded)
            {
                throw new ReferralAlreadyValidatedException(
                    "This referral's rewards have already been recorded. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }
}
