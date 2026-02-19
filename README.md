# OneStream XF Enterprise Manufacturing Implementation

## Project Overview

This repository contains the full implementation artifacts for a OneStream XF deployment serving a global multi-plant manufacturing company. The solution consolidates financial reporting, planning, budgeting, forecasting, account reconciliation, people planning, and operational analytics into a single unified platform, replacing a fragmented landscape of spreadsheets, legacy EPM tools, and disconnected data sources.

The implementation supports 40+ legal entities across 12 countries, 6 manufacturing plants, and 3 distribution centers, with multi-currency consolidation, intercompany elimination, and regulatory reporting (US GAAP, IFRS, and local statutory).

---

## Modules

### Financial Consolidation
- Multi-GAAP consolidation (US GAAP primary, IFRS and local statutory parallel)
- Automated intercompany eliminations with matching and reconciliation
- Currency translation (month-end rate for balance sheet, average rate for income statement, historical rate for equity)
- Ownership management supporting direct and indirect holdings
- Journal entry processing and top-side adjustments
- Consolidation audit trail and certification workflows

### Planning, Budgeting, and Forecasting
- Annual operating plan with driver-based revenue and expense models
- Rolling 18-month forecast updated monthly
- Capital expenditure planning with asset-level detail
- Workforce cost planning integrated with headcount models
- Manufacturing cost planning (raw materials, labor, overhead allocation)
- What-if scenario modeling and version comparison

### Reporting and Dashboards
- Executive KPI dashboards (revenue, margin, EBITDA, working capital)
- Plant-level operational dashboards (OEE, yield, throughput, scrap rates)
- Self-service ad hoc reporting via Cube Views
- Financial statement packages (income statement, balance sheet, cash flow)
- Variance analysis (actual vs. budget, actual vs. forecast, actual vs. prior year)
- Board reporting packages with drill-through to transactional detail

### Account Reconciliation
- Balance sheet reconciliation with risk-based scheduling
- Automated matching for high-volume, low-risk accounts
- Preparer/reviewer workflow with electronic sign-off
- Aging analysis and open item management
- Integration with subledger systems for supporting detail

### People Planning
- Headcount planning by position, department, and location
- Compensation modeling (salary, bonus, benefits, payroll taxes)
- New hire and termination modeling with effective dating
- FTE and contractor blended rate planning
- Allocation of people costs to cost centers and projects

### Data Management (ETL Integration)
- SAP ECC/S4HANA integration via RFC and BAPI connectors
- Oracle EBS integration via database views and REST APIs
- NetSuite integration via SuiteTalk SOAP and RESTlet services
- Flat file staging for ancillary sources (production systems, HR databases)
- Data quality rules with validation, transformation, and exception handling
- Full audit trail of all data loads with row-level lineage

---

## Architecture Overview

The application is built on four cubes with 14 shared and cube-specific dimensions, over 75 business rules, and 5 major workflow profiles.

### High-Level Architecture

```
Source Systems                OneStream XF Platform                 Consumers
-----------------    ---------------------------------    ----------------------
SAP ECC / S4HANA --> |                                 | --> Executive Dashboards
Oracle EBS --------> |  Finance Cube    Planning Cube  | --> Financial Packages
NetSuite ----------> |  HR Cube         Recon Cube     | --> Operational Reports
Flat Files --------> |                                 | --> Board Books
                     |  75+ Business Rules             | --> Ad Hoc Analysis
                     |  5 Workflow Profiles             | --> Regulatory Filings
                     ---------------------------------
```

---

## Folder Structure

```
OneStream/
|
|-- README.md                          # This file
|-- DEPLOYMENT.md                      # Deployment runbook and procedures
|
|-- Application/
|   |-- Dimensions/
|   |   |-- Account.xml                # ~800 members, chart of accounts
|   |   |-- Entity.xml                 # ~120 members, legal and management entities
|   |   |-- Scenario.xml               # Actual, Budget, Forecast, What-If
|   |   |-- Time.xml                   # Monthly periods, FY2020-FY2030
|   |   |-- Flow.xml                   # Flow types (periodic, YTD, QTD)
|   |   |-- Consolidation.xml          # Local, Translated, Eliminations, Consolidated
|   |   |-- UD1_Product.xml            # Product hierarchy (~200 members)
|   |   |-- UD2_Customer.xml           # Customer segments and channels
|   |   |-- UD3_Project.xml            # Capital projects and cost centers
|   |   |-- UD4_Intercompany.xml       # Intercompany partner dimension
|   |   |-- UD5_Movement.xml           # Movement types for roll-forward
|   |   |-- UD6_Currency.xml           # Reporting currency overrides
|   |   |-- UD7_Department.xml         # Departmental hierarchy
|   |   |-- UD8_Driver.xml             # Planning drivers and assumptions
|   |
|   |-- Configuration/
|   |   |-- ApplicationSettings.xml    # Global application parameters
|   |   |-- CurrencyRates.xml          # Exchange rate type definitions
|   |   |-- ConsolidationRules.xml     # Ownership and elimination rules
|   |   |-- SecurityModel.xml          # Role and group definitions
|   |
|   |-- CubeViews/
|       |-- FinancialStatements/       # Income statement, balance sheet, cash flow
|       |-- VarianceAnalysis/          # Budget vs. actual, forecast vs. actual
|       |-- PlantOperations/           # OEE, yield, production metrics
|       |-- PeoplePlanning/            # Headcount and compensation views
|
|-- BusinessRules/
|   |-- Finance/                       # 8 finance business rules
|   |-- Calculate/                     # 20 calculation rules
|   |-- Connector/                     # 10 connector business rules
|   |-- DashboardDataAdapter/          # 15 dashboard data adapters
|   |-- DashboardStringFunction/       # 4 string function rules
|   |-- MemberFilter/                  # 5 member filter rules
|   |-- EventHandler/                  # 6 event handler rules
|   |-- Extender/                      # 6 extender rules
|
|-- Dashboards/
|   |-- Executive/                     # C-suite KPI dashboards
|   |-- Finance/                       # Controller and FP&A dashboards
|   |-- Operations/                    # Plant manager dashboards
|   |-- HR/                            # People planning dashboards
|   |-- Reconciliation/               # Account reconciliation dashboards
|   |-- Components/                    # Shared dashboard components
|
|-- DataManagement/
|   |-- Connectors/
|   |   |-- SAP_GL_Connector.xml       # SAP general ledger extract
|   |   |-- SAP_AP_AR_Connector.xml    # SAP accounts payable/receivable
|   |   |-- Oracle_GL_Connector.xml    # Oracle general ledger extract
|   |   |-- NetSuite_GL_Connector.xml  # NetSuite trial balance extract
|   |   |-- HR_Headcount_Connector.xml # HR system headcount data
|   |   |-- Production_Connector.xml   # Manufacturing execution system data
|   |
|   |-- Transformations/
|   |   |-- MappingRules/              # Source-to-target dimension mappings
|   |   |-- ValidationRules/           # Data quality and validation checks
|   |   |-- LookupTables/             # Reference data for transformations
|   |
|   |-- Schedules/
|       |-- DailyLoads.xml             # Daily data load schedule
|       |-- MonthlyClose.xml           # Month-end close data load sequence
|       |-- AnnualBudget.xml           # Annual budget data load schedule
|
|-- Workflows/
|   |-- MonthlyClose.xml               # 7-step monthly close workflow
|   |-- AnnualBudget.xml               # 8-step annual budget workflow
|   |-- RollingForecast.xml            # Rolling forecast workflow
|   |-- AccountReconciliation.xml      # Account reconciliation workflow
|   |-- PeoplePlanning.xml             # People planning workflow
|
|-- Security/
|   |-- Roles.xml                      # Application roles
|   |-- Groups.xml                     # Security groups
|   |-- AccessControl.xml             # Dimension-level security assignments
|
|-- Testing/
|   |-- TestPlan.xlsx                  # Master test plan
|   |-- TestCases/                     # Individual test case documentation
|   |-- TestData/                      # Sample data for testing
|   |-- ValidationScripts/            # Automated validation scripts
|
|-- Documentation/
    |-- FunctionalDesign/              # Functional design documents
    |-- TechnicalDesign/               # Technical design documents
    |-- UserGuides/                    # End-user training materials
    |-- AdminGuides/                   # System administration guides
```

---

## Dimensional Model

### Dimension Summary

| Dimension       | Type           | Approx. Members | Description                                          |
|-----------------|----------------|-----------------|------------------------------------------------------|
| Account         | Standard       | ~800            | Full chart of accounts (assets, liabilities, equity, revenue, expense, statistical) |
| Entity          | Standard       | ~120            | Legal entities, management entities, and elimination entities across 12 countries |
| Scenario        | Standard       | 8               | Actual, Budget, Forecast, Rolling Forecast, What-If, Prior Year, Restatement, Baseline |
| Time            | Standard       | ~130            | Monthly periods from FY2020 through FY2030, with quarterly and annual roll-ups |
| Flow            | Standard       | 6               | Periodic, YTD, QTD, HTD, Rolling 12, LTM            |
| Consolidation   | Standard       | 5               | Local, Translated, Proportional, Eliminations, Consolidated |
| UD1 (Product)   | User Defined   | ~200            | Product families, product lines, SKU groupings       |
| UD2 (Customer)  | User Defined   | ~80             | Customer segments, channels, geographic markets      |
| UD3 (Project)   | User Defined   | ~60             | Capital projects, cost centers, initiative tracking  |
| UD4 (IC Partner)| User Defined   | ~120            | Intercompany partner entities (mirrors Entity dim)   |
| UD5 (Movement)  | User Defined   | ~30             | Movement types: opening, additions, disposals, revaluation, closing |
| UD6 (Currency)  | User Defined   | ~15             | Reporting currency overrides and constant currency flags |
| UD7 (Department)| User Defined   | ~45             | Departmental hierarchy for cost allocation and reporting |
| UD8 (Driver)    | User Defined   | ~35             | Planning assumptions, growth rates, allocation drivers |

### Account Dimension Hierarchy (Top Level)

```
Total Accounts
|-- Balance Sheet
|   |-- Assets
|   |   |-- Current Assets (Cash, AR, Inventory, Prepaid)
|   |   |-- Non-Current Assets (PP&E, Intangibles, Investments)
|   |-- Liabilities
|   |   |-- Current Liabilities (AP, Accruals, Current Debt)
|   |   |-- Non-Current Liabilities (Long-term Debt, Deferred Tax)
|   |-- Equity
|       |-- Retained Earnings, AOCI, Common Stock
|
|-- Income Statement
|   |-- Revenue
|   |   |-- Product Revenue (by line)
|   |   |-- Service Revenue
|   |   |-- Intercompany Revenue
|   |-- Cost of Goods Sold
|   |   |-- Raw Materials
|   |   |-- Direct Labor
|   |   |-- Manufacturing Overhead
|   |-- Gross Profit
|   |-- Operating Expenses
|   |   |-- Selling & Marketing
|   |   |-- General & Administrative
|   |   |-- Research & Development
|   |-- Operating Income
|   |-- Other Income / Expense
|   |-- Tax Provision
|   |-- Net Income
|
|-- Cash Flow (Indirect Method)
|   |-- Operating Activities
|   |-- Investing Activities
|   |-- Financing Activities
|
|-- Statistical Accounts
    |-- Headcount, FTE, Units Produced, Machine Hours
```

### Entity Dimension Hierarchy (Top Level)

```
Global Consolidated
|-- North America
|   |-- US Operations
|   |   |-- US Plant 1 (Detroit)
|   |   |-- US Plant 2 (Houston)
|   |   |-- US Distribution (Chicago)
|   |   |-- US Corporate HQ
|   |-- Canada Operations
|       |-- Canada Plant (Toronto)
|
|-- Europe
|   |-- Germany Operations
|   |   |-- Germany Plant (Munich)
|   |   |-- Germany Sales Office
|   |-- UK Operations
|   |-- France Operations
|   |-- Netherlands Holding
|
|-- Asia Pacific
|   |-- China Operations
|   |   |-- China Plant (Shanghai)
|   |   |-- China Distribution
|   |-- Japan Sales Office
|   |-- Singapore Regional HQ
|
|-- Eliminations
    |-- NA Eliminations
    |-- Europe Eliminations
    |-- APAC Eliminations
    |-- Global Eliminations
```

---

## Cube Architecture

| Cube            | Primary Use                  | Key Dimensions                                                                 | Data Granularity      |
|-----------------|------------------------------|--------------------------------------------------------------------------------|-----------------------|
| **Finance**     | Actuals, consolidation, statutory reporting | Account, Entity, Scenario, Time, Flow, Consolidation, UD1-UD6               | Monthly by entity     |
| **Planning**    | Budget, forecast, what-if modeling          | Account, Entity, Scenario, Time, Flow, UD1-UD4, UD7, UD8                    | Monthly by entity     |
| **HR**          | People planning, headcount, compensation    | Account, Entity, Scenario, Time, UD7 (Department), UD8 (Driver)             | Monthly by department |
| **Reconciliation** | Balance sheet account reconciliation     | Account, Entity, Scenario, Time, UD5 (Movement)                              | Monthly by account    |

### Cube Details

**Finance Cube** -- The primary cube for financial consolidation and reporting. Receives actual data from SAP, Oracle, and NetSuite via connectors. Supports multi-currency translation, intercompany eliminations, and equity method investments. Contains 5 years of historical actuals and current year data.

**Planning Cube** -- Supports the annual budget cycle and rolling forecast process. Contains driver-based models for revenue (price x volume), cost of goods sold (material costs, labor rates, overhead allocation), and operating expenses (headcount-driven, trend-based, and zero-based). Includes what-if scenario capabilities.

**HR Cube** -- Dedicated cube for people planning at the position level. Models compensation components (base salary, bonus targets, benefits, payroll taxes by jurisdiction), tracks headcount movements (new hires, terminations, transfers), and supports merit increase and promotion modeling.

**Reconciliation Cube** -- Supports the balance sheet reconciliation process. Tracks opening balances, movements, adjustments, and closing balances at the account level. Integrates with subledger detail for drill-through and supports the preparer/reviewer certification workflow.

---

## Business Rules Inventory

### Summary

| Rule Type                  | Count | Description                                              |
|----------------------------|-------|----------------------------------------------------------|
| Finance                    | 8     | Core financial calculations and consolidation logic      |
| Calculate                  | 20    | Calculation, allocation, and driver-based model rules    |
| Connector                  | 10    | Data integration connectors for source system loads      |
| Dashboard DataAdapter      | 15    | Data retrieval rules powering dashboard components       |
| Dashboard String Functions | 4     | String manipulation and formatting for dashboard display |
| Member Filters             | 5     | Dynamic member selection for reports and forms           |
| Event Handlers             | 6     | Workflow event-triggered automation rules                |
| Extenders                  | 6     | Custom extensions for specialized processing             |
| **Total**                  | **74**|                                                          |

### Finance Rules (8)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `FIN_Consolidation`                  | Main consolidation logic including ownership calculations   |
| `FIN_CurrencyTranslation`            | Multi-rate currency translation processing                 |
| `FIN_IntercompanyElimination`        | Automated intercompany elimination entries                  |
| `FIN_EquityPickup`                   | Equity method investment calculations                      |
| `FIN_CashFlowIndirect`              | Indirect cash flow statement derivation                     |
| `FIN_RetainedEarnings`              | Retained earnings roll-forward calculation                  |
| `FIN_MinorityInterest`              | Non-controlling interest calculations                       |
| `FIN_StatutoryAdjustments`          | GAAP-to-local statutory bridge adjustments                  |

### Calculate Rules (20)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `CALC_RevenueModel`                  | Price x volume revenue driver model                        |
| `CALC_COGSAllocation`               | Cost of goods sold allocation by product                    |
| `CALC_LaborCostModel`              | Direct and indirect labor cost calculations                 |
| `CALC_OverheadAllocation`           | Manufacturing overhead allocation across plants             |
| `CALC_DepreciationSchedule`         | Asset depreciation schedule calculations                    |
| `CALC_AmortizationSchedule`         | Intangible asset amortization calculations                  |
| `CALC_InterestExpense`              | Debt schedule and interest expense modeling                 |
| `CALC_TaxProvision`                 | Estimated tax provision by jurisdiction                     |
| `CALC_WorkingCapitalModel`          | Days sales outstanding, inventory turns, DPO calculations   |
| `CALC_VarianceAnalysis`             | Multi-dimensional variance decomposition                    |
| `CALC_HeadcountCost`               | Fully-loaded headcount cost build-up                        |
| `CALC_MeritIncrease`               | Annual merit increase modeling                              |
| `CALC_BonusAccrual`                | Bonus accrual calculation and true-up                       |
| `CALC_BenefitsAllocation`          | Benefits cost allocation by entity and department           |
| `CALC_CapexModel`                  | Capital expenditure and project cost modeling                |
| `CALC_AllocationsEngine`           | Multi-step corporate cost allocation engine                  |
| `CALC_TransferPricing`             | Intercompany transfer pricing calculations                   |
| `CALC_SeasonalityFactors`          | Seasonal spread patterns for forecast distribution           |
| `CALC_RollingForecastBlend`        | Actual/forecast blending for rolling forecast                |
| `CALC_KPICalculations`             | Derived KPI and ratio calculations                           |

### Connector Rules (10)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `CONN_SAP_GL_Actuals`               | SAP general ledger trial balance extract                   |
| `CONN_SAP_AP_Subledger`             | SAP accounts payable subledger detail                      |
| `CONN_SAP_AR_Subledger`             | SAP accounts receivable subledger detail                   |
| `CONN_Oracle_GL_Actuals`            | Oracle EBS general ledger extract                          |
| `CONN_NetSuite_GL_Actuals`          | NetSuite trial balance via SuiteTalk                       |
| `CONN_HR_Headcount`                 | HR system headcount and position data                      |
| `CONN_HR_Compensation`              | HR system compensation and benefits data                   |
| `CONN_Production_Volumes`           | MES production volume and yield data                       |
| `CONN_ExchangeRates`                | Exchange rate feed from treasury system                    |
| `CONN_FixedAssets`                  | Fixed asset register detail from asset system              |

### Dashboard DataAdapter Rules (15)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `DA_ExecutiveKPIs`                   | Executive summary KPI data retrieval                       |
| `DA_IncomeStatement`                 | Income statement data with variance columns                |
| `DA_BalanceSheet`                    | Balance sheet data with period comparison                  |
| `DA_CashFlow`                        | Cash flow statement data adapter                           |
| `DA_RevenueAnalysis`                | Revenue drill-down by product, customer, region             |
| `DA_MarginAnalysis`                 | Gross and operating margin analysis                         |
| `DA_PlantPerformance`              | Plant-level operational KPIs                                |
| `DA_WorkingCapital`                 | Working capital dashboard data                              |
| `DA_HeadcountSummary`              | Headcount and FTE summary data                              |
| `DA_CompensationDetail`            | Compensation detail by position and department              |
| `DA_BudgetVsActual`                | Budget variance analysis data                               |
| `DA_ForecastAccuracy`              | Forecast accuracy tracking over time                        |
| `DA_ReconStatus`                    | Reconciliation status and aging dashboard                   |
| `DA_IntercompanyBalance`           | Intercompany balance matching dashboard                     |
| `DA_CapexTracking`                 | Capital expenditure tracking vs. approved budget            |

### Dashboard String Functions (4)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `SF_PeriodLabel`                     | Dynamic period label formatting                            |
| `SF_CurrencyFormat`                 | Currency-aware number formatting                            |
| `SF_VarianceIndicator`             | Conditional variance display (favorable/unfavorable)        |
| `SF_EntityDescription`             | Dynamic entity description and metadata display             |

### Member Filter Rules (5)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `MF_ActiveEntities`                  | Filters to entities active in selected scenario/period     |
| `MF_PLAccounts`                     | Income statement account filter with dynamic hierarchy      |
| `MF_BSAccounts`                     | Balance sheet account filter with classification            |
| `MF_PlanningAccounts`              | Planning-specific account filter for input forms            |
| `MF_ReconAccounts`                 | Reconciliation-eligible balance sheet accounts              |

### Event Handler Rules (6)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `EH_DataLoadValidation`             | Pre-load data validation and quality checks                |
| `EH_ConsolidationTrigger`          | Post-load automatic consolidation trigger                   |
| `EH_WorkflowNotification`          | Workflow step completion email notifications                |
| `EH_AuditTrailCapture`             | Detailed audit trail logging for data changes               |
| `EH_CertificationEnforcement`      | Certification prerequisite enforcement                      |
| `EH_LockPeriod`                    | Period lock enforcement after close completion               |

### Extender Rules (6)

| Rule Name                            | Purpose                                                    |
|--------------------------------------|------------------------------------------------------------|
| `EXT_DataExportPackage`             | Automated financial package export to PDF/Excel            |
| `EXT_ReconMatchingEngine`          | Automated transaction matching for reconciliation           |
| `EXT_BulkJournalImport`           | Mass journal entry import and validation                    |
| `EXT_AllocationBatchProcess`       | Batch allocation processing with audit output               |
| `EXT_RateLoadAutomation`          | Automated exchange rate loading and validation               |
| `EXT_ArchiveUtility`              | Historical data archival and purge utility                   |

---

## Workflow Design

### Monthly Close Workflow (7 Steps)

| Step | Name                    | Type        | Owner                | Duration | Dependencies      |
|------|-------------------------|-------------|----------------------|----------|-------------------|
| 1    | Data Load & Validation  | Automated   | Data Management Team | Day 1-2  | Source system close |
| 2    | Journal Entries         | Manual      | Corporate Accounting | Day 2-3  | Step 1            |
| 3    | Intercompany Matching   | Semi-Auto   | IC Coordinator       | Day 3-4  | Step 1            |
| 4    | Local Close & Certify   | Manual      | Entity Controllers   | Day 4-5  | Steps 2, 3        |
| 5    | Consolidation           | Automated   | Consolidation Team   | Day 5-6  | Step 4            |
| 6    | Review & Adjustments    | Manual      | Corporate Controller | Day 6-7  | Step 5            |
| 7    | Final Certification     | Manual      | VP Finance / CFO     | Day 7-8  | Step 6            |

### Annual Budget Workflow (8 Steps)

| Step | Name                       | Type        | Owner               | Duration    | Dependencies |
|------|----------------------------|-------------|----------------------|-------------|--------------|
| 1    | Target Setting             | Manual      | CFO / FP&A Director  | Week 1-2    | None         |
| 2    | Assumption Distribution    | Automated   | FP&A Team            | Week 2      | Step 1       |
| 3    | Revenue Planning           | Manual      | Sales / BU Leaders   | Week 3-5    | Step 2       |
| 4    | COGS & Opex Planning       | Manual      | Plant / Dept Managers| Week 3-5    | Step 2       |
| 5    | People Planning            | Manual      | HR / Dept Managers   | Week 3-5    | Step 2       |
| 6    | Capital Planning           | Manual      | Engineering / Ops    | Week 4-6    | Step 2       |
| 7    | Consolidation & Review     | Semi-Auto   | FP&A Team            | Week 6-7    | Steps 3-6   |
| 8    | Executive Approval         | Manual      | CFO / CEO            | Week 7-8    | Step 7       |

### Rolling Forecast Workflow

- Triggered monthly after close completion
- Blends actual months with remaining forecast periods
- 18-month forward horizon, refreshed each cycle
- Uses trend extrapolation with manual override capability
- Incorporates latest run-rate and known commitments

### Account Reconciliation Workflow

- Risk-based scheduling (high-risk accounts reconciled monthly, low-risk quarterly)
- Automated balance pull from Finance cube at period close
- Preparer completes reconciliation with supporting documentation
- Reviewer approves or returns with comments
- Aging tracked for open reconciling items
- Dashboard provides real-time status visibility to management

### People Planning Workflow

- Position-level detail planning by department manager
- Compensation modeling with effective date logic
- Automated calculation of payroll taxes by jurisdiction
- Benefits cost allocation using blended rates
- Approval routing: Manager, HR Business Partner, VP, CFO
- Monthly actuals comparison and variance analysis

---

## Technology Stack

| Component             | Technology                                                   |
|-----------------------|--------------------------------------------------------------|
| Platform              | OneStream XF (cloud-hosted or on-premise)                    |
| Business Rules        | VB.NET (compiled within OneStream runtime)                   |
| Configurations        | XML-based application metadata                               |
| Source System ETL     | OneStream native connectors, Stage tables, REST/SOAP APIs    |
| Dashboards            | OneStream Dashboard framework (HTML5, responsive)            |
| Authentication        | Active Directory / SAML 2.0 / Azure AD integration          |
| Version Control       | Git (this repository)                                        |
| CI/CD                 | Migration packages exported from DEV, promoted through environments |

---

## Getting Started

### Prerequisites

- OneStream XF platform instance (v8.0 or later recommended)
- Administrative access to the target OneStream environment
- Network connectivity to source systems (SAP, Oracle, NetSuite)
- Active Directory or identity provider configured for SSO

### Initial Setup

1. **Review the Deployment Runbook** -- See [DEPLOYMENT.md](DEPLOYMENT.md) for the full deployment sequence, environment strategy, and validation procedures.

2. **Dimension Load** -- Begin by loading the dimension XML files from `Application/Dimensions/`. These define the structural foundation and must be loaded before any other artifacts.

3. **Application Configuration** -- Apply the settings from `Application/Configuration/` including currency rate types, consolidation rules, and security model.

4. **Business Rule Deployment** -- Import business rules from the `BusinessRules/` directory in the sequence outlined in the deployment runbook.

5. **Data Management Setup** -- Configure connectors from `DataManagement/Connectors/` and apply transformation mappings. Test connectivity to all source systems.

6. **Workflow Configuration** -- Deploy workflow profiles from the `Workflows/` directory and assign workflow roles to security groups.

7. **Dashboard Deployment** -- Import dashboards from the `Dashboards/` directory and validate data adapter connectivity.

8. **Security Assignment** -- Apply role-based security from `Security/` and validate access permissions against the security matrix.

9. **Data Load and Validation** -- Execute an initial data load using test data from `Testing/TestData/` and run the validation scripts in `Testing/ValidationScripts/`.

### Key Contacts

| Role                     | Responsibility                              |
|--------------------------|---------------------------------------------|
| Solution Architect       | Overall design and technical decisions       |
| Consolidation Lead       | Finance cube and consolidation logic         |
| Planning Lead            | Planning cube and budgeting/forecasting      |
| Integration Lead         | Data management and connector configuration  |
| Dashboard Developer      | Dashboard design and data adapter rules      |
| Security Administrator   | Access control and role management           |
| Project Manager          | Timeline, resources, and stakeholder management |

---

## License

This implementation is proprietary and confidential. All artifacts are the property of the implementing organization and are subject to the terms of the OneStream XF software license agreement.
