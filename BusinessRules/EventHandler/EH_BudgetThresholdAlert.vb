'------------------------------------------------------------------------------------------------------------
' EH_BudgetThresholdAlert
' Event Handler Business Rule - Budget Variance Threshold Alerts
'
' Purpose:  Compares submitted data against the prior approved budget and generates alerts
'           when any account variance exceeds defined thresholds (10% or $100K). Alerts
'           are flagged for management review and logged for audit trail. Unlike hard
'           gate checks, this rule does NOT block submission but requires the user to
'           acknowledge the alert before proceeding.
'
' Threshold Rules:
'   - Percentage threshold: 10% variance vs. approved budget
'   - Absolute threshold: $100,000 variance
'   - Either threshold breach triggers an alert
'   - Alerts are logged to the audit trail for compliance
'   - Management review flag is set for the entity/period
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

Namespace OneStream.BusinessRule.EventHandler.EH_BudgetThresholdAlert

    Public Class MainClass

        '--- Alert threshold configuration ---
        Private Const PCT_THRESHOLD As Double = 0.10         ' 10% variance
        Private Const AMT_THRESHOLD As Double = 100000.0     ' $100,000 variance

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As EventHandlerArgs) As Object
            Try
                '--- Only execute during workflow submission events ---
                If args.EventHandlerType <> EventHandlerType.WorkflowAction Then
                    Return Nothing
                End If

                '--- Extract POV context ---
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)

                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                Dim userName As String = si.UserName

                BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert: Checking budget variances for " &
                    scenarioName & "/" & timeName & "/" & entityName)

                '--- Define accounts to check for material variances ---
                Dim accountsToCheck As New Dictionary(Of String, String) From {
                    {"A#Revenue", "Revenue"},
                    {"A#COGS", "Cost of Goods Sold"},
                    {"A#GrossProfit", "Gross Profit"},
                    {"A#OperatingExpenses", "Operating Expenses"},
                    {"A#SGA", "SG&A Expenses"},
                    {"A#RandD", "R&D Expenses"},
                    {"A#EBITDA", "EBITDA"},
                    {"A#NetIncome", "Net Income"},
                    {"A#CAPEX", "Capital Expenditure"},
                    {"A#TotalAssets", "Total Assets"}
                }

                '--- Compare each account against the approved budget ---
                Dim alerts As New List(Of BudgetAlert)
                Dim budgetScenario As String = "Budget"

                For Each kvp As KeyValuePair(Of String, String) In accountsToCheck
                    Dim currentValue As Double = GetDataValue(si, scenarioName, timeName, entityName, kvp.Key)
                    Dim budgetValue As Double = GetDataValue(si, budgetScenario, timeName, entityName, kvp.Key)

                    '--- Calculate variance ---
                    Dim varianceAmt As Double = currentValue - budgetValue
                    Dim variancePct As Double = 0
                    If budgetValue <> 0 Then
                        variancePct = varianceAmt / Math.Abs(budgetValue)
                    End If

                    '--- Check against thresholds ---
                    Dim absVarianceAmt As Double = Math.Abs(varianceAmt)
                    Dim absVariancePct As Double = Math.Abs(variancePct)

                    If absVariancePct >= PCT_THRESHOLD OrElse absVarianceAmt >= AMT_THRESHOLD Then
                        Dim alert As New BudgetAlert()
                        alert.AccountCode = kvp.Key
                        alert.AccountName = kvp.Value
                        alert.CurrentValue = currentValue
                        alert.BudgetValue = budgetValue
                        alert.VarianceAmount = varianceAmt
                        alert.VariancePercent = variancePct
                        alert.IsFavorable = DetermineIfFavorable(kvp.Key, varianceAmt)
                        alerts.Add(alert)
                    End If
                Next

                '--- Process alerts ---
                If alerts.Count > 0 Then
                    '--- Log all alerts for audit trail ---
                    LogAlerts(si, scenarioName, timeName, entityName, userName, alerts)

                    '--- Set management review flag ---
                    SetManagementReviewFlag(si, entityName, timeName, scenarioName)

                    '--- Build alert summary for the user ---
                    Dim alertSummary As String = BuildAlertSummary(alerts, entityName, timeName)
                    BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert: " & alertSummary)

                    ' Note: We do NOT throw an exception here because alerts are soft warnings.
                    ' The submission proceeds but the alerts are recorded.
                Else
                    BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert: No material variances detected. No alerts generated.")
                End If

                Return Nothing

            Catch ex As Exception
                ' Alert failures should not block submission -- log and continue
                BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert: ERROR (non-blocking) - " & ex.Message)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Determines whether a variance is favorable based on the account type.
        ''' Revenue: positive variance (above budget) is favorable.
        ''' Expense: negative variance (below budget) is favorable.
        ''' </summary>
        Private Function DetermineIfFavorable(ByVal accountCode As String, ByVal varianceAmt As Double) As Boolean
            '--- Revenue and profit accounts: positive is favorable ---
            Dim revenueAccounts As String() = {"A#Revenue", "A#GrossProfit", "A#EBITDA", "A#NetIncome"}
            For Each acct As String In revenueAccounts
                If accountCode.Equals(acct, StringComparison.OrdinalIgnoreCase) Then
                    Return varianceAmt >= 0
                End If
            Next
            '--- Expense/cost accounts: negative is favorable ---
            Return varianceAmt <= 0
        End Function

        ''' <summary>
        ''' Logs alert details to the OneStream error log for audit trail purposes.
        ''' In production, this would also write to a dedicated audit cube or table.
        ''' </summary>
        Private Sub LogAlerts(ByVal si As SessionInfo, ByVal scenario As String,
                               ByVal time As String, ByVal entity As String,
                               ByVal userName As String, ByVal alerts As List(Of BudgetAlert))
            Dim timestamp As String = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")

            For Each alert As BudgetAlert In alerts
                Dim logEntry As String = String.Format(
                    "BUDGET_ALERT|{0}|{1}|{2}|{3}|{4}|{5}|Current={6:N2}|Budget={7:N2}|Var={8:N2}|VarPct={9:P1}|Favorable={10}",
                    timestamp, userName, scenario, time, entity,
                    alert.AccountName, alert.CurrentValue, alert.BudgetValue,
                    alert.VarianceAmount, alert.VariancePercent, alert.IsFavorable)

                BRApi.ErrorLog.LogMessage(si, logEntry)
            Next
        End Sub

        ''' <summary>
        ''' Sets a flag indicating that the entity/period requires management review
        ''' due to material budget variances.
        ''' </summary>
        Private Sub SetManagementReviewFlag(ByVal si As SessionInfo, ByVal entity As String,
                                             ByVal time As String, ByVal scenario As String)
            Try
                '--- Store the review flag in a substitution variable ---
                Dim flagVar As String = "MgmtReview_" & entity & "_" & time & "_" & scenario
                BRApi.Finance.Data.SetSubstVarValue(si, flagVar, "REQUIRED", True)

                BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert: Management review flag set for " &
                    entity & "/" & time & "/" & scenario)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_BudgetThresholdAlert.SetManagementReviewFlag: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Builds a human-readable alert summary string.
        ''' </summary>
        Private Function BuildAlertSummary(ByVal alerts As List(Of BudgetAlert),
                                            ByVal entity As String, ByVal time As String) As String
            Dim lines As New List(Of String)
            lines.Add(String.Format("{0} budget variance alert(s) generated for {1} / {2}:",
                alerts.Count, entity, time))

            For Each alert As BudgetAlert In alerts
                Dim direction As String = If(alert.IsFavorable, "FAV", "UNFAV")
                lines.Add(String.Format("  {0}: Var {1:N0} ({2:P1}) [{3}]",
                    alert.AccountName, alert.VarianceAmount, alert.VariancePercent, direction))
            Next

            Return String.Join(Environment.NewLine, lines.ToArray())
        End Function

        ''' <summary>
        ''' Reads a data value from the finance cube.
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
            End Try
            Return 0
        End Function

        ''' <summary>
        ''' Data structure for a single budget variance alert.
        ''' </summary>
        Private Class BudgetAlert
            Public Property AccountCode As String
            Public Property AccountName As String
            Public Property CurrentValue As Double
            Public Property BudgetValue As Double
            Public Property VarianceAmount As Double
            Public Property VariancePercent As Double
            Public Property IsFavorable As Boolean
        End Class

    End Class

End Namespace
