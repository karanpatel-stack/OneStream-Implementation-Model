'------------------------------------------------------------------------------------------------------------
' CR_CashFlowIndirect.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Derives the Statement of Cash Flows using the indirect method. Starts with Net Income,
'           adjusts for non-cash items, computes working capital changes, and sums operating,
'           investing, and financing activities. Validates that ending cash ties to the balance sheet.
'
' Structure:
'   Operating Activities:  Net Income + Non-Cash Adjustments + Working Capital Changes
'   Investing Activities:  CAPEX + Disposals + Acquisitions
'   Financing Activities:  Debt Proceeds/Repayments + Dividends + Equity Transactions
'   Net Cash Change:       Operating + Investing + Financing
'   Ending Cash:           Beginning Cash + Net Cash Change
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

Namespace OneStream.BusinessRule.Finance.CR_CashFlowIndirect

    Public Class MainClass

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
                ' SECTION 1: OPERATING ACTIVITIES
                ' =============================================================================================

                ' ---- Net Income (starting point) ----
                Dim netIncome As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_NetIncome")

                ' ---- Non-Cash Adjustments (add back to Net Income) ----
                Dim depreciation As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_DepreciationExp_Total")
                Dim amortization As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_AmortizationExp")
                Dim deferredTax As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_DeferredTaxExpense")
                Dim stockCompExp As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_StockCompExpense")
                Dim impairment As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_ImpairmentCharge")
                Dim gainLossDisposal As Double = GetAmount(si, api, entityName, scenarioName, timeName, "PL_GainLossDisposal_Total")

                ' Non-cash total (gains are subtracted since they are non-cash income)
                Dim nonCashTotal As Double = depreciation + amortization + deferredTax _
                    + stockCompExp + impairment - gainLossDisposal

                ' ---- Working Capital Changes ----
                ' For assets: decrease = source of cash (Prior - Current); increase = use of cash
                ' For liabilities: increase = source of cash (Current - Prior); decrease = use of cash

                ' Accounts Receivable
                Dim currentAR As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccountsReceivable")
                Dim priorAR As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccountsReceivable_PY")
                Dim deltaAR As Double = priorAR - currentAR  ' Decrease in AR = source

                ' Inventory
                Dim currentInv As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_Inventory")
                Dim priorInv As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_Inventory_PY")
                Dim deltaInventory As Double = priorInv - currentInv

                ' Prepaid Expenses
                Dim currentPrepaid As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_PrepaidExpenses")
                Dim priorPrepaid As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_PrepaidExpenses_PY")
                Dim deltaPrepaid As Double = priorPrepaid - currentPrepaid

                ' Other Current Assets
                Dim currentOtherCA As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_OtherCurrentAssets")
                Dim priorOtherCA As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_OtherCurrentAssets_PY")
                Dim deltaOtherCA As Double = priorOtherCA - currentOtherCA

                ' Accounts Payable
                Dim currentAP As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccountsPayable")
                Dim priorAP As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccountsPayable_PY")
                Dim deltaAP As Double = currentAP - priorAP  ' Increase in AP = source

                ' Accrued Liabilities
                Dim currentAccrued As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccruedLiabilities")
                Dim priorAccrued As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_AccruedLiabilities_PY")
                Dim deltaAccrued As Double = currentAccrued - priorAccrued

                ' Deferred Revenue
                Dim currentDefRev As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_DeferredRevenue")
                Dim priorDefRev As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_DeferredRevenue_PY")
                Dim deltaDeferredRev As Double = currentDefRev - priorDefRev

                ' Income Tax Payable
                Dim currentTaxPay As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_IncomeTaxPayable")
                Dim priorTaxPay As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_IncomeTaxPayable_PY")
                Dim deltaTaxPayable As Double = currentTaxPay - priorTaxPay

                Dim workingCapitalChange As Double = deltaAR + deltaInventory + deltaPrepaid _
                    + deltaOtherCA + deltaAP + deltaAccrued + deltaDeferredRev + deltaTaxPayable

                ' ---- Operating Cash Flow ----
                Dim operatingCF As Double = netIncome + nonCashTotal + workingCapitalChange

                ' =============================================================================================
                ' SECTION 2: INVESTING ACTIVITIES
                ' =============================================================================================

                ' Capital Expenditures (negative = use of cash)
                Dim capex As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_CapitalExpenditures")
                ' Asset Disposal Proceeds (positive = source of cash)
                Dim disposalProceeds As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_DisposalProceeds")
                ' Acquisitions (negative)
                Dim acquisitions As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_Acquisitions")
                ' Investment purchases/sales
                Dim investmentActivity As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_InvestmentActivity")

                Dim investingCF As Double = -Math.Abs(capex) + disposalProceeds _
                    - Math.Abs(acquisitions) + investmentActivity

                ' =============================================================================================
                ' SECTION 3: FINANCING ACTIVITIES
                ' =============================================================================================

                ' Debt proceeds (positive)
                Dim debtProceeds As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_DebtProceeds")
                ' Debt repayments (negative)
                Dim debtRepayments As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_DebtRepayments")
                ' Dividends paid (negative)
                Dim dividendsPaid As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_DividendsPaid")
                ' Stock issuance (positive)
                Dim stockIssuance As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_StockIssuance")
                ' Stock repurchase (negative)
                Dim stockRepurchase As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_StockRepurchase")

                Dim financingCF As Double = debtProceeds - Math.Abs(debtRepayments) _
                    - Math.Abs(dividendsPaid) + stockIssuance - Math.Abs(stockRepurchase)

                ' =============================================================================================
                ' SECTION 4: NET CASH CHANGE AND ENDING CASH
                ' =============================================================================================

                Dim netCashChange As Double = operatingCF + investingCF + financingCF

                ' Beginning cash (from balance sheet prior period)
                Dim beginningCash As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_CashAndEquivalents_PY")

                ' FX impact on cash (for multinational consolidation)
                Dim fxImpactOnCash As Double = GetAmount(si, api, entityName, scenarioName, timeName, "CF_FXImpactOnCash")

                ' Ending cash
                Dim endingCash As Double = beginningCash + netCashChange + fxImpactOnCash

                ' =============================================================================================
                ' SECTION 5: WRITE RESULTS
                ' =============================================================================================

                ' Non-cash adjustments subtotals
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_NonCashAdjustments", nonCashTotal)

                ' Working capital subtotals
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_DeltaAR", deltaAR)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_DeltaInventory", deltaInventory)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_DeltaPrepaid", deltaPrepaid)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_DeltaAP", deltaAP)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_DeltaAccrued", deltaAccrued)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_WorkingCapitalChange", workingCapitalChange)

                ' Activity totals
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_OperatingActivities", operatingCF)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_InvestingActivities", investingCF)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_FinancingActivities", financingCF)

                ' Net change and ending cash
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_NetCashChange", netCashChange)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_BeginningCash", beginningCash)
                WriteAmount(si, api, entityName, scenarioName, timeName, "CF_EndingCash", endingCash)

                ' =============================================================================================
                ' SECTION 6: VALIDATION - Ending Cash must tie to BS Cash
                ' =============================================================================================

                Dim bsCash As Double = GetAmount(si, api, entityName, scenarioName, timeName, "BS_CashAndEquivalents")
                Dim cashVariance As Double = Math.Abs(endingCash - bsCash)
                Const toleranceThreshold As Double = 0.01

                If cashVariance > toleranceThreshold Then
                    ' Log a warning; the cash flow does not tie to the balance sheet
                    BRApi.ErrorLog.LogMessage(si, String.Format( _
                        "CR_CashFlowIndirect WARNING: Ending cash ({0:N2}) does not tie to BS cash ({1:N2}) " & _
                        "for Entity={2}, Period={3}. Variance={4:N2}", _
                        endingCash, bsCash, entityName, timeName, cashVariance))

                    ' Write the out-of-balance amount for review
                    WriteAmount(si, api, entityName, scenarioName, timeName, "CF_CashTieVariance", cashVariance)
                End If

                ' Trigger downstream calculations
                api.Data.Calculate("A#CF_EndingCash")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read a single data amount from the cube
        '----------------------------------------------------------------------------------------------------
        Private Function GetAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal scenario As String, ByVal time As String, _
                ByVal account As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write a single data amount to the cube
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal scenario As String, ByVal time As String, _
                ByVal account As String, ByVal amount As Double)

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

    End Class

End Namespace
