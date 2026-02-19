'------------------------------------------------------------------------------------------------------------
' CR_IntercompanyMarkup
' Calculate Business Rule - Transfer Pricing and Intercompany Profit
'
' Purpose:  Reads intercompany transaction data between entities, applies markup rates by
'           transaction type (goods, services, royalties), calculates arm's-length transfer
'           prices, determines IC profit/markup amounts, and flags transactions for elimination.
'           Supports cost-plus, resale-minus, and TNMM (Transactional Net Margin Method).
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

Namespace OneStream.BusinessRule.Finance.CR_IntercompanyMarkup

    Public Class MainClass

        ''' <summary>
        ''' Defines an intercompany transaction pair and its transfer pricing parameters.
        ''' </summary>
        Private Class ICTransactionDef
            Public SellingEntity As String
            Public BuyingEntity As String
            Public TransactionType As String      ' Goods, Services, Royalties
            Public PricingMethod As String        ' CostPlus, ResaleMinus, TNMM
            Public MarkupPct As Double            ' Markup percentage (e.g., 0.10 = 10%)
            Public CostAccount As String          ' Account holding the cost base
            Public RevenueAccount As String       ' Account for IC revenue on seller side
            Public ExpenseAccount As String       ' Account for IC expense on buyer side
        End Class

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup: Starting transfer pricing calculation.")

                    '--- Define IC transaction relationships ---
                    Dim icTransactions As New List(Of ICTransactionDef)

                    ' Manufacturing entity sells goods to distribution entities
                    icTransactions.Add(CreateICDef("ENT_MfgUS", "ENT_DistEU", "Goods", "CostPlus", 0.10,
                        "A#IC_CostOfGoods", "A#IC_RevGoods", "A#IC_ExpGoods"))
                    icTransactions.Add(CreateICDef("ENT_MfgUS", "ENT_DistAPAC", "Goods", "CostPlus", 0.12,
                        "A#IC_CostOfGoods", "A#IC_RevGoods", "A#IC_ExpGoods"))

                    ' Shared services entity provides services to operating entities
                    icTransactions.Add(CreateICDef("ENT_SharedSvc", "ENT_MfgUS", "Services", "CostPlus", 0.05,
                        "A#IC_CostOfServices", "A#IC_RevServices", "A#IC_ExpServices"))
                    icTransactions.Add(CreateICDef("ENT_SharedSvc", "ENT_DistEU", "Services", "CostPlus", 0.05,
                        "A#IC_CostOfServices", "A#IC_RevServices", "A#IC_ExpServices"))

                    ' IP holding entity charges royalties
                    icTransactions.Add(CreateICDef("ENT_IPHolding", "ENT_MfgUS", "Royalties", "TNMM", 0.03,
                        "A#IC_RoyaltyBase", "A#IC_RevRoyalties", "A#IC_ExpRoyalties"))
                    icTransactions.Add(CreateICDef("ENT_IPHolding", "ENT_DistEU", "Royalties", "TNMM", 0.03,
                        "A#IC_RoyaltyBase", "A#IC_RevRoyalties", "A#IC_ExpRoyalties"))

                    ' Distribution entity resells to end-market entity
                    icTransactions.Add(CreateICDef("ENT_DistEU", "ENT_RetailEU", "Goods", "ResaleMinus", 0.20,
                        "A#IC_ResalePrice", "A#IC_RevGoods", "A#IC_ExpGoods"))

                    Dim totalICMarkup As Double = 0
                    Dim totalICElimination As Double = 0

                    '--- Process each IC transaction ---
                    For Each icDef As ICTransactionDef In icTransactions
                        Dim sellerEntity As String = "E#" & icDef.SellingEntity
                        Dim buyerEntity As String = "E#" & icDef.BuyingEntity

                        '--- Read the cost base or revenue base depending on pricing method ---
                        Dim baseAmount As Double = ReadCell(si, api, icDef.CostAccount, sellerEntity)

                        If baseAmount = 0 Then
                            BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup: Skipping " _
                                & icDef.SellingEntity & " -> " & icDef.BuyingEntity & " (zero base)")
                            Continue For
                        End If

                        '--- Calculate transfer price based on pricing method ---
                        Dim transferPrice As Double = 0
                        Dim markupAmount As Double = 0

                        Select Case icDef.PricingMethod
                            Case "CostPlus"
                                ' Transfer Price = Cost x (1 + Markup%)
                                markupAmount = Math.Round(baseAmount * icDef.MarkupPct, 2)
                                transferPrice = baseAmount + markupAmount

                            Case "ResaleMinus"
                                ' Transfer Price = Resale Price x (1 - Margin%)
                                ' baseAmount is the resale price in this method
                                transferPrice = Math.Round(baseAmount * (1 - icDef.MarkupPct), 2)
                                markupAmount = baseAmount - transferPrice

                            Case "TNMM"
                                ' Transfer Price = Revenue Base x Markup%
                                ' For royalties, base is the revenue of the buying entity
                                Dim buyerRevenue As Double = ReadCell(si, api, icDef.CostAccount, buyerEntity)
                                markupAmount = Math.Round(buyerRevenue * icDef.MarkupPct, 2)
                                transferPrice = markupAmount  ' Royalty amount = the transfer price

                            Case Else
                                BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup: Unknown method " & icDef.PricingMethod)
                                Continue For
                        End Select

                        '--- Write IC revenue on selling entity side ---
                        WriteCell(si, api, icDef.RevenueAccount, sellerEntity, transferPrice)

                        '--- Write IC expense on buying entity side ---
                        WriteCell(si, api, icDef.ExpenseAccount, buyerEntity, transferPrice)

                        '--- Write markup / profit amount ---
                        WriteCell(si, api, "A#IC_MarkupAmount", sellerEntity, markupAmount)

                        '--- Flag for elimination (write elimination amounts) ---
                        ' Elimination is the full transfer price that nets to zero in consolidation
                        WriteCell(si, api, "A#IC_ElimRevenue", sellerEntity, -transferPrice)
                        WriteCell(si, api, "A#IC_ElimExpense", buyerEntity, -transferPrice)

                        '--- Write audit trail data ---
                        WriteCell(si, api, "A#IC_TransferPrice", sellerEntity, transferPrice)
                        WriteCell(si, api, "A#IC_BaseAmount", sellerEntity, baseAmount)
                        WriteCell(si, api, "A#IC_MarkupPct", sellerEntity, icDef.MarkupPct)

                        totalICMarkup += markupAmount
                        totalICElimination += transferPrice

                        BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup: " _
                            & icDef.SellingEntity & " -> " & icDef.BuyingEntity _
                            & " | Type=" & icDef.TransactionType _
                            & " | Method=" & icDef.PricingMethod _
                            & " | Base=" & baseAmount.ToString("N2") _
                            & " | Price=" & transferPrice.ToString("N2") _
                            & " | Markup=" & markupAmount.ToString("N2"))
                    Next

                    '--- Write consolidated IC totals ---
                    WriteCell(si, api, "A#IC_TotalMarkup", "E#Eliminations", totalICMarkup)
                    WriteCell(si, api, "A#IC_TotalElimination", "E#Eliminations", totalICElimination)

                    BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup: Completed. Total Markup=" _
                        & totalICMarkup.ToString("N2") & " Total Elimination=" & totalICElimination.ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_IntercompanyMarkup", ex.Message, ex))
            End Try
        End Function

        ''' <summary>
        ''' Factory method to create an IC transaction definition.
        ''' </summary>
        Private Function CreateICDef(ByVal seller As String, ByVal buyer As String,
                                     ByVal txnType As String, ByVal method As String,
                                     ByVal markupPct As Double, ByVal costAcct As String,
                                     ByVal revAcct As String, ByVal expAcct As String) As ICTransactionDef
            Dim def As New ICTransactionDef()
            def.SellingEntity = seller
            def.BuyingEntity = buyer
            def.TransactionType = txnType
            def.PricingMethod = method
            def.MarkupPct = markupPct
            def.CostAccount = costAcct
            def.RevenueAccount = revAcct
            def.ExpenseAccount = expAcct
            Return def
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
                BRApi.ErrorLog.LogMessage(si, "CR_IntercompanyMarkup.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace
