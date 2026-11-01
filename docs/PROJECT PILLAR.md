# Dental Loyalty Platform — Project Pillar

## 1. Purpose

The Dental Loyalty Platform is a SaaS loyalty platform designed for dental clinics.

The platform enables dental clinics to retain existing patients and acquire new patients through a structured loyalty and referral program.

The platform automates the tracking of patient loyalty activity, points, referrals, rewards, and program progress, while providing clinic administrators with visibility into the business results of the loyalty program.

The platform should remain simple, focused, and easy to use and maintain.

---

## 2. Product Vision

The platform provides dental clinics with a simple loyalty and referral system that encourages existing patients to continue using the clinic's services and motivates them to refer new patients.

Patients are rewarded for meaningful engagement with the clinic through a combination of:

- Points earned through eligible clinic services
- Successful referrals
- Progressive rewards
- A clear loyalty progression toward the final reward

Clinic administrators can configure supported loyalty parameters, manage patient loyalty activity, and monitor the effectiveness of the program through analytics.

The platform should provide enough flexibility for clinics to adapt their loyalty program over time without introducing unnecessary complexity.

---

## 3. Problem Statement

Dental clinics need to continuously retain existing patients while attracting new patients.

The primary business challenge is not only bringing a patient to the clinic once, but creating a reason for the patient to return and encouraging existing patients to recommend the clinic to others.

Traditional or manually managed loyalty programs can require unnecessary administrative work and provide limited visibility into their effectiveness.

The Dental Loyalty Platform addresses this problem by providing:

- A structured loyalty program
- Automated tracking of loyalty activity
- Referral-based patient acquisition
- Progressive rewards
- Business analytics
- A simple administrative experience

---

## 4. Primary Product Goals

The primary goals of the platform are:

1. **Patient Retention**
   - Encourage existing patients to return to the clinic.

2. **Patient Acquisition**
   - Encourage existing patients to refer new patients.

3. **Loyalty Program Automation**
   - Remove the need for manual tracking of points, referrals, progress, and rewards.

4. **Patient Engagement**
   - Give patients a clear and understandable progression toward meaningful rewards.

5. **Business Visibility**
   - Allow clinic administrators to understand whether the loyalty program is contributing to patient retention and acquisition.

---

## 5. Product Principles

### 5.1 Simplicity First

The platform must be simple and easy to use.

This applies to:

- Patient Application
- Clinic Admin Application
- Platform administration
- Loyalty program configuration
- Loyalty operations
- Maintenance of the platform

The system should not introduce unnecessary functionality or complexity.

### 5.2 Business Requirements First

The product should be designed around clearly defined business requirements.

Technical complexity should only be introduced when it solves a real business or operational problem.

### 5.3 Simple but Configurable

The loyalty program will provide a predefined set of supported business rules.

Clinic administrators should be able to configure supported parameters when appropriate, without requiring technical intervention.

The platform should not implement a generic rule engine unless a real business requirement requires it.

### 5.4 Clear Patient Progression

Patients should always be able to understand:

- Their current points
- Their referral progress
- Their current reward
- What is required to unlock the next reward
- Whether a reward is locked, unlocked, or redeemed
- What remains to complete the current loyalty cycle

---

# 6. Target Users and Roles

## 6.1 Patient

The Patient is the primary end user of the loyalty program.

The Patient should be able to:

- Register an account
- Authenticate
- View their profile
- View their points
- View their referral progress
- View their rewards
- View their loyalty progression
- See locked rewards
- See unlocked rewards
- See redeemed rewards
- View their progress toward completing the current loyalty cycle
- Understand what is required to progress to the next reward

---

## 6.2 Clinic Staff

Clinic Staff support the day-to-day operation of the loyalty program.

Staff may perform operational loyalty activities such as:

- Managing patient loyalty activity
- Recording eligible activities
- Managing points where permitted
- Managing referral activity where permitted
- Processing reward redemption

Staff should not be responsible for configuring the overall loyalty business rules.

---

## 6.3 Clinic Admin

The Clinic Admin represents the owner, manager, or authorized administrator of a dental clinic.

The Clinic Admin is responsible for managing the clinic's loyalty program.

The Clinic Admin should be able to:

- Configure the loyalty program
- Configure supported points parameters
- Configure supported referral parameters
- Configure rewards
- Manage patients
- View patient loyalty information
- Manage loyalty activity
- Manage points and referrals
- Process or confirm reward redemption
- Manage clinic staff
- View analytics
- Monitor loyalty program performance

The Clinic Admin should be able to manage the clinic's supported loyalty configuration without requiring intervention from the Platform Admin.

---

## 6.4 Platform Admin

The Platform Admin is responsible for the platform itself.

For the MVP, the Platform Admin will manually provision new clinics.

The Platform Admin should be able to:

- Create a clinic
- Create the initial Clinic Admin
- Provision the clinic with the default loyalty configuration
- Perform platform-level administrative operations

The Platform Admin is not required for normal day-to-day management of a clinic's loyalty program.

---

# 7. Core User Journeys

## 7.1 Patient Journey

The primary patient journey is:

1. Patient registers an account.
2. Patient joins the loyalty program.
3. Patient uses eligible clinic services.
4. Patient earns points.
5. Patient refers other people to the clinic.
6. Referral progress increases when referrals are successfully completed.
7. Patient progresses toward the next reward.
8. Patient meets the required points and referral conditions.
9. Reward becomes unlocked.
10. Patient redeems the reward.
11. Patient continues progressing toward the next reward.

The patient experience should clearly communicate the next required action and current progress.

---

## 7.2 Clinic Admin Journey

The primary Clinic Admin journey is:

1. Clinic is provisioned by the Platform Admin.
2. Clinic Admin receives access.
3. Default loyalty configuration is available.
4. Clinic Admin reviews and configures supported parameters where necessary.
5. Clinic Admin manages patients and loyalty activity.
6. Patient data is updated as loyalty activities occur.
7. Updated loyalty information is reflected in the Patient Application.
8. Clinic Admin monitors analytics and program performance.

---

## 7.3 Platform Admin Journey

For the MVP:

1. Platform Admin creates a clinic.
2. Platform Admin creates the initial Clinic Admin.
3. Default loyalty configuration is provisioned.
4. Clinic becomes available for use.

After initial provisioning, normal clinic operations should not depend on the Platform Admin.

---

# 8. Loyalty Program

The loyalty program is the core business capability of the platform.

It consists of:

- Points
- Referrals
- Reward tiers
- Reward progression
- Reward redemption
- Loyalty cycles (§9)

---

## 8.1 Points

Patients earn points through eligible services performed by the clinic.

Each clinic has its own scoring configuration: a spend amount in RSD and the number of points awarded for that amount (`BUSINESS.md` §3.1). The configuration belongs to that clinic only.

The platform's initial default for a new clinic is:

> **100 RSD = 1,000 points** (default; the Clinic Admin can change it during clinic setup)

> Point values in this section follow `BUSINESS.md` §3. The earlier "100 RSD = 1 point" and "1000 points" values are superseded (ADR-001).

Points before rounding are the service amount in RSD ÷ spend amount × points awarded (with the default, service amount × 10). The result is rounded to the nearest whole number of points using **midpoint rounding away from zero**: a fractional part of 0.5 or greater rounds up, and a fractional part below 0.5 rounds down (`BUSINESS.md` §3.2). With the default configuration, for example, 1,234.54 RSD → 12,345 points, 1,234.55 RSD → 12,346 points, 1,234.56 RSD → 12,346 points, and 7,500.00 RSD → 75,000 points.

The points a service generates are always calculated from the service amount using the clinic's scoring configuration. Individual services do not define their own point values (`BUSINESS.md` §3, §22).

Points represent the patient's progress through the current loyalty cycle.

The maximum points balance is:

> **1,000,000 points**

A patient's balance never exceeds 1,000,000 points. If an earning operation would exceed it, only the points that fit are credited; the excess points are discarded and are not carried forward, and the earning operation is not rejected (`BUSINESS.md` §3.3).

Example:

```text
Current balance:     950,000
Calculated points:   120,000
Credited points:      50,000
New balance:       1,000,000 / 1,000,000
```

---

## 8.2 Referrals

Referrals represent a separate loyalty progression mechanism.

The reward tiers require the following referral counts:

| Reward | Required Referrals |
|---|---:|
| Silver | 1 |
| Gold | 3 |
| Platinum | 5 |

A valid referral and its rewards are defined in `BUSINESS.md` §4: the referral becomes valid when the referred new patient comes to the clinic and completes a dental examination (no service purchase is required), and both the referring and the referred patient then receive 50,000 points, up to a maximum of 5 referral rewards per patient.

---

## 8.3 Reward Tiers

The loyalty program contains three reward tiers:

1. Silver
2. Gold
3. Platinum

The progression is:

Silver → Gold → Platinum

Platinum represents the highest and final reward tier.

Each reward has:

- A required points threshold
- A required referral threshold
- A reward definition
- A locked/unlocked/redeemed state

> Superseded by `BUSINESS.md` §6–§8 and ADR-001: the earlier statement that Silver and Gold thresholds would be proportional to a 1000-point Platinum target no longer applies. The MVP reward thresholds are defined in `BUSINESS.md` as three tiers, which ADR-001 allows to be presented as Silver/Gold/Platinum:
>
> - Tier 1 (1 referral): 300,000, 400,000 and 500,000 points
> - Tier 2 (3 referrals): 600,000 and 700,000 points
> - Tier 3 (5 referrals): 1,000,000 points
>
> A reward's points threshold is a separate concept from the 1,000,000-point maximum balance (§8.1), even where the values coincide.

---

## 8.4 Reward Unlocking

A reward can only be unlocked when both required conditions are satisfied:

Required Points reached  
AND  
Required Referrals reached

Example:

- Silver: required points + 1 referral
- Gold: required points + 3 referrals
- Platinum: 1,000,000 points + 5 referrals (Tier 3, `BUSINESS.md` §8; superseded value: 1,000 points)

Having only the required points is not sufficient.

Having only the required referrals is not sufficient.

Both conditions must be satisfied.

---

## 8.5 Reward States

Rewards have a progression state.

### Locked

The patient has not yet satisfied the requirements.

The reward is displayed as locked and visually disabled/greyed out.

### Unlocked

The patient has satisfied both required conditions.

The reward becomes available for redemption.

### Redeemed

The patient has used the reward.

The redemption is recorded through the Clinic Admin Application and the updated state is reflected in the Patient Application.

Redeemed rewards are displayed as used/greyed out.

---

## 8.6 Sequential Reward Progression

Rewards are sequential.

A patient must redeem the currently unlocked reward before continuing toward the next reward.

The expected progression is:

Silver unlocked  
↓  
Silver redeemed  
↓  
Continue earning  
↓  
Gold unlocked  
↓  
Gold redeemed  
↓  
Continue earning  
↓  
Platinum unlocked  
↓  
Platinum redeemed

This progression is intended to provide the patient with a clear and understandable loyalty path.

---

# 9. Loyalty Cycle

> The loyalty cycle is defined in `BUSINESS.md` §5.1, which is authoritative for this section.

Each patient has an individual loyalty cycle. The first cycle starts on the patient's registration date and lasts one year; each subsequent cycle starts on the actual end date of the previous cycle.

During a cycle, the patient can:

- Earn points
- Complete referrals
- Unlock rewards
- Redeem rewards
- Progress toward the final Platinum reward

A cycle can end early only when the patient has accumulated 1,000,000 points during the cycle, has completed five successful referrals during the cycle, and has redeemed all rewards required for the active cycle. Otherwise, the cycle ends automatically one year after its start date, regardless of the patient's progress.

When the cycle ends, early or after one year, a new cycle starts on that end date. At the start of the new cycle:

- The active points balance resets to zero
- Referral progress resets to zero
- Reward progression and availability restart for the new cycle

Historical service records, loyalty transactions, referral records, and reward-redemption records are preserved. Resetting a cycle must never delete historical records.

The patient must start the loyalty progression again in the new cycle.

---

# 10. Patient Experience

The Patient Application should provide a simple visual representation of loyalty progression.

The primary experience should communicate:

- Current points
- Points required for the next reward
- Referral progress
- Current reward
- Next reward
- Locked rewards
- Unlocked rewards
- Redeemed rewards
- Progress toward completing the current loyalty cycle

The progression should behave as a simple guided path toward the final reward.

The interface should avoid unnecessary complexity and should make the next action obvious to the patient.

---

# 11. Clinic Administration

The Clinic Admin Application is the primary operational interface for the clinic.

It should provide:

### Patient Management

- Patient list
- Patient information
- Loyalty status
- Points
- Referrals
- Rewards
- Relevant loyalty activity

### Loyalty Management

- Points management
- Referral management
- Reward management
- Reward redemption
- Loyalty configuration

### Configuration

The platform provides a default loyalty configuration.

Clinic Admins can modify supported configurable parameters when required by the clinic.

The Clinic Admin should not need technical intervention for normal loyalty program configuration.

---

# 12. Analytics and KPIs

Analytics are a core product capability.

The Clinic Admin should always have visibility into the performance of the loyalty program.

The MVP should provide the following key metrics:

### Loyalty Patients

Number of patients participating in the loyalty program.

### Active Loyalty Users

Number of patients actively engaging with the loyalty program.

### Returning Patients

Number of existing patients who return to the clinic.

This is one of the primary indicators of whether the loyalty program is achieving its retention objective.

### New Patients

Number of new patients acquired by the clinic.

### Referral Patients

Number of new patients acquired through the referral program.

### Rewards Redeemed

Number of rewards successfully redeemed by patients.

These metrics should help the clinic answer:

> Is the loyalty program helping us retain existing patients and acquire new patients?

Analytics should remain focused and understandable rather than exposing unnecessary or excessive statistics.

---

# 13. SaaS Model

The platform is intended to operate as a SaaS product serving multiple dental clinics.

Each dental clinic operates its own loyalty program and manages its own patients, staff, configuration, loyalty activity, rewards, and analytics.

The clinic should be able to operate independently after initial provisioning.

For the MVP, new clinics are manually provisioned by the Platform Admin.

Self-service clinic onboarding is outside the MVP scope.

---

# 14. MVP Scope

The MVP includes:

## Patient Application

- User registration
- Authentication
- Patient profile
- Loyalty progress
- Points
- Referrals
- Rewards
- Reward status
- Loyalty cycle information

## Clinic Admin Application

- Authentication
- Patient management
- Loyalty configuration
- Points management
- Referral management
- Reward management
- Reward redemption
- Staff management
- Analytics

## Platform Administration

- Clinic creation
- Initial Clinic Admin creation
- Default loyalty configuration
- Initial clinic provisioning

## Loyalty

- Points
- Referrals
- Silver reward
- Gold reward
- Platinum reward
- Reward progression
- Reward redemption
- Loyalty cycle completion and reset

## Analytics

- Loyalty patients
- Active loyalty users
- Returning patients
- New patients
- Referral patients
- Rewards redeemed

---

# 15. Non-Goals / Out of Scope for MVP

The MVP will not attempt to become a complete dental practice management system.

The following are outside the MVP scope:

- Dental medical records
- Medical history management
- Dental imaging
- Appointment scheduling
- Billing/accounting
- Inventory management
- Full CRM functionality
- Marketing automation
- Generic rule engine
- Self-service clinic onboarding
- Subscription/billing management
- Advanced BI/reporting
- Complex role hierarchy
- Unnecessary customization

These features may be considered in the future if there is a validated business need.

---

# 16. Product Simplicity Requirement

Simplicity is a core requirement of the product.

The platform should be:

- Easy for patients to understand
- Easy for clinic staff to operate
- Easy for Clinic Admins to configure
- Easy to maintain
- Easy to extend

The platform should avoid unnecessary abstractions, configuration options, workflows, and features.

When two solutions satisfy the same business requirement, the simpler solution should be preferred.

---

# 17. Success Criteria

The MVP should demonstrate that the platform can help a dental clinic:

1. Retain existing patients.
2. Acquire new patients through referrals.
3. Operate a loyalty program without manual external tracking.
4. Provide patients with a clear incentive to engage with the clinic.
5. Measure the effectiveness of the loyalty program.

The primary business indicators are:

- Returning patients
- New patients
- Referral patients
- Active loyalty users
- Rewards redeemed
- Loyalty program participation

The platform should ultimately demonstrate that the loyalty program creates measurable value for the participating clinic.

---

# 18. Open Decisions

The following items remain open until the final business configuration is provided.

### Reward Point Thresholds

> Resolved by `BUSINESS.md` §6–§8 and ADR-001 (see §8.3). The earlier "proportional to the final 1000-point Platinum target" approach is superseded.

### Service Point Values

> Resolved by `BUSINESS.md` §3. Points for a service are calculated from the service amount using the clinic's own scoring configuration (spend amount in RSD and points awarded), rounded to a whole number of points using midpoint rounding away from zero (`BUSINESS.md` §3.2). The platform default is **100 RSD = 1,000 points**, which the Clinic Admin can change during clinic setup.

Individual services do not define their own point values.

### Valid Referral Definition

> Resolved by `BUSINESS.md` §4.1 (see §8.2).

### Reward Definitions

> Resolved by `BUSINESS.md` §6–§8 and ADR-001, which define the named MVP rewards for each tier.

### Final Loyalty Configuration

> Resolved for points and reward thresholds: the service-to-points calculation and the clinic's scoring configuration are defined in `BUSINESS.md` §3, and the default reward thresholds in `BUSINESS.md` §6–§8. The scoring configuration and service prices are clinic-configurable (`BUSINESS.md` §22).