# OneStream XF Deployment Runbook

## Table of Contents

1. [Environment Strategy](#environment-strategy)
2. [Pre-Deployment Checklist](#pre-deployment-checklist)
3. [Deployment Sequence](#deployment-sequence)
4. [Post-Deployment Validation](#post-deployment-validation)
5. [Rollback Procedures](#rollback-procedures)
6. [Environment-Specific Configurations](#environment-specific-configurations)
7. [Go-Live Checklist](#go-live-checklist)
8. [Support and Monitoring](#support-and-monitoring)

---

## 1. Environment Strategy

### Environment Topology

```
DEV (Development)  -->  QA (Quality Assurance)  -->  PROD (Production)
     |                        |                           |
  Build & Unit Test     Integration & UAT          Live Operations
  Daily deployments     Weekly deployments         Controlled releases
  Open access           Restricted access          Role-based access
```

### Environment Details

| Attribute              | DEV                          | QA                            | PROD                          |
|------------------------|------------------------------|-------------------------------|-------------------------------|
| Purpose                | Development and unit testing | Integration testing and UAT   | Production operations          |
| Data                   | Synthetic test data          | Sanitized production copy     | Live production data           |
| Refresh Cycle          | On demand                    | Monthly from PROD (sanitized) | N/A                            |
| Deployment Frequency   | Daily / on demand            | Weekly / sprint cadence       | Monthly / release cycle        |
| Access                 | Development team             | Dev team + business testers   | All authorized users           |
| Approval Required      | None                         | Tech lead sign-off            | CAB approval + change ticket   |
| Backup Schedule        | Daily                        | Daily                         | Every 6 hours + pre-deployment |
| Source System Links     | Mock services / test stubs  | QA instances of source systems| Production source systems      |
| URL Convention         | onestream-dev.company.com    | onestream-qa.company.com      | onestream.company.com          |

### Promotion Process

1. **DEV to QA**: Developer completes work and unit tests in DEV. Creates a OneStream migration package. Tech lead reviews the package contents and approves. Package is applied to QA. Integration and regression tests are executed.

2. **QA to PROD**: QA testing is completed and signed off by business owners. Change request is submitted to the Change Advisory Board (CAB). Upon approval, the identical migration package from QA is applied to PROD during the designated maintenance window.

3. **Package Integrity**: The same migration package artifact that was validated in QA must be the one deployed to PROD. No modifications are permitted between environments. If changes are needed, a new package must go through the full DEV to QA to PROD cycle.

---

## 2. Pre-Deployment Checklist

Complete every item before beginning the deployment sequence. Mark each item as verified with initials and timestamp.

### Administrative Preparation

- [ ] Change request approved and change ticket number recorded
- [ ] Maintenance window confirmed with stakeholders and communicated to end users
- [ ] Deployment team members confirmed and available for the full window
- [ ] Rollback plan reviewed and understood by all team members
- [ ] Emergency contact list verified (DBA, network, OneStream support, business owners)

### Technical Preparation

- [ ] Full application backup completed in target environment
- [ ] Database backup completed and verified restorable
- [ ] Migration package integrity verified (checksums match QA-tested package)
- [ ] Target environment OneStream services confirmed running and healthy
- [ ] Sufficient disk space available on application and database servers
- [ ] Source system connectivity verified from target environment
- [ ] Active Directory / identity provider connectivity confirmed
- [ ] SSL certificates valid and not expiring within 30 days

### Application Preparation

- [ ] All active workflow periods confirmed closed or paused
- [ ] No active user sessions in the target environment (or users notified of downtime)
- [ ] No running data load jobs or scheduled tasks during window
- [ ] Current dimension member counts documented (for post-deployment comparison)
- [ ] Current business rule compilation status documented
- [ ] Existing dashboard inventory documented

### Documentation

- [ ] Release notes prepared listing all changes in this deployment
- [ ] Known issues and workarounds documented
- [ ] Post-deployment test scripts prepared and assigned to testers
- [ ] User communication drafted for post-deployment notification

---

## 3. Deployment Sequence

Execute the following steps in strict order. Each step must be completed and validated before proceeding to the next. Record the start time, end time, and operator for each step.

### Step 1: Dimensions

**Estimated Duration**: 30-45 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 1.1 | Load Account dimension | Import `Application/Dimensions/Account.xml` | Verify ~800 members loaded, hierarchy intact |
| 1.2 | Load Entity dimension | Import `Application/Dimensions/Entity.xml` | Verify ~120 members loaded, ownership correct |
| 1.3 | Load Scenario dimension | Import `Application/Dimensions/Scenario.xml` | Verify 8 scenario members present |
| 1.4 | Load Time dimension | Import `Application/Dimensions/Time.xml` | Verify period range FY2020-FY2030 |
| 1.5 | Load Flow dimension | Import `Application/Dimensions/Flow.xml` | Verify 6 flow type members |
| 1.6 | Load Consolidation dimension | Import `Application/Dimensions/Consolidation.xml` | Verify 5 consolidation members |
| 1.7 | Load UD1 through UD8 | Import all user-defined dimension files | Verify member counts per specification |
| 1.8 | Validate cube intersections | Run dimension validation queries | Confirm no orphan members, no circular hierarchies |

**Rollback Point A**: If dimension load fails, restore from pre-deployment backup.

### Step 2: Application Configuration

**Estimated Duration**: 15-20 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 2.1 | Apply application settings | Import `Application/Configuration/ApplicationSettings.xml` | Review settings in admin console |
| 2.2 | Configure currency rate types | Import `Application/Configuration/CurrencyRates.xml` | Verify rate types: Month-End, Average, Historical |
| 2.3 | Configure consolidation rules | Import `Application/Configuration/ConsolidationRules.xml` | Verify ownership percentages and elimination rules |
| 2.4 | Validate configuration | Review all configuration parameters in admin console | Spot-check 5 key settings against specification |

**Rollback Point B**: If configuration fails, restore configuration from backup or manually revert changed settings.

### Step 3: Business Rules

**Estimated Duration**: 45-60 minutes

Deploy business rules in the following order to satisfy compilation dependencies.

| # | Action | Rule Count | Validation |
|---|--------|------------|------------|
| 3.1 | Deploy Member Filter rules | 5 rules | All compile without errors |
| 3.2 | Deploy Finance rules | 8 rules | All compile without errors |
| 3.3 | Deploy Calculate rules | 20 rules | All compile without errors |
| 3.4 | Deploy Connector rules | 10 rules | All compile without errors |
| 3.5 | Deploy Event Handler rules | 6 rules | All compile without errors |
| 3.6 | Deploy Dashboard String Functions | 4 rules | All compile without errors |
| 3.7 | Deploy Dashboard DataAdapter rules | 15 rules | All compile without errors |
| 3.8 | Deploy Extender rules | 6 rules | All compile without errors |
| 3.9 | Full compilation check | All 74 rules | Zero compilation errors, zero warnings |

**Rollback Point C**: If business rule deployment fails, restore from pre-deployment backup. Do not attempt to selectively roll back individual rules.

### Step 4: Data Management

**Estimated Duration**: 20-30 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 4.1 | Deploy connector configurations | Import all 10 connector XML files from `DataManagement/Connectors/` | Connectors visible in admin |
| 4.2 | Deploy transformation mappings | Import mapping rules from `DataManagement/Transformations/MappingRules/` | Verify source-to-target mappings |
| 4.3 | Deploy validation rules | Import rules from `DataManagement/Transformations/ValidationRules/` | Validation rules active |
| 4.4 | Deploy lookup tables | Import reference data from `DataManagement/Transformations/LookupTables/` | Lookup values populated |
| 4.5 | Configure data load schedules | Import schedules from `DataManagement/Schedules/` | Schedule times correct for environment |
| 4.6 | Test source connectivity | Execute connection test for each connector | All 10 connectors connect successfully |

**Rollback Point D**: Revert connector configurations and mappings from backup if data management setup fails.

### Step 5: Workflows

**Estimated Duration**: 15-20 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 5.1 | Deploy Monthly Close workflow | Import `Workflows/MonthlyClose.xml` | 7 steps configured correctly |
| 5.2 | Deploy Annual Budget workflow | Import `Workflows/AnnualBudget.xml` | 8 steps configured correctly |
| 5.3 | Deploy Rolling Forecast workflow | Import `Workflows/RollingForecast.xml` | Steps and triggers configured |
| 5.4 | Deploy Account Reconciliation workflow | Import `Workflows/AccountReconciliation.xml` | Preparer/reviewer roles assigned |
| 5.5 | Deploy People Planning workflow | Import `Workflows/PeoplePlanning.xml` | Approval chain configured |
| 5.6 | Validate workflow assignments | Review workflow role-to-group mappings | All roles mapped to correct security groups |

**Rollback Point E**: Restore workflow configurations from backup.

### Step 6: Dashboards

**Estimated Duration**: 30-40 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 6.1 | Deploy shared components | Import `Dashboards/Components/` | Components available for reference |
| 6.2 | Deploy Executive dashboards | Import `Dashboards/Executive/` | Renders without errors |
| 6.3 | Deploy Finance dashboards | Import `Dashboards/Finance/` | Renders without errors |
| 6.4 | Deploy Operations dashboards | Import `Dashboards/Operations/` | Renders without errors |
| 6.5 | Deploy HR dashboards | Import `Dashboards/HR/` | Renders without errors |
| 6.6 | Deploy Reconciliation dashboards | Import `Dashboards/Reconciliation/` | Renders without errors |
| 6.7 | Validate data adapter binding | Open each dashboard and confirm data loads | No empty grids or error messages |

**Rollback Point F**: Restore dashboard definitions from backup.

### Step 7: Security

**Estimated Duration**: 20-30 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 7.1 | Deploy application roles | Import `Security/Roles.xml` | All roles created |
| 7.2 | Deploy security groups | Import `Security/Groups.xml` | Groups linked to AD/IdP |
| 7.3 | Apply access control rules | Import `Security/AccessControl.xml` | Dimension-level security active |
| 7.4 | Validate role assignments | Test login as representative user from each role | Correct data visibility per role |
| 7.5 | Validate data security | Confirm entity-level restrictions enforced | Users see only authorized entities |
| 7.6 | Validate workflow security | Confirm workflow actions restricted by role | Only authorized roles can approve/certify |

**Rollback Point G**: Restore security configuration from backup. Critical -- verify immediately to prevent unauthorized access.

### Step 8: Sample Data Load

**Estimated Duration**: 20-30 minutes

| # | Action | Details | Validation |
|---|--------|---------|------------|
| 8.1 | Load sample actuals data | Execute `CONN_SAP_GL_Actuals` with test data | Data lands in Finance cube |
| 8.2 | Load sample HR data | Execute `CONN_HR_Headcount` with test data | Data lands in HR cube |
| 8.3 | Execute consolidation | Run consolidation for test period | Consolidated balances correct |
| 8.4 | Validate dashboard output | Open key dashboards and verify data display | Numbers match expected test values |
| 8.5 | Run end-to-end workflow test | Execute a single period close cycle | All 7 steps complete successfully |
| 8.6 | Clear test data (PROD only) | Remove sample data before go-live | Cubes clean for production data |

---

## 4. Post-Deployment Validation

### Automated Validation Tests

Execute the validation scripts from `Testing/ValidationScripts/` and confirm all pass.

| Test ID | Test Name                       | Expected Result                              | Pass/Fail |
|---------|----------------------------------|----------------------------------------------|-----------|
| V-001   | Dimension member count check     | Account ~800, Entity ~120, all UDs per spec  |           |
| V-002   | Business rule compilation        | All 74 rules compile with zero errors        |           |
| V-003   | Connector connectivity           | All 10 connectors return successful ping      |           |
| V-004   | Workflow step count verification | Monthly Close: 7, Annual Budget: 8           |           |
| V-005   | Dashboard render test            | All dashboards load without errors            |           |
| V-006   | Security role enumeration        | All roles and groups present and mapped       |           |
| V-007   | Currency rate type verification  | Month-End, Average, Historical types present  |           |
| V-008   | Consolidation rule verification  | Ownership percentages match specification     |           |

### Manual Validation Tests

| Test ID | Test Name                          | Performed By         | Expected Result                         | Pass/Fail |
|---------|------------------------------------|----------------------|-----------------------------------------|-----------|
| M-001   | Income statement drill-down        | Finance Lead         | All levels expand correctly             |           |
| M-002   | Intercompany elimination balance   | Consolidation Lead   | Eliminations net to zero                |           |
| M-003   | Currency translation verification  | Consolidation Lead   | Translated values match manual calc     |           |
| M-004   | Budget input form functionality    | Planning Lead        | Data entry saves and calculates         |           |
| M-005   | Headcount planning form            | HR Lead              | Position-level entry works correctly    |           |
| M-006   | Reconciliation workflow cycle      | Recon Lead           | Full prep/review cycle completes        |           |
| M-007   | Executive dashboard KPIs           | Business Sponsor     | KPIs display correct values             |           |
| M-008   | Data load end-to-end               | Integration Lead     | Source data flows through to reports    |           |
| M-009   | Workflow notification emails       | Project Manager      | Notifications sent to correct recipients|           |
| M-010   | Entity security restriction        | Security Admin       | Users see only authorized entities      |           |

### Sign-Off

| Role                  | Name | Signature | Date | Comments |
|-----------------------|------|-----------|------|----------|
| Solution Architect    |      |           |      |          |
| Consolidation Lead    |      |           |      |          |
| Planning Lead         |      |           |      |          |
| Integration Lead      |      |           |      |          |
| Security Admin        |      |           |      |          |
| Business Sponsor      |      |           |      |          |
| Project Manager       |      |           |      |          |

---

## 5. Rollback Procedures

### Decision Criteria

Initiate rollback if any of the following occur:

- Dimension load creates data corruption or orphan members that cannot be resolved within 30 minutes
- More than 5 business rules fail compilation after repeated attempts
- Source system connectivity cannot be established within the maintenance window
- Security configuration results in unauthorized data access
- Any critical path validation test fails and cannot be remediated within the maintenance window
- Business sponsor or project manager requests rollback

### Rollback Execution

#### Full Rollback (Restore from Backup)

Use this approach when multiple deployment steps have failed or the system is in an inconsistent state.

1. **Stop all OneStream services** on the application server
2. **Restore the database** from the pre-deployment backup taken in the pre-deployment checklist
3. **Restore application files** from the pre-deployment file system backup
4. **Restart OneStream services** and verify the application loads
5. **Execute validation tests V-001 through V-008** to confirm the environment is back to pre-deployment state
6. **Notify stakeholders** that the deployment has been rolled back
7. **Conduct root cause analysis** within 24 hours and document findings

#### Partial Rollback (Selective Revert)

Use this approach when a specific deployment step has failed but prior steps are stable.

1. **Identify the failed step** and its corresponding rollback point (A through G)
2. **Revert only the failed component** using the OneStream admin console or by reimporting the previous version
3. **Revalidate the reverted component** and all downstream dependencies
4. **Document what was reverted** and update the deployment log
5. **Decide whether to proceed** with remaining steps or abort the deployment

#### Rollback Time Estimates

| Rollback Type        | Estimated Duration | Complexity |
|----------------------|-------------------|------------|
| Full database restore | 60-90 minutes    | Low (scripted) |
| Dimension revert     | 15-20 minutes     | Medium      |
| Business rule revert | 20-30 minutes     | Medium      |
| Dashboard revert     | 10-15 minutes     | Low         |
| Security revert      | 10-15 minutes     | High (critical) |

---

## 6. Environment-Specific Configurations

The following settings must be updated for each environment. Never promote environment-specific values across environments.

### Connection Strings and Endpoints

| Setting                          | DEV                                    | QA                                     | PROD                                   |
|----------------------------------|----------------------------------------|----------------------------------------|----------------------------------------|
| SAP RFC Destination              | SAP_DEV_RFC                            | SAP_QA_RFC                             | SAP_PROD_RFC                           |
| SAP Application Server           | sap-dev.company.com                   | sap-qa.company.com                    | sap.company.com                       |
| Oracle DB Connection             | oracle-dev.company.com:1521/DEVDB     | oracle-qa.company.com:1521/QADB       | oracle.company.com:1521/PRODDB        |
| NetSuite Account ID              | DEV_ACCOUNT_ID                        | QA_ACCOUNT_ID (Sandbox)               | PROD_ACCOUNT_ID                        |
| NetSuite Endpoint                | https://dev.suitetalk.api.netsuite.com | https://sb.suitetalk.api.netsuite.com | https://suitetalk.api.netsuite.com    |
| HR System API                    | https://hr-dev.company.com/api        | https://hr-qa.company.com/api         | https://hr.company.com/api            |
| Exchange Rate Service            | https://rates-dev.company.com         | https://rates-qa.company.com          | https://rates.company.com             |
| SMTP Server                      | smtp-dev.company.com                  | smtp-qa.company.com                   | smtp.company.com                      |

### Authentication Configuration

| Setting                          | DEV                                    | QA                                     | PROD                                   |
|----------------------------------|----------------------------------------|----------------------------------------|----------------------------------------|
| Identity Provider                | AD Dev OU                              | AD QA OU                               | AD Prod OU / Azure AD                  |
| SAML Entity ID                   | onestream-dev                          | onestream-qa                           | onestream-prod                         |
| SSO Redirect URL                 | https://onestream-dev.company.com/sso | https://onestream-qa.company.com/sso  | https://onestream.company.com/sso     |
| Session Timeout (minutes)        | 120                                    | 60                                     | 30                                     |
| Failed Login Lockout             | Disabled                               | 5 attempts                             | 3 attempts                             |

### Application Parameters

| Setting                          | DEV                                    | QA                                     | PROD                                   |
|----------------------------------|----------------------------------------|----------------------------------------|----------------------------------------|
| Logging Level                    | Debug                                  | Info                                   | Warning                                |
| Data Load Batch Size             | 1,000                                  | 10,000                                 | 50,000                                 |
| Max Concurrent Users             | 10                                     | 25                                     | 200                                    |
| Email Notifications Enabled      | No                                     | Selected testers only                  | Yes (all configured recipients)        |
| Scheduled Job Execution          | Manual only                            | Scheduled (off-peak)                   | Scheduled (per production calendar)    |
| Data Retention Period            | 1 year                                 | 2 years                                | 7 years                                |
| Audit Log Retention              | 6 months                               | 1 year                                 | 7 years (regulatory requirement)       |

### Data Load Schedules

| Job Name                         | DEV                                    | QA                                     | PROD                                   |
|----------------------------------|----------------------------------------|----------------------------------------|----------------------------------------|
| Daily GL Actuals Load            | Manual trigger                         | 06:00 UTC daily                        | 04:00 UTC daily                        |
| Exchange Rate Load               | Manual trigger                         | 05:30 UTC daily                        | 03:30 UTC daily                        |
| HR Headcount Sync                | Manual trigger                         | 07:00 UTC Mon/Wed/Fri                 | 06:00 UTC daily                        |
| Production Volume Load           | Manual trigger                         | 08:00 UTC daily                        | 05:00 UTC daily                        |
| Monthly Close Sequence           | Manual trigger                         | 1st business day, 22:00 UTC           | 1st business day, 02:00 UTC           |

---

## 7. Go-Live Checklist

### T-5 Business Days (One Week Before)

- [ ] Final UAT sign-off obtained from all business owners
- [ ] All critical and high-severity defects resolved and retested
- [ ] Go-live change request approved by Change Advisory Board
- [ ] Maintenance window scheduled and communicated (recommend Friday evening or Saturday)
- [ ] Production backup strategy confirmed with infrastructure team
- [ ] Support team on-call schedule confirmed for go-live weekend and first week
- [ ] End-user training completed for all user groups
- [ ] User access requests processed and accounts provisioned in PROD
- [ ] Go/No-Go meeting scheduled for T-1

### T-1 Business Day (Day Before)

- [ ] Go/No-Go decision meeting held; decision: GO / NO-GO
- [ ] Final production backup initiated
- [ ] End-user notification sent: system unavailable during maintenance window
- [ ] Data load schedules in PROD paused
- [ ] Active workflow periods confirmed closed or checkpointed
- [ ] War room (physical or virtual) established for deployment team
- [ ] Communication channels confirmed (Teams channel, phone bridge)
- [ ] Migration package staged on deployment server

### T-0 (Go-Live Day)

- [ ] Confirm all users logged out of PROD
- [ ] Execute pre-deployment backup (application + database)
- [ ] Verify backup completed successfully and is restorable
- [ ] Execute deployment sequence Steps 1 through 8
- [ ] Execute post-deployment validation (automated + manual)
- [ ] Load initial production data (current period actuals)
- [ ] Verify production data against source system reports
- [ ] Run consolidation for current open period
- [ ] Validate executive dashboard with production data
- [ ] Obtain deployment sign-off from Solution Architect and Business Sponsor
- [ ] Re-enable data load schedules
- [ ] Send go-live confirmation to all stakeholders

### T+1 to T+5 (First Week)

- [ ] Hypercare support team available 08:00-20:00 local time
- [ ] Monitor first automated data load execution
- [ ] Monitor first daily close cycle
- [ ] Address all Priority 1 issues within 4 hours
- [ ] Address all Priority 2 issues within 1 business day
- [ ] Daily status call with business owners at 09:00 and 16:00
- [ ] Collect user feedback and log enhancement requests
- [ ] Monitor system performance (page load times, rule execution times, data load durations)

### T+30 (One Month Review)

- [ ] First monthly close cycle completed successfully
- [ ] System performance metrics reviewed and benchmarked
- [ ] Outstanding defect backlog reviewed and prioritized
- [ ] User satisfaction survey distributed
- [ ] Lessons learned session conducted
- [ ] Hypercare formally transitioned to steady-state support

---

## 8. Support and Monitoring

### Support Tiers

| Tier   | Team                    | Scope                                           | Response SLA       |
|--------|-------------------------|--------------------------------------------------|--------------------|
| Tier 1 | Help Desk               | Login issues, navigation help, basic inquiries   | 1 hour             |
| Tier 2 | OneStream Admin Team    | Data load issues, workflow errors, report bugs   | 4 hours            |
| Tier 3 | OneStream Development   | Business rule defects, configuration changes     | 1 business day     |
| Tier 4 | OneStream Vendor Support| Platform issues, product defects                 | Per vendor SLA     |

### Monitoring Procedures

#### Daily Monitoring Checklist

- [ ] Verify all scheduled data loads completed successfully (check job history log)
- [ ] Review data load exception reports for rejected or flagged records
- [ ] Verify application services are running and responsive
- [ ] Check disk space utilization on application and database servers (alert at 80%)
- [ ] Review error logs for any unhandled exceptions or warnings
- [ ] Confirm workflow status (no stuck or orphaned workflow items)
- [ ] Verify database backup completed overnight

#### Weekly Monitoring Checklist

- [ ] Review system performance metrics (average page load time target: under 3 seconds)
- [ ] Review business rule execution times (flag any exceeding 60 seconds)
- [ ] Review data load durations and volumes (flag significant deviations from baseline)
- [ ] Review user login activity and concurrent session counts
- [ ] Review security audit log for unusual access patterns
- [ ] Review open support tickets and escalate aging items
- [ ] Verify database index health and fragmentation levels

#### Monthly Monitoring Checklist

- [ ] Review and archive audit logs per retention policy
- [ ] Review storage growth trends and capacity planning
- [ ] Review user access and deactivate terminated employee accounts
- [ ] Review and update security role assignments
- [ ] Execute database maintenance (statistics update, index rebuild)
- [ ] Review and test disaster recovery procedures
- [ ] Generate system health report for IT leadership

### Key Performance Indicators (System Health)

| Metric                              | Target             | Warning Threshold  | Critical Threshold |
|-------------------------------------|--------------------|--------------------|---------------------|
| Application availability            | 99.5%              | Below 99%          | Below 97%           |
| Average dashboard load time         | Under 3 seconds    | 3-5 seconds        | Over 5 seconds      |
| Consolidation execution time        | Under 5 minutes    | 5-10 minutes       | Over 10 minutes     |
| Daily data load completion          | By 06:00 UTC       | After 07:00 UTC    | After 08:00 UTC     |
| Business rule compilation success   | 100%               | Any warning        | Any error            |
| Data load error rate                | Under 0.1%         | 0.1%-1%            | Over 1%              |
| Concurrent user capacity            | 200                | Over 150           | Over 180             |
| Database storage utilization        | Under 70%          | 70%-85%            | Over 85%             |

### Escalation Matrix

| Severity   | Description                                    | Initial Response | Escalation Path                              |
|------------|------------------------------------------------|------------------|-----------------------------------------------|
| Critical   | System down, data corruption, security breach  | 15 minutes       | Admin Team -> Dev Lead -> Solution Architect -> VP IT |
| High       | Major feature broken, data load failure        | 1 hour           | Admin Team -> Dev Lead -> Solution Architect  |
| Medium     | Minor feature issue, workaround available      | 4 hours          | Admin Team -> Dev Lead                        |
| Low        | Cosmetic issue, enhancement request            | 1 business day   | Admin Team (logged for next release)          |

### Incident Response Procedures

#### Data Load Failure

1. Check the OneStream job history log for the failed job and error details
2. Verify source system availability and connectivity
3. Check for data format or mapping issues in the exception report
4. If transient error (network timeout, source system momentarily unavailable), re-execute the load
5. If persistent error, engage Tier 2 support for investigation
6. If data corruption suspected, halt all downstream processing and escalate to Tier 3
7. Document the incident, root cause, and resolution in the incident log

#### Business Rule Failure

1. Identify the failing rule and the specific error message from the execution log
2. Check if the failure is data-dependent (specific member combination) or systemic
3. For data-dependent failures, investigate the source data for the affected intersection
4. For systemic failures, check for recent changes to dimensions or application configuration
5. Escalate to Tier 3 (Development) with the full error log and reproduction steps
6. Apply hotfix through the standard DEV to QA to PROD promotion process (expedited for critical issues)

#### Security Incident

1. Immediately disable the affected user account(s)
2. Capture the security audit log for the affected time period
3. Notify the Information Security team and project manager
4. Assess the scope of unauthorized access or data exposure
5. Implement corrective security controls
6. Document the incident per the organization's security incident response policy
7. Conduct post-incident review within 48 hours

### Disaster Recovery

| Component              | RPO (Recovery Point Objective) | RTO (Recovery Time Objective) | Method                          |
|------------------------|-------------------------------|-------------------------------|----------------------------------|
| Application database   | 6 hours                       | 4 hours                       | Database restore from backup     |
| Application server     | 24 hours                      | 2 hours                       | Server rebuild from image        |
| Business rules/config  | Real-time (version controlled)| 1 hour                        | Redeploy from Git repository     |
| Source system connectors| N/A (stateless)              | 30 minutes                    | Reconfigure from documented settings |

### Contact Information

| Role                        | Primary Contact           | Phone              | Email                        |
|-----------------------------|---------------------------|--------------------|------------------------------|
| Solution Architect          | [Name]                    | [Phone]            | [Email]                      |
| OneStream Admin Lead        | [Name]                    | [Phone]            | [Email]                      |
| Database Administrator      | [Name]                    | [Phone]            | [Email]                      |
| Network Operations          | [Name]                    | [Phone]            | [Email]                      |
| OneStream Vendor Support    | OneStream Support         | [Vendor Phone]     | support@onestream.com        |
| IT Service Desk             | Help Desk                 | [Help Desk Phone]  | helpdesk@company.com         |
| Project Manager             | [Name]                    | [Phone]            | [Email]                      |
| Business Sponsor            | [Name]                    | [Phone]            | [Email]                      |

---

*This deployment runbook is a living document. Update it after each deployment cycle to reflect lessons learned, process improvements, and environment changes. Store the latest version in the project Git repository and ensure all deployment team members have access.*
