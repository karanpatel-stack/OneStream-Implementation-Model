'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_GoodwillImpairment
'------------------------------------------------------------------------------------------------------------
' Purpose:     Manages goodwill and intangible asset accounting, including annual impairment testing
'              and straight-line amortization of finite-life intangible assets.
'
' Goodwill Impairment Testing (per IFRS - IAS 36 / US GAAP - ASC 350):
'   - Goodwill is tested for impairment at least annually (or when triggering events occur)
'   - Compare carrying value of the reporting unit (CGU) to its fair value
'   - If carrying value > fair value, recognize impairment loss = difference
'   - Under simplified US GAAP (ASU 2017-04): one-step test, impairment = excess of carrying over fair
'   - Under IFRS: impairment allocated first to goodwill, then proportionally to other assets
'
' Intangible Asset Amortization:
'   - Finite-life intangibles (patents, customer lists, trademarks with expiry) are amortized
'   - Straight-line method: Annual Amortization = Cost / Useful Life
'   - Accumulated amortization tracks total amortization to date
'   - Net Book Value = Cost - Accumulated Amortization
'
' Data Intersections:
'   - Goodwill carrying value:  A#BS_Goodwill
'   - Fair value (input):       A#BS_Goodwill_FairValue (entered by user or loaded from external)
'   - Impairment charge (P&L):  A#PL_GoodwillImpairment
'   - Intangible cost:          A#BS_IntangibleAssets_Cost
'   - Accumulated amortization: A#BS_IntangibleAssets_AccumAmort
'   - Amortization expense:     A#PL_AmortizationExpense
'
' Author:       OneStream Administrator
' Created:      2026-02-18
' Modified:     2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.FR_GoodwillImpairment

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for goodwill impairment and intangible amortization processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessGoodwillAndIntangibles(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_GoodwillImpairment.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessGoodwillAndIntangibles: Orchestrates both goodwill impairment testing and
        ' intangible asset amortization for the current entity.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessGoodwillAndIntangibles(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                                        ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim entityName As String = api.Entity.GetName()

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_GoodwillImpairment: Processing goodwill and intangibles for [{entityName}]")

                ' Step 1: Goodwill impairment test
                Me.PerformGoodwillImpairmentTest(si, api, entityName)

                ' Step 2: Intangible asset amortization
                Me.CalculateIntangibleAmortization(si, api, entityName)

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_GoodwillImpairment.ProcessGoodwillAndIntangibles", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' PerformGoodwillImpairmentTest: Compares carrying value to fair value and records
        ' impairment loss if carrying value exceeds fair value.
        '
        ' Carrying Value of Reporting Unit = Net Assets including Goodwill
        ' Fair Value = Externally provided (e.g., from valuation, discounted cash flows)
        '
        ' If Carrying > Fair:
        '   Impairment Loss = Carrying - Fair (capped at the goodwill balance)
        '   DR  Impairment Charge (P&L expense)
        '   CR  Goodwill (BS asset reduction)
        '----------------------------------------------------------------------------------------------------
        Private Sub PerformGoodwillImpairmentTest(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                   ByVal entityName As String)
            Try
                ' Read the goodwill carrying value (gross, on the balance sheet)
                Dim goodwillPov As String = $"E#{entityName}:A#BS_Goodwill:C#C_Local:F#F_None:O#O_None:I#I_None"
                Dim goodwillCarrying As Double = api.Data.GetDataCell(goodwillPov).CellAmount

                ' If no goodwill exists, skip impairment test
                If goodwillCarrying = 0 Then
                    BRApi.ErrorLog.LogMessage(si, $"  No goodwill balance for [{entityName}], skipping impairment test")
                    Return
                End If

                ' Read the fair value of the reporting unit (entered as input or loaded from external source)
                Dim fairValuePov As String = $"E#{entityName}:A#BS_Goodwill_FairValue:C#C_Local:F#F_None:O#O_None:I#I_None"
                Dim fairValue As Double = api.Data.GetDataCell(fairValuePov).CellAmount

                ' Read the total carrying value of the reporting unit (net assets)
                Dim netAssetsPov As String = $"E#{entityName}:A#BS_NetAssets:C#C_Local:F#F_None:O#O_None:I#I_None"
                Dim netAssetsCarrying As Double = api.Data.GetDataCell(netAssetsPov).CellAmount

                ' If fair value has not been provided, skip (test cannot be performed without it)
                If fairValue = 0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  WARNING: No fair value provided for [{entityName}]. Goodwill impairment test cannot be performed.")
                    Return
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Goodwill test [{entityName}]: Carrying={netAssetsCarrying:N2}, FairValue={fairValue:N2}, Goodwill={goodwillCarrying:N2}")

                ' Perform the impairment comparison
                If netAssetsCarrying > fairValue Then
                    ' Impairment exists
                    Dim rawImpairment As Double = netAssetsCarrying - fairValue

                    ' Cap impairment at the goodwill balance (cannot impair more than the goodwill amount)
                    Dim impairmentLoss As Double = Math.Min(rawImpairment, goodwillCarrying)

                    BRApi.ErrorLog.LogMessage(si, _
                        $"  IMPAIRMENT DETECTED: Loss={impairmentLoss:N2} (raw excess={rawImpairment:N2}, capped at goodwill={goodwillCarrying:N2})")

                    ' Record impairment charge to P&L
                    ' Debit: Impairment expense (increases expenses, reduces net income)
                    Dim impairmentExpPov As String = _
                        $"E#{entityName}:A#PL_GoodwillImpairment:C#C_Local:F#F_None:O#O_None:I#I_None"
                    api.Data.SetDataCell(impairmentExpPov, impairmentLoss)

                    ' Credit: Reduce goodwill on Balance Sheet
                    ' Write the impairment as a negative adjustment to accumulated impairment account
                    Dim impairmentBSPov As String = _
                        $"E#{entityName}:A#BS_Goodwill_AccumImpairment:C#C_Local:F#F_None:O#O_None:I#I_None"
                    api.Data.SetDataCell(impairmentBSPov, -impairmentLoss)

                Else
                    ' No impairment -- fair value exceeds carrying value
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  No impairment for [{entityName}]: Fair value ({fairValue:N2}) >= Carrying ({netAssetsCarrying:N2})")
                End If

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_GoodwillImpairment.PerformGoodwillImpairmentTest", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateIntangibleAmortization: Computes straight-line amortization for finite-life
        ' intangible assets. Reads cost, useful life, and accumulated amortization, then calculates
        ' the current period amortization expense.
        '
        ' Monthly Amortization = Original Cost / (Useful Life in Years x 12)
        ' Net Book Value = Cost - Accumulated Amortization
        ' If NBV reaches zero, no further amortization is recorded (fully amortized).
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateIntangibleAmortization(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                     ByVal entityName As String)
            Try
                ' Read intangible asset original cost
                Dim costPov As String = $"E#{entityName}:A#BS_IntangibleAssets_Cost:C#C_Local:F#F_None:O#O_None:I#I_None"
                Dim originalCost As Double = api.Data.GetDataCell(costPov).CellAmount

                ' If no intangible assets exist, skip amortization
                If originalCost = 0 Then
                    BRApi.ErrorLog.LogMessage(si, $"  No intangible assets for [{entityName}], skipping amortization")
                    Return
                End If

                ' Read useful life in years from entity/account property
                Dim usefulLifeStr As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "IntangibleUsefulLife")
                Dim usefulLifeYears As Double = 10.0 ' Default 10 years if not specified

                If Not String.IsNullOrEmpty(usefulLifeStr) Then
                    Double.TryParse(usefulLifeStr, NumberStyles.Any, CultureInfo.InvariantCulture, usefulLifeYears)
                End If

                ' Guard against invalid useful life
                If usefulLifeYears <= 0 Then usefulLifeYears = 10.0

                ' Read accumulated amortization to date (prior period closing balance)
                Dim accumAmortPov As String = _
                    $"E#{entityName}:A#BS_IntangibleAssets_AccumAmort:C#C_Local:F#F_None:O#O_None:I#I_None:T#PriorPeriod"
                Dim accumAmort As Double = Math.Abs(api.Data.GetDataCell(accumAmortPov).CellAmount)

                ' Calculate net book value before current period amortization
                Dim nbvBeforeAmort As Double = originalCost - accumAmort

                ' If fully amortized, skip
                If nbvBeforeAmort <= 0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  Intangible assets for [{entityName}] fully amortized. Cost={originalCost:N2}, AccumAmort={accumAmort:N2}")
                    Return
                End If

                ' Calculate monthly straight-line amortization
                Dim monthlyAmort As Double = originalCost / (usefulLifeYears * 12.0)

                ' Cap at remaining NBV (cannot amortize below zero)
                Dim currentAmort As Double = Math.Min(monthlyAmort, nbvBeforeAmort)

                ' Record amortization expense (P&L debit)
                Dim amortExpPov As String = _
                    $"E#{entityName}:A#PL_AmortizationExpense:C#C_Local:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(amortExpPov, currentAmort)

                ' Update accumulated amortization (BS credit / contra-asset)
                Dim newAccumAmort As Double = accumAmort + currentAmort
                Dim accumAmortCurrentPov As String = _
                    $"E#{entityName}:A#BS_IntangibleAssets_AccumAmort:C#C_Local:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(accumAmortCurrentPov, -newAccumAmort) ' Negative = contra-asset

                Dim nbvAfterAmort As Double = originalCost - newAccumAmort

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Intangible amort [{entityName}]: Cost={originalCost:N2}, Monthly={currentAmort:N2}, " & _
                    $"AccumAmort={newAccumAmort:N2}, NBV={nbvAfterAmort:N2} (Life={usefulLifeYears} yrs)")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_GoodwillImpairment.CalculateIntangibleAmortization", ex.Message))
            End Try
        End Sub

    End Class

End Namespace
