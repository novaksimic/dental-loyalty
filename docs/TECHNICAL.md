# Dental Loyalty Platform — Technical Pillar

## 1. Technical Vision

Build a secure, maintainable, and simple multi-tenant SaaS platform that supports the MVP requirements without introducing unnecessary technical complexity.

The platform should be:

- Secure
- Simple
- Modular
- Testable
- Maintainable
- Sufficiently scalable for future growth

The architecture should solve current business requirements without prematurely solving problems that do not yet exist.

---

## 2. Technical Principles

### 2.1 Simplicity Over Complexity

If two technical solutions satisfy the same business requirement, the simpler solution should be preferred.

The system should avoid unnecessary:

- Abstractions
- Infrastructure
- Services
- Dependencies
- Configuration
- Frameworks
- Distributed components

### 2.2 Security by Design

Security is a core requirement and must be considered from the beginning of the project.

Security must not be treated as a feature that is added after implementation.

Particular attention must be given to:

- Patient data
- Tenant isolation
- Authentication
- Authorization
- Administrative operations
- Cloud infrastructure
- Secrets
- API security

### 2.3 Tenant Isolation

Each dental clinic represents a separate tenant.

A user belonging to one clinic must never be able to access data belonging to another clinic.

Tenant isolation is a security-critical requirement.

### 2.4 Separation of Concerns

Business logic must remain separated from:

- HTTP/API concerns
- Database access
- Authentication infrastructure
- External services
- Infrastructure implementation details

### 2.5 Maintainability

The system should be designed so that business rules can evolve without requiring large changes across unrelated parts of the system.

### 2.6 Testability

Core business logic must be testable independently from:

- Database
- HTTP
- External services
- Infrastructure

---

# 3. System Overview

The platform consists of two primary client applications and a backend API.

```text
                    ┌───────────────────┐
                    │   Patient App     │
                    └─────────┬─────────┘
                              │
                              │ HTTPS
                              ▼
                    ┌───────────────────┐
                    │                   │
                    │    .NET API       │
                    │                   │
                    └─────────┬─────────┘
                              │
             ┌────────────────┼────────────────┐
             │                │                │
             ▼                ▼                ▼
        Identity          Loyalty          Analytics
             │                │                │
             └────────────────┼────────────────┘
                              │
                              ▼
                       ┌─────────────┐
                       │  Database   │
                       └─────────────┘
                              ▲
                              │
                       ┌──────┴──────┐
                       │  Admin App  │
                       └─────────────┘
```

This is a logical system overview and does not represent the final physical deployment architecture.

The system consists of:

- Patient Application
- Admin Application
- .NET Backend API
- Relational Database
- Identity and authentication mechanisms
- Loyalty functionality
- Analytics functionality

The client applications communicate with the backend through secured HTTPS APIs.

The backend is responsible for:

- Authentication
- Authorization
- Tenant isolation
- Business logic
- Loyalty calculations
- Reward progression
- Referral management
- Patient management
- Data persistence
- Analytics data

The database represents the authoritative source of persisted application state.

Client applications must not access the database directly.

---

## 4. Application Architecture

The MVP will use a **Modular Monolith** architecture.

The backend will initially be deployed as a single application with clearly separated business modules.

Conceptually:

```text
Dental Loyalty API
│
├── Identity
├── Clinics
├── Patients
├── Loyalty
├── Rewards
├── Referrals
└── Analytics
```

The system will not use microservices for the MVP.

A Modular Monolith provides:

- Simpler development
- Simpler deployment
- Easier debugging
- Lower infrastructure overhead
- Clear module boundaries
- Easier maintenance
- A path toward future service extraction if genuinely required

Microservices should only be introduced when justified by real system requirements.

The architecture should allow modules to remain logically separated even though they are initially deployed as one application.

---

## 5. Technology Stack

The initial technology stack is:

### 5.1 Backend

- .NET
- ASP.NET Core Web API
- Entity Framework Core

### 5.2 Database

- Relational database

### 5.3 Authentication

- JWT-based authentication

### 5.4 Authorization

- Role-based authorization
- Tenant-based authorization

### 5.5 Client Applications

- Patient Application
- Admin Application

The exact frontend technologies and infrastructure providers remain open technical decisions and will be finalized before implementation.

Technology choices should prioritize:

- Simplicity
- Maintainability
- Security
- Developer productivity
- Long-term support
- Compatibility with the overall architecture

The project should avoid introducing technologies that do not provide clear value for the MVP.

---

## 6. Backend Architecture

The backend should follow a clear separation of responsibilities.

Conceptually:

```text
API
 ↓
Application
 ↓
Domain
 ↓
Infrastructure
```

The exact .NET solution/project structure will be defined later in `ARCHITECTURE.md`.

### 6.1 API Layer

The API layer is responsible for:

- HTTP requests
- HTTP responses
- Authentication integration
- Authorization integration
- Request validation
- API contracts
- Mapping external requests to application operations

The API layer should not contain core business rules.

### 6.2 Application Layer

The Application layer is responsible for:

- Use cases
- Application services
- Commands
- Queries
- DTOs
- Application orchestration

The Application layer coordinates business operations without depending on infrastructure-specific implementations.

### 6.3 Domain Layer

The Domain layer is responsible for:

- Entities
- Value objects
- Domain logic
- Business rules
- Domain invariants
- Loyalty rules

The Domain layer must remain independent from:

- ASP.NET Core
- EF Core
- Database implementation
- External services
- HTTP concerns

Core loyalty business rules should be implemented in a way that allows them to be tested independently from infrastructure.

### 6.4 Infrastructure Layer

The Infrastructure layer is responsible for:

- Entity Framework Core
- Database access
- Persistence
- External services
- Infrastructure implementations
- Authentication infrastructure where applicable

Infrastructure details must not leak into the Domain layer.

The goal is to keep infrastructure replaceable where practical without affecting the core business logic.

---

## 7. Frontend Applications

The platform contains two primary applications:

- Patient Application
- Admin Application

Both applications communicate with the backend through the secured API.

Neither application should directly access the database.

### 7.1 Patient Application

The Patient Application focuses on simplicity and clarity.

It should provide:

- User registration
- Authentication
- Patient profile
- Points
- Referrals
- Rewards
- Loyalty progression
- Reward status
- Loyalty cycle information

The Patient Application should clearly communicate:

- Current points
- Referral progress
- Current reward
- Next reward
- Locked rewards
- Unlocked rewards
- Redeemed rewards
- Progress toward completing the loyalty cycle

The Patient Application should not contain authoritative business logic.

The backend remains the source of truth for loyalty state and business rules.

### 7.2 Admin Application

The Admin Application is used by:

- Platform Admin
- Clinic Admin
- Clinic Staff

depending on the user's authorization level.

The Admin Application should provide functionality for:

- Authentication
- Patient management
- Loyalty management
- Points management
- Referral management
- Reward management
- Reward redemption
- Loyalty configuration
- Analytics
- Clinic administration

The Admin Application should remain simple and easy for non-technical clinic staff to use.

Administrative actions that affect loyalty state must be validated and processed by the backend.

---

## 8. Data Architecture

The platform will use a relational database.

Tenant-specific data must have a clear relationship to the clinic/tenant that owns it.

Conceptually:

```text
Clinic
   ↓
Users
   ↓
Patients
   ↓
Loyalty Data
```

A clinic may contain:

```text
Clinic
 ├── Patients
 ├── Clinic Admins
 ├── Clinic Staff
 ├── Loyalty Configuration
 ├── Services
 ├── Rewards
 ├── Referrals
 └── Analytics Data
```

Tenant context must be applied consistently to all tenant-specific data access.

The database is the authoritative source of persisted application state.

The application must ensure that tenant-specific data cannot be accessed without a valid tenant context.

---

## 9. Multi-Tenancy

Multi-tenancy is a core requirement of the platform.

Each dental clinic represents a separate tenant.

The preferred MVP approach is:

> Shared database / shared schema with strict TenantId-based isolation.

Tenant-specific entities should contain a `TenantId` or equivalent tenant relationship.

Example:

```text
Patient
---------
Id
TenantId
FirstName
LastName
...
```

The exact database implementation remains subject to security and architecture validation.

### 9.1 Tenant Resolution

The backend must determine the current tenant from the authenticated user context.

The client must not be trusted to determine which tenant it belongs to.

The system must not rely on a client-provided tenant identifier as the primary security mechanism.

For example, the following approach must not be considered sufficient for tenant isolation:

```http
GET /patients?tenantId=123
```

Instead, the backend should resolve the tenant through the authenticated user's context:

```text
Authenticated User
        ↓
User's Tenant
        ↓
Tenant Context
        ↓
Allowed Data
```

The tenant context must then be applied to all tenant-specific operations.

### 9.2 Tenant Isolation Requirement

A user belonging to Clinic A must never be able to access:

- Patients from Clinic B
- Loyalty data from Clinic B
- Rewards from Clinic B
- Referrals from Clinic B
- Configuration from Clinic B
- Analytics from Clinic B

Tenant isolation must be enforced server-side.

Tenant isolation is a security-critical requirement.

### 9.3 Tenant Data Ownership

Every tenant-specific record must have a clear ownership relationship with its clinic.

The application must avoid ambiguous ownership models.

When retrieving, creating, updating, or deleting tenant-specific data, the backend must ensure that the operation is performed within the authenticated user's tenant context.

---

## 10. Authentication & Authorization

The platform will use token-based authentication.

The initial authentication approach is:

> JWT-based authentication

The system should support at least the following roles:

```text
PlatformAdmin
ClinicAdmin
ClinicStaff
Patient
```

Authorization must use both:

- Role-based authorization
- Tenant-based authorization

Role authorization alone is insufficient.

Example:

```text
ClinicAdmin
    +
Clinic A tenant context
    ↓
Clinic A data only
```

A Clinic Admin must not gain access to another clinic's data simply because the user has the Clinic Admin role.

### 10.1 Authentication

Authentication is responsible for establishing the identity of the user.

The backend must validate:

- Token signature
- Token validity
- Token expiration
- Required claims
- Authentication context

Unauthenticated users must not be able to access protected resources.

### 10.2 Authorization

Authorization determines whether an authenticated user is allowed to perform a specific operation.

Authorization must consider:

1. User identity
2. User role
3. Tenant context
4. Requested resource
5. Requested operation

Example:

```text
Clinic Admin
    ↓
Clinic A
    ↓
Can manage Clinic A patients
    ↓
Cannot manage Clinic B patients
```

### 10.3 Platform Admin

The Platform Admin operates at the platform level.

The Platform Admin may:

- Create clinics
- Create the initial Clinic Admin
- Provision default loyalty configuration
- Perform platform-level administration

Platform Admin access must be carefully separated from normal clinic-level access.

### 10.4 Clinic Admin

The Clinic Admin operates within a single clinic tenant.

The Clinic Admin may:

- Manage patients
- Manage loyalty activity
- Manage points
- Manage referrals
- Manage rewards
- Configure supported loyalty parameters
- Manage clinic staff
- View analytics

Clinic Admin operations must always be restricted to the administrator's tenant.

### 10.5 Clinic Staff

Clinic Staff operate within a single clinic tenant.

Their permissions should be limited to the operational activities required by the clinic.

Clinic Staff should not automatically receive configuration or platform-level permissions.

The exact permissions will be finalized during authorization design.

### 10.6 Patient

Patients operate within their own patient context.

A Patient may access:

- Their own profile
- Their own points
- Their own referrals
- Their own rewards
- Their own loyalty progression

A Patient must never be able to access another patient's data.

---

## 11. Security

Security is a core technical requirement.

The platform must protect:

- Patient data
- Authentication credentials
- Loyalty data
- Clinic data
- Administrative operations
- Tenant-specific information

Security must be considered throughout the entire application lifecycle.

### 11.1 Security Principles

The platform should follow:

- Least privilege
- Defense in depth
- Secure defaults
- Server-side authorization
- Tenant isolation
- Input validation
- Secure secret management
- Data minimization
- Auditable administrative actions

### 11.2 Transport Security

All communication between client applications and backend services must use HTTPS.

Sensitive data must never be transmitted over unencrypted HTTP.

### 11.3 Password Security

Passwords must never be stored in plain text.

Passwords must be securely hashed using an industry-standard password hashing mechanism.

The application must never log passwords or authentication secrets.

### 11.4 Token Security

JWT tokens must be:

- Cryptographically signed
- Validated by the backend
- Time-limited
- Protected from tampering

The backend must never trust claims without validating the token itself.

### 11.5 API Security

All protected API endpoints must enforce authentication and authorization.

The backend must validate:

- Authentication
- Authorization
- Tenant access
- Input data
- Business rules

The frontend must never be considered a trusted security boundary.

### 11.6 Input Validation

All externally supplied input must be validated.

Validation should protect against:

- Invalid data
- Unexpected values
- Malformed requests
- Injection attacks
- Invalid business operations

Business validation must remain on the backend even if similar validation exists in the frontend.

### 11.7 Tenant Security

Tenant isolation must be enforced on the backend.

The application must never rely on the frontend to prevent cross-tenant access.

Every tenant-specific operation must execute within a validated tenant context.

Cross-tenant access attempts must be rejected.

### 11.8 Sensitive Data

The platform should minimize the amount of sensitive data it stores.

Only data required for the operation of the platform should be persisted.

Sensitive information must not be unnecessarily exposed through:

- API responses
- Logs
- Error messages
- Client-side storage

### 11.9 Secrets Management

Secrets must not be hardcoded in source code.

Secrets may include:

- Database credentials
- JWT signing keys
- API keys
- External service credentials
- Cloud credentials

Secrets must be managed through appropriate environment/configuration mechanisms and secure secret storage.

### 11.10 Administrative Operations

Administrative operations that modify important patient or loyalty data should be auditable.

Examples include:

- Manual point changes
- Referral changes
- Reward redemption
- Loyalty configuration changes
- Patient data changes

The exact audit mechanism will be defined in `SECURITY_PILLAR.md`.

### 11.11 Security Scope

Detailed security requirements and implementation decisions will be defined separately in:

```text
docs/SECURITY_PILLAR.md
```

The Technical Pillar establishes the architectural requirement that security and tenant isolation must be built into the system from the beginning.

---

## 12. API Design

The backend will expose a REST API.

The API should be:

- Predictable
- Resource-oriented
- Consistent
- Versionable
- Documented
- Secured

Example API structure:

```text
/api/v1/patients
/api/v1/loyalty
/api/v1/rewards
/api/v1/referrals
/api/v1/analytics
```

The exact endpoint structure will be defined during implementation.

Client applications must communicate with the backend through the API.

Frontend applications must not directly access the database.

---

## 13. Business Logic

Core loyalty business logic must be centralized in the backend.

The frontend must not be the source of truth for:

- Points
- Referrals
- Reward unlocking
- Reward redemption
- Loyalty cycle completion and reset
- Loyalty configuration

Example:

```text
Patient performs service
        ↓
Backend validates service
        ↓
Points calculated
        ↓
Loyalty state updated
        ↓
Reward eligibility evaluated
        ↓
Updated state persisted
        ↓
Patient App receives updated state
```

The backend/database represents the authoritative source of loyalty state.

Business rules should be implemented independently from infrastructure concerns where practical.

Changes to loyalty rules should not require changes throughout unrelated parts of the system.

---

## 14. Configuration

The loyalty program will provide a default configuration.

The Clinic Admin should be able to modify supported configuration parameters when required.

Supported configuration may include:

- Scoring configuration per clinic: spend amount in RSD and points awarded (default 100 RSD = 1,000 points, `BUSINESS.md` §3.1)
- Service prices (service points are calculated from the price using the clinic's scoring configuration, `BUSINESS.md` §3)
- Reward thresholds
- Referral requirements
- Reward definitions
- Loyalty cycle configuration

The system should not implement a fully generic rules engine unless future business requirements justify it.

Configuration should remain:

- Explicit
- Understandable
- Validated
- Maintainable

The platform should provide sensible defaults so that a newly provisioned clinic can operate without extensive technical configuration.

---

## 15. Data Consistency & Synchronization

The Patient Application and Admin Application must display consistent loyalty state.

When an administrative action occurs:

```text
Admin Action
     ↓
Backend
     ↓
Business Rules
     ↓
Database
     ↓
Updated Loyalty State
     ↓
Patient App
```

The backend and database are the source of truth.

Frontend applications must not maintain an authoritative copy of loyalty state.

Examples of synchronized state include:

- Points
- Referrals
- Reward status
- Reward redemption
- Loyalty progress
- Loyalty cycle state

Any operation that changes loyalty state must be persisted through the backend.

---

## 16. Error Handling

The API should provide a consistent error response format.

Errors should be categorized into:

- Validation errors
- Authentication errors
- Authorization errors
- Business rule violations
- Not found
- Conflict
- Unexpected/system errors

Internal exception details must not be exposed to clients.

User-facing errors should be clear and safe.

Technical details should be available through server-side logging rather than API responses.

The system should avoid exposing sensitive information through error responses.

---

## 17. Logging & Observability

The system should use structured logging.

Logs should provide enough context to troubleshoot application behavior.

Relevant context may include:

- Request identifier
- User identifier
- Tenant identifier
- Operation
- Error information
- Relevant system context

Sensitive patient information must not be logged unnecessarily.

Observability should remain simple for the MVP while still providing enough information for:

- Troubleshooting
- Error investigation
- Monitoring
- Operational support

The system should provide enough logging to identify failures without creating excessive operational complexity.

---

## 18. Testing Strategy

Testing will focus primarily on business-critical functionality.

### 18.1 Unit Tests

Unit tests should cover:

- Points calculation
- Referral rules
- Reward thresholds
- Reward unlocking
- Reward redemption
- Loyalty cycle completion and reset
- Loyalty progression
- Business rule validation

Business logic should be testable without requiring the database or HTTP layer.

### 18.2 Integration Tests

Integration tests should cover:

- Database operations
- API behavior
- Authentication
- Authorization
- Tenant isolation
- Loyalty workflows

Tenant isolation should be explicitly tested.

Example:

```text
Clinic A User
      ↓
Attempts to access
Clinic B data
      ↓
Access denied
```

### 18.3 End-to-End Tests

End-to-end tests should cover the most important user journeys.

Examples:

- Patient registration
- Patient loyalty progression
- Referral flow
- Reward unlocking
- Reward redemption
- Loyalty cycle completion and reset
- Clinic Admin management

The MVP does not require exhaustive E2E coverage of every possible scenario.

---

## 19. Deployment & Infrastructure

The platform will be cloud-hosted as a SaaS product.

MVP infrastructure should remain simple.

The system should provide:

- Application hosting
- Database hosting
- Secure secrets management
- HTTPS
- Logging
- Database backups
- Basic monitoring

The cloud provider and exact infrastructure implementation remain open decisions.

Infrastructure should not be more complex than required by the MVP.

The initial deployment should favor reliability and simplicity over distributed infrastructure.

---

## 20. Scalability

The platform should be sufficiently scalable to support growth in:

- Number of clinics
- Number of patients
- Number of loyalty transactions
- Number of API requests

The MVP does not require a distributed architecture.

The initial architecture should be:

> Modular Monolith + Relational Database + Cloud Deployment

Scaling strategies should be introduced based on real system requirements and measured workload.

Potential future approaches may include:

- Horizontal application scaling
- Caching
- Background processing
- Read optimization
- Database optimization
- Service extraction

Premature optimization should be avoided.

---

## 21. Maintainability

Maintainability is a primary technical goal.

The codebase should be:

- Modular
- Readable
- Testable
- Predictable
- Consistent
- Easy to change

Business rules should remain isolated from infrastructure concerns.

A change to a loyalty rule should not require modifications across unrelated parts of the system.

Example:

Changing:

```text
100 RSD → 1,000 points
```

to:

```text
200 RSD → 1,000 points
```

should be isolated to the relevant loyalty configuration/business logic rather than requiring widespread changes throughout the application.

The architecture should favor clear and explicit code over unnecessary abstractions.

---

## 22. Technical Constraints

The MVP should follow these constraints:

- Avoid unnecessary microservices
- Avoid unnecessary infrastructure
- Avoid premature optimization
- Avoid generic frameworks without a business need
- Avoid over-engineering
- Keep deployment simple
- Keep development simple
- Keep maintenance simple
- Keep the architecture understandable
- Do not introduce complexity without a measurable benefit

Security and tenant isolation are exceptions where additional complexity is justified.

The platform should be designed for the actual MVP requirements rather than hypothetical large-scale requirements.

---

## 23. Future Considerations

Potential future improvements include:

- Advanced analytics
- Automated tenant onboarding
- Subscription management
- Email notifications
- SMS notifications
- Push notifications
- More sophisticated loyalty configuration
- Additional clinic roles
- Advanced reporting
- Independent scaling of platform modules
- Service extraction into microservices if justified
- Additional external integrations

These are not MVP requirements.

Future functionality should only be introduced when supported by real business requirements.

The architecture should provide reasonable extension points without implementing future functionality prematurely.

---

## 24. Open Technical Decisions

The following decisions remain open and should be finalized before implementation:

- Exact frontend technology for Patient Application
- Exact frontend technology for Admin Application
- Exact relational database
- Cloud provider
- Identity provider / JWT implementation
- Deployment strategy
- CI/CD solution
- Monitoring solution
- Backup strategy
- Exact .NET solution/project structure
- Exact multi-tenant database implementation
- API versioning strategy
- External services and integrations

These decisions should be made based on:

- MVP requirements
- Security requirements
- Maintainability
- Development complexity
- Operational complexity
- Cost
- Future growth requirements

No technical decision should be finalized solely for the purpose of adding complexity or prematurely optimizing for future scale.