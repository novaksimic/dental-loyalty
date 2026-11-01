# ADR-003: LoyaltyConfiguration Scope for MVP

## Status

Accepted

## Context

`DATA.md` §7–§8 and `BACKEND & API ARCHITECTURE.md` §8 model a two-level hierarchy: `Clinic 1─1 LoyaltyProgram 1─1 LoyaltyConfiguration`, with `LoyaltyProgram` also owning the clinic's `Reward` collection. The current (skeletal) code instead attaches `Reward` and `Service` directly to `Clinic`, with no `LoyaltyProgram`-equivalent entity at all.

`DATA.md` §7 itself states MVP "should not support multiple simultaneously active loyalty programs for the same Clinic," meaning the multi-program flexibility a `LoyaltyProgram` wrapper would provide has no MVP-visible use. `TECHNICAL.md` §2.1 and `DATA.md` §2.1 both instruct avoiding unnecessary abstraction tables. At the same time, per-clinic configurable settings (point value, cycle duration) are a real, documented MVP requirement (`BUSINESS.md` §18, `DATA.md` §8) and need an explicit home.

## Decision

Use `LoyaltyConfiguration` for MVP as a direct child of `Clinic` (conceptually `Clinic 1─1 LoyaltyConfiguration`). Do **not** introduce a separate `LoyaltyProgram` abstraction/entity at this stage.

`LoyaltyConfiguration` must hold only the clinic-level loyalty settings actually required by MVP business rules, for example:

- Point value / scoring rule (per ADR-001: 100 RSD = 1,000 points, if made clinic-configurable).
- Loyalty cycle duration, if/when the annual cycle is implemented.

`LoyaltyConfiguration` must remain an explicit, bounded set of typed fields. It must not be implemented as a generic key/value rule engine, and must not attempt to generalize beyond the specific settings the MVP business rules actually call for.

`Reward` and `Service` continue to attach directly to `Clinic`, not to an intermediate `LoyaltyProgram`.

## Consequences

- Smaller data model than the documented hierarchy: one fewer join/entity on every loyalty-configuration or reward query, consistent with the "keep the database simple" principle.
- `DATA.md` §7 and `BACKEND & API ARCHITECTURE.md` §8 require an annotation noting that the `LoyaltyProgram` wrapper entity is deferred per this ADR; `LoyaltyConfiguration` itself remains as documented.
- If a genuine future requirement emerges for a clinic to run multiple concurrent loyalty programs, introducing `LoyaltyProgram` later will require a migration to insert it between `Clinic` and its existing `LoyaltyConfiguration`/`Reward`/`Service` records — an accepted future cost in exchange for MVP simplicity now.
- Because `LoyaltyConfiguration` is scoped to explicit, named fields only, adding a new configurable business rule in the future requires a schema change (new column) rather than a new generic rule row — consistent with the project's explicit rejection of a generic rule engine (`BUSINESS.md` §24, `TECHNICAL.md` §14).

## Options Considered

| Option | Outcome |
|---|---|
| A — Implement `LoyaltyProgram` + `LoyaltyConfiguration` exactly as documented | Rejected for MVP — the multi-program flexibility it provides is explicitly unused per `DATA.md` §7. |
| B — Skip `LoyaltyProgram` entirely; put config fields directly on `Clinic` | Rejected — loses the dedicated, documented `LoyaltyConfiguration` entity that two pillars treat as meaningful. |
| C — Keep `LoyaltyConfiguration` as a direct child of `Clinic`; drop only the `LoyaltyProgram` wrapper | **Selected.** Preserves the entity with real MVP behavior while dropping the layer with none. |
