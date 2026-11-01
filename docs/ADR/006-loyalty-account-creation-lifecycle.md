# ADR-006: LoyaltyAccount Creation Lifecycle

## Status

Accepted

## Context

`BACKEND & API ARCHITECTURE.md` §9 states that a patient has a loyalty account representing the current loyalty state, and ADR-004 makes `LoyaltyAccount` the stored operational balance used by every balance-changing operation. ADR-005 requires those operations to load the account through tenant-scoped lookups and to persist changes against its concurrency token.

No document defined when a `LoyaltyAccount` is created. The database already enforces at most one account per Patient (unique index on `LoyaltyAccounts.PatientId`), but nothing guarantees that an account exists. Leaving creation to the first loyalty operation would make every balance-changing use case responsible for creating the account, and concurrent first operations for the same Patient would race to insert it.

## Decision

1. **Created with the Patient.** Every newly created Patient receives a `LoyaltyAccount` as part of the same business operation that creates the Patient. Accounts are not created lazily by loyalty operations.
2. **Atomic.** The Patient and its `LoyaltyAccount` are persisted in one database transaction (one `SaveChanges`, as in ADR-005 §3): both are created or neither is. A Patient without an account, or an account without its Patient, must never be persisted.
3. **Same tenant.** The account's `ClinicId` is the Patient's `ClinicId`, taken from the Patient being created - never from a separate client-supplied value (`SECURITY.md` §6).
4. **Initial state.** The new account starts with a points balance of 0 and no loyalty transactions. Creating an account is not a balance change and records no `LoyaltyTransaction`.
5. **One account per Patient.** The existing unique constraint on `LoyaltyAccounts.PatientId` is kept. A Patient has exactly one `LoyaltyAccount`.
6. **Every creation path.** The rule applies to every supported way a Patient is created, not to a particular endpoint or controller. It belongs to the Patient-creation use case so that any current or future creation path (for example, patient creation through the referral mechanism, `BUSINESS.md` §4.1) creates the account the same way.
7. **Existing Patients.** Patients created before this rule is implemented have no account. They are brought in line by an explicit, reviewed data migration that creates a zero-balance account for each Patient that has none, without changing any Patient data. Like any migration, it is reported before it is applied to a database; it is not applied implicitly.

## Consequences

- Balance-changing use cases can rely on the account existing and only load it (ADR-005); they never create it.
- Patient creation changes from a single insert to an atomic Patient + account insert.
- A data migration is needed for Patients that already exist without an account (§7).
- Balance fields that ADR-004 still requires and that are not yet implemented (`ReferralBalance`) are not defined by this ADR; the change that introduces them defines their initial value for new and existing accounts.
- Loyalty cycles (`BUSINESS.md` §5.1) are not created or changed by this ADR.

## Options Considered

| Option | Outcome |
|---|---|
| A - Create the account with the Patient, atomically | **Selected.** Every Patient always has exactly one account; loyalty operations never need to create one. |
| B - Create the account lazily on the first loyalty operation | Rejected - every balance-changing use case would need create-if-missing logic, and concurrent first operations would race on the unique `PatientId` index. |
| C - Create the account in a separate step after the Patient is saved | Rejected - a failure between the two steps leaves a Patient without an account. |
