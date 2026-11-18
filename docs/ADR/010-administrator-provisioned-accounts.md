# ADR-010: Administrator-Provisioned Accounts

## Status

Accepted (product owner decisions, 2026-11-17). Supersedes ADR-009.

## Context

ADR-009 proposed patient self-registration: a public registration endpoint, *pending* accounts, and in-person linking with a verification code. The product owner has since decided against self-registration for the MVP. Every account is created by an administrator:

- a PlatformAdmin creates a clinic's ClinicAdmin accounts;
- a ClinicAdmin creates the patient and the patient's user account.

ADR-009 also defined rules that do not depend on self-registration: email normalization, the password policy and how login matches an email. Those rules are already implemented (`EmailAddress`, `PasswordPolicy`, `UserRepository.GetByEmailAsync`) and are restated here so they no longer depend on a superseded decision.

`BUSINESS.md` §5.1 starts a patient's first loyalty cycle when the patient "registers in the Patient App and their user account is linked to their Patient record". Under this decision, that moment is when the ClinicAdmin-created user account is linked to the Patient record.

## Decision

1. **ClinicAdmin accounts are created by a PlatformAdmin only** (`POST /api/clinics/{clinicId}/admins`).
   - The clinic comes from the route and must exist (otherwise `404`). The request body carries only the email and password, never a clinic.
   - The new User has role `ClinicAdmin`, belongs to that clinic and is active.
   - Responses: `201` with `{ id, clinicId, email, role }`; `400` for an invalid email or a password outside the policy (field-level errors); `401` without a token; `403` for any role but PlatformAdmin; `409` when the email is already used.
   - Neither the password nor its hash is ever returned or logged.
2. **Patient accounts are created by a ClinicAdmin of the patient's clinic** and linked to the patient's Patient record.
   - The link starts the first loyalty cycle (`BUSINESS.md` §5.1). The User, the link and the cycle are saved atomically, as `PatientRepository.LinkUserAsync` already does.
   - The operation must be tenant-scoped: the clinic is the ClinicAdmin's tenant (`ICurrentTenant`), never a client-supplied value.
   - The endpoint is a separate task. `CreatePatientUserHandler` is kept as the starting point, but it is not tenant-scoped yet and must not be exposed as it is.
3. **No self-registration.** There is no public registration endpoint, no pending account state, no verification code and no `Registration:VerificationKey` secret. ClinicAdmin accounts are never self-registered.
4. **Email.** Every account email is trimmed and lower-cased before it is stored or looked up (`EmailAddress.TryNormalize`).
   - The syntax check is conservative: plain `local-part@domain` with printable ASCII, no quoted local parts, display names or IP-literal domains.
   - It proves nothing about ownership of the address.
   - `Users.Email` stays unique platform-wide, and a User belongs to at most one clinic. One email is one account. A person who is a patient of several clinics needs a separate email for each, for now.
5. **Password policy** (`PasswordPolicy`): 10-128 characters (Unicode characters, not UTF-16 code units), no composition rules, and not equal to the account's email (case-insensitive, ignoring surrounding whitespace). It applies to every account created through the API. The password is never trimmed or changed, and it is hashed with the existing `IPasswordHasher`.
6. **Duplicate emails are reported (`409`).** Only authenticated administrators can create accounts, so reporting that an email is taken does not expose accounts to the public. A concurrent request that slips past the duplicate check is stopped by the unique index and also answered `409`; it saves nothing.
7. **Login email matching (known compatibility limitation).**
   - Login and duplicate checks find a user by the email as typed or by its lower-cased form, preferring the exact match. Both are equality lookups on the unique index.
   - New accounts are stored lower-cased, so they can sign in with any casing on every database provider. On SQL Server, the default case-insensitive collation also matches existing mixed-case emails in any casing.
   - On a case-sensitive provider (SQLite, EF in-memory), an existing account stored in mixed case is found only when typed with its stored casing.
   - Removing this limitation requires a data migration that normalizes the stored emails. That is out of scope.

## Consequences

- ADR-009 is superseded in full. Its registration endpoint, pending accounts, verification codes, registration rate-limit policies and verification key are not implemented.
- The PlatformAdmin hands each new ClinicAdmin their initial password out of band. There is no password change or reset yet (`SECURITY.md` §8.4). Both are needed before production, as is email verification for any flow that relies on the email address.
- Login is not rate-limited yet. `SECURITY.md` §10.6 and §16 still require it.
- The PlatformAdmin bootstrap (`PlatformAdminBootstrapper`, configuration only) does not yet apply the email normalization or the password policy.
- A ClinicAdmin can be created for an inactive clinic. There is no clinic deactivation flow yet, and login does not check the clinic's status.
- Who created an account is not stored. It will be recorded by the audit log (`DATA.md` §19) when that is implemented.

## Options Considered

| Option | Outcome |
|---|---|
| A - Administrators create every account | **Selected.** Matches how clinics onboard patients in the MVP. There are no public account endpoints to protect against enumeration or abuse. |
| B - Patient self-registration with in-person linking (ADR-009) | Rejected by the product owner for the MVP. It needs a public endpoint, pending accounts, verification codes and email verification before production. |
