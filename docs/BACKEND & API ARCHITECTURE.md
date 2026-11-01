# Backend & API Architecture Pillar

## 1. Backend Architecture

The backend will be implemented as a **modular monolith** using ASP.NET Core Web API.

The MVP will use a single backend application containing clearly separated logical modules.

```text
Patient App
     │
Admin App
     │
     ▼
ASP.NET Core Web API
     │
     ▼
Modular Monolith
     │
 ┌───┼──────────────────────────────┐
 │   │                              │
Identity                       Clinics
 │                                │
Patients                       Loyalty
 │                                │
Analytics                       Audit
     │
     ▼
Entity Framework Core
     │
     ▼
Relational Database
```

The main modules are:

```text
Identity
Clinics
Patients
Loyalty
Analytics
Audit
```

These are logical modules inside one application and are **not independent microservices**.

The MVP will use:

* One ASP.NET Core application
* One API
* One deployment
* One relational database
* One modular monolith architecture

Microservices will not be introduced unless future scale or business requirements justify the additional complexity.

> **Keep the backend simple while maintaining clear domain boundaries.**

---

# 2. Modular Monolith Structure

The backend should have clear module boundaries without introducing unnecessary project or infrastructure complexity.

A possible structure is:

```text
src/

│
├── Api/
│
├── Application/
│   ├── Identity/
│   ├── Clinics/
│   ├── Patients/
│   ├── Loyalty/
│   ├── Analytics/
│   └── Audit/
│
├── Domain/
│   ├── Identity/
│   ├── Clinics/
│   ├── Patients/
│   ├── Loyalty/
│   ├── Analytics/
│   └── Audit/
│
└── Infrastructure/
    ├── Identity/
    ├── Clinics/
    ├── Patients/
    ├── Loyalty/
    ├── Analytics/
    └── Audit/
```

The exact physical project structure may evolve during implementation.

The important requirement is that modules remain logically separated.

A module should not directly access another module's internal implementation.

Communication between modules should happen through clearly defined application interfaces or domain contracts.

The architecture should avoid unnecessary abstractions.

---

# 3. Core Backend Modules

## 3.1 Identity

Responsible for:

* authentication
* users
* credentials
* roles
* JWT/token handling
* authenticated identity

Roles include:

```text
PlatformAdmin
ClinicAdmin
ClinicStaff
Patient
```

---

## 3.2 Clinics

Responsible for:

* clinics
* tenant configuration
* clinic users/memberships
* clinic-level configuration
* tenant boundaries

---

## 3.3 Patients

Responsible for:

* patient profiles
* patient identity relationship
* patient-clinic relationship
* patient-specific information

---

## 3.4 Loyalty

Responsible for the core loyalty business domain:

* loyalty accounts
* points
* referral balance
* referrals
* services
* service point calculation
* rewards
* reward eligibility
* reward redemption
* loyalty transactions
* milestones
* loyalty configuration
* loyalty cycles where required

This is the primary business module of the MVP.

---

## 3.5 Analytics

Responsible for:

* loyalty analytics
* clinic-level reporting
* patient loyalty statistics
* aggregated business metrics

Analytics should not become the source of truth for loyalty state.

---

## 3.6 Audit

Responsible for recording important administrative and security-related operations.

Loyalty transactions are separate domain records and should not be replaced by generic audit logs.

---

# 4. API Design

The backend will expose a RESTful HTTP API for the Patient Application and Admin Application.

The API will use versioning:

```text
/api/v1/
```

Example resources:

```text
/api/v1/patients
/api/v1/services
/api/v1/rewards
/api/v1/referrals
/api/v1/loyalty
/api/v1/analytics
```

Business operations may use explicit action endpoints where appropriate.

Example:

```text
POST /api/v1/rewards/{id}/redeem
```

The API should:

* Use appropriate HTTP methods
* Use appropriate HTTP status codes
* Validate input
* Enforce authentication
* Enforce authorization
* Enforce tenant isolation
* Return consistent response structures
* Never expose internal database structures unnecessarily

---

# 5. Authentication & Identity

Authentication will be implemented using JWT-based authentication.

Conceptually:

```text
Client
  ↓
Credentials / Authentication Flow
  ↓
Identity System
  ↓
Authenticated User
  ↓
JWT
  ↓
ASP.NET Core API
```

The authenticated user represents the identity used by the backend for authorization and ownership checks.

The system should distinguish between:

```text
User
Patient
Clinic
Role
Membership
```

A Patient is a domain representation of the user participating in the loyalty program.

The backend must never trust user identity information supplied directly by the client.

---

# 6. Authorization & Tenant Context

Authorization must always be enforced server-side.

Authorization depends on:

```text
User
 +
Role
 +
Tenant
 +
Ownership
 +
Business Rules
```

Tenant context must be resolved from trusted authentication information.

```text
JWT
 ↓
User Identity
 ↓
User / Membership
 ↓
TenantId
 ↓
Tenant Context
 ↓
Authorized Operation
```

The client must not be trusted to select its tenant.

For example:

```text
GET /api/v1/patients?tenantId=123
```

must never be sufficient to access another tenant.

Tenant isolation remains a primary security boundary.

---

# 7. Layered Architecture

The backend will use a simple layered architecture.

```text
API
 ↓
Application
 ↓
Domain
 ↓
Infrastructure
```

Responsibilities:

```text
API
    HTTP concerns

Application
    Use cases and orchestration

Domain
    Business concepts and rules

Infrastructure
    Database and external technical services
```

The architecture should not introduce additional layers unless they solve a real problem.

---

# 8. Domain Layer

The Domain layer represents the core business concepts of the platform.

Important loyalty domain concepts include:

```text
Clinic
User
Patient

LoyaltyAccount
LoyaltyConfiguration
LoyaltyCycle

Service
Referral

Reward
RewardMilestone
RewardRedemption

LoyaltyTransaction
```

Other domain concepts may be introduced when implementation requires them.

The Domain layer should contain important business rules that naturally belong to the domain.

The Domain layer should not depend on:

* ASP.NET Core
* Entity Framework Core
* HTTP
* Controllers
* Database-specific infrastructure

The domain should remain independent of technical implementation details where practical.

---

# 9. Loyalty Account

A patient has a loyalty account representing the current loyalty state.

The loyalty account must track at least:

```text
PatientId
PointsBalance
ReferralBalance
```

The loyalty account represents the patient's current spendable loyalty state.

Every patient has exactly one loyalty account. It is created together with the patient, in the same atomic operation, starting with a points balance of zero; loyalty operations never create it (`ADR/006-loyalty-account-creation-lifecycle.md`).

The backend is the authoritative source of this state.

Patients cannot directly modify:

* PointsBalance
* ReferralBalance

All modifications must occur through authorized business operations.

---

# 10. Loyalty Points

Each clinic has its own scoring configuration: a spend amount in RSD and the points awarded for that amount (`BUSINESS.md` §3.1). It is stored per clinic in `LoyaltyConfiguration` and is tenant-scoped.

The platform default for a new clinic, which the ClinicAdmin can change during clinic setup, is:

```text
100 RSD = 1,000 points
```

The backend must always use the scoring configuration of the clinic the service transaction belongs to, resolved from the trusted tenant context. It must never use another clinic's configuration or a hardcoded platform-wide ratio.

The maximum progress target, and the maximum points balance, is:

```text
1,000,000 points
```

If an earning operation would take the balance above 1,000,000 points, only the points that fit are credited, the excess is discarded, and the operation is not rejected because of the cap (`BUSINESS.md` §3.3).

Service points are calculated by the backend.

The frontend may display the calculation but must never be the authoritative source.

Conceptually:

```text
Service Amount
     ↓
Points Calculation
     ↓
Loyalty Transaction
     ↓
Loyalty Account
```

The calculation must use an appropriate numeric representation and must avoid floating-point precision problems for monetary values.

Points before rounding are the service amount in RSD ÷ spend amount × points awarded (with the default configuration, service amount × 10). The service amount itself is not rounded. The result is rounded to the nearest whole number of points using midpoint rounding away from zero: a fractional part of 0.5 or greater rounds up, and a fractional part below 0.5 rounds down (`BUSINESS.md` §3.2). The rounded result is the number of points generated by the service.

---

# 11. Services

Services belong to a clinic and may contain:

```text
Id
ClinicId
Name
Description
Price
Category
IsActive
```

The service price is clinic-configurable.

The loyalty points generated by a service are derived from the scoring configuration of the clinic that owns the service (§10).

The backend should be able to return:

```text
Service
Price
CalculatedPoints
```

to the Patient App.

Services must be tenant-isolated.

A clinic can manage only its own services.

---

# 12. Points Calculator

The backend should expose a use case for calculating points from a service amount.

Conceptually:

```text
CalculatePoints
      ↓
Amount
      ↓
Clinic Scoring Configuration
      ↓
Calculated Points
```

The calculator uses the scoring configuration of the caller's clinic, taken from the trusted tenant context rather than from a client-supplied value.

The calculator may be exposed through an API endpoint where appropriate.

Example:

```text
GET /api/v1/loyalty/points-calculator?amount=7500
```

The backend returns the calculated loyalty points, rounded exactly as in §10.

The Patient App uses this result to display the calculation.

The calculator does not create a loyalty transaction.

It only calculates a potential points value.

---

# 13. Referral Lifecycle

A referral is not immediately considered a valid loyalty referral.

Conceptually:

```text
Referral Created
      ↓
New Patient Identified
      ↓
New Patient Visits Clinic
      ↓
Examination Completed
      ↓
Referral Validated
      ↓
Rewards Applied
```

A referral becomes valid only after the referred patient completes a dental examination.

The referred patient does not need to purchase or use a dental service.

---

# 14. Referral Rewards

For every valid referral:

```text
Referrer
    +50,000 points

Referred Patient
    +50,000 points
```

A patient may receive a maximum of:

```text
5 valid referral rewards
```

Therefore, referral activity can contribute a maximum of:

```text
250,000 points
```

to one patient's points balance.

The backend must enforce the maximum.

The frontend must not be responsible for enforcing this rule.

---

# 15. Referral Balance

The loyalty account maintains a referral balance used by reward eligibility and redemption.

Valid referrals increase the referral balance.

Reward redemption may consume referral credits according to the individual reward requirement.

The backend must distinguish between:

* referral history
* current referral balance
* referral-derived points

Referral history must not be lost when referral credits are consumed.

---

# 16. Rewards

A reward is an independent business object.

A reward may contain:

```text
Id
ClinicId
Name
Description
Tier
RequiredPoints
RequiredReferrals
RewardType
ServiceId (optional)
Icon
IsActive
```

The exact model may evolve during implementation.

Every reward has its own requirements.

Example:

```text
Reward:
Kompozitni ispun

RequiredPoints:
500,000

RequiredReferrals:
1
```

Rewards are not merely steps toward a final reward.

Each reward is independently evaluated.

---

# 17. Reward Tiers

The MVP defines three tiers.

## Tier 1

```text
1 referral
300,000 points
```

Rewards include:

```text
300,000 points + 1 referral
    → Peskiranje

400,000 points + 1 referral
    → Uklanjanje kamenca + poliranje

500,000 points + 1 referral
    → Kompozitni ispun
```

## Tier 2

```text
3 referrals
600,000 points
```

Rewards include:

```text
600,000 points + 3 referrals
    → 15% popusta na fiksnu protezu

700,000 points + 3 referrals
    → 30% popusta na hiruršku ekstrakciju umnjaka
```

## Tier 3

```text
5 referrals
1,000,000 points
```

Reward:

```text
1,000,000 points + 5 referrals
    → Kućno izbeljivanje zuba
```

The backend must not hardcode these rules exclusively inside application services.

Reward definitions should be represented as domain/configuration data where practical.

---

# 18. Reward Eligibility

Reward eligibility requires all defined conditions to be satisfied.

For the MVP:

```text
Current Points >= Reward.RequiredPoints
AND
Current Referral Balance >= Reward.RequiredReferrals
```

The backend must perform this check.

Example:

```text
Patient:
600,000 points
1 referral

Reward:
500,000 points
1 referral

Result:
Eligible
```

But:

```text
Patient:
600,000 points
0 referrals

Reward:
500,000 points
1 referral

Result:
Not eligible
```

The frontend may display eligibility, but the backend must re-evaluate it before redemption.

---

# 19. Multiple Available Rewards

Multiple rewards may be available at the same time.

The backend must return the current state of each reward for the patient.

Possible states include:

```text
Locked
Available
Redeemed
```

A patient does not need to redeem a lower-tier reward before becoming eligible for another reward.

Rewards can be redeemed independently.

Redeeming one reward can change the eligibility of other rewards because the patient's points and referral balance are reduced.

---

# 20. Reward Redemption

Reward redemption is a business operation.

Example:

```text
POST /api/v1/rewards/{id}/redeem
```

The backend must:

1. authenticate the user
2. resolve the tenant
3. verify patient ownership/access
4. load the reward
5. load the patient's current loyalty state
6. verify reward eligibility
7. verify sufficient points
8. verify sufficient referral balance
9. deduct required points
10. deduct required referral credits
11. create RewardRedemption
12. create LoyaltyTransaction records
13. commit the operation atomically

If any required operation fails:

```text
Rollback
```

The patient's loyalty state must remain unchanged.

---

# 21. Clinic Admin Redemption

The Clinic Admin is responsible for confirming that a reward has actually been used.

The redemption flow may therefore be represented as:

```text
Patient becomes eligible
        ↓
Reward available
        ↓
Patient requests/uses reward
        ↓
Clinic Admin confirms redemption
        ↓
Backend validates current state
        ↓
Points + referrals deducted
        ↓
Redemption recorded
```

The exact UX flow may evolve during frontend implementation, but the backend must ensure that redemption cannot bypass authorization or business validation.

---

# 22. Reward Redemption Entity

Reward redemption should be represented separately from the Reward itself.

A redemption record should contain enough information to establish:

```text
Id
PatientId
RewardId
ClinicId
PointsConsumed
ReferralsConsumed
RedeemedAt
RedeemedBy
Status
```

The exact properties may evolve during implementation.

The reward definition should remain reusable while individual redemptions are historical events.

---

# 23. Loyalty Transactions

The system must maintain a loyalty transaction history.

Possible transaction types include:

```text
ServicePointsEarned
ReferralPointsEarned
RewardRedeemed
ManualPointsAdjustment
ManualReferralAdjustment
```

A transaction should contain sufficient information to establish:

```text
Id
PatientId
ClinicId
Type
PointsChange
ReferralChange
RewardId (optional)
ServiceId (optional)
ReferralId (optional)
CreatedAt
CreatedBy
```

The exact model may evolve during implementation.

For an earning transaction, `PointsChange` is the number of points actually credited to the balance, not the calculated points before the 1,000,000-point maximum is applied (`BUSINESS.md` §3.3, §14).

The transaction history is the audit trail of loyalty balance changes.

---

# 24. Balance vs Transaction History

The system should maintain a current loyalty balance for efficient reads while also maintaining immutable or controlled transaction history.

Conceptually:

```text
LoyaltyAccount
----------------
PointsBalance
ReferralBalance

        +

LoyaltyTransactions
----------------
+50,000 Referral
+100,000 Service
-300,000 Reward
...
```

The balance is the current state.

Transactions explain how that state was reached.

The system must not rely only on a mutable numeric field with no history.

---

# 25. Atomic Loyalty Operations

Operations that modify multiple loyalty records must be atomic.

Example:

```text
Redeem Reward
      ↓
Validate
      ↓
Deduct Points
      ↓
Deduct Referral Balance
      ↓
Create Redemption
      ↓
Create Transaction
      ↓
Commit
```

If one step fails:

```text
Rollback everything
```

This prevents partially completed loyalty operations.

---

# 26. Concurrency

Concurrency is particularly important for:

```text
Points
Referral Balance
Reward Redemption
Loyalty Account
```

Example:

```text
Admin A ──┐
          ├── Redeem/Modify Loyalty
Admin B ──┘
```

The system must prevent two simultaneous operations from spending the same loyalty balance incorrectly.

The MVP should use the simplest reliable concurrency strategy supported by the selected database and Entity Framework Core.

Optimistic concurrency should be preferred where appropriate.

Redemption must verify the current state at the time of the operation.

Complex distributed locking should not be introduced unless a real requirement appears.

---

# 27. Loyalty Milestones

Milestones represent important loyalty thresholds and rewards shown in the Patient App.

A milestone may contain:

```text
Id
ClinicId
RequiredPoints
RequiredReferrals
Tier
RewardId
Name
Icon
```

The backend should provide milestone data to the Patient App.

The frontend should not hardcode the complete milestone configuration.

Milestone status should be derived from the patient's current loyalty state.

---

# 28. Loyalty Progress

The maximum progress target is:

```text
1,000,000 points
```

The backend should provide sufficient information for the Patient App to display:

```text
CurrentPoints
MaximumPoints
ProgressPercentage
CurrentReferralBalance
MaximumReferralRewards
Milestones
Rewards
```

The frontend is responsible for visualization.

The backend remains authoritative for the underlying values and eligibility.

---

# 29. Loyalty Cycles

The loyalty cycle rules are defined in `BUSINESS.md` §5.1.

Each patient has an individual one-year cycle, starting on the patient's registration date and then on the actual end date of the previous cycle. A cycle ends early only when the patient has accumulated 1,000,000 points during the cycle, completed five successful referrals during the cycle, and redeemed all rewards required for the active cycle; otherwise it ends automatically one year after its start date.

When a cycle ends, early or after one year, the backend must end the current cycle and start a new one on the actual end date, resetting the active points balance and referral progress to zero and restarting reward progression and availability.

Where `BUSINESS.md` §5.1 does not yet define a detail, the backend must not invent additional reset behavior.

When implemented, loyalty cycle operations must be transactional and must preserve the required historical information.

---

# 30. Application Layer

The Application layer represents application use cases.

Important MVP use cases include:

```text
Create Patient
Update Patient

Create Service
Update Service
Get Services
Calculate Service Points

Create Referral
Validate Referral

Get Patient Loyalty Status
Get Patient Loyalty Transactions

Get Available Rewards
Check Reward Eligibility

Redeem Reward
Confirm Reward Redemption

Add Points
Apply Referral Reward

Update Loyalty Configuration

Get Clinic Analytics
```

The Application layer is responsible for orchestration.

It should:

* Coordinate domain operations
* Validate application-level requirements
* Call required repositories or persistence abstractions
* Manage transactions where appropriate
* Return application results

It should not contain HTTP-specific concerns.

---

# 31. Domain Business Logic vs Application Orchestration

Business rules should be placed where they naturally belong.

For example:

```text
Can this loyalty account redeem this reward?
```

is domain/business logic.

While:

```text
Load patient
Load reward
Check authorization
Execute redemption
Persist changes
Commit transaction
```

is application orchestration.

This distinction should be maintained during implementation.

The goal is not to create excessive abstractions, but to keep business rules independent from HTTP and persistence concerns.

---

# 32. API / Presentation Layer

Controllers or minimal API endpoints should handle:

* Routing
* Authentication context
* Request binding
* Input validation
* Calling application use cases
* HTTP status codes
* Response formatting

The API layer should not contain complex business logic.

Bad:

```text
Controller
   ↓
Calculate points
   ↓
Check reward eligibility
   ↓
Modify database
```

Preferred:

```text
Controller
   ↓
Application Use Case
   ↓
Domain Logic
   ↓
Persistence
```

Controllers should remain thin.

---

# 33. DTOs & Mapping

API contracts should use DTOs rather than exposing domain or database entities directly.

Example:

```text
PatientEntity
      ↓
PatientResponseDto
      ↓
JSON Response
```

Request DTOs:

```text
CreatePatientRequest
RedeemRewardRequest
CreateServiceRequest
CreateReferralRequest
```

Response DTOs:

```text
PatientResponse
PatientLoyaltyResponse
RewardResponse
ServiceResponse
LoyaltyTransactionResponse
```

DTOs should expose only the information required by the operation.

Clients must not be allowed to directly submit authoritative loyalty balances.

For example, a patient request must never be allowed to specify:

```text
PointsBalance = 1000000
ReferralBalance = 5
```

as authoritative state.

Mapping should remain simple.

Manual mapping is acceptable for the MVP.

A mapping library should only be introduced if it provides clear value.

---

# 34. Input Validation

All client input must be validated by the backend.

Validation should cover:

```text
Required fields
Data types
String lengths
Numeric ranges
Allowed values
Identifiers
Tenant access
Business constraints
```

Examples:

```text
Create Service
      ↓
Validate Name
      ↓
Validate Price
      ↓
Validate Clinic Access
      ↓
Create Service
```

and:

```text
Redeem Reward
      ↓
Validate Reward
      ↓
Validate Patient
      ↓
Validate Tenant
      ↓
Validate Points
      ↓
Validate Referrals
      ↓
Execute Redemption
```

Frontend validation exists for user experience only.

Backend validation is authoritative.

---

# 35. Error Handling

The backend must provide centralized and consistent error handling.

Expected errors should be classified appropriately.

Examples:

```text
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
422 Unprocessable Entity
500 Internal Server Error
```

Examples of business errors:

```text
Reward is not available.
Insufficient points.
Insufficient referrals.
Referral has already been validated.
Maximum referral limit has been reached.
Service does not belong to the current clinic.
Patient does not belong to the current clinic.
```

Unexpected technical errors should be logged server-side.

The client should receive a safe response.

Example:

```json
{
  "message": "Something went wrong. Please try again later."
}
```

> **Backend owns error classification and safe API responses. Frontend owns presentation and user experience.**

---

# 36. Transactions

Operations that modify multiple related records must use database transactions where atomicity is required.

Reward redemption is a primary example.

A redemption may modify:

```text
LoyaltyAccount
RewardRedemption
LoyaltyTransaction
```

Therefore the operation must be atomic.

Conceptually:

```text
Begin Transaction
      ↓
Validate State
      ↓
Update Loyalty Account
      ↓
Create Reward Redemption
      ↓
Create Loyalty Transactions
      ↓
Commit
```

Failure:

```text
Rollback
```

Transactions should be used where required rather than wrapping every operation unnecessarily.

---

# 37. Persistence & Entity Framework Core

Entity Framework Core will be used as the primary ORM.

EF Core will handle:

* entity mapping
* relationships
* database queries
* migrations
* persistence
* transactions
* concurrency support

The implementation should distinguish between:

```text
IQueryable
```

and:

```text
IEnumerable
```

and avoid unintentionally loading large datasets into memory.

Queries should be designed intentionally.

The backend should avoid unnecessary repository abstractions when EF Core already provides the required behavior cleanly.

Repository usage remains an implementation decision.

---

# 38. Database Design Considerations

The relational database must support:

```text
Users
Clinics
Patients
Memberships
Services
LoyaltyAccounts
Referrals
Rewards
RewardRedemptions
LoyaltyTransactions
LoyaltyCycles
AuditLogs
```

All tenant-owned data must include an appropriate tenant relationship.

Important loyalty indexes should be considered for:

```text
TenantId
PatientId
RewardId
ReferralId
CreatedAt
```

Exact indexes will be determined during database implementation based on query patterns.

---

# 39. Database Views & Stored Procedures

The MVP may use database views and stored procedures where they provide clear value.

Potential use cases include:

```text
Complex read/reporting queries
Analytics
Aggregations
Legacy/database integration requirements
```

They should not be introduced merely because they exist as database features.

The primary application data access approach will remain EF Core unless a specific query or business requirement justifies a view or stored procedure.

Where views or stored procedures are used, their purpose should be documented.

---

# 40. Pagination, Filtering & Sorting

Collection endpoints should support pagination where data can grow.

Example:

```text
GET /api/v1/patients?page=1&pageSize=20
```

Filtering may be supported where required.

Example:

```text
GET /api/v1/patients?search=John
```

Sorting may be supported for relevant administrative lists.

Example:

```text
GET /api/v1/patients?sortBy=createdAt&sortDirection=desc
```

The API should protect itself against unreasonable page sizes.

The MVP should implement only filtering, sorting, and pagination that provide concrete business value.

---

# 41. Caching

Caching should not be introduced prematurely.

The MVP should first rely on:

* Database indexes
* Efficient queries
* Appropriate EF Core usage
* Pagination
* Simple application design

Potential future caching candidates include:

```text
Loyalty Configuration
Reward Definitions
Service Definitions
```

Patient loyalty state should not be cached without carefully considering consistency requirements.

Redis or another distributed cache should not be introduced unless there is a demonstrated need.

---

# 42. Background Processing

The MVP should avoid unnecessary background infrastructure.

Potential future background operations include:

```text
Notifications
Emails
Analytics aggregation
Scheduled loyalty operations
Cleanup tasks
```

A message broker should not be introduced unless asynchronous communication becomes a real requirement.

Synchronous request processing should remain the default for simple business operations.

---

# 43. External Integrations

External services should be isolated behind application abstractions.

Examples:

```text
Email Provider
Notification Provider
Analytics Provider
```

Conceptually:

```text
Application
     ↓
IEmailService
     ↓
Infrastructure
     ↓
External Provider
```

The domain should not directly depend on external providers.

External integrations should be introduced only when required.

---

# 44. Logging & Observability

The backend should use structured server-side logging.

Logs should help diagnose:

```text
Authentication failures
Authorization failures
Tenant access violations
Unexpected exceptions
Database failures
Important loyalty operations
Reward redemptions
External service failures
```

Relevant context may include:

```text
RequestId
UserId
TenantId
Operation
Timestamp
Error
```

Sensitive information must not be logged unnecessarily.

---

# 45. Testing Strategy

Testing should focus on business-critical behavior.

## Unit Tests

Important domain/business rules:

```text
Service point calculation
Referral qualification
Referral reward calculation
Referral maximum
Reward eligibility
Reward redemption
Points deduction
Referral deduction
Loyalty balance rules
```

## Integration Tests

Verify:

```text
API
Database
Authentication
Authorization
Tenant Isolation
Transactions
EF Core persistence
Concurrency
```

## Security Tests

Verify:

```text
Patient cannot access another patient.
ClinicAdmin cannot access another clinic.
Patient cannot modify authoritative loyalty state.
Unauthorized users cannot access protected endpoints.
Patient cannot redeem another patient's reward.
```

## API Tests

Important endpoints should have integration/API coverage.

The MVP does not require 100% test coverage.

> **Test business-critical behavior rather than maximizing a coverage percentage.**

---

# 46. Performance & Scalability

The MVP should prioritize:

```text
Correctness
+
Security
+
Simplicity
+
Maintainability
```

Initial performance strategy:

```text
Simple Architecture
        +
Proper Database Indexes
        +
Efficient EF Core Queries
        +
Pagination
        +
Appropriate Transactions
        +
Basic Monitoring
```

The modular monolith can scale vertically and horizontally as required.

The API layer should remain stateless where practical.

Multiple backend instances can be introduced later if necessary.

---

# 47. Backend Source of Truth

The backend is authoritative for:

```text
Patient identity
Tenant ownership
Points
Referral balance
Referral qualification
Reward eligibility
Reward redemption
Loyalty transactions
Service point calculation
Reward configuration
Loyalty configuration
```

The frontend may display calculated information, but it must never be trusted as the source of truth.

The client can request an operation.

Only the backend can authorize and execute it.

---

# 48. Core Loyalty Backend Flow

The main loyalty flows are:

## Service Points

```text
Patient uses Service
       ↓
Service Price
       ↓
Backend Point Calculation
       ↓
Loyalty Transaction
       ↓
Loyalty Account
```

## Referral

```text
Patient creates referral
       ↓
New Patient
       ↓
Examination completed
       ↓
Referral validated
       ↓
+50,000 points to referrer
+50,000 points to referred patient
       ↓
Referral balances updated
       ↓
Transactions recorded
```

## Reward Eligibility

```text
Patient Loyalty State
       ↓
Load Reward
       ↓
Required Points?
       +
Required Referrals?
       ↓
Available / Locked
```

## Reward Redemption

```text
Available Reward
       ↓
Redeem Request
       ↓
Authorization
       ↓
Tenant Check
       ↓
Eligibility Check
       ↓
Concurrency Check
       ↓
Deduct Points
       ↓
Deduct Referral Balance
       ↓
Create Redemption
       ↓
Create Transaction
       ↓
Commit
```

---

# 49. Open Implementation Decisions

The following decisions remain open until implementation requires them:

```text
Exact solution/project structure
Exact authentication provider
Exact JWT implementation
Repository usage
Exact EF Core configuration
Exact database provider
Exact API versioning mechanism
Exact logging/monitoring infrastructure
Exact loyalty cycle reset implementation
```

These decisions should be made based on concrete implementation requirements rather than introduced prematurely.

> The exact concurrency mechanism, formerly listed here, is decided in `ADR/005-loyalty-balance-concurrency.md`: a SQL Server `rowversion` optimistic concurrency token on `LoyaltyAccount` (§26).

---

# 50. Backend Architecture Principle

The backend should remain:

```text
Simple
   +
Modular
   +
Secure
   +
Business-driven
   +
Testable
   +
Maintainable
   +
Scalable when necessary
```

The architecture should provide clear boundaries without introducing infrastructure that the MVP does not need.

> **Build a simple modular monolith first. Introduce additional architectural complexity only when a real business or technical requirement justifies it.**

The Business Pillar is the source of truth for business behavior.

The Backend Pillar defines how those business requirements are represented and implemented within the backend architecture.

When implementation reveals a genuine business ambiguity, the Business Pillar must be clarified before changing the implementation.
