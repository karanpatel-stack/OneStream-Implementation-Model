'------------------------------------------------------------------------------------------------------------
' DDA_KPICockpit
' Dashboard DataAdapter Business Rule
' Purpose: Operational KPI dashboard with targets, thresholds, trend analysis, and traffic light status
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_KPICockpit
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("KPICockpit")
                        dt.Columns.Add("KPIName", GetType(String))
                        dt.Columns.Add("Value", GetType(Double))
                        dt.Columns.Add("Target", GetType(Double))
                        dt.Columns.Add("LowerThreshold", GetType(Double))
                        dt.Columns.Add("UpperThreshold", GetType(Double))
                        dt.Columns.Add("Trend", GetType(String))
                        dt.Columns.Add("Status", GetType(String))
                        dt.Columns.Add("Unit", GetType(String))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim targetScenario As String = args.NameValuePairs.XFGetValue("TargetScenario", "Budget")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Define KPI specifications: Name, Account, Unit, LowerThreshPct, UpperThreshPct, HigherIsBetter
                        Dim kpiSpecs As New List(Of KPIDefinition)()
                        kpiSpecs.Add(New KPIDefinition("OEE", "A#OEE_Pct", "%", 0.75, 0.85, True))
                        kpiSpecs.Add(New KPIDefinition("First Pass Yield", "A#FirstPassYield_Pct", "%", 0.90, 0.95, True))
                        kpiSpecs.Add(New KPIDefinition("Scrap Rate", "A#Scrap_Rate_Pct", "%", 0.03, 0.05, False))
                        kpiSpecs.Add(New KPIDefinition("Throughput", "A#Throughput_Units", "units/hr", 80, 100, True))
                        kpiSpecs.Add(New KPIDefinition("On-Time Delivery", "A#OnTimeDelivery_Pct", "%", 0.90, 0.95, True))
                        kpiSpecs.Add(New KPIDefinition("Customer Satisfaction", "A#CSAT_Score", "score", 3.5, 4.0, True))
                        kpiSpecs.Add(New KPIDefinition("Order Fill Rate", "A#OrderFillRate_Pct", "%", 0.92, 0.97, True))
                        kpiSpecs.Add(New KPIDefinition("Inventory Turns", "A#InventoryTurns", "turns", 6, 8, True))
                        kpiSpecs.Add(New KPIDefinition("Days Sales Outstanding", "A#DSO_Days", "days", 35, 45, False))
                        kpiSpecs.Add(New KPIDefinition("Cost Per Unit", "A#Cost_Per_Unit", "$", 12, 15, False))
                        kpiSpecs.Add(New KPIDefinition("Capacity Utilization", "A#Capacity_Utilization_Pct", "%", 0.70, 0.85, True))
                        kpiSpecs.Add(New KPIDefinition("Employee Productivity", "A#RevenuePerFTE", "$K", 150, 200, True))

                        For Each kpi In kpiSpecs
                            ' Fetch the current KPI value
                            Dim currentValue As Double = FetchKPIValue(si, scenarioName, timeName, entityName, kpi.AccountMember)

                            ' Fetch the target value
                            Dim targetValue As Double = FetchKPIValue(si, targetScenario, timeName, entityName, kpi.AccountMember)

                            ' Determine the traffic light status based on thresholds and direction
                            Dim status As String = EvaluateKPIStatus(currentValue, kpi.LowerThreshold, kpi.UpperThreshold, kpi.HigherIsBetter)

                            ' Determine trend by comparing last 3 periods
                            Dim trend As String = CalculateTrend(si, scenarioName, timeName, entityName, kpi.AccountMember, kpi.HigherIsBetter)

                            Dim row As DataRow = dt.NewRow()
                            row("KPIName") = kpi.Name
                            row("Value") = Math.Round(currentValue, 4)
                            row("Target") = Math.Round(targetValue, 4)
                            row("LowerThreshold") = kpi.LowerThreshold
                            row("UpperThreshold") = kpi.UpperThreshold
                            row("Trend") = trend
                            row("Status") = status
                            row("Unit") = kpi.Unit
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

        Private Function EvaluateKPIStatus(ByVal value As Double, ByVal lowerThreshold As Double, ByVal upperThreshold As Double, ByVal higherIsBetter As Boolean) As String
            If higherIsBetter Then
                ' For metrics where higher is better: green above upper, yellow between, red below lower
                If value >= upperThreshold Then Return "Green"
                If value >= lowerThreshold Then Return "Yellow"
                Return "Red"
            Else
                ' For metrics where lower is better: green below lower, yellow between, red above upper
                If value <= lowerThreshold Then Return "Green"
                If value <= upperThreshold Then Return "Yellow"
                Return "Red"
            End If
        End Function

        Private Function CalculateTrend(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String, ByVal higherIsBetter As Boolean) As String
            ' Fetch values for current and two prior periods to assess direction
            Dim currentValue As Double = FetchKPIValue(si, scenario, timeName, entity, account)
            Dim priorTime1 As String = OffsetMonth(timeName, -1)
            Dim priorTime2 As String = OffsetMonth(timeName, -2)
            Dim priorValue1 As Double = FetchKPIValue(si, scenario, priorTime1, entity, account)
            Dim priorValue2 As Double = FetchKPIValue(si, scenario, priorTime2, entity, account)

            ' Three-period trend: is the series consistently improving, declining, or mixed?
            Dim delta1 As Double = priorValue1 - priorValue2
            Dim delta2 As Double = currentValue - priorValue1

            If higherIsBetter Then
                If delta1 > 0 AndAlso delta2 > 0 Then Return "Improving"
                If delta1 < 0 AndAlso delta2 < 0 Then Return "Declining"
            Else
                ' For lower-is-better KPIs, decreasing values mean improvement
                If delta1 < 0 AndAlso delta2 < 0 Then Return "Improving"
                If delta1 > 0 AndAlso delta2 > 0 Then Return "Declining"
            End If
            Return "Stable"
        End Function

        Private Function OffsetMonth(ByVal timeName As String, ByVal offset As Integer) As String
            Try
                Dim year As Integer = Integer.Parse(timeName.Substring(0, 4))
                Dim month As Integer = Integer.Parse(timeName.Substring(5))
                month += offset
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

        Private Function FetchKPIValue(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

        ' Helper class to define KPI specifications
        Private Class KPIDefinition
            Public Property Name As String
            Public Property AccountMember As String
            Public Property Unit As String
            Public Property LowerThreshold As Double
            Public Property UpperThreshold As Double
            Public Property HigherIsBetter As Boolean

            Public Sub New(ByVal name As String, ByVal account As String, ByVal unit As String, ByVal lower As Double, ByVal upper As Double, ByVal higherBetter As Boolean)
                Me.Name = name
                Me.AccountMember = account
                Me.Unit = unit
                Me.LowerThreshold = lower
                Me.UpperThreshold = upper
                Me.HigherIsBetter = higherBetter
            End Sub
        End Class

    End Class
End Namespace
