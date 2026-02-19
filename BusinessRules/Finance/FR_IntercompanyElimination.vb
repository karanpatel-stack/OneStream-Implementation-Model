'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_IntercompanyElimination
'------------------------------------------------------------------------------------------------------------
' Purpose:     Intercompany (IC) elimination engine. Identifies, matches, and eliminates
'              intercompany transactions between entities within the consolidation group.
'              Ensures that intra-group transactions do not inflate the consolidated financials.
'
' Elimination Categories:
'   1. IC Revenue vs IC COGS:         Eliminate IC sales and corresponding cost of goods sold
'   2. IC Receivables vs IC Payables: Eliminate IC AR and AP balances
'   3. IC Dividends:                  Eliminate IC dividend income and dividend paid
'   4. IC Loans/Borrowings:           Eliminate IC loan receivable and loan payable
'
' Matching Logic:
'   - IC transactions are identified by the Intercompany (IC) dimension partner member
'   - Each entity's IC data references the trading partner via I#[PartnerEntity]
'   - Matching occurs bilaterally: Entity A's IC AR to Entity B must match Entity B's IC AP to Entity A
'   - A configurable tolerance threshold (default $1,000) allows minor mismatches
'   - Multi-currency IC: matching is performed on translated (group currency) amounts
'
' Output:
'   - Elimination entries written to Consolidation = C_Elimination
'   - Unmatched or out-of-tolerance items are logged for investigation
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

Namespace OneStream.BusinessRule.Finance.FR_IntercompanyElimination

    Public Class MainClass

        ' Tolerance threshold for IC matching (absolute value in group currency)
        Private Const IC_TOLERANCE As Double = 1000.0

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for intercompany elimination processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessEliminations(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessEliminations: Orchestrates the IC elimination workflow.
        ' Iterates through all IC elimination pairs and applies matching and elimination logic.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessEliminations(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                             ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim parentEntity As String = api.Entity.GetName()

                ' Only process eliminations at parent (group) entity level
                If Not api.Entity.HasChildren() Then
                    Return Nothing
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_IntercompanyElimination: Processing IC eliminations for group [{parentEntity}]")

                ' Get all child entities that participate in IC transactions
                Dim childEntities As List(Of String) = Me.GetChildEntities(si, api, parentEntity)

                ' Track unmatched items for reporting
                Dim unmatchedItems As New List(Of String)

                '--------------------------------------------------------------------------------------------
                ' Process each IC elimination category
                '--------------------------------------------------------------------------------------------

                ' 1. IC Revenue vs IC COGS
                Me.EliminateRevenueAndCOGS(si, api, childEntities, parentEntity, unmatchedItems)

                ' 2. IC Receivables vs IC Payables
                Me.EliminateReceivablesAndPayables(si, api, childEntities, parentEntity, unmatchedItems)

                ' 3. IC Dividends
                Me.EliminateDividends(si, api, childEntities, parentEntity, unmatchedItems)

                ' 4. IC Loans/Borrowings
                Me.EliminateLoansAndBorrowings(si, api, childEntities, parentEntity, unmatchedItems)

                '--------------------------------------------------------------------------------------------
                ' Report unmatched/out-of-tolerance items
                '--------------------------------------------------------------------------------------------
                If unmatchedItems.Count > 0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"FR_IntercompanyElimination: WARNING - {unmatchedItems.Count} unmatched/out-of-tolerance IC items:")
                    For Each item As String In unmatchedItems
                        BRApi.ErrorLog.LogMessage(si, $"  UNMATCHED: {item}")
                    Next
                Else
                    BRApi.ErrorLog.LogMessage(si, _
                        "FR_IntercompanyElimination: All IC items matched within tolerance.")
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.ProcessEliminations", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetChildEntities: Retrieves child entities for the consolidation group.
        '----------------------------------------------------------------------------------------------------
        Private Function GetChildEntities(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                          ByVal parentEntity As String) As List(Of String)
            Try
                Dim children As New List(Of String)
                Dim memberFilter As String = $"E#{parentEntity}.Descendants"
                Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Entity.Id), memberFilter)

                If memberList IsNot Nothing Then
                    For Each member As MemberInfo In memberList
                        ' Only include base-level entities (actual operating entities with IC data)
                        If Not member.MemberHasChildren Then
                            children.Add(member.Member.Name)
                        End If
                    Next
                End If

                Return children

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.GetChildEntities", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' EliminateRevenueAndCOGS: Matches and eliminates IC Revenue (seller) vs IC COGS (buyer).
        '
        ' Example: Entity A sells to Entity B for $100
        '   Entity A: IC Revenue = $100 (I#EntityB)
        '   Entity B: IC COGS   = $100 (I#EntityA)
        '   Elimination: DR IC Revenue $100, CR IC COGS $100
        '----------------------------------------------------------------------------------------------------
        Private Sub EliminateRevenueAndCOGS(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal entities As List(Of String), ByVal parentEntity As String, _
                                             ByRef unmatchedItems As List(Of String))
            Try
                ' Build IC transaction pairs by iterating through all entity combinations
                For Each sellerEntity As String In entities
                    For Each buyerEntity As String In entities
                        ' Skip self-referencing IC (entity cannot trade with itself)
                        If String.Equals(sellerEntity, buyerEntity, StringComparison.OrdinalIgnoreCase) Then Continue For

                        ' Read seller's IC Revenue to buyer (translated amount for multi-currency matching)
                        Dim sellerRevPov As String = _
                            $"E#{sellerEntity}:A#PL_ICRevenue:C#C_Translated:I#{buyerEntity}:F#F_None:O#O_None"
                        Dim sellerRevAmount As Double = api.Data.GetDataCell(sellerRevPov).CellAmount

                        ' Read buyer's IC COGS from seller
                        Dim buyerCOGSPov As String = _
                            $"E#{buyerEntity}:A#PL_ICCOGS:C#C_Translated:I#{sellerEntity}:F#F_None:O#O_None"
                        Dim buyerCOGSAmount As Double = api.Data.GetDataCell(buyerCOGSPov).CellAmount

                        ' Skip if no IC activity between this pair
                        If sellerRevAmount = 0 AndAlso buyerCOGSAmount = 0 Then Continue For

                        ' Calculate the difference for matching
                        ' Revenue is credit (negative in OneStream convention), COGS is debit (positive)
                        Dim difference As Double = Math.Abs(Math.Abs(sellerRevAmount) - Math.Abs(buyerCOGSAmount))

                        If difference <= IC_TOLERANCE Then
                            ' Match is within tolerance -- generate elimination entries
                            ' Use the average of the two amounts for the elimination
                            Dim elimAmount As Double = (Math.Abs(sellerRevAmount) + Math.Abs(buyerCOGSAmount)) / 2.0

                            ' DR IC Revenue (reverse the credit), CR IC COGS (reverse the debit)
                            ' Written to C_Elimination at the parent entity level
                            Dim elimRevPov As String = _
                                $"E#{parentEntity}:A#PL_ICRevenue:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimRevPov, elimAmount) ' Debit to reverse revenue credit

                            Dim elimCOGSPov As String = _
                                $"E#{parentEntity}:A#PL_ICCOGS:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimCOGSPov, -elimAmount) ' Credit to reverse COGS debit

                            BRApi.ErrorLog.LogMessage(si, _
                                $"  IC Rev/COGS elimination: [{sellerEntity}]->[{buyerEntity}] Amount={elimAmount:N2} (diff={difference:N2})")
                        Else
                            ' Out of tolerance -- log as unmatched
                            unmatchedItems.Add( _
                                $"Rev/COGS: [{sellerEntity}] Rev={sellerRevAmount:N2} vs [{buyerEntity}] COGS={buyerCOGSAmount:N2}, Diff={difference:N2}")
                        End If
                    Next
                Next

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.EliminateRevenueAndCOGS", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' EliminateReceivablesAndPayables: Matches IC AR (one entity) vs IC AP (counterparty).
        '
        ' Elimination: DR IC AP, CR IC AR -- removes the intercompany balance from the consolidated BS.
        '----------------------------------------------------------------------------------------------------
        Private Sub EliminateReceivablesAndPayables(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                     ByVal entities As List(Of String), ByVal parentEntity As String, _
                                                     ByRef unmatchedItems As List(Of String))
            Try
                For Each entityA As String In entities
                    For Each entityB As String In entities
                        If String.Equals(entityA, entityB, StringComparison.OrdinalIgnoreCase) Then Continue For

                        ' Read entity A's IC AR to entity B
                        Dim arPov As String = $"E#{entityA}:A#BS_ICAR:C#C_Translated:I#{entityB}:F#F_None:O#O_None"
                        Dim arAmount As Double = api.Data.GetDataCell(arPov).CellAmount

                        ' Read entity B's IC AP to entity A
                        Dim apPov As String = $"E#{entityB}:A#BS_ICAP:C#C_Translated:I#{entityA}:F#F_None:O#O_None"
                        Dim apAmount As Double = api.Data.GetDataCell(apPov).CellAmount

                        If arAmount = 0 AndAlso apAmount = 0 Then Continue For

                        Dim difference As Double = Math.Abs(Math.Abs(arAmount) - Math.Abs(apAmount))

                        If difference <= IC_TOLERANCE Then
                            Dim elimAmount As Double = (Math.Abs(arAmount) + Math.Abs(apAmount)) / 2.0

                            ' DR IC AP (debit to remove liability), CR IC AR (credit to remove asset)
                            Dim elimAPPov As String = _
                                $"E#{parentEntity}:A#BS_ICAP:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimAPPov, elimAmount) ' Debit AP

                            Dim elimARPov As String = _
                                $"E#{parentEntity}:A#BS_ICAR:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimARPov, -elimAmount) ' Credit AR

                            BRApi.ErrorLog.LogMessage(si, _
                                $"  IC AR/AP elimination: [{entityA}] AR vs [{entityB}] AP, Amount={elimAmount:N2}")
                        Else
                            unmatchedItems.Add( _
                                $"AR/AP: [{entityA}] AR={arAmount:N2} vs [{entityB}] AP={apAmount:N2}, Diff={difference:N2}")
                        End If
                    Next
                Next

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.EliminateReceivablesAndPayables", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' EliminateDividends: Eliminates IC dividend income (parent) vs dividends paid (subsidiary).
        '
        ' When a subsidiary pays dividends to its parent within the group, the dividend income
        ' recorded by the parent and the dividend payment by the subsidiary must be eliminated.
        ' Elimination: DR Dividend Income, CR Dividends Paid
        '----------------------------------------------------------------------------------------------------
        Private Sub EliminateDividends(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                        ByVal entities As List(Of String), ByVal parentEntity As String, _
                                        ByRef unmatchedItems As List(Of String))
            Try
                For Each receiverEntity As String In entities
                    For Each payerEntity As String In entities
                        If String.Equals(receiverEntity, payerEntity, StringComparison.OrdinalIgnoreCase) Then Continue For

                        ' Read dividend income recorded by the receiving entity
                        Dim divIncomePov As String = _
                            $"E#{receiverEntity}:A#PL_ICDividendIncome:C#C_Translated:I#{payerEntity}:F#F_None:O#O_None"
                        Dim divIncomeAmount As Double = api.Data.GetDataCell(divIncomePov).CellAmount

                        ' Read dividends paid by the paying entity to the receiver
                        Dim divPaidPov As String = _
                            $"E#{payerEntity}:A#EQ_ICDividendsPaid:C#C_Translated:I#{receiverEntity}:F#F_None:O#O_None"
                        Dim divPaidAmount As Double = api.Data.GetDataCell(divPaidPov).CellAmount

                        If divIncomeAmount = 0 AndAlso divPaidAmount = 0 Then Continue For

                        Dim difference As Double = Math.Abs(Math.Abs(divIncomeAmount) - Math.Abs(divPaidAmount))

                        If difference <= IC_TOLERANCE Then
                            Dim elimAmount As Double = (Math.Abs(divIncomeAmount) + Math.Abs(divPaidAmount)) / 2.0

                            ' DR Dividend Income (reverse), CR Dividends Paid (reverse)
                            Dim elimDivIncPov As String = _
                                $"E#{parentEntity}:A#PL_ICDividendIncome:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimDivIncPov, elimAmount)

                            Dim elimDivPaidPov As String = _
                                $"E#{parentEntity}:A#EQ_ICDividendsPaid:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                            api.Data.SetDataCell(elimDivPaidPov, -elimAmount)

                            BRApi.ErrorLog.LogMessage(si, _
                                $"  IC Dividend elimination: [{receiverEntity}]<-[{payerEntity}], Amount={elimAmount:N2}")
                        Else
                            unmatchedItems.Add( _
                                $"Dividends: [{receiverEntity}] Income={divIncomeAmount:N2} vs [{payerEntity}] Paid={divPaidAmount:N2}, Diff={difference:N2}")
                        End If
                    Next
                Next

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.EliminateDividends", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' EliminateLoansAndBorrowings: Eliminates IC loan receivables vs IC borrowings.
        '
        ' When one entity within the group lends to another, the loan receivable (lender) and
        ' the borrowing/loan payable (borrower) must be eliminated on consolidation.
        ' Additionally, IC interest income and IC interest expense are eliminated.
        '----------------------------------------------------------------------------------------------------
        Private Sub EliminateLoansAndBorrowings(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                 ByVal entities As List(Of String), ByVal parentEntity As String, _
                                                 ByRef unmatchedItems As List(Of String))
            Try
                For Each lenderEntity As String In entities
                    For Each borrowerEntity As String In entities
                        If String.Equals(lenderEntity, borrowerEntity, StringComparison.OrdinalIgnoreCase) Then Continue For

                        '--- Eliminate Loan Principal (BS) ---
                        ' Lender's IC Loan Receivable
                        Dim loanRecPov As String = _
                            $"E#{lenderEntity}:A#BS_ICLoanReceivable:C#C_Translated:I#{borrowerEntity}:F#F_None:O#O_None"
                        Dim loanRecAmount As Double = api.Data.GetDataCell(loanRecPov).CellAmount

                        ' Borrower's IC Loan Payable
                        Dim loanPayPov As String = _
                            $"E#{borrowerEntity}:A#BS_ICLoanPayable:C#C_Translated:I#{lenderEntity}:F#F_None:O#O_None"
                        Dim loanPayAmount As Double = api.Data.GetDataCell(loanPayPov).CellAmount

                        If loanRecAmount <> 0 OrElse loanPayAmount <> 0 Then
                            Dim difference As Double = Math.Abs(Math.Abs(loanRecAmount) - Math.Abs(loanPayAmount))

                            If difference <= IC_TOLERANCE Then
                                Dim elimAmount As Double = (Math.Abs(loanRecAmount) + Math.Abs(loanPayAmount)) / 2.0

                                ' DR Loan Payable, CR Loan Receivable
                                Dim elimPayPov As String = _
                                    $"E#{parentEntity}:A#BS_ICLoanPayable:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                                api.Data.SetDataCell(elimPayPov, elimAmount)

                                Dim elimRecPov As String = _
                                    $"E#{parentEntity}:A#BS_ICLoanReceivable:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                                api.Data.SetDataCell(elimRecPov, -elimAmount)

                                BRApi.ErrorLog.LogMessage(si, _
                                    $"  IC Loan elimination: [{lenderEntity}]->[{borrowerEntity}], Amount={elimAmount:N2}")
                            Else
                                unmatchedItems.Add( _
                                    $"Loan: [{lenderEntity}] Rec={loanRecAmount:N2} vs [{borrowerEntity}] Pay={loanPayAmount:N2}, Diff={difference:N2}")
                            End If
                        End If

                        '--- Eliminate Interest Income vs Interest Expense (P&L) ---
                        Dim intIncPov As String = _
                            $"E#{lenderEntity}:A#PL_ICInterestIncome:C#C_Translated:I#{borrowerEntity}:F#F_None:O#O_None"
                        Dim intIncAmount As Double = api.Data.GetDataCell(intIncPov).CellAmount

                        Dim intExpPov As String = _
                            $"E#{borrowerEntity}:A#PL_ICInterestExpense:C#C_Translated:I#{lenderEntity}:F#F_None:O#O_None"
                        Dim intExpAmount As Double = api.Data.GetDataCell(intExpPov).CellAmount

                        If intIncAmount <> 0 OrElse intExpAmount <> 0 Then
                            Dim intDiff As Double = Math.Abs(Math.Abs(intIncAmount) - Math.Abs(intExpAmount))

                            If intDiff <= IC_TOLERANCE Then
                                Dim elimIntAmount As Double = (Math.Abs(intIncAmount) + Math.Abs(intExpAmount)) / 2.0

                                ' DR Interest Income, CR Interest Expense
                                Dim elimIntIncPov As String = _
                                    $"E#{parentEntity}:A#PL_ICInterestIncome:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                                api.Data.SetDataCell(elimIntIncPov, elimIntAmount)

                                Dim elimIntExpPov As String = _
                                    $"E#{parentEntity}:A#PL_ICInterestExpense:C#C_Elimination:I#IC_Elim:F#F_None:O#O_None"
                                api.Data.SetDataCell(elimIntExpPov, -elimIntAmount)

                                BRApi.ErrorLog.LogMessage(si, _
                                    $"  IC Interest elimination: [{lenderEntity}]->[{borrowerEntity}], Amount={elimIntAmount:N2}")
                            Else
                                unmatchedItems.Add( _
                                    $"Interest: [{lenderEntity}] Inc={intIncAmount:N2} vs [{borrowerEntity}] Exp={intExpAmount:N2}, Diff={intDiff:N2}")
                            End If
                        End If
                    Next
                Next

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_IntercompanyElimination.EliminateLoansAndBorrowings", ex.Message))
            End Try
        End Sub

    End Class

End Namespace
