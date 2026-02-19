'------------------------------------------------------------------------------------------------------------
' DDA_ExecutiveSummary
' Dashboard DataAdapter Business Rule
' Purpose: Executive dashboard providing KPI tiles with YoY/Budget variances, sparklines, and alerts
'------------------------------------------------------------------------------------------------------------
Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.Linq
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_ExecutiveSummary
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("ExecutiveSummary")
                        dt.Columns.Add("KPIName", GetType(String))
                        dt.Columns.Add("CurrentValue", GetType(Double))
                        dt.Columns.Add("PriorValue", GetType(Double))
                        dt.Columns.Add("BudgetValue", GetType(Double))
                        dt.Columns.Add("Variance", GetType(Double))
                        dt.Columns.Add("VariancePct", GetType(Double))
                        dt.Columns.Add("Trend", GetType(String))
                        dt.Columns.Add("AlertLevel", GetType(String))

                        ' Retrieve dashboard parameters for scenario, time, and entity context
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim priorScenario As String = args.NameValuePairs.XFGetValue("PriorScenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Define the KPIs to retrieve with their account mappings and thresholds
                        Dim kpiDefinitions As New List(Of Tuple(Of String, String, Double, Double))()
                        kpiDefinitions.Add(Tuple.Create("Revenue", "A#Revenue", 0.05, 0.10))
                        kpiDefinitions.Add(Tuple.Create("Gross Margin %", "A#GrossMarginPct", 0.02, 0.05))
                        kpiDefinitions.Add(Tuple.Create("EBITDA", "A#EBITDA", 0.05, 0.10))
                        kpiDefinitions.Add(Tuple.Create("Net Income", "A#NetIncome", 0.05, 0.10))
                        kpiDefinitions.Add(Tuple.Create("Cash Position", "A#CashAndEquivalents", 0.05, 0.10))

                        ' Derive prior year time member
                        Dim priorTimeName As String = GetPriorYearTime(timeName)

                        For Each kpiDef In kpiDefinitions
                            Dim kpiName As String = kpiDef.Item1
                            Dim accountMember As String = kpiDef.Item2
                            Dim yellowThreshold As Double = kpiDef.Item3
                            Dim redThreshold As Double = kpiDef.Item4

                            ' Build POV strings for current, prior year, and budget
                            Dim currentPov As String = String.Format(
                                "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                                scenarioName, timeName, entityName, accountMember)
                            Dim priorPov As String = String.Format(
                                "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                                priorScenario, priorTimeName, entityName, accountMember)
                            Dim budgetPov As String = String.Format(
                                "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                                budgetScenario, timeName, entityName, accountMember)

                            ' Fetch data cells from the finance engine
                            Dim currentValue As Double = GetDataCellValue(si, currentPov)
                            Dim priorValue As Double = GetDataCellValue(si, priorPov)
                            Dim budgetValue As Double = GetDataCellValue(si, budgetPov)

                            ' Calculate variance against budget
                            Dim variance As Double = currentValue - budgetValue
                            Dim variancePct As Double = 0
                            If budgetValue <> 0 Then
                                variancePct = variance / Math.Abs(budgetValue)
                            End If

                            ' Determine 12-month trend direction
                            Dim trend As String = DetermineTrend(si, scenarioName, timeName, entityName, accountMember)

                            ' Evaluate alert level against thresholds
                            Dim alertLevel As String = "Green"
                            Dim absVariancePct As Double = Math.Abs(variancePct)
                            If absVariancePct >= redThreshold Then
                                alertLevel = "Red"
                            ElseIf absVariancePct >= yellowThreshold Then
                                alertLevel = "Yellow"
                            End If

                            ' Unfavorable variance on revenue means below budget is bad
                            If kpiName <> "Gross Margin %" AndAlso variance < 0 AndAlso alertLevel = "Green" Then
                                If absVariancePct >= yellowThreshold Then alertLevel = "Yellow"
                            End If

                            Dim row As DataRow = dt.NewRow()
                            row("KPIName") = kpiName
                            row("CurrentValue") = Math.Round(currentValue, 2)
                            row("PriorValue") = Math.Round(priorValue, 2)
                            row("BudgetValue") = Math.Round(budgetValue, 2)
                            row("Variance") = Math.Round(variance, 2)
                            row("VariancePct") = Math.Round(variancePct, 4)
                            row("Trend") = trend
                            row("AlertLevel") = alertLevel
                            dt.Rows.Add(row)
                        Next

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function GetDataCellValue(ByVal si As SessionInfo, ByVal povString As String) As Double
            Try
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero if data cell retrieval fails
            End Try
            Return 0
        End Function

        Private Function GetPriorYearTime(ByVal timeName As String) As String
            ' Parse year from time name and decrement (e.g., "2024M6" -> "2023M6")
            Try
                Dim yearStr As String = timeName.Substring(0, 4)
                Dim remainder As String = timeName.Substring(4)
                Dim year As Integer = Integer.Parse(yearStr)
                Return (year - 1).ToString() & remainder
            Catch
                Return timeName
            End Try
        End Function

        Private Function DetermineTrend(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As String
            ' Retrieve last 3 months to determine short-term trend direction
            Dim values As New List(Of Double)()
            Dim baseTime As String = timeName

            For i As Integer = 2 To 0 Step -1
                Dim offsetTime As String = GetOffsetTime(baseTime, -i)
                Dim pov As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, offsetTime, entity, account)
                values.Add(GetDataCellValue(si, pov))
            Next

            If values.Count >= 3 Then
                If values(2) > values(1) AndAlso values(1) > values(0) Then Return "Improving"
                If values(2) < values(1) AndAlso values(1) < values(0) Then Return "Declining"
            End If
            Return "Stable"
        End Function

        Private Function GetOffsetTime(ByVal timeName As String, ByVal monthOffset As Integer) As String
            Try
                Dim yearStr As String = timeName.Substring(0, 4)
                Dim monthStr As String = timeName.Substring(5)
                Dim year As Integer = Integer.Parse(yearStr)
                Dim month As Integer = Integer.Parse(monthStr)
                month += monthOffset
                While month <= 0
                    month += 12
                    year -= 1
                End While
                While month > 12
                    month -= 12
                    year += 1
                End While
                Return year.ToString() & "M" & month.ToString()
            Catch
                Return timeName
            End Try
        End Function

    End Class
End Namespace
