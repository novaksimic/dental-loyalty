# ADR-009: Patient Self-Registration and Account Linking

## Status

**Superseded by ADR-010** (product owner decision, 2026-11-17): patients do not self-register. Every account is created by an administrator: a PlatformAdmin creates ClinicAdmin accounts, and a ClinicAdmin creates patient accounts. Nothing in this ADR is to be implemented. The rules it shared with the approved model (email normalization, the password policy, login email matching) are restated in ADR-010. This record is kept for history only.

Originally accepted (product owner decisions D1-D11, 2026-11-08).

## Context

`BUSINESS.md` §5.1 starts a patient's first loyalty cycle when the patient "registers in the Patient App and their user account is linked to their Patient record", and grants no loyalty benefit before that moment. `PROJECT PILLAR.md` §7.1 and §14 put user registration in the MVP. No endpoint created a patient's user account: `CreatePatientUserHandler` linked a new User to a known Patient, but it was not exposed and not tenant-scoped.

Self-registration was chosen. Invitation-based registration stays deferred (ADR-007 decision 4). Two facts constrain the design:

- Proving control of an email address does not prove that a person is a patient of a clinic.
- A Patient record holds only name, phone and an optional email, so it cannot identify a person securely on its own.

## Decision

### Business decisions

1. **Self-registration, then clinic verification (D1).**
   - A patient registers with email and password at a clinic of their choice, creating a *pending* account.
   - A ClinicAdmin of that clinic verifies the patient in person and links the account to the patient's Patient record.
   - The patient must present valid identity documentation and the verification code of decision 11.
   - Only then is the account linked. This is not invitation-based registration: the clinic issues nothing before the patient registers.
2. **Clinic discovery (D2).** Patients choose from a public list of active clinics (id and name only).
3. **One email, one account, one clinic (D3).** `Users.Email` remains unique platform-wide and a User belongs to one clinic. A patient of several clinics needs a separate email for each, for now.
4. **No account enumeration (D4).** The public registration endpoint returns the same response for every well-formed request for an active clinic, whether or not the email is already used. It is rate-limited.
5. **Password policy (D5).** 10-128 characters, no composition rules, and not equal to the email (case-insensitive).
6. **Pending accounts (D6).** A pending account can sign in and see its pending-verification status, and nothing else. A ClinicAdmin can reject it. Pending accounts do not expire in the MVP.
7. **Inactive patients (D7).** A pending account cannot be linked to an inactive Patient record.
8. **Data kept for a pending account (D8).** Only the email (and password hash); no name or phone. No migration.
9. **Responsible ClinicAdmin (D9).** The ClinicAdmin who linked an account is not stored yet; it will be recorded by the audit log (`DATA.md` §19) when that is implemented. The link time is the start of the first loyalty cycle, which is already stored.
10. **Rejection deletes the pending account (D10).**
    - Rejecting a pending registration deletes that User, which frees its email for a new registration.
    - Only an active, Patient-role User of the ClinicAdmin's own clinic that no Patient is linked to can be deleted.
    - A linked User, a Patient, and any loyalty history are never deleted by this operation.
11. **Proof of pending-account control (D11).**
    - The authenticated Patient app shows the pending account's verification code. The ClinicAdmin must enter it to link the account.
    - It proves that the person at the desk controls the pending account (signed in with its password). It does not prove ownership of the email address.

### Implementation decisions

12. **Pending account.** An active `User` with role `Patient`, a `ClinicId`, and no Patient whose `UserId` is that User. No new state, column or table.
13. **Pending accounts reach no patient data.** Every patient and loyalty endpoint resolves the patient through the User's link within the token's tenant, so an unlinked User resolves to none (404). The only patient-facing operation available to a pending account is its registration status.
14. **Registration (`POST api/auth/register`, anonymous).**
    - The email is trimmed and lower-cased. The password is checked against decision 5 and hashed with the existing `IPasswordHasher`.
    - The password is hashed before the email's existence is checked, so a new and an existing email cost the same work.
    - A new email creates the pending User. An existing email - of any role, in any clinic - creates nothing.
    - A unique-index conflict from a concurrent registration is caught and answered like any other existing email.
    - Every well-formed request for an active clinic receives `202 Accepted` with the same body.
    - `400` is returned only for checks that do not depend on accounts: password policy, malformed email, unknown or inactive clinic (the clinic list is public).
15. **Verification code.**
    - The code is `HMAC-SHA256(key, "dental-loyalty/registration-verification/v1|" + UserId + "|" + ClinicId)`.
    - The key is the `Registration:VerificationKey` secret: at least 256 bits, supplied through user secrets or the environment, never committed and never sent to a client. The API refuses to start without it.
    - The first 8 bytes of the MAC, as an unsigned integer modulo 10^10, form a 10-digit code. It is shown as `NNN-NNN-NNNN` and accepted with or without separators.
    - The code is computed on demand and never stored. It is compared in constant time (`CryptographicOperations.FixedTimeEquals`).
    - It is bound to the UserId and the ClinicId, so it is useless for another account or another clinic.
    - It is returned only by the pending account's own status endpoint, never in the ClinicAdmin's pending list, error responses or logs.
    - **Single use without state:** a code is accepted only while its User is a pending account of the ClinicAdmin's clinic. After linking the User is no longer pending, and after rejection it no longer exists, so the code can no longer link anything. A new registration with the same email gets a new UserId, and so a new code.
    - Rotating the key invalidates all outstanding codes. Patients then see a new code; nothing else changes.
16. **Linking (`POST api/registrations/{userId}/link`, ClinicAdmin).**
    - The route carries the pending UserId, and the body carries the PatientId and the verification code - never a clinic.
    - The clinic is the ClinicAdmin's tenant (`ICurrentTenant`). The Patient and the pending User are both loaded within it, and the repository re-checks that User, Patient and tenant are the same clinic. Anything outside it is indistinguishable from nothing (404).
    - A wrong code is rejected without detail.
    - The link and the first loyalty cycle (`LoyaltyCycle.StartFirst`) are saved in one `SaveChanges`, so the cycle exists exactly when the link does (§5.1).
    - Concurrent links are stopped by the existing unique indexes: one active cycle per Patient, and `Patients.UserId`. The losing request is answered `409` and saves nothing.
17. **Rate limiting.** The ASP.NET Core built-in rate limiter is used, with every limit set in configuration. Each policy is separate:
    - **Registration:** per client IP address.
    - **Clinic list:** per client IP address.
    - **Login:** per client IP address.
    - **Linking, per ClinicAdmin:** bounds how many attempts one account can make overall.
    - **Linking, per pending account:** keyed by tenant and pending UserId (the route value). It bounds guessing of that one account's code, whichever ClinicAdmin of the clinic tries.

    The limits are held in memory: they are correct only for a single API instance and reset when it restarts. Several instances, or a restart, multiply the attempts an attacker gets. A distributed limiter is needed before scaling out.

    Client IP addresses are trusted only after the deployment's reverse proxy and forwarded-header configuration (`ForwardedHeadersOptions` with known proxies or networks) has been verified. Until then the address seen may be the proxy's, so all clients would share one limit.
18. **Verification codes never leak.**
    - No code, MAC, key or partial code value is written to logs, exceptions, telemetry or ClinicAdmin responses.
    - The verifier returns only a boolean and never throws with a value. A wrong code gets a fixed, generic error.
    - Neither the pending list nor the link response contains a code. No request-body logging or telemetry that would capture the link request is enabled. A test asserts that responses to wrong codes do not contain the submitted value.
19. `CreatePatientUserHandler` is removed; the new linking operation replaces it.

## Consequences

- No migration. The first cycle's `StartedAt` records when the account was linked; the responsible ClinicAdmin is recorded only once the audit log exists (decision 9).
- A patient can earn nothing between registration and linking. Earlier activity is never credited (`BUSINESS.md` §5.1). The referral registration check (§4.2, ADR-007) uses the link time.
- Because registration answers alike for new and existing emails, a patient whose email is already in use gets no feedback beyond the generic message.
- **Account-blocking risk (accepted for the MVP).** Email ownership is not verified, so anyone can register an email they do not own.
  - Such an account stays pending and can reach no patient data.
  - But the real owner can no longer register with that email. Because of decision 4, they are not told why.
  - Repeating this across many emails, within the rate limits, is a denial-of-service against those people's registration.
  - The block ends only when a ClinicAdmin rejects (deletes) the pending account. The patient has to raise it at the clinic.
- **Email verification is required before production rollout** - a pre-production prerequisite, not part of this implementation. It must also be in place before any flow that relies on the email, such as password reset (`SECURITY.md` §8.4).
- **Login email matching (known compatibility limitation).**
  - Login and duplicate checks find a user by the email as typed or by its lower-cased form, preferring the exact match. Both are equality lookups on the unique index.
  - New accounts (stored lower-cased) can therefore sign in with any casing on every database provider. On SQL Server, the default case-insensitive collation also matches existing mixed-case emails in any casing, as before.
  - On a case-sensitive provider (SQLite, EF in-memory), an existing account stored in mixed case is found only when typed with its stored casing.
  - This is not the intended long-term behaviour. Removing it needs the stored emails normalized by a data migration, which is out of scope.
- **Rate limiting is single-instance.** It must be replaced by a distributed limiter before running more than one API instance, and forwarded headers must be configured before client addresses are relied on (decision 17).
