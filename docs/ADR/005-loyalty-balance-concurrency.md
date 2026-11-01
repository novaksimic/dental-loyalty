# ADR-005: Loyalty Balance Concurrency Mechanism

## Status

Accepted

## Context

ADR-004 decided that balance-changing operations use optimistic concurrency and deferred the concrete mechanism to a follow-up ADR, required before any balance-changing operation is implemented. `BACKEND & API ARCHITECTURE.md` §26 requires the simplest reliable concurrency strategy supported by SQL Server and EF Core, prevention of two simultaneous operations spending the same loyalty balance incorrectly, and verification of the current state at the time of the operation. §25 and §36 require operations that modify multiple loyalty records to be atomic.

A balance-changing operation (service points, referral rewards, reward redemption, manual adjustments) reads one or more `LoyaltyAccount`s, applies the business rules in the Domain (`BUSINESS.md` §3.3 maximum balance, §13 and §20 sufficient balance), and writes the new balance together with the `LoyaltyTransaction` that explains it (`BUSINESS.md` §14; ADR-004). Without a concurrency token, two operations that read the same account before either writes can:

- overwrite each other's balance change (lost update), leaving a transaction history that no longer explains the balance;
- apply the 1,000,000-point maximum against a stale balance, so the recorded credited points are wrong;
- spend the same points or referral credits twice.

## Decision

### 1. Concurrency token

`LoyaltyAccount` carries a SQL Server `rowversion` column, mapped with EF Core `IsRowVersion()` as the account's optimistic concurrency token. SQL Server changes it on every update of the row, so no application code has to maintain it.

### 2. What the token protects

The token detects concurrent updates to the **same `LoyaltyAccount` row**: when an operation saves an account whose row was updated by another operation after it was read, the save fails instead of overwriting that change.

Concurrency checking is tied to actual state changes. An operation that changes an account's balance or other persisted state updates that account and is therefore version-checked. An operation is not required to update an account (or its `UpdatedAt`) solely to force a version check.

A business event and an account balance mutation are distinct. A business event may still require a `LoyaltyTransaction` record when it changes no balance - for example, an earning operation whose credited points are 0 because the balance is already at the maximum (`BUSINESS.md` §3.3, §14). Whether a given event records a transaction is defined by the business rules for that event, not by this ADR.

### 3. Atomic persistence

All writes of one balance-changing business operation are committed in **one database transaction**: every affected `LoyaltyAccount`, every `LoyaltyTransaction`, and every related record the operation creates or changes. A single EF Core `SaveChanges` call provides this, because EF Core executes it in one database transaction. If an operation ever needs more than one `SaveChanges`, the use case wraps them in one explicit transaction.

This applies equally to operations that change more than one patient's account. All account updates and related records commit or roll back together; a concurrency conflict on any one of the affected accounts rolls back the entire operation, and no account, transaction or related record from it is persisted.

### 4. Conflict handling

A genuine optimistic concurrency conflict (EF Core `DbUpdateConcurrencyException`) is translated by Infrastructure into an Application-level concurrency exception, so the Application and API layers do not depend on EF Core. The API returns `409 Conflict`.

The MVP does not retry conflicting operations automatically.

### 5. Retry and resubmission safety

A concurrency conflict leaves no partial writes (§3). This guarantees only that the failed attempt changed nothing. It does not make every operation safe to resubmit: if an operation is resubmitted, it is evaluated again against the current state, and preventing a duplicate business operation is not the job of the concurrency token.

### 6. Limitation: duplicate business operations

`rowversion` prevents lost updates to an account. It does not, by itself, prevent the same business operation from being processed twice when both attempts succeed one after the other - for example, the same referral bonus being awarded twice or the same reward redemption being processed twice. Such duplicates must be prevented independently by the relevant Domain and Application checks against current state, and by database constraints where appropriate. Those checks belong to the respective referral and redemption features and are not defined by this ADR.

### 7. Unchanged boundaries

- **Domain ownership:** the Domain continues to own all business rules (maximum balance, sufficient balance, eligibility). No rule is moved into SQL statements or database locks.
- **Tenant isolation:** accounts continue to be loaded only through tenant-scoped lookups using the ClinicId from the trusted tenant context (`SECURITY.md` §6). A cross-tenant request is rejected as not found before any concurrency handling, so a 409 never reveals another tenant's data.
- **Version not exposed:** the row version is not returned to clients and not accepted from them. Conflicts are detected between concurrent server-side operations, and business rules are always re-validated against the current state (`BACKEND & API ARCHITECTURE.md` §26).

## Testing

- **Provider-independent tests** (existing test infrastructure):
  - Domain rules, unchanged by this ADR.
  - The SQL Server model maps the account's row version as a `rowversion` concurrency token.
  - The migration operations and the known migration sequence; no pending model changes.
  - Atomic persistence of an account change and its transaction records (both or neither).
- **SQL Server integration tests.** `rowversion` correctness must be established against SQL Server itself; emulation in another provider (for example a SQLite trigger) may supplement but does not replace this. Against a dedicated, disposable SQL Server test database - never the development database - verify that:
  - two independent contexts load the same `LoyaltyAccount` and both modify it; the first save succeeds and the second receives a concurrency conflict;
  - after the conflict, the account balance and the `LoyaltyTransaction` history contain exactly the result of the successful operation, with no write from the failed one;
  - for an operation that changes two accounts, a conflict on either account persists nothing from that operation.

## Consequences

- Schema change: a `rowversion` column on `LoyaltyAccounts`, and persistence of `LoyaltyTransaction` so the history is written in the same database transaction as the balance it explains.
- Lost updates, stale maximum-balance calculations, double spending through concurrent updates, and history/balance divergence are rejected instead of persisted; the losing request receives `409 Conflict`.
- Duplicate business operations remain the responsibility of the respective features (§6).
- Pessimistic locking, atomic SQL updates and distributed locks are not used.
- The test suite gains SQL Server integration tests that need a SQL Server test database.
- ADR-004's follow-up ("A separate ADR is required … concrete optimistic concurrency mechanism") is resolved by this ADR. The "Exact concurrency mechanism" item in `BACKEND & API ARCHITECTURE.md` §49 is decided here.

## Options Considered

| Option | Outcome |
|---|---|
| A - SQL Server `rowversion` optimistic concurrency token on `LoyaltyAccount` | **Selected.** Native to SQL Server and EF Core, maintained by the database, keeps business rules in the Domain. Matches ADR-004 and `BACKEND & API ARCHITECTURE.md` §26. |
| B - Application-managed concurrency token (e.g. a `Guid` changed on every mutation) | Rejected - every mutation path must remember to change it; a missed path silently disables protection. |
| C - Pessimistic locking (`UPDLOCK` / `SERIALIZABLE` around the read) | Rejected - contradicts ADR-004's optimistic decision; blocking and deadlock risk; provider-specific SQL. |
| D - Guarded atomic SQL updates (`ExecuteUpdate` with balance conditions) | Rejected - duplicates the maximum-balance and eligibility rules outside the Domain and separates the history write from the balance update. |
| E - Higher transaction isolation level alone | Rejected - produces deadlocks rather than clean, detectable conflicts. |
