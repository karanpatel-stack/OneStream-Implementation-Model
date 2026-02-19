# Integration Architecture Design Document

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Source System Inventory](#1-source-system-inventory)
2. [Integration Patterns](#2-integration-patterns)
3. [Data Flow Pipeline](#3-data-flow-pipeline)
4. [Connection Management](#4-connection-management)
5. [Mapping Strategy](#5-mapping-strategy)
6. [Error Handling and Retry Logic](#6-error-handling-and-retry-logic)
7. [Scheduling](#7-scheduling)
8. [Data Quality Framework](#8-data-quality-framework)
9. [Monitoring and Alerting](#9-monitoring-and-alerting)

---

## 1. Source System Inventory

### 1.1 Source Systems Overview

| # | Source System | Version | Type | Owner | Data Domains | Entities Served |
|---|-------------|---------|------|-------|-------------|-----------------|
| 1 | SAP S/4HANA | 2023 | ERP | Corporate IT | GL, AP, AR, FA, CO, MM | NA entities (US, CA) |
| 2 | Oracle EBS | R12.2 | ERP | EU IT | GL, AR, FA, INV | EU entities (DE, UK, FR, IT, PL) |
| 3 | NetSuite | SuiteCloud | Cloud ERP | AP Finance | GL, Sub-Ledger | AP entities (CN, JP, IN, AU), SA entities (BR, AR) |
| 4 | Workday | Current | Cloud HCM | Global HR | Headcount, Compensation, Benefits, Org Structure | All entities |
| 5 | MES Plant 1-5 | Various | MES | Plant Ops | Production Volume, Yield, Quality, Downtime | 5 largest plants |
| 6 | Excel Templates | N/A | Spreadsheet | Various | Stat data, manual adjustments, budget templates | Various |
| 7 | Flat Files (CSV) | N/A | File | Various | Statistical data, external benchmarks | Various |

### 1.2 Data Volume Summary

| Source | Records per Day | Records per Month | Peak Load Volume | Annual Growth |
|--------|----------------|-------------------|-----------------|---------------|
| SAP HANA | 50,000 | 500,000 | 2,000,000 (month-end) | 10% |
| Oracle EBS | 30,000 | 300,000 | 1,200,000 (month-end) | 8% |
| NetSuite | 5,000 | 50,000 | 200,000 (month-end) | 15% |
| Workday | 2,000 (weekly) | 15,000 | 30,000 (annual enrollment) | 5% |
| MES (5 plants) | 20,000 | 200,000 | 500,000 (month-end) | 12% |
| Excel/Flat File | N/A | 5,000-10,000 | 15,000 | Stable |

---

## 2. Integration Patterns

### 2.1 Pattern Overview

Three integration patterns are used based on source system capabilities and data requirements:

```
Pattern 1: Database Direct
+----------+      +------------------+      +----------+
|  SAP     |----->| JDBC/ODBC        |----->| OneStream|
|  Oracle  |      | Direct Query     |      | Stage    |
+----------+      +------------------+      +----------+

Pattern 2: REST API
+----------+      +------------------+      +----------+
| Workday  |----->| REST API         |----->| OneStream|
| NetSuite |      | JSON/XML Payloads|      | Stage    |
+----------+      +------------------+      +----------+

Pattern 3: File-Based
+----------+      +------------------+      +----------+
|  MES     |----->| CSV/Excel Files  |----->| OneStream|
|  Excel   |      | Drop to Share    |      | Stage    |
+----------+      +------------------+      +----------+
```

### 2.2 Pattern 1: Database Direct (SAP HANA, Oracle EBS)

**When to Use:** Source system provides direct database access with acceptable performance impact.

**SAP HANA Integration:**
- **Connection Type:** JDBC over SSL (port 30015)
- **Access Method:** Read-only database user with SELECT on specific views
- **Source Objects:** SAP GL summary views (`FAGLFLEXT`, `BSEG`, `ACDOCA`)
- **Connector Rule:** `CN_SAP_GLActuals`, `CN_SAP_APData`, `CN_SAP_ARData`, `CN_SAP_FixedAssets`
- **Extraction Logic:** Delta extraction based on posting date; full refresh for month-end
- **Network Path:** VPN tunnel from OneStream app server to SAP HANA host

**Oracle EBS Integration:**
- **Connection Type:** ODBC over SSL (port 1521)
- **Access Method:** Read-only schema with views granted to integration user
- **Source Objects:** `GL_BALANCES`, `GL_JE_LINES`, `FA_ADDITIONS`, `MTL_ONHAND_QUANTITIES`
- **Connector Rule:** `CN_Oracle_GLActuals`, `CN_Oracle_ARData`, `CN_Oracle_FixedAssets`, `CN_Oracle_Inventory`
- **Extraction Logic:** Delta extraction based on last update date; full refresh monthly
- **Network Path:** Dedicated link from OneStream to Oracle DB server

### 2.3 Pattern 2: REST API (Workday, NetSuite)

**When to Use:** Cloud-hosted source systems without direct database access.

**Workday Integration:**
- **API Version:** Workday REST API v42.0
- **Authentication:** OAuth 2.0 Client Credentials flow
- **Endpoints:**
  - `GET /workers` -- Active headcount and demographic data
  - `GET /compensation` -- Salary, bonus, and benefits data
  - `GET /organizations` -- Org structure and cost center assignments
- **Connector Rule:** `CN_Workday_Headcount`, `CN_Workday_Compensation`
- **Rate Limits:** 20 requests/minute; batch pagination (100 records/page)
- **Data Format:** JSON responses parsed in connector business rule

**NetSuite Integration:**
- **API Version:** SuiteTalk REST API 2024.1
- **Authentication:** Token-Based Authentication (TBA)
- **Endpoints:**
  - `GET /record/v1/transaction` -- GL transactions
  - `GET /record/v1/account` -- Chart of accounts
  - Saved Search execution for custom extracts
- **Connector Rule:** `CN_NetSuite_GLActuals`, `CN_NetSuite_SubLedger`
- **Rate Limits:** 10 concurrent requests; governed by SuiteCloud license
- **Data Format:** JSON responses with field-level mapping

### 2.4 Pattern 3: File-Based (MES, Excel, CSV)

**When to Use:** Source systems without API capabilities or for manual data submission.

**MES Integration:**
- **File Format:** CSV (comma-delimited, UTF-8 encoding, with header row)
- **Delivery Method:** SFTP drop to designated folder (`/onestream/inbound/mes/`)
- **File Naming:** `MES_{PlantCode}_{DataType}_{YYYYMMDD}.csv`
- **Connector Rule:** `CN_MES_Production`, `CN_MES_Quality`
- **Processing:** File watcher polls every 15 minutes; processes and archives

**Excel Template Integration:**
- **File Format:** `.xlsx` with standardized template structure
- **Delivery Method:** User upload via OneStream file share or dashboard upload control
- **Templates:** Budget input, statistical data, manual adjustments
- **Connector Rule:** `CN_Excel_BudgetInput`, `CN_FlatFile_StatData`
- **Validation:** Template version check, header validation, data type validation

---

## 3. Data Flow Pipeline

### 3.1 Standard Pipeline

Every data integration follows a standardized six-stage pipeline:

```
Stage 1          Stage 2         Stage 3          Stage 4         Stage 5          Stage 6
+----------+   +----------+   +-----------+   +----------+   +-----------+   +----------+
|          |   |          |   |           |   |          |   |           |   |          |
| EXTRACT  |-->|  STAGE   |-->| TRANSFORM |-->| VALIDATE |-->|   LOAD    |-->|  POST-   |
|          |   |          |   |           |   |          |   |           |   |  LOAD    |
+----------+   +----------+   +-----------+   +----------+   +-----------+   +----------+
| Connector|   | Stage    |   | Map source|   | DQ rules |   | Write to  |   | Trigger  |
| BR pulls |   | tables   |   | to target |   | Balance  |   | cube data |   | calcs    |
| from     |   | in SQL   |   | dimension |   | checks   |   | cells     |   | Notify   |
| source   |   | Server   |   | members   |   | Thresh-  |   | Clear +   |   | users    |
|          |   |          |   | Derive    |   | old chks |   | reload    |   |          |
|          |   |          |   | Aggregate |   | Referent.|   |           |   |          |
+----------+   +----------+   +-----------+   +----------+   +-----------+   +----------+
```

### 3.2 Stage Details

#### Stage 1: Extract (Connector Business Rules)

| Connector Rule | Source | Extraction Method | Key Fields Extracted |
|---------------|--------|-------------------|---------------------|
| CN_SAP_GLActuals | SAP HANA | SQL query via JDBC | CompanyCode, GLAccount, CostCenter, ProfitCenter, PostingDate, Amount, Currency |
| CN_SAP_APData | SAP HANA | SQL query via JDBC | Vendor, InvoiceDate, Amount, PaymentTerms |
| CN_SAP_ARData | SAP HANA | SQL query via JDBC | Customer, InvoiceDate, Amount, AgingBucket |
| CN_SAP_FixedAssets | SAP HANA | SQL query via JDBC | AssetClass, AcquisitionDate, CostBasis, AccumDepr |
| CN_Oracle_GLActuals | Oracle EBS | SQL query via ODBC | LedgerId, AccountCombo, Period, Amount, Currency |
| CN_Oracle_ARData | Oracle EBS | SQL query via ODBC | CustomerNumber, InvoiceAmount, AgingBucket |
| CN_Oracle_FixedAssets | Oracle EBS | SQL query via ODBC | AssetNumber, Category, Cost, Depreciation |
| CN_Oracle_Inventory | Oracle EBS | SQL query via ODBC | Item, SubInventory, Quantity, Value |
| CN_NetSuite_GLActuals | NetSuite | REST API | Account, Department, Location, Amount, Currency |
| CN_NetSuite_SubLedger | NetSuite | REST API (Saved Search) | SubLedgerType, Account, Amount, Reference |
| CN_Workday_Headcount | Workday | REST API | WorkerID, Position, Department, Location, Status |
| CN_Workday_Compensation | Workday | REST API | WorkerID, CompPlan, Amount, EffectiveDate |
| CN_MES_Production | MES CSV | File read | PlantCode, ProductCode, Quantity, Date, Shift |
| CN_MES_Quality | MES CSV | File read | PlantCode, BatchID, DefectCount, YieldPct |
| CN_Excel_BudgetInput | Excel | File read | Entity, Account, Period, Amount |
| CN_FlatFile_StatData | CSV | File read | Entity, StatAccount, Period, Value |

#### Stage 2: Stage (Staging Tables)

Staging tables reside in the OneStream application database under a dedicated schema:

| Table | Source | Key Columns | Row Retention |
|-------|--------|-------------|---------------|
| STG_GL_Actuals | SAP, Oracle, NetSuite | SourceSystem, Entity, Account, Period, Amount | 3 months |
| STG_AP_Detail | SAP | Vendor, Entity, Amount, DueDate | 3 months |
| STG_AR_Detail | SAP, Oracle | Customer, Entity, Amount, AgingBucket | 3 months |
| STG_FA_Detail | SAP, Oracle | Asset, Entity, Cost, AccumDepr | 3 months |
| STG_Inventory | Oracle | Item, Entity, Qty, Value | 3 months |
| STG_Headcount | Workday | WorkerID, Entity, Department, Status | 3 months |
| STG_Compensation | Workday | WorkerID, CompType, Amount | 3 months |
| STG_Production | MES | Plant, Product, Quantity, Date | 3 months |
| STG_Quality | MES | Plant, BatchID, DefectCount, Yield | 3 months |
| STG_StatData | Flat File | Entity, Account, Period, Value | 3 months |

#### Stage 3: Transform

Transformations include:

1. **Dimension Mapping:** Map source system codes to OneStream dimension members
   - SAP Company Code `1000` -> Entity `Plant_US01_Detroit`
   - SAP GL Account `400000` -> Account `REV_Product_Sales`
   - Oracle Period `JAN-26` -> Time `Jan_2026`

2. **Derivation:** Calculate derived dimension members
   - Department derived from SAP Cost Center
   - Product derived from SAP Profit Center
   - Customer segment derived from customer master

3. **Aggregation:** Summarize detail to OneStream granularity
   - Daily transactions -> Monthly totals
   - Line-item detail -> Account-level summary
   - Individual positions -> Headcount aggregates

4. **Currency Conversion:** Standardize source amounts
   - Amounts stored in entity local currency
   - Multi-currency sources tagged with original currency

#### Stage 4: Validate

| Validation Rule | Description | Action on Failure |
|----------------|-------------|-------------------|
| VAL_TrialBalanceCheck | Debits must equal credits for each entity/period | Reject load; alert data team |
| VAL_MemberValidation | All mapped dimension members must exist | Reject unmapped records; log |
| VAL_DuplicateCheck | No duplicate source keys in same load | Reject duplicates; alert |
| VAL_ThresholdCheck | Amount variance vs. prior period < configurable % | Warning alert; allow load |
| VAL_CompletenessCheck | All expected entities/accounts present | Warning alert; allow load |
| VAL_ReferentialIntegrity | IC partners must have matching counterparties | Warning alert; log for review |
| VAL_DataTypeCheck | Numeric fields contain valid numbers | Reject invalid records; log |
| VAL_DateRangeCheck | Period falls within expected range | Reject out-of-range; alert |

#### Stage 5: Load

- **Clear Strategy:** Clear target intersection before loading to prevent stale data accumulation
- **Load Method:** Bulk load via `SetDataBuffer` for performance
- **Concurrency:** Single-writer per entity/scenario/period to prevent conflicts
- **Audit:** Load timestamp, record count, and user logged to audit table

#### Stage 6: Post-Load

- **Trigger Calculations:** Run account derivations, allocations, KPI calculations
- **Notify Users:** Send email/dashboard notification on successful load
- **Update Status:** Mark entity/period as "Data Loaded" in workflow tracker
- **Archive Files:** Move processed files to archive folder with timestamp

---

## 4. Connection Management

### 4.1 Connection Inventory

| Connection Name | Type | Server/URL | Port | Auth Method | Credential Store |
|----------------|------|-----------|------|-------------|-----------------|
| CONN_SAP_HANA | JDBC | saphana.corp.internal | 30015 | SQL Login | OneStream Credential Vault |
| CONN_Oracle_EBS | ODBC | oradb.corp.internal | 1521 | SQL Login | OneStream Credential Vault |
| CONN_NetSuite_API | REST | https://netsuite.com/services | 443 | Token (TBA) | OneStream Credential Vault |
| CONN_Workday_API | REST | https://wd5.myworkday.com/api | 443 | OAuth 2.0 | OneStream Credential Vault |
| CONN_MES_SFTP | SFTP | sftp.mfg.internal | 22 | SSH Key | Key stored on app server |
| CONN_FileShare | UNC | \\\\fileserver\\onestream | N/A | Windows Auth | Service account |

### 4.2 Credential Management

- **Storage:** All credentials stored in OneStream's encrypted credential vault
- **Rotation:** Passwords rotated every 90 days; API tokens rotated annually
- **Access:** Only OneStream service account can access credentials at runtime
- **Environment Isolation:** Each environment (DEV/QA/PROD) has separate credentials
- **Audit:** Credential access logged with timestamp and calling rule

### 4.3 Connection Testing

| Test | Frequency | Method | Alert On Failure |
|------|-----------|--------|-----------------|
| Connectivity Test | Every 6 hours | Lightweight ping/query | Email to IT and data team |
| Authentication Test | Daily | Attempt login with credentials | Email to IT |
| Query Performance Test | Weekly | Execute standard query; measure time | Alert if > 2x baseline |
| API Rate Limit Check | Per execution | Monitor HTTP 429 responses | Log and backoff |

---

## 5. Mapping Strategy

### 5.1 Mapping Tables

Dimension mappings are maintained in SQL lookup tables with the following structure:

```sql
CREATE TABLE DIM_MAPPING (
    MappingID           INT IDENTITY PRIMARY KEY,
    SourceSystem        NVARCHAR(50),       -- SAP, Oracle, NetSuite, etc.
    SourceDimension     NVARCHAR(50),       -- CompanyCode, GLAccount, etc.
    SourceValue         NVARCHAR(100),      -- 1000, 400000, etc.
    TargetDimension     NVARCHAR(50),       -- Entity, Account, etc.
    TargetMember        NVARCHAR(100),      -- Plant_US01_Detroit, REV_Product_Sales
    EffectiveDate       DATE,               -- Mapping effective date
    ExpirationDate      DATE,               -- NULL if current
    IsActive            BIT DEFAULT 1,
    LastModifiedBy      NVARCHAR(50),
    LastModifiedDate    DATETIME DEFAULT GETDATE()
)
```

### 5.2 Mapping Coverage by Source System

| Source System | Entity Mapping | Account Mapping | Time Mapping | UD Mappings | Total Mapping Rows |
|--------------|:-----------:|:-----------:|:-----------:|:-----------:|:-----------------:|
| SAP HANA | 12 | 450 | Auto | 200 | ~662 |
| Oracle EBS | 15 | 380 | Auto | 180 | ~575 |
| NetSuite | 18 | 300 | Auto | 100 | ~418 |
| Workday | 40 | 30 | Auto | 80 | ~150 |
| MES | 5 | 20 | Auto | 25 | ~50 |
| **Total** | **90** | **1,180** | **Auto** | **585** | **~1,855** |

### 5.3 Unmapped Value Handling

| Scenario | Action | Notification |
|----------|--------|-------------|
| New source value not in mapping table | Route to error staging table | Email to data steward |
| Source value maps to inactive target member | Route to error staging table | Email to data steward |
| Source value maps to multiple target members | Apply first active mapping; log warning | Warning in load report |
| Null or empty source value | Apply default member (e.g., DEPT_None) | Log only |

### 5.4 Mapping Maintenance Process

1. **Request:** Business or IT submits mapping change request (new entity, new account, etc.)
2. **Review:** Data steward reviews mapping request against dimension model
3. **Update:** Mapping updated in DEV environment mapping table
4. **Test:** Test data load in DEV with new mapping
5. **Promote:** Promote mapping to QA, then PROD via deployment scripts
6. **Document:** Update mapping documentation and notify affected users

---

## 6. Error Handling and Retry Logic

### 6.1 Error Classification

| Error Class | Examples | Severity | Auto-Retry | Resolution |
|------------|---------|----------|:----------:|------------|
| Connection Failure | Network timeout, auth failure | Critical | Yes (3x) | Escalate to IT after retries |
| Source Data Error | Invalid data type, missing required field | High | No | Route to error table; notify steward |
| Mapping Error | Unmapped source value | Medium | No | Route to error table; add mapping |
| Validation Warning | Threshold exceeded, completeness gap | Low | N/A | Log warning; continue processing |
| System Error | Out of memory, disk full | Critical | No | Immediate escalation to IT |

### 6.2 Retry Logic

```
Retry Configuration:
- Max Retries: 3
- Initial Delay: 30 seconds
- Backoff Multiplier: 2x (30s, 60s, 120s)
- Max Delay: 300 seconds
- Retry On: Connection timeouts, HTTP 429 (rate limit), HTTP 503 (service unavailable)
- Do Not Retry: Authentication failures (401/403), data errors (400), system errors (500)
```

### 6.3 Error Staging

Failed records are stored in an error staging table for manual review:

```sql
CREATE TABLE STG_ERROR_LOG (
    ErrorID             INT IDENTITY PRIMARY KEY,
    LoadDate            DATETIME DEFAULT GETDATE(),
    SourceSystem        NVARCHAR(50),
    ConnectorRule       NVARCHAR(100),
    ErrorClass          NVARCHAR(50),
    ErrorMessage        NVARCHAR(MAX),
    SourceRecord        NVARCHAR(MAX),     -- JSON representation of failed record
    ResolvedDate        DATETIME NULL,
    ResolvedBy          NVARCHAR(50) NULL,
    ResolutionAction    NVARCHAR(200) NULL  -- Reprocessed, Skipped, Mapping Added
)
```

### 6.4 Error Notification Escalation

| Elapsed Time | Action |
|-------------|--------|
| Immediate | Log error to STG_ERROR_LOG |
| 0 minutes | Email notification to data operations team |
| 30 minutes (if unresolved) | Email escalation to data steward |
| 2 hours (if unresolved) | Email escalation to finance manager and IT lead |
| 4 hours (if critical and unresolved) | SMS/page to on-call support |

---

## 7. Scheduling

### 7.1 Schedule Overview

```
Daily Schedule (Business Days)
================================
02:00 AM ET  - MES file pickup and processing (CN_MES_Production, CN_MES_Quality)
02:30 AM ET  - SAP GL extract (CN_SAP_GLActuals)
03:00 AM ET  - Oracle GL extract (CN_Oracle_GLActuals)
03:30 AM ET  - NetSuite GL extract (CN_NetSuite_GLActuals)
04:00 AM ET  - Transform and validate all GL data
04:30 AM ET  - Load to Finance cube
05:00 AM ET  - Post-load calculations (account derivations, KPIs)
05:30 AM ET  - Dashboard refresh and cache warm-up
06:00 AM ET  - Daily load completion notification

Weekly Schedule (Sunday Night)
================================
10:00 PM ET  - Workday headcount extract (CN_Workday_Headcount)
10:30 PM ET  - Workday compensation extract (CN_Workday_Compensation)
11:00 PM ET  - Statistical data file processing (CN_FlatFile_StatData)
11:30 PM ET  - Transform and validate HR/stat data
12:00 AM ET  - Load to HR cube and Finance cube (stat accounts)
12:30 AM ET  - Post-load calculations

Monthly Schedule (Business Day 1-3)
================================
BD1 06:00 PM - Full refresh: SAP (all modules)
BD1 08:00 PM - Full refresh: Oracle (all modules)
BD1 10:00 PM - Full refresh: NetSuite
BD2 02:00 AM - Full transform and validation
BD2 04:00 AM - Full cube load (clear and reload current period)
BD2 06:00 AM - Consolidation pre-check
BD2 08:00 AM - Close process begins (user-driven)
BD3            - Reconciliation data push to Recon cube
```

### 7.2 Schedule Dependencies

```
CN_SAP_GLActuals ----+
                     |
CN_Oracle_GLActuals -+--> Transform_GL --> Validate_GL --> Load_Finance --> PostCalc
                     |
CN_NetSuite_GLActuals+

CN_Workday_HC -------+--> Transform_HR --> Validate_HR --> Load_HR --> Push_to_Planning
CN_Workday_Comp -----+

CN_MES_Production ---+--> Transform_MES --> Validate_MES --> Load_Finance (STAT accounts)
CN_MES_Quality ------+
```

### 7.3 Holiday and Exception Handling

- **Public Holidays:** US holidays skip daily SAP load (no new transactions); Oracle and NetSuite still run
- **Month-End Processing:** Extended processing windows (2x daily allocation)
- **Year-End Processing:** Special year-end close schedule (documented separately)
- **System Maintenance:** Scheduled maintenance windows on first Saturday of each month (02:00-06:00 AM ET)

---

## 8. Data Quality Framework

### 8.1 Data Quality Dimensions

| Dimension | Definition | Measurement Method | Target |
|-----------|-----------|-------------------|--------|
| Completeness | All expected data is present | Entity/account coverage check | 100% of expected entities |
| Accuracy | Data matches source system | Source-to-target reconciliation | 100% match (within $1 tolerance) |
| Timeliness | Data available by SLA deadline | Load completion timestamp | Daily by 06:00 AM ET |
| Consistency | Data aligns across sources | Cross-source reconciliation | < 0.1% unexplained variance |
| Validity | Data conforms to business rules | Validation rule pass rate | > 99.5% pass rate |
| Uniqueness | No duplicate records | Duplicate detection check | 0 duplicates |

### 8.2 Quality Checks by Stage

| Pipeline Stage | Quality Check | Rule | Frequency |
|---------------|---------------|------|-----------|
| Extract | Record count vs. expected | Compare to prior load +/- 20% | Every load |
| Extract | Schema validation | Verify column names, data types | Every load |
| Stage | Duplicate detection | Check for duplicate source keys | Every load |
| Stage | Null value check | Required fields must be non-null | Every load |
| Transform | Mapping coverage | All source values must map | Every load |
| Transform | Aggregation balance | Sum of detail = source total | Every load |
| Validate | Trial balance | Debits = Credits per entity | Every load |
| Validate | Period balance check | BS accounts: opening + movement = closing | Monthly |
| Validate | Cross-source reconciliation | SAP total = Oracle total for shared accounts | Monthly |
| Load | Cell count verification | Loaded cells = expected cells | Every load |
| Post-Load | Consolidation balance | Parent = Sum of children | Monthly |

### 8.3 Data Quality Scorecard

A monthly data quality scorecard is generated and distributed to stakeholders:

| Metric | Target | Actual (Calculated) | RAG Status |
|--------|--------|-------------------|------------|
| Load Success Rate | > 99% | Automated | Green/Amber/Red |
| Mapping Error Rate | < 0.5% | Automated | Green/Amber/Red |
| Trial Balance Pass Rate | 100% | Automated | Green/Red |
| Timeliness (Daily by 06:00 AM) | 100% | Automated | Green/Amber/Red |
| Reconciliation Variance | < $1,000 | Automated | Green/Amber/Red |
| Open Error Records | < 10 | Automated | Green/Amber/Red |

---

## 9. Monitoring and Alerting

### 9.1 Monitoring Dashboard

A dedicated operations dashboard in OneStream displays real-time integration status:

| Panel | Content | Refresh |
|-------|---------|---------|
| Load Status | Current status of all active loads (Running, Complete, Failed) | Real-time |
| Load History | Last 30 days of load executions with success/failure indicators | Hourly |
| Record Counts | Records extracted, staged, loaded per source per day | Per load |
| Error Summary | Open errors by source, class, and age | Real-time |
| Performance Trends | Load duration trending (actual vs. baseline) | Daily |
| Data Freshness | Last successful load time per source system | Real-time |
| Schedule Compliance | On-time completion percentage for scheduled loads | Daily |

### 9.2 Alert Configuration

| Alert | Condition | Recipients | Method | Priority |
|-------|-----------|------------|--------|----------|
| Load Failure | Any load job fails after retries | Data Ops, IT Support | Email | Critical |
| Load Delayed | Daily load not complete by 06:30 AM ET | Data Ops, Finance Mgr | Email | High |
| Mapping Error | > 10 unmapped records in single load | Data Steward | Email | Medium |
| Threshold Breach | Period-over-period variance > 25% | Data Steward, Controller | Email | Medium |
| Connection Down | Source system connection fails health check | IT Support | Email, SMS | Critical |
| Disk Space Low | Staging DB < 10% free space | IT Infrastructure | Email, SMS | Critical |
| Long-Running Load | Load duration > 2x historical average | Data Ops | Email | Low |
| Reconciliation Gap | Source-to-target variance > $10,000 | Controller, Data Ops | Email | High |

### 9.3 Logging Standard

All integration processes log to a centralized table:

```sql
CREATE TABLE INT_EXECUTION_LOG (
    LogID               BIGINT IDENTITY PRIMARY KEY,
    ExecutionID         UNIQUEIDENTIFIER,   -- Groups all logs for one execution
    Timestamp           DATETIME2 DEFAULT SYSDATETIME(),
    ConnectorRule       NVARCHAR(100),
    Stage               NVARCHAR(50),       -- Extract, Stage, Transform, Validate, Load, PostLoad
    LogLevel            NVARCHAR(10),       -- DEBUG, INFO, WARN, ERROR, FATAL
    Message             NVARCHAR(MAX),
    RecordCount         INT NULL,
    DurationMs          INT NULL,
    SourceSystem        NVARCHAR(50),
    EntityScope         NVARCHAR(200),
    PeriodScope         NVARCHAR(50),
    UserName            NVARCHAR(100)
)
```

### 9.4 SLA Definitions

| SLA | Description | Target | Measurement | Escalation |
|-----|-------------|--------|-------------|------------|
| Daily Data Availability | GL actuals available in OneStream | By 06:00 AM ET | Load completion timestamp | 30-minute grace; then escalate |
| Weekly HR Data | Headcount and comp data refreshed | By Monday 06:00 AM ET | Load completion timestamp | Same as daily |
| Monthly Full Refresh | All data refreshed for close | By BD1 + 1 at 06:00 AM ET | Load completion timestamp | Immediate escalation |
| Error Resolution | Mapping and data errors resolved | Within 4 business hours | Error log resolution time | Escalate per Section 6.4 |
| Source System Recovery | Resume loads after source outage | Within 2 hours of source recovery | Monitoring detection + load trigger | Automatic retry + notification |

---

*End of Document*
