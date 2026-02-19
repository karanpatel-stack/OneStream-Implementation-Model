# Business Rules Development Guide

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Prepared For:** Global Multi-Plant Manufacturing Corporation
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [VB.NET Coding Standards](#1-vbnet-coding-standards)
2. [Common OneStream API Patterns](#2-common-onestream-api-patterns)
3. [Error Handling Best Practices](#3-error-handling-best-practices)
4. [Logging Patterns](#4-logging-patterns)
5. [Performance Optimization](#5-performance-optimization)
6. [Testing Approach](#6-testing-approach)
7. [Code Review Checklist](#7-code-review-checklist)
8. [Common Pitfalls and Solutions](#8-common-pitfalls-and-solutions)

---

## 1. VB.NET Coding Standards

### 1.1 General Principles

- Write clear, readable code -- favor clarity over cleverness
- Follow the naming conventions defined in `BR_NamingConventions.md`
- Keep methods short and focused (target < 50 lines per method)
- Add meaningful comments for business logic (not obvious code operations)
- Use `Option Strict On` and `Option Explicit On` semantics (avoid late binding)

### 1.2 Code Structure Template

Every business rule should follow this standard structure:

```vb
'--------------------------------------------------
' Rule Name:    CR_COGSAllocation
' Type:         Calculate Rule
' Description:  Allocates COGS from plant level to product
'               dimension based on production volume drivers.
' Author:       Implementation Team
' Created:      2026-02-18
' Modified:     2026-02-18
' Dependencies: MES production data loaded; product volume
'               data available in STAT_ProductionVolume
'--------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports OneStream.Finance.Engine

Namespace OneStream.BusinessRule.CalculateRule.CR_COGSAllocation

    Public Class MainClass

        ' Constants
        Private Const LOG_PREFIX As String = "[CR_COGSAllocation]"
        Private Const TOLERANCE As Decimal = 0.01D

        ' Main entry point
        Public Function Main(
            ByVal si As SessionInfo,
            ByVal globals As BRGlobals,
            ByVal api As FinanceRulesApi,
            ByVal args As CalculateRuleArgs
        ) As Object

            Try
                ' Step 1: Validate prerequisites
                ValidatePrerequisites(si, api, args)

                ' Step 2: Retrieve allocation drivers
                Dim drivers As Dictionary(Of String, Decimal) = GetAllocationDrivers(si, api, args)

                ' Step 3: Perform allocation
                PerformAllocation(si, api, args, drivers)

                ' Step 4: Validate results
                ValidateResults(si, api, args)

                BRApi.ErrorLog.LogMessage(si, LOG_PREFIX & " Completed successfully.")
                Return Nothing

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, LOG_PREFIX & " ERROR: " & ex.Message)
                Throw
            End Try

        End Function

        ' Private helper methods follow...
        Private Sub ValidatePrerequisites(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, ByVal args As CalculateRuleArgs)
            ' Validation logic
        End Sub

        Private Function GetAllocationDrivers(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, ByVal args As CalculateRuleArgs) As Dictionary(Of String, Decimal)
            ' Driver retrieval logic
            Return New Dictionary(Of String, Decimal)
        End Function

        Private Sub PerformAllocation(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, ByVal args As CalculateRuleArgs, ByVal drivers As Dictionary(Of String, Decimal))
            ' Allocation logic
        End Sub

        Private Sub ValidateResults(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, ByVal args As CalculateRuleArgs)
            ' Validation logic
        End Sub

    End Class

End Namespace
```

### 1.3 Formatting Standards

| Standard | Rule | Example |
|----------|------|---------|
| Indentation | 4 spaces (no tabs) | Standard Visual Studio setting |
| Line length | Maximum 120 characters | Break long lines at logical points |
| Blank lines | One blank line between methods; no multiple consecutive blank lines | -- |
| Braces | N/A (VB.NET uses End If/End Sub/End Function) | -- |
| String concatenation | Use `String.Format` or `$""` interpolation for readability | `$"Entity: {entityName}, Period: {periodKey}"` |
| Comments | Single-line: `'` prefix; block: `'` on each line | `' Calculate the gross margin percentage` |

### 1.4 Type Usage

```vb
' PREFERRED: Use explicit types
Dim amount As Decimal = 0D
Dim entityName As String = String.Empty
Dim isValid As Boolean = False
Dim recordCount As Integer = 0
Dim entityList As New List(Of String)
Dim mappingTable As New Dictionary(Of String, String)

' AVOID: Implicit typing or Object type
Dim amount = 0              ' Avoid -- type not obvious
Dim data As Object = Nothing ' Avoid -- lose type safety
```

---

## 2. Common OneStream API Patterns

### 2.1 GetDataCell -- Reading a Single Data Value

```vb
' Read a single data cell value
Dim dataCellPk As New DataCellPk(
    si.WorkflowClusterPk.ProfileKey,   ' Profile
    si.WorkflowClusterPk.ScenarioKey,  ' Scenario
    si.WorkflowClusterPk.TimeKey,      ' Time
    entityKey,                          ' Entity
    accountKey,                         ' Account
    flowKey,                            ' Flow
    consolidationKey,                   ' Consolidation
    ud1Key,                             ' UD1 (Product)
    ud2Key,                             ' UD2 (Customer)
    ud3Key,                             ' UD3 (Department)
    ud4Key,                             ' UD4 (Project)
    ud5Key,                             ' UD5 (Intercompany)
    ud6Key                              ' UD6 (Plant)
)

Dim dataCell As DataCell = api.Data.GetDataCell(si, dataCellPk)
Dim cellValue As Decimal = dataCell.CellAmount

' Check cell status
If dataCell.CellStatus = DataCellStatus.NoData Then
    ' Handle no data scenario
End If
```

### 2.2 SetDataCell -- Writing a Single Data Value

```vb
' Write a single data cell value
Dim dataCellPk As New DataCellPk(
    si.WorkflowClusterPk.ProfileKey,
    si.WorkflowClusterPk.ScenarioKey,
    si.WorkflowClusterPk.TimeKey,
    entityKey, accountKey, flowKey, consolidationKey,
    ud1Key, ud2Key, ud3Key, ud4Key, ud5Key, ud6Key
)

api.Data.SetDataCell(
    si,
    dataCellPk,
    amount,                  ' Decimal amount to write
    False                    ' False = add to existing; True = replace
)
```

### 2.3 GetDataBuffer -- Bulk Data Retrieval

```vb
' Define the data buffer scope (POV for retrieval)
Dim pov As New PointOfView(
    si.WorkflowClusterPk.ProfileKey,
    si.WorkflowClusterPk.ScenarioKey,
    si.WorkflowClusterPk.TimeKey,
    entityDimPk,
    accountDimPk,
    flowDimPk,
    consolidationDimPk,
    ud1DimPk, ud2DimPk, ud3DimPk, ud4DimPk, ud5DimPk, ud6DimPk
)

' Retrieve the data buffer
Dim dataBuffer As DataBuffer = api.Data.GetDataBufferUsingPov(si, pov)

' Iterate through data cells in the buffer
If dataBuffer IsNot Nothing AndAlso dataBuffer.DataBufferCells IsNot Nothing Then
    For Each cell As DataBufferCell In dataBuffer.DataBufferCells.Values
        Dim amount As Decimal = cell.CellAmount
        Dim accountId As Integer = cell.DataCellPk.AccountId
        ' Process each cell
    Next
End If
```

### 2.4 SetDataBuffer -- Bulk Data Write

```vb
' Create a new data buffer for writing
Dim dataBuffer As New DataBuffer()
dataBuffer.DataBufferCells = New DataBufferCellCollection()

' Add cells to the buffer
For Each item In calculatedItems
    Dim cellPk As New DataCellPk(
        si.WorkflowClusterPk.ProfileKey,
        si.WorkflowClusterPk.ScenarioKey,
        si.WorkflowClusterPk.TimeKey,
        item.EntityKey, item.AccountKey, item.FlowKey,
        item.ConsolidationKey,
        item.UD1Key, item.UD2Key, item.UD3Key,
        item.UD4Key, item.UD5Key, item.UD6Key
    )

    Dim cell As New DataBufferCell(cellPk)
    cell.CellAmount = item.Amount
    dataBuffer.DataBufferCells.Add(cellPk.GetKey(), cell)
Next

' Write the entire buffer at once
api.Data.SetDataBuffer(si, dataBuffer)
```

### 2.5 Calculate -- Triggering Calculations

```vb
' Trigger a calculation for a specific entity/scenario/period
api.Calculate.ExecuteCalculation(
    si,
    scenarioKey,
    timeKey,
    entityKey,
    CalculationMode.Standard
)
```

### 2.6 Dimension Member Lookups

```vb
' Get member info by name
Dim entityMember As MemberInfo = BRApi.Finance.Members.GetMemberInfo(
    si,
    DimType.Entity.Id,
    "Plant_US01_Detroit"
)
Dim entityKey As Integer = entityMember.MemberId

' Get member name by key
Dim memberName As String = BRApi.Finance.Members.GetMemberName(
    si,
    DimType.Account.Id,
    accountKey
)

' Get children of a parent member
Dim childMembers As List(Of MemberInfo) = BRApi.Finance.Members.GetChildren(
    si,
    DimType.Entity.Id,
    "NA_NorthAmerica"
)

' Get base (leaf) members
Dim baseMembers As List(Of MemberInfo) = BRApi.Finance.Members.GetBaseMembers(
    si,
    DimType.Entity.Id,
    "Total_Company"
)
```

### 2.7 SQL Data Access

```vb
' Execute a SQL query against the application database
Dim sql As String = "SELECT SourceValue, TargetMember FROM DIM_MAPPING " &
                    "WHERE SourceSystem = @source AND TargetDimension = @dim AND IsActive = 1"

Using dbConn As DbConnInfo = BRApi.Database.CreateApplicationDbConnInfo(si)
    Dim dt As DataTable = BRApi.Database.ExecuteSql(
        dbConn,
        sql,
        New List(Of DbParamInfo) From {
            New DbParamInfo("@source", DbType.String, "SAP"),
            New DbParamInfo("@dim", DbType.String, "Account")
        },
        False
    )

    For Each row As DataRow In dt.Rows
        Dim sourceValue As String = row("SourceValue").ToString()
        Dim targetMember As String = row("TargetMember").ToString()
        ' Process mapping row
    Next
End Using
```

---

## 3. Error Handling Best Practices

### 3.1 Standard Try/Catch/Finally Pattern

```vb
Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals,
                     ByVal api As FinanceRulesApi, ByVal args As CalculateRuleArgs) As Object

    Dim processedCount As Integer = 0
    Dim errorCount As Integer = 0

    Try
        BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} Starting execution...")

        ' Main processing logic
        For Each entityName In entityList
            Try
                ProcessEntity(si, api, entityName)
                processedCount += 1
            Catch entityEx As Exception
                errorCount += 1
                BRApi.ErrorLog.LogMessage(si,
                    $"{LOG_PREFIX} Error processing entity {entityName}: {entityEx.Message}")
                ' Continue processing other entities
            End Try
        Next

        If errorCount > 0 Then
            BRApi.ErrorLog.LogMessage(si,
                $"{LOG_PREFIX} Completed with {errorCount} errors out of {processedCount + errorCount} entities.")
        Else
            BRApi.ErrorLog.LogMessage(si,
                $"{LOG_PREFIX} Completed successfully. Processed {processedCount} entities.")
        End If

    Catch ex As Exception
        ' Critical error -- entire process failed
        BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} CRITICAL ERROR: {ex.Message}")
        BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} Stack Trace: {ex.StackTrace}")
        Throw  ' Re-throw to ensure OneStream marks the execution as failed

    Finally
        ' Cleanup resources
        BRApi.ErrorLog.LogMessage(si,
            $"{LOG_PREFIX} Execution complete. Processed: {processedCount}, Errors: {errorCount}")
    End Try

    Return Nothing
End Function
```

### 3.2 Error Classification Guidelines

| Error Type | Handling | Example |
|-----------|---------|---------|
| Validation Error | Log warning; skip record; continue processing | Unmapped account code |
| Entity-Level Error | Log error; skip entity; continue to next entity | Missing data for one plant |
| Data Error | Log error; add to error staging; continue processing | Invalid numeric value |
| Configuration Error | Log critical; halt processing; notify admin | Missing mapping table |
| System Error | Log critical; halt processing; throw exception | Database connection failure |
| Transient Error | Retry with backoff; log warning after each retry | API timeout |

### 3.3 Custom Exception Pattern

```vb
' Define custom exceptions for better error classification
Public Class DataValidationException
    Inherits Exception

    Public Property EntityName As String
    Public Property AccountName As String
    Public Property ValidationRule As String

    Public Sub New(message As String, entityName As String, accountName As String, validationRule As String)
        MyBase.New(message)
        Me.EntityName = entityName
        Me.AccountName = accountName
        Me.ValidationRule = validationRule
    End Sub
End Class

' Usage
If Not IsTrialBalanced(entityData) Then
    Throw New DataValidationException(
        $"Trial balance out of balance by {variance:N2}",
        currentEntity, "Total_Accounts", "VAL_TrialBalanceCheck"
    )
End If
```

---

## 4. Logging Patterns

### 4.1 Standard Logging Pattern

```vb
' Always prefix log messages with the rule name
Private Const LOG_PREFIX As String = "[CR_COGSAllocation]"

' Log levels (by convention, since OneStream uses a single log method)
' PREFIX + level indicator:
BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} INFO: Starting COGS allocation for {entityName}")
BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} DEBUG: Processing {recordCount} records")
BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} WARN: No production data found for {productCode}")
BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} ERROR: Failed to allocate - {ex.Message}")
BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} FATAL: Cannot continue - database connection lost")
```

### 4.2 Execution Summary Logging

Every rule should log an execution summary at completion:

```vb
' At the end of processing
Dim elapsed As TimeSpan = DateTime.Now - startTime
BRApi.ErrorLog.LogMessage(si, String.Format(
    "{0} SUMMARY: Entities={1}, Records={2}, Errors={3}, Duration={4:mm\:ss\.fff}",
    LOG_PREFIX, entityCount, recordCount, errorCount, elapsed
))
```

### 4.3 Data Movement Logging

For rules that move data between cubes:

```vb
BRApi.ErrorLog.LogMessage(si, String.Format(
    "{0} DATA MOVEMENT: Source={1}, Target={2}, Cells={3}, SourceTotal={4:N2}, TargetTotal={5:N2}, Variance={6:N2}",
    LOG_PREFIX, sourceCube, targetCube, cellCount, sourceTotal, targetTotal, variance
))
```

---

## 5. Performance Optimization

### 5.1 Batch Operations

```vb
' PREFERRED: Use DataBuffer for bulk read/write
Dim dataBuffer As DataBuffer = api.Data.GetDataBufferUsingPov(si, pov)
' Process all cells in memory
' Write all results with a single SetDataBuffer call
api.Data.SetDataBuffer(si, resultBuffer)

' AVOID: Reading/writing cells one at a time in a loop
For Each account In accountList
    ' BAD: Individual GetDataCell calls in a loop
    Dim cell As DataCell = api.Data.GetDataCell(si, cellPk)
    ' BAD: Individual SetDataCell calls in a loop
    api.Data.SetDataCell(si, cellPk, amount, False)
Next
```

### 5.2 Minimize API Calls

```vb
' PREFERRED: Cache member lookups outside the loop
Dim memberCache As New Dictionary(Of String, Integer)
For Each memberName In memberNames
    Dim info As MemberInfo = BRApi.Finance.Members.GetMemberInfo(si, DimType.Account.Id, memberName)
    memberCache.Add(memberName, info.MemberId)
Next

' Use cache inside the processing loop
For Each entity In entities
    For Each account In accounts
        Dim accountKey As Integer = memberCache(account)
        ' Use cached key -- no API call needed
    Next
Next

' AVOID: Looking up the same member repeatedly
For Each entity In entities
    For Each account In accounts
        ' BAD: Repeated API call for the same member in every iteration
        Dim info As MemberInfo = BRApi.Finance.Members.GetMemberInfo(si, DimType.Account.Id, account)
    Next
Next
```

### 5.3 SQL Query Optimization

```vb
' PREFERRED: Parameterized queries with specific columns
Dim sql As String = "SELECT SourceValue, TargetMember FROM DIM_MAPPING " &
                    "WHERE SourceSystem = @source AND IsActive = 1"

' AVOID: SELECT * and unparameterized queries
Dim sql As String = "SELECT * FROM DIM_MAPPING WHERE SourceSystem = 'SAP'"
```

### 5.4 Memory Management

```vb
' PREFERRED: Dispose of large objects when done
Dim dataBuffer As DataBuffer = api.Data.GetDataBufferUsingPov(si, pov)
Try
    ' Process the buffer
Finally
    dataBuffer = Nothing  ' Allow garbage collection
End Try

' PREFERRED: Use Using blocks for disposable resources
Using dbConn As DbConnInfo = BRApi.Database.CreateApplicationDbConnInfo(si)
    ' Database operations
End Using  ' Connection disposed automatically
```

### 5.5 Performance Benchmarks

| Operation | Target Duration | Optimization Notes |
|-----------|----------------|-------------------|
| Single GetDataCell | < 5 ms | Cache frequently accessed cells |
| DataBuffer (10,000 cells) | < 500 ms | Preferred over individual cell reads |
| SetDataBuffer (10,000 cells) | < 1 second | Bulk write always preferred |
| Member lookup | < 10 ms | Cache results for repeated lookups |
| SQL query (mapping table) | < 200 ms | Indexed columns, parameterized queries |
| Full entity calculation | < 5 seconds | Batch processing, skip empty intersections |
| Full consolidation (all entities) | < 15 minutes | Parallel by region |

---

## 6. Testing Approach

### 6.1 Unit Testing

Each business rule is tested individually in the DEV environment:

| Test Type | Description | Who | When |
|-----------|-------------|-----|------|
| Compilation Test | Rule compiles without errors | Developer | After every code change |
| Functional Test | Rule produces correct results for known inputs | Developer | After compilation |
| Boundary Test | Test edge cases: zero values, null entities, empty periods | Developer | After functional tests pass |
| Negative Test | Test with invalid inputs: bad member names, missing data | Developer | After boundary tests pass |
| Performance Test | Measure execution time against benchmarks | Developer | After functional correctness confirmed |

### 6.2 Test Data Strategy

```
DEV Environment:
- 5 representative entities (1 per region)
- 3 periods of test data
- Full account structure with sample data
- Known-good test cases with expected results documented

QA Environment:
- Full entity set (production mirror)
- 12 months of historical data
- Production-volume test data
- UAT test scripts with expected outcomes

PROD Environment:
- No test data
- Validated through parallel run with legacy system
```

### 6.3 Test Case Template

```
Test Case ID:    TC_CR_COGSAllocation_001
Rule:            CR_COGSAllocation
Description:     Verify COGS allocation to products based on production volume
Preconditions:   Plant_US01_Detroit has COGS data loaded; MES production volumes loaded
Test Data:
  - COGS_DirectMaterial total = $500,000
  - Product A volume = 6,000 units (60%)
  - Product B volume = 4,000 units (40%)
Expected Result:
  - Product A COGS_DirectMaterial = $300,000
  - Product B COGS_DirectMaterial = $200,000
  - Total allocated = $500,000 (no variance)
Actual Result:   [To be completed during testing]
Status:          [Pass/Fail]
Tested By:       [Name]
Test Date:       [Date]
```

### 6.4 Integration Testing

After individual rules pass unit tests, integration testing validates end-to-end flows:

| Test Scenario | Rules Involved | Validation |
|--------------|---------------|------------|
| Daily Data Load | CN_SAP_*, EH_DataQualityValidation, VAL_TrialBalanceCheck | Data matches source; no validation errors |
| Monthly Consolidation | FR_* (all), CR_CashFlowCalc | Consolidated balances tie; CTA balances; IC eliminated |
| Budget Process | CR_BudgetSeeding, CR_GrowthDrivers, CR_PlanDataPush | Budget data flows from Planning to Finance cube correctly |
| HR to Finance Flow | CR_CompCalc, CR_BenefitsCalc, CR_LaborCostPush | Labor costs in Finance match HR cube totals |
| Cross-Cube Reconciliation | CR_*Push rules, VAL_CrossCubeReconciliation | Source and target totals match within tolerance |

---

## 7. Code Review Checklist

### 7.1 Pre-Review (Developer Self-Check)

Before submitting a rule for code review, the developer must confirm:

- [ ] Rule compiles without errors or warnings
- [ ] Rule name follows naming conventions (`BR_NamingConventions.md`)
- [ ] File header comment block is complete (name, type, description, author, date, dependencies)
- [ ] All unit tests pass
- [ ] Performance is within benchmarks
- [ ] No hardcoded member names or magic numbers (use constants or configuration)

### 7.2 Code Review Checklist

| # | Category | Check Item | Severity |
|---|----------|-----------|----------|
| 1 | Naming | Rule name follows prefix convention (FR_, CR_, CN_, etc.) | High |
| 2 | Naming | Variables use correct casing (camelCase local, PascalCase methods) | Medium |
| 3 | Naming | Constants use UPPER_SNAKE_CASE | Medium |
| 4 | Structure | Code follows standard template (header, imports, namespace, class, Main) | High |
| 5 | Structure | Methods are short (< 50 lines) and single-purpose | Medium |
| 6 | Structure | No deeply nested conditionals (max 3 levels) | Medium |
| 7 | Error Handling | Try/Catch wraps all external calls (API, SQL, file I/O) | High |
| 8 | Error Handling | Errors are logged with rule name prefix and meaningful context | High |
| 9 | Error Handling | Critical errors re-throw; non-critical errors allow continuation | High |
| 10 | Logging | Execution start, completion, and summary are logged | Medium |
| 11 | Logging | Log messages include entity/period context where applicable | Medium |
| 12 | Performance | Bulk operations (DataBuffer) used instead of single-cell loops | High |
| 13 | Performance | Member lookups are cached outside inner loops | High |
| 14 | Performance | SQL queries are parameterized and use specific column lists | Medium |
| 15 | Performance | Using blocks wrap disposable resources | Medium |
| 16 | Security | No credentials or connection strings in code (use Credential Vault) | Critical |
| 17 | Security | No SQL injection vulnerabilities (use parameterized queries) | Critical |
| 18 | Security | Sensitive data not written to logs (passwords, PII) | Critical |
| 19 | Logic | Business logic matches requirements/specification | High |
| 20 | Logic | Edge cases handled (null, zero, empty, missing data) | High |
| 21 | Logic | Sign conventions correct (revenue credit, expense debit) | High |
| 22 | Maintainability | No hardcoded dimension member names in logic (use mapping tables) | Medium |
| 23 | Maintainability | Comments explain "why" not "what" for complex logic | Medium |
| 24 | Maintainability | No dead code or commented-out blocks | Low |

### 7.3 Review Outcomes

| Outcome | Criteria | Action |
|---------|---------|--------|
| Approved | No Critical/High issues; Minor issues documented | Merge to DEV branch |
| Approved with Changes | No Critical issues; High issues have agreed remediation | Developer fixes, reviewer verifies, then merge |
| Rejected | Critical issues found (security, data integrity risk) | Developer reworks; re-review required |

---

## 8. Common Pitfalls and Solutions

### 8.1 Hardcoded Member Names

**Pitfall:** Embedding dimension member names directly in calculation logic makes the rule fragile and hard to maintain.

```vb
' BAD: Hardcoded member names
If accountName = "REV_Product_Sales" Then
    ' Calculate commission at 5%
    commission = amount * 0.05D
End If
```

**Solution:** Use configuration tables or account properties.

```vb
' GOOD: Use configuration table
Dim commissionRate As Decimal = GetCommissionRate(si, accountName)
If commissionRate > 0 Then
    commission = amount * commissionRate
End If
```

### 8.2 Missing Null Checks

**Pitfall:** OneStream API calls can return Nothing for non-existent members or empty data cells.

```vb
' BAD: No null check
Dim memberInfo As MemberInfo = BRApi.Finance.Members.GetMemberInfo(si, DimType.Entity.Id, entityName)
Dim memberId As Integer = memberInfo.MemberId  ' NullReferenceException if member not found
```

**Solution:** Always check for Nothing.

```vb
' GOOD: Null check before use
Dim memberInfo As MemberInfo = BRApi.Finance.Members.GetMemberInfo(si, DimType.Entity.Id, entityName)
If memberInfo Is Nothing Then
    BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} WARN: Entity '{entityName}' not found in dimension")
    Continue For
End If
Dim memberId As Integer = memberInfo.MemberId
```

### 8.3 Incorrect Sign Conventions

**Pitfall:** Revenue accounts have credit-normal balances (negative in the database) but display as positive. Failing to account for this causes doubled or zeroed values.

```vb
' BAD: Assuming all amounts are positive
grossMargin = revenueAmount + cogsAmount  ' Wrong: revenue is negative, COGS is positive
```

**Solution:** Understand account type sign conventions.

```vb
' GOOD: Apply sign convention awareness
' Revenue is stored as negative (credit); COGS as positive (debit)
' Gross Margin = Revenue (flip sign) - COGS
grossMargin = Math.Abs(revenueAmount) - cogsAmount

' Or, use the account type to determine sign
If accountType = AccountType.Revenue Then
    displayAmount = -1 * storedAmount  ' Flip for display
End If
```

### 8.4 Infinite Recursion in Calculate Rules

**Pitfall:** A calculate rule that writes data to the same cube/intersection that triggered it can cause infinite recursion.

```vb
' BAD: Writing to the triggering intersection
' If this rule triggers on Account "GM_GrossMargin" and writes to it, infinite loop
api.Data.SetDataCell(si, gmCellPk, grossMarginAmount, True)
```

**Solution:** Use the `IsCalculating` flag or write to a different intersection (different Flow or Consolidation member).

```vb
' GOOD: Check if already calculating, or write to a designated calculation target
If args.IsOriginalRequest Then
    ' Only execute on the original trigger, not on cascading recalculations
    api.Data.SetDataCell(si, gmCellPk, grossMarginAmount, True)
End If
```

### 8.5 Unhandled Period Boundaries

**Pitfall:** Calculations that reference prior period data fail at the first period of loaded data or at fiscal year boundaries.

```vb
' BAD: Blindly getting prior period
Dim priorPeriodKey As Integer = timeKey - 1  ' Wrong at year boundary (Dec -> Jan)
```

**Solution:** Use OneStream's time navigation API.

```vb
' GOOD: Use API for period navigation
Dim priorPeriodInfo As TimeMemberInfo = BRApi.Finance.Time.GetPriorPeriod(si, timeKey)
If priorPeriodInfo IsNot Nothing Then
    Dim priorPeriodKey As Integer = priorPeriodInfo.MemberId
    ' Safe to retrieve prior period data
Else
    BRApi.ErrorLog.LogMessage(si, $"{LOG_PREFIX} WARN: No prior period for timeKey={timeKey}")
End If
```

### 8.6 Large Data Volumes Causing Timeouts

**Pitfall:** Processing all entities sequentially in a single rule execution can exceed the OneStream execution timeout (default 30 minutes).

**Solution:** Batch processing with progress tracking.

```vb
' GOOD: Process in batches with progress logging
Dim batchSize As Integer = 10
Dim totalEntities As Integer = entityList.Count
Dim batchCount As Integer = CInt(Math.Ceiling(totalEntities / batchSize))

For batchIndex As Integer = 0 To batchCount - 1
    Dim batch = entityList.Skip(batchIndex * batchSize).Take(batchSize)

    For Each entityName In batch
        ProcessEntity(si, api, entityName)
    Next

    BRApi.ErrorLog.LogMessage(si,
        $"{LOG_PREFIX} Progress: Batch {batchIndex + 1}/{batchCount} complete " &
        $"({Math.Min((batchIndex + 1) * batchSize, totalEntities)}/{totalEntities} entities)")
Next
```

### 8.7 Not Clearing Data Before Reload

**Pitfall:** Loading data additively without first clearing the target intersection causes data doubling on re-runs.

```vb
' BAD: Loading without clearing
For Each record In dataRecords
    api.Data.SetDataCell(si, cellPk, record.Amount, False)  ' False = add mode
Next
```

**Solution:** Clear the target range before loading.

```vb
' GOOD: Clear target intersection first, then load
api.Data.ClearData(si, clearPov)  ' Clear the target scope

For Each record In dataRecords
    api.Data.SetDataCell(si, cellPk, record.Amount, True)  ' True = replace mode
Next
```

### 8.8 Ignoring Currency in Cross-Entity Calculations

**Pitfall:** Summing amounts across entities without considering that each entity may report in a different local currency.

**Solution:** Always work with translated (common currency) amounts when aggregating across entities, or explicitly convert using the rate table.

```vb
' GOOD: Use the translated consolidation member for cross-entity aggregation
Dim translatedConKey As Integer = BRApi.Finance.Members.GetMemberId(
    si, DimType.Consolidation.Id, "CON_Translated"
)
' Retrieve data at CON_Translated to get amounts in reporting currency
```

---

*End of Document*
