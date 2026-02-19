'------------------------------------------------------------------------------------------------------------
' CR_InventoryValuation
' Calculate Business Rule - Inventory Valuation
'
' Purpose:  Calculates inventory values using standard costing (material + labor + overhead per unit),
'           computes inventory layers (raw materials, WIP, finished goods), handles standard cost
'           variance capitalization, calculates inventory reserves/obsolescence, performs LCNRV
'           (Lower of Cost or Net Realizable Value) testing, and writes results to balance sheet accounts.
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

Namespace OneStream.BusinessRule.Finance.CR_InventoryValuation

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_InventoryValuation: Starting inventory valuation calculation.")

                    '--- Define product SKUs ---
                    Dim products As New List(Of String) From {
                        "SKU_A100", "SKU_B200", "SKU_C300", "SKU_D400", "SKU_E500"
                    }

                    '--- Obsolescence parameters ---
                    Dim obsolescenceAgingBuckets As New Dictionary(Of Integer, Double) From {
                        {90, 0.0},       ' 0-90 days: no reserve
                        {180, 0.25},     ' 91-180 days: 25% reserve
                        {270, 0.50},     ' 181-270 days: 50% reserve
                        {365, 0.75},     ' 271-365 days: 75% reserve
                        {Integer.MaxValue, 1.0}  ' >365 days: 100% reserve
                    }

                    Dim totalRawMaterial As Double = 0
                    Dim totalWIP As Double = 0
                    Dim totalFinishedGoods As Double = 0
                    Dim totalObsolescenceReserve As Double = 0
                    Dim totalLCNRVAdjustment As Double = 0

                    For Each sku As String In products
                        Dim entity As String = "E#" & sku

                        ' ============================================================
                        ' Standard Costing: Material + Labor + Overhead per Unit
                        ' ============================================================
                        Dim stdMaterialPerUnit As Double = ReadCell(si, api, "A#INV_StdMaterialPerUnit", entity)
                        Dim stdLaborPerUnit As Double = ReadCell(si, api, "A#INV_StdLaborPerUnit", entity)
                        Dim stdOverheadPerUnit As Double = ReadCell(si, api, "A#INV_StdOverheadPerUnit", entity)
                        Dim stdCostPerUnit As Double = stdMaterialPerUnit + stdLaborPerUnit + stdOverheadPerUnit

                        ' ============================================================
                        ' Inventory Layers: Raw Materials, WIP, Finished Goods
                        ' ============================================================
                        '--- Raw Materials ---
                        Dim rawMaterialQty As Double = ReadCell(si, api, "A#INV_RawMaterialQty", entity)
                        Dim rawMaterialValue As Double = Math.Round(rawMaterialQty * stdMaterialPerUnit, 2)

                        '--- Work In Progress (WIP) ---
                        Dim wipQty As Double = ReadCell(si, api, "A#INV_WIPQty", entity)
                        Dim wipCompletionPct As Double = ReadCell(si, api, "A#INV_WIPCompletionPct", entity)
                        ' WIP = material (100%) + conversion costs proportional to completion
                        Dim wipValue As Double = Math.Round(
                            wipQty * (stdMaterialPerUnit + (stdLaborPerUnit + stdOverheadPerUnit) * wipCompletionPct), 2)

                        '--- Finished Goods ---
                        Dim fgQty As Double = ReadCell(si, api, "A#INV_FinishedGoodsQty", entity)
                        Dim fgValue As Double = Math.Round(fgQty * stdCostPerUnit, 2)

                        ' ============================================================
                        ' Standard Cost Variance Capitalization
                        ' A portion of production variances stays in inventory; the rest goes to COGS
                        ' ============================================================
                        Dim totalProductionVariance As Double = ReadCell(si, api, "A#VAR_TotalVariance", entity)
                        Dim inventoryTurnoverRatio As Double = ReadCell(si, api, "A#INV_TurnoverRatio", entity)

                        ' Capitalization ratio: inverse of turnover (higher turnover = less stays in inventory)
                        Dim capitalizationPct As Double = 0
                        If inventoryTurnoverRatio > 0 Then
                            capitalizationPct = Math.Min(1.0 / inventoryTurnoverRatio, 1.0)
                        End If

                        Dim varianceToInventory As Double = Math.Round(totalProductionVariance * capitalizationPct, 2)
                        Dim varianceToCOGS As Double = Math.Round(totalProductionVariance - varianceToInventory, 2)

                        ' ============================================================
                        ' Inventory Obsolescence Reserve
                        ' Based on aging of finished goods inventory
                        ' ============================================================
                        Dim avgDaysOnHand As Double = ReadCell(si, api, "A#INV_AvgDaysOnHand", entity)
                        Dim reservePct As Double = 0

                        For Each bucket In obsolescenceAgingBuckets
                            If avgDaysOnHand <= bucket.Key Then
                                reservePct = bucket.Value
                                Exit For
                            End If
                        Next

                        Dim obsolescenceReserve As Double = Math.Round(fgValue * reservePct, 2)

                        ' ============================================================
                        ' LCNRV Test: Lower of Cost or Net Realizable Value
                        ' NRV = Estimated Selling Price - Estimated Costs to Complete - Selling Costs
                        ' ============================================================
                        Dim estimatedSellingPrice As Double = ReadCell(si, api, "A#INV_EstSellingPrice", entity)
                        Dim costsToComplete As Double = ReadCell(si, api, "A#INV_CostsToComplete", entity)
                        Dim sellingCosts As Double = ReadCell(si, api, "A#INV_SellingCosts", entity)

                        Dim nrv As Double = estimatedSellingPrice - costsToComplete - sellingCosts
                        Dim nrvPerUnit As Double = If(fgQty > 0, nrv / fgQty, 0)

                        Dim lcnrvAdjustment As Double = 0
                        If stdCostPerUnit > nrvPerUnit AndAlso nrvPerUnit > 0 Then
                            ' Write down to NRV
                            lcnrvAdjustment = Math.Round((stdCostPerUnit - nrvPerUnit) * fgQty, 2)
                        End If

                        ' ============================================================
                        ' Net Inventory Value
                        ' ============================================================
                        Dim grossInventory As Double = rawMaterialValue + wipValue + fgValue + varianceToInventory
                        Dim netInventory As Double = grossInventory - obsolescenceReserve - lcnrvAdjustment

                        ' ============================================================
                        ' Write to Balance Sheet Accounts
                        ' ============================================================
                        WriteCell(si, api, "A#BS_InvRawMaterials", entity, rawMaterialValue)
                        WriteCell(si, api, "A#BS_InvWIP", entity, wipValue)
                        WriteCell(si, api, "A#BS_InvFinishedGoods", entity, fgValue)
                        WriteCell(si, api, "A#BS_InvStdCostPerUnit", entity, stdCostPerUnit)
                        WriteCell(si, api, "A#BS_InvVarianceCapitalized", entity, varianceToInventory)
                        WriteCell(si, api, "A#BS_InvVarianceToCOGS", entity, varianceToCOGS)
                        WriteCell(si, api, "A#BS_InvObsolescenceReserve", entity, -obsolescenceReserve)
                        WriteCell(si, api, "A#BS_InvLCNRVAdjustment", entity, -lcnrvAdjustment)
                        WriteCell(si, api, "A#BS_InvGrossValue", entity, grossInventory)
                        WriteCell(si, api, "A#BS_InvNetValue", entity, netInventory)

                        '--- Accumulate totals ---
                        totalRawMaterial += rawMaterialValue
                        totalWIP += wipValue
                        totalFinishedGoods += fgValue
                        totalObsolescenceReserve += obsolescenceReserve
                        totalLCNRVAdjustment += lcnrvAdjustment

                        BRApi.ErrorLog.LogMessage(si, "CR_InventoryValuation: " & sku _
                            & " | RM=" & rawMaterialValue.ToString("N2") _
                            & " | WIP=" & wipValue.ToString("N2") _
                            & " | FG=" & fgValue.ToString("N2") _
                            & " | Reserve=" & obsolescenceReserve.ToString("N2") _
                            & " | LCNRV=" & lcnrvAdjustment.ToString("N2") _
                            & " | Net=" & netInventory.ToString("N2"))
                    Next

                    '--- Write consolidated inventory totals ---
                    Dim totalEntity As String = "E#Total_Inventory"
                    WriteCell(si, api, "A#BS_InvRawMaterials", totalEntity, totalRawMaterial)
                    WriteCell(si, api, "A#BS_InvWIP", totalEntity, totalWIP)
                    WriteCell(si, api, "A#BS_InvFinishedGoods", totalEntity, totalFinishedGoods)
                    WriteCell(si, api, "A#BS_InvObsolescenceReserve", totalEntity, -totalObsolescenceReserve)
                    WriteCell(si, api, "A#BS_InvLCNRVAdjustment", totalEntity, -totalLCNRVAdjustment)
                    WriteCell(si, api, "A#BS_InvNetValue", totalEntity,
                        totalRawMaterial + totalWIP + totalFinishedGoods - totalObsolescenceReserve - totalLCNRVAdjustment)

                    BRApi.ErrorLog.LogMessage(si, "CR_InventoryValuation: Completed. Total Inventory=" _
                        & (totalRawMaterial + totalWIP + totalFinishedGoods).ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_InventoryValuation", ex.Message, ex))
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
                BRApi.ErrorLog.LogMessage(si, "CR_InventoryValuation.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace
