# ADR-008: Loyalty Cycle Lifecycle

## Status

Accepted (product owner decisions 1-2, 2026-10-31; the rest are implementation decisions)

## Context

`BUSINESS.md` §5.2-§5.4 define how a loyalty cycle ends - successful completion when all three conditions are met, otherwise expiry one year after its start - and that the next cycle starts on the actual end date, resetting active points and referral progress, recalculating reward availability, and preserving all history. §14 and §5.4 require the end and the reset to be recorded in loyalty history. §13.1 and §19 require a completion caused by a redemption to be part of the same atomic operation.

Two points needed for an implementation were not settled by the documents:

- What happens to activity that took place in a cycle that has since ended but is recorded only afterwards (a service performed, or a referral examination done, before the end).
- Whether completion condition 3 ("redeemed every reward they unlocked during the cycle") is blocked by an unlocked reward the clinic stopped offering before the patient redeemed it.

## Decision

### Business decisions

1. **Late activity is not credited.** Activity belongs to the cycle in which it happened. A service performed in a cycle that has ended is recorded for history without points; a referral examined in a patient's cycle that has ended gives that patient nothing (`ReferralRewardOutcome.CycleEnded`). This is per patient: the other patient of the referral is unaffected. It is distinct from the §4.2 forfeiture, which applies only when a patient was not registered and linked at the examination (registration is the start of the patient's first cycle).
2. **A withdrawn reward does not block completion.** Condition 3 counts only rewards the patient can still redeem: the cycle can complete when no reward is currently *Available* to the patient. A reward that was never unlocked (for example, one requiring more referrals than can be earned) does not block it either.

### Implementation decisions

3. **Completion conditions** (`LoyaltyCycle.IsCompletedBy`): the cycle is active and not expired, the points balance is at 1,000,000, the referral balance is at 5 (both are per-cycle, ADR-007), and no reward is Available. Balances never decrease within a cycle (§13), so every reward unlocked during the cycle is still unlocked when this is evaluated.
4. **One place for the rules.** `LoyaltyCycleLifecycle` (Domain) is the only code that ends a cycle and starts the next one. `LoyaltyCycleProgression` (Application) applies it for every use case that reads or changes loyalty state - service recording, referral validation, reward redemption, and the rewards view - so no handler has its own lifecycle rules.
5. **Completion is part of the triggering operation.** After an operation changes an account or records a redemption, completion is evaluated and, if it happens, saved in the same database transaction as the operation (§5.2, §13.1, §19).
6. **Expiry is processed by the next operation for the patient, and saved on its own.** There is no background job. When an operation finds the patient's active cycle expired, it ends it as `Expired` with `EndedAt` = start + 1 year (the actual end, not the processing time) and starts the next cycle at that moment. A patient inactive for longer has every missed cycle expired in turn. Because the expiry happened at that moment whatever triggered its processing, it is committed atomically on its own before the operation; it stays recorded even if the operation is then rejected or fails. Read-only views apply the same rollover to the loaded state without saving it.
7. **The reset is a history record.** Each ended cycle gets one `CycleReset` loyalty transaction recording the negated balances, linked to the cycle (`LoyaltyTransactions.LoyaltyCycleId`, unique), timestamped at the cycle's end. The account's balances are set to zero; no existing transaction, redemption, referral or cycle is modified or deleted. Redemptions stay linked to the cycle they were made in.
8. **Concurrency.** Every cycle transition updates the patient's account, so the account rowversion (ADR-005) serialises transitions with each other and with every other loyalty operation. The database additionally allows one active cycle per patient and one `CycleReset` per cycle; a violation is reported as a concurrency conflict (409). Service recording now also checks the account's version when it changes no balance (at the maximum), as redemption and referral validation already did: whether the service earned in the current cycle depends on state a concurrent completion can end. ADR-005 §2 permits, but does not require, such checks.
9. **Atomic transition saves.** A single `SaveChanges` does not guarantee that the ended cycle is updated before the next one is inserted, which the one-active-cycle index requires. A save that includes a transition therefore runs two `SaveChanges` in one explicit database transaction (as ADR-005 §3 allows): the operation's changes and the ended cycles first, the next cycles and their resets second. Before commit, the saved accounts' rowversions are read back while the transaction still holds their rows, so the same unit of work can save the account again.

## Consequences

- `LoyaltyCycleStatus` gains `Completed` and `Expired`; `LoyaltyCycle` gains `EndedAt`; `LoyaltyTransactionType` gains `CycleReset`. Migration `AddLoyaltyCycleLifecycle` is additive; existing cycles remain active and are rolled over by the next operation if they have expired.
- The `LoyaltyCycleExpired` rejections of service recording, redemption and referral validation are removed: an expired cycle is now ended and the operation proceeds in the next cycle.
- Refines ADR-007: the registration requirement is evaluated against the start of the patient's first cycle; whether a registered patient is credited depends on their current cycle having been running at the examination (decision 1).
- Until a patient's next operation, the stored state of an expired cycle is stale; every read path that presents loyalty state must apply the rollover (as the rewards view does).
