# OneStream XF Enterprise Manufacturing Accelerator

<p align="center">
  <strong>Pre-Built CPM Solution for Global Multi-Plant Manufacturing</strong><br>
  194 Artifacts &nbsp;|&nbsp; 74 Business Rules &nbsp;|&nbsp; 53,000+ Lines of Code &nbsp;|&nbsp; 6 Modules &nbsp;|&nbsp; 14 Dimensions
</p>

---

## Overview

This repository contains a **complete, production-ready OneStream XF implementation** for a global multi-plant manufacturing company. It serves as an accelerator that reduces implementation timelines by 50-60% and covers the full CPM lifecycle:

- **Financial Consolidation** — Multi-currency, multi-GAAP with IC elimination, equity pickup, and minority interest
- **Planning & Budgeting** — Driver-based planning with BOM rollups, headcount planning, CAPEX, rolling forecasts
- **Reporting & Dashboards** — 16 executive dashboards with KPI cockpits, variance waterfalls, and plant analytics
- **Data Management** — 10 pre-built connectors for SAP, Oracle, NetSuite, Workday, and MES systems
- **Account Reconciliation** — Automated matching engine with risk-based workflows
- **People Planning** — FTE-to-cost modeling with compensation, benefits, and burden rates

---

## Dashboards

### Executive Summary
C-suite KPI overview with revenue trends, margin analysis, and entity performance scorecards.

![Executive Summary Dashboard](Dashboards/Mockups/DB_ExecutiveSummary.png)

### Consolidation Status
Real-time close tracking showing workflow step completion by entity across the monthly close cycle.

![Consolidation Status Dashboard](Dashboards/Mockups/DB_ConsolidationStatus.png)

### Plant Performance
Operational dashboard with OEE, capacity utilization, first pass yield, and scrap rates by plant.

![Plant Performance Dashboard](Dashboards/Mockups/DB_PlantPerformance.png)

### Production Variance
Variance waterfall decomposing budget-to-actual differences across volume, price, mix, and cost drivers.

![Production Variance Dashboard](Dashboards/Mockups/DB_ProductionVariance.png)

### P&L Waterfall
Income statement bridge showing the walk from prior year to current year net income.

![P&L Waterfall Dashboard](Dashboards/Mockups/DB_PLWaterfall.png)

### Balance Sheet
Asset and liability composition with key financial ratios and period-over-period comparison.

![Balance Sheet Dashboard](Dashboards/Mockups/DB_BalanceSheet.png)

### Cash Flow
Cash flow waterfall from operating, investing, and financing activities with trend analysis.

![Cash Flow Dashboard](Dashboards/Mockups/DB_CashFlow.png)

### Budget vs Actual
Grouped comparison of actual vs budget across all P&L categories with entity-level heatmap.

![Budget vs Actual Dashboard](Dashboards/Mockups/DB_BudgetVsActual.png)

### Rolling Forecast Trend
18-month rolling forecast with actual overlay, budget baseline, and confidence bands.

![Rolling Forecast Trend Dashboard](Dashboards/Mockups/DB_RollingForecastTrend.png)

### People Planning
Headcount and compensation analysis with FTE distribution by function and cost breakdowns.

![People Planning Dashboard](Dashboards/Mockups/DB_PeoplePlanning.png)

### CAPEX Tracker
Capital project status with budget vs spend tracking, completion percentages, and forecasted EAC.

![CAPEX Tracker Dashboard](Dashboards/Mockups/DB_CAPEXTracker.png)

### Intercompany Reconciliation
IC balance matching with exception reporting, tolerance analysis, and unmatched item detail.

![Intercompany Reconciliation Dashboard](Dashboards/Mockups/DB_IntercompanyRecon.png)

### Data Quality Scorecard
Validation rule results with overall DQ score, completeness, accuracy, timeliness, and consistency metrics.

![Data Quality Scorecard Dashboard](Dashboards/Mockups/DB_DataQualityScorecard.png)

### KPI Cockpit
4x3 grid of operational KPIs with sparklines and trend indicators — margins, turnover, DSO/DPO/DIO, OEE.

![KPI Cockpit Dashboard](Dashboards/Mockups/DB_KPICockpit.png)

### Account Reconciliation Status
Reconciliation progress tracker with risk-based categorization and unreconciled account detail.

![Account Reconciliation Status Dashboard](Dashboards/Mockups/DB_AccountReconStatus.png)

### Supply Chain Analytics
Inventory analysis with turns, days of supply, fill rates, and supplier performance rankings.

![Supply Chain Analytics Dashboard](Dashboards/Mockups/DB_SupplyChainAnalytics.png)

---

## CubeViews

### Data Entry Forms

#### Revenue Data Entry
Monthly revenue input form with gross revenue by channel, deductions, and calculated net revenue. Yellow cells indicate editable input fields.

![Revenue Data Entry](CubeViews/Mockups/CV_DataEntry_Revenue.png)

#### Operating Expense Data Entry
Departmental OPEX entry organized by SG&A, R&D, and Marketing with quarterly rollups.

![OPEX Data Entry](CubeViews/Mockups/CV_DataEntry_OPEX.png)

#### Headcount Planning
Position-level headcount planning with FTE count, average salary, benefits rate, and total annual cost.

![Headcount Data Entry](CubeViews/Mockups/CV_DataEntry_Headcount.png)

#### Capital Expenditure Planning
Project-level CAPEX input with quarterly spend, estimate at completion, and percentage complete.

![CAPEX Data Entry](CubeViews/Mockups/CV_DataEntry_CAPEX.png)

#### Production Volume & Capacity
Production volume entry with machine hours, quality metrics (OEE, scrap rate, first pass yield), and labor hours.

![Production Data Entry](CubeViews/Mockups/CV_DataEntry_Production.png)

### Financial Reports

#### Income Statement (P&L)
Full P&L from Net Revenue through Net Income with Actual, Budget, Variance $, Variance %, Prior Year, and YoY columns.

![P&L Report](CubeViews/Mockups/CV_Report_PL.png)

#### Balance Sheet
Complete balance sheet with Assets, Liabilities, and Equity showing current period, prior period, and year-over-year changes.

![Balance Sheet Report](CubeViews/Mockups/CV_Report_BS.png)

#### Cash Flow Statement
Indirect method cash flow with Operating, Investing, and Financing activities plus budget comparison.

![Cash Flow Report](CubeViews/Mockups/CV_Report_CF.png)

#### Budget vs Actual Variance
Detailed BvA analysis including flex budget, 4-way cost variance decomposition (price, usage, efficiency, volume).

![BvA Report](CubeViews/Mockups/CV_Report_BvA.png)

#### Consolidation Report
Full elimination detail: Local → FX Translation → IC Elimination → Minority Interest → Consolidated.

![Consolidation Report](CubeViews/Mockups/CV_Report_Consolidation.png)

---

## Architecture

### Cube Architecture

| Cube | Dimensions | Purpose |
|------|-----------|---------|
| **Finance** | Account, Entity, Scenario, Time, Flow, Consolidation, UD1-UD6 | Main consolidation & reporting |
| **Planning** | Account, Entity, Scenario, Time, UD1-UD4, UD6, UD8 | Operational planning & forecasting |
| **HR** | Account (HR subset), Entity, Scenario, Time, UD2, UD8 | People planning & comp modeling |
| **Reconciliation** | Account, Entity, Time, UD8 | Account reconciliation |

### Dimensional Model (14 Dimensions)

| Dimension | Members | Description |
|-----------|---------|-------------|
| Account | ~800 | P&L, Balance Sheet, Cash Flow, Statistical accounts |
| Entity | ~120 | Corporate → Region → Country → Plant → Production Line |
| Scenario | 27 | Actual, Budget, Forecast Q1-Q4, RF Jan-Dec, WhatIf |
| Time | 73 | CY2024-2027, Monthly + Quarterly + Annual |
| Flow | 14 | Opening, Movement, Closing, FX Translation, Elimination |
| Consolidation | 8 | Local, Translated, Proportional, Eliminated, Consolidated |
| UD1 Product | 66 | Industrial / Consumer / Specialty → Family → SKU |
| UD2 CostCenter | 80+ | Production, Warehouse, QC, Maintenance, Admin, R&D, Sales |
| UD3 Intercompany | 19 | Mirrors Entity for IC partner matching |
| UD4 Project | 30 | CAPEX Expansion, Maintenance, Automation, R&D |
| UD5 CustomerSegment | 21 | OEM, Aftermarket, Government, Commercial |
| UD6 Channel | 19 | Direct, Distributor, OEM, E-Commerce |
| UD7 Plant | 16 | Cross-reference for multi-entity plant analysis |
| UD8 Version | 5 | Working, Submitted, Approved, Published, Archived |

### Entity Hierarchy

```
Global Consolidated
├── Corporate_HQ
├── Americas
│   ├── Plant_US01_Detroit      [USD]
│   ├── Plant_US02_Houston      [USD]
│   ├── Plant_US03_Charlotte    [USD]
│   ├── Plant_CA01_Toronto      [CAD]
│   └── Plant_MX01_Monterrey    [MXN]
├── EMEA
│   ├── Plant_DE01_Munich       [EUR]
│   ├── Plant_DE02_Stuttgart    [EUR]
│   ├── Plant_UK01_Birmingham   [GBP]
│   └── Plant_FR01_Lyon         [EUR]
├── APAC
│   ├── Plant_CN01_Shanghai     [CNY]
│   ├── Plant_CN02_Shenzhen     [CNY]
│   ├── Plant_JP01_Osaka        [JPY]
│   └── Plant_IN01_Pune         [INR]
└── Eliminations
```

---

## Business Rules (74 Total)

| Category | Count | Key Capabilities |
|----------|-------|-----------------|
| **Finance Rules** | 8 | Consolidation, FX translation, IC elimination, equity pickup, minority interest, goodwill, journal entries, flow analysis |
| **Calculate Rules** | 20 | COGS allocation, overhead absorption, standard cost variance (4-way), OEE, revenue recognition (ASC 606), BOM rollup, driver-based planning, headcount, CAPEX depreciation, cash flow, KPIs |
| **Connector Rules** | 10 | SAP HANA GL + production + materials, Oracle EBS GL + sub-ledger, NetSuite, Workday HCM, MES, Excel templates, flat files |
| **Dashboard Adapters** | 15 | Executive summary, plant performance, variance waterfall, P&L bridge, BvA, rolling forecast, IC recon, CAPEX tracker, KPI cockpit |
| **Member Filters** | 5 | Entity security, scenario locking, time period control, product access, cost center access |
| **Event Handlers** | 6 | Data quality validation, submission control, IC matching, budget threshold alerts, audit logging, notifications |
| **Extenders** | 6 | Batch consolidation, data archival, ETL orchestrator, recon engine, RF seeder, report distribution |
| **String Functions** | 4 | Status icons, variance formatting, KPI thresholds, dynamic multi-language labels |

---

## Data Integration Pipeline

```
Source Systems              ETL Pipeline                    Target Cubes
─────────────         ─────────────────────          ──────────────────
SAP S/4HANA    ──►    EXTRACT  (10 Connectors)
Oracle EBS     ──►    STAGE    (8 Staging Defs)     ──►  Finance Cube
NetSuite       ──►    TRANSFORM (8 Mappings)        ──►  Planning Cube
Workday HCM    ──►    VALIDATE  (Quality Rules)     ──►  HR Cube
MES Systems    ──►    LOAD     (6 Load Sequences)   ──►  Recon Cube
```

**27 Data Management artifacts** covering connections, staging, transformation (account/entity/product/cost center mapping, currency conversion, sign convention), and loading.

---

## Workflow Design

### Monthly Financial Close (7 Steps, 8-Day Cycle)

| Step | Activity | Duration |
|------|----------|----------|
| 1 | Data Load & Validation | Day 1-2 |
| 2 | Local GAAP Adjustments | Day 2-3 |
| 3 | Intercompany Reconciliation | Day 3-4 |
| 4 | Currency Translation | Day 4-5 |
| 5 | Consolidation | Day 5-6 |
| 6 | Management Review | Day 6-7 |
| 7 | Certification & Lock | Day 7-8 |

### Annual Budget (8 Steps, Sep-Nov)

| Step | Activity | Duration |
|------|----------|----------|
| 1 | Budget Kickoff & Targets | Week 1-2 |
| 2 | Revenue Planning | Week 3-5 |
| 3 | COGS Planning | Week 3-5 |
| 4 | OPEX Planning | Week 3-5 |
| 5 | Headcount Planning | Week 3-5 |
| 6 | CAPEX Planning | Week 4-6 |
| 7 | Review & Iteration | Week 6-7 |
| 8 | Approval & Publish | Week 7-8 |

Additional workflows: **Rolling Forecast** (monthly, 18-month window), **Account Reconciliation** (risk-based), **People Planning** (annual + quarterly refresh).

---

## Deployment

### Environment Strategy

| Environment | Purpose | Config |
|-------------|---------|--------|
| **DEV** | Development & unit testing | Debug logging, no rate limits, 20 users |
| **QA** | Integration & UAT testing | Production-mirror data (masked), strict validation, 50 users |
| **PROD** | Production | HA with F5 load balancer, 99.9% SLA, MFA required, 250 users |

### Automation Scripts

| Script | Purpose |
|--------|---------|
| `Deploy_BusinessRules.ps1` | Upload, compile, and activate 74 business rules with rollback |
| `Deploy_Dimensions.ps1` | Load dimension hierarchies with backup and delta comparison |
| `Deploy_DataManagement.ps1` | Deploy ETL sequences in dependency order |
| `Validate_Deployment.ps1` | Post-deployment health checks: compilation, connectivity, data integrity |

---

## Project Structure

```
OneStream/
├── Application/           AppConfig.xml, CubeDefinitions.xml
├── Dimensions/            21 CSV files across 14 dimensions
├── BusinessRules/
│   ├── Finance/           8 consolidation & FX rules
│   ├── Calculate/         20 calculation & planning rules
│   ├── Connector/         10 source system connectors
│   ├── DashboardDataAdapter/  15 dashboard data adapters
│   ├── DashboardStringFunction/  4 formatting rules
│   ├── MemberFilter/      5 security & access rules
│   ├── EventHandler/      6 workflow automation rules
│   └── Extender/          6 batch processing rules
├── DataManagement/        27 XMLs (connections, stage, transform, load)
├── Workflows/             5 workflow definitions
├── Dashboards/            16 dashboard XMLs + mockup illustrations
├── CubeViews/             10 cube views + mockup illustrations
├── Security/              Roles, assignments, data access profiles
├── Testing/               Validation scripts, test cases, sample data
├── Documentation/         Architecture, user guides, deployment docs
├── Deployment/            PowerShell scripts + environment configs
└── PitchDeck/             Sales presentation (PowerPoint)
```

---

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Platform | OneStream XF (cloud or on-premise) |
| Business Rules | VB.NET (compiled within OneStream runtime) |
| Configurations | XML-based application metadata |
| ETL | OneStream native connectors, REST/SOAP APIs, ODBC |
| Dashboards | OneStream Dashboard framework (HTML5) |
| Authentication | Azure AD / SAML 2.0 SSO with MFA |
| Version Control | Git |
| Deployment | PowerShell automation (DEV → QA → PROD) |

---

## Getting Started

1. **Review** [DEPLOYMENT.md](DEPLOYMENT.md) for the full deployment runbook
2. **Load Dimensions** from `Dimensions/` (foundation — must be loaded first)
3. **Configure Application** settings from `Application/`
4. **Import Business Rules** from `BusinessRules/` (74 VB.NET files)
5. **Set Up Data Management** pipelines from `DataManagement/` (update connection strings)
6. **Deploy Workflows** from `Workflows/`
7. **Import Dashboards** and CubeViews
8. **Apply Security** roles and data access profiles
9. **Run Validation** with sample data from `Testing/SampleData/`

---

## License

This implementation is proprietary and confidential. All artifacts are subject to the terms of the OneStream XF software license agreement.
