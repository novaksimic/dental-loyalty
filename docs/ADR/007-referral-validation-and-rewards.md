# ADR-007: Referral Validation and Referral Rewards

## Status

Accepted (product owner decisions, 2026-10-31)

## Context

`BUSINESS.md` §4 defines when a referral becomes valid (the referred new patient completes a dental examination) and its rewards (50,000 points for both patients, only if both are registered and linked at that moment; otherwise permanently forfeited for both). Four points needed for an implementation were not settled by the documents:

- §4.2 says a patient can receive "a maximum of 5 referral rewards through the referral program", while §5.2 requires five successful referrals **during each cycle** to complete it and §5.4 resets referral progress for every new cycle.
- §4.2 lists 50,000 points for both patients but does not list the referral credit (`ReferralBalance`, ADR-004) explicitly; its forfeiture clause speaks of "no referral credits and no referral progress for either patient".
- Nothing says what happens when only one of the two patients has already reached the maximum.
- §4.1's "referral mechanism" (how a referral is created and how the examination is recorded) is not specified.

## Decision

1. **The maximum is per loyalty cycle.** A patient receives at most 5 referral rewards in each loyalty cycle. Because `ReferralBalance` is the cycle's referral progress (reset with the cycle, §5.4) and redemption never reduces it (§13), it equals the number of referral rewards received in the active cycle, and the maximum is enforced against it.
2. **Both patients receive the full referral reward.** For a referral that generates rewards, the referring and the referred patient each receive 50,000 points (within the 1,000,000-point maximum balance, §3.3) **and** one referral credit, recorded as one `ReferralPointsEarned` transaction per patient. It counts toward each patient's own maximum.
3. **The maximum applies per patient.** If one patient has already reached the maximum, that patient receives nothing for the referral; the other patient is still rewarded. As with the points maximum (§3.3), the limit never rejects the operation. This is distinct from the registration and linking rule (§4.2), which remains all-or-nothing.
4. **Clinic Admin workflow for the MVP slice.** A Clinic Admin of the clinic records a referral between two existing, distinct patients of that clinic (`Pending`), and later confirms the referred patient's examination, giving its time. That confirmation makes the referral valid (`Completed`) and applies its rewards in one atomic operation. Patient-initiated referrals and invitation-based registration are deferred.

## Consequences

- The registration and linking requirement is evaluated at the examination time: each patient must have an active loyalty cycle that was running at that moment. Otherwise the referral is recorded as valid with `RewardsForfeited` and generates nothing, then or later.
- A patient is referred at most once (unique index on `Referrals.ReferredPatientId`, excluding cancelled referrals), and a referral rewards each of its patients at most once (unique index on `LoyaltyTransactions (ReferralId, PatientId)`).
- `Referral` carries a rowversion, and both accounts' versions are checked when a referral is validated (ADR-005), so concurrent validations or balance changes cannot both be saved.
- If a manual referral adjustment (`ManualReferralAdjustment`) is introduced later, it must keep `ReferralBalance` consistent with decision 1, or the per-cycle count must be tracked separately.
- Cycle completion triggered by a referral (§5.2) and cycle expiry/reset (§5.3, §5.4) are not part of this decision.
