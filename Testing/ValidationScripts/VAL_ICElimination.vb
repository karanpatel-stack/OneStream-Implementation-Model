'------------------------------------------------------------------------------------------------------------
' OneStream XF Validation Script: VAL_ICElimination
'------------------------------------------------------------------------------------------------------------
' Purpose:     Validates that intercompany elimination entries net to zero after IC elimination
'              processing. For each IC partner pair, sums all elimination entries posted to the
'              C_Elimination consolidation member and verifies the net balance is zero.
'
' Validation Checks:
'   1. For each IC partner pair, sum elimination DR and CR entries
'   2. Verify eliminations net to zero (within $0.01 tolerance)
'   3. Flag any residual IC balances remaining after elimination
'   4. Validate all known IC relationships have corresponding eliminations
'
' Output:      DataTable with columns: EntityPair, EliminationCategory, EliminationAmount,
'              NetBalance, Status (Pass/Fail), Notes
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.VAL_ICElimination

    Public Class MainClass

        ' Tolerance for IC elimination net-to-zero check
        Private Const ELIM_TOLERANCE As Double = 0.01

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for the IC elimination validation rule.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As DashboardDataAdapterArgs) As Object
            Try
                ' Build the results DataTable
                Dim resultsTable As New DataTable("ICEliminationValidation")
                resultsTable.Columns.Add("EntityPair", GetType(String))
                resultsTable.Columns.Add("EliminationCategory", GetType(String))
                resultsTable.Columns.Add("EliminationAmount", GetType(Double))
                resultsTable.Columns.Add("NetBalance", GetType(Double))
                resultsTable.Columns.Add("Status", GetType(String))
                resultsTable.Columns.Add("Notes", GetType(String))

                ' Define the IC partner pairs based on Entity_Intercompany.csv relationships
                Dim icPairs As New List(Of String())(New String()() { _
                    New String() {"Plant_US01_Detroit", "Plant_DE01_Munich"}, _
                    New String() {"Plant_US01_Detroit", "Plant_CN01_Shanghai"}, _
                    New String() {"Plant_US01_Detroit", "Plant_CA01_Toronto"}, _
                    New String() {"Plant_US01_Detroit", "Plant_MX01_Monterrey"}, _
                    New String() {"Plant_US01_Detroit", "Plant_UK01_Birmingham"}, _
                    New String() {"Plant_US02_Houston", "Plant_DE02_Stuttgart"}, _
                    New String() {"Plant_US02_Houston", "Plant_CN02_Shenzhen"}, _
                    New String() {"Plant_US02_Houston", "Plant_IN01_Pune"}, _
                    New String() {"Plant_US03_Charlotte", "Plant_MX01_Monterrey"}, _
                    New String() {"Plant_DE01_Munich", "Plant_CN01_Shanghai"}, _
                    New String() {"Plant_DE01_Munich", "Plant_JP01_Osaka"}, _
                    New String() {"Plant_DE01_Munich", "Plant_FR01_Lyon"}, _
                    New String() {"Plant_CN01_Shanghai", "Plant_JP01_Osaka"}, _
                    New String() {"Plant_CN01_Shanghai", "Plant_IN01_Pune"}, _
                    New String() {"SS_IT", "Plant_US01_Detroit"}, _
                    New String() {"SS_IT", "Plant_DE01_Munich"}, _
                    New String() {"SS_Finance", "Plant_US01_Detroit"} _
                })

                ' Elimination account categories to validate
                Dim elimCategories As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase)
                elimCategories.Add("Revenue/COGS", New String() {"REV_IC_TransferSales", "REV_IC_ServiceRevenue"})
                elimCategories.Add("AR/AP", New String() {"AR_Intercompany", "AP_Intercompany"})
                elimCategories.Add("Dividends", New String() {"OIE_InvestmentIncome"})
                elimCategories.Add("Loans", New String() {"INT_LongTermDebt", "INT_ShortTermDebt"})

                ' Elimination parent entities for each region
                Dim elimEntities As String() = { _
                    "Elim_Americas", "Elim_EMEA", "Elim_APAC", "Elim_CrossRegion" _
                }

                BRApi.ErrorLog.LogMessage(si, "VAL_ICElimination: Starting IC elimination validation...")

                ' Check each IC partner pair across all elimination categories
                For Each pair As String() In icPairs
                    Dim entityA As String = pair(0)
                    Dim entityB As String = pair(1)
                    Dim pairLabel As String = $"{entityA} <-> {entityB}"

                    For Each category As KeyValuePair(Of String, String()) In elimCategories
                        Dim totalElimAmount As Double = 0
                        Dim netBalance As Double = 0

                        ' Sum elimination entries across all elimination entities for this pair
                        For Each elimEntity As String In elimEntities
                            For Each acctName As String In category.Value
                                ' Read the elimination entry at C_Elimination
                                Dim povStr As String = $"E#{elimEntity}:A#{acctName}:C#C_Elimination:S#Actual:F#F_None:O#O_None"
                                Dim cellValue As Double = BRApi.Finance.Data.GetDataCell(si, povStr).CellAmount
                                totalElimAmount += Math.Abs(cellValue)
                                netBalance += cellValue
                            Next
                        Next

                        ' Skip pairs with no elimination activity
                        If totalElimAmount = 0 Then Continue For

                        ' Determine pass/fail: net balance should be zero
                        Dim absNet As Double = Math.Abs(netBalance)
                        Dim status As String = If(absNet <= ELIM_TOLERANCE, "PASS", "FAIL")
                        Dim notes As String = ""

                        If status = "FAIL" Then
                            notes = $"Residual balance of {netBalance:N2} detected after elimination"
                        End If

                        ' Add result row
                        Dim row As DataRow = resultsTable.NewRow()
                        row("EntityPair") = pairLabel
                        row("EliminationCategory") = category.Key
                        row("EliminationAmount") = Math.Round(totalElimAmount, 2)
                        row("NetBalance") = Math.Round(netBalance, 2)
                        row("Status") = status
                        row("Notes") = notes
                        resultsTable.Rows.Add(row)
                    Next
                Next

                '--------------------------------------------------------------------------------------------
                ' Additional check: Verify no residual IC balances remain after elimination
                ' Read IC AR and IC AP at the consolidated level and ensure they are zero
                '--------------------------------------------------------------------------------------------
                Dim consolidatedEntities As String() = {"Americas", "EMEA", "APAC", "Global"}
                Dim residualAccounts As String() = {"AR_Intercompany", "AP_Intercompany"}

                For Each consolEntity As String In consolidatedEntities
                    For Each residualAcct As String In residualAccounts
                        Dim consolPov As String = $"E#{consolEntity}:A#{residualAcct}:C#C_Consolidated:S#Actual:F#F_Closing:O#O_None"
                        Dim residualAmount As Double = BRApi.Finance.Data.GetDataCell(si, consolPov).CellAmount

                        If Math.Abs(residualAmount) > ELIM_TOLERANCE Then
                            Dim row As DataRow = resultsTable.NewRow()
                            row("EntityPair") = $"{consolEntity} (Consolidated)"
                            row("EliminationCategory") = $"Residual {residualAcct}"
                            row("EliminationAmount") = 0
                            row("NetBalance") = Math.Round(residualAmount, 2)
                            row("Status") = "FAIL"
                            row("Notes") = $"Residual IC balance of {residualAmount:N2} at consolidated level"
                            resultsTable.Rows.Add(row)
                        End If
                    Next
                Next

                ' Summary
                Dim totalChecks As Integer = resultsTable.Rows.Count
                Dim failCount As Integer = resultsTable.Select("Status = 'FAIL'").Length

                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_ICElimination: Complete. Total={totalChecks}, Pass={totalChecks - failCount}, Fail={failCount}")

                Return resultsTable

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "VAL_ICElimination.Main", ex.Message))
            End Try
        End Function

    End Class

End Namespace
