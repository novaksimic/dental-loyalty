# ADR-001: Loyalty Point Model & Authoritative Business Rules

## Status

Accepted

## Context

Two documents in `docs/` define conflicting loyalty numbers for the MVP:

- `PROJECT PILLAR.md` §8 describes: 100 RSD = 1 point, a maximum effective progress of 1,000 points, and Silver/Gold/Platinum tiers requiring 1/3/5 referrals with unnamed rewards.
- `BUSINESS.md` §3–§13, `BACKEND & API ARCHITECTURE.md` §10/§14/§17, and `FRONTEND ARCHITECTURE.md` §12/§17 describe: 100 RSD = 1,000 points, a maximum progress of 1,000,000 points, a referral bonus of 50,000 points to both the referring and referred patient (maximum 5 referral bonuses = 250,000 points), and three tiers with six specifically named rewards.

`BUSINESS.md` §26 explicitly self-declares as "the Business Pillar source of truth for the MVP implementation," and three of the four documents already agree with its numbers. No code currently implements either model.

## Decision

`BUSINESS.md` is the authoritative MVP business source of truth for the loyalty point model. The implementation must use:

- **100 RSD = 1,000 points** as the base service scoring rule.
- **1,000,000 points** as the maximum loyalty progress target.
- **50,000 points** awarded to both the referrer and the referred patient for each valid referral.
- **A maximum of 5 referral bonuses** per patient (250,000 points ceiling from referral activity).
- The current reward/tier definitions from `BUSINESS.md` §6–§8 (Tier 1: 300,000/400,000/500,000 points + 1 referral; Tier 2: 600,000/700,000 points + 3 referrals; Tier 3: 1,000,000 points + 5 referrals), including the specific named rewards listed there.

`PROJECT PILLAR.md` §8 is treated as superseded on this topic. It should be annotated to point to this ADR rather than rewritten wholesale.

## Consequences

- No rework required in `BACKEND & API ARCHITECTURE.md`, `BUSINESS.md`, or `FRONTEND ARCHITECTURE.md` — they already describe this model.
- `PROJECT PILLAR.md` §8 requires an annotation marking it superseded by this ADR to prevent future confusion.
- Domain entities representing points/thresholds (`LoyaltyAccount`, `Reward`) must be designed for six-figure-to-seven-figure integer values; existing `int` fields have sufficient range and require no type change.
- Reward seed/default data for a newly provisioned clinic must use the `BUSINESS.md` tier definitions, not the `PROJECT PILLAR.md` Silver/Gold/Platinum thresholds.
- Any future product renaming (e.g., presenting tiers as "Silver/Gold/Platinum" in the UI) is a presentation-layer decision only and does not change the underlying point/referral thresholds fixed by this ADR.

## Options Considered

| Option | Outcome |
|---|---|
| A — Adopt `BUSINESS.md` numbers | **Selected.** Matches majority of docs and self-declared authority. |
| B — Adopt `PROJECT PILLAR.md` numbers | Rejected — would require rewriting three other pillar documents with no identified benefit. |
| C — Treat both as placeholders pending a finalized clinic price/reward list | Rejected for MVP — would block all loyalty-engine implementation indefinitely. |
