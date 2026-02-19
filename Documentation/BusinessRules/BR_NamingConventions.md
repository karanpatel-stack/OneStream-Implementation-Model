# Business Rules Naming Conventions

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Business Rule Naming](#1-business-rule-naming)
2. [Dimension Member Naming](#2-dimension-member-naming)
3. [VB.NET Variable Naming](#3-vbnet-variable-naming)
4. [File Naming Conventions](#4-file-naming-conventions)
5. [XML Element Naming](#5-xml-element-naming)
6. [Database Object Naming](#6-database-object-naming)
7. [Dashboard Component Naming](#7-dashboard-component-naming)

---

## 1. Business Rule Naming

### 1.1 Prefix Conventions

Every business rule name begins with a type prefix that identifies its category:

| Prefix | Rule Type | Description | Example |
|--------|-----------|-------------|---------|
| `FR_` | Finance Rule | Consolidation processing rules (translation, elimination, ownership) | `FR_Consolidation` |
| `CR_` | Calculate Rule | Calculation, allocation, derivation, and data movement rules | `CR_COGSAllocation` |
| `CN_` | Connector Rule | Source system data extraction rules | `CN_SAP_GLActuals` |
| `DDA_` | Dashboard DataAdapter | Rules that retrieve and format data for dashboard components | `DDA_ExecutiveSummary` |
| `DSF_` | Dashboard String Function | Rules that generate dynamic text, icons, and labels | `DSF_EntityStatusIcon` |
| `MF_` | Member Filter | Rules that dynamically generate dimension member lists | `MF_EntitySecurity` |
| `EH_` | Event Handler | Rules triggered by system events (data change, workflow) | `EH_DataQualityValidation` |
| `EX_` | Extender | Batch processing, scheduled tasks, and utility rules | `EX_BatchConsolidation` |
| `VAL_` | Validation Rule | Data quality and integrity validation rules | `VAL_TrialBalanceCheck` |

### 1.2 Naming Structure

```
{Prefix}_{CategoryOrSource}_{ActionOrDescription}
```

**Examples:**
- `CN_SAP_GLActuals` -- Connector rule, SAP source, GL actuals extraction
- `CR_COGS_Allocation` -- Calculate rule, COGS category, allocation action
- `DDA_Revenue_ByProduct` -- Dashboard DataAdapter, revenue data, product breakdown
- `VAL_IC_BalanceMatch` -- Validation rule, intercompany category, balance matching

### 1.3 Rules for Rule Names

1. **Use PascalCase** for multi-word segments: `GLActuals`, not `gl_actuals` or `glactuals`
2. **Maximum length:** 50 characters including prefix
3. **No spaces or special characters** -- underscores only as delimiters
4. **Descriptive but concise** -- the name should convey purpose at a glance
5. **Source system in name** for connector rules: `CN_SAP_*`, `CN_Oracle_*`, `CN_Workday_*`
6. **Action verbs** where appropriate: `Calc`, `Push`, `Load`, `Check`, `Match`

### 1.4 Version Suffixes (When Needed)

For rules that have versioned variants during development or migration:

| Suffix | Usage | Example |
|--------|-------|---------|
| `_V2` | Major revision of existing rule | `CR_COGSAllocation_V2` |
| `_DEP` | Deprecated -- scheduled for removal | `CR_OldAllocation_DEP` |
| `_TEST` | Test/experimental -- not for production | `CN_SAP_GLActuals_TEST` |

---

## 2. Dimension Member Naming

### 2.1 General Rules

1. **No spaces** -- use underscores as word separators
2. **PascalCase** for multi-word descriptive segments: `HeavyMachinery`, `DirectSales`
3. **Maximum length:** 50 characters
4. **Unique prefix** per dimension to avoid ambiguity in formulas
5. **"None" member** in every UD dimension: `{Prefix}_None`
6. **Description property** holds the full human-readable name

### 2.2 Dimension-Specific Conventions

#### Account Dimension

```
Pattern: {AccountType}_{Description}
Prefix by section:
  REV_    Revenue accounts
  COGS_   Cost of goods sold accounts
  GM_     Gross margin (calculated)
  OPEX_   Operating expense accounts
  OI_     Other income/expense accounts
  TAX_    Tax accounts
  NI_     Net income (calculated)
  BS_     Balance sheet accounts
  CF_     Cash flow accounts
  STAT_   Statistical accounts
  HR_     HR-specific accounts

Examples:
  REV_Product_Sales
  COGS_DirectMaterial
  OPEX_Salaries
  BS_AccountsReceivable
  BS_INV_RawMaterial       (sub-category: INV for inventory)
  STAT_ProductionVolume
  HR_COMP_BaseSalary       (sub-category: COMP for compensation)
```

#### Entity Dimension

```
Pattern: {EntityType}_{CountryCode}{Sequence}_{City}
Entity Types:
  Plant_    Manufacturing plant
  Corp_     Corporate/HQ entity
  Sales_    Sales/distribution entity
  ELIM_     Elimination entity

Country Codes: ISO 3166-1 alpha-2 (US, CA, MX, DE, UK, FR, IT, PL, CN, JP, IN, AU, BR, AR)

Regional Aggregation Codes:
  NA_       North America
  EU_       Europe
  AP_       Asia Pacific
  SA_       South America

Examples:
  Plant_US01_Detroit
  Plant_DE02_Stuttgart
  Corp_US01_HQ
  Sales_JP01_National
  ELIM_NA
  NA_NorthAmerica
  Total_Company
```

#### Scenario Dimension

```
Pattern: {Category}_{Version}
Categories:
  Actual          Actual data (no suffix needed)
  Budget_         Budget versions
  FC_             Forecast versions
  RF_             Rolling forecast
  WI_             What-if scenarios
  PriorYear       Prior year comparison

Examples:
  Actual
  Budget_Working
  Budget_V1
  Budget_Approved
  FC_Working
  FC_Q1
  RF_Current
  WI_Scenario1
  PriorYear
```

#### Time Dimension

```
Pattern: {Period}_{FourDigitYear}
Periods:
  Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec  (months)
  Q1, Q2, Q3, Q4                                               (quarters)
  FY                                                            (full year)

Examples:
  Jan_2026
  Q1_2026
  FY2026
  Total_Time
```

#### Flow Dimension

```
Pattern: F_{MovementType}
Prefix: F_ for all Flow members

Examples:
  F_EndingBalance
  F_OpeningBalance
  F_DataInput
  F_Depreciation
  F_FX_Rate
  F_ICElimination
  F_ManualAdj
```

#### Consolidation Dimension

```
Pattern: CON_{Stage}
Prefix: CON_ for all Consolidation members

Examples:
  CON_Consolidated
  CON_Local
  CON_Input
  CON_Translated
  CON_Elimination
```

#### UD1 -- Product

```
Pattern: PRD_{Category}_{Subcategory}
Prefix: PRD_

Examples:
  PRD_None
  PRD_IE_HeavyMachinery
  PRD_CP_Appliances
  PRD_SVC_Installation
  PRD_SP_Mechanical
```

#### UD2 -- Customer

```
Pattern: CUST_{Segment}_{Subsegment or ID}
Prefix: CUST_

Examples:
  CUST_None
  CUST_IND_KA001           (Industrial key account #1)
  CUST_CON_Retail
  CUST_GOV_Federal
  CUST_OEM_Automotive
  CUST_Intercompany
```

#### UD3 -- Department

```
Pattern: DEPT_{Function}_{Subfunction}
Prefix: DEPT_

Examples:
  DEPT_None
  DEPT_MFG_Production
  DEPT_ENG_RandD
  DEPT_SM_DirectSales
  DEPT_FIN_FPandA
  DEPT_IT_Security
```

#### UD4 -- Project

```
Pattern: PROJ_{Type}_{Category}_{Sequence}
Prefix: PROJ_

Examples:
  PROJ_None
  PROJ_CAP_NE_001          (CAPEX, New Equipment, #001)
  PROJ_CAP_FE_003          (CAPEX, Facility Expansion, #003)
  PROJ_STR_Lean            (Strategic, Lean initiative)
  PROJ_MA_001              (M&A target #001)
```

#### UD5 -- Intercompany

```
Pattern: IC_{MirroredEntityName}
Prefix: IC_

Examples:
  IC_None
  IC_Plant_US01_Detroit
  IC_Plant_DE01_Munich
  IC_Corp_US01_HQ
```

#### UD6 -- Plant

```
Pattern: PLT_{EntityCode}_{LineName}
Prefix: PLT_

Examples:
  PLT_None
  PLT_US01_Detroit_Line1
  PLT_MX01_Monterrey_A
  PLT_CN01_Shanghai_Main
```

#### UD7 -- Currency Reporting

```
Pattern: CURR_{ISOCode}
Prefix: CURR_

Examples:
  CURR_Local
  CURR_USD
  CURR_EUR
  CURR_GBP
```

#### UD8 -- Data Source

```
Pattern: DS_{SystemOrCategory}_{Detail}
Prefix: DS_

Examples:
  DS_None
  DS_SAP
  DS_Oracle
  DS_Workday
  DS_MES_Production
  DS_JournalEntry
  DS_BudgetInput
  DS_Consolidation
```

---

## 3. VB.NET Variable Naming

### 3.1 Local Variables

Use **camelCase** for all local variables:

```vb
Dim entityName As String = "Plant_US01_Detroit"
Dim currentPeriod As String = "Jan_2026"
Dim revenueAmount As Decimal = 0D
Dim isConsolidated As Boolean = False
Dim entityList As New List(Of String)
Dim dataBuffer As DataBuffer = Nothing
```

### 3.2 Method Parameters

Use **camelCase** for method parameters:

```vb
Private Sub ProcessEntity(ByVal entityName As String, ByVal periodKey As String)
Private Function GetRevenueTotal(ByVal entityId As String, ByVal scenarioName As String) As Decimal
```

### 3.3 Methods and Functions

Use **PascalCase** for all method and function names:

```vb
Public Sub CalculateConsolidation()
Private Function GetEntityCurrency(ByVal entityName As String) As String
Private Sub ProcessAllocationBatch()
Private Function ValidateTrialBalance() As Boolean
```

### 3.4 Constants

Use **UPPER_SNAKE_CASE** for constants:

```vb
Private Const MAX_RETRY_COUNT As Integer = 3
Private Const IC_MATCHING_TOLERANCE As Decimal = 1000D
Private Const DEFAULT_SCENARIO As String = "Actual"
Private Const LOG_PREFIX As String = "[FR_Consolidation]"
```

### 3.5 Class-Level Variables

Use **PascalCase** with descriptive names for class-level variables. Prefix with `m_` for private members when disambiguation is needed:

```vb
Private m_ApiClient As OneStreamApiClient
Private m_Logger As LogHelper
Private CurrentScenario As String
Private ProcessingEntities As List(Of String)
```

### 3.6 OneStream API Object Names

Use standard OneStream naming for API objects:

```vb
Dim api As FinanceRulesApi          ' Standard API reference
Dim si As SessionInfo               ' Session information
Dim globals As BRGlobals            ' Global parameters
Dim dataCell As DataCell            ' Single data cell
Dim dataCellPk As DataCellPk        ' Data cell primary key
Dim memberInfo As MemberInfo        ' Dimension member metadata
```

---

## 4. File Naming Conventions

### 4.1 Business Rule Source Files

```
Pattern: {RuleType}_{RuleName}.vb
Location: /BusinessRules/{RuleType}/

Examples:
  /BusinessRules/FinanceRules/FR_Consolidation.vb
  /BusinessRules/CalculateRules/CR_COGSAllocation.vb
  /BusinessRules/ConnectorRules/CN_SAP_GLActuals.vb
  /BusinessRules/DashboardDataAdapters/DDA_ExecutiveSummary.vb
  /BusinessRules/DashboardStringFunctions/DSF_EntityStatusIcon.vb
  /BusinessRules/MemberFilters/MF_EntitySecurity.vb
  /BusinessRules/EventHandlers/EH_DataQualityValidation.vb
  /BusinessRules/Extenders/EX_BatchConsolidation.vb
  /BusinessRules/ValidationRules/VAL_TrialBalanceCheck.vb
```

### 4.2 Dimension Load Files (CSV)

```
Pattern: DIM_{DimensionName}_{Environment}_{YYYYMMDD}.csv
Location: /Data/Dimensions/

Examples:
  DIM_Account_PROD_20260218.csv
  DIM_Entity_PROD_20260218.csv
  DIM_UD1_Product_PROD_20260218.csv
```

### 4.3 Data Load Files

```
Pattern: DATA_{SourceSystem}_{DataType}_{YYYYMMDD}.csv
Location: /Data/Loads/

Examples:
  DATA_SAP_GLActuals_20260218.csv
  DATA_MES_Production_20260218.csv
  DATA_Workday_Headcount_20260216.csv
```

### 4.4 Mapping Files

```
Pattern: MAP_{SourceSystem}_{Dimension}_{YYYYMMDD}.csv
Location: /Data/Mappings/

Examples:
  MAP_SAP_Account_20260218.csv
  MAP_Oracle_Entity_20260218.csv
  MAP_NetSuite_Account_20260218.csv
```

### 4.5 Configuration Files

```
Pattern: {Environment}.config
Location: /Deployment/Configs/

Examples:
  DEV.config
  QA.config
  PROD.config
```

### 4.6 Deployment Scripts

```
Pattern: Deploy_{Component}.ps1  or  Validate_{Component}.ps1
Location: /Deployment/Scripts/

Examples:
  Deploy_BusinessRules.ps1
  Deploy_Dimensions.ps1
  Deploy_DataManagement.ps1
  Validate_Deployment.ps1
```

---

## 5. XML Element Naming

### 5.1 Configuration XML

Use **PascalCase** for XML element and attribute names:

```xml
<EnvironmentConfig>
    <ServerUrl>https://onestream.company.com</ServerUrl>
    <DatabaseName>OS_PROD</DatabaseName>
    <ApplicationName>MFG_Enterprise</ApplicationName>
    <Settings>
        <LogLevel>Warning</LogLevel>
        <SampleDataEnabled>false</SampleDataEnabled>
        <NotificationsEnabled>true</NotificationsEnabled>
        <BackupEnabled>true</BackupEnabled>
    </Settings>
    <Connections>
        <Connection Name="SAP_HANA" Type="JDBC">
            <ServerHost>saphana.corp.internal</ServerHost>
            <Port>30015</Port>
            <AuthMethod>SQLLogin</AuthMethod>
        </Connection>
    </Connections>
</EnvironmentConfig>
```

### 5.2 Data Exchange XML

```xml
<DataExchange>
    <Header>
        <SourceSystem>SAP</SourceSystem>
        <ExportDate>2026-02-18</ExportDate>
        <RecordCount>50000</RecordCount>
    </Header>
    <Records>
        <Record>
            <EntityCode>1000</EntityCode>
            <AccountCode>400000</AccountCode>
            <PeriodKey>2026001</PeriodKey>
            <Amount>1500000.00</Amount>
            <CurrencyCode>USD</CurrencyCode>
        </Record>
    </Records>
</DataExchange>
```

### 5.3 Dashboard Definition XML

```xml
<DashboardDefinition Name="ExecutiveSummary">
    <Components>
        <Component Type="DataGrid" Name="KPISummary">
            <DataAdapter>DDA_ExecutiveSummary</DataAdapter>
            <Layout Rows="10" Columns="6" />
        </Component>
        <Component Type="Chart" Name="RevenueTrend">
            <DataAdapter>DDA_RevenueByProduct</DataAdapter>
            <ChartType>Line</ChartType>
        </Component>
    </Components>
</DashboardDefinition>
```

---

## 6. Database Object Naming

### 6.1 Tables

```
Pattern: {Schema}_{Category}_{Description}
Schemas: STG (Staging), DIM (Dimension), MAP (Mapping), AUD (Audit), CFG (Configuration)

Examples:
  STG_GL_Actuals
  STG_ERROR_LOG
  DIM_MAPPING
  MAP_SAP_Account
  AUD_DataChangeLog
  CFG_ThresholdValues
  INT_EXECUTION_LOG
```

### 6.2 Stored Procedures

```
Pattern: usp_{Action}_{Object}
Prefix: usp_ (user stored procedure)

Examples:
  usp_Load_GLActuals
  usp_Validate_TrialBalance
  usp_Archive_StagingData
  usp_Get_MappingBySource
```

### 6.3 Views

```
Pattern: vw_{Description}
Prefix: vw_

Examples:
  vw_CurrentPeriodActuals
  vw_EntityMappingLookup
  vw_DataQualityScorecard
```

### 6.4 Indexes

```
Pattern: IX_{TableName}_{ColumnName(s)}

Examples:
  IX_STG_GL_Actuals_EntityPeriod
  IX_DIM_MAPPING_SourceValue
  IX_INT_EXECUTION_LOG_Timestamp
```

---

## 7. Dashboard Component Naming

### 7.1 Dashboard Names

```
Pattern: DB_{Category}_{Description}

Examples:
  DB_EXEC_Summary              (Executive Summary)
  DB_FIN_PLAnalysis            (P&L Analysis)
  DB_FIN_BSAnalysis            (Balance Sheet Analysis)
  DB_OPS_PlantPerformance      (Plant Operations)
  DB_PLN_BudgetEntry           (Budget Entry Forms)
  DB_PLN_ForecastReview        (Forecast Review)
  DB_RECON_Status              (Reconciliation Status)
  DB_ADMIN_DataOps             (Data Operations Monitor)
```

### 7.2 CubeView Names

```
Pattern: CV_{Cube}_{Purpose}_{Detail}

Examples:
  CV_FIN_DataEntry_GLJournal
  CV_FIN_Inquiry_PLByEntity
  CV_FIN_Inquiry_BSRollForward
  CV_PLN_DataEntry_RevenueBudget
  CV_PLN_DataEntry_OPEXBudget
  CV_PLN_Inquiry_BudgetSummary
  CV_HR_DataEntry_Headcount
  CV_HR_Inquiry_CompSummary
  CV_RECON_DataEntry_Certification
```

### 7.3 Workspace Names

```
Pattern: WS_{UserGroup}_{Purpose}

Examples:
  WS_Executive_Dashboard
  WS_Finance_Close
  WS_Planning_Budget
  WS_HR_Planning
  WS_Recon_Certification
  WS_Admin_Operations
```

---

*End of Document*
