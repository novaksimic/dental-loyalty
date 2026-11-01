# Data Pillar

## 1. Data Architecture Purpose

The Data Pillar defines how application data is structured, stored, related, protected, and maintained within the Dental Loyalty Platform.

The data architecture should remain as simple as possible while fully supporting:

* User identity
* Clinic tenancy
* Patient management
* Loyalty programs
* Points
* Referrals
* Rewards
* Reward progression
* Reward redemption
* Loyalty cycles
* Services
* Patient activity
* Analytics
* Audit information

The database is the authoritative source of persisted application state.

The backend is responsible for enforcing business rules, authorization, tenant isolation, and data consistency.

Client applications must never access the database directly.

The data model should avoid unnecessary entities, abstractions, or duplicated state.

> **The database should be as simple as possible while fully representing the domain, business rules, relationships, and tenant boundaries required by the MVP.**

---

## 2. Data Architecture Principles

The data architecture follows the following principles:

### 2.1 Simplicity

The database should contain only entities that represent real business or technical requirements.

The MVP should avoid:

* Unnecessary tables
* Generic rules engines
* Unnecessary abstraction tables
* Duplicated data
* Premature optimization
* Complex event-sourcing mechanisms
* Separate analytics databases

Complexity should only be introduced when there is a clear business or technical requirement.

### 2.2 Clear Domain Ownership

Every entity must have a clearly defined owner.

The primary ownership hierarchy is:

```text
Platform
   ↓
Clinic
   ↓
Tenant Data
```

Clinic-owned data must always be associated with the appropriate Clinic.

Patient-specific data must belong to the Patient and therefore indirectly to the Patient's Clinic.

### 2.3 Tenant Awareness

Tenant isolation is a fundamental requirement of the data architecture.

Each Clinic represents one tenant.

Tenant-specific entities must have a clear relationship with their owning Clinic.

Conceptually:

```text
Clinic
   │
   ├── Users
   ├── Patients
   ├── Services
   ├── LoyaltyProgram
   └── Tenant-specific data
```

The database structure must make tenant ownership explicit and predictable.

### 2.4 User and Patient Separation

`User` represents the authenticated identity of a person within the platform.

`Patient` represents the domain-specific patient profile.

For the MVP, a Patient Application user is a Patient.

The relationship is:

```text
User
  │
  └── Patient
```

A Patient references the corresponding User through `UserId`.

The `User` entity is responsible for:

* Authentication
* Authorization
* Email
* Password hash
* Role
* Account status

The `Patient` entity is responsible for patient-specific domain information.

This separation keeps authentication concerns independent from the patient domain model.

A Patient must belong to exactly one Clinic.

### 2.5 Single Source of Truth

The database must avoid storing the same authoritative information in multiple places unless there is a clear reason.

Historical transactions should remain historical records.

Current loyalty state should have one clearly defined source.

The exact strategy for calculating or maintaining the current point balance will be finalized during implementation.

### 2.6 Historical Data

Historical business activity should not be destroyed simply because the active loyalty state is reset.

For example:

```text
Loyalty Cycle 1
   ↓
Points / Referrals / Rewards
   ↓
Cycle completed
   ↓
Reset
   ↓
Loyalty Cycle 2
```

The reset should affect the active loyalty state without unnecessarily deleting historical information required for:

* Analytics
* Auditing
* Business reporting
* Loyalty history

### 2.7 Referential Integrity

The database should enforce structural relationships using:

* Primary keys
* Foreign keys
* Unique constraints
* Required fields
* Appropriate data types

Business rules remain the responsibility of the backend.

### 2.8 Transactional Consistency

Operations that modify multiple related entities and represent one business operation should be executed atomically.

For example:

```text
Reward Redemption
        ↓
Validate State
        ↓
Create Redemption Record
        ↓
Update Loyalty State
        ↓
Reset if Required
        ↓
Commit
```

If any required operation fails, the complete transaction should be rolled back.

---

## 3. Database Overview

The MVP will use a relational database.

The initial data model should contain a small number of clearly defined entities representing the core domain.

The main entities are:

```text
User
Clinic
Patient
LoyaltyProgram
LoyaltyConfiguration
Service
PointTransaction
Referral
Reward
RewardRedemption
LoyaltyCycle
PatientActivity
AuditLog
```

Conceptually:

```text
                         Clinic
                           │
             ┌─────────────┼─────────────┐
             │             │             │
           Users        Patients      Services
             │             │
             │             ├── PointTransactions
             │             ├── Referrals
             │             ├── RewardRedemptions
             │             ├── LoyaltyCycles
             │             └── PatientActivity
             │
             └── Patient User

                         Clinic
                           │
                           ▼
                    LoyaltyProgram
                           │
                 ┌─────────┼─────────┐
                 │         │         │
        LoyaltyConfiguration Rewards
```

The core relationship between authentication and the patient domain is:

```text
User
  │
  │ 1 : 0..1
  ▼
Patient
```

A User may represent a Patient, while not every User is necessarily a Patient.

For the MVP:

```text
PlatformAdmin
   ↓
User

ClinicAdmin
   ↓
User

Patient
   ↓
User + Patient
```

The `User` entity therefore represents authentication and authorization identity, while `Patient` represents patient-specific domain data.

The main tenant relationships are:

```text
Clinic 1 ─── * User
Clinic 1 ─── * Patient
Clinic 1 ─── * Service
Clinic 1 ─── 1 LoyaltyProgram
```

The loyalty relationships are:

```text
LoyaltyProgram 1 ─── 1 LoyaltyConfiguration
LoyaltyProgram 1 ─── * Reward
```

The patient loyalty relationships are:

```text
Patient 1 ─── * PointTransaction
Patient 1 ─── * Referral
Patient 1 ─── * RewardRedemption
Patient 1 ─── * LoyaltyCycle
Patient 1 ─── * PatientActivity
```

The complete logical model is therefore:

```text
Clinic
 │
 ├── Users
 │
 ├── Patients
 │    │
 │    ├── PointTransactions
 │    ├── Referrals
 │    ├── RewardRedemptions
 │    ├── LoyaltyCycles
 │    └── PatientActivity
 │
 ├── Services
 │
 └── LoyaltyProgram
      │
      ├── LoyaltyConfiguration
      └── Rewards
```

`AuditLog` is associated with the User and Clinic responsible for an auditable operation.

The database should not introduce additional tables unless they are required by an identified business or technical requirement.

---

## 4. Users

`User` represents the authenticated identity within the platform.

The User entity is responsible for authentication and authorization rather than patient-specific business data.

Conceptually:

```text
User
---------
Id
ClinicId
Email
PasswordHash
Role
IsActive
CreatedAt
UpdatedAt
```

`ClinicId` identifies the tenant to which the user belongs for tenant-scoped roles.

Platform-level users such as `PlatformAdmin` may not require a Clinic relationship.

The MVP uses three roles:

```text
PlatformAdmin
ClinicAdmin
Patient
```

A Patient Application user is represented by:

```text
User
  │
  └── Patient
```

The User entity should not contain loyalty information.

Points, referrals, rewards, and loyalty progression belong to the Patient domain.

Email should be unique according to the final identity strategy.

Passwords must be stored only as secure password hashes.

---

## 5. Clinics

`Clinic` represents a dental clinic and therefore a tenant of the SaaS platform.

Conceptually:

```text
Clinic
---------
Id
Name
IsActive
CreatedAt
UpdatedAt
```

A Clinic owns tenant-specific business data.

A Clinic may contain:

```text
Clinic
 ├── Users
 ├── Patients
 ├── Services
 ├── LoyaltyProgram
 └── AuditLogs
```

Each Clinic represents an isolated tenant.

Tenant-specific records must never be accessible across Clinic boundaries.

A Clinic is created by `PlatformAdmin`.

The initial `ClinicAdmin` can be created during clinic provisioning.

---

## 6. Patients

Patient represents the patient domain entity and the user of the Patient Application.

Each Patient belongs to exactly one Clinic and is linked to exactly one authenticated User.

Conceptually:

```text
Clinic
   │
   └── Patient
          │
          └── User
```

Conceptual model:

```text
Patient
---------
Id
UserId
ClinicId
FirstName
LastName
NickName
CreatedAt
UpdatedAt
IsActive
```

`UserId` references the authenticated User account.

`ClinicId` identifies the Patient's tenant.

Patient-specific data should remain separate from authentication data.

Patient does not directly own a separate authoritative loyalty balance unless the final implementation explicitly requires a cached current state.

> The current loyalty state is held in a separate `LoyaltyAccount` (ADR-004). Every Patient has exactly one, created atomically with the Patient (ADR-006).

The Patient belongs to only one Clinic.

A Patient cannot be moved between clinics through a normal client operation.

Any change of tenant ownership would be an explicit, authorized server-side operation.

---

## 7. Loyalty Program

`LoyaltyProgram` represents the loyalty program belonging to one Clinic.

For the MVP, one Clinic has one active Loyalty Program.

Conceptually:

```text
Clinic
   │
   └── LoyaltyProgram
          ├── Configuration
          ├── Rewards
          └── Services
```

Conceptual model:

```text
LoyaltyProgram
----------------
Id
ClinicId
Name
IsActive
CreatedAt
UpdatedAt
```

The Loyalty Program defines the context in which loyalty rules are applied.

It connects:

* Loyalty configuration
* Rewards
* Services
* Patient loyalty activity

The MVP should not support multiple simultaneously active loyalty programs for the same Clinic.

> Per ADR-003, the MVP does not introduce a separate `LoyaltyProgram` entity. `LoyaltyConfiguration`, `Reward` and `Service` belong directly to the `Clinic` (see §8).

---

## 8. Loyalty Configuration

`LoyaltyConfiguration` defines supported loyalty program parameters.

The configuration should be explicit and simple rather than implementing a generic rules engine.

Conceptually:

```text
LoyaltyConfiguration
----------------------
Id
ClinicId
SpendAmountRsd
PointsAwarded
CreatedAt
UpdatedAt
```

Each Clinic has exactly one `LoyaltyConfiguration` (`Clinic 1 ─── 1 LoyaltyConfiguration`, ADR-003). It is tenant-scoped data: it belongs to that Clinic only, must never be shared with or applied to another Clinic, and can be read or changed only within that Clinic's tenant context.

`SpendAmountRsd` and `PointsAwarded` are the clinic's scoring configuration (`BUSINESS.md` §3.1): `PointsAwarded` loyalty points are earned for every `SpendAmountRsd` RSD of service amount. Service points are always calculated with the configuration of the Clinic the service transaction belongs to.

Platform default for a newly provisioned Clinic (changeable by the ClinicAdmin during clinic setup):

```text
SpendAmountRsd = 100
PointsAwarded  = 1,000
(100 RSD = 1,000 points)
```

Calculated points are rounded to a whole number using midpoint rounding away from zero (`BUSINESS.md` §3.2). The stored service amount is not rounded.

Reward-specific requirements should be stored on the Reward entity.

Referral requirements should also be represented through Reward configuration rather than hardcoded throughout the application.

The system should provide sensible default configuration when a new Clinic is provisioned.

Only supported configuration parameters should be exposed to ClinicAdmin.

---

## 9. Points

Points represent loyalty points earned by a Patient.

Points should be represented through loyalty transactions rather than relying exclusively on one mutable balance field.

Conceptually:

```text
PointTransaction
-----------------
Id
PatientId
ServiceId
Points
Type
CreatedAt
CreatedBy
```

`Type` may represent the reason for the transaction:

```text
Service
Referral
ManualAdjustment
Redemption
Reset
```

The exact transaction types will be finalized during implementation.

For an earning transaction, `Points` records the points actually credited to the balance, which can be lower than the calculated points when the 1,000,000-point maximum balance applies (`BUSINESS.md` §3.3, §14).

The current point balance may either be:

* Calculated from valid transactions, or
* Maintained as a current state with appropriate consistency guarantees.

The MVP should choose the simpler approach that provides reliable behavior and understandable maintenance.

### Point Rules

Points must be processed through backend business logic.

Example:

```text
Patient uses service
        ↓
Backend validates service
        ↓
Point value calculated
        ↓
Point transaction created
        ↓
Loyalty state updated
```

Patient cannot directly create or modify PointTransactions.

ClinicAdmin may perform authorized manual adjustments when supported by the business rules.

---

## 10. Referrals

Referrals represent recommendations where an existing Patient refers another Patient to the Clinic.

A Referral connects:

* The Patient who made the referral
* The Patient who was referred

Conceptually:

```text
Patient A
    │
    └── Referral
           │
           ▼
       Patient B
```

Simple model:

```text
Referral
---------
Id
ReferrerPatientId
ReferredPatientId
CreatedAt
Status
```

A Referral is not necessarily valid simply because the record exists.

The backend determines when a Referral becomes valid according to the business rules.

Only valid referrals should contribute to loyalty progression.

A Patient cannot manually change their referral count.

The system should prevent invalid referral relationships such as self-referrals where prohibited by business rules.

---

## 11. Rewards

Rewards represent benefits that Patients can unlock through loyalty progression.

Each Reward belongs to one Loyalty Program.

Conceptually:

```text
LoyaltyProgram
      │
      ├── Reward 1
      ├── Reward 2
      └── Reward 3
```

Simple model:

```text
Reward
--------
Id
LoyaltyProgramId
Name
Description
RequiredPoints
RequiredReferrals
DisplayOrder
IsActive
CreatedAt
UpdatedAt
```

Different rewards may require different combinations of:

* Points
* Referrals

Example:

```text
Reward 1
Points + Referrals

Reward 2
Points + Referrals

Reward 3
1,000,000 Points + 5 Referrals
```

The MVP default reward values are defined in `BUSINESS.md` §6–§8 (ADR-001), superseding the earlier "1000 Points" example. A reward's required points are separate from the 1,000,000-point maximum balance.

### Reward Order

`DisplayOrder` defines the order in which rewards are presented in the Patient Application.

For example:

```text
Silver
Gold
Platinum
```

The exact names and order remain business decisions.

---

## 12. Reward Progression

Reward progression represents the Patient's progress toward available rewards.

Eligibility is derived from the Patient's current loyalty state and Reward requirements.

Conceptually:

```text
Current Points
      +
Current Referrals
      +
Reward Requirements
      ↓
Reward Eligibility
      ↓
Locked / Unlocked
```

The backend is responsible for calculating reward eligibility.

The Patient Application only displays the resulting state.

Reward status should not be duplicated as a complex permanent structure when it can be derived from:

* Current loyalty state
* Reward requirements
* Redemption state

The system should keep the stored model simple and calculate derived state where practical.

---

## 13. Reward Redemption

`RewardRedemption` remains part of the data model because the system needs to know that an unlocked reward was actually redeemed.

However, it should not represent a separate reward lifecycle or duplicate the Reward entity.

Its purpose is to record the redemption event.

Conceptually:

```text
Reward
   ↓
Eligible
   ↓
Redeemed
   ↓
Redemption Record
```

Simple model:

```text
RewardRedemption
-----------------
Id
PatientId
RewardId
RedeemedAt
RedeemedBy
```

`RewardRedemption` represents the historical fact that a specific Patient redeemed a specific Reward.

It does not need to contain a large state machine.

When a reward is redeemed:

```text
Patient reaches reward requirement
        ↓
Reward becomes eligible
        ↓
ClinicAdmin confirms redemption
        ↓
RewardRedemption created
        ↓
Loyalty state progresses/resets
```

The existence of a `RewardRedemption` record provides:

* Redemption history
* Analytics
* Auditability
* Prevention of duplicate redemption

After the redemption, the next reward becomes available according to the loyalty progression rules.

The exact state transition will be implemented in the backend as one transactional business operation.

---

## 14. Loyalty Cycle

The Loyalty Cycle represents the period during which a Patient progresses through the loyalty program. The cycle rules are defined in `BUSINESS.md` §5.1.

Conceptually:

```text
Cycle Start
     ↓
Points + Referrals
     ↓
Reward Progression
     ↓
Reward Redemption
     ↓
Cycle Reset
```

Minimal model:

```text
LoyaltyCycle
-------------
Id
PatientId
StartDate
EndDate
Status
```

A Patient may have multiple historical LoyaltyCycle records.

Only one cycle should be active for a Patient at a time.

Each Patient has an individual cycle. The first cycle starts on the Patient's registration date; each subsequent cycle starts on the actual end date of the previous cycle.

A cycle lasts one year. It ends early only when its completion conditions are met (`BUSINESS.md` §5.1); otherwise it ends automatically one year after its start date. `EndDate` is the actual end date, early or after one year, and is empty while the cycle is active.

---

## 15. Loyalty Reset

A cycle ends early when all completion conditions are met (`BUSINESS.md` §5.1) — 1,000,000 points accumulated during the cycle, five successful referrals completed during the cycle, and all rewards required for the active cycle redeemed. Otherwise it ends automatically one year after its start date. In both cases a new cycle starts on the actual end date.

At the start of the new cycle, conceptually:

```text
Active points balance → 0
Referral progress     → 0
Rewards               → progression and availability restart
```

Resetting the loyalty cycle must never delete historical data: service records, loyalty transactions, referral records and reward-redemption records are preserved for audit and reporting.

For example:

```text
Previous Cycle
     ↓
Historical Points
Historical Referrals
Historical Redemptions
     ↓
New Cycle
     ↓
New Progress
```

The reset should create or activate the next LoyaltyCycle while preserving historical records.

Reset operations must be executed by the backend.

Where multiple records are changed, the operation must use a database transaction.

---

## 16. Services

`Service` represents a service offered by a Clinic that can participate in the loyalty program.

A Clinic may have multiple Services.

Conceptually:

```text
Clinic
   │
   └── Services
          ├── Service A
          ├── Service B
          └── Service C
```

Conceptual model:

```text
Service
--------
Id
ClinicId
Name
Description
Price
Points
IsActive
CreatedAt
UpdatedAt
```

A Service does not define its own point value. Its points are calculated from the service amount using its Clinic's scoring configuration (§8, `BUSINESS.md` §3); `Points` above means that calculated value (`BUSINESS.md` §15), not an independently configured number.

Example, with the default scoring configuration:

```text
100 RSD = 1,000 points
```

Calculated points are rounded to a whole number using midpoint rounding away from zero (`BUSINESS.md` §3.2).

The exact implementation will be determined during implementation.

ClinicAdmin may manage supported Services.

A Service belongs to exactly one Clinic.

---

## 17. Patient Activity

Patient Activity represents an interaction or service activity relevant to the loyalty platform.

The MVP should not introduce a complete appointment-management system unless future requirements require it.

A simple activity model is sufficient.

Conceptually:

```text
PatientActivity
----------------
Id
PatientId
ServiceId
CreatedAt
```

Patient Activity may support:

* Point calculation
* Returning patient analytics
* Service usage
* Patient activity metrics

The activity record should not duplicate unnecessary patient information.

The exact relationship between PatientActivity and PointTransaction will be finalized during implementation.

---

## 18. Analytics Data

Analytics provides ClinicAdmin with visibility into the performance of the loyalty program.

MVP analytics should support metrics such as:

* New patients
* Returning patients
* Loyalty program users
* Referral activity
* Reward redemptions
* Patient activity

Analytics is tenant-scoped.

Conceptually:

```text
Clinic
   │
   └── Analytics
```

The MVP does not require a separate analytics database or data warehouse.

Metrics can initially be calculated from existing business data.

Example:

```text
Patients
   +
Patient Activity
   +
Referrals
   +
Reward Redemptions
   ↓
Analytics
```

If analytics queries become expensive in the future, aggregated data or specialized analytics storage may be introduced.

---

## 19. Audit Data

Important administrative and security-sensitive operations should be auditable.

Examples include:

* Manual point changes
* Referral changes
* Reward redemption
* Loyalty configuration changes
* Patient data changes
* Security-sensitive operations

Simple model:

```text
AuditLog
---------
Id
UserId
ClinicId
Action
Resource
ResourceId
CreatedAt
```

`UserId` identifies the user who performed the action.

`ClinicId` identifies the tenant in which the operation occurred when applicable.

Audit logs should not unnecessarily store:

* Passwords
* Tokens
* Secrets
* Sensitive patient information
* Complete request or response bodies

The MVP audit model should remain simple.

---

## 20. Entity Relationships

The main relationships are:

```text
Clinic 1 ─── * User
Clinic 1 ─── * Patient
Clinic 1 ─── * Service
Clinic 1 ─── 1 LoyaltyProgram

User 1 ─── 0..1 Patient

LoyaltyProgram 1 ─── 1 LoyaltyConfiguration
LoyaltyProgram 1 ─── * Reward

Patient 1 ─── * PointTransaction
Patient 1 ─── * Referral
Patient 1 ─── * RewardRedemption
Patient 1 ─── * LoyaltyCycle
Patient 1 ─── * PatientActivity

Reward 1 ─── * RewardRedemption

User 1 ─── * AuditLog
Clinic 1 ─── * AuditLog
```

The complete logical relationship model is:

```text
Clinic
 │
 ├── User
 │    └── Patient
 │         ├── PointTransaction
 │         ├── Referral
 │         ├── RewardRedemption
 │         ├── LoyaltyCycle
 │         └── PatientActivity
 │
 ├── Service
 │
 └── LoyaltyProgram
      ├── LoyaltyConfiguration
      └── Reward
           └── RewardRedemption
```

The database should use foreign keys to enforce these structural relationships.

---

## 21. Data Ownership

Every entity must have a clearly defined ownership model.

The primary ownership hierarchy is:

```text
Platform
   ↓
Clinic
   ↓
Tenant Data
```

Clinic owns tenant-specific data such as:

```text
Clinic
 ├── Users
 ├── Patients
 ├── Services
 ├── LoyaltyProgram
 └── AuditLogs
```

Patient-specific data belongs to the Patient and therefore to the Patient's Clinic.

Examples include:

```text
Patient
 ├── PointTransactions
 ├── Referrals
 ├── RewardRedemptions
 ├── LoyaltyCycles
 └── PatientActivity
```

PlatformAdmin has platform-level authority over Clinic administration.

Platform-level access to Patient data must still be explicitly defined and controlled.

---

## 22. Data Integrity

The database and backend must work together to maintain data consistency.

The database should enforce:

* Primary keys
* Foreign keys
* Unique constraints
* Required fields
* Appropriate data types

The backend must enforce:

* Authorization
* Tenant isolation
* Business rules
* Loyalty rules
* Ownership rules

Example:

```text
Patient
   ↓
Redeem Reward
   ↓
Backend validates:
   ├── Patient owns the reward
   ├── Reward is eligible
   ├── Reward is not already redeemed
   └── Redemption is allowed
   ↓
Persist changes
```

A Patient must never be able to manipulate authoritative loyalty data simply by sending modified values from the frontend.

---

## 23. Data Lifecycle

Data passes through defined business lifecycles.

For a Patient:

```text
Registration
     ↓
Active
     ↓
Loyalty Activity
     ↓
Reward Progression
     ↓
Reward Redemption
     ↓
Future Loyalty Cycles
```

For a Loyalty Cycle:

```text
Created
   ↓
Active
   ↓
Completed
   ↓
Historical
```

For a Reward:

```text
Available
   ↓
Locked
   ↓
Eligible
   ↓
Redeemed
```

Derived states should be calculated where practical rather than unnecessarily persisted.

Historical records should remain available when required for:

* Analytics
* Auditing
* Reporting
* Business history

---

## 24. Data Deletion

Data deletion must be carefully controlled.

Particular attention should be given to:

* Point history
* Referral history
* Reward redemption history
* Patient activity
* Analytics-related data
* Audit data

Uncontrolled cascade deletion should not be used for all related entities.

For example:

```text
Patient
   │
   ├── PointTransactions
   ├── Referrals
   ├── RewardRedemptions
   └── PatientActivity
```

Deleting a Patient should not automatically delete all historical information without understanding the business and legal consequences.

Patient deletion strategy will depend on:

* Business requirements
* Legal requirements
* Data retention requirements
* Clinic requirements

Soft deletion may be considered where historical relationships must be preserved.

---

## 25. Database Performance & Indexing

The MVP data model should remain simple while providing appropriate database performance.

Indexes should be added for frequently used lookup and filtering fields.

Particular attention should be given to:

```text
ClinicId
UserId
PatientId
LoyaltyProgramId
RewardId
ServiceId
CreatedAt
```

Tenant-scoped queries should be designed with tenant filtering in mind.

Potential composite indexes may include:

```text
ClinicId + PatientId
ClinicId + CreatedAt
PatientId + CreatedAt
PatientId + RewardId
```

The exact indexes will be determined after the query patterns are implemented.

The project should avoid premature optimization.

Indexes should be introduced based on actual query requirements and measured performance where possible.

---

## 26. Transactions

Operations that modify multiple related loyalty records should be executed atomically.

A reward redemption may involve:

```text
Begin Transaction
      ↓
Validate Patient
      ↓
Validate Reward Eligibility
      ↓
Create RewardRedemption
      ↓
Update Loyalty Cycle
      ↓
Reset or Progress Loyalty State
      ↓
Commit
```

If any required step fails:

```text
Transaction
     ↓
Failure
     ↓
Rollback
```

The database must not be left in a partially updated state.

The backend should define transaction boundaries around business operations rather than exposing database transactions directly to the client.

---

## 27. Backup, Recovery & Open Data Decisions

### 27.1 Backup & Recovery

Production database data must be backed up regularly.

Backups should include:

* Clinics
* Users
* Patients
* Loyalty programs
* Loyalty configuration
* Services
* Points
* Referrals
* Rewards
* Reward redemptions
* Loyalty cycles
* Patient activity
* Relevant audit data

Managed database backup capabilities should be preferred where available.

The system should define:

* Backup frequency
* Backup retention
* Recovery procedure
* Recovery testing

> **A backup is not sufficient unless the data can actually be restored.**

### 27.2 Open Data Decisions

The following decisions remain open and should be finalized before implementation:

* Exact relational database
* Exact User/Identity implementation
* Point transaction vs current balance strategy
* Exact PatientActivity model
* Exact LoyaltyCycle implementation
* Data retention period
* Patient deletion strategy
* Exact analytics implementation
* Exact audit implementation
* Exact indexing strategy
* Exact database migration strategy

These decisions should be made based on:

* MVP requirements
* Security requirements
* Simplicity
* Maintainability
* Performance
* Cost
* Future growth requirements

No database decision should be made solely to prepare for hypothetical future scale.

---

## 28. Data Architecture Principles & Final Requirements

The Data Pillar establishes the following final requirements for the MVP.

### Core Principles

```text
1. Keep the database simple.

2. Every entity must have a clear business or technical purpose.

3. Clinic is the primary tenant boundary.

4. Tenant-specific data must always have a clear relationship to its Clinic.

5. User represents authenticated identity.

6. Patient represents the patient domain entity.

7. A Patient is linked to one User account.

8. A Patient belongs to exactly one Clinic.

9. Loyalty data belongs to the Patient domain.

10. Historical loyalty activity should not be unnecessarily deleted.

11. Reward eligibility should be derived from authoritative loyalty state.

12. RewardRedemption records the historical fact that a reward was redeemed.

13. RewardRedemption should not become an unnecessary complex state machine.

14. Business rules are enforced by the backend.

15. Database constraints enforce structural integrity.

16. Multi-entity business operations must use transactions.

17. Client applications must never access the database directly.

18. Analytics should initially use existing business data.

19. A separate analytics database is not required for the MVP.

20. Audit data should remain simple.

21. Cascade deletion must be used carefully.

22. Database indexes should support real query patterns.

23. Premature optimization should be avoided.

24. Data retention and deletion must consider business and legal requirements.
```

### Final Logical Model

```text
                              PLATFORM
                                 │
                                 ▼
                               CLINIC
                                 │
              ┌──────────────────┼──────────────────┐
              │                  │                  │
              ▼                  ▼                  ▼
             USER             PATIENT           SERVICE
              │                  │
              │                  ├── POINT TRANSACTION
              │                  ├── REFERRAL
              │                  ├── REWARD REDEMPTION
              │                  ├── LOYALTY CYCLE
              │                  └── PATIENT ACTIVITY
              │
              └── PATIENT ACCOUNT

                               CLINIC
                                 │
                                 ▼
                         LOYALTY PROGRAM
                                 │
                    ┌────────────┴────────────┐
                    │                         │
                    ▼                         ▼
          LOYALTY CONFIGURATION             REWARD
                                              │
                                              ▼
                                      REWARD REDEMPTION

                               CLINIC
                                 │
                                 ▼
                             AUDIT LOG
```

### Final Data Architecture Principle

The database should be:

```text
Simple
   +
Clear
   +
Consistent
   +
Tenant-aware
   +
Domain-focused
   +
Secure
   +
Maintainable
```

The platform should not introduce additional tables, relationships, abstractions, or persistence mechanisms unless they solve a clearly identified business or technical problem.

> **The database should be as simple as possible while fully representing the domain, business rules, relationships, historical information, and tenant boundaries required by the MVP.**
