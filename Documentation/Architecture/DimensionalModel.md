# Dimensional Model Design Document

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Dimensions Overview](#1-dimensions-overview)
2. [Account Dimension](#2-account-dimension)
3. [Entity Dimension](#3-entity-dimension)
4. [Scenario Dimension](#4-scenario-dimension)
5. [Time Dimension](#5-time-dimension)
6. [Flow Dimension](#6-flow-dimension)
7. [Consolidation Dimension](#7-consolidation-dimension)
8. [UD1 -- Product](#8-ud1----product)
9. [UD2 -- Customer](#9-ud2----customer)
10. [UD3 -- Department](#10-ud3----department)
11. [UD4 -- Project](#11-ud4----project)
12. [UD5 -- Intercompany](#12-ud5----intercompany)
13. [UD6 -- Plant](#13-ud6----plant)
14. [UD7 -- Currency Reporting](#14-ud7----currency-reporting)
15. [UD8 -- Data Source](#15-ud8----data-source)
16. [Cross-Dimension Relationships](#16-cross-dimension-relationships)
17. [Naming Conventions](#17-naming-conventions)

---

## 1. Dimensions Overview

The dimensional model comprises 14 dimensions designed to support financial consolidation, planning, reporting, account reconciliation, and people planning across a global manufacturing organization.

| # | Dimension | Type | Member Count | Purpose | Cube Assignment |
|---|-----------|------|-------------|---------|-----------------|
| 1 | Account | Standard | ~800 | Chart of accounts: PL, BS, CF, Statistical | Finance, Planning, HR, Recon |
| 2 | Entity | Standard | ~120 | Legal entities, plants, regions, eliminations | Finance, Planning, HR, Recon |
| 3 | Scenario | Standard | ~15 | Actual, Budget, Forecast, What-If | Finance, Planning |
| 4 | Time | Standard | ~240 | Months, quarters, years (FY aligned) | Finance, Planning, HR, Recon |
| 5 | Flow | Standard | ~25 | Roll-forward movement types | Finance |
| 6 | Consolidation | Standard | ~10 | Consolidation processing stages | Finance |
| 7 | UD1 - Product | User Defined | ~150 | Product lines and SKU groups | Finance, Planning |
| 8 | UD2 - Customer | User Defined | ~100 | Customer segments and key accounts | Finance, Planning |
| 9 | UD3 - Department | User Defined | ~80 | Organizational departments | Finance, Planning, HR |
| 10 | UD4 - Project | User Defined | ~60 | CAPEX projects and initiatives | Finance, Planning |
| 11 | UD5 - Intercompany | User Defined | ~45 | IC trading partner mirror | Finance |
| 12 | UD6 - Plant | User Defined | ~30 | Manufacturing plant detail | Finance, Planning |
| 13 | UD7 - Currency Rpt | User Defined | ~10 | Reporting currency views | Finance |
| 14 | UD8 - Data Source | User Defined | ~20 | Source system identifier | Finance, Planning |

**Total Estimated Intersections (Finance Cube):** ~800 x 120 x 15 x 240 x 25 x 10 x 150 x 100 x 80 x 60 x 45 x 30 x 10 x 20
*Note: Sparse configuration and data sparsity mean actual stored cells are a very small fraction of theoretical maximum.*

---

## 2. Account Dimension

### 2.1 Design Rationale

The Account dimension represents the complete chart of accounts organized into financial statement sections. The structure supports multi-GAAP reporting (US GAAP and IFRS) through a unified account hierarchy with alternative roll-ups. Statistical accounts are included to support operational KPIs and planning drivers without creating separate structures.

### 2.2 Hierarchy Structure

```
Total_Accounts
|
+-- PL_IncomeStatement
|   +-- REV_Revenue (~50 accounts)
|   |   +-- REV_GrossRevenue
|   |   |   +-- REV_Product_Sales
|   |   |   +-- REV_Service_Revenue
|   |   |   +-- REV_License_Revenue
|   |   +-- REV_Deductions
|   |       +-- REV_Discounts
|   |       +-- REV_Returns
|   |       +-- REV_Allowances
|   |
|   +-- COGS_CostOfSales (~80 accounts)
|   |   +-- COGS_DirectMaterial
|   |   +-- COGS_DirectLabor
|   |   +-- COGS_ManufacturingOH
|   |   +-- COGS_Freight
|   |   +-- COGS_Warranty
|   |   +-- COGS_StdCostVariance
|   |
|   +-- GM_GrossMargin (calculated)
|   |
|   +-- OPEX_OperatingExpenses (~200 accounts)
|   |   +-- OPEX_SGA
|   |   |   +-- OPEX_Salaries
|   |   |   +-- OPEX_Benefits
|   |   |   +-- OPEX_Travel
|   |   |   +-- OPEX_Professional
|   |   |   +-- OPEX_Occupancy
|   |   |   +-- OPEX_Technology
|   |   |   +-- OPEX_Marketing
|   |   |   +-- OPEX_Insurance
|   |   |   +-- OPEX_Other
|   |   +-- OPEX_RandD
|   |   +-- OPEX_Depreciation
|   |   +-- OPEX_Amortization
|   |
|   +-- OI_OtherIncome (~30 accounts)
|   |   +-- OI_InterestIncome
|   |   +-- OI_InterestExpense
|   |   +-- OI_FXGainLoss
|   |   +-- OI_GainLossDisposal
|   |
|   +-- TAX_IncomeTax (~20 accounts)
|   |   +-- TAX_CurrentTax
|   |   +-- TAX_DeferredTax
|   |
|   +-- NI_NetIncome (calculated)
|
+-- BS_BalanceSheet
|   +-- BS_Assets (~150 accounts)
|   |   +-- BS_CurrentAssets
|   |   |   +-- BS_Cash
|   |   |   +-- BS_AccountsReceivable
|   |   |   +-- BS_Inventory
|   |   |   |   +-- BS_INV_RawMaterial
|   |   |   |   +-- BS_INV_WIP
|   |   |   |   +-- BS_INV_FinishedGoods
|   |   |   +-- BS_Prepaid
|   |   |   +-- BS_OtherCurrentAssets
|   |   +-- BS_NonCurrentAssets
|   |       +-- BS_PPE_Gross
|   |       +-- BS_AccumDepreciation
|   |       +-- BS_PPE_Net (calculated)
|   |       +-- BS_Intangibles
|   |       +-- BS_Goodwill
|   |       +-- BS_Investments
|   |       +-- BS_OtherNonCurrentAssets
|   |
|   +-- BS_Liabilities (~120 accounts)
|   |   +-- BS_CurrentLiabilities
|   |   |   +-- BS_AccountsPayable
|   |   |   +-- BS_AccruedExpenses
|   |   |   +-- BS_CurrentDebt
|   |   |   +-- BS_DeferredRevenue
|   |   |   +-- BS_OtherCurrentLiab
|   |   +-- BS_NonCurrentLiabilities
|   |       +-- BS_LongTermDebt
|   |       +-- BS_Pension
|   |       +-- BS_DeferredTaxLiab
|   |       +-- BS_OtherNonCurrentLiab
|   |
|   +-- BS_Equity (~50 accounts)
|       +-- BS_CommonStock
|       +-- BS_APIC
|       +-- BS_RetainedEarnings
|       +-- BS_AOCI
|       +-- BS_TreasuryStock
|       +-- BS_NCI (Non-Controlling Interest)
|
+-- CF_CashFlow (~60 accounts)
|   +-- CF_Operating
|   +-- CF_Investing
|   +-- CF_Financing
|   +-- CF_FXEffect
|   +-- CF_NetChange
|
+-- STAT_Statistical (~40 accounts)
    +-- STAT_Headcount
    +-- STAT_FTE
    +-- STAT_ProductionVolume
    +-- STAT_ProductionHours
    +-- STAT_Yield
    +-- STAT_Capacity
    +-- STAT_SquareFootage
    +-- STAT_CustomerCount
    +-- STAT_OrderBacklog
```

### 2.3 Account Type Assignments

| Account Group | Account Type | Behavior | Exchange Rate Type |
|---------------|-------------|----------|-------------------|
| REV_* | Revenue | Credit balance, flow | Weighted Average |
| COGS_* | Expense | Debit balance, flow | Weighted Average |
| OPEX_* | Expense | Debit balance, flow | Weighted Average |
| OI_* | Revenue/Expense | Varies by account, flow | Weighted Average |
| TAX_* | Expense | Debit balance, flow | Weighted Average |
| BS_Cash, BS_AR, BS_AP | Asset/Liability | Balance | Period End Rate |
| BS_Inventory | Asset | Debit balance, balance | Historical Rate |
| BS_PPE_*, BS_Intangibles | Asset | Debit balance, balance | Historical Rate |
| BS_Equity (except RE) | Equity | Credit balance, balance | Historical Rate |
| BS_RetainedEarnings | Equity | Credit balance, balance | Calculated |
| CF_* | Cash Flow | Flow | Weighted Average |
| STAT_* | Statistical | No currency | None (No Translation) |

### 2.4 Account Properties

Each account member carries the following properties:

| Property | Description | Example Values |
|----------|-------------|----------------|
| AccountType | OneStream account type | Revenue, Expense, Asset, Liability, Equity, Flow |
| ExchangeRateType | FX translation rate | WeightedAverage, EndRate, Historical |
| IsCalculated | Dynamic calculation flag | True, False |
| DataEntryEnabled | Allows input | True, False |
| USGAAPMapping | Mapping to US GAAP line | GAAP_Revenue_Net |
| IFRSMapping | Mapping to IFRS line | IFRS_Revenue_Net |
| ReconRequired | Account reconciliation flag | True, False |

---

## 3. Entity Dimension

### 3.1 Design Rationale

The Entity dimension represents the legal and management reporting structure of the organization. It supports both legal entity consolidation (for statutory reporting) and management hierarchies (for operational reporting). Elimination entities are included at each consolidation level to capture intercompany eliminations.

### 3.2 Hierarchy Structure

```
Total_Company (Top Consolidation)
|
+-- ELIM_Global (Global eliminations)
|
+-- NA_NorthAmerica (Region)
|   +-- ELIM_NA (Regional eliminations)
|   +-- US_UnitedStates (Country)
|   |   +-- ELIM_US
|   |   +-- Plant_US01_Detroit (Plant/Legal Entity)
|   |   +-- Plant_US02_Chicago
|   |   +-- Plant_US03_Houston
|   |   +-- Plant_US04_LosAngeles
|   |   +-- Corp_US01_HQ (Corporate HQ)
|   |   +-- Sales_US01_East
|   |   +-- Sales_US02_West
|   +-- CA_Canada
|   |   +-- Plant_CA01_Toronto
|   |   +-- Plant_CA02_Vancouver
|   +-- MX_Mexico
|       +-- Plant_MX01_Monterrey
|       +-- Plant_MX02_Guadalajara
|
+-- EU_Europe (Region)
|   +-- ELIM_EU
|   +-- DE_Germany
|   |   +-- Plant_DE01_Munich
|   |   +-- Plant_DE02_Stuttgart
|   |   +-- Sales_DE01_DACH
|   +-- UK_UnitedKingdom
|   |   +-- Plant_UK01_Birmingham
|   |   +-- Sales_UK01_Northern
|   +-- FR_France
|   |   +-- Plant_FR01_Lyon
|   +-- IT_Italy
|   |   +-- Plant_IT01_Milan
|   +-- PL_Poland
|       +-- Plant_PL01_Warsaw
|
+-- AP_AsiaPacific (Region)
|   +-- ELIM_AP
|   +-- CN_China
|   |   +-- Plant_CN01_Shanghai
|   |   +-- Plant_CN02_Shenzhen
|   |   +-- Sales_CN01_East
|   +-- JP_Japan
|   |   +-- Plant_JP01_Osaka
|   |   +-- Sales_JP01_National
|   +-- IN_India
|   |   +-- Plant_IN01_Mumbai
|   |   +-- Plant_IN02_Bangalore
|   +-- AU_Australia
|       +-- Sales_AU01_National
|
+-- SA_SouthAmerica (Region)
    +-- ELIM_SA
    +-- BR_Brazil
    |   +-- Plant_BR01_SaoPaulo
    +-- AR_Argentina
        +-- Sales_AR01_National
```

### 3.3 Consolidation Methods

| Entity Type | Method | Ownership % | Description |
|-------------|--------|-------------|-------------|
| Plant_* | Full Consolidation | 100% | Wholly-owned manufacturing plants |
| Corp_* | Full Consolidation | 100% | Corporate/HQ entities |
| Sales_* | Full Consolidation | 100% | Sales/distribution entities |
| Plant_IN02 | Proportional | 65% | Joint venture in Bangalore |
| Sales_AR01 | Equity Method | 40% | Minority stake in Argentina |
| ELIM_* | Elimination | N/A | Elimination entities at each consolidation level |

### 3.4 Entity Properties

| Property | Description | Example Values |
|----------|-------------|----------------|
| DefaultCurrency | Local reporting currency | USD, EUR, CAD, CNY, JPY |
| ConsolidationMethod | Consolidation approach | Full, Proportional, Equity |
| OwnershipPct | Parent ownership percentage | 100, 65, 40 |
| EntityType | Classification | Plant, Corporate, Sales, Elimination |
| Country | Country code | US, DE, CN, JP |
| Region | Geographic region | NA, EU, AP, SA |
| ERPSystem | Source ERP | SAP, Oracle, NetSuite |
| TimeZone | Local time zone | EST, CET, CST, JST |

---

## 4. Scenario Dimension

### 4.1 Design Rationale

The Scenario dimension supports the full planning cycle from actuals through budgeting, forecasting, and ad-hoc analysis. Multiple forecast versions allow side-by-side comparison while maintaining a clean "working" scenario for active planning.

### 4.2 Hierarchy Structure

```
Total_Scenarios
|
+-- Actual (Loaded from source systems)
|
+-- Budget
|   +-- Budget_Working (Active input version)
|   +-- Budget_V1 (First submission)
|   +-- Budget_V2 (Revised submission)
|   +-- Budget_Approved (Board-approved version)
|
+-- Forecast
|   +-- FC_Working (Active forecast)
|   +-- FC_Q1 (Q1 forecast snapshot)
|   +-- FC_Q2 (Q2 forecast snapshot)
|   +-- FC_Q3 (Q3 forecast snapshot)
|
+-- RollingForecast
|   +-- RF_Current (18-month rolling view)
|   +-- RF_Prior (Previous month snapshot)
|
+-- WhatIf
|   +-- WI_Scenario1 (Ad-hoc analysis 1)
|   +-- WI_Scenario2 (Ad-hoc analysis 2)
|
+-- PriorYear (Comparative reference)
```

### 4.3 Scenario Properties

| Scenario | Input Enabled | Lock Status | Data Source | Retention |
|----------|---------------|-------------|-------------|-----------|
| Actual | No (loaded) | Locked after close | ERP/Source Systems | Permanent |
| Budget_Working | Yes | Open during budget cycle | User input | Current + 1 prior |
| Budget_Approved | No | Permanently locked | Promoted from Working | Permanent |
| FC_Working | Yes | Open until snapshot | User input + drivers | Current + 1 prior |
| RF_Current | Yes | Open | Actual + Forecast blend | Rolling |
| WI_Scenario1/2 | Yes | User-controlled | Copied from any scenario | Temporary |
| PriorYear | No | Locked | Rolled from Actual | Permanent |

---

## 5. Time Dimension

### 5.1 Design Rationale

Monthly granularity aligns with the organization's reporting cadence. The fiscal year runs January through December (calendar year). Five years of history plus the current year and two future years provide sufficient depth for trending and planning.

### 5.2 Hierarchy Structure

```
Total_Time
|
+-- FY2022
|   +-- Q1_2022
|   |   +-- Jan_2022
|   |   +-- Feb_2022
|   |   +-- Mar_2022
|   +-- Q2_2022
|   |   +-- Apr_2022 ... Jun_2022
|   +-- Q3_2022
|   |   +-- Jul_2022 ... Sep_2022
|   +-- Q4_2022
|       +-- Oct_2022 ... Dec_2022
|
+-- FY2023 ... (same structure)
+-- FY2024 ... (same structure)
+-- FY2025 ... (same structure)
+-- FY2026 (Current Year)
|   +-- Q1_2026
|   |   +-- Jan_2026
|   |   +-- Feb_2026 (Current Period)
|   |   +-- Mar_2026
|   +-- Q2_2026 ... Q4_2026
|
+-- FY2027 (Plan Year 1)
+-- FY2028 (Plan Year 2)
```

### 5.3 Time Properties

| Property | Description | Example |
|----------|-------------|---------|
| YearNumber | Fiscal year | 2026 |
| QuarterNumber | Quarter within year | 1, 2, 3, 4 |
| MonthNumber | Month within year | 1-12 |
| DaysInPeriod | Calendar days | 28, 29, 30, 31 |
| WorkingDays | Business days | 20, 21, 22, 23 |
| PeriodStatus | Open/Closed/Future | Open |

---

## 6. Flow Dimension

### 6.1 Design Rationale

The Flow dimension supports roll-forward analysis for balance sheet accounts and provides movement categories for cash flow statement construction. This eliminates the need for separate roll-forward reports and enables automated cash flow generation.

### 6.2 Hierarchy Structure

```
Total_Flow
|
+-- F_EndingBalance (Calculated: Opening + Movements)
|
+-- F_OpeningBalance (Prior period ending balance)
|
+-- F_Movements
|   +-- F_DataInput (Direct data entry / loaded data)
|   +-- F_Acquisitions (M&A activity)
|   +-- F_Disposals (Asset disposals)
|   +-- F_Depreciation (Depreciation charges)
|   +-- F_Amortization (Amortization charges)
|   +-- F_Impairment (Asset impairments)
|   +-- F_Reclass (Reclassification entries)
|   +-- F_Accrual (Accrual adjustments)
|   +-- F_Provision (Provision movements)
|   +-- F_FairValue (Fair value adjustments)
|   +-- F_Other (Other movements)
|
+-- F_FXTranslation
|   +-- F_FX_Rate (Rate-driven translation)
|   +-- F_FX_Volume (Volume-driven translation)
|
+-- F_Eliminations
|   +-- F_ICElimination (Intercompany elimination)
|   +-- F_MinorityInterest (NCI adjustment)
|
+-- F_Adjustments
    +-- F_AuditAdj (Audit adjustments)
    +-- F_TaxAdj (Tax provision adjustments)
    +-- F_ManualAdj (Manual journal entries)
```

---

## 7. Consolidation Dimension

### 7.1 Design Rationale

The Consolidation dimension captures the processing stages of the financial close, enabling users to view data at each step of the consolidation pipeline.

### 7.2 Hierarchy Structure

```
Total_Consolidation
|
+-- CON_Consolidated (Fully consolidated view)
|
+-- CON_Local (Local currency, pre-translation)
|   +-- CON_Input (As loaded from source)
|   +-- CON_Calculated (After local calculations)
|   +-- CON_Adjusted (After local adjustments)
|
+-- CON_Translated (After currency translation)
|
+-- CON_Elimination (IC eliminations applied)
|
+-- CON_Proportional (After proportional adjustments)
|
+-- CON_EquityPickup (After equity method pickup)
|
+-- CON_Contribution (Entity contribution to parent)
```

---

## 8. UD1 -- Product

### 8.1 Design Rationale

Product dimension enables revenue and COGS analysis by product line. The hierarchy aligns with the organization's product catalog structure. This dimension is used in the Finance and Planning cubes to support product-level profitability analysis and revenue planning.

### 8.2 Hierarchy Structure

```
Total_Products
|
+-- PRD_None (Non-product-specific data; default member)
|
+-- PRD_ManufacturedGoods
|   +-- PRD_IndustrialEquipment
|   |   +-- PRD_IE_HeavyMachinery
|   |   +-- PRD_IE_Automation
|   |   +-- PRD_IE_Tooling
|   +-- PRD_ConsumerProducts
|   |   +-- PRD_CP_Appliances
|   |   +-- PRD_CP_Electronics
|   |   +-- PRD_CP_Accessories
|   +-- PRD_Components
|       +-- PRD_CM_Castings
|       +-- PRD_CM_Assemblies
|       +-- PRD_CM_ElectricalParts
|
+-- PRD_Services
|   +-- PRD_SVC_Installation
|   +-- PRD_SVC_Maintenance
|   +-- PRD_SVC_Training
|
+-- PRD_Spare_Parts
    +-- PRD_SP_Mechanical
    +-- PRD_SP_Electrical
    +-- PRD_SP_Consumables
```

---

## 9. UD2 -- Customer

### 9.1 Design Rationale

Customer dimension enables revenue analysis by customer segment. Key accounts are individually tracked; smaller customers are grouped by segment. This supports customer profitability analysis and sales forecasting.

### 9.2 Hierarchy Structure

```
Total_Customers
|
+-- CUST_None (Non-customer-specific data; default member)
|
+-- CUST_Industrial
|   +-- CUST_IND_KeyAccounts
|   |   +-- CUST_IND_KA001 through CUST_IND_KA020 (Top 20 industrial accounts)
|   +-- CUST_IND_MidMarket
|   +-- CUST_IND_SmallBusiness
|
+-- CUST_Consumer
|   +-- CUST_CON_Retail
|   +-- CUST_CON_Wholesale
|   +-- CUST_CON_Ecommerce
|
+-- CUST_Government
|   +-- CUST_GOV_Federal
|   +-- CUST_GOV_StateLocal
|   +-- CUST_GOV_Military
|
+-- CUST_OEM
|   +-- CUST_OEM_Automotive
|   +-- CUST_OEM_Aerospace
|   +-- CUST_OEM_Medical
|
+-- CUST_Intercompany (IC sales -- mirrors entity)
```

---

## 10. UD3 -- Department

### 10.1 Design Rationale

Department dimension supports OPEX budgeting and headcount planning by organizational unit. The structure aligns with the HR org chart and enables cost center-level reporting.

### 10.2 Hierarchy Structure

```
Total_Departments
|
+-- DEPT_None (Non-department-specific data; default member)
|
+-- DEPT_Manufacturing
|   +-- DEPT_MFG_Production
|   +-- DEPT_MFG_Quality
|   +-- DEPT_MFG_Maintenance
|   +-- DEPT_MFG_Logistics
|   +-- DEPT_MFG_Safety
|
+-- DEPT_Engineering
|   +-- DEPT_ENG_Design
|   +-- DEPT_ENG_Process
|   +-- DEPT_ENG_RandD
|
+-- DEPT_SalesMarketing
|   +-- DEPT_SM_DirectSales
|   +-- DEPT_SM_ChannelSales
|   +-- DEPT_SM_Marketing
|   +-- DEPT_SM_CustomerService
|
+-- DEPT_Finance
|   +-- DEPT_FIN_Accounting
|   +-- DEPT_FIN_FPandA
|   +-- DEPT_FIN_Tax
|   +-- DEPT_FIN_Treasury
|   +-- DEPT_FIN_InternalAudit
|
+-- DEPT_HR
|   +-- DEPT_HR_TalentAcq
|   +-- DEPT_HR_Compensation
|   +-- DEPT_HR_Training
|
+-- DEPT_IT
|   +-- DEPT_IT_Infrastructure
|   +-- DEPT_IT_Applications
|   +-- DEPT_IT_Security
|
+-- DEPT_Legal
|   +-- DEPT_LGL_Corporate
|   +-- DEPT_LGL_Compliance
|
+-- DEPT_Executive
    +-- DEPT_EXEC_CEO
    +-- DEPT_EXEC_CFO
    +-- DEPT_EXEC_COO
```

---

## 11. UD4 -- Project

### 11.1 Design Rationale

Project dimension tracks CAPEX projects and strategic initiatives. Each project has a lifecycle (Planned, Active, Complete) and is linked to an entity and department for reporting.

### 11.2 Hierarchy Structure

```
Total_Projects
|
+-- PROJ_None (Non-project-specific data; default member)
|
+-- PROJ_CAPEX
|   +-- PROJ_CAP_NewEquipment
|   |   +-- PROJ_CAP_NE_001 through PROJ_CAP_NE_015
|   +-- PROJ_CAP_FacilityExpansion
|   |   +-- PROJ_CAP_FE_001 through PROJ_CAP_FE_005
|   +-- PROJ_CAP_Technology
|   |   +-- PROJ_CAP_IT_001 through PROJ_CAP_IT_010
|   +-- PROJ_CAP_Maintenance
|       +-- PROJ_CAP_MT_001 through PROJ_CAP_MT_010
|
+-- PROJ_Strategic
|   +-- PROJ_STR_Lean (Lean manufacturing initiatives)
|   +-- PROJ_STR_Digital (Digital transformation)
|   +-- PROJ_STR_Sustainability (ESG initiatives)
|
+-- PROJ_MandA
    +-- PROJ_MA_001 (Acquisition targets -- restricted access)
```

---

## 12. UD5 -- Intercompany

### 12.1 Design Rationale

The Intercompany dimension mirrors the Entity dimension to facilitate IC transaction matching and elimination. Each IC partner member corresponds to a base-level entity. The "IC_None" member is used for non-IC transactions (the vast majority of data).

### 12.2 Hierarchy Structure

```
Total_ICPartners
|
+-- IC_None (Non-intercompany transactions; default member)
|
+-- IC_NA
|   +-- IC_Plant_US01_Detroit
|   +-- IC_Plant_US02_Chicago
|   +-- IC_Plant_US03_Houston
|   +-- IC_Plant_US04_LosAngeles
|   +-- IC_Corp_US01_HQ
|   +-- IC_Plant_CA01_Toronto
|   +-- IC_Plant_CA02_Vancouver
|   +-- IC_Plant_MX01_Monterrey
|   +-- IC_Plant_MX02_Guadalajara
|
+-- IC_EU
|   +-- IC_Plant_DE01_Munich
|   +-- IC_Plant_DE02_Stuttgart
|   +-- IC_Plant_UK01_Birmingham
|   +-- IC_Plant_FR01_Lyon
|   +-- IC_Plant_IT01_Milan
|   +-- IC_Plant_PL01_Warsaw
|
+-- IC_AP
|   +-- IC_Plant_CN01_Shanghai
|   +-- IC_Plant_CN02_Shenzhen
|   +-- IC_Plant_JP01_Osaka
|   +-- IC_Plant_IN01_Mumbai
|   +-- IC_Plant_IN02_Bangalore
|
+-- IC_SA
    +-- IC_Plant_BR01_SaoPaulo
```

---

## 13. UD6 -- Plant

### 13.1 Design Rationale

The Plant dimension provides manufacturing-specific detail below the entity level. While entities represent legal entities (which may have one or more plants), this dimension enables plant-level operational analysis. For entities with a single plant, the plant member maps one-to-one with the entity.

### 13.2 Hierarchy Structure

```
Total_Plants
|
+-- PLT_None (Non-plant-specific data; default member)
|
+-- PLT_NA
|   +-- PLT_US01_Detroit_Line1
|   +-- PLT_US01_Detroit_Line2
|   +-- PLT_US02_Chicago_Main
|   +-- PLT_US03_Houston_Main
|   +-- PLT_US04_LA_Main
|   +-- PLT_CA01_Toronto_Main
|   +-- PLT_CA02_Vancouver_Main
|   +-- PLT_MX01_Monterrey_A
|   +-- PLT_MX01_Monterrey_B
|   +-- PLT_MX02_Guadalajara_Main
|
+-- PLT_EU
|   +-- PLT_DE01_Munich_Main
|   +-- PLT_DE02_Stuttgart_Main
|   +-- PLT_UK01_Birmingham_Main
|   +-- PLT_FR01_Lyon_Main
|   +-- PLT_IT01_Milan_Main
|   +-- PLT_PL01_Warsaw_Main
|
+-- PLT_AP
|   +-- PLT_CN01_Shanghai_Main
|   +-- PLT_CN02_Shenzhen_Main
|   +-- PLT_JP01_Osaka_Main
|   +-- PLT_IN01_Mumbai_Main
|   +-- PLT_IN02_Bangalore_Main
|
+-- PLT_SA
    +-- PLT_BR01_SaoPaulo_Main
```

---

## 14. UD7 -- Currency Reporting

### 14.1 Design Rationale

The Currency Reporting dimension allows users to view data in different reporting currencies without needing separate cubes. The local currency view shows data in the entity's functional currency, while USD, EUR, and GBP views support management reporting in key corporate currencies.

### 14.2 Hierarchy Structure

```
Total_CurrencyViews
|
+-- CURR_Local (Entity functional currency)
+-- CURR_USD (US Dollar reporting)
+-- CURR_EUR (Euro reporting)
+-- CURR_GBP (British Pound reporting)
+-- CURR_Elim (Elimination currency adjustments)
+-- CURR_Adj (Currency translation adjustments)
```

---

## 15. UD8 -- Data Source

### 15.1 Design Rationale

Data Source tracks the origin of each data point, supporting audit trail requirements and data quality analysis. This enables reconciliation between OneStream and source systems.

### 15.2 Hierarchy Structure

```
Total_DataSources
|
+-- DS_None (Default / unspecified)
|
+-- DS_ERP
|   +-- DS_SAP (SAP HANA GL data)
|   +-- DS_Oracle (Oracle EBS GL data)
|   +-- DS_NetSuite (NetSuite GL data)
|
+-- DS_HCM
|   +-- DS_Workday (Workday HCM data)
|
+-- DS_MES
|   +-- DS_MES_Production (Production volume data)
|   +-- DS_MES_Quality (Quality metrics)
|
+-- DS_Manual
|   +-- DS_JournalEntry (Manual journal entries)
|   +-- DS_ExcelUpload (Excel template uploads)
|   +-- DS_DataForm (CubeView data entry)
|
+-- DS_Calculated
|   +-- DS_Consolidation (Consolidation-generated)
|   +-- DS_Allocation (Allocation-generated)
|   +-- DS_FXTranslation (FX translation-generated)
|
+-- DS_Planning
    +-- DS_BudgetInput (Budget data entry)
    +-- DS_ForecastInput (Forecast data entry)
    +-- DS_DriverCalc (Driver-based calculations)
```

---

## 16. Cross-Dimension Relationships

### 16.1 Entity-to-IC Partner Mapping

Each base-level entity has a corresponding IC partner member. The mapping is maintained in a lookup table and enforced during data load:

| Entity Member | IC Partner Member |
|--------------|-------------------|
| Plant_US01_Detroit | IC_Plant_US01_Detroit |
| Plant_DE01_Munich | IC_Plant_DE01_Munich |
| (All base entities) | (Corresponding IC_ member) |

### 16.2 Entity-to-Plant Mapping

| Entity | Plant Members |
|--------|--------------|
| Plant_US01_Detroit | PLT_US01_Detroit_Line1, PLT_US01_Detroit_Line2 |
| Plant_MX01_Monterrey | PLT_MX01_Monterrey_A, PLT_MX01_Monterrey_B |
| (Single-plant entities) | (One-to-one mapping) |

### 16.3 Account-to-Flow Validity

| Account Type | Valid Flow Members |
|-------------|-------------------|
| PL Accounts (Revenue, Expense) | F_DataInput only |
| BS_PPE | F_OpeningBalance, F_Acquisitions, F_Disposals, F_Depreciation, F_FXTranslation |
| BS_Inventory | F_OpeningBalance, F_DataInput, F_Reclass, F_FXTranslation |
| BS_Equity | F_OpeningBalance, F_DataInput, F_FXTranslation |
| STAT Accounts | F_DataInput only |

### 16.4 Account-to-Department Validity

| Account Group | Valid Departments |
|--------------|-------------------|
| REV_* | DEPT_SalesMarketing, DEPT_None |
| COGS_* | DEPT_Manufacturing, DEPT_None |
| OPEX_* | All departments |
| BS_* | DEPT_None (balance sheet not tracked by department) |
| STAT_Headcount, STAT_FTE | All departments |

---

## 17. Naming Conventions

### 17.1 Dimension Member Naming

| Dimension | Pattern | Example | Description |
|-----------|---------|---------|-------------|
| Account | {Type}_{Description} | REV_Product_Sales | Type prefix, underscore-separated |
| Entity | {Type}_{CountryCode}{Seq}_{City} | Plant_US01_Detroit | Type, country code, sequence, city |
| Scenario | {Category}_{Version} | Budget_Working | Category prefix, version identifier |
| Time | {Period}_{Year} | Jan_2026 | Three-letter month, four-digit year |
| Flow | F_{Movement} | F_Depreciation | "F_" prefix, movement description |
| Consolidation | CON_{Stage} | CON_Translated | "CON_" prefix, stage name |
| UD1 - Product | PRD_{Category}_{Sub} | PRD_IE_HeavyMachinery | "PRD_" prefix, category abbreviation |
| UD2 - Customer | CUST_{Segment}_{Sub} | CUST_IND_KA001 | "CUST_" prefix, segment |
| UD3 - Department | DEPT_{Function}_{Sub} | DEPT_FIN_Accounting | "DEPT_" prefix, function abbreviation |
| UD4 - Project | PROJ_{Type}_{Seq} | PROJ_CAP_NE_001 | "PROJ_" prefix, type, sequence |
| UD5 - IC Partner | IC_{EntityName} | IC_Plant_US01_Detroit | "IC_" prefix, mirrors entity name |
| UD6 - Plant | PLT_{EntityCode}_{Line} | PLT_US01_Detroit_Line1 | "PLT_" prefix, entity code, line |
| UD7 - Currency | CURR_{Code} | CURR_USD | "CURR_" prefix, ISO currency code |
| UD8 - Data Source | DS_{System}_{Detail} | DS_SAP | "DS_" prefix, system name |

### 17.2 General Rules

1. **No Spaces:** Use underscores to separate words
2. **PascalCase for Multi-Word Segments:** e.g., `HeavyMachinery`, `DirectSales`
3. **Consistent Prefixes:** Every dimension uses a unique prefix for member names
4. **"None" Members:** Each UD dimension has a `{Prefix}_None` member as the default for non-applicable data
5. **Elimination Entities:** Always prefixed with `ELIM_`
6. **Maximum Length:** Member names limited to 50 characters
7. **Description Field:** Full descriptive names stored in the Description property (e.g., "Detroit Manufacturing Plant - Heavy Equipment")

---

*End of Document*
