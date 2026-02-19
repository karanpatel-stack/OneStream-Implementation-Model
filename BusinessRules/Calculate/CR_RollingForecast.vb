'------------------------------------------------------------------------------------------------------------
' CR_RollingForecast.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Implements a rolling forecast seeding mechanism. Determines the current closed period
'           and automatically populates the rolling forecast scenario with actuals for closed
'           months and forecast data for open months, maintaining an 18-month rolling window.
'           Supports growth rate adjustments and scenario copy between RF_Jan through RF_Dec.
'
' Logic:
'   Months 1 to N (closed)     -> Copy from Actual scenario
'   Months N+1 to N+18 (open)  -> Retain existing forecast or seed from prior forecast with adjustments
'
' Frequency: Monthly (triggered after period close)
' Scope:     All forecasting entities
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.CR_RollingForecast

    Public Class MainClass

        ' Rolling forecast window length in months
        Private Const ROLLING_WINDOW_MONTHS As Integer = 18

        '----------------------------------------------------------------------------------------------------
        ' Main entry point
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object

            Try
                If args.CalculationType <> FinanceRulesCalculationType.Calculate Then
                    Return Nothing
                End If

                Dim entityName As String = api.Entity.GetName()
                Dim scenarioName As String = api.Scenario.GetName()
                Dim currentTimeName As String = api.Time.GetName()

                ' ---- Determine current closed period ----
                ' The last closed period number is stored in a control account
                Dim closedPeriodPov As String = String.Format( _
                    "E#{0}:S#Actual:T#{1}:A#SYS_LastClosedPeriod", entityName, currentTimeName)
                Dim closedPeriodCell As DataCell = BRApi.Finance.Data.GetDataCell(si, closedPeriodPov, True)
                Dim lastClosedMonth As Integer = CInt(closedPeriodCell.CellAmount)

                ' Validate closed period
                If lastClosedMonth < 1 OrElse lastClosedMonth > 12 Then
                    BRApi.ErrorLog.LogMessage(si, String.Format( _
                        "CR_RollingForecast WARNING: Invalid last closed period ({0}) for entity {1}. " & _
                        "Defaulting to 0 (no closed periods).", lastClosedMonth, entityName))
                    lastClosedMonth = 0
                End If

                ' ---- Determine the base year from the current time period ----
                ' Assumes time period naming convention: "2025M1", "2025M2", etc.
                Dim baseYear As Integer = DateTime.Now.Year
                Dim currentYearStr As String = currentTimeName
                If currentTimeName.Length >= 4 Then
                    Integer.TryParse(currentTimeName.Substring(0, 4), baseYear)
                End If

                ' ---- Define accounts to seed ----
                Dim forecastAccounts As New List(Of String) From {
                    "PL_NetRevenue", "PL_COGS", "PL_GrossProfit",
                    "PL_SGA_Expense", "PL_RD_Expense", "PL_DepreciationExp_Total",
                    "PL_InterestExpense", "PL_TaxExpense", "PL_NetIncome",
                    "BS_CashAndEquivalents", "BS_AccountsReceivable", "BS_Inventory",
                    "BS_AccountsPayable", "BS_TotalDebt"
                }

                ' ---- Define growth rate adjustments by account ----
                ' Applied when seeding open months from prior forecast
                Dim growthRates As New Dictionary(Of String, Double) From {
                    {"PL_NetRevenue", 0.03},          ' 3% annual revenue growth
                    {"PL_COGS", 0.025},               ' 2.5% COGS growth
                    {"PL_GrossProfit", 0.03},
                    {"PL_SGA_Expense", 0.02},          ' 2% SGA growth
                    {"PL_RD_Expense", 0.05},           ' 5% R&D investment growth
                    {"PL_DepreciationExp_Total", 0.01},
                    {"PL_InterestExpense", 0.0},
                    {"PL_TaxExpense", 0.02},
                    {"PL_NetIncome", 0.03},
                    {"BS_CashAndEquivalents", 0.0},
                    {"BS_AccountsReceivable", 0.025},
                    {"BS_Inventory", 0.02},
                    {"BS_AccountsPayable", 0.02},
                    {"BS_TotalDebt", 0.0}
                }

                ' ---- Determine the rolling forecast scenario name ----
                ' Convention: RF_Jan, RF_Feb, ..., RF_Dec based on the month the forecast was created
                Dim rfScenarioName As String = String.Format("RF_{0}", _
                    CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(Math.Max(lastClosedMonth, 1)))

                ' ---- Process each month in the 18-month rolling window ----
                For monthOffset As Integer = 1 To ROLLING_WINDOW_MONTHS

                    ' Calculate the target year and month
                    Dim targetMonth As Integer = ((lastClosedMonth + monthOffset - 1) Mod 12) + 1
                    Dim targetYear As Integer = baseYear + ((lastClosedMonth + monthOffset - 1) \ 12)
                    Dim targetTimeName As String = String.Format("{0}M{1}", targetYear, targetMonth)

                    ' Determine absolute month index for comparison with closed period
                    Dim absoluteMonth As Integer = (targetYear - baseYear) * 12 + targetMonth

                    For Each acct As String In forecastAccounts

                        If absoluteMonth <= lastClosedMonth Then
                            ' ---- CLOSED PERIOD: Copy from Actual scenario ----
                            Dim actualPov As String = String.Format( _
                                "E#{0}:S#Actual:T#{1}:A#{2}", entityName, targetTimeName, acct)
                            Dim actualCell As DataCell = BRApi.Finance.Data.GetDataCell(si, actualPov, True)
                            Dim actualAmount As Double = actualCell.CellAmount

                            ' Write actual value to rolling forecast scenario
                            Dim rfPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#{3}", entityName, rfScenarioName, targetTimeName, acct)
                            api.Data.SetDataCell(si, rfPov, actualAmount, True)

                            ' Flag the period as actuals-sourced
                            Dim flagPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#RF_SourceFlag", entityName, rfScenarioName, targetTimeName)
                            api.Data.SetDataCell(si, flagPov, 1.0, True)  ' 1 = Actual

                        Else
                            ' ---- OPEN PERIOD: Seed from prior forecast or apply growth ----
                            ' First, check if existing forecast data is present
                            Dim existingPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#{3}", entityName, rfScenarioName, targetTimeName, acct)
                            Dim existingCell As DataCell = BRApi.Finance.Data.GetDataCell(si, existingPov, True)

                            If Math.Abs(existingCell.CellAmount) > 0.01 Then
                                ' Existing forecast data found -- retain it (do not overwrite)
                                Continue For
                            End If

                            ' No existing data -- seed from prior year actual with growth adjustment
                            Dim priorYearTimeName As String = String.Format("{0}M{1}", targetYear - 1, targetMonth)
                            Dim priorPov As String = String.Format( _
                                "E#{0}:S#Actual:T#{1}:A#{2}", entityName, priorYearTimeName, acct)
                            Dim priorCell As DataCell = BRApi.Finance.Data.GetDataCell(si, priorPov, True)
                            Dim priorAmount As Double = priorCell.CellAmount

                            ' Apply monthly growth rate (annualized rate / 12)
                            Dim annualGrowth As Double = 0
                            If growthRates.ContainsKey(acct) Then
                                annualGrowth = growthRates(acct)
                            End If
                            Dim monthlyGrowthFactor As Double = 1.0 + (annualGrowth / 12.0) * monthOffset

                            Dim seededAmount As Double = priorAmount * monthlyGrowthFactor

                            ' Write seeded forecast amount
                            api.Data.SetDataCell(si, existingPov, seededAmount, True)

                            ' Flag the period as forecast-sourced
                            Dim flagPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#RF_SourceFlag", entityName, rfScenarioName, targetTimeName)
                            api.Data.SetDataCell(si, flagPov, 2.0, True)  ' 2 = Seeded Forecast

                        End If

                    Next ' account

                Next ' monthOffset

                ' ---- Copy rolling forecast to the main RollingForecast scenario ----
                For monthOffset As Integer = 1 To ROLLING_WINDOW_MONTHS
                    Dim targetMonth As Integer = ((lastClosedMonth + monthOffset - 1) Mod 12) + 1
                    Dim targetYear As Integer = baseYear + ((lastClosedMonth + monthOffset - 1) \ 12)
                    Dim targetTimeName As String = String.Format("{0}M{1}", targetYear, targetMonth)

                    For Each acct As String In forecastAccounts
                        Dim sourcePov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#{3}", entityName, rfScenarioName, targetTimeName, acct)
                        Dim sourceCell As DataCell = BRApi.Finance.Data.GetDataCell(si, sourcePov, True)

                        Dim destPov As String = String.Format( _
                            "E#{0}:S#RollingForecast:T#{1}:A#{2}", entityName, targetTimeName, acct)
                        api.Data.SetDataCell(si, destPov, sourceCell.CellAmount, True)
                    Next
                Next

                ' Trigger downstream calculations
                api.Data.Calculate("A#RF_SourceFlag")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

    End Class

End Namespace
