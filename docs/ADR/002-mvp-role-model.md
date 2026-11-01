# ADR-002: MVP Role Model

## Status

Accepted

## Context

`PROJECT PILLAR.md` §6.2, `TECHNICAL.md` §10, and `BACKEND & API ARCHITECTURE.md` §3.1 describe a four-role model including a `ClinicStaff` role with operational-only permissions (recording loyalty activity, processing redemptions) distinct from `ClinicAdmin`.

`SECURITY.md` §5 explicitly states: *"The MVP uses three roles: PlatformAdmin, ClinicAdmin, Patient"* and *"There is no separate `ClinicStaff` role in the MVP."* `DATA.md` §4 also models only three roles. The existing `DentalLoyalty.Domain.Enums.UserRole` enum already implements only three values (`PlatformAdmin`, `ClinicAdmin`, `Patient`), consistent with `SECURITY.md`/`DATA.md` but never formally decided.

`BUSINESS.md`'s documented core business flow (§18, §25) only ever refers to "Clinic Admin confirms redemption" — it does not reference a `ClinicStaff` role anywhere.

## Decision

The MVP role model consists of exactly three roles:

- `PlatformAdmin`
- `ClinicAdmin`
- `Patient`

`ClinicStaff` is **explicitly deferred to post-MVP**. It remains a documented future concept — not deleted from the product vision — to be reconsidered once a real business need for a restricted-permission, non-admin clinic account is validated (e.g., front-desk staff who should not have full `ClinicAdmin` configuration access).

## Consequences

- No rework of the existing `UserRole` enum or of `SECURITY.md`'s RBAC matrix — both already reflect this decision.
- `PROJECT PILLAR.md` §6.2, `TECHNICAL.md` §10, and `BACKEND & API ARCHITECTURE.md` §3.1 require an annotation noting that `ClinicStaff` is deferred post-MVP per this ADR, rather than being deleted from those documents.
- For MVP, clinics with dedicated front-desk or operational staff have no restricted-permission account available; such staff must either share `ClinicAdmin` credentials or have an admin account provisioned for them. This is an accepted MVP trade-off, not an oversight.
- Any future introduction of `ClinicStaff` will require its own ADR defining its permission boundary relative to `ClinicAdmin`, since `SECURITY.md`'s current RBAC matrix has no entry for it.

## Options Considered

| Option | Outcome |
|---|---|
| A — Three roles only, matching `SECURITY.md`/`DATA.md`/code | **Selected**, with `ClinicStaff` retained as a documented future item. |
| B — Four roles, adding `ClinicStaff` now | Rejected for MVP — requires a new permission matrix and contradicts `SECURITY.md`'s explicit statement; no current business flow requires it. |
| C — Three roles, `ClinicStaff` silently dropped from product vision | Rejected — loses tracked product intent; superseded by Option A retaining it as a deferred concept. |
