'------------------------------------------------------------------------------------------------------------
' CR_VarianceAnalysis.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Multi-dimensional variance decomposition across scenarios, periods, and dimensions.
'           Computes Actual vs Budget, Actual vs Prior Year, and Forecast vs Budget variances.
'           Calculates absolute and percentage variances with favorable/unfavorable classification.
'           Decomposes revenue variances into Volume, Price, and Mix components, and performs
'           FX variance analysis for multinational reporting.
'
' Variance Types:
'   - Actual vs Budget (absolute + %)
'   - Actual vs Prior Year (absolute + %)
'   - Forecast vs Budget (absolute + %)
'   - Volume Variance: (Actual Vol - Budget Vol) x Budget Price
'   - Price Variance:  (Actual Price - Budget Price) x Actual Vol
'   - Mix Variance:    Residual of product/channel mix shift
'   - FX Variance:     Budget at actual rates vs budget at budget rates
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

Namespace OneStream.BusinessRule.Finance.CR_VarianceAnalysis

    Public Class MainClass

        ' Account classification for favorable/unfavorable determination
        Private Enum AccountNature
            Revenue     ' Positive variance = favorable
            Expense     ' Negative variance = favorable
            StatAccount ' No fav/unfav classification
        End Enum

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
                Dim timeName As String = api.Time.GetName()

                ' ---- Define P&L accounts and their nature ----
                Dim accountsToAnalyze As New Dictionary(Of String, AccountNature) From {
                    {"PL_NetRevenue", AccountNature.Revenue},
                    {"PL_COGS", AccountNature.Expense},
                    {"PL_GrossProfit", AccountNature.Revenue},
                    {"PL_SGA_Expense", AccountNature.Expense},
                    {"PL_RD_Expense", AccountNature.Expense},
                    {"PL_OperatingIncome", AccountNature.Revenue},
                    {"PL_InterestExpense", AccountNature.Expense},
                    {"PL_TaxExpense", AccountNature.Expense},
                    {"PL_NetIncome", AccountNature.Revenue},
                    {"PL_DepreciationExp_Total", AccountNature.Expense},
                    {"PL_EBITDA", AccountNature.Revenue}
                }

                ' ---- Scenario Pairs for Variance Calculation ----
                ' (Actual scenario, Comparison scenario, Variance prefix)
                Dim scenarioPairs As New List(Of Tuple(Of String, String, String)) From {
                    Tuple.Create("Actual", "Budget", "VAR_AvB"),
                    Tuple.Create("Actual", "PriorYear", "VAR_AvPY"),
                    Tuple.Create("Forecast", "Budget", "VAR_FvB")
                }

                ' =============================================================================================
                ' SECTION 1: Absolute and Percentage Variances
                ' =============================================================================================
                For Each pair As Tuple(Of String, String, String) In scenarioPairs

                    Dim actualScenario As String = pair.Item1
                    Dim compareScenario As String = pair.Item2
                    Dim varPrefix As String = pair.Item3

                    For Each kvp As KeyValuePair(Of String, AccountNature) In accountsToAnalyze

                        Dim acctName As String = kvp.Key
                        Dim acctNature As AccountNature = kvp.Value

                        ' Read actual/current scenario amount
                        Dim actualAmount As Double = GetAmount(si, entityName, actualScenario, timeName, acctName)

                        ' Read comparison scenario amount
                        Dim compareAmount As Double = GetAmount(si, entityName, compareScenario, timeName, acctName)

                        ' ---- Absolute Variance ----
                        Dim absVariance As Double = actualAmount - compareAmount

                        ' ---- Percentage Variance ----
                        Dim pctVariance As Double = 0
                        If Math.Abs(compareAmount) > 0.01 Then
                            pctVariance = absVariance / Math.Abs(compareAmount)
                        End If

                        ' ---- Favorable / Unfavorable Flag ----
                        ' +1 = Favorable, -1 = Unfavorable, 0 = Neutral
                        Dim favUnfavFlag As Double = 0
                        Select Case acctNature
                            Case AccountNature.Revenue
                                ' For revenue/income: positive variance = favorable
                                favUnfavFlag = If(absVariance >= 0, 1.0, -1.0)
                            Case AccountNature.Expense
                                ' For expenses: negative variance (spend less) = favorable
                                favUnfavFlag = If(absVariance <= 0, 1.0, -1.0)
                            Case AccountNature.StatAccount
                                favUnfavFlag = 0
                        End Select

                        ' ---- Write Variance Results ----
                        WriteAmount(si, api, entityName, timeName, _
                            String.Format("{0}_Abs_{1}", varPrefix, acctName), absVariance)
                        WriteAmount(si, api, entityName, timeName, _
                            String.Format("{0}_Pct_{1}", varPrefix, acctName), pctVariance)
                        WriteAmount(si, api, entityName, timeName, _
                            String.Format("{0}_FavUnfav_{1}", varPrefix, acctName), favUnfavFlag)

                    Next ' account
                Next ' scenario pair

                ' =============================================================================================
                ' SECTION 2: Volume / Price / Mix Variance Decomposition (Revenue)
                ' =============================================================================================

                Dim productLines As New List(Of String) From {
                    "Product_A", "Product_B", "Product_C", "Product_D", "Product_E"
                }

                Dim totalVolumeVariance As Double = 0
                Dim totalPriceVariance As Double = 0
                Dim totalMixVariance As Double = 0

                For Each product As String In productLines

                    ' Read Actual volume and price
                    Dim actualVol As Double = GetAmountUD(si, entityName, "Actual", timeName, "STAT_Volume", product)
                    Dim actualPrice As Double = GetAmountUD(si, entityName, "Actual", timeName, "STAT_AvgPrice", product)

                    ' Read Budget volume and price
                    Dim budgetVol As Double = GetAmountUD(si, entityName, "Budget", timeName, "STAT_Volume", product)
                    Dim budgetPrice As Double = GetAmountUD(si, entityName, "Budget", timeName, "STAT_AvgPrice", product)

                    ' Volume Variance: (Actual Volume - Budget Volume) x Budget Price
                    Dim volumeVar As Double = (actualVol - budgetVol) * budgetPrice

                    ' Price Variance: (Actual Price - Budget Price) x Actual Volume
                    Dim priceVar As Double = (actualPrice - budgetPrice) * actualVol

                    ' Total revenue variance
                    Dim totalRevenueVar As Double = (actualVol * actualPrice) - (budgetVol * budgetPrice)

                    ' Mix Variance: residual = Total - Volume - Price
                    Dim mixVar As Double = totalRevenueVar - volumeVar - priceVar

                    ' Write product-level variances
                    WriteAmountUD(si, api, entityName, timeName, "VAR_VolumeVariance", product, volumeVar)
                    WriteAmountUD(si, api, entityName, timeName, "VAR_PriceVariance", product, priceVar)
                    WriteAmountUD(si, api, entityName, timeName, "VAR_MixVariance", product, mixVar)

                    totalVolumeVariance += volumeVar
                    totalPriceVariance += priceVar
                    totalMixVariance += mixVar

                Next ' product

                ' Write total volume/price/mix variances
                WriteAmount(si, api, entityName, timeName, "VAR_TotalVolumeVariance", totalVolumeVariance)
                WriteAmount(si, api, entityName, timeName, "VAR_TotalPriceVariance", totalPriceVariance)
                WriteAmount(si, api, entityName, timeName, "VAR_TotalMixVariance", totalMixVariance)

                ' =============================================================================================
                ' SECTION 3: FX Variance
                ' =============================================================================================

                ' FX variance = Budget at actual FX rates - Budget at budget FX rates
                Dim budgetAtActualRates As Double = GetAmount(si, entityName, "Budget_AtActualRates", timeName, "PL_NetRevenue")
                Dim budgetAtBudgetRates As Double = GetAmount(si, entityName, "Budget", timeName, "PL_NetRevenue")
                Dim fxVariance As Double = budgetAtActualRates - budgetAtBudgetRates

                WriteAmount(si, api, entityName, timeName, "VAR_FX_Revenue", fxVariance)

                ' FX variance on expenses
                Dim expBudgetActualRates As Double = GetAmount(si, entityName, "Budget_AtActualRates", timeName, "PL_TotalExpenses")
                Dim expBudgetBudgetRates As Double = GetAmount(si, entityName, "Budget", timeName, "PL_TotalExpenses")
                Dim fxVarianceExpense As Double = expBudgetActualRates - expBudgetBudgetRates

                WriteAmount(si, api, entityName, timeName, "VAR_FX_Expenses", fxVarianceExpense)
                WriteAmount(si, api, entityName, timeName, "VAR_FX_NetImpact", fxVariance - fxVarianceExpense)

                ' Trigger downstream calculations
                api.Data.Calculate("A#VAR_TotalVolumeVariance")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount from cube (base dimensions)
        '----------------------------------------------------------------------------------------------------
        Private Function GetAmount(ByVal si As SessionInfo, ByVal entity As String, _
                ByVal scenario As String, ByVal time As String, ByVal account As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}", entity, scenario, time, account)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount from cube with UD1 dimension
        '----------------------------------------------------------------------------------------------------
        Private Function GetAmountUD(ByVal si As SessionInfo, ByVal entity As String, _
                ByVal scenario As String, ByVal time As String, ByVal account As String, _
                ByVal ud1 As String) As Double

            Dim pov As String = String.Format("E#{0}:S#{1}:T#{2}:A#{3}:U1#{4}", _
                entity, scenario, time, account, ud1)
            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write amount to cube (Variance scenario)
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal time As String, ByVal account As String, _
                ByVal amount As Double)

            Dim pov As String = String.Format("E#{0}:S#Variance:T#{1}:A#{2}", entity, time, account)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write amount to cube with UD1 dimension
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
