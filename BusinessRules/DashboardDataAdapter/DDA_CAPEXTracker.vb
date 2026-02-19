'------------------------------------------------------------------------------------------------------------
' DDA_CAPEXTracker
' Dashboard DataAdapter Business Rule
' Purpose: Capital expenditure project tracking with budget, spend, forecast-to-complete, and status
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_CAPEXTracker
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("CAPEXTracker")
                        dt.Columns.Add("ProjectName", GetType(String))
                        dt.Columns.Add("ProjectType", GetType(String))
                        dt.Columns.Add("Budget", GetType(Double))
                        dt.Columns.Add("ActualSpend", GetType(Double))
                        dt.Columns.Add("ForecastToComplete", GetType(Double))
                        dt.Columns.Add("TotalForecast", GetType(Double))
                        dt.Columns.Add("Variance", GetType(Double))
                        dt.Columns.Add("PctComplete", GetType(Double))
                        dt.Columns.Add("Status", GetType(String))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim forecastScenario As String = args.NameValuePairs.XFGetValue("ForecastScenario", "Forecast")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Define CAPEX projects tracked in UD dimensions
                        Dim projectDefinitions As New List(Of Tuple(Of String, String, String))()
                        projectDefinitions.Add(Tuple.Create("ERP Upgrade", "IT", "U1#CAPEX_ERP_Upgrade"))
                        projectDefinitions.Add(Tuple.Create("Detroit Line Expansion", "Manufacturing", "U1#CAPEX_Detroit_Expansion"))
                        projectDefinitions.Add(Tuple.Create("Shanghai New Facility", "Manufacturing", "U1#CAPEX_Shanghai_Facility"))
                        projectDefinitions.Add(Tuple.Create("Warehouse Automation", "Logistics", "U1#CAPEX_Warehouse_Auto"))
                        projectDefinitions.Add(Tuple.Create("Munich R&D Lab", "R&D", "U1#CAPEX_Munich_RD"))
                        projectDefinitions.Add(Tuple.Create("Data Center Migration", "IT", "U1#CAPEX_DataCenter"))
                        projectDefinitions.Add(Tuple.Create("Fleet Replacement", "Logistics", "U1#CAPEX_Fleet"))
                        projectDefinitions.Add(Tuple.Create("Office Renovation HQ", "Facilities", "U1#CAPEX_Office_Reno"))
                        projectDefinitions.Add(Tuple.Create("Solar Installation", "Facilities", "U1#CAPEX_Solar"))
                        projectDefinitions.Add(Tuple.Create("CRM Implementation", "IT", "U1#CAPEX_CRM"))

                        For Each projDef In projectDefinitions
                            Dim projectName As String = projDef.Item1
                            Dim projectType As String = projDef.Item2
                            Dim udMember As String = projDef.Item3

                            ' Fetch approved budget (YTD cumulative from budget scenario)
                            Dim budgetAmount As Double = FetchProjectAmount(si, budgetScenario, timeName, entityName, "A#CAPEX_Budget", udMember)

                            ' Fetch actual spend to date (YTD cumulative)
                            Dim actualSpend As Double = FetchProjectAmount(si, scenarioName, timeName, entityName, "A#CAPEX_Actual", udMember)

                            ' Fetch forecast to complete (remaining estimated spend)
                            Dim forecastToComplete As Double = FetchProjectAmount(si, forecastScenario, timeName, entityName, "A#CAPEX_ForecastToComplete", udMember)

                            ' Calculate total forecast (actual spend + remaining forecast)
                            Dim totalForecast As Double = actualSpend + forecastToComplete

                            ' Calculate variance to budget (negative means over budget)
                            Dim variance As Double = budgetAmount - totalForecast

                            ' Calculate percent complete based on spend vs total forecast
                            Dim pctComplete As Double = 0
                            If totalForecast > 0 Then
                                pctComplete = actualSpend / totalForecast
                            End If

                            ' Determine project status
                            Dim status As String = DetermineProjectStatus(variance, budgetAmount, pctComplete)

                            Dim row As DataRow = dt.NewRow()
                            row("ProjectName") = projectName
                            row("ProjectType") = projectType
                            row("Budget") = Math.Round(budgetAmount, 2)
                            row("ActualSpend") = Math.Round(actualSpend, 2)
                            row("ForecastToComplete") = Math.Round(forecastToComplete, 2)
                            row("TotalForecast") = Math.Round(totalForecast, 2)
                            row("Variance") = Math.Round(variance, 2)
                            row("PctComplete") = Math.Round(pctComplete, 4)
                            row("Status") = status
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

        Private Function FetchProjectAmount(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String, ByVal udMember As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:A#{3}:V#YTD:F#EndBal:O#Forms:IC#[ICP None]:{4}:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account.Replace("A#", ""), udMember)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on failure
            End Try
            Return 0
        End Function

        Private Function DetermineProjectStatus(ByVal variance As Double, ByVal budget As Double, ByVal pctComplete As Double) As String
            If budget = 0 Then Return "Not Started"

            ' Calculate variance as percentage of budget
            Dim variancePct As Double = 0
            If budget > 0 Then variancePct = variance / budget

            ' Status logic:
            ' On Track: forecast within 5% of budget
            ' At Risk: forecast 5-15% over budget
            ' Over Budget: forecast >15% over budget
            ' Complete: >95% spend realized
            If pctComplete >= 0.95 Then
                Return "Complete"
            ElseIf variancePct >= -0.05 Then
                Return "On Track"
            ElseIf variancePct >= -0.15 Then
                Return "At Risk"
            Else
                Return "Over Budget"
            End If
        End Function

    End Class
End Namespace
