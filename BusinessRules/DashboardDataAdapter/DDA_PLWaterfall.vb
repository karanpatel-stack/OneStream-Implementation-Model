'------------------------------------------------------------------------------------------------------------
' DDA_PLWaterfall
' Dashboard DataAdapter Business Rule
' Purpose: P&L bridge/waterfall with variance breakdown by volume, price, mix, FX, and cost effects
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_PLWaterfall
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("PLWaterfall")
                        dt.Columns.Add("Category", GetType(String))
                        dt.Columns.Add("ActualAmount", GetType(Double))
                        dt.Columns.Add("BudgetAmount", GetType(Double))
                        dt.Columns.Add("Variance", GetType(Double))
                        dt.Columns.Add("BridgeEffect", GetType(String))
                        dt.Columns.Add("WaterfallSequence", GetType(Integer))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Define P&L line items in income statement order
                        Dim plLineItems As New List(Of Tuple(Of String, String, Integer))()
                        plLineItems.Add(Tuple.Create("Revenue", "A#Revenue", 1))
                        plLineItems.Add(Tuple.Create("Cost of Goods Sold", "A#COGS", 2))
                        plLineItems.Add(Tuple.Create("Gross Profit", "A#GrossProfit", 3))
                        plLineItems.Add(Tuple.Create("Sales & Marketing", "A#SalesMarketing", 4))
                        plLineItems.Add(Tuple.Create("General & Admin", "A#GeneralAdmin", 5))
                        plLineItems.Add(Tuple.Create("Research & Development", "A#RandD", 6))
                        plLineItems.Add(Tuple.Create("Other OPEX", "A#OtherOpex", 7))
                        plLineItems.Add(Tuple.Create("EBITDA", "A#EBITDA", 8))
                        plLineItems.Add(Tuple.Create("Depreciation & Amort", "A#DepreciationAmort", 9))
                        plLineItems.Add(Tuple.Create("EBIT", "A#EBIT", 10))
                        plLineItems.Add(Tuple.Create("Interest Expense", "A#InterestExpense", 11))
                        plLineItems.Add(Tuple.Create("Tax Provision", "A#TaxProvision", 12))
                        plLineItems.Add(Tuple.Create("Net Income", "A#NetIncome", 13))

                        ' Fetch actual and budget amounts for each P&L line item
                        For Each lineItem In plLineItems
                            Dim category As String = lineItem.Item1
                            Dim account As String = lineItem.Item2
                            Dim seq As Integer = lineItem.Item3

                            Dim actualAmt As Double = FetchAmount(si, scenarioName, timeName, entityName, account)
                            Dim budgetAmt As Double = FetchAmount(si, budgetScenario, timeName, entityName, account)
                            Dim variance As Double = actualAmt - budgetAmt

                            Dim row As DataRow = dt.NewRow()
                            row("Category") = category
                            row("ActualAmount") = Math.Round(actualAmt, 2)
                            row("BudgetAmount") = Math.Round(budgetAmt, 2)
                            row("Variance") = Math.Round(variance, 2)
                            row("BridgeEffect") = "LineItem"
                            row("WaterfallSequence") = seq
                            dt.Rows.Add(row)
                        Next

                        ' Build the revenue bridge effects: Volume, Price, Mix, FX
                        Dim bridgeSequence As Integer = 100
                        Dim bridgeEffects As New List(Of Tuple(Of String, String))()
                        bridgeEffects.Add(Tuple.Create("Volume Effect", "A#Bridge_Volume"))
                        bridgeEffects.Add(Tuple.Create("Price Effect", "A#Bridge_Price"))
                        bridgeEffects.Add(Tuple.Create("Mix Effect", "A#Bridge_Mix"))
                        bridgeEffects.Add(Tuple.Create("FX Effect", "A#Bridge_FX"))
                        bridgeEffects.Add(Tuple.Create("Cost Effect", "A#Bridge_Cost"))

                        For Each bridge In bridgeEffects
                            Dim effectName As String = bridge.Item1
                            Dim effectAccount As String = bridge.Item2
                            bridgeSequence += 1

                            Dim effectValue As Double = FetchAmount(si, scenarioName, timeName, entityName, effectAccount)

                            Dim bridgeRow As DataRow = dt.NewRow()
                            bridgeRow("Category") = effectName
                            bridgeRow("ActualAmount") = Math.Round(effectValue, 2)
                            bridgeRow("BudgetAmount") = 0
                            bridgeRow("Variance") = Math.Round(effectValue, 2)
                            bridgeRow("BridgeEffect") = "Bridge"
                            bridgeRow("WaterfallSequence") = bridgeSequence
                            dt.Rows.Add(bridgeRow)
                        Next

                        ' Add budget starting point and actual endpoint for the waterfall visualization
                        Dim budgetNetIncome As Double = FetchAmount(si, budgetScenario, timeName, entityName, "A#NetIncome")
                        Dim actualNetIncome As Double = FetchAmount(si, scenarioName, timeName, entityName, "A#NetIncome")

                        Dim startRow As DataRow = dt.NewRow()
                        startRow("Category") = "Budget Net Income"
                        startRow("ActualAmount") = Math.Round(budgetNetIncome, 2)
                        startRow("BudgetAmount") = Math.Round(budgetNetIncome, 2)
                        startRow("Variance") = 0
                        startRow("BridgeEffect") = "Anchor"
                        startRow("WaterfallSequence") = 200
                        dt.Rows.Add(startRow)

                        Dim endRow As DataRow = dt.NewRow()
                        endRow("Category") = "Actual Net Income"
                        endRow("ActualAmount") = Math.Round(actualNetIncome, 2)
                        endRow("BudgetAmount") = Math.Round(budgetNetIncome, 2)
                        endRow("Variance") = Math.Round(actualNetIncome - budgetNetIncome, 2)
                        endRow("BridgeEffect") = "Anchor"
                        endRow("WaterfallSequence") = 210
                        dt.Rows.Add(endRow)

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
