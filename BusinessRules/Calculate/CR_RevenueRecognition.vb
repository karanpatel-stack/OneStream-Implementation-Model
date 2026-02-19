'------------------------------------------------------------------------------------------------------------
' CR_RevenueRecognition
' Calculate Business Rule - ASC 606 Revenue Recognition
'
' Purpose:  Implements the 5-step ASC 606 revenue recognition model:
'           1) Identify performance obligations
'           2) Determine transaction price
'           3) Allocate transaction price to performance obligations
'           4) Recognize revenue as obligations are satisfied
'           5) Handle variable consideration (rebates, discounts) and contract modifications
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

Namespace OneStream.BusinessRule.Finance.CR_RevenueRecognition

    Public Class MainClass

        ''' <summary>
        ''' Represents a single performance obligation within a revenue contract.
        ''' </summary>
        Private Class PerformanceObligation
            Public ObligationId As String
            Public Description As String
            Public StandaloneSellPrice As Double
            Public AllocatedPrice As Double
            Public PctComplete As Double          ' 0.0 to 1.0
            Public IsSatisfied As Boolean
            Public RecognitionMethod As String    ' "PointInTime" or "OverTime"
        End Class

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition: Starting ASC 606 revenue recognition.")

                    '--- Define revenue streams (contract types) ---
                    Dim contractTypes As New List(Of String) From {
                        "CT_ProductSales", "CT_ServiceContracts", "CT_Licensing", "CT_BundledDeals"
                    }

                    Dim totalRecognizedRevenue As Double = 0
                    Dim totalDeferredRevenue As Double = 0

                    For Each ct As String In contractTypes
                        Dim entity As String = "E#" & ct

                        ' ============================================================
                        ' Step 1: Identify Performance Obligations
                        ' Read the number of obligations and their properties
                        ' ============================================================
                        Dim numObligations As Integer = CInt(ReadCell(si, api, "A#REV_NumObligations", entity))
                        If numObligations <= 0 Then
                            BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition: No obligations for " & ct)
                            Continue For
                        End If

                        Dim obligations As New List(Of PerformanceObligation)
                        Dim totalSSP As Double = 0

                        For idx As Integer = 1 To numObligations
                            Dim po As New PerformanceObligation()
                            po.ObligationId = ct & "_PO" & idx.ToString()

                            '--- Read standalone selling price for this obligation ---
                            po.StandaloneSellPrice = ReadCell(si, api, "A#REV_SSP_PO" & idx.ToString(), entity)
                            po.PctComplete = ReadCell(si, api, "A#REV_PctComplete_PO" & idx.ToString(), entity)
                            po.RecognitionMethod = If(ReadCell(si, api, "A#REV_OverTime_PO" & idx.ToString(), entity) = 1,
                                                     "OverTime", "PointInTime")
                            po.IsSatisfied = (po.PctComplete >= 1.0)

                            totalSSP += po.StandaloneSellPrice
                            obligations.Add(po)
                        Next

                        ' ============================================================
                        ' Step 2: Determine Transaction Price
                        ' ============================================================
                        Dim contractPrice As Double = ReadCell(si, api, "A#REV_ContractPrice", entity)

                        '--- Handle variable consideration ---
                        Dim estimatedRebates As Double = ReadCell(si, api, "A#REV_EstRebates", entity)
                        Dim estimatedDiscounts As Double = ReadCell(si, api, "A#REV_EstDiscounts", entity)
                        Dim volumeBonuses As Double = ReadCell(si, api, "A#REV_VolumeBonuses", entity)

                        '--- Apply constraint on variable consideration (most likely amount method) ---
                        Dim variableConsideration As Double = volumeBonuses - estimatedRebates - estimatedDiscounts
                        Dim constrainedVC As Double = variableConsideration * 0.85  ' Conservative estimate

                        Dim transactionPrice As Double = contractPrice + constrainedVC

                        '--- Handle contract modifications ---
                        Dim modificationAmount As Double = ReadCell(si, api, "A#REV_ContractMod", entity)
                        If modificationAmount <> 0 Then
                            ' Treat as modification to existing contract (cumulative catch-up)
                            transactionPrice += modificationAmount
                            BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition: Contract modification of " _
                                & modificationAmount.ToString("N2") & " applied to " & ct)
                        End If

                        ' ============================================================
                        ' Step 3: Allocate Transaction Price to Performance Obligations
                        ' Based on relative standalone selling prices
                        ' ============================================================
                        If totalSSP > 0 Then
                            Dim runningAllocation As Double = 0
                            For idx As Integer = 0 To obligations.Count - 1
                                If idx = obligations.Count - 1 Then
                                    ' Last obligation gets remainder to avoid rounding
                                    obligations(idx).AllocatedPrice = transactionPrice - runningAllocation
                                Else
                                    obligations(idx).AllocatedPrice = Math.Round(
                                        transactionPrice * (obligations(idx).StandaloneSellPrice / totalSSP), 2)
                                    runningAllocation += obligations(idx).AllocatedPrice
                                End If
                            Next
                        End If

                        ' ============================================================
                        ' Step 4: Recognize Revenue as Obligations are Satisfied
                        ' ============================================================
                        Dim contractRecognized As Double = 0
                        Dim contractDeferred As Double = 0

                        For idx As Integer = 0 To obligations.Count - 1
                            Dim po As PerformanceObligation = obligations(idx)
                            Dim recognizedAmount As Double = 0

                            If po.RecognitionMethod = "PointInTime" Then
                                ' Recognize 100% when obligation is satisfied
                                If po.IsSatisfied Then
                                    recognizedAmount = po.AllocatedPrice
                                End If
                            Else
                                ' Over time: recognize based on percentage of completion
                                recognizedAmount = Math.Round(po.AllocatedPrice * po.PctComplete, 2)
                            End If

                            Dim deferredAmount As Double = po.AllocatedPrice - recognizedAmount

                            contractRecognized += recognizedAmount
                            contractDeferred += deferredAmount

                            '--- Write per-obligation amounts ---
                            Dim poIdx As String = (idx + 1).ToString()
                            WriteCell(si, api, "A#REV_Allocated_PO" & poIdx, entity, po.AllocatedPrice)
                            WriteCell(si, api, "A#REV_Recognized_PO" & poIdx, entity, recognizedAmount)
                            WriteCell(si, api, "A#REV_Deferred_PO" & poIdx, entity, deferredAmount)
                        Next

                        ' ============================================================
                        ' Step 5: Write contract-level revenue amounts
                        ' ============================================================
                        WriteCell(si, api, "A#REV_TransactionPrice", entity, transactionPrice)
                        WriteCell(si, api, "A#REV_VariableConsideration", entity, constrainedVC)
                        WriteCell(si, api, "A#REV_RecognizedRevenue", entity, contractRecognized)
                        WriteCell(si, api, "A#REV_DeferredRevenue", entity, contractDeferred)

                        totalRecognizedRevenue += contractRecognized
                        totalDeferredRevenue += contractDeferred

                        BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition: " & ct _
                            & " | TxnPrice=" & transactionPrice.ToString("N2") _
                            & " | Recognized=" & contractRecognized.ToString("N2") _
                            & " | Deferred=" & contractDeferred.ToString("N2"))
                    Next

                    '--- Write consolidated totals ---
                    WriteCell(si, api, "A#REV_TotalRecognized", "E#Total_Revenue", totalRecognizedRevenue)
                    WriteCell(si, api, "A#REV_TotalDeferred", "E#Total_Revenue", totalDeferredRevenue)

                    BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition: Completed. Recognized=" _
                        & totalRecognizedRevenue.ToString("N2") & " Deferred=" & totalDeferredRevenue.ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_RevenueRecognition", ex.Message, ex))
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
                BRApi.ErrorLog.LogMessage(si, "CR_RevenueRecognition.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace
