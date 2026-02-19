'------------------------------------------------------------------------------------------------------------
' DDA_CashFlow
' Dashboard DataAdapter Business Rule
' Purpose: Cash flow waterfall visualization with operating, investing, and financing components
'------------------------------------------------------------------------------------------------------------
Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.Linq
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_CashFlow
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("CashFlow")
                        dt.Columns.Add("Category", GetType(String))
                        dt.Columns.Add("Amount", GetType(Double))
                        dt.Columns.Add("RunningTotal", GetType(Double))
                        dt.Columns.Add("CashFlowType", GetType(String))
                        dt.Columns.Add("WaterfallSequence", GetType(Integer))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Fetch beginning cash balance
                        Dim beginningCash As Double = FetchAmount(si, scenarioName, timeName, entityName, "A#Cash_Beginning")

                        ' Define cash flow items in waterfall sequence
                        Dim cashFlowItems As New List(Of Tuple(Of String, String, String, Integer))()

                        ' Opening balance anchor
                        cashFlowItems.Add(Tuple.Create("Beginning Cash", "A#Cash_Beginning", "Anchor", 1))

                        ' Operating activities - detailed items
                        cashFlowItems.Add(Tuple.Create("Net Income", "A#CF_NetIncome", "Operating", 10))
                        cashFlowItems.Add(Tuple.Create("Depreciation & Amort", "A#CF_DepAmort", "Operating", 11))
                        cashFlowItems.Add(Tuple.Create("Stock-Based Comp", "A#CF_StockComp", "Operating", 12))
                        cashFlowItems.Add(Tuple.Create("Change in AR", "A#CF_ChangeAR", "Operating", 13))
                        cashFlowItems.Add(Tuple.Create("Change in Inventory", "A#CF_ChangeInventory", "Operating", 14))
                        cashFlowItems.Add(Tuple.Create("Change in AP", "A#CF_ChangeAP", "Operating", 15))
                        cashFlowItems.Add(Tuple.Create("Change in Accrued Liab", "A#CF_ChangeAccrued", "Operating", 16))
                        cashFlowItems.Add(Tuple.Create("Other Operating", "A#CF_OtherOperating", "Operating", 17))

                        ' Operating subtotal
                        cashFlowItems.Add(Tuple.Create("Cash from Operations", "A#CF_OperatingTotal", "Subtotal", 20))

                        ' Investing activities
                        cashFlowItems.Add(Tuple.Create("Capital Expenditures", "A#CF_CapEx", "Investing", 30))
                        cashFlowItems.Add(Tuple.Create("Acquisitions", "A#CF_Acquisitions", "Investing", 31))
                        cashFlowItems.Add(Tuple.Create("Asset Disposals", "A#CF_AssetDisposals", "Investing", 32))
                        cashFlowItems.Add(Tuple.Create("Other Investing", "A#CF_OtherInvesting", "Investing", 33))

                        ' Investing subtotal
                        cashFlowItems.Add(Tuple.Create("Cash from Investing", "A#CF_InvestingTotal", "Subtotal", 40))

                        ' Financing activities
                        cashFlowItems.Add(Tuple.Create("Debt Issuance", "A#CF_DebtIssuance", "Financing", 50))
                        cashFlowItems.Add(Tuple.Create("Debt Repayment", "A#CF_DebtRepayment", "Financing", 51))
                        cashFlowItems.Add(Tuple.Create("Dividends Paid", "A#CF_Dividends", "Financing", 52))
                        cashFlowItems.Add(Tuple.Create("Share Repurchase", "A#CF_ShareRepurchase", "Financing", 53))
                        cashFlowItems.Add(Tuple.Create("Other Financing", "A#CF_OtherFinancing", "Financing", 54))

                        ' Financing subtotal
                        cashFlowItems.Add(Tuple.Create("Cash from Financing", "A#CF_FinancingTotal", "Subtotal", 60))

                        ' FX impact and ending balance
                        cashFlowItems.Add(Tuple.Create("FX Impact on Cash", "A#CF_FXImpact", "Other", 70))
                        cashFlowItems.Add(Tuple.Create("Ending Cash", "A#Cash_Ending", "Anchor", 99))

                        ' Build the waterfall data with running totals
                        Dim runningTotal As Double = 0

                        For Each item In cashFlowItems
                            Dim category As String = item.Item1
                            Dim account As String = item.Item2
                            Dim cfType As String = item.Item3
                            Dim seq As Integer = item.Item4

                            Dim amount As Double = FetchAmount(si, scenarioName, timeName, entityName, account)

                            ' Calculate running total based on item type
                            If cfType = "Anchor" AndAlso seq = 1 Then
                                ' Beginning cash sets the starting point
                                runningTotal = amount
                            ElseIf cfType = "Anchor" Then
                                ' Ending cash is the final anchor (use the calculated running total)
                                runningTotal = runningTotal
                                amount = runningTotal
                            ElseIf cfType = "Subtotal" Then
                                ' Subtotals don't change the running total; they represent a subtotal display
                                amount = FetchAmount(si, scenarioName, timeName, entityName, account)
                            Else
                                ' Regular items increment the running total
                                runningTotal += amount
                            End If

                            Dim row As DataRow = dt.NewRow()
                            row("Category") = category
                            row("Amount") = Math.Round(amount, 2)
                            row("RunningTotal") = Math.Round(runningTotal, 2)
                            row("CashFlowType") = cfType
                            row("WaterfallSequence") = seq
                            dt.Rows.Add(row)
                        Next

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function FetchAmount(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on failure
            End Try
            Return 0
        End Function

    End Class
End Namespace
