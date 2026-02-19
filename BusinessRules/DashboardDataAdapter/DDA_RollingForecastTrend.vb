'------------------------------------------------------------------------------------------------------------
' DDA_RollingForecastTrend
' Dashboard DataAdapter Business Rule
' Purpose: Rolling forecast trend across 18-month window showing actuals, forecast, and original budget
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_RollingForecastTrend
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("RollingForecastTrend")
                        dt.Columns.Add("Period", GetType(String))
                        dt.Columns.Add("Actual", GetType(Double))
                        dt.Columns.Add("Forecast", GetType(Double))
                        dt.Columns.Add("Budget", GetType(Double))
                        dt.Columns.Add("IsActual", GetType(Boolean))
                        dt.Columns.Add("TrendLine", GetType(Double))

                        ' Dashboard parameters
                        Dim actualScenario As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim forecastScenario As String = args.NameValuePairs.XFGetValue("ForecastScenario", "Forecast")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")
                        Dim accountMember As String = args.NameValuePairs.XFGetValue("Account", "A#Revenue")
                        Dim currentTimeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)

                        ' Parse the current period to determine the 18-month window boundaries
                        Dim currentYear As Integer = 0
                        Dim currentMonth As Integer = 0
                        ParseTimeName(currentTimeName, currentYear, currentMonth)

                        ' Determine which months are closed (actuals) vs open (forecast)
                        ' Assume months up to and including the current period have actuals
                        Dim closedThrough As Integer = currentMonth
                        Dim closedYear As Integer = currentYear

                        ' Build 18-month window: 6 months back + current + 11 months forward
                        Dim periodValues As New List(Of Double)()
                        Dim periodNames As New List(Of String)()

                        For offset As Integer = -6 To 11
                            Dim periodYear As Integer = currentYear
                            Dim periodMonth As Integer = currentMonth + offset

                            ' Normalize month boundaries
                            While periodMonth <= 0
                                periodMonth += 12
                                periodYear -= 1
                            End While
                            While periodMonth > 12
                                periodMonth -= 12
                                periodYear += 1
                            End While

                            Dim periodTimeName As String = periodYear.ToString() & "M" & periodMonth.ToString()
                            Dim displayName As String = periodYear.ToString() & "-" & periodMonth.ToString("D2")

                            ' Determine if this period has actuals or uses forecast
                            Dim isPastOrCurrent As Boolean = (periodYear < closedYear) OrElse
                                                             (periodYear = closedYear AndAlso periodMonth <= closedThrough)

                            Dim actualValue As Double = 0
                            Dim forecastValue As Double = 0
                            Dim budgetValue As Double = 0

                            If isPastOrCurrent Then
                                ' Closed period: use actual data
                                actualValue = FetchAmount(si, actualScenario, periodTimeName, entityName, accountMember)
                                forecastValue = actualValue ' Show actual as the "forecast realized" value
                            Else
                                ' Open period: use forecast data
                                forecastValue = FetchAmount(si, forecastScenario, periodTimeName, entityName, accountMember)
                            End If

                            ' Budget is always available for comparison
                            budgetValue = FetchAmount(si, budgetScenario, periodTimeName, entityName, accountMember)

                            ' Track values for trend line calculation
                            If isPastOrCurrent Then
                                periodValues.Add(actualValue)
                            Else
                                periodValues.Add(forecastValue)
                            End If
                            periodNames.Add(displayName)

                            Dim row As DataRow = dt.NewRow()
                            row("Period") = displayName
                            row("Actual") = Math.Round(actualValue, 2)
                            row("Forecast") = Math.Round(forecastValue, 2)
                            row("Budget") = Math.Round(budgetValue, 2)
                            row("IsActual") = isPastOrCurrent
                            row("TrendLine") = 0 ' Placeholder, calculated below
                            dt.Rows.Add(row)
                        Next

                        ' Calculate simple linear trend line using least squares regression
                        CalculateTrendLine(dt, periodValues)

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Sub ParseTimeName(ByVal timeName As String, ByRef year As Integer, ByRef month As Integer)
            Try
                year = Integer.Parse(timeName.Substring(0, 4))
                Dim mPart As String = timeName.Substring(5)
                month = Integer.Parse(mPart)
            Catch
                year = DateTime.Now.Year
                month = DateTime.Now.Month
            End Try
        End Sub

        Private Sub CalculateTrendLine(ByVal dt As DataTable, ByVal values As List(Of Double))
            ' Least squares linear regression for trend line
            Dim n As Integer = values.Count
            If n < 2 Then Exit Sub

            Dim sumX As Double = 0
            Dim sumY As Double = 0
            Dim sumXY As Double = 0
            Dim sumX2 As Double = 0

            For i As Integer = 0 To n - 1
                sumX += i
                sumY += values(i)
                sumXY += i * values(i)
                sumX2 += i * i
            Next

            Dim denominator As Double = (n * sumX2) - (sumX * sumX)
            If denominator = 0 Then Exit Sub

            Dim slope As Double = ((n * sumXY) - (sumX * sumY)) / denominator
            Dim intercept As Double = (sumY - (slope * sumX)) / n

            ' Apply trend values to the DataTable
            For i As Integer = 0 To dt.Rows.Count - 1
                dt.Rows(i)("TrendLine") = Math.Round(intercept + (slope * i), 2)
            Next
        End Sub

        Private Function FetchAmount(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on failure
            End Try
            Return 0
        End Function

    End Class
End Namespace
