# Frontend Architecture

## 1. Frontend Architecture Overview

The platform consists of two separate frontend applications:

```text
                         SaaS Platform

                              │

                         .NET Web API

                              │

              ┌───────────────┴───────────────┐
              │                               │
              ▼                               ▼
      Admin Application              Patient Application
           Angular                         Flutter
```

The Admin Application is used by clinic administrators, clinic staff, and platform administrators.

The Patient Application is used by patients to manage and interact with their loyalty program.

The two applications are separate frontend clients and must not share framework-specific UI components or implementation code.

They may share:

* API contracts
* Business concepts
* Authentication concepts
* Authorization concepts
* Domain terminology
* Validation rules where appropriate
* UX principles

The applications should evolve independently while remaining aligned with the same backend API and business rules.

The frontend architecture should remain simple, maintainable, and appropriate for the MVP.

---

## 2. Frontend Technology

The MVP uses:

```text
Admin Application
        ↓
     Angular

Patient Application
        ↓
     Flutter
```

Angular is used for the administrative application because the application is primarily data-driven and contains dashboards, tables, forms, configuration screens, analytics, and administrative workflows.

Flutter is used for the patient application because the patient experience is primarily mobile-oriented and should provide a consistent cross-platform mobile experience.

The two applications should evolve independently.

The architecture must not introduce abstractions simply to make Angular and Flutter implementations look identical.

---

## 3. Frontend Application Boundaries

Each frontend application has its own responsibility.

```text
Admin Application

    ├── Authentication
    ├── Clinic Administration
    ├── Patient Management
    ├── Loyalty Management
    ├── Service Management
    ├── Reward Management
    ├── Referral Management
    ├── Reward Redemption Management
    └── Analytics


Patient Application

    ├── Authentication
    ├── Profile
    ├── Loyalty Overview
    ├── Points
    ├── Services & Points Calculator
    ├── Referrals
    ├── Rewards
    └── Reward Status
```

The frontend must not expose functionality that belongs exclusively to another application.

For example, the Patient Application must not expose administrative configuration or reward management simply because the API technically supports it.

---

## 4. Feature-Based Organization

Frontend code should primarily be organized around business features rather than technical types.

Example Angular structure:

```text
src/

├── core/
│
├── shared/
│
├── features/
│   ├── authentication/
│   ├── patients/
│   ├── loyalty/
│   ├── services/
│   ├── rewards/
│   ├── referrals/
│   ├── analytics/
│   └── clinic/
│
└── app/
```

The exact folder structure may evolve during implementation.

The main principle is that related functionality should remain close together.

A feature should contain the UI, state, API interaction, and feature-specific presentation logic required by that feature where appropriate.

---

## 5. Shared UI Components

The frontend may use shared UI components within the same application.

Examples:

```text
Button
Input
Modal
Dialog
Table
Card
LoadingIndicator
ErrorMessage
Pagination
ProgressBar
EmptyState
```

Shared components should contain reusable presentation behavior.

They should not contain business-specific rules.

For example:

```text
Button
    → Generic UI component

ProgressBar
    → Generic presentation component

RewardCard
    → Loyalty feature component

RewardEligibility
    → Loyalty feature logic
```

Angular and Flutter may have conceptually similar components, but framework-specific implementations must remain independent.

---

## 6. Navigation & Routing

Navigation must be controlled by the frontend router.

The Admin Application should expose routes appropriate for administrative functionality.

Example:

```text
/login

/dashboard

/patients
/patients/:id

/loyalty
/loyalty/services
/loyalty/rewards
/loyalty/referrals

/analytics

/settings
```

The Patient Application should expose patient-specific navigation.

Example:

```text
/login

/home

/loyalty
/services
/rewards
/referrals

/profile
```

Frontend navigation improves user experience but is not a security boundary.

The backend must independently enforce authorization.

---

## 7. Authentication

Authentication is handled through the backend authentication system.

The frontend is responsible for:

* Login
* Logout
* Maintaining authentication state
* Handling expired sessions/tokens
* Redirecting unauthenticated users
* Providing authentication feedback

Conceptually:

```text
User
  ↓
Frontend Login
  ↓
Backend Authentication
  ↓
Authentication Result
  ↓
Frontend Authentication State
```

The frontend must never determine whether credentials are valid.

The backend remains the authentication authority.

---

## 8. Authorization & Route Protection

The frontend should use authorization information to provide an appropriate user experience.

Examples:

```text
PlatformAdmin
    → Platform administration

ClinicAdmin
    → Clinic administration + loyalty management

ClinicStaff
    → Operational clinic functionality

Patient
    → Patient functionality
```

The frontend may:

* Hide unavailable navigation
* Disable unavailable functionality
* Protect routes
* Redirect users

However:

> Frontend authorization is not a security boundary.

The backend must always enforce:

```text
Authentication
+
Role
+
Tenant
+
Ownership
+
Permission
```

A user must not gain access simply by manually navigating to a protected route or modifying frontend state.

---

## 9. API Communication

Frontend applications communicate with the backend exclusively through the API.

Conceptually:

```text
UI
 ↓
Feature Logic
 ↓
API Service / Client
 ↓
HTTP
 ↓
.NET Web API
```

API communication should be centralized rather than implementing raw HTTP calls throughout UI components.

Example:

```text
PatientComponent
      ↓
PatientService
      ↓
API Client
      ↓
HTTP API
```

The exact implementation differs between Angular and Flutter.

---

## 10. API Contracts & DTOs

Frontend applications should communicate using explicit API contracts.

The frontend should not depend directly on backend database entities.

```text
Backend Entity
      ≠
API DTO
      ≠
Frontend View Model
```

For example:

```text
PatientResponse

----------------

id
firstName
lastName
nickName
```

A loyalty response may contain:

```text
LoyaltyProgressResponse

------------------------

currentPoints
maximumPoints
progressPercentage

currentReferralBalance
maximumReferralRewards

milestones
rewards
```

The frontend should consume API contracts rather than relying on the internal structure of Entity Framework entities.

API contract changes should be intentional and backwards compatibility should be considered where necessary.

---

## 11. Loyalty Progress & Server State

The loyalty UI must be driven primarily by backend-provided state.

The backend is authoritative for:

```text
Current Points
Referral Balance
Maximum Progress
Reward Eligibility
Reward Requirements
Reward Redemption
Milestones
Reward Status
```

The Patient Application may calculate presentation-only values such as:

```text
Progress percentage
Visual position of a milestone
Remaining points for display
```

when appropriate.

However, it must not independently determine whether a reward is actually unlocked.

Example:

```text
Backend

CurrentPoints = 650,000
ReferralBalance = 3

        ↓

Frontend

Display:
65% progress
Eligible for relevant rewards
```

The frontend should not maintain an independent authoritative loyalty balance.

---

## 12. Loyalty Progress UI

The Patient Application should provide a clear visual representation of loyalty progress.

The progress bar should represent:

```text
0 → 1,000,000 points
```

The UI should support milestones with icons representing rewards.

Conceptually:

```text
0
│
├── 🦷 300,000 → Air polishing
│
├── 🦷 400,000 → Scaling + polishing
│
├── 🦷 500,000 → Composite filling
│
├── 🦷 600,000 → 15% fixed braces discount
│
├── 🦷 700,000 → 30% surgical wisdom tooth extraction discount
│
└── 🦷 1,000,000 → Home teeth whitening
```

The exact milestone data, requirements, labels, and icons should be supplied by the backend configuration/API rather than hardcoded in the frontend.

The frontend is responsible for rendering the milestone information clearly.

---

## 13. Rewards

Rewards are independent.

The Patient Application must not assume that the patient needs to reach the final reward before using earlier rewards.

The UI should allow the patient to see:

```text
Available Rewards
Locked Rewards
Redeemed / Used Rewards
```

Each reward should clearly communicate:

* Reward name
* Required points
* Required referrals
* Current eligibility
* Reward type
* Service or discount information
* Redemption status
* Relevant milestone

Example:

```text
Air Polishing

Required:
300,000 points
1 referral

Status:
AVAILABLE
```

Multiple rewards may be available at the same time.

The frontend must render the state supplied by the backend.

---

## 14. Reward Redemption

Reward redemption is a backend-authoritative operation.

The Patient Application may initiate:

```text
User
 ↓
Select Reward
 ↓
Confirm Redemption
 ↓
API Request
 ↓
Backend validates eligibility
 ↓
Backend deducts points/referrals
 ↓
Backend creates redemption
 ↓
Frontend receives updated state
```

The frontend must not independently deduct points or referrals.

After successful redemption, the frontend should refresh or update its displayed server state.

For example:

```text
Before redemption:

Points = 500,000
Referrals = 2

Reward:
300,000 + 1 referral

        ↓

Redemption

        ↓

After redemption:

Points = 200,000
Referrals = 1
```

The actual resulting values must come from the backend response rather than frontend calculations.

---

## 15. Reward Usage / Clinic Confirmation

Reward redemption and reward usage should be treated as distinct concepts where required by the backend workflow.

The Patient Application may show that a reward has been redeemed or is awaiting/has completed usage.

The Admin Application should provide the appropriate workflow for clinic staff or administrators to confirm that the reward was actually used.

Conceptually:

```text
Patient

Redeem Reward
      ↓
Reward Redemption


Clinic Staff / Admin

Review Redemption
      ↓
Confirm Reward Used
      ↓
Reward Status Updated
```

The frontend must display the status returned by the backend and must not assume that redemption automatically means the service was consumed unless the backend defines it that way.

---

## 16. Points & Services

The Patient Application should provide access to the clinic's dental services and their prices.

The service list may contain:

```text
Service
Price
Category
Description
Point Calculation
```

The patient should be able to understand how many points a service can generate.

The application should include a points calculator.

Example, using the platform's default scoring configuration:

```text
Service price: 7,500 RSD

100 RSD → 1,000 points

Calculated points:
75,000 points
```

Each clinic has its own scoring configuration (`BUSINESS.md` §3.1), so the ratio must come from the backend for the patient's clinic and must not be hardcoded in the frontend. Calculated points are rounded as defined in `BUSINESS.md` §3.2.

The displayed calculation should be consistent with the backend calculation.

The points actually credited can be lower than the calculated points if the patient's balance would exceed the 1,000,000-point maximum (`BUSINESS.md` §3.3).

The backend remains authoritative for actual points awarded after a service is completed.

The calculator is informational and must not create a loyalty transaction.

---

## 17. Referrals

The Patient Application should provide referral information separately from the referral balance.

The UI may display:

```text
Referral Balance
Referral History
Referral Progress
```

The referral balance represents the available referral credits used for reward eligibility.

Referral history represents actual referral activity.

A valid referral is confirmed by the backend when:

```text
New Patient
    ↓
Visits Clinic
    ↓
Completes Examination
    ↓
Referral Validated
```

After validation, the backend awards:

```text
Referrer:
+50,000 points
+1 referral credit

Referred Patient:
+50,000 points
```

The frontend should display the resulting state returned by the backend.

The maximum referral contribution to a patient's loyalty points is:

```text
5 referrals × 50,000 points
=
250,000 points
```

The frontend must not independently determine whether a referral is valid.

---

## 18. State Management

State management should be introduced according to actual application complexity.

State can be divided into:

```text
Local UI State
      ↓
Feature State
      ↓
Application State
      ↓
Server State
```

Examples of local state:

```text
Modal open/closed
Selected tab
Form state
Selected reward
```

Examples of application state:

```text
Authenticated user
Current tenant
Permissions
```

Examples of server state:

```text
Patients
Services
Rewards
Points
Referral data
Loyalty progress
Analytics
```

The MVP should avoid introducing complex global state management unless the application actually requires it.

Server state should remain synchronized with the backend.

---

## 19. Business Logic in the Frontend

The frontend may contain presentation and user-experience logic.

It must not be the authoritative source of business rules.

Frontend may calculate:

```text
Progress percentage
Visual formatting
Remaining points for display
```

Backend must determine:

```text
Whether a reward is eligible
Whether a reward can be redeemed
How many points a service awards
Whether a referral is valid
How many points/referrals are deducted
Whether a reward has been used
```

Conceptually:

```text
Frontend
    → Presentation logic

Backend
    → Authoritative business rules
```

Duplicated business rules should be minimized.

---

## 20. Forms & Client-Side Validation

Forms should provide immediate validation feedback.

Examples:

```text
Required fields
Email format
Minimum length
Numeric ranges
Allowed values
```

Client-side validation improves user experience.

However:

> Client-side validation is never authoritative.

The backend must validate all incoming data again.

```text
User Input
    ↓
Frontend Validation
    ↓
API Request
    ↓
Backend Validation
    ↓
Business Rules
    ↓
Persistence
```

---

## 21. Error Handling

Frontend applications must provide consistent error handling.

The backend is responsible for:

* Error classification
* Appropriate HTTP status codes
* Safe error responses
* Preventing technical details from reaching users

The frontend is responsible for:

* User-friendly messages
* Validation errors
* Authentication errors
* Authorization errors
* Network failures
* Retry actions where appropriate

Example:

```text
HTTP 400
    ↓
"The selected reward is no longer available."
```

Unexpected errors should provide a generic user-friendly experience.

Technical details should remain on the server.

---

## 22. Loading & Async States

The frontend must explicitly handle asynchronous operations.

Typical states include:

```text
Idle
Loading
Success
Error
Empty
```

This applies especially to:

```text
Patient lists
Loyalty progress
Rewards
Referrals
Services
Analytics
Redemption
```

Example:

```text
Loading Rewards
      ↓
Success
      ↓
Display Rewards
```

or:

```text
Loading Rewards
      ↓
Error
      ↓
Display Error
      ↓
Retry
```

The UI should not appear frozen while waiting for API operations.

---

## 23. Responsive & Device-Specific UI

The Admin Application and Patient Application have different primary usage patterns.

```text
Admin
    → Desktop / Tablet oriented

Patient
    → Mobile oriented
```

The Admin Application should support screen sizes required for normal administrative workflows.

The Patient Application should prioritize mobile usability and touch interaction.

The architecture should avoid unnecessary complexity for unsupported device sizes.

---

## 24. Accessibility

Frontend applications should follow reasonable accessibility practices.

Important areas include:

* Semantic controls
* Keyboard navigation where applicable
* Sufficient contrast
* Labels for form controls
* Meaningful error messages
* Screen reader compatibility where applicable
* Focus management

Accessibility should be considered during component development rather than added only at the end.

---

## 25. UX & Design Consistency

The applications should follow consistent UX principles.

Shared concepts may include:

```text
Colors
Typography
Spacing
Button behavior
Error presentation
Loading behavior
Navigation principles
Reward terminology
Loyalty terminology
```

However, Angular and Flutter may use platform-specific implementations.

The goal is consistent user experience, not identical implementation.

```text
Angular Button
    ≠
Flutter Button

Same UX principle
Different implementation
```

---

## 26. Internationalization

Internationalization should be considered if the platform requires multiple languages.

Text displayed to users should not be hardcoded throughout components.

Conceptually:

```text
Translation Key
      ↓
Localization System
      ↓
Localized Text
```

Example:

```text
reward.unlocked
```

rather than:

```text
"Reward unlocked"
```

Language-specific formatting should also be considered for:

* Dates
* Numbers
* Currency
* Messages

The exact supported languages will be defined by product requirements.

---

## 27. Frontend Security

Frontend applications must follow secure development practices.

Important principles include:

* Never trust client-side authorization
* Never store secrets in the application
* Never expose backend credentials
* Use HTTPS
* Validate user input
* Avoid unsafe HTML rendering
* Protect authentication state
* Avoid logging sensitive information

The frontend must assume that users can inspect and manipulate client-side code.

Therefore:

```text
Frontend
    ↓
Untrusted Environment
```

The backend remains the security boundary.

---

## 28. Performance

Frontend performance should be considered without premature optimization.

Important areas include:

```text
Network requests
Bundle size
Rendering
Images
Large lists
Change detection
State updates
Caching
Lazy loading
```

For Angular, appropriate techniques may include:

* Lazy-loaded routes
* Appropriate change detection strategies
* Efficient list rendering
* Avoiding unnecessary subscriptions
* Image optimization

For Flutter:

* Efficient widget trees
* Appropriate state updates
* Avoiding unnecessary rebuilds
* Efficient list rendering
* Image optimization

Performance optimizations should be driven by real problems.

---

## 29. Testing Strategy

Frontend testing should cover different levels of behavior.

### Unit Tests

Test isolated logic and components.

Examples:

```text
Validation
Formatting
Progress presentation
State transitions
Utility functions
```

### Integration Tests

Test interaction between multiple frontend parts.

Examples:

```text
Feature
+
State
+
API client
```

### End-to-End Tests

Test important user journeys.

Examples:

```text
Login
    ↓
View loyalty progress
    ↓
View available reward
    ↓
Redeem reward
    ↓
View updated points/referral balance
```

Another important flow:

```text
View service
    ↓
Calculate points
    ↓
Understand potential points
```

The MVP should prioritize tests around critical user journeys and business behavior rather than attempting to test every line of UI code.

---

## 30. Environment & Configuration

Frontend applications must support separate environments.

```text
Development
Testing
Staging
Production
```

Environment-specific configuration may include:

```text
API URL
Feature flags
Application configuration
Logging configuration
```

Secrets must never be embedded into frontend applications.

Anything shipped to the frontend should be considered potentially visible to the user.

Configuration should not require source-code modifications for every environment.

---

# 31. Frontend Architecture Principles

The frontend architecture should follow these principles:

```text
1. Keep Admin and Patient applications independent.

2. Angular and Flutter may share concepts and contracts,
   but not framework-specific UI components.

3. Frontend applications communicate with the backend through APIs.

4. The frontend is not a security boundary.

5. Backend authorization is always authoritative.

6. Business-critical rules must be enforced by the backend.

7. Frontend validation exists primarily for user experience.

8. API contracts should be explicit and stable.

9. State management should match actual application complexity.

10. Avoid unnecessary global state.

11. Organize code primarily around business features.

12. Shared UI components should remain generic and reusable.

13. Avoid putting business rules inside generic UI components.

14. Loading, error, empty, and success states should be handled explicitly.

15. Loyalty state should be treated as server state.

16. Reward eligibility must come from the backend.

17. Reward redemption must be performed through the backend.

18. Points and referral balances must not be independently
    maintained as authoritative frontend state.

19. Loyalty milestones should be driven by backend configuration.

20. Services and points calculation should be available
    to the Patient Application.

21. The points calculator is informational and must not
    create loyalty transactions.

22. Referral history and referral balance should be presented
    as distinct concepts.

23. Accessibility should be considered during development.

24. Performance optimization should be based on real requirements.

25. Security-sensitive information must never be exposed.

26. Secrets must never be stored in frontend code.

27. Testing should focus on important user journeys
    and business behavior.

28. Environment-specific configuration should be separated
    from source code.

29. Avoid premature abstraction.

30. Avoid over-engineering.

31. Prefer simple and understandable solutions.

32. The frontend should remain easy to maintain as the platform grows.
```

# Final Frontend Architecture

```text
                         SaaS Platform

                              │

                         .NET Web API

                              │

               ┌──────────────┴──────────────┐
               │                             │
               ▼                             ▼

       Admin Application             Patient Application
            Angular                       Flutter

               │                             │

       ┌───────┴────────┐           ┌────────┴─────────┐
       │                │           │                  │
 Authentication    Authorization  Authentication     Profile
 Patients           Loyalty       Loyalty            Points
 Services           Rewards       Services           Rewards
 Referrals          Analytics     Referrals          Reward Status
 Clinic Admin       Redemption    Points Calculator
                    Management

               │                             │
               └──────────────┬──────────────┘
                              │
                            HTTPS
                              │
                              ▼
                         Backend API
```

# Frontend Architecture Priority

The following areas are considered critical for the MVP:

```text
1. Clear separation between Admin and Patient applications

2. Reliable API communication

3. Authentication and authorization handling

4. Server-side business rule enforcement

5. Correct presentation of loyalty progress

6. Correct presentation of points and referral balances

7. Independent reward eligibility and redemption

8. Service catalog and points calculator

9. Consistent loading, error, empty, and success states

10. Simple feature-based architecture

11. Maintainable and testable frontend code

12. Appropriate performance

13. Secure handling of authentication and user data

14. Avoiding unnecessary abstraction and over-engineering
```

> **The frontend should be simple, predictable, maintainable, and focused on delivering a good user experience while treating the backend as the authoritative source for security, business rules, loyalty state, reward eligibility, and reward redemption.**
