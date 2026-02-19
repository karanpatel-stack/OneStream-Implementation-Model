# Environment Strategy Document

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Environment Topology](#1-environment-topology)
2. [DEV Environment](#2-dev-environment)
3. [QA Environment](#3-qa-environment)
4. [PROD Environment](#4-prod-environment)
5. [Promotion Process](#5-promotion-process)
6. [Configuration Differences](#6-configuration-differences)
7. [Data Management per Environment](#7-data-management-per-environment)
8. [Access Control per Environment](#8-access-control-per-environment)

---

## 1. Environment Topology

### 1.1 Three-Environment Architecture

```
+------------------+       +------------------+       +------------------+
|                  |       |                  |       |                  |
|   DEV            |  -->  |   QA             |  -->  |   PROD           |
|   Development    |       |   Quality        |       |   Production     |
|   & Unit Testing |       |   Assurance &    |       |                  |
|                  |       |   UAT            |       |                  |
+------------------+       +------------------+       +------------------+
|                  |       |                  |       |                  |
| App Server: 1x   |       | App Server: 1x   |       | App Server: 2x   |
| DB Server:  1x   |       | DB Server:  1x   |       | DB Server:  2x   |
| Web Server: 1x   |       | Web Server: 1x   |       | Web Server: 2x   |
|                  |       |                  |       |                  |
| URL:             |       | URL:             |       | URL:             |
| onestream-dev    |       | onestream-qa     |       | onestream        |
| .company.com     |       | .company.com     |       | .company.com     |
|                  |       |                  |       |                  |
| DB: OS_DEV       |       | DB: OS_QA        |       | DB: OS_PROD      |
| App: MFG_        |       | App: MFG_        |       | App: MFG_        |
|   Enterprise_DEV |       |   Enterprise_QA  |       |   Enterprise     |
+------------------+       +------------------+       +------------------+
```

### 1.2 Environment Purpose Summary

| Attribute | DEV | QA | PROD |
|-----------|-----|-----|------|
| **Purpose** | Development and unit testing | Integration testing and UAT | Live production operations |
| **Users** | Developers, technical team (5-10) | Testers, key business users (15-25) | All end users (150+) |
| **Data** | Sample data set (5 entities, 3 periods) | Production-mirror data (all entities, 12 months) | Live production data |
| **Availability** | Business hours; may be offline for maintenance | Extended hours; scheduled maintenance windows | 24/7 with 99.9% SLA |
| **Change Frequency** | Multiple times daily | Weekly deployments | Monthly (after close cycle) |
| **Backup** | Daily (7-day retention) | Daily (14-day retention) | Full daily + differential hourly (30-day retention) |

---

## 2. DEV Environment

### 2.1 Purpose

The DEV environment is the primary workspace for developers and technical architects. All new development, bug fixes, and configuration changes originate in DEV. This environment supports rapid iteration with frequent deployments.

### 2.2 Configuration

| Setting | Value |
|---------|-------|
| Server URL | https://onestream-dev.company.com |
| Database | OS_DEV |
| Application Name | MFG_Enterprise_DEV |
| Log Level | Debug |
| Sample Data Enabled | True |
| Notifications Enabled | False (no external emails sent) |
| SSO Enabled | Yes (DEV Azure AD app registration) |
| API Rate Limiting | Disabled |
| Session Timeout | 120 minutes (extended for debugging) |

### 2.3 Data Strategy

- **Sample data set** covering 5 representative entities (one per region)
- **3 periods** of test data (current month, prior month, same month prior year)
- **Known-good test cases** with documented expected results
- **Reset capability**: Data can be reset to baseline at any time using restore scripts
- **No production data**: Sensitive production data is never loaded into DEV

### 2.4 Usage Guidelines

| Activity | Permitted | Notes |
|----------|-----------|-------|
| New business rule development | Yes | Primary purpose of DEV |
| Dimension changes | Yes | Test new members, hierarchy changes |
| Dashboard development | Yes | Build and test new dashboards |
| Performance testing | Limited | Small data set; not representative of production volumes |
| Data load testing | Yes | Test connector BRs against source system dev instances |
| Destructive testing | Yes | Reset data as needed; test error scenarios |
| User acceptance testing | No | Use QA for UAT |

---

## 3. QA Environment

### 3.1 Purpose

The QA environment serves two primary functions: (1) integration testing by the technical team, and (2) user acceptance testing (UAT) by key business users. QA data mirrors production to ensure realistic testing conditions.

### 3.2 Configuration

| Setting | Value |
|---------|-------|
| Server URL | https://onestream-qa.company.com |
| Database | OS_QA |
| Application Name | MFG_Enterprise_QA |
| Log Level | Info |
| Sample Data Enabled | True (production-mirror data) |
| Notifications Enabled | True (test recipients only; QA distribution list) |
| SSO Enabled | Yes (QA Azure AD app registration) |
| API Rate Limiting | Enabled (matches production settings) |
| Session Timeout | 60 minutes (matches production) |

### 3.3 Data Strategy

- **Production-mirror data**: Full entity set with 12 months of historical data
- **Data refresh**: Refreshed from production monthly (after each close)
- **Data masking**: Sensitive fields (employee names, SSN) are masked/anonymized
- **Stable baseline**: Data is not modified by automated processes unless explicitly scheduled
- **Test scenarios**: Dedicated test entities and test scenarios for UAT

### 3.4 Usage Guidelines

| Activity | Permitted | Notes |
|----------|-----------|-------|
| Integration testing | Yes | Primary purpose; test end-to-end flows |
| User acceptance testing | Yes | Key business users validate functionality |
| Performance testing | Yes | Realistic data volumes; meaningful performance metrics |
| Regression testing | Yes | Run after each deployment to verify existing functionality |
| Bug reproduction | Yes | Mirror production issues for diagnosis |
| Development / code changes | No | All changes must come from DEV |
| Direct database modifications | No | All changes via application deployment scripts |

---

## 4. PROD Environment

### 4.1 Purpose

The PROD environment is the live production system used by all end users for financial consolidation, planning, reporting, and reconciliation. Maximum stability, security, and performance are required.

### 4.2 Configuration

| Setting | Value |
|---------|-------|
| Server URL | https://onestream.company.com |
| Database | OS_PROD |
| Application Name | MFG_Enterprise |
| Log Level | Warning |
| Sample Data Enabled | False |
| Notifications Enabled | True (production recipients) |
| Backup Enabled | True (full daily + differential hourly) |
| SSO Enabled | Yes (production Azure AD app registration) |
| API Rate Limiting | Enabled |
| Session Timeout | 30 minutes |

### 4.3 High Availability Configuration

| Component | Configuration | Failover |
|-----------|--------------|---------|
| Application Server | 2-node active/active cluster | Automatic failover; load balanced |
| Database Server | SQL Server Always On Availability Group | Automatic failover; synchronous replica |
| Web Server | 2-node behind F5 load balancer | Automatic failover; health-check based |
| File Storage | Dedicated SAN with RAID-10 | Redundant paths; no single point of failure |

### 4.4 Usage Guidelines

| Activity | Permitted | Notes |
|----------|-----------|-------|
| Financial close processing | Yes | Primary business activity |
| Budget/forecast data entry | Yes | During designated planning windows |
| Reporting and dashboards | Yes | Self-service by authorized users |
| Code deployment | Scheduled only | Monthly maintenance window; emergency hotfix process available |
| Direct database access | No | All access through application layer |
| Data corrections | By admin only | Requires change request and approval |

---

## 5. Promotion Process

### 5.1 Standard Promotion Flow

```
Developer                  Technical Lead           QA Lead/Business         Release Manager
    |                           |                        |                       |
    | 1. Develop in DEV         |                        |                       |
    | 2. Unit test in DEV       |                        |                       |
    | 3. Submit for code review |                        |                       |
    |-------------------------->|                        |                       |
    |                           | 4. Code review         |                       |
    |                           | 5. Approve/Reject      |                       |
    |    (if rejected)          |                        |                       |
    |<--------------------------|                        |                       |
    |    (fix and resubmit)     |                        |                       |
    |                           |                        |                       |
    |    (if approved)          |                        |                       |
    |                           | 6. Deploy to QA        |                       |
    |                           |----------------------->|                       |
    |                           |                        | 7. Integration test   |
    |                           |                        | 8. UAT                |
    |                           |                        | 9. Approve/Reject     |
    |                           |    (if rejected)       |                       |
    |                           |<-----------------------|                       |
    |<--------------------------|    (fix in DEV)        |                       |
    |                           |                        |                       |
    |                           |    (if approved)       |                       |
    |                           |                        |---------------------> |
    |                           |                        |                       | 10. Schedule PROD
    |                           |                        |                       |     deployment
    |                           |                        |                       | 11. Execute deploy
    |                           |                        |                       | 12. Post-deploy
    |                           |                        |                       |     validation
    |                           |                        |                       | 13. Notify team
```

### 5.2 Promotion Stages

| Stage | Environment | Activities | Gate Criteria |
|-------|-------------|-----------|---------------|
| **Development** | DEV | Code, configure, unit test | Compiles; unit tests pass; code review approved |
| **QA Deployment** | QA | Deploy via automated scripts | Deployment script runs successfully |
| **Integration Test** | QA | Test end-to-end data flows, calculations | All integration test cases pass |
| **UAT** | QA | Business users validate functionality | UAT sign-off from business stakeholders |
| **PROD Deployment** | PROD | Deploy during maintenance window | Change request approved; rollback plan documented |
| **Post-Deploy Validation** | PROD | Verify deployment success | Validation script passes; smoke tests complete |

### 5.3 Deployment Package Contents

Each promotion creates a deployment package containing:

| Component | Included | Format |
|-----------|----------|--------|
| Business Rules | All modified rules (source code) | `.vb` files |
| Dimension Members | New/modified members | CSV files |
| Mapping Tables | Updated source-to-target mappings | SQL scripts |
| Dashboard Definitions | New/modified dashboards | XML export |
| CubeView Definitions | New/modified CubeViews | XML export |
| Configuration Changes | Environment-specific settings | Config XML |
| Data Management Sequences | New/modified DM steps | Export package |
| Deployment Script | Automated deployment script | PowerShell `.ps1` |
| Rollback Script | Automated rollback script | PowerShell `.ps1` |
| Release Notes | Summary of changes, testing evidence | Markdown document |

### 5.4 Emergency Hotfix Process

For critical production issues requiring immediate resolution:

1. **Identify:** Issue reported and classified as P1 (critical)
2. **Develop:** Fix developed in DEV with expedited code review
3. **Test:** Abbreviated testing in QA (focused regression on affected area)
4. **Approve:** Emergency change request approved by Release Manager and Business Sponsor
5. **Deploy:** Immediate deployment to PROD (outside maintenance window)
6. **Validate:** Post-deployment validation and monitoring
7. **Document:** Full post-mortem within 48 hours

**Emergency Hotfix Authority:** Requires approval from both the Technical Lead and the Business Sponsor (Finance Manager or above).

---

## 6. Configuration Differences

### 6.1 Environment-Specific Settings

| Setting | DEV | QA | PROD |
|---------|-----|-----|------|
| Server URL | onestream-dev.company.com | onestream-qa.company.com | onestream.company.com |
| Database Name | OS_DEV | OS_QA | OS_PROD |
| Application Name | MFG_Enterprise_DEV | MFG_Enterprise_QA | MFG_Enterprise |
| Log Level | Debug | Info | Warning |
| Session Timeout | 120 min | 60 min | 30 min |
| Max Concurrent Users | 20 | 50 | 250 |
| Calc Timeout | 60 min | 45 min | 30 min |
| Email SMTP Server | smtp-dev.company.com | smtp-qa.company.com | smtp.company.com |
| Email From Address | noreply-dev@company.com | noreply-qa@company.com | noreply@company.com |
| Notification Recipients | Dev team only | QA test group | Production distribution lists |
| SSL Certificate | Self-signed (dev) | Internal CA | Public CA (DigiCert) |
| Backup Schedule | Daily 02:00 AM | Daily 02:00 AM | Full daily 02:00 AM + Diff hourly |
| Backup Retention | 7 days | 14 days | 30 days |

### 6.2 Source System Connections

| Connection | DEV | QA | PROD |
|-----------|-----|-----|------|
| SAP HANA | DEV instance (saphana-dev) | QA instance (saphana-qa) | PROD instance (saphana) |
| Oracle EBS | DEV instance (oradb-dev) | QA instance (oradb-qa) | PROD instance (oradb) |
| NetSuite | Sandbox account | Sandbox account | Production account |
| Workday | Preview tenant | Implementation tenant | Production tenant |
| MES SFTP | Dev SFTP path | QA SFTP path | Prod SFTP path |

### 6.3 Feature Flags

| Feature | DEV | QA | PROD |
|---------|-----|-----|------|
| Debug Logging | Enabled | Disabled | Disabled |
| Sample Data Generation | Enabled | Disabled | Disabled |
| Test Mode (no external calls) | Available | Available | Disabled |
| Performance Profiling | Enabled | Available | Disabled |
| Audit Trail Detail Level | Verbose | Standard | Standard |
| Data Validation Strictness | Warn Only | Strict | Strict |

---

## 7. Data Management per Environment

### 7.1 Data Refresh Schedule

| Environment | Refresh Source | Frequency | Method |
|-------------|--------------|-----------|--------|
| DEV | Static test data set | On-demand (reset script) | Restore from baseline backup |
| QA | PROD (masked) | Monthly (after close) | Database copy + masking script |
| PROD | Live source systems | Daily (automated ETL) | Data Management sequences |

### 7.2 Data Masking Rules (QA)

When production data is copied to QA, the following masking is applied:

| Data Category | Masking Rule |
|--------------|-------------|
| Employee Names | Replaced with "Test User NNN" |
| Social Security Numbers | Replaced with "XXX-XX-XXXX" |
| Bank Account Numbers | Replaced with "XXXX-XXXX-XXXX" |
| Salary Details | Randomized within +/- 15% of original |
| Customer Names (Key Accounts) | Replaced with "Customer AAANNN" |
| Financial Amounts | Preserved (no masking -- needed for testing) |
| Entity Names | Preserved (no masking) |
| Account Names | Preserved (no masking) |

### 7.3 Data Retention

| Environment | Active Data | Archive Data | Total Retention |
|-------------|------------|-------------|-----------------|
| DEV | 3 periods | None | Reset as needed |
| QA | 12 months | None | Overwritten monthly |
| PROD | 24 months (active partitions) | 60 months (compressed) | 7 years total |

---

## 8. Access Control per Environment

### 8.1 Access Matrix

| Role | DEV | QA | PROD |
|------|-----|-----|------|
| System Administrator | Full access | Full access | Full access |
| Application Developer | Full access | Read + deploy access | No access |
| Technical Lead | Full access | Full access | Read + deploy access |
| QA Tester | No access | Full functional access | No access |
| Business User (UAT) | No access | Functional access (UAT window) | Full functional access |
| Finance Manager | No access | Read-only | Full functional access |
| Plant Controller | No access | Read-only (UAT: input access) | Full functional access |
| Executive | No access | No access | Read-only dashboards |
| Auditor | No access | No access | Read-only full visibility |
| Release Manager | Read-only | Deploy access | Deploy access |

### 8.2 Service Accounts

| Account | Purpose | Environment | Permissions |
|---------|---------|-------------|-------------|
| svc_os_dev_deploy | Deployment automation | DEV | Application admin |
| svc_os_qa_deploy | Deployment automation | QA | Application admin |
| svc_os_prod_deploy | Deployment automation | PROD | Application admin |
| svc_os_dev_etl | Data load processing | DEV | Data load, cube write |
| svc_os_qa_etl | Data load processing | QA | Data load, cube write |
| svc_os_prod_etl | Data load processing | PROD | Data load, cube write |
| svc_os_prod_monitor | Monitoring and alerting | PROD | Read-only, log access |

### 8.3 Access Request Process

1. Submit access request through IT Service Desk
2. Request requires manager approval
3. For PROD access: additional approval from Application Owner (Finance)
4. Access provisioned within 2 business days
5. Access reviewed quarterly (SOX compliance requirement)

---

*End of Document*
