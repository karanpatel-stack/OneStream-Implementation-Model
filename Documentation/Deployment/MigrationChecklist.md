# Migration Checklist -- Go-Live

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Overview](#1-overview)
2. [Pre-Migration Tasks](#2-pre-migration-tasks)
3. [Migration Day Tasks](#3-migration-day-tasks)
4. [Post-Migration Validation](#4-post-migration-validation)
5. [Rollback Plan](#5-rollback-plan)
6. [Support Escalation Path](#6-support-escalation-path)
7. [Sign-Off Requirements](#7-sign-off-requirements)

---

## 1. Overview

### 1.1 Migration Scope

This checklist covers the cutover from legacy CPM systems to OneStream XF for production use. The go-live migration includes deploying the final application configuration, loading historical data, validating system readiness, and transitioning users from legacy systems.

### 1.2 Migration Timeline

| Phase | Duration | Timing |
|-------|----------|--------|
| Pre-Migration (T-14 to T-1) | 14 days | Two weeks before go-live |
| Migration Day (T-0) | 1 day | Go-live Saturday (minimize business impact) |
| Post-Migration (T+1 to T+5) | 5 days | First business week after go-live |
| Hypercare (T+1 to T+30) | 30 days | Extended support period |

### 1.3 Go/No-Go Decision

A formal Go/No-Go decision meeting is held at T-2 (two days before migration). The decision is based on:
- All pre-migration tasks complete
- UAT sign-off received
- Rollback plan documented and tested
- Support team staffed and ready
- Business stakeholders confirm readiness

---

## 2. Pre-Migration Tasks (30 Items)

### 2.1 Infrastructure and Environment (Tasks 1-8)

| # | Task | Owner | Target Date | Status | Notes |
|---|------|-------|-------------|--------|-------|
| 1 | Verify PROD server hardware meets specifications (CPU, RAM, disk) | IT Infrastructure | T-14 | [ ] | App: 2x 16 vCPU/64GB; DB: 2x 16 vCPU/128GB |
| 2 | Confirm SQL Server Always On AG configured and tested | DBA | T-14 | [ ] | Automatic failover validated with zero data loss |
| 3 | Verify load balancer configuration for web tier | IT Network | T-14 | [ ] | F5 health checks, SSL termination, session persistence |
| 4 | Confirm SSL certificate installed and valid | IT Security | T-14 | [ ] | Wildcard cert from DigiCert; expires > 1 year |
| 5 | Validate network connectivity to all source systems from PROD servers | IT Network | T-12 | [ ] | SAP, Oracle, NetSuite, Workday, MES SFTP -- all ports open |
| 6 | Verify backup infrastructure and schedule | DBA | T-12 | [ ] | Full daily + differential hourly; 30-day retention; test restore |
| 7 | Confirm disaster recovery procedures documented and tested | IT Infrastructure | T-10 | [ ] | RPO < 1 hour; RTO < 4 hours |
| 8 | Verify monitoring agents installed (CPU, memory, disk, application) | IT Operations | T-10 | [ ] | SCOM or equivalent monitoring; alerting configured |

### 2.2 Application Deployment (Tasks 9-16)

| # | Task | Owner | Target Date | Status | Notes |
|---|------|-------|-------------|--------|-------|
| 9 | Deploy final business rules to PROD (all 79 rules compiled) | Technical Lead | T-7 | [ ] | Use Deploy_BusinessRules.ps1; verify compilation |
| 10 | Deploy final dimension metadata to PROD (all 14 dimensions) | Technical Lead | T-7 | [ ] | Use Deploy_Dimensions.ps1; verify member counts |
| 11 | Deploy data management sequences to PROD | Technical Lead | T-7 | [ ] | Use Deploy_DataManagement.ps1; verify connections |
| 12 | Deploy dashboards and CubeViews to PROD | Technical Lead | T-7 | [ ] | Export from QA; import to PROD; verify rendering |
| 13 | Deploy mapping tables to PROD database | Technical Lead | T-7 | [ ] | All 1,855 mapping rows; verify source-to-target coverage |
| 14 | Configure PROD source system connections with production credentials | Technical Lead | T-5 | [ ] | SAP PROD, Oracle PROD, NetSuite PROD, Workday PROD |
| 15 | Test each source system connection from PROD | Technical Lead | T-5 | [ ] | Execute connectivity test for all 7 connections |
| 16 | Deploy environment-specific PROD.config | Technical Lead | T-5 | [ ] | Verify all settings match production requirements |

### 2.3 Security and Access (Tasks 17-22)

| # | Task | Owner | Target Date | Status | Notes |
|---|------|-------|-------------|--------|-------|
| 17 | Configure SSO with production Azure AD app registration | IT Security | T-10 | [ ] | SAML 2.0; test with pilot users |
| 18 | Create all user accounts and role assignments in PROD | Application Admin | T-7 | [ ] | 150+ users across 8 roles (per security matrix) |
| 19 | Configure entity-level data security per user role | Application Admin | T-7 | [ ] | Plant controllers see only assigned entities |
| 20 | Verify service accounts created with appropriate permissions | IT Security | T-7 | [ ] | svc_os_prod_deploy, svc_os_prod_etl, svc_os_prod_monitor |
| 21 | Test login for 5 representative users (one per role) | Application Admin | T-5 | [ ] | Verify SSO, role assignment, entity access, dashboard visibility |
| 22 | Document and distribute login instructions to all users | Project Manager | T-3 | [ ] | Include URL, SSO instructions, first-login guide, support contacts |

### 2.4 Data Readiness (Tasks 23-27)

| # | Task | Owner | Target Date | Status | Notes |
|---|------|-------|-------------|--------|-------|
| 23 | Load historical actual data (FY2022-FY2025) to PROD Finance cube | Data Operations | T-5 | [ ] | Verify totals match legacy system; document reconciliation |
| 24 | Load current year (FY2026) actual data through latest closed period | Data Operations | T-3 | [ ] | Verify current month data ties to ERP GL reports |
| 25 | Load approved FY2026 budget data to PROD | Data Operations | T-3 | [ ] | Verify budget totals match legacy planning system |
| 26 | Load exchange rate history (FY2022-current) | Data Operations | T-5 | [ ] | Weighted average, period-end, and historical rates |
| 27 | Load ownership table for current consolidation structure | Data Operations | T-3 | [ ] | Verify ownership percentages for all entities |

### 2.5 Training and Communication (Tasks 28-30)

| # | Task | Owner | Target Date | Status | Notes |
|---|------|-------|-------------|--------|-------|
| 28 | Complete end-user training for all user groups | Training Lead | T-7 | [ ] | Role-based training: Executives, Finance Mgrs, Plant Controllers, Budget Analysts |
| 29 | Distribute user guides (Monthly Close, Budget Entry, Reporting) | Project Manager | T-5 | [ ] | PDF distribution via email; also available on SharePoint |
| 30 | Send go-live communication to all stakeholders | Project Manager | T-3 | [ ] | Include go-live date, system URL, support contacts, known limitations |

---

## 3. Migration Day Tasks (20 Items)

### 3.1 Pre-Cutover (Morning: 06:00-10:00)

| # | Task | Owner | Time | Status | Notes |
|---|------|-------|------|--------|-------|
| 31 | Take full backup of PROD database | DBA | 06:00 | [ ] | Baseline backup for rollback; verify backup integrity |
| 32 | Disable legacy system data feeds (prevent dual processing) | IT Operations | 07:00 | [ ] | Stop all scheduled ETL jobs in legacy CPM system |
| 33 | Place legacy system in read-only mode | Application Admin | 07:30 | [ ] | Users can view but not modify data in legacy system |
| 34 | Verify all pre-migration tasks marked complete | Project Manager | 08:00 | [ ] | Review checklist items 1-30; confirm no open blockers |
| 35 | Execute final data refresh for current period actuals | Data Operations | 08:00 | [ ] | Load any transactions posted since T-3 data load |
| 36 | Verify exchange rates current through migration date | Data Operations | 08:30 | [ ] | Load latest spot and average rates |

### 3.2 Core Migration (10:00-14:00)

| # | Task | Owner | Time | Status | Notes |
|---|------|-------|------|--------|-------|
| 37 | Execute full consolidation for all historical periods | Technical Lead | 10:00 | [ ] | FY2022-FY2025 full consolidation; verify parent = sum of children |
| 38 | Execute consolidation for current year through latest closed period | Technical Lead | 11:00 | [ ] | FY2026 through latest closed month |
| 39 | Run data management sequences (all daily, weekly, monthly jobs) | Data Operations | 11:30 | [ ] | Verify all jobs complete successfully |
| 40 | Execute all post-load calculations (KPIs, derivations, variances) | Technical Lead | 12:00 | [ ] | Verify calculated values populate correctly |
| 41 | Generate reconciliation report: OneStream vs. legacy system | Data Operations | 12:30 | [ ] | Compare key totals: Revenue, NI, Total Assets, Total Equity |
| 42 | Verify reconciliation within acceptable tolerance ($1,000) | Technical Lead | 13:00 | [ ] | Document any variances and root causes |

### 3.3 Validation and Activation (14:00-18:00)

| # | Task | Owner | Time | Status | Notes |
|---|------|-------|------|--------|-------|
| 43 | Run Validate_Deployment.ps1 script | Technical Lead | 14:00 | [ ] | Automated validation of all components |
| 44 | Execute smoke tests (5 key user scenarios) | QA Lead | 14:30 | [ ] | Login, view dashboard, run report, enter data, submit workflow |
| 45 | Business stakeholder validation of key reports | Finance Manager | 15:00 | [ ] | Verify P&L, BS, CF match expectations; sign off on accuracy |
| 46 | Enable production data load schedules (daily, weekly, monthly) | Data Operations | 16:00 | [ ] | Activate all DM sequences per production schedule |
| 47 | Enable production notifications and alerting | Application Admin | 16:00 | [ ] | Activate email notifications, monitoring alerts |
| 48 | Enable user access for all production users | Application Admin | 16:30 | [ ] | Remove "maintenance mode" restriction |
| 49 | Send go-live confirmation notification to all stakeholders | Project Manager | 17:00 | [ ] | "OneStream is now live" communication with key information |
| 50 | Take post-migration backup of PROD database | DBA | 18:00 | [ ] | Capture the post-migration baseline state |

---

## 4. Post-Migration Validation (15 Items)

### 4.1 Day 1 (T+1) -- First Business Day

| # | Task | Owner | Target | Status | Notes |
|---|------|-------|--------|--------|-------|
| 51 | Verify daily data load executed successfully (02:00 AM run) | Data Operations | 08:00 AM | [ ] | Check DB_ADMIN_DataOps dashboard |
| 52 | Verify all dashboards render correctly with current data | QA Lead | 09:00 AM | [ ] | Spot-check 5 key dashboards |
| 53 | Verify user logins working for all regions (SSO) | Application Admin | 09:00 AM | [ ] | Confirm at least one user per region can log in |
| 54 | Monitor system performance (response times, CPU, memory) | IT Operations | 10:00 AM | [ ] | Compare to baseline; flag any degradation |
| 55 | Collect user feedback on first-day experience | Project Manager | End of day | [ ] | Distribute quick survey; track support tickets |

### 4.2 Day 2-3 (T+2 to T+3) -- Early Validation

| # | Task | Owner | Target | Status | Notes |
|---|------|-------|--------|--------|-------|
| 56 | Verify data quality metrics (completeness, accuracy) | Data Operations | T+2 | [ ] | Run data quality scorecard; compare to pre-migration baseline |
| 57 | Execute parallel run: compare OneStream outputs to legacy | Finance Manager | T+2 | [ ] | Key reports run in both systems; reconcile totals |
| 58 | Verify workflow tasks assigned correctly to users | Application Admin | T+2 | [ ] | Close tasks, reconciliation tasks visible to assigned users |
| 59 | Test budget data entry (if in budget cycle) | FP&A Lead | T+3 | [ ] | Enter and save sample budget data; verify calculations |
| 60 | Verify report scheduling and email distribution working | Application Admin | T+3 | [ ] | Trigger a test scheduled report; confirm email delivery |

### 4.3 Day 4-5 (T+4 to T+5) -- Stability Confirmation

| # | Task | Owner | Target | Status | Notes |
|---|------|-------|--------|--------|-------|
| 61 | Confirm 5 consecutive successful daily data loads | Data Operations | T+5 | [ ] | Zero failures in first 5 business days |
| 62 | Confirm system uptime > 99.9% for first week | IT Operations | T+5 | [ ] | Monitor uptime tracking; document any outages |
| 63 | Resolve all P1/P2 support tickets from first week | Support Lead | T+5 | [ ] | Track and resolve critical/high issues; document resolutions |
| 64 | Complete post-migration reconciliation report | Data Operations | T+5 | [ ] | Final reconciliation: OneStream vs. legacy for all key totals |
| 65 | Obtain business stakeholder sign-off on go-live success | Project Manager | T+5 | [ ] | Formal sign-off from CFO or delegate |

---

## 5. Rollback Plan

### 5.1 Rollback Decision Criteria

A rollback to legacy systems may be initiated if ANY of the following conditions are met:

| Condition | Description | Decision Authority |
|-----------|-------------|-------------------|
| Critical Data Integrity | Consolidated financial data is materially incorrect and cannot be corrected within 4 hours | CFO + Technical Lead |
| System Availability | OneStream PROD is unavailable for > 2 hours with no resolution path | IT Director + Project Manager |
| Security Breach | Unauthorized data access or security vulnerability discovered | CISO + CFO |
| Integration Failure | Source system data loads fail for > 24 hours with no workaround | Technical Lead + Finance Manager |

### 5.2 Rollback Procedure

| Step | Action | Owner | Duration | Notes |
|------|--------|-------|----------|-------|
| 1 | Convene rollback decision meeting (virtual) | Project Manager | 30 min | All stakeholders must agree on rollback |
| 2 | Disable all OneStream production data loads | Data Operations | 5 min | Stop all DM sequences |
| 3 | Disable user access to OneStream PROD | Application Admin | 5 min | Set to maintenance mode |
| 4 | Restore legacy system to read-write mode | Application Admin | 15 min | Re-enable data entry in legacy system |
| 5 | Re-enable legacy system data feeds | IT Operations | 15 min | Restart legacy ETL jobs |
| 6 | Load any missing data to legacy (transactions since cutover) | Data Operations | 2-4 hours | Run catch-up loads for data posted during OneStream period |
| 7 | Verify legacy system is fully operational | Technical Lead | 1 hour | Run validation checks on legacy system |
| 8 | Notify all users of rollback and legacy system availability | Project Manager | Immediate | Email + Slack/Teams notification |
| 9 | Conduct post-mortem to identify root cause | Project Manager | T+2 | Document findings and remediation plan |
| 10 | Reschedule go-live after issues resolved | Project Manager | TBD | New go-live date based on remediation timeline |

### 5.3 Rollback Time Estimate

| Scenario | Estimated Rollback Time | Notes |
|----------|------------------------|-------|
| Day-of rollback (T-0) | 4-6 hours | Restore pre-migration backup; re-enable legacy |
| Day-1 rollback (T+1) | 6-8 hours | Catch-up data load to legacy for 1 day of transactions |
| Day-2+ rollback (T+2 to T+5) | 8-12 hours | Increasing data catch-up; may require parallel processing |
| After T+5 | Not recommended | Full re-implementation approach; discuss alternatives |

### 5.4 Rollback Test

The rollback procedure is tested at T-7:
1. Take a snapshot of QA environment
2. Simulate a go-live migration on QA
3. Execute the rollback procedure
4. Verify QA is restored to pre-migration state
5. Document timing and any issues
6. Update rollback plan if needed

---

## 6. Support Escalation Path

### 6.1 Hypercare Support Model (T+1 to T+30)

During the 30-day hypercare period, enhanced support is available:

| Support Level | Availability | Response Time | Team |
|--------------|-------------|---------------|------|
| L1 - Help Desk | 08:00-20:00 ET (Mon-Fri) | 30 minutes | IT Help Desk + OneStream trained agents |
| L2 - Application Support | 08:00-20:00 ET (Mon-Fri) | 1 hour | Application support team (3 dedicated resources) |
| L3 - Technical Team | 08:00-18:00 ET (Mon-Fri) + On-call | 2 hours | Implementation technical team |
| L4 - OneStream Vendor | 09:00-17:00 ET (Mon-Fri) | 4 hours | OneStream Support (contract support agreement) |

### 6.2 Escalation Matrix

| Priority | Definition | Examples | L1 Response | L2 Response | L3 Response | Resolution Target |
|----------|-----------|---------|------------|------------|------------|-------------------|
| P1 - Critical | System down or data integrity issue affecting all users | System unavailable; incorrect consolidation; data loss | 15 min | 30 min | 1 hour | 4 hours |
| P2 - High | Major function impaired affecting multiple users | Data load failure; dashboard not rendering; calculation error | 30 min | 1 hour | 2 hours | 8 hours |
| P3 - Medium | Single function issue affecting limited users | Report formatting error; export failure; slow performance | 1 hour | 4 hours | 8 hours | 2 business days |
| P4 - Low | Minor issue or enhancement request | Cosmetic issue; nice-to-have feature; documentation error | 4 hours | 1 day | 3 days | 5 business days |

### 6.3 Contact Information

| Role | Name | Email | Phone | Availability |
|------|------|-------|-------|-------------|
| IT Help Desk | Help Desk Team | helpdesk@company.com | x5000 | 08:00-20:00 ET |
| Application Support Lead | [Name] | app-support@company.com | [Phone] | 08:00-20:00 ET |
| Technical Lead | [Name] | [email] | [Phone] | 08:00-18:00 ET + on-call |
| Project Manager | [Name] | [email] | [Phone] | 08:00-18:00 ET |
| Data Operations Lead | [Name] | data-operations@company.com | [Phone] | 06:00-18:00 ET |
| Finance Sponsor | [Name] | [email] | [Phone] | Business hours |
| OneStream Vendor Support | OneStream Support | support@onestream.com | [Number] | 09:00-17:00 ET |

### 6.4 Issue Tracking

All support issues during hypercare are tracked in:
- **Tool:** ServiceNow (or equivalent ITSM)
- **Category:** OneStream XF
- **Subcategories:** Login/Access, Data Quality, Dashboard/Reports, Calculations, Workflow, Performance, Other
- **Reporting:** Daily issue summary distributed to project team; weekly summary to steering committee

---

## 7. Sign-Off Requirements

### 7.1 Pre-Migration Sign-Off (T-2)

Required before proceeding with migration:

| Sign-Off | Approver | Role | Status | Date |
|----------|---------|------|--------|------|
| Infrastructure Ready | [Name] | IT Infrastructure Director | [ ] | |
| Security Configuration Complete | [Name] | IT Security Manager | [ ] | |
| Application Deployment Complete | [Name] | Technical Lead | [ ] | |
| UAT Complete and Passed | [Name] | QA Lead | [ ] | |
| Business Readiness Confirmed | [Name] | Finance Sponsor (VP Finance) | [ ] | |
| Training Complete | [Name] | Training Lead | [ ] | |
| Rollback Plan Tested | [Name] | Technical Lead | [ ] | |
| Go/No-Go Decision | [Name] | Project Sponsor (CFO) | [ ] | |

### 7.2 Migration Day Sign-Off

Required at the end of migration day:

| Sign-Off | Approver | Role | Status | Date |
|----------|---------|------|--------|------|
| Data Reconciliation Acceptable | [Name] | Finance Manager | [ ] | |
| Smoke Tests Passed | [Name] | QA Lead | [ ] | |
| System Operational | [Name] | Technical Lead | [ ] | |
| Go-Live Confirmed | [Name] | Project Manager | [ ] | |

### 7.3 Post-Migration Sign-Off (T+5)

Required to formally close the migration and enter steady-state operations:

| Sign-Off | Approver | Role | Status | Date |
|----------|---------|------|--------|------|
| 5-Day Stability Confirmed | [Name] | IT Operations Manager | [ ] | |
| Data Quality Acceptable | [Name] | Data Operations Lead | [ ] | |
| All P1/P2 Issues Resolved | [Name] | Support Lead | [ ] | |
| Parallel Run Reconciled | [Name] | Finance Manager | [ ] | |
| Users Operational | [Name] | Business Stakeholders | [ ] | |
| **Go-Live Success** | **[Name]** | **CFO** | [ ] | |

### 7.4 Legacy System Decommission (T+90)

After 90 days of successful OneStream operation:

| Sign-Off | Approver | Role | Status | Date |
|----------|---------|------|--------|------|
| No rollback required in 90 days | [Name] | Project Manager | [ ] | |
| All historical data archived from legacy | [Name] | Data Operations Lead | [ ] | |
| Legacy system decommission approved | [Name] | IT Director | [ ] | |
| Legacy license termination initiated | [Name] | IT Procurement | [ ] | |
| **Decommission Complete** | **[Name]** | **CIO** | [ ] | |

---

*End of Document*
