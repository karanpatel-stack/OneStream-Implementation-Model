# Solution Architecture Document

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Solution Overview](#1-solution-overview)
2. [Architecture Diagram](#2-architecture-diagram)
3. [Module Architecture](#3-module-architecture)
4. [Integration Architecture Overview](#4-integration-architecture-overview)
5. [Infrastructure Requirements](#5-infrastructure-requirements)
6. [Security Architecture](#6-security-architecture)
7. [Performance Considerations](#7-performance-considerations)
8. [Appendices](#8-appendices)

---

## 1. Solution Overview

### 1.1 Purpose

This document defines the high-level solution architecture for the OneStream XF implementation serving a global multi-plant manufacturing organization. The solution replaces multiple legacy CPM tools (Hyperion, SAP BPC, and various spreadsheet-based processes) with a unified platform that delivers financial consolidation, planning and budgeting, reporting, account reconciliation, and people planning capabilities.

### 1.2 Business Objectives

| Objective | Description | Success Metric |
|-----------|-------------|----------------|
| Unified Financial Close | Single platform for global consolidation across 40+ legal entities | Close cycle reduced from 12 to 5 business days |
| Integrated Planning | Connected financial and operational planning | Budget cycle reduced from 16 to 8 weeks |
| Real-Time Reporting | Self-service analytics with drill-through to source | Report generation time < 5 seconds |
| Regulatory Compliance | Multi-GAAP support (US GAAP, IFRS, local statutory) | Zero restatements |
| Operational Visibility | Plant-level manufacturing analytics | Daily KPI availability |

### 1.3 Scope

**In Scope:**
- Financial consolidation for 40+ legal entities across 12 countries
- Annual budgeting and monthly rolling forecast (18-month horizon)
- Management reporting and executive dashboards
- Account reconciliation for balance sheet accounts
- People planning (headcount, compensation, benefits)
- Integration with 7 source systems

**Out of Scope:**
- Detailed production scheduling (remains in MES)
- Transactional accounting (remains in ERP)
- Tax provisioning (separate tool)
- Treasury management

### 1.4 Solution Principles

1. **Single Source of Truth** -- All financial data consolidated in OneStream
2. **Process-Driven Workflows** -- Embedded task management for close and planning
3. **Self-Service Where Possible** -- Empower business users for reporting and ad-hoc analysis
4. **Scalable Design** -- Architecture supports 50% growth in entities and users
5. **Maintainable Code** -- Standardized business rules, naming conventions, and documentation

---

## 2. Architecture Diagram

### 2.1 End-to-End Data Flow

```
+=====================================================================+
|                        SOURCE SYSTEMS LAYER                         |
+=====================================================================+
|                                                                     |
|  +----------+  +-----------+  +----------+  +---------+  +-------+  |
|  |   SAP    |  |  Oracle   |  | NetSuite |  | Workday |  |  MES  |  |
|  |   HANA   |  |   EBS     |  |  Cloud   |  |   HCM   |  | (5x)  | |
|  | (GL, AP, |  | (GL, AR,  |  | (GL,     |  | (HC,    |  | (Prod |  |
|  |  AR, FA) |  |  FA, INV) |  |  SubLed) |  |  Comp)  |  |  Data)|  |
|  +----+-----+  +----+------+  +----+-----+  +----+----+  +---+---+  |
|       |             |              |              |            |     |
+=====================================================================+
        |             |              |              |            |
        v             v              v              v            v
+=====================================================================+
|                    DATA MANAGEMENT LAYER (ETL)                      |
+=====================================================================+
|                                                                     |
|  +-------------------+  +------------------+  +------------------+  |
|  | Connector BRs     |  | Stage Tables     |  | Transform &      |  |
|  | (CN_SAP_*,        |  | (Staging DB)     |  | Validation       |  |
|  |  CN_Oracle_*,     |  |                  |  | (Mapping,        |  |
|  |  CN_NetSuite_*,   |  |                  |  |  Derivation,     |  |
|  |  CN_Workday_*,    |  |                  |  |  Data Quality)   |  |
|  |  CN_FlatFile_*)   |  |                  |  |                  |  |
|  +-------------------+  +------------------+  +------------------+  |
|           |                      |                     |            |
|           v                      v                     v            |
|  +--------------------------------------------------------------+   |
|  |           Data Management Sequences (Orchestration)          |   |
|  |  Daily: GL Actuals | Weekly: Stats | Monthly: Full Refresh   |   |
|  +--------------------------------------------------------------+   |
|                                                                     |
+=====================================================================+
                                |
                                v
+=====================================================================+
|                     ONESTREAM CUBE LAYER                            |
+=====================================================================+
|                                                                     |
|  +----------------+  +----------------+  +--------+  +-----------+  |
|  | FINANCE CUBE   |  | PLANNING CUBE  |  |HR CUBE |  |RECON CUBE |  |
|  | 12 Dimensions  |  | 10 Dimensions  |  |6 Dims  |  | 4 Dims    |  |
|  | - Consolidation|  | - Budget Entry |  |- HC    |  |- BS Match |  |
|  | - Multi-GAAP   |  | - Forecast     |  |- Comp  |  |- Certify  |  |
|  | - Elimination  |  | - What-If      |  |- Ben   |  |- Variance |  |
|  | - FX Trans     |  | - Drivers      |  |        |  |           |  |
|  +----------------+  +----------------+  +--------+  +-----------+  |
|                                                                     |
|  +--------------------------------------------------------------+   |
|  |                  Calculation Engine                           |   |
|  |  Finance Rules | Calculate Rules | Validation Rules          |   |
|  +--------------------------------------------------------------+   |
|                                                                     |
+=====================================================================+
                                |
                                v
+=====================================================================+
|                     PRESENTATION LAYER                              |
+=====================================================================+
|                                                                     |
|  +----------------+  +----------------+  +---------------------+    |
|  | DASHBOARDS     |  | CUBEVIEWS      |  | REPORTS             |    |
|  | - Executive    |  | - Data Entry   |  | - Financial Stmts   |    |
|  |   Summary      |  | - Inquiry      |  | - Mgmt Reports      |    |
|  | - Plant Ops    |  | - Analysis     |  | - Board Packages     |   |
|  | - Close Status |  | - Validation   |  | - Regulatory Filings |   |
|  | - Budget vs    |  |                |  |                     |    |
|  |   Actual       |  |                |  |                     |    |
|  +----------------+  +----------------+  +---------------------+    |
|                                                                     |
|  +--------------------------------------------------------------+   |
|  |              Task Manager (Workflow Orchestration)            |   |
|  |  Close Tasks | Budget Tasks | Reconciliation Tasks           |   |
|  +--------------------------------------------------------------+   |
|                                                                     |
+=====================================================================+
```

### 2.2 Component Interaction Summary

| Layer | Components | Technology | Data Direction |
|-------|-----------|------------|----------------|
| Source Systems | SAP, Oracle, NetSuite, Workday, MES | Various ERP/HCM/MES | Outbound to OneStream |
| Data Management | Connectors, Staging, Transforms | OneStream DM Framework | Bidirectional (extract/writeback) |
| Cube Layer | 4 Cubes, Calculation Engine | OneStream XF OLAP | Internal processing |
| Presentation | Dashboards, CubeViews, Reports | OneStream UI, Excel Add-in | Outbound to users |
| Workflow | Task Manager | OneStream Workflow Engine | User interaction |

---

## 3. Module Architecture

### 3.1 Financial Consolidation Module

**Purpose:** Automated multi-currency, multi-GAAP consolidation for 40+ legal entities.

**Key Components:**
- **Consolidation Rules (FR_Consolidation):** Ownership-based consolidation with support for full, proportional, and equity methods
- **Currency Translation (FR_CurrencyTranslation):** Multi-rate translation using weighted average, period-end, and historical rates
- **Intercompany Elimination (FR_ICElimination):** Automated matching and elimination of IC balances and transactions
- **Journal Entries (FR_JournalEntries):** Top-side adjustments with full audit trail
- **Flow Analysis (FR_FlowAnalysis):** Roll-forward analysis for balance sheet movements

**Processing Sequence:**
1. Data load and validation
2. Local currency calculations
3. Currency translation
4. IC matching and elimination
5. Ownership-based consolidation
6. Top-side adjustments
7. Reporting consolidation

### 3.2 Planning and Budgeting Module

**Purpose:** Annual budget and monthly rolling forecast with driver-based modeling.

**Key Components:**
- **Revenue Planning:** Top-down targets with bottom-up build by product/customer
- **COGS Planning:** Standard cost models with material, labor, and overhead components
- **OPEX Planning:** Department-level expense budgeting with headcount drivers
- **CAPEX Planning:** Project-based capital expenditure planning with depreciation schedules
- **Rolling Forecast:** 18-month horizon updated monthly with actual-to-plan blending

**Workflow:**
1. Corporate targets distributed
2. Plant-level input (revenue, production, headcount)
3. Automated COGS calculation from production plans
4. OPEX build-up from cost center managers
5. Multi-level review and approval
6. Consolidation of plan data
7. Board reporting package generation

### 3.3 Reporting Module

**Purpose:** Self-service financial and operational reporting.

**Key Components:**
- **Executive Dashboard:** KPI summary with traffic-light indicators
- **Financial Statements:** Income statement, balance sheet, cash flow (multi-GAAP)
- **Management Reports:** Variance analysis (budget vs. actual, prior year)
- **Plant Operations:** Production volumes, yields, cost per unit
- **Board Package:** Automated monthly board reporting with commentary

### 3.4 Account Reconciliation Module

**Purpose:** Balance sheet account certification and reconciliation.

**Key Components:**
- **Reconciliation Templates:** Standardized templates for each BS account type
- **Matching Rules:** Automated matching of subledger to GL balances
- **Certification Workflow:** Preparer/reviewer sign-off with due dates
- **Aging Analysis:** Outstanding item aging with escalation triggers
- **Variance Monitoring:** Threshold-based alerts for unusual balances

### 3.5 People Planning Module

**Purpose:** Headcount and compensation planning integrated with financial plan.

**Key Components:**
- **Headcount Planning:** Position-level planning with hire/term dates
- **Compensation Modeling:** Salary, bonus, benefits, and payroll tax calculations
- **FTE Analysis:** Full-time equivalent trending and forecasting
- **Cost Allocation:** Labor cost distribution to cost centers and projects
- **Workforce Analytics:** Turnover, vacancy, and span-of-control metrics

---

## 4. Integration Architecture Overview

### 4.1 Integration Patterns

| Source System | Pattern | Frequency | Volume (Records/Load) |
|--------------|---------|-----------|----------------------|
| SAP HANA | Database Direct (JDBC) | Daily | ~500,000 |
| Oracle EBS | Database Direct (ODBC) | Daily | ~300,000 |
| NetSuite | REST API | Daily | ~50,000 |
| Workday | REST API | Weekly | ~15,000 |
| MES (5 plants) | File-Based (CSV) | Daily | ~200,000 |
| Excel Templates | File Upload | Monthly | ~5,000 |
| Flat Files (Stat) | File-Based (CSV) | Weekly | ~10,000 |

### 4.2 Data Flow Summary

All integrations follow a standardized pipeline:

```
Source System --> Connector BR (Extract) --> Stage Table --> Transform BR
    --> Validation BR --> Load to Cube --> Post-Load Calculations
```

Detailed integration architecture is documented in `IntegrationArchitecture.md`.

---

## 5. Infrastructure Requirements

### 5.1 Server Architecture

| Component | DEV | QA | PROD |
|-----------|-----|-----|------|
| Application Server | 1x (8 vCPU, 32GB) | 1x (8 vCPU, 32GB) | 2x (16 vCPU, 64GB) HA |
| Database Server | 1x (8 vCPU, 64GB) | 1x (8 vCPU, 64GB) | 2x (16 vCPU, 128GB) HA |
| Web Server | 1x (4 vCPU, 16GB) | 1x (4 vCPU, 16GB) | 2x (8 vCPU, 32GB) LB |
| File Server | Shared | Shared | Dedicated (1TB SSD) |

### 5.2 Software Requirements

| Component | Version | Purpose |
|-----------|---------|---------|
| OneStream XF Platform | 8.x (Latest) | Core application |
| Microsoft SQL Server | 2022 Enterprise | Application database |
| Windows Server | 2022 Datacenter | Operating system |
| IIS | 10.x | Web hosting |
| .NET Framework | 4.8+ | Runtime |
| SSL Certificate | Wildcard (*.company.com) | HTTPS encryption |

### 5.3 Network Requirements

- **Bandwidth:** Minimum 1 Gbps between app and database servers
- **Latency:** < 5ms between app/db tiers; < 100ms to source systems
- **Firewall Rules:** Ports 443 (HTTPS), 1433 (SQL), custom ports for source system connectivity
- **VPN:** Site-to-site VPN for plant MES connectivity
- **DNS:** Internal DNS entries for each environment

### 5.4 Storage Requirements

| Component | DEV | QA | PROD |
|-----------|-----|-----|------|
| Database (Data) | 200 GB | 200 GB | 500 GB |
| Database (Log) | 100 GB | 100 GB | 250 GB |
| Database (TempDB) | 50 GB | 50 GB | 100 GB |
| File Storage | 50 GB | 50 GB | 200 GB |
| Backup Storage | 500 GB | 500 GB | 2 TB |

---

## 6. Security Architecture

### 6.1 Authentication

```
+----------+      +----------+      +-------------+      +-----------+
|  User    | ---> |  Azure   | ---> |  OneStream  | ---> | Application|
| (Browser)|      |   AD     |      |   SSO       |      |  Access   |
|          |      | (SAML 2.0)|     |  Gateway    |      |           |
+----------+      +----------+      +-------------+      +-----------+
```

- **Single Sign-On (SSO):** SAML 2.0 integration with Azure Active Directory
- **Multi-Factor Authentication (MFA):** Enforced via Azure AD Conditional Access policies
- **Service Accounts:** Dedicated service accounts for integration processes (non-interactive)
- **Session Management:** 30-minute idle timeout, 8-hour maximum session

### 6.2 Role-Based Access Control (RBAC)

| Role | Description | User Count | Access Level |
|------|-------------|------------|--------------|
| System Admin | Platform administration | 3 | Full system access |
| Application Admin | Application configuration | 5 | App config, no server access |
| Power User | Report building, ad-hoc | 15 | Full read, limited write |
| Finance Manager | Close process management | 20 | Full cube access, workflow admin |
| Plant Controller | Plant-level data entry | 40 | Plant-specific read/write |
| Budget Analyst | Budget data entry | 30 | Planning cube write, finance read |
| Executive | Dashboard consumption | 25 | Read-only dashboards |
| Auditor | Audit review | 10 | Read-only, full visibility |

### 6.3 Data-Level Security

- **Entity Security:** Users see only their assigned entities (plant, region, or global)
- **Account Security:** Sensitive accounts (executive compensation, M&A) restricted
- **Scenario Security:** Budget lock-down after approval; Actual read-only for non-admins
- **IC Security:** IC partner data visible only to authorized consolidation users

### 6.4 Audit and Compliance

- **Audit Trail:** All data changes logged with user, timestamp, old/new values
- **SOX Compliance:** Segregation of duties enforced (preparer cannot approve)
- **Data Retention:** 7-year retention policy for financial data
- **Access Reviews:** Quarterly access certification reviews

---

## 7. Performance Considerations

### 7.1 Cube Optimization

- **Dense/Sparse Configuration:** Account and Time configured as dense dimensions; Entity and Scenario as sparse to optimize storage and retrieval
- **Aggregation Strategy:** Pre-calculated aggregations at key reporting levels (Region, Business Unit, Total Company)
- **Calculation Caching:** Frequently accessed calculations cached with time-based invalidation
- **Partition Strategy:** Finance cube partitioned by year for historical data isolation

### 7.2 Data Load Performance

| Process | Target Duration | Optimization Strategy |
|---------|----------------|----------------------|
| Daily GL Load (All Sources) | < 30 minutes | Parallel extraction, incremental loads |
| Monthly Full Refresh | < 2 hours | Bulk operations, staged processing |
| Consolidation (Full) | < 15 minutes | Optimized calc scripts, parallel entity processing |
| Currency Translation | < 5 minutes | Rate table caching |
| Budget Calculation | < 10 minutes | Driver-based batch processing |

### 7.3 Reporting Performance

- **Dashboard Load:** Target < 3 seconds for initial render
- **CubeView Retrieval:** Target < 2 seconds for standard views
- **Drill-Through:** Target < 5 seconds for source detail
- **Export to Excel:** Target < 10 seconds for standard reports

### 7.4 Scalability Planning

- **Current Capacity:** 40 entities, 150 concurrent users, 5 years of data
- **Growth Target:** 60 entities, 250 concurrent users, 7 years of data
- **Scale-Up Path:** Vertical scaling of app/db servers (CPU, RAM)
- **Scale-Out Path:** Additional web servers behind load balancer

---

## 8. Appendices

### Appendix A: Acronyms

| Acronym | Definition |
|---------|-----------|
| BS | Balance Sheet |
| CAPEX | Capital Expenditure |
| CF | Cash Flow |
| COGS | Cost of Goods Sold |
| CPM | Corporate Performance Management |
| DM | Data Management |
| ETL | Extract, Transform, Load |
| FA | Fixed Assets |
| FX | Foreign Exchange |
| GL | General Ledger |
| HA | High Availability |
| HC | Headcount |
| HCM | Human Capital Management |
| IC | Intercompany |
| KPI | Key Performance Indicator |
| LB | Load Balanced |
| MES | Manufacturing Execution System |
| OLAP | Online Analytical Processing |
| OPEX | Operating Expense |
| PL | Profit and Loss |
| POV | Point of View |
| RF | Rolling Forecast |
| SSO | Single Sign-On |
| STAT | Statistical |
| UAT | User Acceptance Testing |
| UD | User-Defined (Dimension) |

### Appendix B: Related Documents

| Document | Location |
|----------|----------|
| Dimensional Model | `Documentation/Architecture/DimensionalModel.md` |
| Cube Architecture | `Documentation/Architecture/CubeArchitecture.md` |
| Integration Architecture | `Documentation/Architecture/IntegrationArchitecture.md` |
| Business Rules Catalog | `Documentation/BusinessRules/BR_Catalog.md` |
| Environment Strategy | `Documentation/Deployment/EnvironmentStrategy.md` |
| Migration Checklist | `Documentation/Deployment/MigrationChecklist.md` |

---

*End of Document*
