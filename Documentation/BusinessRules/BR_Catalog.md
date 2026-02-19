# Business Rules Catalog

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Overview](#1-overview)
2. [Finance Rules](#2-finance-rules)
3. [Calculate Rules](#3-calculate-rules)
4. [Connector Rules](#4-connector-rules)
5. [Dashboard DataAdapter Rules](#5-dashboard-dataadapter-rules)
6. [Dashboard String Functions](#6-dashboard-string-functions)
7. [Member Filter Rules](#7-member-filter-rules)
8. [Event Handler Rules](#8-event-handler-rules)
9. [Extender Rules](#9-extender-rules)
10. [Validation Rules](#10-validation-rules)

---

## 1. Overview

This catalog documents all 79 business rules in the OneStream XF implementation. Each rule is classified by type, category, and includes its trigger mechanism and dependencies.

### 1.1 Summary by Type

| Rule Type | Count | Prefix | Purpose |
|-----------|-------|--------|---------|
| Finance Rules | 8 | FR_ | Consolidation processing (translation, elimination, ownership) |
| Calculate Rules | 20 | CR_ | Data calculations, allocations, data movement |
| Connector Rules | 10 | CN_ | Source system data extraction |
| Dashboard DataAdapters | 15 | DDA_ | Dashboard data retrieval and formatting |
| Dashboard String Functions | 4 | DSF_ | Dynamic text and icon generation for dashboards |
| Member Filters | 5 | MF_ | Dynamic member list filtering based on security/context |
| Event Handlers | 6 | EH_ | Event-driven processing (data quality, notifications) |
| Extender Rules | 6 | EX_ | Batch processing, report distribution, utilities |
| Validation Rules | 5 | VAL_ | Data quality and integrity checks |
| **Total** | **79** | | |

---

## 2. Finance Rules

Finance Rules execute during the consolidation process and handle currency translation, intercompany elimination, ownership-based consolidation, and related adjustments.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 1 | FR_Consolidation | Consolidation | Performs ownership-based consolidation across the entity hierarchy. Supports full, proportional, and equity methods based on the ownership table. Processes bottom-up from base entities to Total_Company. | Consolidation process (On Consolidate event) | Ownership table; FR_CurrencyTranslation must complete first |
| 2 | FR_CurrencyTranslation | Translation | Translates entity financial data from local currency to reporting currencies (USD, EUR, GBP). Applies weighted average rates for income statement accounts, period-end rates for monetary BS accounts, and historical rates for non-monetary items. Calculates CTA to BS_AOCI. | Consolidation process (On Translate event) | Exchange rate tables loaded for current period; account type properties configured |
| 3 | FR_ICElimination | Elimination | Matches intercompany balances across trading partners. Auto-eliminates matched amounts within tolerance ($1,000). Routes unmatched balances to exception report. Generates elimination journal entries on ELIM_* entities. | Consolidation process (On Eliminate event) | IC account tags configured; IC partner dimension mapped; FR_CurrencyTranslation complete |
| 4 | FR_JournalEntries | Journals | Processes top-side adjustment journal entries entered by users. Validates debit/credit balance, entity/period validity, and account permissions. Applies approved journals to consolidation dimension (CON_Adjusted). | Consolidation process (On Journals event) | Journal entry forms configured; approval workflow active |
| 5 | FR_OwnershipCalc | Ownership | Calculates minority interest (NCI) for partially owned subsidiaries. Applies ownership percentages from the ownership table to compute parent share and NCI share of net income and equity. | Consolidation process (after FR_Consolidation) | Ownership table with NCI percentages; FR_Consolidation complete |
| 6 | FR_EquityPickup | Equity Method | Processes equity method investees. Calculates proportional share of net income and records equity pickup entry. Updates investment balance on parent entity. | Consolidation process (for equity method entities) | Equity method entities identified in ownership table; investee data loaded |
| 7 | FR_ProportionalConsolidation | Proportional | Applies proportional consolidation for joint ventures. Includes only the owned percentage of each line item in the consolidated totals. | Consolidation process (for proportional entities) | Ownership percentages defined; entity consolidation method set to Proportional |
| 8 | FR_FlowAnalysis | Roll-Forward | Generates roll-forward analysis for balance sheet accounts. Calculates opening balance from prior period closing, decomposes movements into Flow dimension members (acquisitions, disposals, depreciation, FX, etc.). | Post-consolidation calculation | Prior period data available; Flow dimension configured; BS account properties set |

---

## 3. Calculate Rules

Calculate Rules handle data calculations, allocations, driver-based computations, and data movement between cubes.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 9 | CR_COGSAllocation | Allocation | Allocates cost of goods sold from total plant level to product dimension based on production volume drivers from MES data. Supports standard cost and actual cost methods. | On Calculate (Finance cube) | MES production data loaded; product volume data available |
| 10 | CR_OverheadAllocation | Allocation | Distributes manufacturing overhead costs to products/departments based on configurable allocation drivers (direct labor hours, machine hours, square footage). | On Calculate (Finance cube) | Direct cost data loaded; allocation drivers populated |
| 11 | CR_DepreciationCalc | Calculation | Calculates monthly depreciation charges based on fixed asset register data. Supports straight-line, declining balance, and units of production methods. Updates accumulated depreciation and net book value. | On Calculate (Finance and Planning cubes) | Fixed asset data loaded from SAP/Oracle; asset class depreciation methods configured |
| 12 | CR_CashFlowCalc | Calculation | Generates indirect cash flow statement from balance sheet movements and income statement data. Applies standard adjustments for non-cash items, working capital changes, and investing/financing activities. | Post-consolidation (Finance cube) | Consolidated BS and PL data available; prior period data for movement calculation |
| 13 | CR_AccountDerivations | Derivation | Calculates derived accounts: gross margin (REV - COGS), operating income (GM - OPEX), EBITDA, EBT, and net income. Ensures calculated accounts stay in sync with input accounts. | On Calculate (Finance and Planning cubes) | Input account data loaded (revenue, COGS, OPEX, other income, tax) |
| 14 | CR_KPICalculations | Calculation | Computes key performance indicators: gross margin %, operating margin %, return on assets, inventory turns, DSO, DPO, revenue per FTE, cost per unit, yield %. | Post-calculation (Finance cube) | Financial data and statistical data loaded; headcount data available |
| 15 | CR_GrowthDrivers | Driver | Applies corporate growth rate targets to prior year actuals for budget seeding. Supports different growth rates by entity, product, and account group. | Planning workflow (manual trigger) | Prior year actuals available in Planning cube; growth rate table populated |
| 16 | CR_BudgetSeeding | Data Movement | Copies prior year actual data from Finance cube to Planning cube as budget starting point. Adjusts for one-time items and applies growth assumptions. | Planning workflow (manual trigger) | Prior year actuals finalized in Finance cube; Planning cube cleared for new budget |
| 17 | CR_RevenueDriverCalc | Driver | Calculates revenue from driver inputs: volume x price x mix. Supports top-down (target allocation) and bottom-up (product-level build) approaches. | On Calculate (Planning cube) | Volume and price driver inputs entered; product/customer assignments configured |
| 18 | CR_CompCalc | Calculation | Calculates total compensation cost from headcount and salary inputs. Applies merit increase assumptions, bonus accrual rates, and payroll tax rates by jurisdiction. | On Calculate (HR cube) | Headcount data entered/loaded; compensation rate tables populated |
| 19 | CR_BenefitsCalc | Calculation | Calculates employee benefit costs from headcount and benefit plan enrollment. Applies per-employee rates for medical, dental, vision, life, disability, and retirement plans. | On Calculate (HR cube) | Headcount data available; benefit rate tables populated by plan and entity |
| 20 | CR_PayrollTaxCalc | Calculation | Calculates employer payroll tax burden by jurisdiction (federal, state, international). Applies wage bases and rate tables per entity location. | On Calculate (HR cube) | Compensation data calculated (CR_CompCalc); tax rate tables by jurisdiction |
| 21 | CR_TotalLaborCost | Calculation | Aggregates total labor cost: base compensation + bonus + benefits + payroll tax. Writes summary to HR_TotalCost account for push to Planning cube. | On Calculate (HR cube) | CR_CompCalc, CR_BenefitsCalc, CR_PayrollTaxCalc complete |
| 22 | CR_LaborCostPush | Data Movement | Pushes aggregated labor cost data from HR cube to Planning cube OPEX accounts by entity and department. Maps HR accounts to financial OPEX accounts. | Monthly (automated) or on-demand | CR_TotalLaborCost complete; account mapping table configured |
| 23 | CR_PlanDataPush | Data Movement | Pushes approved plan data (budget or forecast) from Planning cube to Finance cube for consolidated variance reporting. Handles dimension mapping for non-shared dimensions. | Budget approval workflow; monthly forecast snapshot | Plan data approved in Planning cube; dimension mapping table configured |
| 24 | CR_ActualsToRF | Data Movement | Copies actual data from Finance cube to Planning cube Rolling Forecast scenario. Replaces forecast months with actuals as each month closes. | Monthly (after close) | Current month actuals finalized in Finance cube; RF scenario open |
| 25 | CR_ReconDataPush | Data Movement | Pushes balance sheet balances from Finance cube to Recon cube for reconciliation. Includes GL balance and calculates subledger expected balance. | Monthly (after data load) | Finance cube BS data loaded for current period |
| 26 | CR_ICMatchingCalc | Calculation | Performs intercompany balance matching at the account level. Identifies matched pairs, calculates net IC position, and flags exceptions for review. | On-demand (before consolidation) | IC-tagged transactions loaded with IC partner dimension populated |
| 27 | CR_VarianceAnalysis | Calculation | Calculates budget-to-actual and prior-year variance (amount and percentage) for all accounts. Stores results in derived variance accounts for reporting. | Post-load (Finance cube) | Actual data loaded; Budget/PriorYear data available for comparison period |
| 28 | CR_StatisticalCalc | Calculation | Calculates statistical metrics: production cost per unit, revenue per unit, capacity utilization %, yield %, defect rate. | Post-load (Finance cube) | Financial data and MES statistical data loaded for current period |

---

## 4. Connector Rules

Connector Rules extract data from source systems and load it into OneStream staging tables.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 29 | CN_SAP_GLActuals | Extract | Extracts general ledger actual balances from SAP HANA. Queries `ACDOCA` (Universal Journal) for posting-date-based delta extraction. Handles multi-company-code extraction in parallel. | DM Sequence (daily at 02:30 AM) | CONN_SAP_HANA connection active; SAP period open for extraction |
| 30 | CN_SAP_APData | Extract | Extracts accounts payable data from SAP including open items, aging, and vendor balances. Sources from `BSIK`/`BSAK` tables. | DM Sequence (daily at 02:30 AM) | CONN_SAP_HANA connection active |
| 31 | CN_SAP_ARData | Extract | Extracts accounts receivable data from SAP including open items, aging, and customer balances. Sources from `BSID`/`BSAD` tables. | DM Sequence (daily at 02:30 AM) | CONN_SAP_HANA connection active |
| 32 | CN_SAP_FixedAssets | Extract | Extracts fixed asset register from SAP including asset master data, acquisition values, and accumulated depreciation. Sources from `ANLA`/`ANLC` tables. | DM Sequence (monthly full refresh) | CONN_SAP_HANA connection active |
| 33 | CN_Oracle_GLActuals | Extract | Extracts general ledger balances from Oracle EBS. Queries `GL_BALANCES` view with period-based filtering. Handles multi-ledger extraction for EU entities. | DM Sequence (daily at 03:00 AM) | CONN_Oracle_EBS connection active; Oracle period open |
| 34 | CN_Oracle_ARData | Extract | Extracts accounts receivable data from Oracle EBS including customer balances and aging. Sources from `AR_PAYMENT_SCHEDULES_ALL`. | DM Sequence (daily at 03:00 AM) | CONN_Oracle_EBS connection active |
| 35 | CN_Oracle_FixedAssets | Extract | Extracts fixed asset data from Oracle EBS including additions, retirements, and depreciation. Sources from `FA_BOOKS` and `FA_DEPRN_SUMMARY`. | DM Sequence (monthly full refresh) | CONN_Oracle_EBS connection active |
| 36 | CN_Oracle_Inventory | Extract | Extracts inventory balances and valuation from Oracle EBS. Sources from `MTL_ONHAND_QUANTITIES` and `CST_ITEM_COSTS`. | DM Sequence (daily at 03:00 AM) | CONN_Oracle_EBS connection active |
| 37 | CN_NetSuite_GLActuals | Extract | Extracts general ledger transaction data from NetSuite via REST API. Uses SuiteQL queries for efficient data retrieval with pagination. Handles API rate limiting. | DM Sequence (daily at 03:30 AM) | CONN_NetSuite_API connection active; API token valid |
| 38 | CN_Workday_Headcount | Extract | Extracts active worker data from Workday HCM via REST API. Includes worker demographics, position, department, and employment status. Handles paginated responses. | DM Sequence (weekly on Sunday at 10:00 PM) | CONN_Workday_API connection active; OAuth token valid |
| 39 | CN_Workday_Compensation | Extract | Extracts compensation plan data from Workday including base salary, bonus targets, and benefit elections. Processes paginated API responses. | DM Sequence (weekly on Sunday at 10:30 PM) | CONN_Workday_API connection active; OAuth token valid |
| 40 | CN_MES_Production | Extract | Reads production volume and quality data from MES-generated CSV files. Parses file structure, validates headers, and loads to staging table. Archives processed files. | DM Sequence (daily at 02:00 AM) | MES CSV files dropped to SFTP by 01:30 AM; file naming convention followed |
| 41 | CN_MES_Quality | Extract | Reads quality metrics (defect counts, yield percentages, scrap rates) from MES-generated CSV files. Validates data ranges and loads to staging. | DM Sequence (daily at 02:00 AM) | MES CSV files dropped to SFTP; CN_MES_Production processes first |
| 42 | CN_Excel_BudgetInput | Extract | Processes Excel budget templates uploaded by plant controllers. Validates template version, header structure, and data types. Routes invalid files to error folder with notification. | On-demand (user upload during budget cycle) | Excel template version matches expected format; user has upload permission |
| 43 | CN_FlatFile_StatData | Extract | Reads statistical data from flat CSV files (external benchmarks, market data, non-system metrics). Validates structure and loads to staging. | DM Sequence (weekly on Sunday at 11:00 PM) | CSV files available in designated folder; header format validated |

---

## 5. Dashboard DataAdapter Rules

Dashboard DataAdapter rules retrieve and format data for dashboard components.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 44 | DDA_ExecutiveSummary | Executive | Retrieves consolidated KPIs (revenue, EBITDA, net income, margins) for the executive summary dashboard. Includes current period, YTD, budget variance, and prior year variance. | Dashboard load (Executive Summary page) | Current period data loaded and consolidated; KPI calculations complete |
| 45 | DDA_PLAnalysis | Financial | Retrieves income statement data with multi-level drill capability. Supports actuals, budget, forecast, and variance views. Formats with conditional color coding for variances. | Dashboard load (P&L Analysis page) | Financial data loaded; variance calculations complete |
| 46 | DDA_BSAnalysis | Financial | Retrieves balance sheet data with roll-forward view. Supports period-end balances and movement analysis through the Flow dimension. | Dashboard load (Balance Sheet page) | BS data loaded; flow analysis calculations complete |
| 47 | DDA_CashFlowAnalysis | Financial | Retrieves cash flow statement data (operating, investing, financing) with waterfall chart formatting. Supports drill-through to underlying BS movements. | Dashboard load (Cash Flow page) | Cash flow calculations complete (CR_CashFlowCalc) |
| 48 | DDA_PlantOperations | Operational | Retrieves plant-level operational metrics: production volume, yield, capacity utilization, cost per unit. Supports plant comparison view. | Dashboard load (Plant Operations page) | MES data loaded; statistical calculations complete |
| 49 | DDA_RevenueByProduct | Revenue | Retrieves revenue data broken down by product line. Includes volume, price, and mix variance analysis. Supports drill-from-total-to-product-detail. | Dashboard load (Revenue Analysis page) | Revenue data loaded; COGS allocated to products |
| 50 | DDA_RevenueByCustomer | Revenue | Retrieves revenue data by customer segment and top key accounts. Includes customer concentration analysis and trend data. | Dashboard load (Customer Analysis page) | Revenue data loaded with customer dimension populated |
| 51 | DDA_OPEXByDepartment | Expense | Retrieves operating expense data by department with budget comparison. Includes headcount driver metrics for cost-per-FTE analysis. | Dashboard load (OPEX Analysis page) | OPEX data loaded; budget data available; headcount data available |
| 52 | DDA_CAPEXTracking | Capital | Retrieves CAPEX project spending vs. budget. Includes project-level detail, cumulative spending curves, and remaining budget analysis. | Dashboard load (CAPEX Tracking page) | CAPEX actuals loaded; project budget data available |
| 53 | DDA_ICDashboard | Intercompany | Retrieves intercompany balance summary showing matched/unmatched positions by entity pair. Highlights out-of-balance situations. | Dashboard load (IC Reconciliation page) | IC matching calculation complete (CR_ICMatchingCalc) |
| 54 | DDA_BudgetVsActual | Variance | Retrieves budget-to-actual variance data at configurable granularity (entity, department, account group). Supports drill-down from summary to detail. | Dashboard load (Budget vs. Actual page) | Actual and budget data available; variance calculations complete |
| 55 | DDA_ForecastTrend | Planning | Retrieves rolling forecast trend data showing how forecast has evolved over time. Displays actual vs. successive forecast versions for accuracy analysis. | Dashboard load (Forecast Accuracy page) | Multiple forecast snapshots available; actuals loaded |
| 56 | DDA_CloseStatus | Workflow | Retrieves close process status for all entities. Shows task completion, pending items, approvals, and timeline adherence. Supports drill-to-task-detail. | Dashboard load (Close Status page) | Task Manager configured; close tasks assigned |
| 57 | DDA_WorkforceAnalytics | HR | Retrieves workforce metrics: headcount trend, turnover rate, vacancy rate, cost per FTE, department distribution. | Dashboard load (Workforce page) | HR cube data loaded; HR metrics calculated |
| 58 | DDA_AcctReconStatus | Reconciliation | Retrieves account reconciliation status: certified/pending/overdue counts, variance summary, aging of open items. Supports drill-to-account-detail. | Dashboard load (Reconciliation Status page) | Recon cube data loaded; reconciliation workflow active |

---

## 6. Dashboard String Functions

Dashboard String Functions generate dynamic text, icons, and labels for dashboard components.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 59 | DSF_EntityStatusIcon | Display | Returns a traffic-light icon (green/amber/red) based on entity close status. Green = all tasks complete, Amber = in progress on schedule, Red = behind schedule or failed validation. | Dashboard component render | Task Manager status data |
| 60 | DSF_VarianceFormatting | Display | Returns formatted variance text with directional indicators and color coding. Favorable variances show green with up-arrow; unfavorable show red with down-arrow. Handles sign conventions for revenue vs. expense. | Dashboard component render | Variance data available from DDA rules |
| 61 | DSF_DynamicPOVLabels | Display | Generates dynamic labels for dashboard POV (Point of View) selectors reflecting current selections. Formats entity names, period descriptions, and scenario labels for display. | Dashboard POV change event | Dimension member metadata |
| 62 | DSF_DynamicLabels | Display | Generates context-sensitive labels for dashboard sections. Adjusts titles, column headers, and descriptions based on selected entity, scenario, and time period. Supports multi-language (EN, DE, FR, ES, PT, CN, JP). | Dashboard component render | Localization string tables; current POV context |

---

## 7. Member Filter Rules

Member Filter Rules dynamically generate dimension member lists based on user security, context, and business rules.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 63 | MF_EntitySecurity | Security | Returns the list of entities the current user is authorized to view or edit. Supports hierarchical security (access to parent grants access to children). Handles multiple security group assignments. | CubeView/Dashboard member list population | Security group assignments in user profile |
| 64 | MF_ScenarioAccess | Security | Returns available scenarios based on user role and current workflow state. Budget scenarios are only visible during budget cycle; What-If scenarios are user-specific. Locked scenarios return as read-only. | CubeView/Dashboard member list population | Scenario workflow state; user role assignment |
| 65 | MF_ActiveEntities | Filter | Returns entities that have data in the selected scenario and period. Filters out entities with no activity to simplify navigation and reduce empty rows in reports. | CubeView/Dashboard member list population | Data presence check against selected POV |
| 66 | MF_ReconAccounts | Filter | Returns balance sheet accounts that require reconciliation for the selected entity. Account list varies by entity based on account activity and materiality thresholds. | Reconciliation dashboard/CubeView | Account reconciliation configuration; entity-account mapping |
| 67 | MF_CostCenterAccess | Security | Returns cost centers (departments) the current user can view or edit for OPEX budget entry. Budget owners see only their assigned cost centers; managers see their direct reports' cost centers. | CubeView member list (Planning cube) | Cost center ownership assignments; org hierarchy |

---

## 8. Event Handler Rules

Event Handler Rules respond to system events such as data changes, workflow transitions, and scheduled triggers.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 68 | EH_DataQualityValidation | Quality | Executes data quality checks after each data load. Validates trial balance, checks for anomalies (threshold breaches), and updates the data quality scorecard. Flags issues for review. | After data load event (OnAfterDataLoad) | Validation rules configured; threshold values set |
| 69 | EH_WorkflowTransition | Workflow | Handles workflow state transitions for close process. Updates entity status when tasks are completed, routes approvals, and enforces prerequisite task completion before advancing. | Task completion event | Task Manager configuration; task dependency chain defined |
| 70 | EH_DataLockManager | Security | Manages data lock/unlock based on workflow state. Automatically locks entity/period data after close approval. Prevents edits to locked periods. Supports emergency unlock by admin. | Workflow state change; period close/open events | Workflow status; user admin permissions for unlock |
| 71 | EH_AuditTrailLogger | Audit | Captures detailed audit trail for all data modifications. Records user, timestamp, old value, new value, cell address, and modification source (manual entry, calculation, data load). | Any data write event (OnDataChanged) | Audit log table provisioned; logging enabled |
| 72 | EH_ICAlertNotification | Notification | Sends email alerts when intercompany out-of-balance amounts exceed threshold. Notifies both sides of the IC pair with balance details and reconciliation deadline. | After IC matching calculation | IC matching complete; email configuration; user email addresses |
| 73 | EH_NotificationDispatcher | Notification | Central notification engine that dispatches email, dashboard, and in-app notifications based on configurable triggers. Supports templates for different notification types (load complete, approval needed, deadline reminder). | Various events (configurable) | Email server configured; notification templates defined; recipient lists maintained |

---

## 9. Extender Rules

Extender Rules perform batch operations, scheduled tasks, and utility functions outside the normal data flow.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 74 | EX_BatchConsolidation | Batch | Executes consolidation for multiple entities/periods in a single batch operation. Supports parallel processing by region. Used for full reconsolidation during month-end close. | Scheduled (month-end) or manual trigger | All entity data loaded and validated; prior period consolidated |
| 75 | EX_DataArchive | Maintenance | Archives historical data older than the configured retention period. Moves data to archive tables, updates partition pointers, and compresses historical cube partitions. | Scheduled (quarterly) | Archive table structure created; retention period configured (default 5 years active) |
| 76 | EX_DimensionMaintenance | Maintenance | Performs batch dimension member operations: add new members, update properties, deactivate members. Reads change requests from a staging table and applies them with validation. | On-demand or scheduled (monthly) | Dimension change requests staged; validation rules defined |
| 77 | EX_ExchangeRateLoad | Data Load | Loads exchange rates from corporate treasury flat file or API. Populates rate tables for weighted average, period-end, and historical rates across all currency pairs. | Scheduled (daily for spot rates; monthly for average rates) | Rate source file/API available; rate table structure configured |
| 78 | EX_ReportDistribution | Distribution | Generates and distributes automated report packages. Creates PDF/Excel exports from configured report definitions and distributes via email to designated recipient lists based on entity assignment. | Scheduled (monthly after close; ad-hoc for board packages) | Report definitions configured; consolidation complete; recipient lists maintained |
| 79 | EX_EnvironmentSync | Deployment | Synchronizes configuration between environments (DEV to QA, QA to PROD). Exports dimension metadata, business rule source code, dashboard definitions, and mapping tables as a deployment package. | Manual trigger (during deployment) | Source environment stable; target environment accessible |

---

## 10. Validation Rules

Validation Rules check data integrity and business rule compliance.

| # | Rule Name | Category | Description | Triggered By | Dependencies |
|---|-----------|----------|-------------|-------------|-------------|
| 80 | VAL_TrialBalanceCheck | Integrity | Validates that total debits equal total credits for each entity and period. Checks at both local currency and translated currency levels. Reports any imbalances with account-level detail. | After data load; before consolidation | GL data loaded; account types (debit/credit normal balance) configured |
| 81 | VAL_ICBalanceMatch | Integrity | Validates that intercompany receivable/payable balances match between trading partners. Reports mismatches with entity pair, account, and variance amount. Checks both BS and PL IC accounts. | Before consolidation (after IC matching) | IC transactions loaded with partner dimension; CR_ICMatchingCalc complete |
| 82 | VAL_BudgetBalanceCheck | Planning | Validates budget submissions for completeness and reasonableness. Checks that all required accounts have data, revenue and OPEX are within acceptable ranges vs. prior year, and headcount ties to HR plan. | Budget submission event | Budget data entered in Planning cube; HR data available for headcount validation |
| 83 | VAL_PeriodRollForward | Integrity | Validates that the opening balance for the current period equals the closing balance of the prior period for all balance sheet accounts. Reports any breaks in the roll-forward with account-level detail. | After data load; before consolidation | Current and prior period data loaded; flow analysis configured |
| 84 | VAL_CrossCubeReconciliation | Integrity | Reconciles data between cubes after cross-cube data movement. Verifies that totals in the source cube match totals in the target cube for each movement (HR->Planning, Planning->Finance, Finance->Recon). | After data movement rules execute | Data movement rules (CR_LaborCostPush, CR_PlanDataPush, CR_ReconDataPush) complete |

---

## Appendix: Rule Dependency Map

```
Data Load Flow:
CN_* (Connectors) --> EH_DataQualityValidation --> VAL_TrialBalanceCheck
                                                --> VAL_PeriodRollForward

Calculation Flow:
CR_AccountDerivations --> CR_COGSAllocation --> CR_OverheadAllocation
                      --> CR_KPICalculations --> CR_StatisticalCalc
                      --> CR_VarianceAnalysis

Consolidation Flow:
VAL_TrialBalanceCheck --> CR_ICMatchingCalc --> VAL_ICBalanceMatch
                      --> FR_CurrencyTranslation --> FR_ICElimination
                      --> FR_Consolidation --> FR_OwnershipCalc
                      --> FR_JournalEntries --> FR_FlowAnalysis
                      --> CR_CashFlowCalc

Planning Flow:
CR_BudgetSeeding --> CR_GrowthDrivers --> CR_RevenueDriverCalc
                --> (User Input) --> VAL_BudgetBalanceCheck
                --> CR_PlanDataPush --> VAL_CrossCubeReconciliation

HR Flow:
CN_Workday_* --> CR_CompCalc --> CR_BenefitsCalc --> CR_PayrollTaxCalc
             --> CR_TotalLaborCost --> CR_LaborCostPush
             --> VAL_CrossCubeReconciliation

Recon Flow:
CR_ReconDataPush --> VAL_CrossCubeReconciliation
                 --> DDA_AcctReconStatus
```

---

*End of Document*
