'------------------------------------------------------------------------------------------------------------
' OneStream XF Validation Script: VAL_TrialBalanceCheck
'------------------------------------------------------------------------------------------------------------
' Purpose:     Validates that the trial balance is in balance for each entity and time period.
'              Sums all debit accounts (Assets, Expenses) and all credit accounts (Liabilities,
'              Equity, Revenue) and confirms total debits equal total credits within tolerance.
'
' Tolerance:   $0.01 (absolute difference allowed between debit and credit totals)
'
' Debit Accounts:  TotalAssets (Asset type), TotalCOGS (Expense), TotalOPEX (Expense),
'                  DepAmort (Expense), InterestExpense (Expense), IncomeTax (Expense)
' Credit Accounts: TotalLiabilities (Liability), TotalEquity (Equity), TotalRevenue (Revenue),
'                  OtherIncExp (Revenue type)
'
' Output:      DataTable with columns: Entity, Period, TotalDebits, TotalCredits, Difference,
'              Status (Pass/Fail)
'
' Author:      OneStream Administrator
' Created:     2026-02-18
' Modified:    2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Math
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.VAL_TrialBalanceCheck

    Public Class MainClass

        ' Tolerance for trial balance check -- $0.01
        Private Const TB_TOLERANCE As Double = 0.01

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for the validation rule, called as a Dashboard Data Adapter.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As DashboardDataAdapterArgs) As Object
            Try
                ' Build the results DataTable
                Dim resultsTable As New DataTable("TrialBalanceValidation")
                resultsTable.Columns.Add("Entity", GetType(String))
                resultsTable.Columns.Add("Period", GetType(String))
                resultsTable.Columns.Add("TotalDebits", GetType(Double))
                resultsTable.Columns.Add("TotalCredits", GetType(Double))
                resultsTable.Columns.Add("Difference", GetType(Double))
                resultsTable.Columns.Add("Status", GetType(String))

                ' Define the list of leaf entities to validate
                Dim leafEntities As String() = { _
                    "Plant_US01_Detroit", "Plant_US02_Houston", "Plant_US03_Charlotte", _
                    "Plant_CA01_Toronto", "Plant_MX01_Monterrey", _
                    "Plant_DE01_Munich", "Plant_DE02_Stuttgart", _
                    "Plant_UK01_Birmingham", "Plant_FR01_Lyon", _
                    "Plant_CN01_Shanghai", "Plant_CN02_Shenzhen", _
                    "Plant_JP01_Osaka", "Plant_IN01_Pune", _
                    "SS_IT", "SS_HR", "SS_Finance" _
                }

                ' Define periods to validate (January 2025 through December 2025)
                Dim periods As String() = { _
                    "2025M1", "2025M2", "2025M3", "2025M4", "2025M5", "2025M6", _
                    "2025M7", "2025M8", "2025M9", "2025M10", "2025M11", "2025M12" _
                }

                ' Debit-normal accounts (Assets + Expenses)
                Dim debitAccounts As String() = { _
                    "TotalAssets", "TotalCOGS", "TotalOPEX", "DepAmort", "InterestExpense", "IncomeTax" _
                }

                ' Credit-normal accounts (Liabilities + Equity + Revenue)
                Dim creditAccounts As String() = { _
                    "TotalLiabilities", "TotalEquity", "TotalRevenue", "OtherIncExp" _
                }

                BRApi.ErrorLog.LogMessage(si, "VAL_TrialBalanceCheck: Starting trial balance validation...")

                ' Iterate through each entity and period combination
                For Each entityName As String In leafEntities
                    For Each period As String In periods
                        Dim totalDebits As Double = 0
                        Dim totalCredits As Double = 0

                        ' Sum all debit-normal account balances
                        For Each acctName As String In debitAccounts
                            Dim pov As String = $"E#{entityName}:A#{acctName}:C#C_Local:T#{period}:S#Actual:F#F_Closing:O#O_None"
                            Dim cellValue As Double = BRApi.Finance.Data.GetDataCell(si, pov).CellAmount
                            totalDebits += Math.Abs(cellValue)
                        Next

                        ' Sum all credit-normal account balances
                        For Each acctName As String In creditAccounts
                            Dim pov As String = $"E#{entityName}:A#{acctName}:C#C_Local:T#{period}:S#Actual:F#F_Closing:O#O_None"
                            Dim cellValue As Double = BRApi.Finance.Data.GetDataCell(si, pov).CellAmount
                            totalCredits += Math.Abs(cellValue)
                        Next

                        ' Calculate the difference and determine pass/fail
                        Dim difference As Double = Math.Abs(totalDebits - totalCredits)
                        Dim status As String = If(difference <= TB_TOLERANCE, "PASS", "FAIL")

                        ' Add the result row
                        Dim row As DataRow = resultsTable.NewRow()
                        row("Entity") = entityName
                        row("Period") = period
                        row("TotalDebits") = Math.Round(totalDebits, 2)
                        row("TotalCredits") = Math.Round(totalCredits, 2)
                        row("Difference") = Math.Round(difference, 2)
                        row("Status") = status
                        resultsTable.Rows.Add(row)

                        ' Log failures
                        If status = "FAIL" Then
                            BRApi.ErrorLog.LogMessage(si, _
                                $"  FAIL: [{entityName}] [{period}] Debits={totalDebits:N2} Credits={totalCredits:N2} Diff={difference:N2}")
                        End If
                    Next
                Next

                ' Summary statistics
                Dim totalChecks As Integer = resultsTable.Rows.Count
                Dim failCount As Integer = resultsTable.Select("Status = 'FAIL'").Length
                Dim passCount As Integer = totalChecks - failCount

                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_TrialBalanceCheck: Complete. Total={totalChecks}, Pass={passCount}, Fail={failCount}")

                Return resultsTable

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "VAL_TrialBalanceCheck.Main", ex.Message))
            End Try
        End Function

    End Class

End Namespace
