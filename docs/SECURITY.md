# Security Pillar

## 1. Security Principles

Security is a core architectural requirement of the platform.

The platform must protect:

- Patient data
- Authentication credentials
- Loyalty data
- Clinic data
- Administrative operations
- Tenant-specific information

The security model is based on:

- Least privilege
- Defense in depth
- Secure defaults
- Server-side authorization
- Tenant isolation
- Input validation
- Secure secret management
- Data minimization
- Auditable administrative actions

The MVP should use proven security mechanisms provided by the .NET framework, database, cloud provider, and authentication infrastructure rather than introducing unnecessary custom security solutions.

---

## 2. Authentication Security

Authentication establishes the identity of a user.

The platform will use token-based authentication with JWT.

The backend must validate:

- User identity
- Token signature
- Token expiration
- Issuer
- Audience
- Required claims

Unauthenticated users must not access protected resources.

Authentication must remain separate from authorization.

> A successfully authenticated user is not automatically authorized to access every resource.

---

## 3. Authorization

Authorization determines whether an authenticated user can perform a specific operation.

Authorization must consider:

1. User identity
2. User role
3. Tenant context
4. Resource ownership
5. Requested operation
6. Business rules

Conceptually:

```text
Authenticated User
        ↓
Role
        ↓
Tenant / Ownership
        ↓
Permission
        ↓
Business Rules
        ↓
Allowed Operation
```

---

## 4. Tenant Isolation

Tenant isolation is a primary security requirement of the platform.

Each dental clinic represents a separate tenant.

A tenant must never be able to access, modify, or delete data belonging to another tenant.

Conceptually:

```text
Platform
   │
   ├── Tenant A
   │     ├── Patients
   │     ├── Points
   │     ├── Referrals
   │     ├── Rewards
   │     └── Analytics
   │
   ├── Tenant B
   │     ├── Patients
   │     ├── Points
   │     ├── Referrals
   │     ├── Rewards
   │     └── Analytics
   │
   └── Tenant C
         ├── Patients
         ├── Points
         ├── Referrals
         ├── Rewards
         └── Analytics
```

Tenant A must never access Tenant B or Tenant C data through normal application operations.

---

## 5. Role-Based Access Control (RBAC)

The MVP uses three roles:

```text
PlatformAdmin
ClinicAdmin
Patient
```

### 5.1 PlatformAdmin

PlatformAdmin operates at platform level.

May:

- Create clinics
- Create the initial ClinicAdmin
- Provision default loyalty configuration
- Perform platform-level administration

PlatformAdmin is not a normal clinic-level user.

PlatformAdmin operations must be explicitly separated from tenant-level operations.

### 5.2 ClinicAdmin

ClinicAdmin represents the administrator of one dental clinic.

ClinicAdmin may:

- Manage patients
- Manage points
- Manage referrals
- Manage rewards
- Confirm reward redemption
- Manage supported loyalty configuration
- View analytics
- Manage clinic-level data

ClinicAdmin is restricted to their own tenant.

There is no separate `ClinicStaff` role in the MVP.

ClinicAdmin is responsible for the administrative operations required by the clinic.

### 5.3 Patient

Patient may access:

- Own profile
- Own points
- Own referrals
- Own rewards
- Own loyalty progression
- Own reward status
- Own loyalty cycle information

Patient may not:

- Access another patient
- Modify points
- Modify referrals
- Manually unlock rewards
- Change reward status
- Change loyalty configuration
- Access the Admin Application
- Access another tenant

### 5.4 RBAC Permission Matrix

| Resource / Operation | PlatformAdmin | ClinicAdmin | Patient |
|---|---|---|---|
| Manage Clinics | Yes | No | No |
| Create initial ClinicAdmin | Yes | No | No |
| Manage Patients | Platform | Own Clinic | Own only |
| View Patient Data | Platform | Own Clinic | Own only |
| Manage Points | Platform | Own Clinic | No |
| Manage Referrals | Platform | Own Clinic | Own only |
| Manage Rewards | Platform | Own Clinic | Own only |
| Redeem Reward | Platform | Own Clinic | No |
| Loyalty Configuration | Platform | Own Clinic | No |
| View Analytics | Platform | Own Clinic | No |
| Platform Administration | Yes | No | No |
| Own Profile | No | Own account | Own account |

Role authorization never bypasses tenant isolation.

Authorization must always be enforced server-side.

The frontend may hide unavailable functionality, but the frontend is not a security boundary.

---

## 6. Multi-Tenant Security

Multi-tenancy is a core requirement of the SaaS platform.

The preferred MVP strategy is:

> Shared database / shared schema with strict TenantId-based isolation.

### 6.1 Tenant Context

Every authenticated clinic user must have a validated tenant context.

```text
Authenticated User
        ↓
User Identity
        ↓
User Tenant
        ↓
Tenant Context
        ↓
Tenant-scoped Operation
```

The client must not be trusted to determine which tenant it belongs to.

### 6.2 Tenant Resolution

The backend must resolve the tenant through a trusted authenticated user context.

```text
JWT
 ↓
User Identity
 ↓
User
 ↓
TenantId
 ↓
Tenant Context
```

### 6.3 Client Cannot Choose Tenant

The following must not be considered sufficient for tenant isolation:

```text
GET /api/v1/patients?tenantId=123
```

Changing `tenantId` in the client request must never allow access to another tenant.

### 6.4 Tenant-Scoped Data

Tenant-specific entities must have a clear relationship with their owning tenant.

Examples include:

- Patients
- Points
- Referrals
- Rewards
- Loyalty configuration
- Services
- Analytics
- Clinic users

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

### 6.5 Tenant-Scoped Operations

Tenant context must be enforced for:

- Read
- Create
- Update
- Delete

Example:

```text
ClinicAdmin A
      ↓
Update Patient #123
      ↓
Patient belongs to Clinic B
      ↓
Tenant mismatch
      ↓
Access Denied
```

### 6.6 Patient Tenant Isolation

A patient belongs to one tenant and can access only their own patient context.

```text
Patient
   ↓
Own Patient Account
   +
Own Tenant
```

### 6.7 Tenant Isolation Testing

Tenant isolation must be explicitly tested.

Example:

```text
Clinic A User
      ↓
Attempts to access Clinic B Patient
      ↓
Access Denied
```

Tests should cover:

- Read
- Create
- Update
- Delete
- Points
- Referrals
- Rewards
- Loyalty configuration
- Analytics

### 6.8 Tenant Security Principle

> No application operation may cross a tenant boundary unless it is an explicitly authorized platform-level operation.

---

## 7. Patient Data Protection

The platform processes patient data required for registration, identification, loyalty functionality, and clinic administration.

### 7.1 Data Minimization

Only data required for the platform should be stored.

Example:

```text
Patient
---------
Id
TenantId
FirstName
LastName
NickName
Email
...
```

The exact data model will be defined in `DATA_MODEL.md`.

### 7.2 Patient Access

Patients may access only their own data.

A patient must not gain access to another patient simply by changing a resource ID in an API request.

### 7.3 ClinicAdmin Access

ClinicAdmin may access patient data belonging to their own clinic only.

```text
ClinicAdmin A
      ↓
Tenant A
      ↓
Patients belonging to Tenant A
      ↓
Allowed
```

### 7.4 Sensitive Data Exposure

API responses should expose only data required for the requested operation.

The system should avoid returning complete records when only a subset of data is required.

### 7.5 Patient Data Modification

Patient data changes must be authorized by the backend.

Patients must not modify authoritative loyalty state such as:

```text
Points
Referrals
Reward status
Loyalty configuration
```

### 7.6 Loyalty Data Protection

Protected loyalty data includes:

- Points
- Referrals
- Rewards
- Reward status
- Loyalty progress
- Redemption history
- Loyalty cycle state

### 7.7 Patient Data in Logs

The system must avoid unnecessarily logging:

- Passwords
- Authentication tokens
- Secrets
- Sensitive patient data
- Complete request/response bodies containing sensitive information

### 7.8 Data Encryption

Data should be protected:

- In transit
- At rest

Client-to-backend communication must use HTTPS/TLS.

Database and storage encryption-at-rest should use the capabilities of the selected cloud/database infrastructure.

### 7.9 Data Retention

The platform should define appropriate retention rules for patient data.

Retention periods will depend on:

- Business requirements
- Legal requirements
- Data protection requirements
- Clinic requirements

### 7.10 Patient Data Deletion

Patient deletion must be controlled because loyalty history, referrals, rewards, analytics, and clinic records may depend on the patient.

Cascade deletion must not be introduced without understanding its consequences.

### 7.11 Analytics

Analytics should use only the patient information necessary to produce business metrics.

Examples:

- Returning patients
- New patients
- Loyalty program users
- Reward redemptions
- Referral activity

ClinicAdmin may view analytics only for their own clinic.

### 7.12 PlatformAdmin Access

Platform-level privileges must not automatically imply unrestricted access to all patient information.

Any support or administrative access to patient data must be explicitly defined and auditable.

---

## 8. Password Security

Passwords must never be stored in plain text.

### 8.1 Password Hashing

Passwords must be securely hashed using an industry-standard password hashing mechanism.

```text
User Password
     ↓
Password Hashing
     ↓
Stored Password Hash
```

### 8.2 Password Storage

The system must never store:

- Plain-text passwords
- Reversible encrypted passwords
- Passwords in logs
- Passwords in API responses

### 8.3 Password Requirements

The application should enforce reasonable password requirements.

The exact policy will be defined during authentication implementation.

### 8.4 Password Reset

Password reset must:

- Verify the user's identity
- Use a temporary, time-limited reset mechanism
- Never expose the existing password
- Invalidate the reset mechanism after successful use

### 8.5 Password Changes

Authenticated users should be able to change their password through the appropriate application flow.

> Passwords are never stored or exposed in plain text.

---

## 9. JWT & Token Security

The platform will use JWT-based authentication for API access.

### 9.1 Token Validation

Every protected API request must validate the JWT.

The backend must validate:

- Token signature
- Token expiration
- Issuer
- Audience
- Required claims

### 9.2 Token Claims

JWT should contain only claims required by the application.

Relevant claims may include:

```text
UserId
Role
TenantId
```

The backend must validate that the user actually belongs to the tenant represented by the authentication context.

### 9.3 Token Lifetime

Access tokens should have a limited lifetime.

The exact expiration period will be defined during authentication implementation.

### 9.4 Token Signing

JWT signing keys must:

- Never be hardcoded
- Never be committed to Git
- Be stored securely
- Support rotation when required

### 9.5 Token Storage

Token storage must follow the security requirements of the selected frontend architecture.

The exact storage mechanism will be finalized during implementation.

### 9.6 Authorization

A valid JWT does not automatically grant access.

```text
JWT
 ↓
User
 ↓
Role
 ↓
Tenant / Ownership
 ↓
Permission
```

> Valid token does not mean unrestricted access.

---

## 10. API Security

The backend API is the primary security boundary of the platform.

### 10.1 Authentication

Protected endpoints must require authentication.

```text
Request
  ↓
Authentication
  ↓
Valid JWT?
  ├── No → 401 Unauthorized
  └── Yes → Continue
```

### 10.2 Authorization

The API must verify:

- Role
- Tenant
- Ownership
- Permission
- Business rules

### 10.3 Input Validation

All client-provided input must be validated on the backend.

### 10.4 Resource Access

Resources must never be accessible simply because the client knows their ID.

### 10.5 HTTPS

Production API communication must use HTTPS.

### 10.6 Rate Limiting

Rate limiting should be applied where abuse could affect security or availability, especially for:

- Login
- Registration
- Password reset
- Authentication endpoints
- Public endpoints

### 10.7 Error Responses

The API must return appropriate HTTP status codes and user-friendly error messages.

Error responses must provide enough information for the client application to correctly handle the error and display an understandable message.

Error messages exposed to users must be:

- Clear
- User-friendly
- Actionable where appropriate
- Non-technical
- Consistent

The API must not expose:

- Stack traces
- Internal exceptions
- Database errors
- Infrastructure details
- Sensitive information
- Internal implementation details

Example:

```text
HTTP 400 Bad Request
```

```json
{
  "message": "The selected service is not available."
}
```

Instead of:

```text
SqlException: Cannot insert duplicate key...
```

For unexpected errors:

```text
HTTP 500 Internal Server Error
```

```json
{
  "message": "Something went wrong. Please try again later."
}
```

Technical details must be logged server-side.

Frontend error handling is responsible for presentation and user experience, while backend error handling is responsible for classification and safe API responses.

> Backend owns error classification and safe API responses. Frontend owns presentation and user experience.

### 10.8 API Security Principle

> Never trust the client.

---

## 11. Input Validation & Business Rule Protection

All data received from client applications must be validated by the backend.

Frontend validation is useful for user experience but is never authoritative.

### 11.1 Input Validation

Backend validation must verify:

- Required fields
- Data types
- Allowed values
- String lengths
- Numeric ranges
- Valid identifiers
- Business constraints

### 11.2 Business Rule Validation

Business rules must be enforced on the backend.

Examples:

```text
Patient cannot manually increase points
Patient cannot manually unlock rewards
Patient cannot redeem a locked reward
Patient cannot modify referral count
```

### 11.3 Loyalty State Protection

Authoritative loyalty data includes:

- Points
- Referrals
- Reward status
- Reward eligibility
- Redemption status
- Loyalty cycle
- Loyalty configuration

Client-provided values must not automatically overwrite authoritative state.

### 11.4 Validation Before Persistence

```text
Request
   ↓
Authentication
   ↓
Authorization
   ↓
Input Validation
   ↓
Business Rules
   ↓
Persistence
```

If validation fails, the operation must not modify persistent data.

> The client can request an operation, but only the backend can authorize and validate it.

---

## 12. Secrets & Configuration Management

Application secrets and sensitive configuration must never be stored directly in source code or committed to Git.

### 12.1 Secrets

Secrets may include:

- Database credentials
- JWT signing keys
- API keys
- External service credentials
- Cloud credentials

### 12.2 Source Control

The following must never be committed:

```text
Passwords
JWT signing keys
API keys
Database connection strings containing credentials
Cloud credentials
Private keys
```

### 12.3 Environment Configuration

Configuration must support separate environments:

```text
Development
Testing
Staging
Production
```

Environment-specific configuration must not require source-code changes.

### 12.4 Secret Rotation

Important secrets should be replaceable without modifying application code.

### 12.5 Configuration vs Secrets

Normal configuration may include:

```text
Application name
API URL
Feature flags
Logging level
```

Sensitive values are secrets:

```text
Database password
JWT signing key
API secret
```

> Secrets belong to the environment, not to the source code.

---

## 13. Data Protection & Encryption

The platform must protect data during transmission and while stored.

### 13.1 Data in Transit

Communication between:

- Patient Application
- Admin Application
- Backend API
- External services

must use HTTPS/TLS.

### 13.2 Data at Rest

Database and persistent storage should use encryption-at-rest provided by the selected infrastructure.

### 13.3 Encryption Scope

The MVP should avoid custom encryption mechanisms when standard infrastructure capabilities are sufficient.

> Use proven, standard encryption mechanisms rather than implementing custom cryptography.

---

## 14. Audit Logging

Important administrative and security-sensitive operations should be auditable.

Examples:

- Manual point changes
- Referral changes
- Reward redemption
- Loyalty configuration changes
- Patient data changes
- Authentication/security events

Audit records may contain:

```text
UserId
TenantId
Action
Resource
Timestamp
RequestId
```

Sensitive information should not be unnecessarily stored in audit logs.

Audit logging should remain simple for the MVP.

---

## 15. Security Logging & Monitoring

The platform should use structured server-side logging.

Logs should help identify:

- Authentication failures
- Authorization failures
- Unexpected errors
- Database failures
- Security-related events
- Application failures

Relevant context may include:

```text
RequestId
UserId
TenantId
Operation
Timestamp
Error
```

Sensitive patient data, passwords, tokens, and secrets must not be unnecessarily logged.

The MVP should provide basic monitoring without unnecessary infrastructure complexity.

---

## 16. Rate Limiting & Abuse Protection

The API should provide rate limiting where abuse could affect security or availability.

Particular attention should be given to:

- Login
- Registration
- Password reset
- Authentication endpoints
- Public API endpoints

Rate limits should protect the platform without negatively affecting normal clinic or patient usage.

Exact limits will be defined during implementation.

---

## 17. CORS & Client Access

The backend must explicitly control which client applications are allowed to communicate with the API.

CORS configuration should allow only trusted application origins.

Production environments should not use unrestricted configurations such as:

```text
AllowAnyOrigin
```

unless there is a clearly justified requirement.

CORS is not an authentication mechanism and must not replace authorization.

---

## 18. Dependency & Package Security

Application dependencies must be kept reasonably up to date.

The project should:

- Use trusted packages
- Avoid unnecessary dependencies
- Monitor known vulnerabilities
- Remove unused dependencies
- Update security-sensitive dependencies when required

Dependencies should be reviewed before being introduced.

The MVP should avoid adding libraries when existing framework capabilities are sufficient.

---

## 19. Database Security

Database access must occur only through the backend.

Client applications must never connect directly to the database.

The database must:

- Require authentication
- Use secure credentials
- Restrict network access
- Use least-privilege permissions
- Support encryption at rest
- Have regular backups

Application users should not receive direct database credentials.

---

## 20. Backup & Recovery

Production data must be backed up regularly.

Backups should cover:

- Patient data
- Loyalty data
- Rewards
- Referrals
- Clinic configuration

The system should define:

- Backup frequency
- Backup retention
- Recovery procedure
- Recovery testing

Managed cloud/database backup capabilities should be preferred where appropriate.

> A backup is not sufficient unless the data can actually be restored.

---

## 21. Security Testing

Security must be included in normal development and testing.

### Authentication

Test:

- Invalid credentials
- Expired tokens
- Invalid tokens
- Unauthorized access

### Authorization

Test:

- Incorrect role
- Missing permissions
- Patient accessing another patient
- ClinicAdmin accessing another clinic

### Tenant Isolation

```text
Clinic A
   ↓
Attempts to access
Clinic B
   ↓
Denied
```

### Input Security

Test:

- Invalid input
- Unexpected values
- Malformed requests
- Injection attempts

Security testing should focus on realistic threats to the MVP.

---

## 22. Secure Development Practices

Security should be considered throughout development.

Developers should follow:

- Code review
- Secure dependency management
- Secret management
- Input validation
- Server-side authorization
- Tenant isolation
- Safe error handling
- Secure configuration

Security-sensitive changes should receive additional review where appropriate.

The development process should favor simple, well-understood solutions over custom security mechanisms.

---

## 23. Security Incident Handling

The platform should provide a basic process for handling security incidents.

Potential incidents include:

- Suspected unauthorized access
- Cross-tenant access
- Credential compromise
- Database exposure
- Authentication abuse
- Data leakage

Initial response:

```text
Detect
  ↓
Investigate
  ↓
Contain
  ↓
Fix
  ↓
Verify
  ↓
Document
```

The exact incident response process can evolve as the SaaS platform grows.

---

## 24. Security Principles & Final Requirements

Security requirements defined in this pillar are architectural requirements of the platform.

The most important principles are:

```text
1. Tenant isolation is a primary security boundary.

2. The backend is the security authority.

3. Never trust the client.

4. Authentication does not automatically grant authorization.

5. Role + Tenant + Ownership determine access.

6. Patients can access only their own data.

7. ClinicAdmins can access only their own clinic data.

8. PlatformAdmin operates at platform level.

9. Sensitive data must be minimized and protected.

10. Passwords and secrets must never be exposed.

11. Technical errors must never be returned directly to users.

12. Business rules must be enforced on the backend.

13. Client applications must never access the database directly.

14. Important administrative operations should be auditable.

15. Security mechanisms should use proven framework and infrastructure capabilities.

16. Avoid unnecessary security complexity and over-engineering.
```

## Final Security Architecture

```text
                         SaaS Platform
                              │
                    ┌─────────┴─────────┐
                    │                   │
               Patient App          Admin App
                    │                   │
                    └─────────┬─────────┘
                              │
                            HTTPS
                              │
                              ▼
                       .NET Web API
                              │
              ┌───────────────┼───────────────┐
              │               │               │
        Authentication   Authorization   Business Rules
              │               │               │
              └───────────────┼───────────────┘
                              │
                       Tenant Context
                              │
                              ▼
                    Tenant-Scoped Data
                              │
                              ▼
                       Relational DB
```

### Security Priority

The following five areas are considered critical for the MVP:

```text
1. Tenant Isolation
2. Authentication & Authorization
3. Patient Data Protection
4. Backend Business Rule Enforcement
5. Secure Error & Secret Handling
```

The Security Pillar should remain aligned with the overall project principle:

> **Keep the platform simple, secure, precise, and easy to maintain.**