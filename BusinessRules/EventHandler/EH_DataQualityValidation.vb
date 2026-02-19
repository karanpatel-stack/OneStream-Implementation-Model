'------------------------------------------------------------------------------------------------------------
' EH_DataQualityValidation
' Event Handler Business Rule - Pre-Submission Data Quality Validation
'
' Purpose:  Performs comprehensive data quality checks before allowing workflow submission.
'           Validates trial balance integrity, balance sheet equation, required account
'           population, inventory sign checks, account-level reasonableness, and
'           statistical data completeness. Returns structured validation results with
'           pass/fail status and detailed error messages.
'
' Validation Suite:
'   1. Trial Balance      - Total debits must equal total credits
'   2. Balance Sheet      - Assets = Liabilities + Equity
'   3. Required Accounts  - Revenue and COGS must have non-zero values
'   4. Inventory Sign     - No negative inventory balances allowed
'   5. Account Reasonableness - Revenue not negative, expenses not positive (for normal sign)
'   6. Statistical Data   - Headcount and production volumes must be populated
'
' Critical validations block submission; warnings are logged but do not block.
'
' Scope:    Event Handler
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.EventHandler.EH_DataQualityValidation

    Public Class MainClass

        '--- Tolerance for trial balance check (allows for minor rounding differences) ---
        Private Const TB_TOLERANCE As Double = 0.01
        '--- Tolerance for balance sheet check ---
        Private Const BS_TOLERANCE As Double = 1.0

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As EventHandlerArgs) As Object
            Try
                '--- Only execute during the pre-submission workflow event ---
                If args.EventHandlerType <> EventHandlerType.WorkflowAction Then
                    Return Nothing
                End If

                '--- Extract POV context from the event arguments ---
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)

                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                BRApi.ErrorLog.LogMessage(si, "EH_DataQualityValidation: Starting validation for " &
                    scenarioName & "/" & timeName & "/" & entityName)

                '--- Initialize the validation results collector ---
                Dim results As New List(Of ValidationResult)

                '--- Execute each validation check ---
                ValidateTrialBalance(si, scenarioName, timeName, entityName, results)
                ValidateBalanceSheet(si, scenarioName, timeName, entityName, results)
                ValidateRequiredAccounts(si, scenarioName, timeName, entityName, results)
                ValidateInventorySign(si, scenarioName, timeName, entityName, results)
                ValidateAccountReasonableness(si, scenarioName, timeName, entityName, results)
                ValidateStatisticalData(si, scenarioName, timeName, entityName, results)

                '--- Evaluate overall results ---
                Dim hasCriticalFailures As Boolean = False
                Dim warningCount As Integer = 0
                Dim passCount As Integer = 0

                For Each result As ValidationResult In results
                    Select Case result.Severity
                        Case "CRITICAL"
                            hasCriticalFailures = True
                            BRApi.ErrorLog.LogMessage(si, "  CRITICAL FAIL: " & result.RuleName & " - " & result.Message)
                        Case "WARNING"
                            warningCount += 1
                            BRApi.ErrorLog.LogMessage(si, "  WARNING: " & result.RuleName & " - " & result.Message)
                        Case "PASS"
                            passCount += 1
                    End Select
                Next

                '--- Build summary message ---
                Dim summary As String = String.Format(
                    "Data Quality Validation Complete: {0} passed, {1} warnings, {2} critical failures.",
                    passCount, warningCount, If(hasCriticalFailures, "HAS", "0"))

                BRApi.ErrorLog.LogMessage(si, "EH_DataQualityValidation: " & summary)

                '--- Block submission if critical validations failed ---
                If hasCriticalFailures Then
                    Dim errorMessages As New List(Of String)
                    For Each result As ValidationResult In results
                        If result.Severity = "CRITICAL" Then
                            errorMessages.Add(result.RuleName & ": " & result.Message)
                        End If
                    Next
                    Dim blockMessage As String = "Submission blocked due to data quality failures:" & Environment.NewLine &
                        String.Join(Environment.NewLine, errorMessages.ToArray())

                    ' Returning False or throwing signals OneStream to block the workflow action
                    Throw New XFException(si, "EH_DataQualityValidation", blockMessage)
                End If

                Return Nothing

            Catch ex As XFException
                Throw
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "EH_DataQualityValidation", ex.Message))
            End Try
        End Function

        '--- Validation 1: Trial Balance ---
        Private Sub ValidateTrialBalance(ByVal si As SessionInfo, ByVal scenario As String,
                                          ByVal time As String, ByVal entity As String,
                                          ByVal results As List(Of ValidationResult))
            Try
                Dim totalDebits As Double = GetDataValue(si, scenario, time, entity, "A#TotalDebits")
                Dim totalCredits As Double = GetDataValue(si, scenario, time, entity, "A#TotalCredits")
                Dim difference As Double = Math.Abs(totalDebits - totalCredits)

                If difference > TB_TOLERANCE Then
                    results.Add(New ValidationResult("TrialBalance", "CRITICAL",
                        String.Format("Trial balance out of balance by {0:N2}. Debits={1:N2}, Credits={2:N2}",
                            difference, totalDebits, totalCredits)))
                Else
                    results.Add(New ValidationResult("TrialBalance", "PASS", "Trial balance in balance."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("TrialBalance", "WARNING",
                    "Unable to validate trial balance: " & ex.Message))
            End Try
        End Sub

        '--- Validation 2: Balance Sheet Equation ---
        Private Sub ValidateBalanceSheet(ByVal si As SessionInfo, ByVal scenario As String,
                                          ByVal time As String, ByVal entity As String,
                                          ByVal results As List(Of ValidationResult))
            Try
                Dim totalAssets As Double = GetDataValue(si, scenario, time, entity, "A#TotalAssets")
                Dim totalLiabilities As Double = GetDataValue(si, scenario, time, entity, "A#TotalLiabilities")
                Dim totalEquity As Double = GetDataValue(si, scenario, time, entity, "A#TotalEquity")

                ' Balance sheet equation: Assets = Liabilities + Equity
                Dim difference As Double = Math.Abs(totalAssets - (totalLiabilities + totalEquity))

                If difference > BS_TOLERANCE Then
                    results.Add(New ValidationResult("BalanceSheet", "CRITICAL",
                        String.Format("Balance sheet out of balance by {0:N2}. Assets={1:N2}, L+E={2:N2}",
                            difference, totalAssets, totalLiabilities + totalEquity)))
                Else
                    results.Add(New ValidationResult("BalanceSheet", "PASS", "Balance sheet in balance."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("BalanceSheet", "WARNING",
                    "Unable to validate balance sheet: " & ex.Message))
            End Try
        End Sub

        '--- Validation 3: Required Accounts ---
        Private Sub ValidateRequiredAccounts(ByVal si As SessionInfo, ByVal scenario As String,
                                              ByVal time As String, ByVal entity As String,
                                              ByVal results As List(Of ValidationResult))
            Try
                '--- Define accounts that must have non-zero values ---
                Dim requiredAccounts As New Dictionary(Of String, String) From {
                    {"A#Revenue", "Revenue"},
                    {"A#COGS", "Cost of Goods Sold"},
                    {"A#GrossProfit", "Gross Profit"},
                    {"A#OperatingExpenses", "Operating Expenses"}
                }

                Dim missingAccounts As New List(Of String)

                For Each kvp As KeyValuePair(Of String, String) In requiredAccounts
                    Dim value As Double = GetDataValue(si, scenario, time, entity, kvp.Key)
                    If value = 0 Then
                        missingAccounts.Add(kvp.Value)
                    End If
                Next

                If missingAccounts.Count > 0 Then
                    results.Add(New ValidationResult("RequiredAccounts", "CRITICAL",
                        "Missing required account values: " & String.Join(", ", missingAccounts.ToArray())))
                Else
                    results.Add(New ValidationResult("RequiredAccounts", "PASS", "All required accounts populated."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("RequiredAccounts", "WARNING",
                    "Unable to validate required accounts: " & ex.Message))
            End Try
        End Sub

        '--- Validation 4: Inventory Sign Check ---
        Private Sub ValidateInventorySign(ByVal si As SessionInfo, ByVal scenario As String,
                                           ByVal time As String, ByVal entity As String,
                                           ByVal results As List(Of ValidationResult))
            Try
                Dim inventoryAccounts As String() = {
                    "A#RawMaterials", "A#WorkInProcess", "A#FinishedGoods", "A#TotalInventory"
                }

                Dim negativeAccounts As New List(Of String)

                For Each acct As String In inventoryAccounts
                    Dim value As Double = GetDataValue(si, scenario, time, entity, acct)
                    If value < 0 Then
                        negativeAccounts.Add(acct & " (" & value.ToString("N2") & ")")
                    End If
                Next

                If negativeAccounts.Count > 0 Then
                    results.Add(New ValidationResult("InventorySign", "CRITICAL",
                        "Negative inventory balances found: " & String.Join(", ", negativeAccounts.ToArray())))
                Else
                    results.Add(New ValidationResult("InventorySign", "PASS", "No negative inventory balances."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("InventorySign", "WARNING",
                    "Unable to validate inventory signs: " & ex.Message))
            End Try
        End Sub

        '--- Validation 5: Account Reasonableness ---
        Private Sub ValidateAccountReasonableness(ByVal si As SessionInfo, ByVal scenario As String,
                                                   ByVal time As String, ByVal entity As String,
                                                   ByVal results As List(Of ValidationResult))
            Try
                Dim issues As New List(Of String)

                ' Revenue should not be negative (credit balance = positive in OneStream for revenue)
                Dim revenue As Double = GetDataValue(si, scenario, time, entity, "A#Revenue")
                If revenue < 0 Then
                    issues.Add("Revenue is negative (" & revenue.ToString("N2") & ")")
                End If

                ' COGS should not be positive (debit balance = positive, should be negative/cost)
                ' Note: depends on sign convention; here we assume COGS stored as positive cost
                Dim cogs As Double = GetDataValue(si, scenario, time, entity, "A#COGS")
                If cogs < 0 Then
                    issues.Add("COGS is negative (" & cogs.ToString("N2") & ") - possible sign error")
                End If

                ' Cash should not be significantly negative (overdraft check)
                Dim cash As Double = GetDataValue(si, scenario, time, entity, "A#CashAndEquivalents")
                If cash < -1000000 Then
                    issues.Add("Cash is significantly negative (" & cash.ToString("N2") & ")")
                End If

                If issues.Count > 0 Then
                    results.Add(New ValidationResult("AccountReasonableness", "WARNING",
                        "Reasonableness concerns: " & String.Join("; ", issues.ToArray())))
                Else
                    results.Add(New ValidationResult("AccountReasonableness", "PASS", "All accounts pass reasonableness checks."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("AccountReasonableness", "WARNING",
                    "Unable to validate account reasonableness: " & ex.Message))
            End Try
        End Sub

        '--- Validation 6: Statistical Data Completeness ---
        Private Sub ValidateStatisticalData(ByVal si As SessionInfo, ByVal scenario As String,
                                             ByVal time As String, ByVal entity As String,
                                             ByVal results As List(Of ValidationResult))
            Try
                Dim missingStats As New List(Of String)

                '--- Define required statistical accounts ---
                Dim statAccounts As New Dictionary(Of String, String) From {
                    {"A#STAT_Headcount", "Headcount"},
                    {"A#STAT_FTE", "FTE Count"},
                    {"A#STAT_ProductionVolume", "Production Volume"}
                }

                For Each kvp As KeyValuePair(Of String, String) In statAccounts
                    Dim value As Double = GetDataValue(si, scenario, time, entity, kvp.Key)
                    If value = 0 Then
                        missingStats.Add(kvp.Value)
                    End If
                Next

                If missingStats.Count > 0 Then
                    results.Add(New ValidationResult("StatisticalData", "WARNING",
                        "Missing statistical data: " & String.Join(", ", missingStats.ToArray())))
                Else
                    results.Add(New ValidationResult("StatisticalData", "PASS", "All statistical data populated."))
                End If

            Catch ex As Exception
                results.Add(New ValidationResult("StatisticalData", "WARNING",
                    "Unable to validate statistical data: " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Reads a data value from the finance cube for the specified POV intersection.
        ''' </summary>
        Private Function GetDataValue(ByVal si As SessionInfo, ByVal scenario As String,
                                       ByVal time As String, ByVal entity As String,
                                       ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, time, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on any data retrieval error
            End Try
            Return 0
        End Function

        ''' <summary>
        ''' Holds the result of a single validation check.
        ''' </summary>
        Private Class ValidationResult
            Public Property RuleName As String
            Public Property Severity As String   ' "PASS", "WARNING", "CRITICAL"
            Public Property Message As String

            Public Sub New(ByVal ruleName As String, ByVal severity As String, ByVal message As String)
                Me.RuleName = ruleName
                Me.Severity = severity
                Me.Message = message
            End Sub
        End Class

    End Class

End Namespace
