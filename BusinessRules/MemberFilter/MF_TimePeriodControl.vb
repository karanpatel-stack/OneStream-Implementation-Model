'------------------------------------------------------------------------------------------------------------
' MF_TimePeriodControl
' Member Filter Business Rule - Time Period Input Restriction
'
' Purpose:  Restricts data input to currently open time periods based on the scenario type
'           and the organization's close/planning calendar. Ensures users can only enter
'           data into periods that are actively accepting input.
'
' Period Rules by Scenario:
'   Actual     -> Only the current close month is open for input
'   Budget     -> Budget cycle months are open (typically next fiscal year periods)
'   Forecast   -> Current month and all future months within the forecast horizon
'
' The "current open period" is determined from application settings / substitution variables
' maintained by the close manager or system administrator.
'
' Scope:    Member Filter
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.MemberFilter.MF_TimePeriodControl

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As MemberFilterArgs) As Object
            Try
                '--- Determine the current scenario context ---
                Dim scenarioName As String = BRApi.Finance.Members.GetMemberName(
                    si, DimType.Scenario.Id, si.WorkflowClusterPk.ScenarioId)

                BRApi.ErrorLog.LogMessage(si, "MF_TimePeriodControl: Evaluating open periods for scenario=" & scenarioName)

                '--- Read the current open period settings from application substitution variables ---
                Dim currentCloseMonth As String = GetAppSetting(si, "CurrentCloseMonth", "2025M12")
                Dim currentCloseYear As String = GetAppSetting(si, "CurrentCloseYear", "2025")
                Dim budgetTargetYear As String = GetAppSetting(si, "BudgetTargetYear", "2026")
                Dim forecastHorizonMonths As Integer = 18  ' Default: 18-month rolling forecast

                Dim forecastHorizonStr As String = GetAppSetting(si, "ForecastHorizonMonths", "18")
                Integer.TryParse(forecastHorizonStr, forecastHorizonMonths)

                '--- Build the time period filter based on scenario type ---
                Dim timeFilter As String = String.Empty

                Select Case scenarioName.ToUpper()

                    Case "ACTUAL"
                        '--- Actuals: only the current close month is open ---
                        timeFilter = BuildActualFilter(currentCloseMonth)

                    Case "BUDGET"
                        '--- Budget: all months in the budget target year ---
                        timeFilter = BuildBudgetFilter(budgetTargetYear)

                    Case "FORECAST", "FORECAST_Q2", "FORECAST_Q3", "FORECAST_Q4"
                        '--- Forecast: current month + future months within horizon ---
                        timeFilter = BuildForecastFilter(currentCloseMonth, forecastHorizonMonths)

                    Case Else
                        '--- Unknown scenario: default to current close month only ---
                        BRApi.ErrorLog.LogMessage(si, "MF_TimePeriodControl: Unknown scenario '" &
                            scenarioName & "', defaulting to current close month.")
                        timeFilter = "T#" & currentCloseMonth

                End Select

                BRApi.ErrorLog.LogMessage(si, "MF_TimePeriodControl: Returning filter=" & timeFilter)
                Return timeFilter

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "MF_TimePeriodControl", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Builds the time filter for the Actual scenario. Only the current close month is open.
        ''' </summary>
        Private Function BuildActualFilter(ByVal currentCloseMonth As String) As String
            ' For Actuals, only a single month is open at any given time
            Return "T#" & currentCloseMonth
        End Function

        ''' <summary>
        ''' Builds the time filter for the Budget scenario. All 12 months in the budget
        ''' target year are open for input during the budget cycle.
        ''' </summary>
        Private Function BuildBudgetFilter(ByVal budgetTargetYear As String) As String
            Dim filterParts As New List(Of String)

            ' Open all monthly periods in the budget year
            For month As Integer = 1 To 12
                filterParts.Add("T#" & budgetTargetYear & "M" & month.ToString())
            Next

            Return String.Join(":", filterParts.ToArray())
        End Function

        ''' <summary>
        ''' Builds the time filter for Forecast scenarios. Opens the current month and
        ''' all future months up to the specified forecast horizon.
        ''' </summary>
        Private Function BuildForecastFilter(ByVal currentCloseMonth As String,
                                              ByVal horizonMonths As Integer) As String
            Try
                Dim filterParts As New List(Of String)

                '--- Parse the current close month to determine start point ---
                Dim currentYear As Integer
                Dim currentMonth As Integer
                ParseTimeMember(currentCloseMonth, currentYear, currentMonth)

                '--- Generate time members from current month forward for the horizon ---
                Dim year As Integer = currentYear
                Dim month As Integer = currentMonth

                For i As Integer = 0 To horizonMonths - 1
                    filterParts.Add("T#" & year.ToString() & "M" & month.ToString())

                    ' Advance to next month
                    month += 1
                    If month > 12 Then
                        month = 1
                        year += 1
                    End If
                Next

                Return String.Join(":", filterParts.ToArray())

            Catch ex As Exception
                ' If parsing fails, fall back to the current close month only
                Return "T#" & currentCloseMonth
            End Try
        End Function

        ''' <summary>
        ''' Parses a OneStream time member name (e.g., "2025M6") into year and month components.
        ''' </summary>
        Private Sub ParseTimeMember(ByVal timeName As String, ByRef year As Integer, ByRef month As Integer)
            Dim parts() As String = timeName.Split("M"c)
            year = Integer.Parse(parts(0))
            month = Integer.Parse(parts(1))
        End Sub

        ''' <summary>
        ''' Reads an application setting from substitution variables with a default fallback.
        ''' </summary>
        Private Function GetAppSetting(ByVal si As SessionInfo, ByVal settingName As String,
                                        ByVal defaultValue As String) As String
            Try
                Dim value As String = BRApi.Finance.Data.GetSubstVarValue(si, settingName)
                If Not String.IsNullOrEmpty(value) Then
                    Return value.Trim()
                End If
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_TimePeriodControl.GetAppSetting: " &
                    "Could not read '" & settingName & "' - " & ex.Message)
            End Try
            Return defaultValue
        End Function

    End Class

End Namespace
