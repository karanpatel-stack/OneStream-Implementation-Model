'------------------------------------------------------------------------------------------------------------
' CR_KPICalculations.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Calculates derived Key Performance Indicators (KPIs) from P&L, Balance Sheet, and
'           statistical accounts. Computes profitability ratios, efficiency metrics, liquidity
'           ratios, leverage ratios, and operational KPIs. All results are written to calculated
'           KPI accounts for dashboard and reporting consumption.
'
' KPIs Calculated:
'   Profitability:   Gross Margin %, EBITDA %, EBIT %, Net Margin %
'   Returns:         ROIC, ROE, Asset Turnover
'   Working Capital: DSO, DPO, DIO, Cash Conversion Cycle
'   Liquidity:       Current Ratio, Quick Ratio
'   Leverage:        Debt-to-Equity, Interest Coverage
'   Operational:     Revenue per Employee
'
' Frequency: Monthly
' Scope:     All reporting entities
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

Namespace OneStream.BusinessRule.Finance.CR_KPICalculations

    Public Class MainClass

        ' Number of days in the analysis period (annualized basis)
        Private Const DAYS_IN_YEAR As Double = 365.0

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
                Dim timeName As String = api.Time.GetName()

                ' =============================================================================================
                ' READ SOURCE DATA
                ' =============================================================================================

                ' ---- P&L Amounts (annualized or YTD) ----
                Dim netRevenue As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_NetRevenue")
                Dim cogs As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_COGS")
                Dim grossProfit As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_GrossProfit")
                Dim ebitda As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_EBITDA")
                Dim depreciation As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_DepreciationExp_Total")
                Dim amortization As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_AmortizationExp")
                Dim ebit As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_OperatingIncome")
                Dim interestExpense As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_InterestExpense")
                Dim taxExpense As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_TaxExpense")
                Dim netIncome As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_NetIncome")

                ' ---- Balance Sheet Amounts ----
                Dim totalAssets As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_TotalAssets")
                Dim currentAssets As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_TotalCurrentAssets")
                Dim currentLiabilities As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_TotalCurrentLiabilities")
                Dim totalEquity As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_TotalEquity")
                Dim totalDebt As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_TotalDebt")
                Dim accountsReceivable As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_AccountsReceivable")
                Dim inventory As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_Inventory")
                Dim accountsPayable As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_AccountsPayable")
                Dim cashAndEquiv As Double = GetAmount(si, entityName, scenarioName, timeName, "BS_CashAndEquivalents")

                ' ---- Statistical Amounts ----
                Dim fte As Double = GetAmount(si, entityName, scenarioName, timeName, "STAT_TotalFTE")

                ' ---- Effective Tax Rate (for NOPAT calculation) ----
                Dim effectiveTaxRate As Double = 0
                Dim preTaxIncome As Double = GetAmount(si, entityName, scenarioName, timeName, "PL_PreTaxIncome")
                If Math.Abs(preTaxIncome) > 0.01 Then
                    effectiveTaxRate = taxExpense / preTaxIncome
                End If
                ' Cap the effective tax rate at reasonable bounds
                If effectiveTaxRate < 0 Then effectiveTaxRate = 0
                If effectiveTaxRate > 0.50 Then effectiveTaxRate = 0.50

                ' =============================================================================================
                ' SECTION 1: PROFITABILITY RATIOS
                ' =============================================================================================

                ' Gross Margin % = Gross Profit / Net Revenue x 100
                Dim grossMarginPct As Double = SafeDivide(grossProfit, netRevenue) * 100.0

                ' EBITDA % = EBITDA / Net Revenue x 100
                Dim ebitdaPct As Double = SafeDivide(ebitda, netRevenue) * 100.0

                ' EBIT % (Operating Margin) = EBIT / Net Revenue x 100
                Dim ebitPct As Double = SafeDivide(ebit, netRevenue) * 100.0

                ' Net Margin % = Net Income / Net Revenue x 100
                Dim netMarginPct As Double = SafeDivide(netIncome, netRevenue) * 100.0

                ' Write profitability KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_GrossMarginPct", grossMarginPct)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_EBITDA_Pct", ebitdaPct)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_EBIT_Pct", ebitPct)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_NetMarginPct", netMarginPct)

                ' =============================================================================================
                ' SECTION 2: RETURN RATIOS
                ' =============================================================================================

                ' NOPAT = EBIT x (1 - Effective Tax Rate)
                Dim nopat As Double = ebit * (1.0 - effectiveTaxRate)

                ' Invested Capital = Total Assets - Current Liabilities
                Dim investedCapital As Double = totalAssets - currentLiabilities

                ' ROIC = NOPAT / Invested Capital x 100
                Dim roic As Double = SafeDivide(nopat, investedCapital) * 100.0

                ' ROE = Net Income / Total Equity x 100
                Dim roe As Double = SafeDivide(netIncome, totalEquity) * 100.0

                ' Asset Turnover = Revenue / Total Assets
                Dim assetTurnover As Double = SafeDivide(netRevenue, totalAssets)

                ' ROA = Net Income / Total Assets x 100
                Dim roa As Double = SafeDivide(netIncome, totalAssets) * 100.0

                ' Write return KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_NOPAT", nopat)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_ROIC", roic)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_ROE", roe)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_AssetTurnover", assetTurnover)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_ROA", roa)

                ' =============================================================================================
                ' SECTION 3: WORKING CAPITAL METRICS
                ' =============================================================================================

                ' Daily Revenue and Daily COGS (for DSO, DPO, DIO calculations)
                Dim dailyRevenue As Double = netRevenue / DAYS_IN_YEAR
                Dim dailyCOGS As Double = cogs / DAYS_IN_YEAR

                ' DSO = Accounts Receivable / (Revenue / 365)
                Dim dso As Double = SafeDivide(accountsReceivable, dailyRevenue)

                ' DPO = Accounts Payable / (COGS / 365)
                Dim dpo As Double = SafeDivide(accountsPayable, dailyCOGS)

                ' DIO = Inventory / (COGS / 365)
                Dim dio As Double = SafeDivide(inventory, dailyCOGS)

                ' Cash Conversion Cycle = DSO + DIO - DPO
                Dim cashConversionCycle As Double = dso + dio - dpo

                ' Write working capital KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_DSO", dso)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_DPO", dpo)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_DIO", dio)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_CashConversionCycle", cashConversionCycle)

                ' =============================================================================================
                ' SECTION 4: LIQUIDITY RATIOS
                ' =============================================================================================

                ' Current Ratio = Current Assets / Current Liabilities
                Dim currentRatio As Double = SafeDivide(currentAssets, currentLiabilities)

                ' Quick Ratio = (Current Assets - Inventory) / Current Liabilities
                Dim quickRatio As Double = SafeDivide(currentAssets - inventory, currentLiabilities)

                ' Cash Ratio = Cash / Current Liabilities
                Dim cashRatio As Double = SafeDivide(cashAndEquiv, currentLiabilities)

                ' Write liquidity KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_CurrentRatio", currentRatio)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_QuickRatio", quickRatio)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_CashRatio", cashRatio)

                ' =============================================================================================
                ' SECTION 5: LEVERAGE RATIOS
                ' =============================================================================================

                ' Debt-to-Equity = Total Debt / Total Equity
                Dim debtToEquity As Double = SafeDivide(totalDebt, totalEquity)

                ' Interest Coverage = EBIT / Interest Expense
                Dim interestCoverage As Double = SafeDivide(ebit, interestExpense)

                ' Debt-to-EBITDA = Total Debt / EBITDA
                Dim debtToEBITDA As Double = SafeDivide(totalDebt, ebitda)

                ' Net Debt = Total Debt - Cash
                Dim netDebt As Double = totalDebt - cashAndEquiv

                ' Net Debt-to-EBITDA
                Dim netDebtToEBITDA As Double = SafeDivide(netDebt, ebitda)

                ' Write leverage KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_DebtToEquity", debtToEquity)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_InterestCoverage", interestCoverage)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_DebtToEBITDA", debtToEBITDA)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_NetDebt", netDebt)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_NetDebtToEBITDA", netDebtToEBITDA)

                ' =============================================================================================
                ' SECTION 6: OPERATIONAL KPIS
                ' =============================================================================================

                ' Revenue per Employee = Revenue / FTE
                Dim revenuePerEmployee As Double = SafeDivide(netRevenue, fte)

                ' Gross Profit per Employee
                Dim grossProfitPerEmployee As Double = SafeDivide(grossProfit, fte)

                ' EBITDA per Employee
                Dim ebitdaPerEmployee As Double = SafeDivide(ebitda, fte)

                ' Compensation as % of Revenue
                Dim totalCompensation As Double = GetAmount(si, entityName, scenarioName, timeName, "HR_TotalCompensation_All")
                Dim compToRevenuePct As Double = SafeDivide(totalCompensation, netRevenue) * 100.0

                ' Write operational KPIs
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_RevenuePerEmployee", revenuePerEmployee)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_GrossProfitPerEmployee", grossProfitPerEmployee)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_EBITDAPerEmployee", ebitdaPerEmployee)
                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_CompToRevenuePct", compToRevenuePct)

                ' =============================================================================================
                ' SECTION 7: EFFECTIVE TAX RATE
                ' =============================================================================================

                WriteKPI(si, api, entityName, scenarioName, timeName, "KPI_EffectiveTaxRate", effectiveTaxRate * 100.0)

                ' Trigger downstream KPI reporting calculations
                api.Data.Calculate("A#KPI_GrossMarginPct")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Safe division (returns 0 if denominator is zero or near-zero)
        '----------------------------------------------------------------------------------------------------
        Private Function SafeDivide(ByVal numerator As Double, ByVal denominator As Double) As Double

            If Math.Abs(denominator) < 0.01 Then
                Return 0
            End If
            Return numerator / denominator

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount from cube
        '----------------------------------------------------------------------------------------------------
        Private Function GetAmount(ByVal si As SessionInfo, ByVal entity As String, _
                ByVal scenario As String, ByVal time As String, ByVal account As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write KPI value to cube
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteKPI(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal scenario As String, ByVal time As String, _
                ByVal account As String, ByVal amount As Double)

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

    End Class

End Namespace
