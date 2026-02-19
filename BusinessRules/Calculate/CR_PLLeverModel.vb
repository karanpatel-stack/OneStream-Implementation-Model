'------------------------------------------------------------------------------------------------------------
' CR_PLLeverModel.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Calculates P&L waterfall/bridge analysis between periods or scenarios. Decomposes
'           the total variance into individual "levers" that explain the drivers of change:
'           Volume, Price, Mix, FX, Cost, New Business, Lost Business, and Acquisition/Divestiture.
'           Results are written to analysis accounts for waterfall chart display.
'
' Lever Decomposition:
'   1. Volume Effect     - Delta volume x prior period avg price x prior period margin
'   2. Price Effect      - Delta price x current volume
'   3. Mix Effect        - Impact of product/channel mix change on margins
'   4. FX Effect         - Current volume at current price: budget rates vs actual rates
'   5. Cost Effect       - Input cost changes (materials, labor, overhead)
'   6. New Business      - Revenue/margin from new products or customers
'   7. Lost Business     - Revenue/margin from lost accounts
'   8. M&A Effect        - Acquisition or divestiture impact
'
' Frequency: Monthly / Quarterly
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

Namespace OneStream.BusinessRule.Finance.CR_PLLeverModel

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

                ' ---- Define analysis dimensions ----
                ' Current period = Actual, Comparison = Budget (can also be Prior Year)
                Const currentScenario As String = "Actual"
                Const comparisonScenario As String = "Budget"

                Dim productLines As New List(Of String) From {
                    "Product_A", "Product_B", "Product_C", "Product_D", "Product_E"
                }

                ' ---- Accumulators for total entity-level levers ----
                Dim totalVolumeEffect As Double = 0
                Dim totalPriceEffect As Double = 0
                Dim totalMixEffect As Double = 0
                Dim totalCostEffect As Double = 0
                Dim totalFXEffect As Double = 0
                Dim totalNewBusiness As Double = 0
                Dim totalLostBusiness As Double = 0
                Dim totalMAEffect As Double = 0

                ' ---- Read entity-level comparison starting point ----
                Dim comparisonRevenue As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_NetRevenue")
                Dim comparisonGrossProfit As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_GrossProfit")
                Dim comparisonGrossMargin As Double = 0
                If Math.Abs(comparisonRevenue) > 0.01 Then
                    comparisonGrossMargin = comparisonGrossProfit / comparisonRevenue
                End If

                ' =============================================================================================
                ' SECTION 1: Volume, Price, and Mix Effects by Product Line
                ' =============================================================================================

                Dim totalCurrentVolume As Double = 0
                Dim totalComparisonVolume As Double = 0

                For Each product As String In productLines

                    ' ---- Read volume and price data ----
                    Dim currentVol As Double = ReadAmountUD(si, entityName, currentScenario, timeName, _
                        "STAT_Volume", product)
                    Dim compVol As Double = ReadAmountUD(si, entityName, comparisonScenario, timeName, _
                        "STAT_Volume", product)
                    Dim currentPrice As Double = ReadAmountUD(si, entityName, currentScenario, timeName, _
                        "STAT_AvgPrice", product)
                    Dim compPrice As Double = ReadAmountUD(si, entityName, comparisonScenario, timeName, _
                        "STAT_AvgPrice", product)

                    ' Read product-level margin
                    Dim compMargin As Double = ReadAmountUD(si, entityName, comparisonScenario, timeName, _
                        "STAT_GrossMarginPct", product)
                    If compMargin <= 0 Then compMargin = comparisonGrossMargin

                    totalCurrentVolume += currentVol
                    totalComparisonVolume += compVol

                    ' ---- Volume Effect ----
                    ' (Actual Volume - Budget Volume) x Budget Price x Budget Margin
                    Dim volumeEffect As Double = (currentVol - compVol) * compPrice * compMargin

                    ' ---- Price Effect ----
                    ' (Actual Price - Budget Price) x Actual Volume
                    Dim priceEffect As Double = (currentPrice - compPrice) * currentVol

                    ' ---- Mix Effect (calculated as residual at product level) ----
                    Dim actualRevenue As Double = currentVol * currentPrice
                    Dim budgetRevenue As Double = compVol * compPrice
                    Dim totalRevVar As Double = actualRevenue - budgetRevenue
                    Dim pureVolumeAtBudgetPrice As Double = (currentVol - compVol) * compPrice
                    Dim purePriceAtActualVol As Double = (currentPrice - compPrice) * currentVol
                    Dim mixEffect As Double = totalRevVar - pureVolumeAtBudgetPrice - purePriceAtActualVol

                    ' Accumulate totals
                    totalVolumeEffect += volumeEffect
                    totalPriceEffect += priceEffect
                    totalMixEffect += mixEffect

                    ' Write product-level levers
                    WriteAmountUD(si, api, entityName, timeName, "LEVER_VolumeEffect", product, volumeEffect)
                    WriteAmountUD(si, api, entityName, timeName, "LEVER_PriceEffect", product, priceEffect)
                    WriteAmountUD(si, api, entityName, timeName, "LEVER_MixEffect", product, mixEffect)

                Next ' product

                ' =============================================================================================
                ' SECTION 2: FX Effect
                ' =============================================================================================

                ' Read revenue at actual FX rates and at budget FX rates
                Dim revenueAtActualFX As Double = ReadAmount(si, entityName, "Actual", timeName, "PL_NetRevenue")
                Dim revenueAtBudgetFX As Double = ReadAmount(si, entityName, "Actual_AtBudgetRates", timeName, "PL_NetRevenue")
                totalFXEffect = revenueAtActualFX - revenueAtBudgetFX

                ' FX effect on costs
                Dim costsAtActualFX As Double = ReadAmount(si, entityName, "Actual", timeName, "PL_COGS")
                Dim costsAtBudgetFX As Double = ReadAmount(si, entityName, "Actual_AtBudgetRates", timeName, "PL_COGS")
                Dim fxCostEffect As Double = costsAtActualFX - costsAtBudgetFX

                ' Net FX effect on gross profit
                Dim netFXEffect As Double = totalFXEffect - fxCostEffect

                ' =============================================================================================
                ' SECTION 3: Cost Effect
                ' =============================================================================================

                ' Material cost variance
                Dim actualMaterialCost As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_MaterialCost")
                Dim budgetMaterialCost As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_MaterialCost")
                Dim materialCostVar As Double = actualMaterialCost - budgetMaterialCost

                ' Labor cost variance
                Dim actualLaborCost As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_DirectLaborCost")
                Dim budgetLaborCost As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_DirectLaborCost")
                Dim laborCostVar As Double = actualLaborCost - budgetLaborCost

                ' Overhead cost variance
                Dim actualOHCost As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_ManufacturingOH")
                Dim budgetOHCost As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_ManufacturingOH")
                Dim overheadCostVar As Double = actualOHCost - budgetOHCost

                ' Total cost effect (negative = favorable for expenses)
                totalCostEffect = materialCostVar + laborCostVar + overheadCostVar

                ' =============================================================================================
                ' SECTION 4: New Business / Lost Business Effects
                ' =============================================================================================

                ' Revenue from new products/customers (flagged in the cube)
                totalNewBusiness = ReadAmount(si, entityName, currentScenario, timeName, "PL_NewBusinessRevenue")
                Dim newBusinessMargin As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_NewBusinessMargin")

                ' Revenue lost from discontinued products/customers
                totalLostBusiness = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_LostBusinessRevenue")
                Dim lostBusinessMargin As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_LostBusinessMargin")

                ' =============================================================================================
                ' SECTION 5: Acquisition / Divestiture Effect
                ' =============================================================================================

                Dim acquisitionRevenue As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_AcquisitionRevenue")
                Dim acquisitionMargin As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_AcquisitionMargin")
                Dim divestitureRevenue As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_DivestitureRevenue")
                Dim divestitureMargin As Double = ReadAmount(si, entityName, comparisonScenario, timeName, "PL_DivestitureMargin")

                totalMAEffect = (acquisitionRevenue * acquisitionMargin) - (divestitureRevenue * divestitureMargin)

                ' =============================================================================================
                ' SECTION 6: Write Entity-Level Lever Results (Waterfall Bridge)
                ' =============================================================================================

                ' Starting point
                WriteAmount(si, api, entityName, timeName, "LEVER_StartingGrossProfit", comparisonGrossProfit)

                ' Individual levers
                WriteAmount(si, api, entityName, timeName, "LEVER_TotalVolumeEffect", totalVolumeEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_TotalPriceEffect", totalPriceEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_TotalMixEffect", totalMixEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_FXEffect_Revenue", totalFXEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_FXEffect_Cost", fxCostEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_NetFXEffect", netFXEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_MaterialCostEffect", materialCostVar)
                WriteAmount(si, api, entityName, timeName, "LEVER_LaborCostEffect", laborCostVar)
                WriteAmount(si, api, entityName, timeName, "LEVER_OverheadCostEffect", overheadCostVar)
                WriteAmount(si, api, entityName, timeName, "LEVER_TotalCostEffect", totalCostEffect)
                WriteAmount(si, api, entityName, timeName, "LEVER_NewBusinessEffect", totalNewBusiness)
                WriteAmount(si, api, entityName, timeName, "LEVER_LostBusinessEffect", -Math.Abs(totalLostBusiness))
                WriteAmount(si, api, entityName, timeName, "LEVER_MAEffect", totalMAEffect)

                ' Ending point: starting + all levers
                Dim currentGrossProfit As Double = ReadAmount(si, entityName, currentScenario, timeName, "PL_GrossProfit")
                WriteAmount(si, api, entityName, timeName, "LEVER_EndingGrossProfit", currentGrossProfit)

                ' Reconciliation check: sum of levers should equal total variance
                Dim leverSum As Double = totalVolumeEffect + totalPriceEffect + totalMixEffect _
                    + netFXEffect + totalCostEffect + totalNewBusiness - Math.Abs(totalLostBusiness) + totalMAEffect
                Dim totalVariance As Double = currentGrossProfit - comparisonGrossProfit
                Dim reconciliationDiff As Double = totalVariance - leverSum

                If Math.Abs(reconciliationDiff) > 1.0 Then
                    ' Write unexplained variance
                    WriteAmount(si, api, entityName, timeName, "LEVER_UnexplainedVariance", reconciliationDiff)
                    BRApi.ErrorLog.LogMessage(si, String.Format( _
                        "CR_PLLeverModel WARNING: Lever sum ({0:N2}) does not reconcile to total variance " & _
                        "({1:N2}) for {2}/{3}. Unexplained: {4:N2}", _
                        leverSum, totalVariance, entityName, timeName, reconciliationDiff))
                End If

                ' Trigger downstream analysis
                api.Data.Calculate("A#LEVER_EndingGrossProfit")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount (base dimensions)
        '----------------------------------------------------------------------------------------------------
        Private Function ReadAmount(ByVal si As SessionInfo, ByVal entity As String, _
                ByVal scenario As String, ByVal time As String, ByVal account As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount with UD1
        '----------------------------------------------------------------------------------------------------
        Private Function ReadAmountUD(ByVal si As SessionInfo, ByVal entity As String, _
                ByVal scenario As String, ByVal time As String, ByVal account As String, _
                ByVal ud1 As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}:U1#{4}", _
                entity, scenario, time, account, ud1)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write amount to Variance scenario
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal time As String, ByVal account As String, _
                ByVal amount As Double)

            Dim pov As String = String.Format("E#{0}:S#Variance:T#{1}:A#{2}", entity, time, account)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write amount with UD1 to Variance scenario
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteAmountUD(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal time As String, ByVal account As String, _
                ByVal ud1 As String, ByVal amount As Double)

            Dim pov As String = String.Format("E#{0}:S#Variance:T#{1}:A#{2}:U1#{3}", _
                entity, time, account, ud1)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

    End Class

End Namespace
