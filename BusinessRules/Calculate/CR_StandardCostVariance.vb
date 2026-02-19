'------------------------------------------------------------------------------------------------------------
' CR_StandardCostVariance
' Calculate Business Rule - 4-Way Standard Cost Variance Decomposition
'
' Purpose:  Calculates a full set of manufacturing variances including material price/usage,
'           labor rate/efficiency, overhead spending/volume, and product mix variances.
'           Writes each variance component to separate accounts for management reporting.
'
' Scope:    Finance - Calculate
' Version:  1.0
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

Namespace OneStream.BusinessRule.Finance.CR_StandardCostVariance

    Public Class MainClass

        ''' <summary>
        ''' Holds standard and actual data for a single product line used in variance analysis.
        ''' </summary>
        Private Class ProductVarianceData
            Public ProductLine As String
            Public ActualPrice As Double
            Public StandardPrice As Double
            Public ActualQty As Double
            Public StandardQty As Double
            Public ActualLaborRate As Double
            Public StandardLaborRate As Double
            Public ActualLaborHours As Double
            Public StandardLaborHours As Double
            Public ActualOHRate As Double
            Public StandardOHRate As Double
            Public ActualUnits As Double
            Public BudgetedUnits As Double
            Public StandardCostPerUnit As Double
        End Class

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_StandardCostVariance: Starting 4-way variance decomposition.")

                    '--- Define product lines for variance analysis ---
                    Dim productLines As New List(Of String) From {
                        "PROD_Alpha", "PROD_Beta", "PROD_Gamma", "PROD_Delta"
                    }

                    Dim allProductData As New List(Of ProductVarianceData)
                    Dim totalActualUnits As Double = 0
                    Dim totalBudgetedUnits As Double = 0

                    '--- Gather actual and standard data for each product ---
                    For Each pl As String In productLines
                        Dim pvd As New ProductVarianceData()
                        pvd.ProductLine = pl

                        '--- Material data ---
                        pvd.ActualPrice = ReadCell(si, api, "A#ACT_MaterialPrice", "E#" & pl)
                        pvd.StandardPrice = ReadCell(si, api, "A#STD_MaterialPrice", "E#" & pl)
                        pvd.ActualQty = ReadCell(si, api, "A#ACT_MaterialQty", "E#" & pl)
                        pvd.StandardQty = ReadCell(si, api, "A#STD_MaterialQty", "E#" & pl)

                        '--- Labor data ---
                        pvd.ActualLaborRate = ReadCell(si, api, "A#ACT_LaborRate", "E#" & pl)
                        pvd.StandardLaborRate = ReadCell(si, api, "A#STD_LaborRate", "E#" & pl)
                        pvd.ActualLaborHours = ReadCell(si, api, "A#ACT_LaborHours", "E#" & pl)
                        pvd.StandardLaborHours = ReadCell(si, api, "A#STD_LaborHours", "E#" & pl)

                        '--- Overhead data ---
                        pvd.ActualOHRate = ReadCell(si, api, "A#ACT_OHRate", "E#" & pl)
                        pvd.StandardOHRate = ReadCell(si, api, "A#STD_OHRate", "E#" & pl)

                        '--- Units for mix variance ---
                        pvd.ActualUnits = ReadCell(si, api, "A#ACT_UnitsProduced", "E#" & pl)
                        pvd.BudgetedUnits = ReadCell(si, api, "A#BUD_UnitsProduced", "E#" & pl)
                        pvd.StandardCostPerUnit = ReadCell(si, api, "A#STD_CostPerUnit", "E#" & pl)

                        totalActualUnits += pvd.ActualUnits
                        totalBudgetedUnits += pvd.BudgetedUnits

                        allProductData.Add(pvd)
                    Next

                    '--- Calculate variances for each product line ---
                    For Each pvd As ProductVarianceData In allProductData
                        Dim entity As String = "E#" & pvd.ProductLine

                        ' ============================================================
                        ' 1. Material Price Variance = (Actual Price - Std Price) x Actual Qty
                        ' ============================================================
                        Dim materialPriceVariance As Double = Math.Round(
                            (pvd.ActualPrice - pvd.StandardPrice) * pvd.ActualQty, 2)

                        ' ============================================================
                        ' 2. Material Usage Variance = (Actual Qty - Std Qty) x Std Price
                        ' ============================================================
                        Dim materialUsageVariance As Double = Math.Round(
                            (pvd.ActualQty - pvd.StandardQty) * pvd.StandardPrice, 2)

                        ' ============================================================
                        ' 3. Labor Rate Variance = (Actual Rate - Std Rate) x Actual Hours
                        ' ============================================================
                        Dim laborRateVariance As Double = Math.Round(
                            (pvd.ActualLaborRate - pvd.StandardLaborRate) * pvd.ActualLaborHours, 2)

                        ' ============================================================
                        ' 4. Labor Efficiency Variance = (Actual Hours - Std Hours) x Std Rate
                        ' ============================================================
                        Dim laborEfficiencyVariance As Double = Math.Round(
                            (pvd.ActualLaborHours - pvd.StandardLaborHours) * pvd.StandardLaborRate, 2)

                        ' ============================================================
                        ' 5. Overhead Spending Variance = Actual OH - (Actual Hours x Std OH Rate)
                        ' ============================================================
                        Dim actualOverhead As Double = pvd.ActualOHRate * pvd.ActualLaborHours
                        Dim budgetedOHAtActualHours As Double = pvd.StandardOHRate * pvd.ActualLaborHours
                        Dim ohSpendingVariance As Double = Math.Round(actualOverhead - budgetedOHAtActualHours, 2)

                        ' ============================================================
                        ' 6. Overhead Volume Variance = (Std Hours - Actual Hours) x Std OH Rate
                        ' ============================================================
                        Dim ohVolumeVariance As Double = Math.Round(
                            (pvd.StandardLaborHours - pvd.ActualLaborHours) * pvd.StandardOHRate, 2)

                        ' ============================================================
                        ' 7. Mix Variance for product mix changes
                        '    = (Actual Mix % - Budget Mix %) x Total Actual Units x Std Cost/Unit
                        ' ============================================================
                        Dim mixVariance As Double = 0
                        If totalActualUnits > 0 AndAlso totalBudgetedUnits > 0 Then
                            Dim actualMixPct As Double = pvd.ActualUnits / totalActualUnits
                            Dim budgetMixPct As Double = pvd.BudgetedUnits / totalBudgetedUnits
                            mixVariance = Math.Round(
                                (actualMixPct - budgetMixPct) * totalActualUnits * pvd.StandardCostPerUnit, 2)
                        End If

                        '--- Total variance ---
                        Dim totalVariance As Double = materialPriceVariance + materialUsageVariance _
                            + laborRateVariance + laborEfficiencyVariance _
                            + ohSpendingVariance + ohVolumeVariance + mixVariance

                        '--- Write each variance to its designated account ---
                        WriteCell(si, api, "A#VAR_MaterialPrice", entity, materialPriceVariance)
                        WriteCell(si, api, "A#VAR_MaterialUsage", entity, materialUsageVariance)
                        WriteCell(si, api, "A#VAR_LaborRate", entity, laborRateVariance)
                        WriteCell(si, api, "A#VAR_LaborEfficiency", entity, laborEfficiencyVariance)
                        WriteCell(si, api, "A#VAR_OHSpending", entity, ohSpendingVariance)
                        WriteCell(si, api, "A#VAR_OHVolume", entity, ohVolumeVariance)
                        WriteCell(si, api, "A#VAR_ProductMix", entity, mixVariance)
                        WriteCell(si, api, "A#VAR_TotalVariance", entity, totalVariance)

                        '--- Write favorability flags (positive = unfavorable for cost variances) ---
                        Dim materialFavorable As Integer = If(materialPriceVariance + materialUsageVariance <= 0, 1, 0)
                        Dim laborFavorable As Integer = If(laborRateVariance + laborEfficiencyVariance <= 0, 1, 0)
                        WriteCell(si, api, "A#STAT_MaterialFavorable", entity, materialFavorable)
                        WriteCell(si, api, "A#STAT_LaborFavorable", entity, laborFavorable)

                        BRApi.ErrorLog.LogMessage(si, "CR_StandardCostVariance: " & pvd.ProductLine _
                            & " | MatPrice=" & materialPriceVariance.ToString("N2") _
                            & " | MatUsage=" & materialUsageVariance.ToString("N2") _
                            & " | LabRate=" & laborRateVariance.ToString("N2") _
                            & " | LabEff=" & laborEfficiencyVariance.ToString("N2") _
                            & " | OHSpend=" & ohSpendingVariance.ToString("N2") _
                            & " | OHVol=" & ohVolumeVariance.ToString("N2") _
                            & " | Mix=" & mixVariance.ToString("N2") _
                            & " | Total=" & totalVariance.ToString("N2"))
                    Next

                    BRApi.ErrorLog.LogMessage(si, "CR_StandardCostVariance: Variance decomposition completed successfully.")
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_StandardCostVariance", ex.Message, ex))
            End Try
        End Function

        Private Function ReadCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                  ByVal acct As String, ByVal entity As String) As Double
            Try
                Dim pov As String = api.Pov.Scenario.Name & ":" & api.Pov.Time.Name & ":" _
                    & entity & ":" & acct & ":F#Periodic:O#Top:I#Top:C1#Top:C2#Top:C3#Top:C4#Top"
                Dim dc As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, False)
                Return dc.CellAmount
            Catch ex As Exception
                Return 0
            End Try
        End Function

        Private Sub WriteCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                              ByVal acct As String, ByVal entity As String, ByVal amount As Double)
            Try
                api.Data.SetDataCell(si, acct, entity, "F#Periodic", "O#Top", "I#Top",
                                     "C1#Top", "C2#Top", "C3#Top", "C4#Top", amount, True)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_StandardCostVariance.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace
