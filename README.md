# Dental Loyalty Platform

A multi-tenant SaaS loyalty platform designed specifically for dental clinics.

The platform enables dental clinics to create and manage configurable loyalty programs, while patients can track their loyalty progress, earn stamps or points, and redeem rewards.

The project is being designed with a strong focus on **security, tenant isolation, maintainability, scalability, and clean architecture**.

---

## Project Vision

The goal is to build a reusable loyalty platform that allows multiple dental clinics to independently operate their own loyalty programs from a shared platform.

Each clinic should be able to:

* Configure its own loyalty rules.
* Define how patients earn stamps or points.
* Create and manage rewards.
* Manage patient loyalty accounts.
* Track loyalty activity.
* Manage staff access.
* Monitor reward redemptions.

Patients should be able to:

* Access their loyalty account.
* View their current balance.
* Track loyalty activity.
* See available rewards.
* Understand how they can earn additional loyalty benefits.
* Redeem eligible rewards.

The platform is **not intended to replace a dental practice management system or electronic health record system**.

---

## Core Principles

### Multi-Tenancy

The platform is designed as a multi-tenant SaaS application.

Each dental clinic represents a tenant.

Tenant data must always remain strictly isolated from other tenants.

The initial architecture is expected to use a shared database and shared schema with tenant identification applied to tenant-owned data.

### Security First

Security is a fundamental architectural requirement.

The system must ensure that:

* Users can only access resources they are authorized to access.
* Clinic users can only access data belonging to their clinic.
* Tenant identifiers supplied by clients are never trusted.
* Authorization is enforced server-side.
* Sensitive operations are auditable.
* Authentication and authorization are treated separately.
* Security-sensitive decisions are never delegated to the client application.

### Configurable Loyalty Rules

Loyalty behavior should be configurable by the clinic rather than hard-coded into the application.

Different clinics may have different:

* Earning rules.
* Reward requirements.
* Promotional rules.
* Referral rules.
* Redemption conditions.

---

## Planned Technology Stack

The exact technology choices may evolve during the architecture phase.

### Backend

* .NET / ASP.NET Core
* Entity Framework Core
* PostgreSQL or Azure SQL
* REST API
* Authentication & Authorization

### Frontend

* Flutter for the patient-facing mobile application
* Angular for the administrative interface

### Infrastructure

The initial deployment is expected to use cloud infrastructure.

Potential services include:

* Managed database
* Application hosting
* Identity provider
* Secure secrets management
* Logging and monitoring

Infrastructure decisions will be documented before implementation.

---

## Repository Structure

The repository is organized around the separation between product requirements, technical architecture, application code, and tests.

```text
dental-loyalty/
│
├── docs/
│   ├── PROJECT_PILLAR.md
│   ├── TECHNICAL_PILLAR.md
│   ├── ARCHITECTURE.md
│   └── ADR/
│
├── src/
│   ├── DentalLoyalty.Api/
│   ├── DentalLoyalty.Application/
│   ├── DentalLoyalty.Domain/
│   └── DentalLoyalty.Infrastructure/
│
├── tests/
│   ├── DentalLoyalty.UnitTests/
│   └── DentalLoyalty.IntegrationTests/
│
├── README.md
└── .gitignore
```

The final structure may change as architectural decisions are made.

---

## Architecture Documentation

The project uses architecture and product documentation as a source of truth.

### Project Pillar

`docs/PROJECT_PILLAR.md`

Defines:

* Product vision.
* Business goals.
* Target users.
* Core workflows.
* Product boundaries.
* Business rules.
* MVP scope.
* Product constraints.

### Technical Pillar

`docs/TECHNICAL_PILLAR.md`

Defines:

* Architecture principles.
* Technology decisions.
* Application boundaries.
* Multi-tenancy strategy.
* Security requirements.
* Data architecture.
* API principles.
* Testing strategy.
* Deployment principles.
* Development rules.

### Architecture Decision Records

`docs/ADR/`

Architectural decisions that require explicit justification should be documented as ADRs.

Examples:

```text
001-multi-tenancy.md
002-authentication.md
003-database-strategy.md
004-authorization.md
```

---

## Multi-Tenant Model

The initial architecture is expected to follow a shared application and shared database model.

Conceptually:

```text
                    ┌─────────────────────┐
                    │   Dental Loyalty    │
                    │      Platform       │
                    └──────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │      .NET API       │
                    └──────────┬──────────┘
                               │
                 ┌─────────────┴─────────────┐
                 │                           │
          ┌──────▼──────┐             ┌──────▼──────┐
          │  Clinic A   │             │  Clinic B   │
          │   Tenant    │             │   Tenant    │
          └─────────────┘             └─────────────┘
```

Tenant isolation must be enforced by the backend.

A client must never be able to access another clinic's data simply by changing a tenant identifier in a request.

---

## Security Requirements

Security requirements include, but are not limited to:

* Strong authentication.
* Role-based authorization.
* Tenant-aware authorization.
* Server-side authorization checks.
* Secure secret management.
* Input validation.
* Protection against unauthorized tenant access.
* Audit logging for sensitive operations.
* Secure handling of personal data.
* Principle of least privilege.

The platform should avoid storing clinical or medical information unless a future requirement explicitly introduces such functionality.

---

## Development Philosophy

The project is being developed with the following principles:

1. **Architecture before implementation.**
2. **Business rules before technical solutions.**
3. **Security by design.**
4. **Explicit tenant isolation.**
5. **Simple solutions before unnecessary complexity.**
6. **Clear separation of responsibilities.**
7. **Test business-critical behavior.**
8. **Document important architectural decisions.**
9. **Avoid premature optimization.**
10. **Keep the system maintainable by a small engineering team.**

---

## AI-Assisted Development

AI tools may be used throughout development as engineering assistants.

AI-generated code must follow the project's documented architecture and business rules.

The following documents represent the primary source of truth:

```text
PROJECT_PILLAR.md
        ↓
TECHNICAL_PILLAR.md
        ↓
ARCHITECTURE.md / ADRs
        ↓
Implementation
```

AI must not introduce architectural changes that contradict these documents without explicitly identifying the conflict and proposing an architectural decision.

Important architectural decisions should be reviewed and documented before implementation.

---

## Project Status

**Current Phase: Product & Architecture Definition**

The project is currently focused on:

* Defining the product vision.
* Defining the MVP.
* Modeling the loyalty domain.
* Defining multi-tenancy.
* Defining security requirements.
* Establishing the technical architecture.
* Preparing the development environment.

Implementation will begin after the initial product and technical foundations are sufficiently defined.

---

## Roadmap

### Phase 1 — Product Definition

* [x] Define initial product concept
* [ ] Define product requirements
* [ ] Define core user journeys
* [ ] Define loyalty rules
* [ ] Define MVP scope

### Phase 2 — Architecture

* [ ] Create Project Pillar
* [ ] Create Technical Pillar
* [ ] Define system architecture
* [ ] Define domain model
* [ ] Define multi-tenancy strategy
* [ ] Define authentication and authorization
* [ ] Define database architecture
* [ ] Document key ADRs

### Phase 3 — Foundation

* [ ] Create .NET solution
* [ ] Configure project structure
* [ ] Configure database
* [ ] Configure authentication
* [ ] Implement tenant resolution
* [ ] Implement authorization
* [ ] Establish testing infrastructure
* [ ] Establish CI/CD

### Phase 4 — MVP

* [ ] Clinic management
* [ ] Patient management
* [ ] Loyalty program configuration
* [ ] Loyalty earning
* [ ] Rewards
* [ ] Reward redemption
* [ ] Loyalty history
* [ ] Patient mobile application
* [ ] Administrative application

### Phase 5 — Pilot

* [ ] Deploy pilot environment
* [ ] Onboard first clinic
* [ ] Validate real-world workflows
* [ ] Collect feedback
* [ ] Improve UX and business rules
* [ ] Validate scalability and security assumptions

---

## Long-Term Direction

The initial focus is dental clinics, but the architecture should avoid unnecessary coupling to a single clinic implementation.

If the product proves successful, the platform may evolve to support:

* Advanced loyalty campaigns.
* Referral programs.
* Analytics and reporting.
* Notifications.
* Marketing integrations.
* Additional healthcare or service-based businesses.
* More sophisticated reward systems.

Future functionality must be evaluated against the original product vision and architectural principles before being introduced.

---

## License

License and distribution model will be defined before public release.
