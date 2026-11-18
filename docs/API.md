# API Reference — Implemented Endpoints

This document lists the HTTP API **as it is implemented**. It is the reference for frontend integration. Where `BACKEND & API ARCHITECTURE.md` shows illustrative routes (for example `/api/v1/...`), this document takes precedence for what actually exists.

The role matrix behind these endpoints is `SECURITY.md` §5.4–§5.5. Every route and the roles it allows are pinned by `EndpointAuthorizationInventoryTests`.

---

## 1. Conventions

### 1.1 Base path and authentication

- All routes start with `/api/`. There is no version segment.
- Every endpoint except `POST /api/auth/login` requires `Authorization: Bearer <token>`. The token comes from login.
- The token carries `userId`, `role` and, for ClinicAdmin and Patient, `clinicId`.
- **The clinic is never taken from the request** for clinic-scoped operations. It is always the caller's own clinic from the token. Any `clinicId` in a body or query string is ignored. The one exception is clinic-level provisioning by a PlatformAdmin (§2.2 and `POST /api/patients`).
- JSON uses camelCase.

### 1.2 Calling from the Angular Admin app (CORS)

The browser may call the API only from an origin listed in `Cors:AllowedOrigins` (`SECURITY.md` §17).

| Environment | Allowed origins |
|---|---|
| Development | `http://localhost:4200`, the `ng serve` default (`appsettings.Development.json`). For another port, set `Cors__AllowedOrigins__0=http://localhost:<port>`. |
| Every other environment | None by default. The deployment sets them, for example `Cors__AllowedOrigins__0=https://admin.<your-domain>`. |

The policy allows:

- methods `GET`, `POST`, `PUT` and `DELETE`;
- request headers `Authorization` and `Content-Type`;
- **no credentials**: send the token in the `Authorization` header, not as a cookie.

The app can read the `Location` and `Retry-After` response headers.

### 1.3 Status codes and error format

Every error response is a ProblemDetails (`application/problem+json`, RFC 9457):

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "traceId": "00-4bf92f…-01" }
```

`traceId` identifies the request in the server logs; quote it when reporting a problem. Error responses never contain stack traces, exception messages, SQL or submitted values.

| Code | Meaning |
|---|---|
| `200 OK` | Read succeeded, or an operation that returns a result (referral validation). |
| `201 Created` | Resource created. Body as documented per endpoint. |
| `204 No Content` | Update, deactivation or activation succeeded. Empty body. |
| `400 Bad Request` | Invalid input. Adds `"errors": { "<field>": ["message"] }` for field validation. |
| `401 Unauthorized` | No token, or an invalid or expired one. Also returned by login for any failed sign-in. |
| `403 Forbidden` | Authenticated, but the role is not allowed (or a clinic-scoped list/create is called without a clinic in the token). |
| `404 Not Found` | No such resource **in the caller's clinic** (a resource of another clinic is indistinguishable from a missing one), or no such route. |
| `409 Conflict` | The request conflicts with current state (duplicate, already done, inactive, concurrent change). `title` describes it. |
| `429 Too Many Requests` | Login rate limit exceeded (§2.1). `Retry-After` gives the seconds to wait. |
| `500 Internal Server Error` | Unexpected server error. Always the generic title "An unexpected error occurred."; details are only in the server log. |

### 1.4 Activation instead of deletion

Services and rewards are never deleted; history refers to them.

- `POST .../deactivation` withdraws one and `POST .../activation` offers it again. Both return `204` and are idempotent: repeating one changes nothing.
- Patients are deactivated with `DELETE /api/patients/{id}`, which is a soft deactivation, not a row delete. There is no patient reactivation endpoint yet.

### 1.5 Pagination

Paginated lists accept `page` (1-based, default 1) and `pageSize` (default 20, maximum 100). Values outside those ranges return `400`.

| Endpoint | Response envelope |
|---|---|
| `GET /api/patients` | `{ items, page, pageSize, totalCount, totalPages }`. `totalPages` is 0 when nothing matches. |
| `GET .../loyalty-history/transactions` | `{ page, pageSize, totalCount, items }`. No `totalPages`; the frontend computes it. |

A page beyond the last returns an empty `items` array with the same totals.

---

## 2. Identity and clinic provisioning

There is no self-registration (ADR-010). Every account is created by an administrator.

### 2.1 `POST /api/auth/login` — anonymous

Request:

```json
{ "email": "ana@clinic.rs", "password": "correct horse battery" }
```

`200` response:

```json
{ "token": "eyJ...", "userId": "guid", "role": "ClinicAdmin", "clinicId": "guid" }
```

- `clinicId` is `null` for PlatformAdmin.
- The email is matched case-insensitively (ADR-010 decision 7).
- `401` for every failure: unknown email, wrong password, inactive user, or a **patient whose patient record is deactivated** (`SECURITY.md` §9.7). The response is the same for all of them.
- **Rate-limited per client IP address** (`SECURITY.md` §16): by default 10 attempts per 60 seconds (`RateLimiting:Login:PermitLimit` / `WindowSeconds`).
  - Beyond that, the answer is `429` with `Retry-After`, whatever the credentials.
  - No other endpoint is rate-limited.

### 2.2 Clinics — PlatformAdmin only

| Method and route | Request | Success | Errors |
|---|---|---|---|
| `POST /api/clinics` | `{ "name": "string" }` | `201` `{ "id": "guid" }`. The clinic gets the default scoring configuration (100 RSD = 1,000 points) and **no rewards**. | — |
| `POST /api/clinics/{clinicId}/admins` | `{ "email", "password" }` | `201` `{ "id", "clinicId", "email", "role": "ClinicAdmin" }` | `400` invalid `Email` / `Password`; `404` unknown clinic; `409` email already used |

- The email is trimmed, lower-cased and unique platform-wide.
- The password must be 10–128 characters and must not equal the email (`SECURITY.md` §8.3).

---

## 3. Patients

### 3.1 `GET /api/patients` — ClinicAdmin

One page of the caller's clinic's patients, ordered by last name, then first name.

| Query | Meaning |
|---|---|
| `search` | Optional, at most 100 characters. Split on whitespace; **every** term must occur in the first name, last name, email or phone number. Case-insensitive on SQL Server's default collation. `%`, `_` and `[` match literally. |
| `isActive` | Optional `true`/`false`. Omitted, both are listed. |
| `page`, `pageSize` | See §1.5. |

`200` response:

```json
{
  "items": [
    {
      "id": "guid",
      "firstName": "Ana",
      "lastName": "Petrović",
      "phoneNumber": "+381641112233",
      "email": "ana@mail.rs",
      "isActive": true,
      "hasAccount": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

- `hasAccount` tells whether a user account is linked. The account itself (user id, login email) is not exposed.
- `400` for invalid `page`, `pageSize` or `search`; `403` without a clinic in the token.

### 3.2 Patient records

| Method and route | Roles | Request | Success | Errors |
|---|---|---|---|---|
| `POST /api/patients` | ClinicAdmin, PlatformAdmin | `{ "clinicId", "firstName", "lastName", "phoneNumber", "email"? }` | `201` `{ "id" }`; the patient's loyalty account is created with it (ADR-006) | `400` missing required fields |
| `GET /api/patients/{id}` | ClinicAdmin | — | `200` `{ "id", "clinicId", "firstName", "lastName", "phoneNumber", "email", "isActive" }` | `404` |
| `PUT /api/patients/{id}` | ClinicAdmin | `{ "phoneNumber", "email"? }` | `204` | `404` |
| `DELETE /api/patients/{id}` | ClinicAdmin | — | `204` (soft deactivation) | `404` |
| `GET /api/patients/me` | Patient | — | `200`, same shape as `GET /{id}` | `404` |

For `POST /api/patients`, a ClinicAdmin's `clinicId` is ignored and the patient is created in the caller's own clinic. A PlatformAdmin's `clinicId` selects the clinic.

### 3.3 `POST /api/patients/{id}/account` — ClinicAdmin

Creates the patient's login account and links it to the patient. This starts the patient's first loyalty cycle (`BUSINESS.md` §5.1).

Request:

```json
{ "email": "ana@mail.rs", "password": "at-least-ten-chars" }
```

`201` response:

```json
{ "userId": "guid", "patientId": "guid", "email": "ana@mail.rs", "role": "Patient" }
```

Errors:

- `400`: invalid `Email` or `Password`.
- `404`: no such patient in the caller's clinic.
- `409`: email already used; patient already has an account; patient is inactive; or the patient was linked concurrently.

The same email and password rules as §2.2 apply.

### 3.4 Recording a service — `POST /api/patients/{id}/services` — ClinicAdmin

Request:

```json
{ "id": "client-chosen guid", "serviceId": "guid", "performedAt": "2026-11-17T10:00:00Z" }
```

- `id` identifies the service occurrence. Resubmitting the same `id` never records or credits it twice.
- `performedAt` is optional and defaults to now.

`201` response:

```json
{ "id": "guid", "pointsAwarded": true, "earnedPoints": 75000, "creditedPoints": 75000, "cycleCompleted": false }
```

Errors:

- `400`: empty `id`, or a future `performedAt`.
- `404`: patient or service not in the caller's clinic.
- `409`: patient inactive; service inactive; occurrence already recorded; or a concurrent change.

### 3.5 Patient rewards and redemption — ClinicAdmin

| Method and route | Success | Errors |
|---|---|---|
| `GET /api/patients/{id}/rewards` | `200` `{ "points", "referralBalance", "rewards": [ { "id", "name", "description", "requiredPoints", "requiredReferrals", "status": "Locked" \| "Available" \| "Redeemed" } ] }` | `404` |
| `POST /api/patients/{id}/rewards/{rewardId}/redemptions` | `201` `{ "id", "rewardId", "redeemedAt", "cycleCompleted" }` | `404`; `409` patient inactive, reward not offered (inactive), not unlocked, already redeemed in the cycle, concurrent change |

- The rewards list contains active rewards, plus rewards redeemed in the current cycle that have since been deactivated.
- A redemption deducts no points and no referrals (`BUSINESS.md` §13).

### 3.6 Loyalty history

| Method and route | Roles |
|---|---|
| `GET /api/patients/me/loyalty-history` | Patient |
| `GET /api/patients/me/loyalty-history/transactions?page&pageSize` | Patient |
| `GET /api/patients/{id}/loyalty-history` | ClinicAdmin |
| `GET /api/patients/{id}/loyalty-history/transactions?page&pageSize` | ClinicAdmin |

History response:

```text
{
  status: { points, maximumPoints, referralBalance, maximumReferralRewards,
            activeCycle: { startedAt, expiresAt } | null,
            rewards: [ { name, description, requiredPoints, requiredReferrals, status } ] },
  cycles: [ { status, startedAt, endedAt, expiresAt, points, referrals,
              redemptions: [ { rewardName, redeemedAt } ] } ],
  referrals: [ { status, referredAt, examinedAt, rewardsForfeited, rewarded, creditedPoints } ]
}
```

Transactions response:

```text
{ page, pageSize, totalCount,
  items: [ { occurredAt, type, pointsChange, referralChange, serviceName, rewardName,
             referralRole, endedCycleStatus } ] }
```

- Both are read-only: reading never records a pending cycle expiry.
- Responses carry no internal ids.
- `serviceName` and `rewardName` are the current names of the service or reward.

---

## 4. Service catalog — ClinicAdmin only

| Method and route | Request | Success | Errors |
|---|---|---|---|
| `GET /api/services?isActive=` | — | `200` `[ ServiceDto ]`, ordered by name | `403` without clinic |
| `GET /api/services/{id}` | — | `200` `ServiceDto` | `404` |
| `POST /api/services` | `{ "name", "price" }` | `201` `ServiceDto` + `Location` | `400` `Name` / `Price`; `403` without clinic |
| `PUT /api/services/{id}` | `{ "name", "price" }` | `204` | `400`; `404` |
| `POST /api/services/{id}/deactivation` | — | `204` | `404` |
| `POST /api/services/{id}/activation` | — | `204` | `404` |

`ServiceDto`:

```json
{ "id": "guid", "name": "Kompozitni ispun", "price": 7500.00, "isActive": true }
```

Validation:

- `name`: required, trimmed, at most 200 characters.
- `price`: required, in RSD; at least 0 (a free service is allowed), at most 2 decimal places, and must fit `decimal(18,2)`.

Behaviour:

- An inactive service cannot be recorded for a patient (`409`).
- A recorded service keeps the price it had at the time, so changing the price never rewrites history.

---

## 5. Reward definitions — ClinicAdmin only

Rewards exist only once a ClinicAdmin creates them. No clinic is seeded with defaults.

| Method and route | Request | Success | Errors |
|---|---|---|---|
| `GET /api/rewards?isActive=` | — | `200` `[ RewardDto ]`, ordered by required points, then required referrals, then name | `403` without clinic |
| `GET /api/rewards/{id}` | — | `200` `RewardDto` | `404` |
| `POST /api/rewards` | `RewardRequest` | `201` `RewardDto` + `Location` | `400` per field; `403` without clinic |
| `PUT /api/rewards/{id}` | `RewardRequest` | `204` | `400`; `404` |
| `POST /api/rewards/{id}/deactivation` | — | `204` | `404` |
| `POST /api/rewards/{id}/activation` | — | `204` | `404` |

`RewardRequest`:

```json
{ "name": "Peskiranje", "description": "Optional text", "requiredPoints": 300000, "requiredReferrals": 1 }
```

`RewardDto`:

```json
{ "id": "guid", "name": "Peskiranje", "description": "Optional text", "requiredPoints": 300000, "requiredReferrals": 1, "isActive": true }
```

Validation:

- `name`: required, trimmed, at most 200 characters.
- `description`: optional, trimmed (blank means none), at most 1,000 characters.
- `requiredPoints`: required, 0 to 1,000,000.
- `requiredReferrals`: required, 0 or more. More than a cycle can earn (5) is accepted but never unlocked (ADR-008 decision 2).

Behaviour:

- Availability for a patient is always computed from the current definition and the patient's balances. Changing requirements therefore changes availability from then on.
- A deactivated reward is not offered and cannot be redeemed.
- A reward already redeemed in the patient's cycle stays shown as Redeemed.

---

## 6. Referrals — ClinicAdmin only

| Method and route | Request | Success | Errors |
|---|---|---|---|
| `POST /api/referrals` | `{ "referrerPatientId", "referredPatientId" }` | `201` `{ "id", "status": "Pending" }` | `400` self-referral; `404` patient not in clinic; `409` patient inactive or already referred |
| `POST /api/referrals/{id}/validation` | `{ "examinedAt"? }` (defaults to now) | `200` `{ "id", "status": "Completed", "referrer": RewardOutcome, "referred": RewardOutcome }` | `400` future `examinedAt`; `404`; `409` not pending, patient inactive, concurrent change |

`RewardOutcome` is `{ "outcome": "Rewarded" | "Forfeited" | "MaximumReached" | "CycleEnded", "creditedPoints", "cycleCompleted" }`.

There is no referral list or cancellation endpoint yet.

---

## 7. Loyalty configuration — ClinicAdmin only

| Method and route | Request | Success | Errors |
|---|---|---|---|
| `GET /api/loyalty-configuration` | — | `200` `{ "clinicId", "spendAmountRsd", "pointsAwarded" }` | `404` |
| `PUT /api/loyalty-configuration` | `{ "spendAmountRsd", "pointsAwarded" }` | `204` | `400`; `404` |

---

## 8. Not implemented yet

These are listed so they are not mistaken for existing endpoints:

- Clinic list, read, update and deactivation.
- Patient reactivation.
- Patient-facing service list with calculated points, and the points calculator (`BUSINESS.md` §15, §16).
- Referral list and cancellation.
- Password change and reset.
- Analytics and dashboard.
