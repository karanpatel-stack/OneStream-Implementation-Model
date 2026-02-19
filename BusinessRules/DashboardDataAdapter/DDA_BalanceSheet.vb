'------------------------------------------------------------------------------------------------------------
' DDA_BalanceSheet
' Dashboard DataAdapter Business Rule
' Purpose: Balance sheet dashboard with drill-down hierarchy, prior period comparison, and ratio analysis
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_BalanceSheet
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("BalanceSheet")
                        dt.Columns.Add("AccountCategory", GetType(String))
                        dt.Columns.Add("CurrentPeriod", GetType(Double))
                        dt.Columns.Add("PriorPeriod", GetType(Double))
                        dt.Columns.Add("Change", GetType(Double))
                        dt.Columns.Add("PctChange", GetType(Double))
                        dt.Columns.Add("Level", GetType(Integer))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Derive prior period time member
                        Dim priorTimeName As String = GetPriorPeriodTime(timeName)

                        ' Define balance sheet hierarchy with levels for drill-down
                        ' Level 0 = major category, Level 1 = subcategory, Level 2 = detail
                        Dim bsItems As New List(Of Tuple(Of String, String, Integer))()

                        ' Assets
                        bsItems.Add(Tuple.Create("Total Assets", "A#TotalAssets", 0))
                        bsItems.Add(Tuple.Create("  Current Assets", "A#CurrentAssets", 1))
                        bsItems.Add(Tuple.Create("    Cash & Equivalents", "A#CashAndEquivalents", 2))
                        bsItems.Add(Tuple.Create("    Accounts Receivable", "A#AccountsReceivable", 2))
                        bsItems.Add(Tuple.Create("    Inventory", "A#Inventory", 2))
                        bsItems.Add(Tuple.Create("    Prepaid Expenses", "A#PrepaidExpenses", 2))
                        bsItems.Add(Tuple.Create("    Other Current Assets", "A#OtherCurrentAssets", 2))
                        bsItems.Add(Tuple.Create("  Non-Current Assets", "A#NonCurrentAssets", 1))
                        bsItems.Add(Tuple.Create("    Property Plant & Equip", "A#PPE_Net", 2))
                        bsItems.Add(Tuple.Create("    Goodwill", "A#Goodwill", 2))
                        bsItems.Add(Tuple.Create("    Intangible Assets", "A#IntangibleAssets", 2))
                        bsItems.Add(Tuple.Create("    Other Non-Current", "A#OtherNonCurrentAssets", 2))

                        ' Liabilities
                        bsItems.Add(Tuple.Create("Total Liabilities", "A#TotalLiabilities", 0))
                        bsItems.Add(Tuple.Create("  Current Liabilities", "A#CurrentLiabilities", 1))
                        bsItems.Add(Tuple.Create("    Accounts Payable", "A#AccountsPayable", 2))
                        bsItems.Add(Tuple.Create("    Accrued Liabilities", "A#AccruedLiabilities", 2))
                        bsItems.Add(Tuple.Create("    Current Debt", "A#CurrentDebt", 2))
                        bsItems.Add(Tuple.Create("    Other Current Liab", "A#OtherCurrentLiabilities", 2))
                        bsItems.Add(Tuple.Create("  Long-Term Liabilities", "A#LongTermLiabilities", 1))
                        bsItems.Add(Tuple.Create("    Long-Term Debt", "A#LongTermDebt", 2))
                        bsItems.Add(Tuple.Create("    Pension Obligations", "A#PensionObligations", 2))
                        bsItems.Add(Tuple.Create("    Other LT Liabilities", "A#OtherLTLiabilities", 2))

                        ' Equity
                        bsItems.Add(Tuple.Create("Total Equity", "A#TotalEquity", 0))
                        bsItems.Add(Tuple.Create("  Common Stock", "A#CommonStock", 1))
                        bsItems.Add(Tuple.Create("  Additional Paid-In Capital", "A#APIC", 1))
                        bsItems.Add(Tuple.Create("  Retained Earnings", "A#RetainedEarnings", 1))
                        bsItems.Add(Tuple.Create("  Treasury Stock", "A#TreasuryStock", 1))
                        bsItems.Add(Tuple.Create("  Other Comprehensive Income", "A#OCI", 1))

                        ' Total L+E check
                        bsItems.Add(Tuple.Create("Total Liabilities & Equity", "A#TotalLiabilitiesEquity", 0))

                        For Each bsItem In bsItems
                            Dim category As String = bsItem.Item1
                            Dim account As String = bsItem.Item2
                            Dim level As Integer = bsItem.Item3

                            ' Fetch current and prior period balances
                            Dim currentAmount As Double = FetchBalance(si, scenarioName, timeName, entityName, account)
                            Dim priorAmount As Double = FetchBalance(si, scenarioName, priorTimeName, entityName, account)

                            ' Calculate period-over-period change
                            Dim change As Double = currentAmount - priorAmount
                            Dim pctChange As Double = 0
                            If priorAmount <> 0 Then
                                pctChange = change / Math.Abs(priorAmount)
                            End If

                            Dim row As DataRow = dt.NewRow()
                            row("AccountCategory") = category
                            row("CurrentPeriod") = Math.Round(currentAmount, 2)
                            row("PriorPeriod") = Math.Round(priorAmount, 2)
                            row("Change") = Math.Round(change, 2)
                            row("PctChange") = Math.Round(pctChange, 4)
                            row("Level") = level
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

        Private Function FetchBalance(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                ' Balance sheet uses EndBal flow for point-in-time balances
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

        Private Function GetPriorPeriodTime(ByVal timeName As String) As String
            ' Decrement the month by one period (e.g., "2024M6" -> "2024M5", "2024M1" -> "2023M12")
            Try
                Dim yearStr As String = timeName.Substring(0, 4)
                Dim monthStr As String = timeName.Substring(5)
                Dim year As Integer = Integer.Parse(yearStr)
                Dim month As Integer = Integer.Parse(monthStr)

                month -= 1
                If month <= 0 Then
                    month = 12
                    year -= 1
                End If

                Return year.ToString() & "M" & month.ToString()
            Catch
                Return timeName
            End Try
        End Function

    End Class
End Namespace
