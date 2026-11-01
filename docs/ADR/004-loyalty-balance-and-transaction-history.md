# ADR-004: Loyalty Balance & Transaction History Strategy

## Status

Accepted

## Context

`DATA.md` §2.5/§9 leaves the balance-tracking *mechanism* explicitly open: "current point balance may either be calculated from valid transactions, or maintained as current state... the MVP should choose the simpler approach." However, `BUSINESS.md` §14 ("the system must maintain a history of loyalty transactions") and `BACKEND & API ARCHITECTURE.md` §23–§24 ("must not rely only on a mutable numeric field with no history") make the *existence* of a transaction history non-optional.

The current code's `LoyaltyAccount` entity has only a bare mutable `Points` field, no referral balance field, and no transaction/history entity of any kind — contradicting the mandatory history requirement.

Concurrent balance-changing operations (e.g., two simultaneous redemption attempts) are also a documented concern (`BACKEND & API ARCHITECTURE.md` §26), but the exact concurrency mechanism is explicitly left open pending persistence hardening.

## Decision

`LoyaltyAccount` must contain the current, operational balances:

- `PointsBalance`
- `ReferralBalance`

These are the fast-read operational state used for reward-eligibility checks and Patient App display.

A `PointTransaction` / `LoyaltyTransaction` history must be introduced as an append-only record of every balance-changing event (e.g., service points earned, referral points earned, reward redemption, manual adjustment), written in the same operation as the corresponding balance update. The transaction history is the audit/history record explaining how the current balance was reached; it is not the primary read path for displaying current balances.

Optimistic concurrency will be used for balance-changing operations. The concrete mechanism (e.g., EF Core concurrency token / `RowVersion` field, retry strategy, conflict handling) is **not decided by this ADR** and will be documented in a separate ADR when persistence is hardened.

## Consequences

- `LoyaltyAccount` requires a schema change to add `ReferralBalance` (currently absent) alongside the renamed/confirmed `PointsBalance`.
- A new `PointTransaction`/`LoyaltyTransaction` entity and table must be introduced; every future use case that changes a balance (service point award, referral bonus, redemption, manual adjustment) must write both a balance update and a corresponding transaction record within one atomic operation.
- This satisfies `BUSINESS.md`'s and `BACKEND & API ARCHITECTURE.md`'s mandatory history requirement while keeping reads fast for the frequently-viewed Patient App loyalty screen, since balances remain a stored field rather than an on-the-fly aggregation.
- Redemption and referral-award use cases must be designed as atomic, multi-write operations from the outset (balance change + transaction insert), which is consistent with the atomicity requirements already documented in `BUSINESS.md` §13 and `BACKEND & API ARCHITECTURE.md` §20/§25.
- Because the concurrency mechanism is deferred, current balance-changing logic must not be implemented as production-ready until that follow-up ADR exists — implementing it now risks a lost-update race on concurrent redemptions.
- No decision is made here about whether historical `PointTransaction` rows can ever be deleted/archived; that remains governed by `DATA.md` §24 (Data Deletion) and is out of scope for this ADR.

## Options Considered

| Option | Outcome |
|---|---|
| A — Cached/stored balance fields + append-only transaction log | **Selected.** Matches the mandatory history requirement and keeps reads fast. |
| B — Fully derived balance (computed from transaction log on every read) | Rejected for MVP — turns a high-frequency read (loyalty progress screen) into an aggregation query; a performance risk the project documentation elsewhere cautions against introducing prematurely. |
| C — Status quo: balance field only, no transaction log | Rejected — directly contradicts `BUSINESS.md` §14 and `BACKEND & API ARCHITECTURE.md` §23-24's mandatory audit-trail requirement. |

## Follow-up Required

A separate ADR is required before implementing any balance-changing operation, to decide the concrete optimistic concurrency mechanism (e.g., EF Core `[Timestamp]`/`RowVersion` token, conflict-retry behavior, and how a concurrency conflict is surfaced to the caller).

> **Resolved by [ADR-005](005-loyalty-balance-concurrency.md):** SQL Server `rowversion` concurrency token on `LoyaltyAccount`, one database transaction per balance-changing business operation, no automatic retry in the MVP, and conflicts surfaced as an application-level concurrency exception / `409 Conflict`.
