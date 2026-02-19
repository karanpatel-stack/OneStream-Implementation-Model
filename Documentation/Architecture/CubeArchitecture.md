# Cube Architecture Design Document

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Multi-Cube Strategy](#1-multi-cube-strategy)
2. [Finance Cube](#2-finance-cube)
3. [Planning Cube](#3-planning-cube)
4. [HR Cube](#4-hr-cube)
5. [Recon Cube](#5-recon-cube)
6. [Dimension Assignment Matrix](#6-dimension-assignment-matrix)
7. [Data Movement Between Cubes](#7-data-movement-between-cubes)
8. [Performance Optimization](#8-performance-optimization)
9. [Storage Estimates](#9-storage-estimates)

---

## 1. Multi-Cube Strategy

### 1.1 Rationale for Four Cubes

A four-cube architecture was selected after evaluating single-cube, two-cube, and four-cube designs against the following criteria:

| Criterion | Single Cube | Two Cubes | Four Cubes (Selected) |
|-----------|-------------|-----------|----------------------|
| Dimensional Relevance | Poor -- many N/A intersections | Moderate | Excellent -- each cube uses only relevant dimensions |
| Storage Efficiency | Poor -- sparse waste | Moderate | Excellent -- minimal sparse waste |
| Calculation Performance | Slow -- large calc scope | Moderate | Fast -- targeted calc scope |
| Security Granularity | Limited | Moderate | Excellent -- cube-level security |
| Maintenance Complexity | Simple | Moderate | Moderate -- well-documented |
| Cross-Module Reporting | Seamless | Some movement needed | Data movement required |
| Consolidation Support | N/A | Supported | Dedicated Finance cube |

**Decision:** The four-cube design was selected because it optimizes storage efficiency and calculation performance while maintaining clear separation of concerns. The added complexity of data movement between cubes is managed through standardized business rules and is offset by significant performance and security advantages.

### 1.2 Cube Summary

| Cube | Purpose | Dimensions | Consolidation Enabled | Primary Users |
|------|---------|------------|----------------------|---------------|
| Finance | Financial consolidation, actuals reporting, management reporting | 12 | Yes | Finance, Accounting, Executives |
| Planning | Budgeting, forecasting, what-if analysis | 10 | No (push to Finance) | FP&A, Plant Controllers, Budget Owners |
| HR | Headcount and compensation planning | 6 | No (push to Finance) | HR, Compensation Analysts, FP&A |
| Recon | Balance sheet reconciliation and certification | 4 | No | Accounting, Internal Audit |

---

## 2. Finance Cube

### 2.1 Purpose and Scope

The Finance cube is the primary consolidation and reporting cube. It receives actual data from all source systems, performs multi-currency translation, intercompany elimination, and ownership-based consolidation. It also receives summarized plan data from the Planning and HR cubes for variance analysis.

### 2.2 Dimension Configuration

| Dimension | Type | Dense/Sparse | Rationale |
|-----------|------|-------------|-----------|
| Account | Standard | **Dense** | Most frequently queried; all accounts have data at most intersections |
| Time | Standard | **Dense** | All periods populated; queried with every retrieval |
| Entity | Standard | Sparse | Not all entities have data at all intersections |
| Scenario | Standard | Sparse | Few scenarios active at any time |
| Flow | Standard | Sparse | Most data at F_DataInput; other flows are sparse |
| Consolidation | Standard | Sparse | Processing stages -- only populated during close |
| UD1 - Product | User Defined | Sparse | Product detail only on revenue/COGS accounts |
| UD2 - Customer | User Defined | Sparse | Customer detail only on revenue accounts |
| UD3 - Department | User Defined | Sparse | Department detail only on OPEX accounts |
| UD4 - Project | User Defined | Sparse | Project detail only on CAPEX accounts |
| UD5 - Intercompany | User Defined | Sparse | IC detail only on IC-tagged accounts |
| UD6 - Plant | User Defined | Sparse | Plant detail only on manufacturing accounts |

**Note:** UD7 (Currency Reporting) and UD8 (Data Source) are handled through member properties and metadata rather than as cube dimensions, reducing the Finance cube to 12 active dimensions.

### 2.3 Consolidation Configuration

| Setting | Value | Description |
|---------|-------|-------------|
| Consolidation Enabled | Yes | Full consolidation processing |
| Ownership Table | MFG_Ownership_2026 | Entity ownership percentages |
| Default Consolidation Method | Full | 100% subsidiary consolidation |
| IC Matching Enabled | Yes | Automated IC balance matching |
| IC Matching Tolerance | $1,000 | Threshold for auto-elimination |
| Currency Translation | Multi-Rate | Rate varies by account type |
| CTA Account | BS_AOCI | Cumulative Translation Adjustment |
| Elimination Entities | ELIM_* | One per consolidation level |

### 2.4 Processing Rules

| Processing Step | Business Rule | Trigger | Execution Order |
|----------------|---------------|---------|-----------------|
| Pre-Calculation | CR_AccountDerivations | On Calculate | 1 |
| COGS Allocation | CR_COGSAllocation | On Calculate | 2 |
| Overhead Allocation | CR_OverheadAllocation | On Calculate | 3 |
| IC Matching | FR_ICElimination | On Consolidation | 4 |
| Currency Translation | FR_CurrencyTranslation | On Consolidation | 5 |
| Ownership Consolidation | FR_Consolidation | On Consolidation | 6 |
| Journal Entries | FR_JournalEntries | On Consolidation | 7 |
| Cash Flow Generation | CR_CashFlowCalc | Post-Consolidation | 8 |
| KPI Calculation | CR_KPICalculations | Post-Consolidation | 9 |

---

## 3. Planning Cube

### 3.1 Purpose and Scope

The Planning cube supports annual budgeting and monthly rolling forecasts. It enables driver-based planning for revenue, COGS, OPEX, and CAPEX. Data entry occurs directly in this cube through CubeView input forms. Approved plan data is pushed to the Finance cube for consolidated variance analysis.

### 3.2 Dimension Configuration

| Dimension | Type | Dense/Sparse | Rationale |
|-----------|------|-------------|-----------|
| Account | Standard | **Dense** | Shared account structure with Finance cube |
| Time | Standard | **Dense** | Full planning horizon (current + 2 future years) |
| Entity | Standard | Sparse | Plant-level input; no consolidation hierarchy |
| Scenario | Standard | Sparse | Budget and Forecast scenarios only |
| UD1 - Product | User Defined | Sparse | Revenue planning by product |
| UD2 - Customer | User Defined | Sparse | Revenue planning by customer segment |
| UD3 - Department | User Defined | Sparse | OPEX planning by department |
| UD4 - Project | User Defined | Sparse | CAPEX planning by project |
| UD6 - Plant | User Defined | Sparse | Manufacturing planning by plant line |
| UD8 - Data Source | User Defined | Sparse | Tracks input vs. calculated data |

**Excluded Dimensions (vs. Finance Cube):**
- Flow: Not needed -- planning uses simple input, no roll-forward
- Consolidation: Not needed -- plan data consolidated after push to Finance
- UD5 - Intercompany: Not needed -- IC transactions not planned at this level
- UD7 - Currency Reporting: Not needed -- plan entered in local currency, translated in Finance

### 3.3 Planning Workflow

| Step | Description | Rule | User Role |
|------|-------------|------|-----------|
| 1 | Seed budget from prior year actuals | CR_BudgetSeeding | FP&A Admin |
| 2 | Apply corporate growth targets | CR_GrowthDrivers | FP&A Manager |
| 3 | Revenue input by product/customer | (Data entry) | Plant Controller |
| 4 | COGS calculation from production plan | CR_COGSAllocation | Automated |
| 5 | OPEX input by department | (Data entry) | Department Manager |
| 6 | CAPEX project input | (Data entry) | Plant Controller |
| 7 | Headcount cost pull from HR Cube | (Data movement) | Automated |
| 8 | Depreciation calculation | CR_DepreciationCalc | Automated |
| 9 | Validation and submission | VAL_BudgetBalanceCheck | Plant Controller |
| 10 | Manager review and approval | (Workflow) | Finance Manager |
| 11 | Push approved data to Finance Cube | CR_PlanDataPush | FP&A Admin |

### 3.4 Version Control

| Version | Purpose | Editable | Retention |
|---------|---------|----------|-----------|
| Budget_Working | Active input version | Yes -- during budget window | Overwritten each cycle |
| Budget_V1 | First submission snapshot | No -- locked after snapshot | 2 years |
| Budget_V2 | Revised submission | No -- locked after snapshot | 2 years |
| Budget_Approved | Final approved budget | No -- permanently locked | Permanent |
| FC_Working | Active forecast | Yes -- until monthly snapshot | Overwritten monthly |
| FC_Q1-Q4 | Quarterly snapshots | No -- locked after snapshot | Current year + 1 |
| RF_Current | Rolling 18-month forecast | Yes -- always open | Overwritten monthly |

---

## 4. HR Cube

### 4.1 Purpose and Scope

The HR cube supports headcount planning, compensation modeling, and workforce analytics. It is deliberately kept small (6 dimensions) to optimize performance for the high volume of individual position-level calculations. Aggregated compensation costs are pushed to the Planning cube and ultimately to the Finance cube.

### 4.2 Dimension Configuration

| Dimension | Type | Dense/Sparse | Rationale |
|-----------|------|-------------|-----------|
| Account | Standard | **Dense** | HR-specific accounts: HC, FTE, Salary, Benefits, Tax |
| Time | Standard | **Dense** | Monthly planning horizon |
| Entity | Standard | Sparse | Entity assignment for each position |
| Scenario | Standard | Sparse | Budget and Forecast only |
| UD3 - Department | User Defined | Sparse | Department assignment |
| UD8 - Data Source | User Defined | Sparse | Manual vs. Workday-loaded |

### 4.3 HR Account Structure (Subset of Main Account Dimension)

```
HR_Accounts
|
+-- HR_Headcount
|   +-- HR_HC_Active
|   +-- HR_HC_NewHires
|   +-- HR_HC_Terminations
|   +-- HR_HC_Transfers
|
+-- HR_FTE
|   +-- HR_FTE_FullTime
|   +-- HR_FTE_PartTime
|   +-- HR_FTE_Contractor
|
+-- HR_Compensation
|   +-- HR_COMP_BaseSalary
|   +-- HR_COMP_Bonus
|   +-- HR_COMP_Overtime
|   +-- HR_COMP_Commission
|
+-- HR_Benefits
|   +-- HR_BEN_Medical
|   +-- HR_BEN_Dental
|   +-- HR_BEN_Vision
|   +-- HR_BEN_Life
|   +-- HR_BEN_Disability
|   +-- HR_BEN_Retirement401k
|   +-- HR_BEN_PensionDB
|
+-- HR_PayrollTax
|   +-- HR_TAX_SocialSecurity
|   +-- HR_TAX_Medicare
|   +-- HR_TAX_StateLocal
|   +-- HR_TAX_International
|
+-- HR_TotalCost (Calculated: Comp + Benefits + Tax)
|
+-- HR_Metrics
    +-- HR_MET_TurnoverRate
    +-- HR_MET_VacancyRate
    +-- HR_MET_AvgTenure
    +-- HR_MET_SpanOfControl
    +-- HR_MET_CostPerFTE
```

### 4.4 Compensation Calculation Logic

| Calculation | Formula | Rule |
|-------------|---------|------|
| Total Base Cost | Headcount x Average Salary x Months | CR_CompCalc |
| Bonus Accrual | Base Salary x Bonus % by Level | CR_CompCalc |
| Benefits Cost | Headcount x Benefit Rate by Plan | CR_BenefitsCalc |
| Payroll Tax | (Base + Bonus) x Tax Rate by Jurisdiction | CR_PayrollTaxCalc |
| Total Labor Cost | Base + Bonus + Benefits + Tax | CR_TotalLaborCost |

---

## 5. Recon Cube

### 5.1 Purpose and Scope

The Recon cube supports balance sheet account reconciliation and certification. It stores reconciliation detail (subledger balances, adjustments, reconciling items) that would be too granular for the Finance cube. Only balance sheet accounts that require reconciliation are included.

### 5.2 Dimension Configuration

| Dimension | Type | Dense/Sparse | Rationale |
|-----------|------|-------------|-----------|
| Account | Standard | **Dense** | BS accounts requiring reconciliation (~150) |
| Time | Standard | **Dense** | Monthly reconciliation periods |
| Entity | Standard | Sparse | Entity-level reconciliation |
| UD8 - Data Source | User Defined | Sparse | Tracks GL vs. subledger vs. adjustment |

### 5.3 Reconciliation Account Structure (Subset)

```
RECON_Accounts
|
+-- RECON_GLBalance (GL balance from ERP)
+-- RECON_SubledgerBalance (Subledger total)
+-- RECON_Variance (GL - Subledger)
+-- RECON_ReconcilingItems
|   +-- RECON_TimingDifferences
|   +-- RECON_PendingItems
|   +-- RECON_Adjustments
|   +-- RECON_Reclassifications
+-- RECON_ExplainedVariance
+-- RECON_UnexplainedVariance (Should be zero)
+-- RECON_CertificationStatus (0=Open, 1=Prepared, 2=Reviewed, 3=Certified)
+-- RECON_MaterialityThreshold
```

### 5.4 Reconciliation Workflow

| Step | Actor | Action | Status |
|------|-------|--------|--------|
| 1 | System | Load GL and subledger balances | Open |
| 2 | System | Calculate variance | Open |
| 3 | Preparer | Enter reconciling items | In Progress |
| 4 | Preparer | Certify account | Prepared |
| 5 | Reviewer | Review and approve | Reviewed |
| 6 | Controller | Final certification | Certified |

---

## 6. Dimension Assignment Matrix

The following matrix shows which dimensions are active in each cube:

| Dimension | Finance | Planning | HR | Recon |
|-----------|:-------:|:--------:|:--:|:-----:|
| Account | X | X | X | X |
| Entity | X | X | X | X |
| Scenario | X | X | X | -- |
| Time | X | X | X | X |
| Flow | X | -- | -- | -- |
| Consolidation | X | -- | -- | -- |
| UD1 - Product | X | X | -- | -- |
| UD2 - Customer | X | X | -- | -- |
| UD3 - Department | X | X | X | -- |
| UD4 - Project | X | X | -- | -- |
| UD5 - Intercompany | X | -- | -- | -- |
| UD6 - Plant | X | X | -- | -- |
| UD7 - Currency Rpt | -- | -- | -- | -- |
| UD8 - Data Source | -- | X | X | X |
| **Total Dimensions** | **12** | **10** | **6** | **4** |

**Note:** UD7 (Currency Reporting) is managed through metadata/properties rather than as a cube dimension, and UD8 (Data Source) is used in Planning, HR, and Recon but not in Finance (where data source is tracked via audit trail).

---

## 7. Data Movement Between Cubes

### 7.1 Data Movement Flows

```
+----------+                    +----------+
|  HR Cube |-- Labor Costs ---->| Planning |
|          |   (Monthly)        |   Cube   |
+----------+                    +----+-----+
                                     |
                                     | Approved Plan
                                     | (Budget Cycle)
                                     v
+----------+                    +----------+
|  Recon   |<-- BS Balances --- | Finance  |
|  Cube    |   (Monthly)        |   Cube   |
+----------+                    +----------+
```

### 7.2 Movement Specifications

| Movement | Source Cube | Target Cube | Frequency | Rule | Volume |
|----------|-----------|-------------|-----------|------|--------|
| Labor Costs | HR | Planning | Monthly | CR_LaborCostPush | ~5,000 cells |
| Approved Budget | Planning | Finance | Budget cycle (annual) | CR_PlanDataPush | ~200,000 cells |
| Approved Forecast | Planning | Finance | Monthly | CR_PlanDataPush | ~50,000 cells |
| BS Balances | Finance | Recon | Monthly | CR_ReconDataPush | ~20,000 cells |
| Actuals Summary | Finance | Planning | Monthly (for RF) | CR_ActualsToRF | ~100,000 cells |

### 7.3 Data Movement Rules

Each data movement follows a standard pattern:

1. **Source Query:** Read data from source cube using `GetDataBuffer`
2. **Transformation:** Map dimensions (account mapping, default member assignment for missing dims)
3. **Validation:** Verify totals match between source and target
4. **Load:** Write to target cube using `SetDataBuffer`
5. **Logging:** Record movement timestamp, cell count, and status

### 7.4 Dimension Mapping for Cross-Cube Movement

When moving data between cubes with different dimension counts, unmapped dimensions default to their "None" or "Total" members:

| Source Dimension | HR -> Planning | Planning -> Finance |
|-----------------|---------------|-------------------|
| Account | Direct map | Direct map |
| Entity | Direct map | Direct map |
| Scenario | Direct map | Direct map |
| Time | Direct map | Direct map |
| UD1 - Product | N/A -> PRD_None | Direct map |
| UD2 - Customer | N/A -> CUST_None | Direct map |
| UD3 - Department | Direct map | Direct map |
| UD4 - Project | N/A -> PROJ_None | Direct map |
| UD5 - Intercompany | N/A | N/A -> IC_None |
| UD6 - Plant | N/A -> PLT_None | Direct map |
| Flow | N/A | N/A -> F_DataInput |
| Consolidation | N/A | N/A -> CON_Input |

---

## 8. Performance Optimization

### 8.1 Dense/Sparse Strategy

The dense/sparse configuration is critical for OLAP performance. Dense dimensions form fixed-size data blocks; sparse dimensions determine the number of blocks.

**Finance Cube Block Structure:**
- Dense dimensions: Account (800) x Time (240) = 192,000 cells per block
- Sparse dimensions: Entity x Scenario x Flow x Consolidation x UD1 x UD2 x UD3 x UD4 x UD5 x UD6
- Estimated populated blocks: ~50,000 (of theoretical millions)
- Block size: ~1.5 MB (192,000 x 8 bytes)
- Estimated data footprint: ~75 GB

### 8.2 Aggregation Strategy

| Aggregation Level | Pre-Calculated | On-Demand | Rationale |
|-------------------|:-------------:|:---------:|-----------|
| Total Company | X | | Most common executive query |
| Region (NA, EU, AP, SA) | X | | Standard management reporting |
| Country | X | | Statutory reporting requirement |
| Entity (base) | | X | Large number; loaded directly |
| Total Products | X | | Revenue summary |
| Product Group | | X | Detail level -- query on demand |
| Total Departments | X | | OPEX summary |
| Department detail | | X | Budget owner queries |

### 8.3 Calculation Optimization

| Technique | Application | Expected Impact |
|-----------|-------------|----------------|
| Batch calculation | Process all entities in a single calc pass | 40% faster than entity-by-entity |
| Sparse skip | Skip empty intersections during calculation | 60% reduction in calc time |
| Parallel processing | Consolidation processed in parallel by region | 3x faster consolidation |
| Result caching | Cache KPI and ratio calculations | Dashboard loads < 3 seconds |
| Incremental calc | Only recalculate changed entities | 80% faster for single-entity updates |

### 8.4 Cube Partitioning

The Finance cube is partitioned by fiscal year to optimize performance:

| Partition | Years | Status | Storage Mode |
|-----------|-------|--------|-------------|
| Historical | FY2022-FY2024 | Read-only | Compressed |
| Prior Year | FY2025 | Read-only (after close) | Standard |
| Current Year | FY2026 | Active | Standard |
| Future | FY2027-FY2028 | Planning only | Standard |

**Benefits of Year-Based Partitioning:**
- Historical partitions are compressed, reducing storage by ~60%
- Read-only partitions are not recalculated, reducing consolidation time
- Current year partition is optimized for frequent read/write
- Backup and restore can target specific partitions

---

## 9. Storage Estimates

### 9.1 Per-Cube Storage

| Cube | Dimensions | Estimated Populated Cells | Raw Data Size | With Indexes | Growth/Year |
|------|-----------|--------------------------|--------------|-------------|-------------|
| Finance | 12 | ~500 million | 80 GB | 120 GB | 15% |
| Planning | 10 | ~50 million | 8 GB | 12 GB | 10% |
| HR | 6 | ~5 million | 0.8 GB | 1.2 GB | 5% |
| Recon | 4 | ~2 million | 0.3 GB | 0.5 GB | 5% |
| **Total** | | **~557 million** | **~89 GB** | **~134 GB** | |

### 9.2 Database Storage Requirements

| Component | Size | Notes |
|-----------|------|-------|
| Cube Data | 134 GB | All four cubes |
| Staging Tables | 20 GB | Temporary; purged after load |
| Metadata | 5 GB | Dimension definitions, rules, configs |
| Audit Trail | 30 GB | 2-year rolling window |
| TempDB | 50 GB | Calculation workspace |
| Transaction Log | 40 GB | Recovery log |
| **Total Database** | **~280 GB** | |
| Backup (Full) | 200 GB | Compressed ~70% |
| Backup (Differential) | 30 GB | Daily incremental |

### 9.3 Five-Year Storage Projection

| Year | Finance | Planning | HR | Recon | Total DB | Backup |
|------|---------|----------|----|-------|----------|--------|
| Year 1 (Go-Live) | 120 GB | 12 GB | 1.2 GB | 0.5 GB | 280 GB | 200 GB |
| Year 2 | 138 GB | 13 GB | 1.3 GB | 0.5 GB | 320 GB | 230 GB |
| Year 3 | 159 GB | 14 GB | 1.3 GB | 0.6 GB | 365 GB | 260 GB |
| Year 4 | 183 GB | 16 GB | 1.4 GB | 0.6 GB | 415 GB | 295 GB |
| Year 5 | 210 GB | 17 GB | 1.5 GB | 0.6 GB | 475 GB | 340 GB |

**Recommendation:** Provision 500 GB for production database with annual review. Archive historical partitions older than 5 years to cold storage.

---

*End of Document*
